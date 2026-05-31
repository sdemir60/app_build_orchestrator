namespace BuildOrchestrator.Core.Incremental;

/// <summary>
/// A working-tree change reported by <c>git status</c> / <c>git diff</c>.
/// <see cref="Path"/> is repo-relative or absolute; the mapper normalizes both.
/// </summary>
public sealed record FileChange(string Path);

/// <summary>Build-affecting file extensions (Section 6).</summary>
public static class BuildAffecting
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".xaml", ".resx", ".csproj", ".props", ".targets"
    };

    /// <summary>Imported "from above" files that, when changed, dirty every project beneath them.</summary>
    public static readonly HashSet<string> DirectoryWideFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Directory.Build.props", "Directory.Build.targets"
    };

    public static bool IsBuildAffecting(string path)
        => Extensions.Contains(System.IO.Path.GetExtension(path));

    public static bool IsDirectoryWide(string path)
        => DirectoryWideFiles.Contains(System.IO.Path.GetFileName(path));
}
