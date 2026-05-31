using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Graph;

namespace BuildOrchestrator.Core.Tests;

public class GraphTests
{
    private static ProjectNode N(string id, params string[] deps)
        => new() { Id = id, Name = id, ProjectPath = id, Dependencies = deps.ToList() };

    [Fact]
    public void TopologicalSort_OrdersDependenciesFirst()
    {
        // C -> B -> A  (C depends on B depends on A)
        var graph = new DependencyGraph(new[]
        {
            N("A"),
            N("B", "A"),
            N("C", "B")
        });

        var order = TopologicalSorter.Sort(graph).Order.ToList();

        Assert.True(order.IndexOf("A") < order.IndexOf("B"));
        Assert.True(order.IndexOf("B") < order.IndexOf("C"));
        var result = TopologicalSorter.Sort(graph);
        Assert.Empty(result.CyclicRemainder);
    }

    [Fact]
    public void TopologicalSort_IndependentProjectsShareFirstWave()
    {
        var graph = new DependencyGraph(new[]
        {
            N("A"),
            N("B"),
            N("C", "A", "B")
        });

        var result = TopologicalSorter.Sort(graph);

        Assert.Contains("A", result.Waves[0]);
        Assert.Contains("B", result.Waves[0]);
        Assert.Equal(new[] { "C" }, result.Waves[1]);
    }

    [Fact]
    public void CycleDetector_FindsTwoNodeCycle()
    {
        // A <-> B
        var graph = new DependencyGraph(new[]
        {
            N("A", "B"),
            N("B", "A"),
            N("C", "A")
        });

        var result = CycleDetector.FindCycles(graph);

        Assert.True(result.HasCycles);
        var cycle = Assert.Single(result.Cycles);
        Assert.Equal(new[] { "A", "B" }, cycle.OrderBy(x => x));
    }

    [Fact]
    public void CycleDetector_NoFalsePositiveOnDiamond()
    {
        // D depends on B and C; both depend on A. No cycle.
        var graph = new DependencyGraph(new[]
        {
            N("A"),
            N("B", "A"),
            N("C", "A"),
            N("D", "B", "C")
        });

        Assert.False(CycleDetector.FindCycles(graph).HasCycles);
    }

    [Fact]
    public void CycleDetector_DetectsSelfReference()
    {
        var graph = new DependencyGraph(new[] { N("A", "A") });
        Assert.True(CycleDetector.FindCycles(graph).HasCycles);
    }

    [Fact]
    public void Annotate_MarksCycleMembers()
    {
        var nodes = new[] { N("A", "B"), N("B", "A") };
        var graph = new DependencyGraph(nodes);
        var result = CycleDetector.FindCycles(graph);

        CycleDetector.Annotate(nodes, result);

        Assert.All(nodes, n => Assert.True(n.IsInCycle));
        Assert.All(nodes, n => Assert.Equal(2, n.CycleMembers.Count));
    }

    [Fact]
    public void TransitiveDependents_IncludesAllUpstreamConsumers()
    {
        // A <- B <- C  (B depends on A, C depends on B)
        var graph = new DependencyGraph(new[]
        {
            N("A"),
            N("B", "A"),
            N("C", "B")
        });

        var dependents = graph.TransitiveDependents(new[] { "A" });

        Assert.Equal(new[] { "A", "B", "C" }, dependents.OrderBy(x => x));
    }

    [Fact]
    public void TopologicalSort_LargeGraphDoesNotOverflow()
    {
        // Deep chain of 5000 to exercise the iterative algorithms.
        var nodes = new List<ProjectNode>();
        for (var i = 0; i < 5000; i++)
        {
            nodes.Add(i == 0 ? N($"P{i}") : N($"P{i}", $"P{i - 1}"));
        }
        var graph = new DependencyGraph(nodes);

        Assert.False(CycleDetector.FindCycles(graph).HasCycles);
        var result = TopologicalSorter.Sort(graph);
        Assert.Equal(5000, result.Order.Count);
        Assert.Equal("P0", result.Order[0]);
    }
}
