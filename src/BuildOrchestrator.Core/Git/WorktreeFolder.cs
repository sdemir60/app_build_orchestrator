namespace BuildOrchestrator.Core.Git;

/// <summary>[worktree] Ayardaki worktree klasörünün durumu — <see cref="WorktreeFolder.Inspect"/>'in cevabı.</summary>
public enum WorktreeFolderState
{
    /// <summary>Klasör yok ya da boş: Save onu <c>git worktree add --detach</c> ile oluşturabilir.</summary>
    Creatable,
    /// <summary>Bu reponun linked worktree'si: geçilebilir.</summary>
    Linked,
    /// <summary>Var ama bu reponun worktree'si değil (dolu klasör, ayrı bir klon, başka reponun worktree'si): dokunulmaz.</summary>
    Foreign,
    /// <summary>Repo kökünün kendisi, içinde ya da onu kapsıyor: keşif aynı projeleri iki kez bulurdu.</summary>
    Nested,
    /// <summary>Tam yol değil (göreli, sürücüye göreli ya da köke göreli).</summary>
    NotRooted,
    /// <summary>Repo kökü bir git çalışma kopyası değil: worktree kurulamaz.</summary>
    NoRepository,
    /// <summary>Klasör okunamadı (erişim reddi, G/Ç hatası).</summary>
    Unreadable,
}

/// <summary>
/// [worktree] Ayardaki worktree klasörünün denetimi — Settings'in Save kapısı, alt bardaki anahtar ve motorun oluşturma kapısı
/// AYNI soruyu buradan sorar (kopya YASAK).
///
/// <para><b>Saf dosya okuması, process yok:</b> linked bir worktree'nin kökünde <c>.git</c> bir DOSYADIR (<c>gitdir: …</c>);
/// gösterdiği git dizininin ortak dizini (<see cref="GitDirectory.ResolveCommon"/>) ana reponun ortak dizinine eşitse klasör
/// bu reponundur. Ref biçimine (loose, packed, reftable) bakılmaz: bu işaretler her biçimde aynıdır.</para>
///
/// <para><b>Sıra:</b> önce yolun kendisi (tam yol mu, repo köküyle iç içe mi) — disk okunmadan; sonra repo kökünün git dizini;
/// en son klasör. Repo kökünün içindeki bir worktree'yi ana kopyanın keşfi de bulurdu; iç içe olma bu yüzden klasör
/// yokken bile reddedilir. Yan yana duran ve adı repo adıyla başlayan klasör (<c>acme</c> → <c>acme-worktree</c>) iç içe
/// değildir: karşılaştırma yol bileşeni üzerindendir.</para>
/// </summary>
public static class WorktreeFolder
{
    public static WorktreeFolderState Inspect(string repositoryRoot, string folder)
    {
        if (!Path.IsPathFullyQualified(folder) || !Path.IsPathFullyQualified(repositoryRoot))
            return WorktreeFolderState.NotRooted;

        try
        {
            string repo = Normalize(repositoryRoot);
            string candidate = Normalize(folder);
            if (IsSameOrInside(candidate, repo) || IsSameOrInside(repo, candidate)) return WorktreeFolderState.Nested;

            if (GitDirectory.Resolve(repo) is not { } repoGitDir) return WorktreeFolderState.NoRepository;

            if (File.Exists(candidate)) return WorktreeFolderState.Foreign;
            if (!Directory.Exists(candidate) || !Directory.EnumerateFileSystemEntries(candidate).Any())
                return WorktreeFolderState.Creatable;

            // Linked worktree'nin işareti bir DOSYADIR; klasör olan .git ayrı bir klondur.
            if (!File.Exists(Path.Combine(candidate, ".git")) || GitDirectory.Resolve(candidate) is not { } candidateGitDir)
                return WorktreeFolderState.Foreign;

            return string.Equals(Normalize(GitDirectory.ResolveCommon(candidateGitDir)), Normalize(GitDirectory.ResolveCommon(repoGitDir)),
                    StringComparison.OrdinalIgnoreCase)
                ? WorktreeFolderState.Linked
                : WorktreeFolderState.Foreign;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return WorktreeFolderState.Unreadable;
        }
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary><paramref name="path"/>, <paramref name="ancestor"/>'ın kendisi ya da altındaki bir yol mu — metin öneki değil,
    /// yol bileşeni üzerinden (Windows yolları büyük/küçük harf duyarsız). Sürücü kökü (<c>D:\</c>) zaten ayraçla biter.</summary>
    private static bool IsSameOrInside(string path, string ancestor)
    {
        if (string.Equals(path, ancestor, StringComparison.OrdinalIgnoreCase)) return true;
        string prefix = Path.EndsInDirectorySeparator(ancestor) ? ancestor : ancestor + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
