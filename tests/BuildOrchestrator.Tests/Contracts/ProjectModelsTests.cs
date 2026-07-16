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

    [Fact]
    public void HintPathClass_serializes_camelCase()
    {
        string json = JsonSerializer.Serialize(HintPathClass.ExternalOsysPlatform, IpcJson.Options);
        Assert.Equal("\"externalOsysPlatform\"", json);
    }
}
