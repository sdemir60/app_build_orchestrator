namespace BuildOrchestrator.Core.Git;

/// <summary>
/// [Faz 2/T1] Git işlem durumu için kullanıcıya ulaşan metinlerin TEK yeri (kopya YASAK, CLAUDE.md —
/// <see cref="GitMessages"/> deseni). Uygulama İngilizce-only'dir.
/// </summary>
public static class GitOperationText
{
    /// <summary>Takılı <c>index.lock</c> uyarısı — işleme göre değişmez, tek sabit metin.</summary>
    public const string StuckLock =
        "git's index.lock has been there for 30 s — a git process may have crashed; delete it only if no git command is running";

    /// <summary>Alt bardaki durum çipinin tooltip'i. <see cref="GitOperation.None"/> için <c>null</c> —
    /// sürmekte bir işlem yoksa gösterilecek tooltip de yoktur.</summary>
    public static string? Tooltip(GitOperation operation) => operation switch
    {
        GitOperation.Merge => "Merge in progress — finish or abort it in git",
        GitOperation.Rebase => "Rebase in progress — finish or abort it in git",
        GitOperation.CherryPick => "Cherry-pick in progress — finish or abort it in git",
        GitOperation.Revert => "Revert in progress — finish or abort it in git",
        GitOperation.CommandRunning => "A git command is running",
        _ => null,
    };

    /// <summary>Build başlamadan önce gösterilen uyarı. <see cref="GitOperation.None"/> ve
    /// <see cref="GitOperation.CommandRunning"/> için <c>null</c> — ikisi de "conflict marker'lı dosya"
    /// riski taşımaz (CommandRunning kısa süreli bir git komutudur, çakışma durumu değil).</summary>
    public static string? BuildWarning(GitOperation operation) => operation switch
    {
        GitOperation.Merge => "a merge is in progress — files with conflict markers will not compile",
        GitOperation.Rebase => "a rebase is in progress — files with conflict markers will not compile",
        GitOperation.CherryPick => "a cherry-pick is in progress — files with conflict markers will not compile",
        GitOperation.Revert => "a revert is in progress — files with conflict markers will not compile",
        _ => null,
    };
}
