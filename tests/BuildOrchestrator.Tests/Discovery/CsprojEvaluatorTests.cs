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

    // [T72/Task 14] Legacy csproj'un TargetFrameworkVersion'ı StaleObjDetector.Inspect'in beklediği TAM
    // moniker'a çevrilip EvaluatedProject'e taşınmalı (OSYS legacy csproj'ları v4.6/v4.8 kullanır).
    [Fact]
    public void Evaluate_legacy_project_derives_target_framework_moniker_from_target_framework_version()
    {
        string root = Path.Combine(Path.GetTempPath(), "eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "A");
            string proj = WriteProj(dir, "A.csproj", """
                <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup>
                    <AssemblyName>OSYS.A</AssemblyName>
                    <TargetFrameworkVersion>v4.6</TargetFrameworkVersion>
                  </PropertyGroup>
                </Project>
                """);
            var ev = new CsprojEvaluator().Evaluate(proj);
            Assert.Equal(".NETFramework,Version=v4.6", ev.TargetFrameworkMoniker);
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
            Assert.Equal("net10.0", ev.TargetFrameworkMoniker); // [T72/Task 14] SDK-style TFM olduğu gibi taşınır
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // [T72/Task 14] SDK-style <TargetFramework>netstandardX.Y</TargetFramework> uzun moniker'a çevrilmeli
    // (StaleObjDetector'ın karşılaştırdığı project.assets.json "targets" anahtar biçimiyle aynı olsun diye).
    [Fact]
    public void Evaluate_sdk_style_netstandard_derives_dot_net_standard_moniker()
    {
        string root = Path.Combine(Path.GetTempPath(), "eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "N");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "N.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>netstandard2.0</TargetFramework></PropertyGroup></Project>");
            var ev = new CsprojEvaluator().Evaluate(Path.Combine(dir, "N.csproj"));
            Assert.Equal(".NETStandard,Version=v2.0", ev.TargetFrameworkMoniker);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // [D11] legacy <Compile Include="**\*.cs" /> recursive glob'un projectDir altındaki TÜM .cs dosyalarını
    // (nested dahil) bulması gerekir; eskiden "**" literal dizin adı sanılıp sıfır dosya dönüyordu.
    [Fact]
    public void Evaluate_legacy_recursive_glob_finds_nested_files_but_flat_glob_does_not()
    {
        string root = Path.Combine(Path.GetTempPath(), "eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "R");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "Root.cs"), "class Root{}");
            Directory.CreateDirectory(Path.Combine(dir, "Sub"));
            File.WriteAllText(Path.Combine(dir, "Sub", "Deep.cs"), "class Deep{}");

            string projRecursive = WriteProj(dir, "R.csproj", """
                <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup><AssemblyName>OSYS.R</AssemblyName></PropertyGroup>
                  <ItemGroup>
                    <Compile Include="**\*.cs" />
                  </ItemGroup>
                </Project>
                """);
            var evRecursive = new CsprojEvaluator().Evaluate(projRecursive);
            Assert.Contains(evRecursive.CompileFiles, f => f.EndsWith("Root.cs", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(evRecursive.CompileFiles, f => f.EndsWith("Deep.cs", StringComparison.OrdinalIgnoreCase));

            string flatDir = Path.Combine(root, "F");
            Directory.CreateDirectory(flatDir);
            File.WriteAllText(Path.Combine(flatDir, "Root.cs"), "class Root{}");
            Directory.CreateDirectory(Path.Combine(flatDir, "Sub"));
            File.WriteAllText(Path.Combine(flatDir, "Sub", "Deep.cs"), "class Deep{}");
            string projFlat = WriteProj(flatDir, "F.csproj", """
                <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup><AssemblyName>OSYS.F</AssemblyName></PropertyGroup>
                  <ItemGroup>
                    <Compile Include="*.cs" />
                  </ItemGroup>
                </Project>
                """);
            var evFlat = new CsprojEvaluator().Evaluate(projFlat);
            Assert.Contains(evFlat.CompileFiles, f => f.EndsWith("Root.cs", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(evFlat.CompileFiles, f => f.EndsWith("Deep.cs", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // [D11] ProjectReference Include de MSBuild kuralınca ';' ile bölünmeli (Compile ile aynı davranış).
    [Fact]
    public void Evaluate_legacy_projectreference_splits_on_semicolon()
    {
        string root = Path.Combine(Path.GetTempPath(), "eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "P");
            Directory.CreateDirectory(dir);
            string proj = WriteProj(dir, "P.csproj", """
                <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup><AssemblyName>OSYS.P</AssemblyName></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="..\C\C.csproj;..\D\D.csproj" />
                  </ItemGroup>
                </Project>
                """);
            var ev = new CsprojEvaluator().Evaluate(proj);
            Assert.Equal(2, ev.ProjectReferences.Count);
            Assert.Contains(ev.ProjectReferences, r => r.EndsWith("C.csproj", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(ev.ProjectReferences, r => r.EndsWith("D.csproj", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
