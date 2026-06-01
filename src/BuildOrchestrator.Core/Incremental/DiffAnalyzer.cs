using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Discovery;

namespace BuildOrchestrator.Core.Incremental;

/// <summary>
/// Maps changed files to projects and decides which projects are "dirty" (Section 6).
/// Decision is source-signal only (never DLL timestamps).
/// </summary>
public sealed class DiffAnalyzer
{
    private static readonly HashSet<string> BuildRelevantExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".xaml", ".resx", ".csproj", ".props", ".targets"
    };

    private static readonly HashSet<string> GlobalImportFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"
    };

    public static bool IsBuildRelevant(string path)
        => BuildRelevantExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Given the changed files, return the set of directly dirty project ids.
    /// A file maps to the project whose directory is the deepest ancestor of the file.
    /// A changed Directory.Build.props/targets marks every project beneath its directory dirty.
    /// </summary>
    public HashSet<string> GetDirtyProjects(DependencyGraph graph, IEnumerable<string> changedFiles)
    {
        var dirty = new HashSet<string>();

        // Precompute project directories (normalized, with trailing separator).
        var projectDirs = graph.Projects
            .Select(p => (p.Id, Dir: NormalizeDir(Path.GetDirectoryName(p.ProjectPath)!)))
            .ToList();

        foreach (var rawFile in changedFiles)
        {
            if (!IsBuildRelevant(rawFile))
                continue;

            var file = ProjectId.Normalize(rawFile);
            var fileName = Path.GetFileName(rawFile);

            if (GlobalImportFiles.Contains(fileName))
            {
                // Affects all projects under the file's directory.
                var importDir = NormalizeDir(Path.GetDirectoryName(rawFile)!);
                foreach (var (id, dir) in projectDirs)
                    if (dir.StartsWith(importDir, StringComparison.OrdinalIgnoreCase))
                        dirty.Add(id);
                continue;
            }

            // Find the deepest project directory that contains the file.
            string? bestId = null;
            int bestLen = -1;
            foreach (var (id, dir) in projectDirs)
            {
                if (file.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && dir.Length > bestLen)
                {
                    bestId = id;
                    bestLen = dir.Length;
                }
            }
            if (bestId != null)
                dirty.Add(bestId);
        }

        return dirty;
    }

    private static string NormalizeDir(string dir)
    {
        var full = ProjectId.Normalize(dir);
        return full.EndsWith(Path.DirectorySeparatorChar) ? full : full + Path.DirectorySeparatorChar;
    }
}
