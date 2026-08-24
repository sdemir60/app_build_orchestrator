using System.Text.Json;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// Harici projelerin tel üzerindeki yüzeyi: komutlara eklenen liste ve node'a eklenen VCS rozeti. Alanlar
/// KUYRUKTA ve varsayılan değerlidir — bu alanları hiç yazmayan eski NDJSON satırları çözülmeye devam eder.
/// </summary>
public class ExternalWireShapeTests
{
    private static readonly ExternalProject Mail = new("Mail", @"D:\ext\mail", @"D:\ext\mail\Mail.sln");

    [Fact]
    public void Sync_command_carries_the_external_list()
    {
        var command = new SyncWorkspaceCommand(@"D:\repo", "main", ExternalProjects: [Mail]);

        string json = JsonSerializer.Serialize<IpcCommand>(command, IpcJson.Options);
        var back = (SyncWorkspaceCommand)JsonSerializer.Deserialize<IpcCommand>(json, IpcJson.Options)!;

        Assert.Equal([Mail], back.ExternalProjects);
    }

    [Fact]
    public void A_sync_command_written_before_externals_existed_still_parses()
    {
        const string legacy = """{"type":"syncWorkspace","rootPath":"D:\\repo","branch":"main"}""";

        var back = (SyncWorkspaceCommand)JsonSerializer.Deserialize<IpcCommand>(legacy, IpcJson.Options)!;

        Assert.Null(back.ExternalProjects);
    }

    [Fact]
    public void Start_run_command_carries_the_external_list()
    {
        var command = new StartRunCommand("run-1", RunMode.Build, @"D:\repo", "Debug", 4, ExternalProjects: [Mail]);

        string json = JsonSerializer.Serialize<IpcCommand>(command, IpcJson.Options);
        var back = (StartRunCommand)JsonSerializer.Deserialize<IpcCommand>(json, IpcJson.Options)!;

        Assert.Equal([Mail], back.ExternalProjects);
    }

    [Fact]
    public void A_start_run_command_written_before_externals_existed_still_parses()
    {
        const string legacy = """
            {"type":"startRun","runId":"run-1","mode":"build","rootPath":"D:\\repo","configuration":"Debug","parallelism":4}
            """;

        var back = (StartRunCommand)JsonSerializer.Deserialize<IpcCommand>(legacy, IpcJson.Options)!;

        Assert.Null(back.ExternalProjects);
    }

    [Fact]
    public void Project_node_round_trips_its_vcs_badge()
    {
        var node = ExternalNode(VcsKind.Tfvc);

        string json = JsonSerializer.Serialize(node, IpcJson.Options);
        var back = JsonSerializer.Deserialize<ProjectNode>(json, IpcJson.Options)!;

        Assert.Contains("\"externalVcs\":\"tfvc\"", json);
        Assert.Equal(VcsKind.Tfvc, back.ExternalVcs);
        Assert.Equal(node, back);
    }

    [Fact]
    public void A_node_without_the_badge_is_an_ordinary_project()
    {
        var ordinary = new ProjectNode(@"C:\r\A.csproj", "A", @"C:\r\A.csproj", ["Sln1"], [], 0, null, null, false, null);

        string json = JsonSerializer.Serialize(ordinary, IpcJson.Options);

        Assert.DoesNotContain("externalVcs", json); // WhenWritingNull
        Assert.Null(JsonSerializer.Deserialize<ProjectNode>(json, IpcJson.Options)!.ExternalVcs);
    }

    [Fact]
    public void Project_node_equality_includes_the_vcs_badge()
    {
        // ProjectNode'un Equals'ı ELLE yazılmıştır (liste alanları yüzünden); yeni bir alan oraya
        // eklenmezse sessizce yutulur ve iki farklı node eşit görünür.
        Assert.NotEqual(ExternalNode(VcsKind.Git), ExternalNode(VcsKind.Tfvc));
        Assert.NotEqual(ExternalNode(VcsKind.Git), ExternalNode(null));
        Assert.Equal(ExternalNode(VcsKind.Git), ExternalNode(VcsKind.Git));
    }

    private static ProjectNode ExternalNode(VcsKind? vcs) => new(
        @"D:\ext\mail\Mail.sln", "Mail", @"D:\ext\mail\Mail.sln", ["Mail.sln"], [],
        BuildOrder: 0, LayerIndex: -1, LayerName: "External", InCycle: false, WillBuild: true,
        WillBuildReason: WillBuildReason.NeverBuilt, ExternalVcs: vcs);
}
