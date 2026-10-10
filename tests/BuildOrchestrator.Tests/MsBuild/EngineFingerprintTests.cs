using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BuildOrchestrator.Core.MsBuild;
using Xunit;

namespace BuildOrchestrator.Tests.MsBuild;

/// <summary>
/// [RESOLVE Faz 3/Task 3.2 — karar 6] Motor parmak izi: MSBuild.exe tam yolu + dosya sürümü + build argüman
/// sözleşmesinin (<see cref="MsBuildArguments.Build"/> çıktısının proje yolu DIŞINDAKİ kısmı) SHA-256'sı. Farklıysa
/// Resolve'un tur 1'inde grupta herkes gerekli sayılır. Testler sürümü doğrudan veren saf girişi kullanır; dosyadan
/// okuyan giriş ayrıca gerçek bir dosya ve olmayan bir yol üzerinden sınanır.
/// </summary>
public class EngineFingerprintTests
{
    private const string MsBuildExe = @"C:\VS\MSBuild\Current\Bin\amd64\MSBuild.exe";
    private const string Version = "17.14.8.25101";
    private const string TargetsPath = @"C:\cache\msbuild\wpf-temporary-assembly.targets";

    /// <summary>Bugünkü sözleşme: WPF targets argümanlı build listesi (Supervisor'ın takımı böyle kurar).</summary>
    private static IReadOnlyList<string> Today(string projectPath, string configuration) =>
        MsBuildArguments.Build(projectPath, configuration, customBeforeTargets: TargetsPath);

    private static string Fingerprint(Func<string, string, IReadOnlyList<string>> build) =>
        EngineFingerprint.Compute(MsBuildExe, Version, build);

    [Fact]
    public void The_same_input_yields_the_same_sha256_fingerprint()
    {
        Assert.Equal(Fingerprint(Today), Fingerprint(Today));
        Assert.Matches("^[0-9A-F]{64}$", Fingerprint(Today));
    }

    [Fact]
    public void A_changed_argument_list_changes_the_fingerprint()
    {
        string today = Fingerprint(Today);

        // WPF targets argümanı yok ya da başka bir yolda: sözleşme değişti → grupta herkes gerekli (kabul edilen sonuç).
        Assert.NotEqual(today, Fingerprint((p, c) => MsBuildArguments.Build(p, c)));
        Assert.NotEqual(today, Fingerprint((p, c) => MsBuildArguments.Build(p, c, customBeforeTargets: @"D:\other\wpf.targets")));
        // Öğe sınırı kayması ayırt edilir: ["-a", "-bc"] ile ["-ab", "-c"] aynı birleşimi üretmez.
        Assert.NotEqual(Fingerprint((p, _) => [p, "-a", "-bc"]), Fingerprint((p, _) => [p, "-ab", "-c"]));
    }

    [Fact]
    public void A_different_toolset_changes_the_fingerprint()
    {
        string today = Fingerprint(Today);

        Assert.NotEqual(today, EngineFingerprint.Compute(@"C:\VS2\MSBuild\Current\Bin\amd64\MSBuild.exe", Version, Today));
        Assert.NotEqual(today, EngineFingerprint.Compute(MsBuildExe, "17.14.9.1", Today));
        Assert.NotEqual(today, EngineFingerprint.Compute(MsBuildExe, null, Today));
    }

    /// <summary>
    /// [R3 final] Toolset girişi (<see cref="EngineFingerprint.ForToolset"/>) koordinatörün eskiden kendi kurduğu parmak izinin
    /// AYNI değerini üretir: MSBuild.exe yolu + dosya sürümü + invoker'ın koşturduğu build sözleşmesi (WPF targets argümanı
    /// dahil; <c>null</c> yol ⇒ argümansız liste). Koordinatör argüman listesini artık seçmez (<c>MsBuildArgumentsTests</c>
    /// guard'ı) ama değer değişmez — değişseydi kayıtlı her döngü üyesi bir kez "engine changed" sayılır, grupta herkes derlenirdi.
    /// </summary>
    [Fact]
    public void The_toolset_entry_yields_the_fingerprint_of_the_build_contract_with_the_same_targets()
    {
        Assert.Equal(EngineFingerprint.Compute(MsBuildExe, Today), EngineFingerprint.ForToolset(MsBuildExe, TargetsPath));
        Assert.Equal(EngineFingerprint.Compute(MsBuildExe, (p, c) => MsBuildArguments.Build(p, c)),
            EngineFingerprint.ForToolset(MsBuildExe, null));
        Assert.NotEqual(EngineFingerprint.ForToolset(MsBuildExe, TargetsPath), EngineFingerprint.ForToolset(MsBuildExe, null));
    }

    [Fact]
    public void The_project_path_does_not_enter_the_fingerprint()
    {
        // Proje yolu listenin ilk öğesidir ve dışarıda kalır: hangi projeyle istenirse istensin aynı motor aynı okunur.
        Assert.Equal(Fingerprint(Today), Fingerprint((p, c) => Today(@"D:\elsewhere\" + p + ".csproj", c)));
    }

    [Fact]
    public void The_file_reading_entry_uses_the_file_version_and_tolerates_a_missing_file()
    {
        string existing = typeof(EngineFingerprintTests).Assembly.Location;
        string? version = FileVersionInfo.GetVersionInfo(existing).FileVersion;
        Assert.NotNull(version);
        Assert.Equal(EngineFingerprint.Compute(existing, version, Today), EngineFingerprint.Compute(existing, Today));
        Assert.NotEqual(EngineFingerprint.Compute(existing, null, Today), EngineFingerprint.Compute(existing, Today));

        string missing = Path.Combine(Path.GetTempPath(), "bo-no-msbuild-" + Guid.NewGuid().ToString("N"), "MSBuild.exe");
        Assert.Equal(EngineFingerprint.Compute(missing, null, Today), EngineFingerprint.Compute(missing, Today));
    }
}
