using BuildOrchestrator.Core.Graph;

namespace BuildOrchestrator.Tests.Graph;

public class TopoSortTests
{
    private static ProjectEdges E(string id, params string[] deps) => new(id, deps);

    [Fact]
    public void linear_chain_orders_dependencies_first()
    {
        // A→B→C (A depends B, B depends C) ⇒ build-order C,B,A
        var r = TopoSort.Compute([E("A", "B"), E("B", "C"), E("C")]);
        Assert.Equal(["C", "B", "A"], r.BuildOrder);
        Assert.Empty(r.Cycles);
    }

    [Fact]
    public void independent_nodes_break_ties_by_id_ordinal()
    {
        var r = TopoSort.Compute([E("B"), E("A"), E("C")]);
        Assert.Equal(["A", "B", "C"], r.BuildOrder); // deterministik
    }

    [Fact]
    public void detects_cycle_as_scc()
    {
        // A→B, B→A ⇒ SCC {A,B}
        var r = TopoSort.Compute([E("A", "B"), E("B", "A")]);
        Assert.Single(r.Cycles);
        Assert.Equal(["A", "B"], r.Cycles[0]);
    }

    // [It-1 deferred → It-2] indeg/dependents yön mantığı: diamond şekli hem "kaç kişi bana bağımlı"
    // hem "ben kaça bağımlıyım" izlemesini aynı anda zorlar — It-2 ReadySetScheduler bu doğruluğa dayanır.
    [Fact]
    public void diamond_orders_dependencies_first_and_is_deterministic()
    {
        // A→B, A→C, B→D, C→D (A, B ve C'ye bağımlı; B ve C, D'ye bağımlı) ⇒ build-order: D,B,C,A
        var edges = new[] { E("A", "B", "C"), E("B", "D"), E("C", "D"), E("D") };

        var r1 = TopoSort.Compute(edges);
        Assert.Empty(r1.Cycles);
        AssertValidTopologicalOrder(edges, r1.BuildOrder);
        Assert.Equal(["D", "B", "C", "A"], r1.BuildOrder);

        // Determinizm [D8]: aynı girdide tekrarlanan Compute çağrısı aynı sırayı üretir
        var r2 = TopoSort.Compute(edges);
        Assert.Equal(r1.BuildOrder, r2.BuildOrder);

        // Determinizm [D8]: kenarların girdi sırası değişse de sonuç değişmez
        var reordered = new[] { E("D"), E("C", "D"), E("A", "B", "C"), E("B", "D") };
        var r3 = TopoSort.Compute(reordered);
        Assert.Equal(r1.BuildOrder, r3.BuildOrder);
    }

    // [It-1 deferred → It-2] çok-üyeli SCC'nin acyclic düğümler arasına doğru yerleştirildiğini sınar.
    [Fact]
    public void multi_node_scc_is_placed_contiguously_between_its_upstream_and_downstream_neighbors()
    {
        // A→B, B→C, C→B, C→D: {B,C} 2-cycle SCC; D SCC'nin bağımlılığı (build-order'da SCC'den önce
        // gelmeli), A SCC'ye bağımlı (build-order'da SCC'den sonra gelmeli)
        var edges = new[] { E("A", "B"), E("B", "C"), E("C", "B", "D"), E("D") };

        var r = TopoSort.Compute(edges);

        Assert.Single(r.Cycles);
        Assert.Equal(["B", "C"], r.Cycles[0]); // SCC üyeleri ordinal sıralı

        var order = r.BuildOrder;
        Assert.Equal(["D", "B", "C", "A"], order);

        int dIdx = order.ToList().IndexOf("D");
        int bIdx = order.ToList().IndexOf("B");
        int cIdx = order.ToList().IndexOf("C");
        int aIdx = order.ToList().IndexOf("A");

        Assert.True(dIdx < bIdx && dIdx < cIdx); // D (bağımlılık) SCC'den önce
        Assert.True(aIdx > bIdx && aIdx > cIdx); // A (bağımlı) SCC'den sonra
        Assert.Equal(1, Math.Abs(bIdx - cIdx));  // SCC üyeleri build-order'da ardışık

        // Determinizm [D8]: girdi kenar sırası değişse de sonuç değişmez
        var reordered = new[] { E("D"), E("C", "B", "D"), E("A", "B"), E("B", "C") };
        var r2 = TopoSort.Compute(reordered);
        Assert.Equal(order, r2.BuildOrder);
    }

    // Herhangi bir edge kümesi + üretilen build-order için "bağımlılık, bağımlıdan önce gelir" kuralını
    // genel olarak doğrular (yalnız acyclic düğümler için anlamlı; SCC üyeleri arasında sıra garantisi yok).
    private static void AssertValidTopologicalOrder(IReadOnlyList<ProjectEdges> edges, IReadOnlyList<string> order)
    {
        var position = order.Select((id, i) => (id, i)).ToDictionary(t => t.id, t => t.i, StringComparer.OrdinalIgnoreCase);
        foreach (var e in edges)
            foreach (var dep in e.Dependencies)
                Assert.True(position[dep] < position[e.ProjectId],
                    $"{dep} (bağımlılık), {e.ProjectId}'den önce gelmeli ama gelmiyor.");
    }
}
