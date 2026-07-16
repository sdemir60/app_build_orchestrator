using System;
using System.IO;
using BuildOrchestrator.Core.Discovery;

namespace BuildOrchestrator.Tests.Discovery;

public class CsprojEvaluatorTests
{
    private static string WriteProj(string dir, string name, string body)
    {
        Directory.CreateDirectory(dir);
        string p = Path.Combine(dir, name);
        File.WriteAllText(p, body);
        return p;
    }

    [Fact]
    public void Evaluate_legacy_project_extracts_items()
    {
        string root = Path.Combine(Path.GetTempPath(), "eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "A");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "dummy.txt"), ""); // ensure root
            string proj = WriteProj(dir, "A.csproj", """
                <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup><AssemblyName>OSYS.A</AssemblyName></PropertyGroup>
                  <ItemGroup>
                    <Compile Include="Foo.cs" />
                    <Reference Include="OSYS.B"><HintPath>..\B\bin\OSYS.B.dll</HintPath></Reference>
                    <Reference Include="System.Xml" />
                    <ProjectReference Include="..\C\C.csproj" />
                  </ItemGroup>
                </Project>
                """);
            var ev = new CsprojEvaluator().Evaluate(proj);
            Assert.Equal("OSYS.A", ev.AssemblyName);
            Assert.False(ev.IsSdkStyle);
            Assert.Contains(ev.CompileFiles, f => f.EndsWith("Foo.cs", StringComparison.OrdinalIgnoreCase));
            Assert.Single(ev.HintPaths);                       // System.Xml (HintPath yok) elendi
            Assert.Equal("osys.b.dll", ev.HintPaths[0].BaseName);
            Assert.Single(ev.ProjectReferences);
            Assert.EndsWith("C.csproj", ev.ProjectReferences[0]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Evaluate_sdk_style_defaults_assemblyname_and_globs_cs()
    {
        string root = Path.Combine(Path.GetTempPath(), "eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "S");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "S.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(dir, "Bar.cs"), "class Bar{}");
            Directory.CreateDirectory(Path.Combine(dir, "obj"));
            File.WriteAllText(Path.Combine(dir, "obj", "Skip.cs"), "class Skip{}"); // obj → glob dışı
            var ev = new CsprojEvaluator().Evaluate(Path.Combine(dir, "S.csproj"));
            Assert.True(ev.IsSdkStyle);
            Assert.Equal("S", ev.AssemblyName);                // default = dosya adı
            Assert.Contains(ev.CompileFiles, f => f.EndsWith("Bar.cs", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(ev.CompileFiles, f => f.EndsWith("Skip.cs", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
