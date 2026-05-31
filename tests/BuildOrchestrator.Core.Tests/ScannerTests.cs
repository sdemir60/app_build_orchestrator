using BuildOrchestrator.Core.Sync;

namespace BuildOrchestrator.Core.Tests;

public class ScannerTests : IDisposable
{
    private readonly string _root;

    public ScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bo_scan_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    private string WriteProject(string relDir, string name, params string[] referenceRelPaths)
    {
        var dir = Path.Combine(_root, relDir);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + ".csproj");
        var refs = string.Concat(referenceRelPaths.Select(r =>
            $"  <ItemGroup><ProjectReference Include=\"{r}\" /></ItemGroup>\n"));
        File.WriteAllText(path,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>\n" +
            refs +
            "</Project>\n");
        return path;
    }

    [Fact]
    public void Scan_BuildsGraphAndOrder()
    {
        WriteProject("A", "A");
        WriteProject("B", "B", "../A/A.csproj");
        WriteProject("C", "C", "../B/B.csproj");

        var result = new WorkspaceScanner().Scan(_root);

        Assert.Equal(3, result.Projects.Count);
        Assert.False(result.HasCycles);

        var a = result.Projects.Single(p => p.Name == "A");
        var c = result.Projects.Single(p => p.Name == "C");
        Assert.True(a.BuildOrder < c.BuildOrder);
        Assert.Single(c.Dependencies); // references B
    }

    [Fact]
    public void Scan_IgnoresBinObjAndGit()
    {
        WriteProject("A", "A");
        // Decoy projects inside ignored folders.
        WriteProject(Path.Combine("A", "bin"), "ShouldIgnore1");
        WriteProject(Path.Combine("A", "obj"), "ShouldIgnore2");
        WriteProject(".git", "ShouldIgnore3");

        var result = new WorkspaceScanner().Scan(_root);

        Assert.Single(result.Projects);
        Assert.Equal("A", result.Projects[0].Name);
    }

    [Fact]
    public void Scan_TagsSolutionName()
    {
        var aPath = WriteProject("A", "A");
        var slnPath = Path.Combine(_root, "My.sln");
        File.WriteAllText(slnPath,
            "Microsoft Visual Studio Solution File, Format Version 12.00\n" +
            $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"A\", \"A\\A.csproj\", \"{{11111111-1111-1111-1111-111111111111}}\"\n" +
            "EndProject\n");

        var result = new WorkspaceScanner().Scan(_root);

        Assert.Equal("My", result.Projects.Single().SolutionName);
        _ = aPath;
    }

    [Fact]
    public void Scan_DetectsCycle()
    {
        WriteProject("A", "A", "../B/B.csproj");
        WriteProject("B", "B", "../A/A.csproj");

        var result = new WorkspaceScanner().Scan(_root);

        Assert.True(result.HasCycles);
        Assert.All(result.Projects, p => Assert.True(p.IsInCycle));
    }
}
