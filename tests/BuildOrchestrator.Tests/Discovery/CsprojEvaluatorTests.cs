using System;
using System.IO;
using System.Linq;
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

    // [Review fix/Task 14] SDK-style <TargetFramework>netstandardX.Y</TargetFramework> KISA biçimde geçmeli —
    // project.assets.json "targets" anahtarı SDK-style'da hep kısa TFM'dir, netstandard de istisna değildir.
    // Eskiden burada uzun ".NETStandard,Version=vX.Y" biçimine çevriliyordu; bu, temiz bir SDK-style netstandard
    // projesinde StaleObjDetector'ın kendi meşru "targets" anahtarını tanımayıp sahte "stale" uyarısı üretmesine
    // yol açıyordu (bkz. StaleObjRunStartWarnerTests round-trip testi — RED→GREEN kanıtı orada).
    [Fact]
    public void Evaluate_sdk_style_netstandard_passes_through_unchanged()
    {
        string root = Path.Combine(Path.GetTempPath(), "eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "N");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "N.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>netstandard2.0</TargetFramework></PropertyGroup></Project>");
            var ev = new CsprojEvaluator().Evaluate(Path.Combine(dir, "N.csproj"));
            Assert.Equal("netstandard2.0", ev.TargetFrameworkMoniker);
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

    // ---------------------------------------------------------------- [Faz 3/Task 1] OutputPath / OutputType / HintPathTargets

    private static string WriteRealisticProj(string dir, string extraPropertyGroups)
    {
        Directory.CreateDirectory(dir);
        string proj = Path.Combine(dir, "A.csproj");
        File.WriteAllText(proj, $$"""
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <AssemblyName>OSYS.A</AssemblyName>
                <OutputType>Library</OutputType>
                <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
              </PropertyGroup>
              {{extraPropertyGroups}}
            </Project>
            """);
        return proj;
    }

    [Fact]
    public void A_legacy_debug_group_gives_bin_debug_dll()
    {
        string root = Path.Combine(Path.GetTempPath(), "eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "A");
            string proj = WriteRealisticProj(dir, """
                <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
                  <OutputPath>bin\Debug\</OutputPath>
                </PropertyGroup>
                <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
                  <OutputPath>bin\Release\</OutputPath>
                </PropertyGroup>
                """);
            var ev = new CsprojEvaluator().Evaluate(proj);
            Assert.Equal(Path.Combine(dir, "bin", "Debug", "OSYS.A.dll"), ev.OutputFileFor("Debug"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void The_release_group_is_picked_for_release()
    {
        string root = Path.Combine(Path.GetTempPath(), "eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "A");
            string proj = WriteRealisticProj(dir, """
                <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
                  <OutputPath>bin\Debug\</OutputPath>
                </PropertyGroup>
                <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' ">
                  <OutputPath>bin\Release\</OutputPath>
                </PropertyGroup>
                """);
            var ev = new CsprojEvaluator().Evaluate(proj);
            Assert.Equal(Path.Combine(dir, "bin", "Release", "OSYS.A.dll"), ev.OutputFileFor("Release"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void An_unconditional_output_path_applies()
    {
        string root = Path.Combine(Path.GetTempPath(), "eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "A");
            string proj = WriteRealisticProj(dir, """
                <PropertyGroup>
                  <OutputPath>bin\Whatever\</OutputPath>
                </PropertyGroup>
                """);
            var ev = new CsprojEvaluator().Evaluate(proj);
            Assert.Equal(Path.Combine(dir, "bin", "Whatever", "OSYS.A.dll"), ev.OutputFileFor("Debug"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_later_matching_group_wins()
    {
        string root = Path.Combine(Path.GetTempPath(), "eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "A");
            string proj = WriteRealisticProj(dir, """
                <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
                  <OutputPath>bin\First\</OutputPath>
                </PropertyGroup>
                <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
                  <OutputPath>bin\Second\</OutputPath>
                </PropertyGroup>
                """);
            var ev = new CsprojEvaluator().Evaluate(proj);
            Assert.Equal(Path.Combine(dir, "bin", "Second", "OSYS.A.dll"), ev.OutputFileFor("Debug"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void No_output_path_falls_back_to_bin_configuration()
    {
        string root = Path.Combine(Path.GetTempPath(), "eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "A");
            string proj = WriteRealisticProj(dir, "");
            var ev = new CsprojEvaluator().Evaluate(proj);
            Assert.Equal(Path.Combine(dir, "bin", "Debug", "OSYS.A.dll"), ev.OutputFileFor("Debug"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_platform_other_than_the_default_is_ignored()
    {
        string root = Path.Combine(Path.GetTempPath(), "eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "A");
            string proj = WriteRealisticProj(dir, """
                <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|x64' ">
                  <OutputPath>bin\x64Debug\</OutputPath>
                </PropertyGroup>
                """);
            var ev = new CsprojEvaluator().Evaluate(proj);
            // Varsayilan platform AnyCPU'dur (Platform elemani AnyCPU verir); x64 grubu eslesmemeli, fallback kullanilir.
            Assert.Equal(Path.Combine(dir, "bin", "Debug", "OSYS.A.dll"), ev.OutputFileFor("Debug"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Exe_and_WinExe_give_an_exe()
    {
        string root = Path.Combine(Path.GetTempPath(), "eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dirExe = Path.Combine(root, "Exe");
            string projExe = WriteProj(dirExe, "A.csproj", """
                <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup>
                    <AssemblyName>OSYS.A</AssemblyName>
                    <OutputType>Exe</OutputType>
                  </PropertyGroup>
                </Project>
                """);
            var evExe = new CsprojEvaluator().Evaluate(projExe);
            Assert.Equal(Path.Combine(dirExe, "bin", "Debug", "OSYS.A.exe"), evExe.OutputFileFor("Debug"));

            string dirWinExe = Path.Combine(root, "WinExe");
            string projWinExe = WriteProj(dirWinExe, "A.csproj", """
                <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup>
                    <AssemblyName>OSYS.A</AssemblyName>
                    <OutputType>WinExe</OutputType>
                  </PropertyGroup>
                </Project>
                """);
            var evWinExe = new CsprojEvaluator().Evaluate(projWinExe);
            Assert.Equal(Path.Combine(dirWinExe, "bin", "Debug", "OSYS.A.exe"), evWinExe.OutputFileFor("Debug"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void No_output_type_gives_no_evidence()
    {
        string root = Path.Combine(Path.GetTempPath(), "eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "A");
            Directory.CreateDirectory(dir);
            string proj = WriteProj(dir, "A.csproj", """
                <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup>
                    <AssemblyName>OSYS.A</AssemblyName>
                  </PropertyGroup>
                  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
                    <OutputPath>bin\Debug\</OutputPath>
                  </PropertyGroup>
                </Project>
                """);
            var ev = new CsprojEvaluator().Evaluate(proj);
            Assert.Null(ev.OutputFileFor("Debug"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Sdk_style_gives_no_evidence()
    {
        string root = Path.Combine(Path.GetTempPath(), "eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "S");
            Directory.CreateDirectory(dir);
            string proj = Path.Combine(dir, "S.csproj");
            File.WriteAllText(proj, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <OutputType>Library</OutputType>
                  </PropertyGroup>
                </Project>
                """);
            var ev = new CsprojEvaluator().Evaluate(proj);
            Assert.Null(ev.OutputFileFor("Debug"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void An_unreadable_condition_carrying_an_output_path_gives_no_evidence()
    {
        string root = Path.Combine(Path.GetTempPath(), "eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "A");
            string proj = WriteRealisticProj(dir, """
                <PropertyGroup Condition=" '$(Configuration)' != 'Release' ">
                  <OutputPath>bin\NotRelease\</OutputPath>
                </PropertyGroup>
                """);
            var ev = new CsprojEvaluator().Evaluate(proj);
            Assert.True(ev.OutputPathUndecidable);
            Assert.Null(ev.OutputFileFor("Debug"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_property_in_the_output_path_gives_no_evidence()
    {
        string root = Path.Combine(Path.GetTempPath(), "eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "A");
            string proj = WriteRealisticProj(dir, """
                <PropertyGroup>
                  <OutputPath>$(SolutionDir)bin\Debug\</OutputPath>
                </PropertyGroup>
                """);
            var ev = new CsprojEvaluator().Evaluate(proj);
            Assert.Null(ev.OutputFileFor("Debug"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Hint_path_targets_resolve_relative_and_skip_properties()
    {
        string root = Path.Combine(Path.GetTempPath(), "eval-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "A");
            Directory.CreateDirectory(dir);
            string proj = WriteProj(dir, "A.csproj", """
                <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup><AssemblyName>OSYS.A</AssemblyName></PropertyGroup>
                  <ItemGroup>
                    <Reference Include="OSYS.B"><HintPath>..\B\bin\OSYS.B.dll</HintPath></Reference>
                    <Reference Include="OSYS.B.Dup"><HintPath>..\B\bin\OSYS.B.dll</HintPath></Reference>
                    <Reference Include="Absolute"><HintPath>C:\Absolute\Foo.dll</HintPath></Reference>
                    <Reference Include="WithProperty"><HintPath>$(SolutionDir)packages\X\X.dll</HintPath></Reference>
                  </ItemGroup>
                </Project>
                """);
            var ev = new CsprojEvaluator().Evaluate(proj);
            var targets = ev.HintPathTargets();

            Assert.Equal(2, targets.Count); // tekil: dup collapsed; property atlandi
            Assert.DoesNotContain(targets, t => t.Contains("SolutionDir", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(targets, t => string.Equals(
                t, Path.GetFullPath(Path.Combine(dir, "..", "B", "bin", "OSYS.B.dll")), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(targets, t => string.Equals(t, @"C:\Absolute\Foo.dll", StringComparison.OrdinalIgnoreCase));
            Assert.True(targets.SequenceEqual(targets.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))); // sirali
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
