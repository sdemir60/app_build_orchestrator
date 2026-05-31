using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Graph;
using BuildOrchestrator.Core.Incremental;

namespace BuildOrchestrator.Core.Tests;

public class IncrementalTests
{
    private static ProjectNode N(string id, string dir, params string[] deps)
        => new() { Id = id, Name = id, ProjectPath = Path.Combine(dir, id + ".csproj"), Dependencies = deps.ToList() };

    private static string Root => Path.Combine(Path.GetTempPath(), "bo_inc_test_repo");

    [Fact]
    public void FileMapper_AttributesFileToDeepestProject()
    {
        var root = Root;
        var projA = N("A", Path.Combine(root, "src", "A"));
        var projB = N("B", Path.Combine(root, "src", "A", "B")); // nested deeper

        var mapper = new ProjectFileMapper(new[] { projA, projB });
        var dirty = mapper.MapToDirtyProjects(
            new[] { new FileChange(Path.Combine("src", "A", "B", "Thing.cs")) }, root);

        Assert.Equal(new[] { projB.Id }, dirty);
    }

    [Fact]
    public void FileMapper_IgnoresNonBuildAffectingExtensions()
    {
        var root = Root;
        var projA = N("A", Path.Combine(root, "src", "A"));
        var mapper = new ProjectFileMapper(new[] { projA });

        var dirty = mapper.MapToDirtyProjects(
            new[] { new FileChange(Path.Combine("src", "A", "readme.md")) }, root);

        Assert.Empty(dirty);
    }

    [Fact]
    public void FileMapper_DirectoryBuildPropsDirtiesWholeSubtree()
    {
        var root = Root;
        var projA = N("A", Path.Combine(root, "src", "A"));
        var projB = N("B", Path.Combine(root, "src", "B"));
        var mapper = new ProjectFileMapper(new[] { projA, projB });

        var dirty = mapper.MapToDirtyProjects(
            new[] { new FileChange(Path.Combine("src", "Directory.Build.props")) }, root);

        Assert.Equal(new[] { projA.Id, projB.Id }, dirty.OrderBy(x => x));
    }

    [Fact]
    public void Planner_NeverBuilt_IsSelected()
    {
        var graph = new DependencyGraph(new[] { N("A", "/a") });
        var planner = new IncrementalPlanner(graph);

        var plan = planner.PlanBuild(new IncrementalContext
        {
            Branch = "main",
            CurrentCommit = "c1",
            LocallyDirty = new HashSet<string>(),
            StateLookup = _ => null
        });

        var d = Assert.Single(plan);
        Assert.True(d.ShouldBuild);
        Assert.Equal(BuildReason.NeverBuilt, d.Reason);
    }

    [Fact]
    public void Planner_SkipsWhenCommitUnchangedAndClean()
    {
        var a = N("A", "/a");
        var graph = new DependencyGraph(new[] { a });
        var planner = new IncrementalPlanner(graph);

        var plan = planner.PlanBuild(new IncrementalContext
        {
            Branch = "main",
            CurrentCommit = "c1",
            LocallyDirty = new HashSet<string>(),
            StateLookup = id => new BuildState
            {
                ProjectId = id,
                Branch = "main",
                LastBuiltCommit = "c1",
                LastResult = ProjectStatus.Succeeded
            }
        });

        Assert.False(Assert.Single(plan).ShouldBuild);
    }

    [Fact]
    public void Planner_CommitChanged_Rebuilds()
    {
        var a = N("A", "/a");
        var graph = new DependencyGraph(new[] { a });
        var planner = new IncrementalPlanner(graph);

        var plan = planner.PlanBuild(new IncrementalContext
        {
            Branch = "main",
            CurrentCommit = "c2",
            LocallyDirty = new HashSet<string>(),
            StateLookup = id => new BuildState
            {
                ProjectId = id, Branch = "main", LastBuiltCommit = "c1", LastResult = ProjectStatus.Succeeded
            }
        });

        Assert.Equal(BuildReason.CommitChanged, Assert.Single(plan).Reason);
    }

    [Fact]
    public void Planner_SafeMode_BuildsTransitiveDependents()
    {
        // A clean+built, B clean+built, C depends on B depends on A. A is locally dirty.
        var a = N("A", "/a");
        var b = N("B", "/b", a.Id);
        var c = N("C", "/c", b.Id);
        var graph = new DependencyGraph(new[] { a, b, c });
        TopologicalSorter.ApplyBuildOrder(new[] { a, b, c }, TopologicalSorter.Sort(graph));
        var planner = new IncrementalPlanner(graph);

        BuildState Built(string id) => new()
        {
            ProjectId = id, Branch = "main", LastBuiltCommit = "c1", LastResult = ProjectStatus.Succeeded
        };

        var plan = planner.PlanBuild(new IncrementalContext
        {
            Branch = "main",
            CurrentCommit = "c1",
            LocallyDirty = new HashSet<string> { a.Id },
            StateLookup = Built,
            DependentMode = DependentMode.Safe
        });

        Assert.All(plan, d => Assert.True(d.ShouldBuild));
        Assert.Equal(BuildReason.LocalChange, plan.Single(d => d.ProjectId == a.Id).Reason);
        Assert.Equal(BuildReason.DependentOfDirty, plan.Single(d => d.ProjectId == c.Id).Reason);
    }

    [Fact]
    public void Planner_FastMode_OnlyBuildsDirty()
    {
        var a = N("A", "/a");
        var b = N("B", "/b", a.Id);
        var graph = new DependencyGraph(new[] { a, b });
        TopologicalSorter.ApplyBuildOrder(new[] { a, b }, TopologicalSorter.Sort(graph));
        var planner = new IncrementalPlanner(graph);

        BuildState Built(string id) => new()
        {
            ProjectId = id, Branch = "main", LastBuiltCommit = "c1", LastResult = ProjectStatus.Succeeded
        };

        var plan = planner.PlanBuild(new IncrementalContext
        {
            Branch = "main",
            CurrentCommit = "c1",
            LocallyDirty = new HashSet<string> { a.Id },
            StateLookup = Built,
            DependentMode = DependentMode.Fast
        });

        Assert.True(plan.Single(d => d.ProjectId == a.Id).ShouldBuild);
        Assert.False(plan.Single(d => d.ProjectId == b.Id).ShouldBuild);
    }

    [Fact]
    public void Planner_Rebuild_BuildsEverything()
    {
        var a = N("A", "/a");
        var b = N("B", "/b", a.Id);
        var graph = new DependencyGraph(new[] { a, b });
        var planner = new IncrementalPlanner(graph);

        var plan = planner.PlanRebuild();

        Assert.All(plan, d => Assert.True(d.ShouldBuild));
        Assert.All(plan, d => Assert.Equal(BuildReason.Rebuild, d.Reason));
    }
}
