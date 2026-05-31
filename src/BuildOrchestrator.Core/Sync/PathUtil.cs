namespace BuildOrchestrator.Core.Sync;

/// <summary>Path normalization helpers used to produce stable project ids.</summary>
public static class PathUtil
{
    /// <summary>
    /// Produces a canonical id for a path: full path, trimmed trailing separators.
    /// Comparison casing follows the OS (case-insensitive on Windows).
    /// </summary>
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full;
    }

    public static StringComparison IdComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool IdEquals(string a, string b) => string.Equals(a, b, IdComparison);
}
