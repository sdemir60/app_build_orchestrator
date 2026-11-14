using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.ProcessControl;
using Xunit;

namespace BuildOrchestrator.Tests.Integration;

/// <summary>
/// [files-in-use · KABUL] Kilitli kopya kararı gerçek <c>MSBuild.exe</c> ve gerçek bir görüntü kilidiyle: eski stil (.NET Framework 4.8)
/// küçük bir kütüphane <c>PostBuildEvent</c>'iyle (<c>copy /y "$(TargetDir)$(TargetName).*" "&lt;paylaşılan&gt;\"</c>) paylaşılan bir
/// klasöre kopyalanır; o klasördeki DLL'i bir .NET Framework süreci (Windows PowerShell'in <c>Assembly.LoadFrom</c>'u) yükleyip tutar —
/// istemcinin kendisi gibi. Derleme motorun gerçek invoker'ıyla koşar (<see cref="MsBuildInvoker"/>, inner job), decorator onu sarar.
/// <list type="number">
/// <item>Ayar kapalı: kopya düşer (exit≠0), rapor <see cref="FilesInUseOutcome.Held"/>, paylaşılan DLL eski kalır — kilit gerçektir ve
/// tespit onu görür (testin kırmızı ön koşulu).</item>
/// <item>Ayar açık: başarı, rapor <see cref="FilesInUseOutcome.Replaced"/>, paylaşılan DLL derlemenin çıktısıyla bayt bayt aynı, eski
/// kopya <c>.in-use</c>'da <c>.old</c>.</item>
/// <item>Tutan süreç kapanınca süpürme eski kopyayı siler ve klasörü kaldırır.</item>
/// </list>
/// <para><b>Normal koşudan HARİÇTİR</b> (<c>[Trait("Category","Acceptance")]</c>); MSBuild ya da .NET Framework 4.8 hedef paketi yoksa
/// test ATLANIR (ortam eksiği — <see cref="WpfMiniFixture"/>'ın kuralı).</para>
/// </summary>
[Trait("Category", "Acceptance")]
public sealed class FilesInUseAcceptanceTests
{
    private const string Csproj = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project ToolsVersion="15.0" DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
          <PropertyGroup>
            <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
            <Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
            <ProjectGuid>{5A1D2E3B-0F6C-4C1E-9B4A-1C2D3E4F5A6B}</ProjectGuid>
            <OutputType>Library</OutputType>
            <RootNamespace>Acme.Lib</RootNamespace>
            <AssemblyName>Acme.Lib</AssemblyName>
            <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
            <FileAlignment>512</FileAlignment>
          </PropertyGroup>
          <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ">
            <DebugSymbols>true</DebugSymbols>
            <DebugType>full</DebugType>
            <Optimize>false</Optimize>
            <OutputPath>bin\Debug\</OutputPath>
          </PropertyGroup>
          <ItemGroup>
            <Reference Include="System" />
          </ItemGroup>
          <ItemGroup>
            <Compile Include="Class1.cs" />
          </ItemGroup>
          <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
          <PropertyGroup>
            <PostBuildEvent>copy /y "$(TargetDir)$(TargetName).*" "__SHARED__\"</PostBuildEvent>
          </PropertyGroup>
        </Project>
        """;

    private static void WriteSource(string projectDirectory, string version) =>
        File.WriteAllText(Path.Combine(projectDirectory, "Class1.cs"),
            $"namespace Acme.Lib {{ public static class Class1 {{ public static string Version() {{ return \"{version}\"; }} }} }}");

    /// <summary>Paylaşılan DLL'i yükleyip tutan .NET Framework süreci; "ready" yazınca kilit kurulmuştur, stdin kapanınca çıkar.</summary>
    private static async Task<Process> HoldAsync(string dll)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-Command",
            "$null = [Reflection.Assembly]::LoadFrom($env:ACME_HOLD_DLL); [Console]::Out.WriteLine('ready'); [Console]::Out.Flush(); "
            + "$null = [Console]::In.ReadLine()" })
            psi.ArgumentList.Add(arg);
        psi.Environment["ACME_HOLD_DLL"] = dll;
        var holder = Process.Start(psi)!;
        string? line = await holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal("ready", line);
        return holder;
    }

    [SkippableFact]
    public async Task A_dll_a_running_program_loaded_from_the_shared_folder_is_replaced_by_the_next_build()
    {
        string msbuild = await WpfMiniFixture.ResolveMsBuildOrSkipAsync();
        using var temp = new TempDir();
        string shared = Path.Combine(temp.Path, "Acme", "Client", "bin");
        string projectDirectory = Path.Combine(temp.Path, "src", "Acme.Lib");
        Directory.CreateDirectory(shared);
        Directory.CreateDirectory(projectDirectory);
        string project = Path.Combine(projectDirectory, "Acme.Lib.csproj");
        File.WriteAllText(project, Csproj.Replace("__SHARED__", shared));
        WriteSource(projectDirectory, "v1");
        string sharedDll = Path.Combine(shared, "Acme.Lib.dll");
        string builtDll = Path.Combine(projectDirectory, "bin", "Debug", "Acme.Lib.dll");
        var stash = new InUseStash(Path.Combine(temp.Path, "state", InUseStash.RecordFileName));
        var request = new MsBuildInvokeRequest(project, "Debug", projectDirectory, NeedsRestore: false);

        using var job = JobObject.CreateKillOnClose();
        var real = new MsBuildInvoker(job, msbuild);
        var lines = new ConcurrentQueue<string>();
        var first = await real.InvokeAsync(request, lines.Enqueue, CancellationToken.None);
        Skip.IfNot(first.ExitCode == 0 && File.Exists(sharedDll),
            "the .NET Framework 4.8 mini project does not build here: " + string.Join(" | ", lines.Where(l => l.Contains("error")).Take(3)));

        var reports = new ConcurrentQueue<FilesInUseReport>();
        IMsBuildInvoker Decorated(bool replace) => new InUseCopyInvoker(real, stash, replace, (_, report) => reports.Enqueue(report));
        using var holder = await HoldAsync(sharedDll);
        try
        {
            WriteSource(projectDirectory, "v2");
            byte[] before = File.ReadAllBytes(sharedDll);

            var off = await Decorated(replace: false).InvokeAsync(request, _ => { }, CancellationToken.None);

            Assert.NotEqual(0, off.ExitCode);
            var held = Assert.Single(reports);
            Assert.Equal(FilesInUseOutcome.Held, held.Outcome);
            Assert.Contains("Acme.Lib.dll", held.FileNames);
            Assert.Contains(held.Holders, h => h.ProcessId == holder.Id);
            Assert.Equal(before, File.ReadAllBytes(sharedDll));

            reports.Clear();
            var on = await Decorated(replace: true).InvokeAsync(request, _ => { }, CancellationToken.None);

            Assert.Equal(0, on.ExitCode);
            Assert.Equal(FilesInUseOutcome.Replaced, Assert.Single(reports).Outcome);
            Assert.Equal(File.ReadAllBytes(builtDll), File.ReadAllBytes(sharedDll));
            Assert.Contains(Directory.GetFiles(Path.Combine(shared, InUseStash.FolderName)),
                f => Path.GetFileName(f).StartsWith("Acme.Lib.dll~", StringComparison.Ordinal) && f.EndsWith(".old", StringComparison.Ordinal));
        }
        finally
        {
            holder.StandardInput.Close();
            await holder.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }

        var sweep = stash.Sweep();

        Assert.Equal(0, sweep.Kept);
        Assert.True(sweep.Removed >= 1);
        Assert.False(Directory.Exists(Path.Combine(shared, InUseStash.FolderName)));
    }
}
