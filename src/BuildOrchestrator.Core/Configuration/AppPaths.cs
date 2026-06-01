namespace BuildOrchestrator.Core.Configuration;

/// <summary>
/// Well-known storage locations under %LOCALAPPDATA%\BuildOrchestrator\ (Section 2).
/// </summary>
public static class AppPaths
{
    public const string ProductName = "BuildOrchestrator";

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ProductName);

    public static string ConfigFile => Path.Combine(Root, "config.json");

    public static string DependencyGraphFile => Path.Combine(Root, "dependency-graph.json");

    public static string BuildStateFile => Path.Combine(Root, "build-state.json");

    public static string WorktreesRoot => Path.Combine(Root, "worktrees");

    public static string LogsRoot => Path.Combine(Root, "logs");

    /// <summary>Per-branch worktree directory (Section 6 / branch yönetimi).</summary>
    public static string WorktreeFor(string branch)
        => Path.Combine(WorktreesRoot, Sanitize(branch));

    public static void EnsureRoot()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(WorktreesRoot);
        Directory.CreateDirectory(LogsRoot);
    }

    /// <summary>Make a branch name safe for use as a folder name (e.g. feature/x -> feature_x).</summary>
    public static string Sanitize(string branch)
    {
        var invalid = Path.GetInvalidFileNameChars();
        Span<char> buffer = stackalloc char[branch.Length];
        for (int i = 0; i < branch.Length; i++)
        {
            char c = branch[i];
            buffer[i] = (c == '/' || c == '\\' || Array.IndexOf(invalid, c) >= 0) ? '_' : c;
        }
        return new string(buffer);
    }
}
