using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Sync;

namespace BuildOrchestrator.Core.Incremental;

/// <summary>
/// Maps changed files to the projects they make dirty (Section 6).
///
/// Rules:
///  * A file dirties a project when it lives under that project's folder AND has a build-affecting
///    extension (.cs .xaml .resx .csproj .props .targets).
///  * A changed <c>Directory.Build.props/targets</c> dirties every project in its folder subtree
///    (imported "from above").
/// </summary>
public sealed class ProjectFileMapper
{
    private readonly List<(string Id, string Dir)> _projectDirs;

    public ProjectFileMapper(IEnumerable<ProjectNode> projects)
    {
        _projectDirs = projects
            .Select(p => (p.Id, Dir: NormalizeDir(Path.GetDirectoryName(p.ProjectPath) ?? p.ProjectPath)))
            .ToList();
    }

    /// <summary>Returns the ids of projects made dirty by <paramref name="changes"/>.</summary>
    public HashSet<string> MapToDirtyProjects(IEnumerable<FileChange> changes, string repoRoot)
    {
        var dirty = new HashSet<string>(StringComparer.Ordinal);
        var rootFull = NormalizeDir(repoRoot);

        foreach (var change in changes)
        {
            var full = ToAbsolute(change.Path, rootFull);

            if (BuildAffecting.IsDirectoryWide(full))
            {
                // Dirties every project at or below the file's directory.
                var scope = NormalizeDir(Path.GetDirectoryName(full) ?? full);
                foreach (var (id, dir) in _projectDirs)
                {
                    if (IsUnder(dir, scope))
                    {
                        dirty.Add(id);
                    }
                }
                continue;
            }

            if (!BuildAffecting.IsBuildAffecting(full))
            {
                continue;
            }

            // Attribute to the most specific (deepest) project directory containing the file.
            string? bestId = null;
            var bestLen = -1;
            foreach (var (id, dir) in _projectDirs)
            {
                if (IsUnder(full, dir) && dir.Length > bestLen)
                {
                    bestId = id;
                    bestLen = dir.Length;
                }
            }

            if (bestId is not null)
            {
                dirty.Add(bestId);
            }
        }

        return dirty;
    }

    private static string ToAbsolute(string path, string repoRoot)
    {
        var normalized = path.Replace('\\', Path.DirectorySeparatorChar)
                             .Replace('/', Path.DirectorySeparatorChar);
        if (!Path.IsPathRooted(normalized))
        {
            normalized = Path.Combine(repoRoot, normalized);
        }
        return PathUtil.Normalize(normalized);
    }

    private static string NormalizeDir(string dir)
        => PathUtil.Normalize(dir);

    /// <summary>True if <paramref name="path"/> is the directory itself or nested under it.</summary>
    private static bool IsUnder(string path, string dir)
    {
        if (string.Equals(path, dir, PathUtil.IdComparison))
        {
            return true;
        }
        var prefix = dir + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, PathUtil.IdComparison);
    }
}
