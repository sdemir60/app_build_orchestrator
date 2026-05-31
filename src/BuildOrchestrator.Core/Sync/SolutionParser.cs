using System.Text.RegularExpressions;

namespace BuildOrchestrator.Core.Sync;

/// <summary>
/// Lightweight .sln parser: extracts the absolute paths of the .csproj projects referenced by a
/// solution so each project can be tagged with its owning solution name (Section 7 cards).
/// </summary>
public static partial class SolutionParser
{
    // Project("{guid}") = "Name", "relative\path.csproj", "{guid}"
    [GeneratedRegex("Project\\(\"\\{[^}]+\\}\"\\)\\s*=\\s*\"[^\"]*\"\\s*,\\s*\"([^\"]+\\.csproj)\"",
        RegexOptions.IgnoreCase)]
    private static partial Regex ProjectLine();

    public static IReadOnlyList<string> GetProjectPaths(string solutionPath)
    {
        string text;
        try
        {
            text = File.ReadAllText(solutionPath);
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(solutionPath)) ?? ".";
        var result = new List<string>();

        foreach (Match m in ProjectLine().Matches(text))
        {
            var rel = m.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(dir, rel));
            result.Add(PathUtil.Normalize(full));
        }

        return result;
    }
}
