using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using System.Text.Json;

namespace BuildOrchestrator.Tests.Contracts;

public class ProjectModelsTests
{
    [Fact]
    public void ProjectNode_round_trips_with_ipc_json_options()
    {
        var node = new ProjectNode("C:\\r\\A.csproj", "A", "C:\\r\\A.csproj",
            ["Sln1"], ["C:\\r\\B.csproj"], BuildOrder: 3, LayerIndex: null, LayerName: null,
            InCycle: false, WillBuild: true);
        string json = JsonSerializer.Serialize(node, IpcJson.Options);
        Assert.Contains("\"willBuild\":true", json); // camelCase
        var back = JsonSerializer.Deserialize<ProjectNode>(json, IpcJson.Options)!;
        Assert.Equal(node, back); // record value-equality
    }

    /// <summary>[Faz 3/Task 3] <c>FedOutputs</c> eşitliğe İÇERİKLE girer (liste alanı — JSON round-trip sonrası
    /// farklı örnek, <c>DepIssueRoots</c> ile aynı gerekçe); farklı liste ya da null ≠ boş liste eşit değildir.</summary>
    [Fact]
    public void BuildState_equality_includes_fed_outputs()
    {
        var state = new BuildState(@"C:\r\A.csproj", "sig", FedOutputs: [@"C:\lib\A.dll"]);
        string json = JsonSerializer.Serialize(state, IpcJson.Options);
        var back = JsonSerializer.Deserialize<BuildState>(json, IpcJson.Options)!;

        Assert.Equal(state, back);
        Assert.Equal(state.GetHashCode(), back.GetHashCode());
        Assert.NotEqual(state, state with { FedOutputs = [@"C:\lib2\A.dll"] });
        Assert.NotEqual(state with { FedOutputs = null }, state with { FedOutputs = [] });
        Assert.NotEqual(state.GetHashCode(), (state with { FedOutputs = [@"C:\lib2\A.dll"] }).GetHashCode());
    }

    [Fact]
    public void HintPathClass_serializes_camelCase()
    {
        string json = JsonSerializer.Serialize(HintPathClass.ExternalOsysPlatform, IpcJson.Options);
        Assert.Equal("\"externalOsysPlatform\"", json);
    }

    [Fact]
    public void BranchRef_round_trips_with_ipc_json_options()
    {
        var branch = new BranchRef("main", "abc123", true, false);
        string json = JsonSerializer.Serialize(branch, IpcJson.Options);
        Assert.Contains("\"isRemoteTracking\":false", json); // camelCase
        var back = JsonSerializer.Deserialize<BranchRef>(json, IpcJson.Options)!;
        Assert.Equal(branch, back);
    }

    [Fact]
    public void ExternalProject_round_trips_through_ipc_json()
    {
        // [DEĞİŞEN KURAL] Kart eskiden bir `vcs` alanı da taşıyordu ("git"/"tfvc") ve ayrı bir test o metnin
        // tel üzerinde METİN olarak gittiğini pinliyordu. TFVC kolu kaldırıldı: kartın taşıdığı tek şey yol.
        var external = new ExternalProject(@"D:\ext\mail\Mail.sln");
        string json = JsonSerializer.Serialize(external, IpcJson.Options);
        Assert.Contains("\"path\":", json);   // camelCase
        Assert.DoesNotContain("vcs", json);
        var back = JsonSerializer.Deserialize<ExternalProject>(json, IpcJson.Options)!;
        Assert.Equal(external, back);
    }

    /// <summary>Eski bir NDJSON satırı / ayar dosyası hâlâ çözülür: tanınmayan <c>vcs</c> alanı YOK SAYILIR.
    /// Bu, kullanıcının diskteki TFVC kartlı dosyasının uygulamayı düşürmemesinin garantisidir.</summary>
    [Fact]
    public void An_external_project_with_a_legacy_vcs_field_still_deserializes()
    {
        var back = JsonSerializer.Deserialize<ExternalProject>(
            """{"path":"D:\\ext\\mail\\Mail.sln","vcs":"tfvc"}""", IpcJson.Options)!;

        Assert.Equal(@"D:\ext\mail\Mail.sln", back.Path);
    }
}
