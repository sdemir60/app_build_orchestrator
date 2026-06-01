using System.Text.RegularExpressions;

namespace BuildOrchestrator.Core.Discovery;

/// <summary>Raw result of scanning the workspace (before graph construction).</summary>
public sealed class ScanResult
{
    /// <summary>Absolute paths of every discovered .csproj.</summary>
    public List<string> ProjectFiles { get; } = new();

    /// <summary>Absolute paths of every discovered .sln.</summary>
    public List<string> SolutionFiles { get; } = new();

    /// <summary>Map of normalized project path -> owning solution name (first match wins).</summary>
    public Dictionary<string, string> ProjectToSolution { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Recursively scans the root for *.sln and *.csproj, ignoring well-known folders
/// (.git, bin, obj, node_modules, .vs) per Section 5.
/// </summary>
public sealed partial class ProjectScanner
{
    private static readonly HashSet<string> IgnoredDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", ".vs"
    };

    [GeneratedRegex(@"Project\(""\{[^}]+\}""\)\s*=\s*""[^""]*"",\s*""([^""]+\.csproj)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex SlnProjectLine();

    public ScanResult Scan(string rootPath, Action<string>? progress = null)
    {
        var result = new ScanResult();
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            return result;

        foreach (var file in EnumerateFiles(rootPath))
        {
            if (file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                result.ProjectFiles.Add(file);
                progress?.Invoke(file);
            }
            else if (file.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                result.SolutionFiles.Add(file);
            }
        }

        MapProjectsToSolutions(result);
        return result;
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] subdirs;
            try
            {
                subdirs = Directory.GetDirectories(dir);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var sub in subdirs)
            {
                var name = Path.GetFileName(sub);
                if (IgnoredDirs.Contains(name))
                    continue;
                stack.Push(sub);
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(dir);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var f in files)
            {
                if (f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
                {
                    yield return f;
                }
            }
        }
    }

    private static void MapProjectsToSolutions(ScanResult result)
    {
        foreach (var sln in result.SolutionFiles)
        {
            var slnName = Path.GetFileNameWithoutExtension(sln);
            var slnDir = Path.GetDirectoryName(sln)!;
            string content;
            try
            {
                content = File.ReadAllText(sln);
            }
            catch
            {
                continue;
            }

            foreach (Match m in SlnProjectLine().Matches(content))
            {
                var relative = m.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar);
                var full = Path.GetFullPath(Path.Combine(slnDir, relative));
                var key = ProjectId.Normalize(full);
                // First solution that references a project wins (deterministic enough for display).
                result.ProjectToSolution.TryAdd(key, slnName);
            }
        }
    }
}
