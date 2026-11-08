namespace BuildOrchestrator.Core.Git;

/// <summary>
/// [Faz 2/T1] Bir çalışma kopyası kökünden git dizinini (gitdir) çözer — saf dosya okuması, process YOK
/// (<see cref="Externals.VcsDetector"/> ile aynı işaretin altına iner ama gitdir'in TAM yolunu döner).
/// <c>&lt;root&gt;\.git</c> bir KLASÖRSE o klasördür; bir DOSYAYSA içeriği <c>gitdir: &lt;yol&gt;</c>
/// satırıdır (yol köke göre göreli ya da mutlak olabilir — linked worktree ve <c>git submodule</c> bu deseni
/// kullanır). Ne klasör ne dosya varsa ya da dosya içeriği tanınmıyorsa <c>null</c> döner.
/// </summary>
public static class GitDirectory
{
    private const string GitMarker = ".git";
    private const string GitDirPrefix = "gitdir:";

    public static string? Resolve(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;

        string marker = Path.Combine(root, GitMarker);
        if (Directory.Exists(marker)) return marker;
        if (!File.Exists(marker)) return null;

        string content = File.ReadAllText(marker).TrimEnd('\r', '\n');
        if (!content.StartsWith(GitDirPrefix, StringComparison.Ordinal)) return null; // bozuk gitfile

        string path = content[GitDirPrefix.Length..].Trim();
        if (path.Length == 0) return null;

        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
    }

    /// <summary>Bir git dizininin ORTAK dizini: linked worktree'de <c>commondir</c> dosyası ana reponun <c>.git</c>'ine göreli
    /// ya da mutlak yolu taşır; dosya yoksa (normal repo) ortak dizin <paramref name="gitDir"/>'in kendisidir. Ref'ler
    /// (<see cref="HeadReader"/>) ve bir klasörün hangi repoya ait olduğu (<see cref="WorktreeFolder.Inspect"/>) buradan
    /// okunur — tek tanım (kopya YASAK).</summary>
    public static string ResolveCommon(string gitDir)
    {
        string commonDirFile = Path.Combine(gitDir, "commondir");
        if (!File.Exists(commonDirFile)) return gitDir;

        string raw = File.ReadAllText(commonDirFile).Trim();
        if (raw.Length == 0) return gitDir;

        return Path.GetFullPath(Path.IsPathRooted(raw) ? raw : Path.Combine(gitDir, raw));
    }
}
