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
    public BuildPlan Build(string root, string configuration, IReadOnlyList<LayerPattern>? layerPatterns = null) =>
        Build(scanner.Scan(root), configuration, layerPatterns);

    /// <summary>
    /// [Task 18] <see cref="Build(string, string, IReadOnlyList{LayerPattern}?)"/> ile AYNI pipeline, yalnız
    /// scan ADIMI dışarıdan verilir: çağıran (ör. Supervisor'ın Program.cs'i) hem <see cref="BuildPlan"/>'ı hem
    /// de HAM <see cref="ScanResult"/>'ı (ör. <c>SolutionMapper.MapRefs</c> için .sln YOLLARI) istiyorsa,
    /// workspace'i İKİ KEZ taramak zorunda kalmaz — <c>scanner.Scan(root)</c> TEK SEFER çağrılır, sonucu her
    /// iki ihtiyaç için de paylaşılır.
    /// </summary>
    public BuildPlan Build(ScanResult scan, string configuration, IReadOnlyList<LayerPattern>? layerPatterns = null)
    {
        // GetOrEvaluate canlı build ↔ scan yarışında kaybolan bir dosya için null dönebilir
        // [Task 0/It-4a, savunmanın ikinci katı] — bu yollar plandan sessizce düşer (OfType null'ları eler).
        var evaluated = scan.CsprojPaths.Select(p => cache.GetOrEvaluate(p, evaluator.Evaluate)).OfType<EvaluatedProject>().ToList();
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

        // [T15][N8] Katman ataması + sert faz bariyeri: pattern yoksa (varsayılan) LayerEngine nodes'u aynen
        // döner (mevcut davranış, regresyon yok). [A1] Warnings (ters katman bağımlılığı) artık YUTULMAZ,
        // BuildPlan.LayerWarnings ile taşınır: warn-only tasarımın tek gerçek düzeltmesi kullanıcının
        // pattern'leri gözden geçirmesidir, bu yüzden uyarının ona ULAŞMASI şarttır.
        var assignment = LayerEngine.AssignLayers(nodes, layerPatterns ?? []);
        return new BuildPlan(assignment.Nodes, topo.Cycles, configuration, assignment.Warnings);
    }
}
