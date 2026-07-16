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
}
