using System.IO;
using System.Text.Json;

namespace BuildOrchestrator.Tests.App;

/// <summary>[yayın hattı · Task 1] Repo kökündeki dağıtım dosyaları: MIT lisansı (SignPath önkoşulu; public repo'da
/// lisanssız = hak verilmemiş), SDK bandını lokal ile CI'da eşitleyen global.json ve vpk'yı pinleyen tool manifest.</summary>
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
}
