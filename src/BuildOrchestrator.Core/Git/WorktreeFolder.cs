using BuildOrchestrator.Core.Paths;

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

        // Kök karşılaştırmasının TEK cevabı RootScope'tur: iki yol da sondaki ayraçla normalize edilir, eşitlik de "ile başlar"a düşer ve
        // önek tuzağı (acme → acme-worktree) bileşen sınırında kalır.
        if (RootScope.NormalizeRoot(repositoryRoot) is not { } repo || RootScope.NormalizeRoot(folder) is not { } candidate)
            return WorktreeFolderState.Unreadable;
        if (RootScope.Contains(repo, candidate) || RootScope.Contains(candidate, repo)) return WorktreeFolderState.Nested;

        try
        {
            if (GitDirectory.Resolve(repo) is not { } repoGitDir) return WorktreeFolderState.NoRepository;

            string path = Path.TrimEndingDirectorySeparator(candidate);
            if (File.Exists(path)) return WorktreeFolderState.Foreign;
            if (!Directory.Exists(path) || !Directory.EnumerateFileSystemEntries(path).Any())
                return WorktreeFolderState.Creatable;

            // Linked worktree'nin işareti bir DOSYADIR; klasör olan .git ayrı bir klondur.
            if (!File.Exists(Path.Combine(path, ".git")) || GitDirectory.Resolve(path) is not { } candidateGitDir)
                return WorktreeFolderState.Foreign;

            return string.Equals(RootScope.NormalizeRoot(GitDirectory.ResolveCommon(candidateGitDir)),
                    RootScope.NormalizeRoot(GitDirectory.ResolveCommon(repoGitDir)), StringComparison.OrdinalIgnoreCase)
                ? WorktreeFolderState.Linked
                : WorktreeFolderState.Foreign;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return WorktreeFolderState.Unreadable;
        }
    }

    /// <summary>Kullanılamayan bir klasörün nedeni — küçük harfle başlayan İngilizce ifade; kullanılabilir durumlarda (<c>Creatable</c>,
    /// <c>Linked</c>) <c>null</c>. Settings'in Save satırı (baş harfi büyütülerek) ve motorun ret ayrıntısı AYNI metni kullanır.</summary>
    public static string? Describe(WorktreeFolderState state) => state switch
    {
        WorktreeFolderState.Foreign => "the worktree folder is not empty and is not a worktree of this repository",
        WorktreeFolderState.Nested => "the worktree folder must sit next to the repository, not inside it or around it",
        WorktreeFolderState.NotRooted => "the worktree folder needs a full path",
        WorktreeFolderState.NoRepository => "the repository root is not a git repository",
        WorktreeFolderState.Unreadable => "the worktree folder cannot be read",
        _ => null,
    };

}
