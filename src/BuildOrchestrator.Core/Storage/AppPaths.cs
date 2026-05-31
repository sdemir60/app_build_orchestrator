namespace BuildOrchestrator.Core.Storage;

/// <summary>
/// Resolves the persistent data locations under %LOCALAPPDATA%\BuildOrchestrator (Section 2/3).
/// All paths are created on demand.
/// </summary>
public sealed class AppPaths
{
    public const string AppFolderName = "BuildOrchestrator";

    public AppPaths(string? rootOverride = null)
    {
        Root = string.IsNullOrWhiteSpace(rootOverride)
            ? Path.Combine(GetLocalAppData(), AppFolderName)
            : rootOverride!;
    }

    /// <summary>Base data directory.</summary>
    public string Root { get; }

    public string ConfigFile => Path.Combine(Root, "config.json");

    public string DependencyGraphFile => Path.Combine(Root, "dependency-graph.json");

    public string BuildStateFile => Path.Combine(Root, "build-state.json");

    /// <summary>Pool of per-branch worktrees (Section 6).</summary>
    public string WorktreesRoot => Path.Combine(Root, "worktrees");

    public string WorktreeFor(string branch)
        => Path.Combine(WorktreesRoot, SanitizeBranch(branch));

    public void EnsureRoot() => Directory.CreateDirectory(Root);

    /// <summary>Branch names may contain '/'; flatten them for filesystem use.</summary>
    public static string SanitizeBranch(string branch)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = branch.Select(c => invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c).ToArray();
        return new string(chars);
    }

    private static string GetLocalAppData()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local))
        {
            return local;
        }

        // Non-Windows fallback (dev/test on Linux/macOS).
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(string.IsNullOrEmpty(home) ? "." : home, ".local", "share");
    }
}
