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
    public void Worktree_round_trips_with_ipc_json_options_and_omits_null_diskSizeBytes()
    {
        var worktree = new Worktree("wt-1", "feature/x", @"D:\repo\.worktrees\wt-1", false, null);
        string json = JsonSerializer.Serialize(worktree, IpcJson.Options);
        Assert.DoesNotContain("diskSizeBytes", json); // WhenWritingNull
        var back = JsonSerializer.Deserialize<Worktree>(json, IpcJson.Options)!;
        Assert.Equal(worktree, back);

        var withSize = worktree with { DiskSizeBytes = 12345 };
        string jsonWithSize = JsonSerializer.Serialize(withSize, IpcJson.Options);
        Assert.Contains("\"diskSizeBytes\":12345", jsonWithSize);
    }
}
