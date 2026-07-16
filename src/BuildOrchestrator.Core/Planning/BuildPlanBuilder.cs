namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Graph;
using BuildOrchestrator.Contracts.Model;

/// <summary>
/// [T26] Tam planning pipeline'ının assembler'ı: scan → (cache'li) evaluate → producer map →
/// edges → solution map → topo → BuildPlan (build-order'da). Bileşenler ayrı ayrı test edilmiştir;
/// bu sınıf yalnız onları birbirine bağlar (wiring), iş mantığı eklemez.
/// </summary>
public sealed class BuildPlanBuilder(WorkspaceScanner scanner, CsprojEvaluator evaluator, EvaluationCache cache)
{
    public BuildPlan Build(string root, string configuration)
    {
        var scan = scanner.Scan(root);
        var evaluated = scan.CsprojPaths.Select(p => cache.GetOrEvaluate(p, evaluator.Evaluate)).ToList();
        cache.Flush();

        var producers = ProducerMapBuilder.Build(evaluated);
        var edges = GraphBuilder.BuildEdges(evaluated, producers);
        var solutions = SolutionMapper.Map(scan.SlnPaths, scan.CsprojPaths);
        var topo = TopoSort.Compute(edges);

        var edgeById = edges.ToDictionary(e => e.ProjectId, e => e.Dependencies, StringComparer.OrdinalIgnoreCase);
        var nameById = evaluated.ToDictionary(e => e.Path, e => e.AssemblyName, StringComparer.OrdinalIgnoreCase);
        var inCycle = new HashSet<string>(topo.Cycles.SelectMany(c => c), StringComparer.OrdinalIgnoreCase);

        var nodes = new List<ProjectNode>();
        for (int i = 0; i < topo.BuildOrder.Count; i++)
        {
            string id = topo.BuildOrder[i];
            nodes.Add(new ProjectNode(
                Id: id, Name: nameById.GetValueOrDefault(id, Path.GetFileNameWithoutExtension(id)), ProjectPath: id,
                SolutionNames: solutions.GetValueOrDefault(id, []),
                Dependencies: edgeById.GetValueOrDefault(id, []),
                BuildOrder: i, LayerIndex: null, LayerName: null,
                InCycle: inCycle.Contains(id), WillBuild: null));
        }
        return new BuildPlan(nodes, topo.Cycles, configuration);
    }
}
