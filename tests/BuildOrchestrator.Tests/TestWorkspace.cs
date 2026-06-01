using System.Text;

namespace BuildOrchestrator.Tests;

/// <summary>
/// Writes a throwaway folder of .csproj/.sln files on disk so the scanner and graph builder
/// can be exercised against real files. Disposed (deleted) at the end of each test.
/// </summary>
internal sealed class TestWorkspace : IDisposable
{
    public string Root { get; }

    public TestWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "bo_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>Create &lt;project&gt;/&lt;project&gt;.csproj that references the given project names.</summary>
    public string AddProject(string name, params string[] references)
    {
        var dir = Path.Combine(Root, name);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + ".csproj");

        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine("  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>");
        if (references.Length > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var r in references)
                sb.AppendLine($"    <ProjectReference Include=\"..\\{r}\\{r}.csproj\" />");
            sb.AppendLine("  </ItemGroup>");
        }
        sb.AppendLine("</Project>");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    public string AddFile(string relativePath, string content = "// test")
    {
        var full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
