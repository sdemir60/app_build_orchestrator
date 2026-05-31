using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Graph;

namespace BuildOrchestrator.Core.Sync;

/// <summary>Progress callback signature for a workspace scan.</summary>
public delegate void SyncProgressHandler(string phase, int scanned, int total, string? current);

/// <summary>
/// Implements Section 5: recursively discover *.sln and *.csproj (ignoring well-known folders),
/// build the global ProjectReference graph, compute topological order, detect cycles, and return
/// every discovered project (including ones that will never be built).
/// </summary>
public sealed class WorkspaceScanner
{
    private static readonly string[] IgnoredDirs = { ".git", "bin", "obj", "node_modules", ".vs" };

    /// <summary>
    /// Scans <paramref name="rootPath"/> and returns the fully-annotated node set plus whether any
    /// cycle exists. Nodes carry build order, solution name, and cycle annotations.
    /// </summary>
    public ScanResult Scan(string rootPath, SyncProgressHandler? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Root path not found: {rootPath}");
        }

        progress?.Invoke("scanning", 0, 0, rootPath);

        var csprojFiles = EnumerateFiles(rootPath, "*.csproj", ct).ToList();
        var slnFiles = EnumerateFiles(rootPath, "*.sln", ct).ToList();

        // Map project path -> solution name (first solution wins; a project may be in several).
        var solutionByProject = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sln in slnFiles)
        {
            ct.ThrowIfCancellationRequested();
            var slnName = Path.GetFileNameWithoutExtension(sln);
            foreach (var proj in SolutionParser.GetProjectPaths(sln))
            {
                solutionByProject.TryAdd(proj, slnName);
            }
        }

        // Build nodes.
        var nodes = new List<ProjectNode>(csprojFiles.Count);
        var idSet = new HashSet<string>(csprojFiles.Select(PathUtil.Normalize), StringComparer.Ordinal);
        var total = csprojFiles.Count;
        var scanned = 0;

        foreach (var file in csprojFiles)
        {
            ct.ThrowIfCancellationRequested();
            var id = PathUtil.Normalize(file);
            var refs = CsprojParser.GetProjectReferences(file)
                .Where(idSet.Contains) // keep only references to projects we actually discovered
                .Distinct(StringComparer.Ordinal)
                .ToList();

            nodes.Add(new ProjectNode
            {
                Id = id,
                Name = Path.GetFileNameWithoutExtension(file),
                ProjectPath = id,
                SolutionName = solutionByProject.TryGetValue(id, out var s) ? s : string.Empty,
                Dependencies = refs
            });

            scanned++;
            progress?.Invoke("parsing", scanned, total, file);
        }

        progress?.Invoke("analyzing", total, total, null);

        var graph = new DependencyGraph(nodes);

        var cycles = CycleDetector.FindCycles(graph);
        CycleDetector.Annotate(nodes, cycles);

        var topo = TopologicalSorter.Sort(graph);
        TopologicalSorter.ApplyBuildOrder(nodes, topo);

        var ordered = nodes.OrderBy(n => n.BuildOrder).ThenBy(n => n.Name, StringComparer.Ordinal).ToList();

        return new ScanResult(ordered, cycles.HasCycles, topo);
    }

    private static IEnumerable<string> EnumerateFiles(string root, string pattern, CancellationToken ct)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            string[] subDirs;
            try
            {
                subDirs = Directory.GetDirectories(dir);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var sub in subDirs)
            {
                var name = Path.GetFileName(sub);
                if (IgnoredDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                stack.Push(sub);
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(dir, pattern);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }
}

/// <summary>Result of a workspace scan.</summary>
public sealed record ScanResult(
    IReadOnlyList<ProjectNode> Projects,
    bool HasCycles,
    TopologicalSorter.Result Topology);
