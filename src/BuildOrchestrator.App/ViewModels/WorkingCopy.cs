namespace BuildOrchestrator.App.ViewModels;

/// <summary>[worktree] Uygulamanın derlediği çalışma kopyası: Settings'teki repo kökü ya da onun yanındaki worktree.</summary>
public enum WorkingCopy
{
    Repository,
    Worktree,
}

/// <summary>[worktree] Kopya geçişinin ve worktree kurulumunun konsol metinleri — TEK kaynak (kopya YASAK).</summary>
public static class WorkingCopyText
{
    /// <summary>Kopyanın konsoldaki adı.</summary>
    public static string Name(WorkingCopy copy) => copy == WorkingCopy.Worktree ? "the worktree" : "the repository";

    /// <summary>Geçişin açtığı bölümün ilk satırı.</summary>
    public static string Switched(WorkingCopy copy, string root) => $"Switched to {Name(copy)} — {root}";
}
