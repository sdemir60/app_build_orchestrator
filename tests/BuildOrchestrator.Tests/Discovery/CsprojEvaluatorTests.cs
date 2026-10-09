using System;
using System.IO;
using System.Linq;
using BuildOrchestrator.Core.Discovery;

namespace BuildOrchestrator.Tests.Discovery;

/// <summary>
/// Ham-XML csproj değerlendirmesi: öğeler, hedef framework, legacy <c>OutputPath</c> okuması ve SDK-style projenin SDK
/// varsayılan çıktı yolu (ARCHITECTURE §6.2).
/// <para><b>Ölçüm (gerçek OSYS, 2026-10-09 — A1):</b> SDK-style projeye varsayılan çıktı yolu türetilince
/// <c>OSYS.Types.General</c>'daki gövde değişikliğinde Build 7 yerine 2 proje derledi, koşu 19,0 sn'den 7,7 sn'ye indi: 4 SDK-style
/// PRM projesinin bağımlıları kapıdan atlandı, <c>UI.DMS</c> grubu 17/17 taşındı (ayrıntı:
/// .claude/outputs/2026-10-09-20-19-sdk-output-layout-measurement.md).</para>
/// </summary>
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

    // ---------------------------------------------------------------- [A1-A2] SDK-style: SDK'nın varsayılan çıktı düzeni

    /// <summary>SDK-style testlerinin kökü; yukarı arama orada durur (<see cref="SdkFixture.WriteSearchStoppers"/>).</summary>
    private static string NewSdkRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "eval-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        SdkFixture.WriteSearchStoppers(root);
        return root;
    }

    /// <summary>Tek PropertyGroup'lu SDK-style proje; <paramref name="projectLevel"/> kök altına, grubun ardına yazılır.</summary>
    private static string WriteSdkProj(string dir, string name, string properties, string projectLevel = "",
        string sdk = "Microsoft.NET.Sdk") =>
        WriteProj(dir, name, $$"""
            <Project Sdk="{{sdk}}">
              <PropertyGroup>
                {{properties}}
              </PropertyGroup>
              {{projectLevel}}
            </Project>
            """);

    /// <summary>[A1] Düzeni oynatan hiçbir ayar yokken SDK-style proje SDK'nın varsayılan yolunu alır:
    /// <c>&lt;proje&gt;\bin\&lt;Configuration&gt;\&lt;TargetFramework&gt;\&lt;AssemblyName&gt;.dll</c> (AssemblyName yoksa dosya adı).
    /// <para><b>[DEĞİŞEN KURAL — A1 · kullanıcı kararı 2026-10-09]</b> Eski iddia (<c>Sdk_style_gives_no_evidence</c>): SDK-style
    /// proje kanıtsızdır — yol türetilmez. Değişme gerekçesi (ölçüm 2026-10-09, surface-gate-measurement): OSYS'teki 4 SDK-style
    /// PRM projesi yüzey kapısını ve taşımayı kör ediyordu — Types değişikliğinde 7 derlemenin 5'i; SDK'nın varsayılan düzeni
    /// hiçbir ayar onu oynatmıyorsa belirlidir.</para></summary>
    [Fact]
    public void Sdk_style_project_with_the_default_layout_gives_the_sdk_default_output()
    {
        string root = NewSdkRoot();
        try
        {
            string dir = Path.Combine(root, "S");
            string proj = WriteSdkProj(dir, "S.csproj", "<TargetFramework>net10.0</TargetFramework><OutputType>Library</OutputType>");
            var ev = new CsprojEvaluator().Evaluate(proj);
            Assert.Equal(Path.Combine(dir, "bin", "Debug", "net10.0", "S.dll"), ev.OutputFileFor("Debug"));
            Assert.Equal(Path.Combine(dir, "bin", "Release", "net10.0", "S.dll"), ev.OutputFileFor("Release"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>[A2] <c>OutputType</c> yoksa SDK projeyi <c>Library</c> sayar — OSYS'in PRM projelerinin biçimi (AssemblyName açık,
    /// OutputType yok, net46).</summary>
    [Fact]
    public void Sdk_style_project_without_an_output_type_is_a_library()
    {
        string root = NewSdkRoot();
        try
        {
            string dir = Path.Combine(root, "OSYS.Types.PRM");
            string proj = WriteSdkProj(dir, "OSYS.Types.PRM.csproj",
                "<TargetFramework>net46</TargetFramework><AssemblyName>OSYS.Types.PRM</AssemblyName>");
            var ev = new CsprojEvaluator().Evaluate(proj);
            Assert.Equal(Path.Combine(dir, "bin", "Debug", "net46", "OSYS.Types.PRM.dll"), ev.OutputFileFor("Debug"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>[A2] Düzeni oynatan bir ayar varsa yol YOK (§7.6: güvenle türetilemiyorsa yaklaşık değil, hiç) — hangi
    /// PropertyGroup'ta olursa olsun (koşullu grup, Choose/When dahil): çıktı yolunu değiştiren MSBuild ayarları, çoklu hedef,
    /// Exe/WinExe, çözülmemiş bir özellik. [Ruling] Yolun kendisini değiştirenler de sayılır — platform klasörü
    /// (<c>Platform</c>, <c>PlatformName</c>, <c>AppendPlatformToOutputPath</c>), çıktı dosyasının adı ve uzantısı (<c>TargetName</c>,
    /// <c>TargetExt</c>), moniker'ı TargetFramework'ten değil kendinden türeten <c>TargetFrameworkVersion</c>, hangi Directory.Build
    /// dosyasının içe alınacağını değiştiren <c>DirectoryBuildPropsPath</c>/<c>DirectoryBuildTargetsPath</c> — ve içine bakılamayan bir
    /// <c>Import</c> ya da ek <c>Sdk</c>. [Final review I1] Yolun csproj'dan okunan girdileri (<c>AssemblyName</c>,
    /// <c>TargetFramework</c>, <c>OutputType</c>) en çok bir kez, koşulsuz, üst düzey bir PropertyGroup'ta ve MSBuild'in okuduğu
    /// yazımla bulunmalı: değerlendirici ilk dolu değeri alır, MSBuild koşulları tartar ve son yazanı alır — biri ayrışırsa
    /// türetilen dosya hiç oluşmaz ve proje kalıcı olarak "output missing" okurdu.</summary>
    [Theory]
    [InlineData(@"<TargetFramework>net10.0</TargetFramework><OutputPath>out\</OutputPath>", "")]
    [InlineData(@"<TargetFramework>net10.0</TargetFramework><OutDir>out\</OutDir>", "")]
    [InlineData(@"<TargetFramework>net10.0</TargetFramework><BaseOutputPath>build\</BaseOutputPath>", "")]
    [InlineData("<TargetFramework>net10.0</TargetFramework><AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>", "")]
    [InlineData("<TargetFramework>net10.0</TargetFramework><AppendRuntimeIdentifierToOutputPath>true</AppendRuntimeIdentifierToOutputPath>", "")]
    [InlineData("<TargetFramework>net10.0</TargetFramework><RuntimeIdentifier>win-x64</RuntimeIdentifier>", "")]
    [InlineData("<TargetFramework>net10.0</TargetFramework><RuntimeIdentifiers>win-x64</RuntimeIdentifiers>", "")]
    [InlineData("<TargetFramework>net10.0</TargetFramework><UseArtifactsOutput>true</UseArtifactsOutput>", "")]
    [InlineData("<TargetFramework>net10.0</TargetFramework><ArtifactsPath>art</ArtifactsPath>", "")]
    [InlineData("<TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>", "")]
    [InlineData("<TargetFramework>net10.0</TargetFramework><OutputType>WinExe</OutputType>", "")]
    [InlineData("<TargetFrameworks>net46;net48</TargetFrameworks>", "")]
    [InlineData("<TargetFramework>$(Tfm)</TargetFramework>", "")]
    [InlineData("<TargetFramework>net10.0</TargetFramework><AssemblyName>$(Prefix).S</AssemblyName>", "")]
    [InlineData("<TargetFramework>net10.0</TargetFramework><Platform>x64</Platform>", "")]
    [InlineData("<TargetFramework>net10.0</TargetFramework><TargetName>Other</TargetName>", "")]
    [InlineData("<TargetFramework>net10.0</TargetFramework><TargetExt>.bin</TargetExt>", "")]
    [InlineData("<TargetFramework>net48</TargetFramework><TargetFrameworkVersion>v4.8</TargetFrameworkVersion>", "")]
    [InlineData("<TargetFramework>net10.0</TargetFramework>", @"<Import Project=""..\shared.props"" />")]
    [InlineData("<TargetFramework>net10.0</TargetFramework>", @"<Sdk Name=""My.Layout.Sdk"" Version=""1.0.0"" />")]
    [InlineData("<TargetFramework>net10.0</TargetFramework>",
        @"<PropertyGroup Condition=""'$(Configuration)' == 'Release'""><OutputPath>rel\</OutputPath></PropertyGroup>")]
    [InlineData("<TargetFramework>net10.0</TargetFramework>",
        @"<Choose><When Condition=""'$(CI)' == 'true'""><PropertyGroup><OutDir>ci\</OutDir></PropertyGroup></When></Choose>")]
    [InlineData("<TargetFramework>net10.0</TargetFramework><AppendPlatformToOutputPath>true</AppendPlatformToOutputPath>", "")]
    [InlineData("<TargetFramework>net10.0</TargetFramework><PlatformName>x64</PlatformName>", "")]
    [InlineData(@"<TargetFramework>net10.0</TargetFramework><DirectoryBuildTargetsPath>..\custom.targets</DirectoryBuildTargetsPath>", "")]
    [InlineData(@"<TargetFramework>net10.0</TargetFramework><DirectoryBuildPropsPath>..\custom.props</DirectoryBuildPropsPath>", "")]
    [InlineData(@"<TargetFramework Condition=""'$(OS)' != 'Windows_NT'"">net8.0</TargetFramework><TargetFramework Condition=""'$(OS)' == 'Windows_NT'"">net8.0-windows</TargetFramework>", "")]
    [InlineData("<TargetFramework>net46</TargetFramework><TargetFramework>net48</TargetFramework>", "")]
    [InlineData("", @"<PropertyGroup Condition=""'$(Configuration)' == 'Debug'""><TargetFramework>net48</TargetFramework></PropertyGroup>")]
    [InlineData("<TargetFramework>net10.0</TargetFramework>",
        @"<Choose><When Condition=""'$(CI)' == 'true'""><PropertyGroup><TargetFramework>net48</TargetFramework></PropertyGroup></When></Choose>")]
    [InlineData(@"<TargetFramework>net10.0</TargetFramework><AssemblyName Condition=""'$(Configuration)' == 'Release'"">S.Release</AssemblyName>", "")]
    [InlineData(@"<TargetFramework>net10.0</TargetFramework><OutputType Condition=""'$(Configuration)' == 'Debug'"">Library</OutputType>", "")]
    [InlineData("<TargetFramework>net10.0</TargetFramework><assemblyName>Other</assemblyName>", "")]
    public void Sdk_style_project_with_an_overridden_layout_gives_no_evidence(string properties, string projectLevel)
    {
        string root = NewSdkRoot();
        try
        {
            string proj = WriteSdkProj(Path.Combine(root, "S"), "S.csproj", properties, projectLevel);
            Assert.Null(new CsprojEvaluator().Evaluate(proj).OutputFileFor("Debug"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>[A2] Projenin klasöründen yukarı EN YAKIN <c>Directory.Build.props</c> ya da <c>.targets</c> düzeni oynatıyorsa yol
    /// YOK: aynı ayarlardan biri, içine bakılamayan bir <c>Import</c>, okunamayan (bozuk) dosya. [Ruling] Yolun csproj'dan okunan
    /// girdileri (<c>AssemblyName</c>, <c>OutputType</c>, <c>TargetFramework</c>) orada tanımlıysa da: props csproj'da olmayanın
    /// yerine geçer, targets csproj'unkini ezer.</summary>
    [Theory]
    [InlineData("Directory.Build.props", @"<Project><PropertyGroup><OutputPath>out\</OutputPath></PropertyGroup></Project>")]
    [InlineData("Directory.Build.props", @"<Project><Import Project=""x.props"" /></Project>")]
    [InlineData("Directory.Build.props", @"<Project><PropertyGroup><BaseOutputPath>build\</BaseOutputPath></PropertyGroup></Project>")]
    [InlineData("Directory.Build.targets", @"<Project><PropertyGroup><OutputPath>out\</OutputPath></PropertyGroup></Project>")]
    [InlineData("Directory.Build.targets", @"<Project><Import Project=""x.targets"" /></Project>")]
    [InlineData("Directory.Build.props", "<Project><PropertyGroup><AssemblyName>Company.S</AssemblyName></PropertyGroup></Project>")]
    [InlineData("Directory.Build.props", "<Project><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>")]
    [InlineData("Directory.Build.targets", "<Project><PropertyGroup><TargetFramework>net48</TargetFramework></PropertyGroup></Project>")]
    [InlineData("Directory.Build.props", "<Project><PropertyGroup>")]
    public void A_directory_build_props_that_moves_the_output_gives_no_evidence(string fileName, string content)
    {
        string root = NewSdkRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, fileName), content);
            string proj = WriteSdkProj(Path.Combine(root, "S"), "S.csproj", "<TargetFramework>net10.0</TargetFramework>");
            Assert.Null(new CsprojEvaluator().Evaluate(proj).OutputFileFor("Debug"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>[A2] Düzene dokunmayan bir props yolu bozmaz — OSYS'in <c>Common\PRM\Directory.Build.props</c>'u yalnız
    /// <c>EnableSourceControlManagerQueries</c> taşır.</summary>
    [Fact]
    public void A_directory_build_props_that_leaves_the_layout_alone_keeps_the_default()
    {
        string root = NewSdkRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"), """
                <Project>
                  <PropertyGroup>
                    <EnableSourceControlManagerQueries>false</EnableSourceControlManagerQueries>
                  </PropertyGroup>
                </Project>
                """);
            string dir = Path.Combine(root, "S");
            string proj = WriteSdkProj(dir, "S.csproj", "<TargetFramework>net46</TargetFramework>");
            Assert.Equal(Path.Combine(dir, "bin", "Debug", "net46", "S.dll"), new CsprojEvaluator().Evaluate(proj).OutputFileFor("Debug"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>[A2] Yalnız EN YAKIN props sayılır (MSBuild'in kuralı): proje klasöründe yoksa yukarı yürünür ve ilk bulunan karar
    /// verir — iki seviye yukarıdaki düzeni oynatan props <c>A</c>'yı kanıtsız bırakır; <c>C</c>'nin kendi dalında daha yakın,
    /// düzene dokunmayan bir props vardır ve kökteki onu ilgilendirmez.</summary>
    [Fact]
    public void Only_the_nearest_directory_build_props_counts()
    {
        string root = NewSdkRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"),
                @"<Project><PropertyGroup><OutputPath>out\</OutputPath></PropertyGroup></Project>");
            string a = WriteSdkProj(Path.Combine(root, "x", "y", "A"), "A.csproj", "<TargetFramework>net46</TargetFramework>");
            Directory.CreateDirectory(Path.Combine(root, "c"));
            File.WriteAllText(Path.Combine(root, "c", "Directory.Build.props"), "<Project><PropertyGroup><LangVersion>latest</LangVersion></PropertyGroup></Project>");
            string cDir = Path.Combine(root, "c", "d", "C");
            string c = WriteSdkProj(cDir, "C.csproj", "<TargetFramework>net46</TargetFramework>");

            Assert.Null(new CsprojEvaluator().Evaluate(a).OutputFileFor("Debug"));
            Assert.Equal(Path.Combine(cDir, "bin", "Debug", "net46", "C.dll"), new CsprojEvaluator().Evaluate(c).OutputFileFor("Debug"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>[Ruling] Varsayılan düzen .NET SDK'sınındır (<c>Microsoft.NET.Sdk</c> ve <c>Microsoft.NET.Sdk.*</c>): başka bir SDK
    /// (ör. hiç derlemeyen <c>Microsoft.Build.NoTargets</c>), sürüm sabitli ya da birden çok SDK'lı proje kanıtsız kalır — yoksa
    /// var olmayan bir çıktı "output missing" ile projeyi her koşuda derletirdi.</summary>
    [Theory]
    [InlineData("Microsoft.NET.Sdk.WindowsDesktop", true)]
    [InlineData("Microsoft.NET.Sdk.Razor", true)]
    [InlineData("Microsoft.Build.NoTargets/3.7.0", false)]
    [InlineData("MSBuild.Sdk.Extras/3.0.44", false)]
    [InlineData("Microsoft.NET.Sdk;My.Custom.Sdk", false)]
    public void Only_the_dotnet_sdk_has_a_default_layout(string sdk, bool derivable)
    {
        string root = NewSdkRoot();
        try
        {
            string dir = Path.Combine(root, "S");
            string proj = WriteSdkProj(dir, "S.csproj", "<TargetFramework>net10.0</TargetFramework>", sdk: sdk);
            Assert.Equal(derivable ? Path.Combine(dir, "bin", "Debug", "net10.0", "S.dll") : null,
                new CsprojEvaluator().Evaluate(proj).OutputFileFor("Debug"));
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
