using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.Planning;

// [T15][A6/N8] LayerEngine: sıralı regex+isim pattern listesine göre katman ataması + sert faz bariyeri
// (Layer N tümü, Layer N+1 başlamadan) + ters katman bağımlılığı warn-only tespiti. Testler saf ProjectNode
// fixture'ları üzerinde, I/O yok — nodesInBuildOrder zaten topo/build-order kabulü (diğer Planning/Scheduling
// testleriyle aynı desen).
public class LayerEngineTests
{
    private static ProjectNode N(string id, string name, string[]? deps = null, int buildOrder = 0) =>
        new(Id: id, Name: name, ProjectPath: id, SolutionNames: [], Dependencies: deps ?? [],
            BuildOrder: buildOrder, LayerIndex: null, LayerName: null, InCycle: false, WillBuild: null);

    [Fact]
    public void ordered_regex_first_match_wins_over_later_broader_pattern()
    {
        // "FooData" hem Order=0 (Data) hem Order=1 (catch-all .*) pattern'ine uyar; Order=0 kazanmalı.
        LayerPattern[] patterns =
        [
            new(Order: 0, Regex: "Data", Name: "DataLayer"),
            new(Order: 1, Regex: ".*", Name: "Everything"),
        ];
        var nodes = new[] { N("A", "FooData") };

        var result = LayerEngine.AssignLayers(nodes, patterns);

        Assert.Equal(0, result.Nodes[0].LayerIndex);
        Assert.Equal("DataLayer", result.Nodes[0].LayerName);
    }

    [Fact]
    public void unmatched_node_is_assigned_other_layer_after_last_explicit_layer()
    {
        LayerPattern[] patterns = [new(Order: 0, Regex: "^Data", Name: "DataLayer")];
        var nodes = new[] { N("A", "Ui") };

        var result = LayerEngine.AssignLayers(nodes, patterns);

        Assert.Equal(1, result.Nodes[0].LayerIndex);        // max(Order)=0 + 1
        Assert.Equal(LayerEngine.OtherLayerName, result.Nodes[0].LayerName);
        Assert.Equal("Other", result.Nodes[0].LayerName);
    }

    [Fact]
    public void hard_phase_barrier_orders_all_layer0_before_layer1_preserving_topo_order_within_layer()
    {
        LayerPattern[] patterns =
        [
            new(Order: 0, Regex: "^Data", Name: "DataLayer"),
            new(Order: 1, Regex: "^Ui", Name: "UiLayer"),
        ];
        // Orijinal topo/build-order: X(Ui), A(Data), B(Data), Y(Ui) — bir layer-1 node, layer-0 node'lardan
        // ÖNCE geliyor; bariyer bunu düzeltmeli: tüm layer-0 (A,B, topo sırası korunarak) tüm layer-1'den
        // (X,Y, topo sırası korunarak) önce çıkmalı.
        var nodes = new[]
        {
            N("X", "UiX", buildOrder: 0),
            N("A", "DataA", buildOrder: 1),
            N("B", "DataB", buildOrder: 2),
            N("Y", "UiY", buildOrder: 3),
        };

        var result = LayerEngine.AssignLayers(nodes, patterns);

        Assert.Equal(["A", "B", "X", "Y"], result.Nodes.Select(n => n.Id));
        Assert.Equal([0, 1, 2, 3], result.Nodes.Select(n => n.BuildOrder));
        Assert.Equal([0, 0, 1, 1], result.Nodes.Select(n => n.LayerIndex));
    }

    [Fact]
    public void reverse_layer_dependency_is_warned_but_not_blocked_or_reordered_to_fix()
    {
        LayerPattern[] patterns =
        [
            new(Order: 0, Regex: "^Data", Name: "DataLayer"),
            new(Order: 1, Regex: "^Ui", Name: "UiLayer"),
        ];
        // P (Data, layer0) Q'ya (Ui, layer1) bağımlı — ters katman bağımlılığı: erken katman, sonraki
        // katmandaki bir projeye bağımlı.
        var nodes = new[]
        {
            N("P", "DataP", deps: ["Q"], buildOrder: 0),
            N("Q", "UiQ", buildOrder: 1),
        };

        var result = LayerEngine.AssignLayers(nodes, patterns);

        Assert.Single(result.Warnings);
        Assert.Contains("DataP", result.Warnings[0]);
        Assert.Contains("Q", result.Warnings[0]);

        // Bloklanmadı / "düzeltilmek üzere" yeniden sıralanmadı: bariyer kuralı aynen uygulandı — P hâlâ
        // layer0 olduğu için Q'dan (layer1) önce çıkar, kendi bağımlılığından ÖNCE dispatch edilebilir hâle
        // gelmesi warn-only'nin doğal (düzeltilmeyen) sonucudur.
        Assert.Equal(["P", "Q"], result.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void empty_pattern_list_leaves_layer_fields_null_and_order_unchanged()
    {
        var nodes = new[]
        {
            N("B", "B", buildOrder: 0),
            N("A", "A", deps: ["B"], buildOrder: 1),
        };

        var result = LayerEngine.AssignLayers(nodes, []);

        Assert.Empty(result.Warnings);
        Assert.Equal(["B", "A"], result.Nodes.Select(n => n.Id));
        Assert.All(result.Nodes, n => Assert.Null(n.LayerIndex));
        Assert.All(result.Nodes, n => Assert.Null(n.LayerName));
        Assert.Equal([0, 1], result.Nodes.Select(n => n.BuildOrder));
    }

    [Fact]
    public void assignment_is_deterministic_across_repeated_calls()
    {
        LayerPattern[] patterns =
        [
            new(Order: 0, Regex: "^Data", Name: "DataLayer"),
            new(Order: 1, Regex: "^Ui", Name: "UiLayer"),
        ];
        var nodes = new[]
        {
            N("X", "UiX", buildOrder: 0),
            N("A", "DataA", buildOrder: 1),
            N("B", "DataB", deps: ["A"], buildOrder: 2),
        };

        var r1 = LayerEngine.AssignLayers(nodes, patterns);
        var r2 = LayerEngine.AssignLayers(nodes, patterns);

        Assert.Equal(r1.Nodes, r2.Nodes);
        Assert.Equal(r1.Warnings, r2.Warnings);
    }
}
