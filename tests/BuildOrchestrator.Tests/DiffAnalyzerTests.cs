using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Incremental;

namespace BuildOrchestrator.Tests;

public sealed class DiffAnalyzerTests
{
    private static DependencyGraph BuildGraph(string root)
    {
        ProjectNode Node(string name) => new()
        {
            Id = name,
            Name = name,
            ProjectPath = Path.Combine(root, name, name + ".csproj")
        };

        return new DependencyGraph
        {
            RootPath = root,
            Projects = new List<ProjectNode> { Node("A"), Node("B") }
        };
    }

    [Fact]
    public void ChangedFile_MapsToDeepestOwningProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "diff_" + Guid.NewGuid().ToString("N"));
        var graph = BuildGraph(root);

        var changed = new[] { Path.Combine(root, "B", "Services", "Foo.cs") };
        var dirty = new DiffAnalyzer().GetDirtyProjects(graph, changed);

        Assert.Contains("B", dirty);
        Assert.DoesNotContain("A", dirty);
    }

    [Fact]
    public void NonBuildRelevantFiles_AreIgnored()
    {
        var root = Path.Combine(Path.GetTempPath(), "diff_" + Guid.NewGuid().ToString("N"));
        var graph = BuildGraph(root);

        var changed = new[] { Path.Combine(root, "A", "readme.md") };
        var dirty = new DiffAnalyzer().GetDirtyProjects(graph, changed);

        Assert.Empty(dirty);
    }

    [Fact]
    public void DirectoryBuildProps_MarksAllProjectsBeneathIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "diff_" + Guid.NewGuid().ToString("N"));
        var graph = BuildGraph(root);

        var changed = new[] { Path.Combine(root, "Directory.Build.props") };
        var dirty = new DiffAnalyzer().GetDirtyProjects(graph, changed);

        Assert.Contains("A", dirty);
        Assert.Contains("B", dirty);
    }
}
