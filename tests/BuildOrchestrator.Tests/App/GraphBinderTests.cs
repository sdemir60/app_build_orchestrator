using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T50/D5] <see cref="GraphBinder"/> — topoloji + satır VM'lerini graf besleme modeline (<see cref="GraphNode"/>/
/// <see cref="GraphEdge"/>) çeviren SAF çekirdek. Kenar yönü, katman (LayerIndex ?? topolojik derinlik), statü
/// eşlemesi (TEK otorite <see cref="ProjectRowViewModel.Status"/>), katman-içi build-order sırası ve veri-türevli
/// kısa-ad öneki burada pinlenir. WPF'siz.
/// </summary>
public class GraphBinderTests
{
    private static string Id(string name) => $@"C:\repo\{name}.csproj";

    private static ProjectNode Node(string name, string[] deps, int? layerIndex = null, bool inCycle = false, int buildOrder = 0) =>
        new(Id(name), name, Id(name), SolutionNames: [], Dependencies: [.. deps.Select(Id)],
            BuildOrder: buildOrder, LayerIndex: layerIndex, LayerName: null, InCycle: inCycle, WillBuild: null);

    private static IReadOnlyDictionary<string, ProjectRowViewModel> RowsFor(IReadOnlyList<ProjectNode> topology)
    {
        var dict = new Dictionary<string, ProjectRowViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in topology)
            dict[n.Id] = new ProjectRowViewModel(n.Id, n.Name, ProjectRowState.Pending) { InCycle = n.InCycle };
        return dict;
    }

    private static int IndexOf(IReadOnlyList<GraphNode> nodes, string name)
    {
        for (int i = 0; i < nodes.Count; i++) if (nodes[i].Name == name) return i;
        return -1;
    }

    [Fact]
    public void Edges_point_from_dependency_to_dependent()
    {
        var topology = new[]
        {
            Node("Base", []),
            Node("Data.Core", ["Base"]),
            Node("Server.Api", ["Data.Core", "External"]), // "External" topolojide YOK → kenar üretmez
        };

        var edges = GraphBinder.Edges(topology);

        // From = bağımlılık (producer) adı, To = bağımlı (consumer) adı — GraphEdge sözleşmesi (yukarıdan aşağı).
        Assert.Contains(edges, e => e.From == "Base" && e.To == "Data.Core");
        Assert.Contains(edges, e => e.From == "Data.Core" && e.To == "Server.Api");
        Assert.DoesNotContain(edges, e => e.From == "External"); // topoloji-dışı dep atlanır
        Assert.Equal(2, edges.Count);
    }

    [Fact]
    public void Layer_falls_back_to_topological_depth_when_no_layer_patterns_are_configured()
    {
        // LayerIndex hepsi null (katman patterni yok) → derinlik: Base 0, Data 1, Api/Portal 2.
        var topology = new[]
        {
            Node("Base", []),
            Node("Data.Core", ["Base"]),
            Node("Server.Api", ["Data.Core"]),
            Node("Web.Portal", ["Data.Core"]),
        };

        var depth = GraphBinder.TopologicalDepths(topology);
        Assert.Equal(0, depth[Id("Base")]);
        Assert.Equal(1, depth[Id("Data.Core")]);
        Assert.Equal(2, depth[Id("Server.Api")]);
        Assert.Equal(2, depth[Id("Web.Portal")]);

        // LayerOf: LayerIndex yoksa derinliğe düşer...
        Assert.Equal(2, GraphBinder.LayerOf(topology[2], depth));
        // ...ama LayerIndex VARSA o kazanır (fallback değil).
        Assert.Equal(5, GraphBinder.LayerOf(Node("Pinned", ["Base"], layerIndex: 5), depth));

        // Uçtan uca aynı katmanlar Nodes() çıktısında.
        var nodes = GraphBinder.Nodes(topology, RowsFor(topology));
        Assert.Equal(0, nodes.Single(n => n.Name == "Base").Layer);
        Assert.Equal(1, nodes.Single(n => n.Name == "Data.Core").Layer);
        Assert.Equal(2, nodes.Single(n => n.Name == "Web.Portal").Layer);
    }

    [Fact]
    public void Cycle_members_are_reported_as_cycle_status()
    {
        // StatusOf: row null + inCycle → Cycle; sync öncesi her şey Discovered (cycle olsa bile).
        Assert.Equal(GraphStatus.Cycle, GraphBinder.StatusOf(null, inCycle: true, synced: true));
        Assert.Equal(GraphStatus.Discovered, GraphBinder.StatusOf(null, inCycle: true, synced: false));

        // Row VARSA statü TEK otoriteden (row.Status) gelir — cycle üyesi satır (InCycle + Pending) Cycle döner
        // (StatusOf cycle'ı YENİDEN eşlemez; row.Status'a delege eder — çift otorite YASAK).
        var cycleRow = new ProjectRowViewModel(Id("X"), "X", ProjectRowState.Pending) { InCycle = true };
        Assert.Equal(GraphStatus.Cycle, GraphBinder.StatusOf(cycleRow, inCycle: true, synced: true));

        // Uçtan uca: cycle düğümü grafta Cycle; cycle-dışı düğüm değil.
        var topology = new[] { Node("X", [], inCycle: true), Node("Y", ["X"]) };
        var nodes = GraphBinder.Nodes(topology, RowsFor(topology));
        Assert.Equal(GraphStatus.Cycle, nodes.Single(n => n.Name == "X").Status);
        Assert.NotEqual(GraphStatus.Cycle, nodes.Single(n => n.Name == "Y").Status);
    }

    [Fact]
    public void Node_order_within_a_layer_follows_build_order()
    {
        // Alpha ve Beta AYNI katmanda (ikisi de Root'a bağlı → derinlik 1); topoloji build-order'da (Alpha önce).
        var topology = new[]
        {
            Node("Root", []),
            Node("Alpha", ["Root"], buildOrder: 1),
            Node("Beta", ["Root"], buildOrder: 2),
        };

        var nodes = GraphBinder.Nodes(topology, RowsFor(topology));

        Assert.Equal(nodes.Single(n => n.Name == "Alpha").Layer, nodes.Single(n => n.Name == "Beta").Layer);
        Assert.True(IndexOf(nodes, "Alpha") < IndexOf(nodes, "Beta"),
            "aynı katmanda düğüm sırası topolojinin build-order'ını izlemeli");
    }

    [Fact]
    public void Short_label_strips_the_common_prefix_derived_from_the_data_not_a_hardcoded_one()
    {
        // Adlar OSYS DEĞİL: önek VERİDEN türetilir (Contoso.). Hardcode "OSYS." olsaydı hiçbir şey kırpılmazdı.
        var topology = new[]
        {
            Node("Contoso.Web", []),
            Node("Contoso.Data.Core", ["Contoso.Web"]),
        };
        var nodes = GraphBinder.Nodes(topology, RowsFor(topology));
        Assert.Equal("Web", nodes.Single(n => n.Name == "Contoso.Web").ShortName);
        Assert.Equal("Data.Core", nodes.Single(n => n.Name == "Contoso.Data.Core").ShortName);

        // Ortak nokta-segmenti yoksa hiç kırpılmaz (tam ad kalır).
        var noCommon = new[] { Node("Alpha.One", []), Node("Beta.Two", []) };
        var n2 = GraphBinder.Nodes(noCommon, RowsFor(noCommon));
        Assert.Equal("Alpha.One", n2.Single(n => n.Name == "Alpha.One").ShortName);
    }
}
