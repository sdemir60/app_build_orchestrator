using BuildOrchestrator.Core.Git;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>[worktree] Uygulamanın derlediği çalışma kopyası: Settings'teki repo kökü ya da onun yanındaki worktree.</summary>
public enum WorkingCopy
{
    Repository,
    Worktree,
}

/// <summary>[worktree] Alt bardaki anahtarın metinleri — tooltip bir sonraki tıklamanın ne yapacağını söyler.</summary>
public static class WorktreeToggleText
{
    /// <summary>Repo kopyasında: worktree tanımsızsa kurulum daveti, tanımlıysa worktree'ye geçiş ve yolu; worktree'de: repo'ya dönüş ve
    /// repo kökü.</summary>
    public static string Tooltip(WorkingCopy active, string worktreeFolder, string repositoryRoot = "") =>
        active == WorkingCopy.Worktree ? $"Back to the repository — {repositoryRoot}"
        : worktreeFolder.Length == 0 ? "Set up a worktree — a second working copy for another branch"
        : $"Switch to the worktree — {worktreeFolder}";
}

/// <summary>[worktree] Kopya geçişinin ve worktree kurulumunun konsol ve Settings metinleri — TEK kaynak (kopya YASAK).</summary>
public static class WorkingCopyText
{
    /// <summary>Kopyanın konsoldaki adı.</summary>
    public static string Name(WorkingCopy copy) => copy == WorkingCopy.Worktree ? "the worktree" : "the repository";

    /// <summary>Geçişin açtığı bölümün ilk satırı.</summary>
    public static string Switched(WorkingCopy copy, string root) => $"Switched to {Name(copy)} — {root}";

    /// <summary>Settings Save worktree'yi kurmaya başladı.</summary>
    public static string Creating(string folder) => $"Creating the worktree at {folder}…";

    /// <summary>Worktree kuruldu (ya da zaten oradaydı) — ana kopyanın HEAD'inde, branch'siz.</summary>
    public static string Created(string folder, string revision) => $"Worktree ready at {folder} — detached at {revision}";

    /// <summary>Worktree kurulamadı.</summary>
    public static string NotCreated(string reason) => $"warning: worktree not created — {reason}";

    /// <summary>Settings Save worktree klasörünü değiştirdi.</summary>
    public static string FolderChanged(string folder) =>
        folder.Length > 0 ? $"Worktree folder → {folder}" : "Worktree folder cleared";

    /// <summary>Settings'in Save satırı: klasör kullanılamıyorsa nedeni (baş harfi büyük); kullanılabilirse <c>null</c>. Metnin kendisi
    /// Core'dadır (<see cref="WorktreeFolder.Describe"/>) — motorun ret ayrıntısıyla aynı cümle.</summary>
    public static string? FolderProblem(WorktreeFolderState state) =>
        WorktreeFolder.Describe(state) is { Length: > 0 } text ? char.ToUpperInvariant(text[0]) + text[1..] : null;
}
