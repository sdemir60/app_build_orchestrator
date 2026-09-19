namespace BuildOrchestrator.Core.Git;

/// <summary>
/// [Faz 2/T1] <see cref="HeadReader.Read"/> sonucu: normal bir repoda ikisi de dolu; detached HEAD'de
/// <see cref="Branch"/> null; unborn HEAD'de (henüz commit yok) <see cref="Sha"/> null.
/// </summary>
public sealed record HeadState(string? Branch, string? Sha);

/// <summary>
/// [Faz 2/T1] <c>HEAD</c> dosyasını (ve gerektiğinde <c>refs/heads/*</c> ya da <c>packed-refs</c>'i) saf
/// dosya okumasıyla çözer — process YOK. Linked worktree'de <c>HEAD</c> worktree'nin KENDİ gitdir'inden
/// okunur ama ref'ler (<c>refs/heads/*</c>, <c>packed-refs</c>) ORTAK dizinden (<c>commondir</c> dosyasının
/// gösterdiği yoldan) okunur — worktree'ler kendi <c>refs/heads</c>'ini taşımaz.
/// </summary>
public static class HeadReader
{
    private const string RefPrefix = "ref: ";
    private const string BranchRefPrefix = "refs/heads/";

    public static HeadState? Read(string gitDir)
    {
        if (string.IsNullOrWhiteSpace(gitDir)) return null;

        string headPath = Path.Combine(gitDir, "HEAD");
        if (!File.Exists(headPath)) return null;

        string content = File.ReadAllText(headPath).Trim();
        if (content.Length == 0) return null;

        if (!content.StartsWith(RefPrefix, StringComparison.Ordinal))
            return new HeadState(Branch: null, Sha: content); // detached — içerik doğrudan sha

        string refName = content[RefPrefix.Length..].Trim();
        string? branch = refName.StartsWith(BranchRefPrefix, StringComparison.Ordinal)
            ? refName[BranchRefPrefix.Length..]
            : refName; // refs/heads/ dışı bir ref (savunmacı — normalde görülmez)

        string commonDir = ResolveCommonDir(gitDir);
        string? sha = ReadLooseRef(commonDir, refName) ?? ReadPackedRef(commonDir, refName);
        return new HeadState(branch, sha);
    }

    /// <summary>Linked worktree'de <c>commondir</c> dosyası ortak <c>.git</c>'e göreli/mutlak yolu taşır;
    /// dosya yoksa (normal repo) ref'ler zaten <paramref name="gitDir"/>'in kendisindedir.</summary>
    private static string ResolveCommonDir(string gitDir)
    {
        string commonDirFile = Path.Combine(gitDir, "commondir");
        if (!File.Exists(commonDirFile)) return gitDir;

        string raw = File.ReadAllText(commonDirFile).Trim();
        if (raw.Length == 0) return gitDir;

        return Path.GetFullPath(Path.IsPathRooted(raw) ? raw : Path.Combine(gitDir, raw));
    }

    private static string? ReadLooseRef(string commonDir, string refName)
    {
        string path = Path.Combine(commonDir, refName.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return null;

        string sha = File.ReadAllText(path).Trim();
        return sha.Length == 0 ? null : sha;
    }

    /// <summary><c>packed-refs</c> satırları <c>&lt;sha&gt; &lt;refname&gt;</c> biçimindedir; yorum
    /// (<c>#</c>) ve peeled-tag (<c>^</c>) satırları atlanır.</summary>
    private static string? ReadPackedRef(string commonDir, string refName)
    {
        string path = Path.Combine(commonDir, "packed-refs");
        if (!File.Exists(path)) return null;

        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] is '#' or '^') continue;

            int space = line.IndexOf(' ');
            if (space < 0) continue;

            if (string.Equals(line[(space + 1)..].Trim(), refName, StringComparison.Ordinal))
                return line[..space];
        }
        return null;
    }
}
