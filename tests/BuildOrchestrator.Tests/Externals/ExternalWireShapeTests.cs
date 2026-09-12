using System.Text.Json;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// Harici projelerin tel üzerindeki yüzeyi: komutlara eklenen liste (yalnız yol) ve node'a eklenen harici
/// rozeti. Alanlar KUYRUKTA ve varsayılan değerlidir — bu alanları hiç yazmayan eski NDJSON satırları
/// çözülmeye devam eder.
/// </summary>
public class ExternalWireShapeTests
{
    private static readonly ExternalProject Mail = new(@"D:\ext\mail");

    [Fact]
    public void Sync_command_carries_the_external_list()
    {
        var command = new SyncWorkspaceCommand(@"D:\repo", "main", ExternalProjects: [Mail]);

        string json = JsonSerializer.Serialize<IpcCommand>(command, IpcJson.Options);
        var back = (SyncWorkspaceCommand)JsonSerializer.Deserialize<IpcCommand>(json, IpcJson.Options)!;

        // [DEĞİŞEN KURAL] Eski iddia: kart telde bir `vcs` METNİ taşıyordu. TFVC kolu kaldırıldı —
        // kartın taşıdığı tek şey yol; anahtar artık hiç yazılmaz.
        Assert.DoesNotContain("\"vcs\"", json);
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
    public void A_command_written_before_the_update_flag_existed_still_updates_externals()
    {
        // Bayrağın varsayılanı true: alanı hiç yazmayan eski bir satır, özelliğin bugünkü davranışını
        // (her Build'den önce güncelle) korumalı — false'a düşseydi sessizce bayat kaynak derlenirdi.
        const string legacy = """
            {"type":"startRun","runId":"run-1","mode":"build","rootPath":"D:\\repo","configuration":"Debug","parallelism":4}
            """;

        var back = (StartRunCommand)JsonSerializer.Deserialize<IpcCommand>(legacy, IpcJson.Options)!;

        Assert.True(back.UpdateExternals);
    }

    [Fact]
    public void The_update_flag_round_trips_when_it_is_turned_off()
    {
        var command = new StartRunCommand("run-1", RunMode.Build, @"D:\repo", "Debug", 4,
            ExternalProjects: [Mail], UpdateExternals: false);

        string json = JsonSerializer.Serialize<IpcCommand>(command, IpcJson.Options);
        var back = (StartRunCommand)JsonSerializer.Deserialize<IpcCommand>(json, IpcJson.Options)!;

        Assert.False(back.UpdateExternals);
    }

    /// <summary>
    /// <b>Eski iddia:</b> rozet bir <c>externalVcs</c> alanıydı ve tel üzerinde METİN (<c>"git"</c>/
    /// <c>"tfvc"</c>) taşıyordu. TFVC kolu kaldırıldı: taşınacak bir DEĞER kalmadı, soru "harici mi"ye indi ve
    /// alan <c>bool IsExternal</c> oldu.
    /// </summary>
    [Fact]
    public void Project_node_round_trips_its_external_badge()
    {
        var node = ExternalNode(external: true);

        string json = JsonSerializer.Serialize(node, IpcJson.Options);
        var back = JsonSerializer.Deserialize<ProjectNode>(json, IpcJson.Options)!;

        Assert.Contains("\"isExternal\":true", json);
        Assert.True(back.IsExternal);
        Assert.Equal(node, back);
    }

    [Fact]
    public void A_node_without_the_badge_is_an_ordinary_project()
    {
        var ordinary = new ProjectNode(@"C:\r\A.csproj", "A", @"C:\r\A.csproj", ["Sln1"], [], 0, null, null, false, null);

        string json = JsonSerializer.Serialize(ordinary, IpcJson.Options);
        var back = JsonSerializer.Deserialize<ProjectNode>(json, IpcJson.Options)!;

        Assert.False(back.IsExternal);
        Assert.Equal(ordinary, back);
    }

    /// <summary>Rozet taşımayan ESKİ bir NDJSON satırı da çözülür ve sıradan bir proje olur — alan sona ve
    /// varsayılanlı eklenmiş olmasının tek görünür sonucu budur.</summary>
    [Fact]
    public void A_legacy_node_line_without_the_badge_deserializes_as_ordinary()
    {
        var back = JsonSerializer.Deserialize<ProjectNode>(
            """{"id":"/r/A.csproj","name":"A","projectPath":"/r/A.csproj","solutionNames":[],"dependencies":[],"buildOrder":0,"inCycle":false}""",
            IpcJson.Options)!;

        Assert.False(back.IsExternal);
    }

    [Fact]
    public void Project_node_equality_includes_the_external_badge()
    {
        // ProjectNode'un Equals'ı ELLE yazılmıştır (liste alanları yüzünden); yeni bir alan oraya
        // eklenmezse sessizce yutulur ve iki farklı node eşit görünür.
        Assert.NotEqual(ExternalNode(external: true), ExternalNode(external: false));
        Assert.Equal(ExternalNode(external: true), ExternalNode(external: true));
    }

    private static ProjectNode ExternalNode(bool external) => new(
        @"D:\ext\mail\Mail.sln", "Mail", @"D:\ext\mail\Mail.sln", ["Mail.sln"], [],
        BuildOrder: 0, LayerIndex: -1, LayerName: "External", InCycle: false, WillBuild: true,
        WillBuildReason: WillBuildReason.NeverBuilt, IsExternal: external);
}
