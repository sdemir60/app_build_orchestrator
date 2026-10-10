using System.IO;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.MsBuild;
using Xunit;

namespace BuildOrchestrator.Tests.Integration;

/// <summary>
/// [A1 · A4 · KABUL] SDK-style projenin türetilen çıktı yolu (<see cref="EvaluatedProject.OutputFileFor"/>), gerçek
/// <c>MSBuild.exe</c>'nin çıktıyı yazdığı yerdir. WpfMini'nin SDK-style mini projesi (<c>net10.0-windows</c>, Library,
/// <c>AssemblyName</c> MiniSdk) geçici bir kopyada motorun kendi argüman planıyla (<see cref="MsBuildArguments.PlanFor"/>:
/// restore, ardından <c>-p:Configuration=Debug</c> ile build) derlenir; değerlendiricinin yolu
/// <c>bin\Debug\net10.0-windows\MiniSdk.dll</c>'dir ve o dosya diskte vardır. Kopya fixture'ın boş
/// <c>Directory.Build.props</c>'unu da taşır: düzeni oynatmayan en yakın props odur, yukarı arama orada durur.
///
/// <para><b>Normal koşudan HARİÇTİR</b> (<c>[Trait("Category","Acceptance")]</c>): MSBuild çözülemezse ya da mini proje bu
/// makinede derlenemezse (.NET 10 SDK'sını taşıyabilen bir MSBuild yok) test ATLANIR — ortam eksiği
/// (<see cref="WpfTemporaryAssemblyAcceptanceTests"/> ile aynı kural). Kırmızı yalnız türetilen yol ile gerçek çıktı
/// ayrıştığında olur (gösterildi: <c>OutputFileFor</c>'un SDK dalı <c>null</c> dönünce düşer).</para>
/// </summary>
[Trait("Category", "Acceptance")]
public sealed class SdkOutputLayoutAcceptanceTests
{
    [SkippableFact]
    public async Task The_derived_sdk_output_path_is_where_msbuild_writes_the_output()
    {
        string msbuild = await WpfMiniFixture.ResolveMsBuildOrSkipAsync();
        using var scratch = new TempDir();
        WpfMiniFixture.CopyTo(scratch.Path);
        string csproj = Path.Combine(scratch.Path, "Sdk", "MiniSdk.csproj");
        string projectDirectory = Path.GetDirectoryName(csproj)!;

        var (restore, build) = MsBuildArguments.PlanFor(new MsBuildInvokeRequest(csproj, "Debug", projectDirectory, NeedsRestore: true));
        foreach (var arguments in new[] { restore!, build })
        {
            var result = await WpfMiniFixture.RunAsync(msbuild, arguments, projectDirectory);
            Skip.IfNot(result.Success, "the SDK-style mini project does not build here: " + WpfMiniFixture.ErrorLines(result));
        }

        string? output = new CsprojEvaluator().Evaluate(csproj).OutputFileFor("Debug");

        Assert.NotNull(output);
        Assert.Equal(Path.Combine(projectDirectory, "bin", "Debug", "net10.0-windows", "MiniSdk.dll"), output);
        Assert.True(File.Exists(output), "a real build left no file at the derived output path: " + output);
    }
}
