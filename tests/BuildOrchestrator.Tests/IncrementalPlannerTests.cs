using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.Persistence;

namespace BuildOrchestrator.Tests;

public sealed class IncrementalPlannerTests
{
    private static (DependencyGraph graph, string a, string b) MakeChainGraph()
    {
        // Unique ids keep the test isolated from any on-disk build state.
        string a = "A_" + Guid.NewGuid().ToString("N");
        string b = "B_" + Guid.NewGuid().ToString("N");

        var graph = new DependencyGraph
        {
            RootPath = "/root",
            Projects = new List<ProjectNode>
            {
                new() { Id = a, Name = "A", ProjectPath = "/root/A/A.csproj", BuildOrder = 0 },
                new() { Id = b, Name = "B", ProjectPath = "/root/B/B.csproj", BuildOrder = 1,
                        Dependencies = new List<string> { a } }
            }
        };
        return (graph, a, b);
    }

    [Fact]
    public void NeverBuiltProjects_AreAlwaysBuilt()
    {
        var (graph, a, b) = MakeChainGraph();
        var states = new BuildStateStore();

        var plan = new IncrementalPlanner().Plan(
            graph, states, branch: "main", currentCommit: "c1",
            locallyDirty: new HashSet<string>(), dependentMode: DependentMode.Fast);

        Assert.Contains(a, plan.ToBuild);
        Assert.Contains(b, plan.ToBuild);
    }

    [Fact]
    public void UpToDateProject_IsSkipped()
    {
        var (graph, a, b) = MakeChainGraph();
        var branch = "branch_" + Guid.NewGuid().ToString("N");
        var states = new BuildStateStore();
        states.Set(new BuildState { ProjectId = a, Branch = branch, LastBuiltCommit = "c1", LastResult = ProjectStatus.Succeeded });
        states.Set(new BuildState { ProjectId = b, Branch = branch, LastBuiltCommit = "c1", LastResult = ProjectStatus.Succeeded });

        var plan = new IncrementalPlanner().Plan(
            graph, states, branch, currentCommit: "c1",
            locallyDirty: new HashSet<string>(), dependentMode: DependentMode.Fast);

        Assert.Contains(a, plan.ToSkip);
        Assert.Contains(b, plan.ToSkip);
    }

    [Fact]
    public void SafeMode_RebuildsDependentsOfDirtyProject()
    {
        var (graph, a, b) = MakeChainGraph();
        var branch = "branch_" + Guid.NewGuid().ToString("N");
        var states = new BuildStateStore();
        // Both previously built & up to date...
        states.Set(new BuildState { ProjectId = a, Branch = branch, LastBuiltCommit = "c1", LastResult = ProjectStatus.Succeeded });
        states.Set(new BuildState { ProjectId = b, Branch = branch, LastBuiltCommit = "c1", LastResult = ProjectStatus.Succeeded });

        // ...but A has a local change. Safe mode must also rebuild its dependent B.
        var plan = new IncrementalPlanner().Plan(
            graph, states, branch, currentCommit: "c1",
            locallyDirty: new HashSet<string> { a }, dependentMode: DependentMode.Safe);

        Assert.Contains(a, plan.ToBuild);
        Assert.Contains(b, plan.ToBuild);
    }

    [Fact]
    public void FastMode_DoesNotRebuildCleanDependents()
    {
        var (graph, a, b) = MakeChainGraph();
        var branch = "branch_" + Guid.NewGuid().ToString("N");
        var states = new BuildStateStore();
        states.Set(new BuildState { ProjectId = a, Branch = branch, LastBuiltCommit = "c1", LastResult = ProjectStatus.Succeeded });
        states.Set(new BuildState { ProjectId = b, Branch = branch, LastBuiltCommit = "c1", LastResult = ProjectStatus.Succeeded });

        var plan = new IncrementalPlanner().Plan(
            graph, states, branch, currentCommit: "c1",
            locallyDirty: new HashSet<string> { a }, dependentMode: DependentMode.Fast);

        Assert.Contains(a, plan.ToBuild);
        Assert.Contains(b, plan.ToSkip);
    }
}
