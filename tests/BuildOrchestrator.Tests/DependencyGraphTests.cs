using BuildOrchestrator.Core.Discovery;

namespace BuildOrchestrator.Tests;

public sealed class DependencyGraphTests
{
    [Fact]
    public void TopologicalOrder_DependenciesComeFirst()
    {
        using var ws = new TestWorkspace();
        ws.AddProject("A");
        ws.AddProject("B", "A");
        ws.AddProject("C", "B");

        var scan = new ProjectScanner().Scan(ws.Root);
        var graph = new DependencyGraphBuilder().Build(ws.Root, scan);

        Assert.Equal(3, graph.Projects.Count);

        int OrderOf(string name) =>
            graph.Projects.Single(p => p.Name == name).BuildOrder;

        Assert.True(OrderOf("A") < OrderOf("B"));
        Assert.True(OrderOf("B") < OrderOf("C"));
        Assert.All(graph.Projects, p => Assert.False(p.InCycle));
    }

    [Fact]
    public void DependencyEdges_AreExtractedFromProjectReferences()
    {
        using var ws = new TestWorkspace();
        ws.AddProject("Core");
        ws.AddProject("Api", "Core");

        var scan = new ProjectScanner().Scan(ws.Root);
        var graph = new DependencyGraphBuilder().Build(ws.Root, scan);

        var core = graph.Projects.Single(p => p.Name == "Core");
        var api = graph.Projects.Single(p => p.Name == "Api");

        Assert.Empty(core.Dependencies);
        Assert.Single(api.Dependencies);
        Assert.Equal(core.Id, api.Dependencies[0]);
    }

    [Fact]
    public void CycleDetection_FlagsAllMembers()
    {
        using var ws = new TestWorkspace();
        ws.AddProject("X", "Y");
        ws.AddProject("Y", "X");

        var scan = new ProjectScanner().Scan(ws.Root);
        var graph = new DependencyGraphBuilder().Build(ws.Root, scan);

        var x = graph.Projects.Single(p => p.Name == "X");
        var y = graph.Projects.Single(p => p.Name == "Y");

        Assert.True(x.InCycle);
        Assert.True(y.InCycle);
        Assert.Contains("Y", x.CycleMembers);
        Assert.Contains("X", y.CycleMembers);
    }

    [Fact]
    public void TopologicalOrder_ContainsEveryProjectExactlyOnce()
    {
        using var ws = new TestWorkspace();
        ws.AddProject("A");
        ws.AddProject("B", "A");
        ws.AddProject("C", "A");
        ws.AddProject("D", "B", "C");

        var scan = new ProjectScanner().Scan(ws.Root);
        var graph = new DependencyGraphBuilder().Build(ws.Root, scan);

        Assert.Equal(graph.Projects.Count, graph.TopologicalOrder.Count);
        Assert.Equal(graph.TopologicalOrder.Count, graph.TopologicalOrder.Distinct().Count());

        // D depends (transitively) on everything, so it must be last.
        var dId = graph.Projects.Single(p => p.Name == "D").Id;
        Assert.Equal(dId, graph.TopologicalOrder[^1]);
    }
}
