using System.IO;
using System.Text.Json;

namespace BuildOrchestrator.Tests.App;

/// <summary>[yayın hattı · Task 1, 5] Repo kökündeki dağıtım dosyaları: MIT lisansı (SignPath önkoşulu; public repo'da
/// lisanssız = hak verilmemiş), SDK bandını lokal ile CI'da eşitleyen global.json, vpk'yı pinleyen tool manifest ve
/// GitHub Actions workflow'ları (ci.yml build+test, release.yml tag ile yayın).</summary>
public class RepoHygieneTests
{
    [Fact]
    public void The_repository_carries_an_MIT_licence()
    {
        string text = File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, "LICENSE"));
        Assert.StartsWith("MIT License", text, StringComparison.Ordinal);
        Assert.Contains("Copyright (c) 2026 Delta Yazılım", text, StringComparison.Ordinal);
        Assert.Contains("THE SOFTWARE IS PROVIDED \"AS IS\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void global_json_pins_the_sdk_band_and_rolls_forward_within_it()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, "global.json")));
        var sdk = doc.RootElement.GetProperty("sdk");
        Assert.StartsWith("10.0.", sdk.GetProperty("version").GetString(), StringComparison.Ordinal);
        Assert.Equal("latestFeature", sdk.GetProperty("rollForward").GetString());
    }

    [Fact]
    public void The_tool_manifest_pins_vpk()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, ".config", "dotnet-tools.json")));
        var vpk = doc.RootElement.GetProperty("tools").GetProperty("vpk");
        Assert.Matches(@"^\d+\.\d+\.\d+$", vpk.GetProperty("version").GetString());
        Assert.Equal("vpk", vpk.GetProperty("commands")[0].GetString());
    }

    private static string Workflow(string name) =>
        File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, ".github", "workflows", name));

    [Fact]
    public void CI_builds_and_tests_main_on_a_pinned_windows_image_and_is_callable_by_the_release()
    {
        string ci = Workflow("ci.yml");
        Assert.Contains("runs-on: windows-2025", ci, StringComparison.Ordinal);   // windows-latest sürüklenmez
        Assert.Contains("workflow_call:", ci, StringComparison.Ordinal);          // release.yml build+test'i buradan alır (kopya yok)
        Assert.Contains("global-json-file: global.json", ci, StringComparison.Ordinal);
        Assert.Contains("Category!=Acceptance&Category!=LocalOnly", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("vpk", ci, StringComparison.Ordinal);               // CI yayın yapmaz
    }

    [Fact]
    public void The_release_workflow_runs_on_version_tags_reuses_CI_and_publishes_through_the_package_script()
    {
        string release = Workflow("release.yml");
        Assert.Contains("tags: ['v*']", release, StringComparison.Ordinal);
        Assert.Contains("contents: write", release, StringComparison.Ordinal);
        Assert.Contains("uses: ./.github/workflows/ci.yml", release, StringComparison.Ordinal);
        Assert.Contains("scripts/release-guard.ps1", release, StringComparison.Ordinal);
        Assert.Contains("scripts/package.ps1", release, StringComparison.Ordinal);
        Assert.Contains("vpk upload github", release, StringComparison.Ordinal);
        Assert.Contains("gh release edit", release, StringComparison.Ordinal);     // gövde = CHANGELOG bölümü
        Assert.DoesNotContain("dotnet publish", release, StringComparison.Ordinal); // publish komutunun tek sahibi package.ps1
    }
}
