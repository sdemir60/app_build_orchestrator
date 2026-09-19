namespace BuildOrchestrator.Core.Git;

/// <summary>
/// [Faz 2/T1] Git işlem durumu için kullanıcıya ulaşan metinlerin TEK yeri (kopya YASAK, CLAUDE.md —
/// <see cref="GitMessages"/> deseni). Uygulama İngilizce-only'dir.
/// </summary>
public static class GitOperationText
{
    /// <summary>[Faz 2/T9 · spec §6.4] Takılı kilit eşiği: <c>index.lock</c> (<see cref="GitOperation.CommandRunning"/>)
    /// bu kadar saniye kesintisiz durursa <see cref="StuckLock"/> yazılır — eşiğin ve metnin TEK kaynağı.</summary>
    public const int StuckLockSeconds = 30;

    /// <summary>Takılı <c>index.lock</c> uyarısı — işleme göre değişmez, tek sabit metin.</summary>
    public static readonly string StuckLock =
        $"git's index.lock has been there for {StuckLockSeconds} s — a git process may have crashed; delete it only if no git command is running";

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

    /// <summary>[Faz 2/T9 · spec §6.4] Sync düğmesi yarım bir ağaçta koşarken temizlikten sonra yazılan TEK satır.
    /// <see cref="BuildWarning"/> ile aynı kural: <see cref="GitOperation.None"/> ve
    /// <see cref="GitOperation.CommandRunning"/> için <c>null</c>.</summary>
    public static string? SyncWarning(GitOperation operation) => operation switch
    {
        GitOperation.Merge => "the working tree is mid-merge — results may change once it finishes",
        GitOperation.Rebase => "the working tree is mid-rebase — results may change once it finishes",
        GitOperation.CherryPick => "the working tree is mid-cherry-pick — results may change once it finishes",
        GitOperation.Revert => "the working tree is mid-revert — results may change once it finishes",
        _ => null,
    };
}
