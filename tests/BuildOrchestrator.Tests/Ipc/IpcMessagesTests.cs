using System.Text.Json;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.Ipc;

public class IpcMessagesTests
{
    [Fact]
    public void Command_roundtrip_preserves_type_and_payload()
    {
        IpcCommand cmd = new StopRunCommand("run-1", StopKind.Hard);
        string json = JsonSerializer.Serialize(cmd, IpcJson.Options);
        Assert.Contains("\"type\":\"stopRun\"", json);
        var back = Assert.IsType<StopRunCommand>(JsonSerializer.Deserialize<IpcCommand>(json, IpcJson.Options));
        Assert.Equal(StopKind.Hard, back.Kind);
    }

    [Fact]
    public void Event_roundtrip_all_types()
    {
        IpcEvent[] events = [ new EngineReadyEvent(123, "1.0"), new PongEvent(7), new ErrorEvent("x", "y"),
            new RunStoppedEvent("r", true), new ProjectLogChunkEvent("p", 0, "t", false, 0), new DebugChildrenSpawnedEvent([1, 2]) ];
        foreach (var e in events)
        {
            var back = JsonSerializer.Deserialize<IpcEvent>(JsonSerializer.Serialize(e, IpcJson.Options), IpcJson.Options);
            if (e is DebugChildrenSpawnedEvent debugEvent)
            {
                Assert.Equal(debugEvent.Pids, ((DebugChildrenSpawnedEvent)back!).Pids);
            }
            else
            {
                Assert.Equal(e, back);
            }
        }
    }

    [Fact]
    public void Unknown_discriminator_throws()
        => Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<IpcCommand>("""{"type":"yok"}""", IpcJson.Options));

    [Fact]
    public void StartRun_roundtrips_with_discriminator()
    {
        var cmd = new StartRunCommand("r1", RunMode.Rebuild, @"D:\repo", "Debug", 6);
        string json = JsonSerializer.Serialize<IpcCommand>(cmd, IpcJson.Options);
        Assert.Contains("\"type\":\"startRun\"", json);
        Assert.Contains("\"mode\":\"rebuild\"", json); // camelCase enum
        var back = Assert.IsType<StartRunCommand>(JsonSerializer.Deserialize<IpcCommand>(json, IpcJson.Options));
        Assert.Equal(cmd, back);
    }

    [Fact]
    public void Run_events_roundtrip_with_discriminators()
    {
        IpcEvent[] events =
        [
            new RunStartedEvent("r1", RunMode.Continue, 177, 6, "Debug", 4200),
            new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"),
            new ProjectLogEvent("r1", @"C:\p\a.csproj", 1, "  A.cs(3,5): error CS0103: ..."),
            new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 2400),
            new ProjectFailedEvent("r1", @"C:\p\b.csproj", 900, "exit 1"),
            new ProjectSkippedEvent("r1", @"C:\p\c.csproj", "in dependency cycle"),
            new RunCompletedEvent("r1", RunOutcome.Stopped, 3, 1, 2, 171, 65000),
            new ProjectLogChunkEvent(@"C:\p\a.csproj", 0, "line\n", true, 42),
        ];
        foreach (var ev in events)
        {
            string json = JsonSerializer.Serialize(ev, IpcJson.Options);
            Assert.Equal(ev, JsonSerializer.Deserialize<IpcEvent>(json, IpcJson.Options));
        }
    }

    [Theory]
    [InlineData(RunMode.Build, "\"mode\":\"build\"")]
    [InlineData(RunMode.RetryFailed, "\"mode\":\"retryFailed\"")]
    public void RunMode_new_values_roundtrip_camelCase(RunMode mode, string expectedFragment)
    {
        var cmd = new StartRunCommand("r1", mode, @"D:\repo", "Debug", 6);
        string json = JsonSerializer.Serialize<IpcCommand>(cmd, IpcJson.Options);
        Assert.Contains(expectedFragment, json);
        var back = Assert.IsType<StartRunCommand>(JsonSerializer.Deserialize<IpcCommand>(json, IpcJson.Options));
        Assert.Equal(mode, back.Mode);
    }

    [Fact]
    public void StartRunCommand_new_fields_roundtrip()
    {
        var cmd = new StartRunCommand("r1", RunMode.RetryFailed, @"D:\repo", "Debug", 6,
            Branch: "feature/x", UseWorktree: true, WorktreeName: "wt-1", DependentMode: DependentMode.Fast);
        string json = JsonSerializer.Serialize<IpcCommand>(cmd, IpcJson.Options);
        Assert.Contains("\"branch\":\"feature/x\"", json);
        Assert.Contains("\"useWorktree\":true", json);
        Assert.Contains("\"worktreeName\":\"wt-1\"", json);
        Assert.Contains("\"dependentMode\":\"fast\"", json); // camelCase enum
        var back = Assert.IsType<StartRunCommand>(JsonSerializer.Deserialize<IpcCommand>(json, IpcJson.Options));
        Assert.Equal(cmd, back);
    }

    [Fact]
    public void StartRunCommand_new_fields_default_to_safe_backward_compatible_shape()
    {
        var cmd = new StartRunCommand("r1", RunMode.Rebuild, @"D:\repo", "Debug", 6);
        Assert.Equal("", cmd.Branch);
        Assert.False(cmd.UseWorktree);
        Assert.Null(cmd.WorktreeName);
        Assert.Equal(DependentMode.Safe, cmd.DependentMode);
        Assert.Null(cmd.LayerPatterns); // [A1] katman ataması varsayılan olarak KAPALI (mevcut davranış)
    }

    // [A1/T15] Katman pattern'leri App'ten Supervisor'a IPC ile taşınır — Core'daki LayerEngine ancak bu
    // alan dolu geldiğinde çalışır. Sıra ANLAMLIDIR (Order = hem eşleşme önceliği hem LayerIndex).
    [Fact]
    public void StartRunCommand_layerPatterns_roundtrip_preserving_order()
    {
        var cmd = new StartRunCommand("r1", RunMode.Build, @"D:\repo", "Debug", 6,
            LayerPatterns: [new LayerPattern(0, "^OSYS\\.Data", "DataLayer"), new LayerPattern(1, "^OSYS\\.Ui", "UiLayer")]);
        string json = JsonSerializer.Serialize<IpcCommand>(cmd, IpcJson.Options);
        Assert.Contains("\"layerPatterns\":", json);

        var back = Assert.IsType<StartRunCommand>(JsonSerializer.Deserialize<IpcCommand>(json, IpcJson.Options));
        Assert.Equal(cmd.LayerPatterns, back.LayerPatterns);
    }

    [Fact]
    public void ProjectSucceededEvent_depIssues_null_is_omitted_and_populated_preserves_order()
    {
        var withoutIssues = new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 2400);
        string jsonNoIssues = JsonSerializer.Serialize<IpcEvent>(withoutIssues, IpcJson.Options);
        Assert.DoesNotContain("depIssues", jsonNoIssues);
        var backNoIssues = Assert.IsType<ProjectSucceededEvent>(JsonSerializer.Deserialize<IpcEvent>(jsonNoIssues, IpcJson.Options));
        Assert.Null(backNoIssues.DepIssues);

        var withIssues = new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 2400, ["dependent B stale", "dependent A stale"]);
        string jsonWithIssues = JsonSerializer.Serialize<IpcEvent>(withIssues, IpcJson.Options);
        Assert.Contains("\"depIssues\":[\"dependent B stale\",\"dependent A stale\"]", jsonWithIssues);
        var backWithIssues = Assert.IsType<ProjectSucceededEvent>(JsonSerializer.Deserialize<IpcEvent>(jsonWithIssues, IpcJson.Options));
        Assert.Equal(["dependent B stale", "dependent A stale"], backWithIssues.DepIssues);
    }

    [Fact]
    public void ProjectFailedEvent_depIssues_null_is_omitted_and_populated_preserves_order()
    {
        var withoutIssues = new ProjectFailedEvent("r1", @"C:\p\b.csproj", 900, "exit 1");
        string jsonNoIssues = JsonSerializer.Serialize<IpcEvent>(withoutIssues, IpcJson.Options);
        Assert.DoesNotContain("depIssues", jsonNoIssues);
        var backNoIssues = Assert.IsType<ProjectFailedEvent>(JsonSerializer.Deserialize<IpcEvent>(jsonNoIssues, IpcJson.Options));
        Assert.Null(backNoIssues.DepIssues);

        var withIssues = new ProjectFailedEvent("r1", @"C:\p\b.csproj", 900, "exit 1", ["dep C broken", "dep D broken"]);
        string jsonWithIssues = JsonSerializer.Serialize<IpcEvent>(withIssues, IpcJson.Options);
        Assert.Contains("\"depIssues\":[\"dep C broken\",\"dep D broken\"]", jsonWithIssues);
        var backWithIssues = Assert.IsType<ProjectFailedEvent>(JsonSerializer.Deserialize<IpcEvent>(jsonWithIssues, IpcJson.Options));
        Assert.Equal(["dep C broken", "dep D broken"], backWithIssues.DepIssues);
    }

    [Fact]
    public void RunCompletedEvent_depIssueCount_roundtrips()
    {
        var ev = new RunCompletedEvent("r1", RunOutcome.Completed, 3, 1, 2, 171, 65000, DepIssueCount: 2);
        string json = JsonSerializer.Serialize<IpcEvent>(ev, IpcJson.Options);
        Assert.Contains("\"depIssueCount\":2", json);
        var back = Assert.IsType<RunCompletedEvent>(JsonSerializer.Deserialize<IpcEvent>(json, IpcJson.Options));
        Assert.Equal(2, back.DepIssueCount);
    }

    [Fact]
    public void BuildPreviewEvent_roundtrips_with_discriminator_and_preserves_willBuild_tristate()
    {
        var ev = new BuildPreviewEvent(
        [
            new BuildPreviewItem(@"C:\p\a.csproj", "A", true),   // dirty
            new BuildPreviewItem(@"C:\p\b.csproj", "B", false),  // güncel/clean
            new BuildPreviewItem(@"C:\p\c.csproj", "C", null),   // hollow/imza-yok
        ]);
        string json = JsonSerializer.Serialize<IpcEvent>(ev, IpcJson.Options);
        Assert.Contains("\"type\":\"buildPreview\"", json);
        var back = Assert.IsType<BuildPreviewEvent>(JsonSerializer.Deserialize<IpcEvent>(json, IpcJson.Options));
        Assert.Equal(ev.Items, back.Items);
    }

    [Fact]
    public void SyncWorkspaceCommand_roundtrips_with_discriminator()
    {
        IpcCommand cmd = new SyncWorkspaceCommand(@"D:\repo", "main");
        string json = JsonSerializer.Serialize(cmd, IpcJson.Options);
        Assert.Contains("\"type\":\"syncWorkspace\"", json);
        var back = Assert.IsType<SyncWorkspaceCommand>(JsonSerializer.Deserialize<IpcCommand>(json, IpcJson.Options));
        Assert.Equal(cmd, back);
    }

    [Fact]
    public void Sync_events_roundtrip_with_discriminators()
    {
        IpcEvent[] events =
        [
            new SyncStartedEvent(@"D:\repo", "main"),
            new SyncProgressEvent("fetching origin...", "info"),
            new SyncCompletedEvent("main", "abc123", false, 42, 0),
            new BranchListEvent([
                new BranchRef("main", "abc123", true, false),
                new BranchRef("origin/main", "abc123", false, true),
            ]),
        ];
        foreach (var ev in events)
        {
            string json = JsonSerializer.Serialize(ev, IpcJson.Options);
            var back = JsonSerializer.Deserialize<IpcEvent>(json, IpcJson.Options);
            if (ev is BranchListEvent blOriginal)
            {
                var blBack = Assert.IsType<BranchListEvent>(back);
                Assert.Equal(blOriginal.Branches, blBack.Branches);
            }
            else
            {
                Assert.Equal(ev, back);
            }
        }
        Assert.Contains("\"type\":\"syncStarted\"", JsonSerializer.Serialize(events[0], IpcJson.Options));
        Assert.Contains("\"type\":\"syncProgress\"", JsonSerializer.Serialize(events[1], IpcJson.Options));
        Assert.Contains("\"type\":\"syncCompleted\"", JsonSerializer.Serialize(events[2], IpcJson.Options));
        Assert.Contains("\"type\":\"branchList\"", JsonSerializer.Serialize(events[3], IpcJson.Options));
    }

    // ---------------------------------------------------------------- [A5/T69] Sync / branch / worktree / topoloji

    // App'in branch seçici, worktree havuzu ve "worktree sil" akışlarını besleyen üç komut: hepsi RootPath
    // taşır (Supervisor tek bir repo'ya sabitlenmiş DEĞİLDİR — kök her komutta gelir).
    [Fact]
    public void ListBranches_listWorktrees_deleteWorktree_roundtrip_with_discriminators()
    {
        IpcCommand[] commands = [new ListBranchesCommand(@"D:\repo"), new ListWorktreesCommand(@"D:\repo"),
            new DeleteWorktreeCommand(@"D:\repo", "main-1")];
        string[] expectedDiscriminators = ["\"type\":\"listBranches\"", "\"type\":\"listWorktrees\"", "\"type\":\"deleteWorktree\""];
        for (int i = 0; i < commands.Length; i++)
        {
            string json = JsonSerializer.Serialize(commands[i], IpcJson.Options);
            Assert.Contains(expectedDiscriminators[i], json);
            Assert.Equal(commands[i], JsonSerializer.Deserialize<IpcCommand>(json, IpcJson.Options));
        }
    }

    // [A5/T69] Graf paneli (D5), katman gruplaması (D1) ve Open-in-VS (E1) için gereken TÜM veri tek event'te
    // taşınır. ProjectNode'un liste alanları (SolutionNames/Dependencies) round-trip'te YENİ liste örneklerine
    // dönüşür — ProjectNode'un ELLE YAZILMIŞ Equals'ı (sıralı içerik eşitliği) bu yüzden vardır; Cycles ise
    // iç içe liste olduğundan event-seviyesinde record eşitliğiyle KIYASLANAMAZ, üye üye karşılaştırılır.
    [Fact]
    public void WorkspaceTopology_roundtrips_with_nodes_cycles_solutions_and_layer_warnings()
    {
        var ev = new WorkspaceTopologyEvent(
            Nodes:
            [
                new ProjectNode(@"C:\p\a.csproj", "A", @"C:\p\a.csproj", ["Osys"], [], 0, 0, "DataLayer", false, true),
                new ProjectNode(@"C:\p\b.csproj", "B", @"C:\p\b.csproj", ["Osys", "Tools"], [@"C:\p\a.csproj"], 1, 1, "UiLayer", true, false),
            ],
            Cycles: [[@"C:\p\b.csproj", @"C:\p\c.csproj"]],
            Solutions: [new SolutionRef("Osys", @"C:\p\Osys.sln"), new SolutionRef("Tools", @"C:\p\Tools.sln")],
            LayerWarnings: ["UiLayer -> DataLayer reverse dependency"]);

        string json = JsonSerializer.Serialize<IpcEvent>(ev, IpcJson.Options);
        Assert.Contains("\"type\":\"workspaceTopology\"", json);

        var back = Assert.IsType<WorkspaceTopologyEvent>(JsonSerializer.Deserialize<IpcEvent>(json, IpcJson.Options));
        Assert.Equal(ev.Nodes, back.Nodes);                   // ProjectNode.Equals — sıralı içerik eşitliği
        Assert.Equal(ev.Solutions, back.Solutions);
        Assert.Equal(ev.LayerWarnings, back.LayerWarnings);
        Assert.Equal(ev.Cycles.Count, back.Cycles.Count);
        for (int i = 0; i < ev.Cycles.Count; i++) Assert.Equal(ev.Cycles[i], back.Cycles[i]);
        // Dependency/layer/solution verisinin GERÇEKTEN taşındığının kanıtı (boş listeler de eşit olurdu):
        Assert.Equal([@"C:\p\a.csproj"], back.Nodes[1].Dependencies);
        Assert.Equal("UiLayer", back.Nodes[1].LayerName);
        Assert.Equal(@"C:\p\Osys.sln", back.Solutions[0].Path);
    }

    [Fact]
    public void WorktreeList_roundtrips_with_discriminator()
    {
        var ev = new WorktreeListEvent([
            new Worktree("main-1", "main", @"C:\pool\main-1", true, 1234),
            new Worktree("feature-x-1", "feature/x", @"C:\pool\feature-x-1", false, null),
        ]);
        string json = JsonSerializer.Serialize<IpcEvent>(ev, IpcJson.Options);
        Assert.Contains("\"type\":\"worktreeList\"", json);
        var back = Assert.IsType<WorktreeListEvent>(JsonSerializer.Deserialize<IpcEvent>(json, IpcJson.Options));
        Assert.Equal(ev.Worktrees, back.Worktrees);
    }

    // [A5/T69] Sync de katman pattern'lerini taşır (StartRunCommand ile aynı gerekçe): topoloji event'indeki
    // LayerIndex/LayerName ve ters-katman uyarıları ancak pattern'ler Core'a ULAŞIRSA doldurulabilir.
    [Fact]
    public void SyncWorkspace_carries_layer_patterns()
    {
        var cmd = new SyncWorkspaceCommand(@"D:\repo", "main",
            LayerPatterns: [new LayerPattern(0, "^OSYS\\.Data", "DataLayer"), new LayerPattern(1, "^OSYS\\.Ui", "UiLayer")],
            Configuration: "Release");
        string json = JsonSerializer.Serialize<IpcCommand>(cmd, IpcJson.Options);
        Assert.Contains("\"type\":\"syncWorkspace\"", json);
        Assert.Contains("\"layerPatterns\":", json);
        Assert.Contains("\"configuration\":\"Release\"", json);

        var back = Assert.IsType<SyncWorkspaceCommand>(JsonSerializer.Deserialize<IpcCommand>(json, IpcJson.Options));
        Assert.Equal(cmd.LayerPatterns, back.LayerPatterns);
        Assert.Equal("Release", back.Configuration);

        // Varsayılan şekil geriye dönük uyumlu kalır (mevcut çağrılar iki argümanla kurar).
        var bare = new SyncWorkspaceCommand(@"D:\repo", "main");
        Assert.Null(bare.LayerPatterns);
        Assert.Equal("Debug", bare.Configuration);
    }

    // ---------------------------------------------------------------- [T20-b/K11] perf modu (cap + priority)

    // Perf modu App'ten Supervisor'a İKİ yoldan gider: run başında StartRunCommand.PerfMode, koşarken
    // setPerfMode. İkincisi YENİ bir diskriminatördür — kayıtlı olmasaydı Unknown_discriminator_throws'un
    // yakaladığı hataya düşer, kayıtlı ama dispatch edilmemiş olsaydı error(unknownCommand) dönerdi.
    [Fact]
    public void SetPerfModeCommand_roundtrips_with_its_own_discriminator()
    {
        IpcCommand cmd = new SetPerfModeCommand("Light");
        string json = JsonSerializer.Serialize(cmd, IpcJson.Options);
        Assert.Contains("\"type\":\"setPerfMode\"", json);
        var back = Assert.IsType<SetPerfModeCommand>(JsonSerializer.Deserialize<IpcCommand>(json, IpcJson.Options));
        Assert.Equal("Light", back.PerfMode);
    }

    // [T20-b] Geriye dönük uyum: PerfMode NULLABLE + varsayılan değerlidir. Alanı hiç taşımayan (P2 öncesi
    // yazılmış) bir NDJSON satırı hâlâ çözülmeli ve null'a düşmeli — Supervisor o durumda cap'e HİÇ dokunmaz.
    [Fact]
    public void StartRunCommand_perfMode_roundtrips_and_stays_optional_for_older_lines()
    {
        var withPerf = new StartRunCommand("r1", RunMode.Build, @"D:\repo", "Debug", 2, PerfMode: "Light");
        string json = JsonSerializer.Serialize<IpcCommand>(withPerf, IpcJson.Options);
        Assert.Contains("\"perfMode\":\"Light\"", json);
        Assert.Equal(withPerf, JsonSerializer.Deserialize<IpcCommand>(json, IpcJson.Options));

        var bare = new StartRunCommand("r1", RunMode.Build, @"D:\repo", "Debug", 2);
        Assert.Null(bare.PerfMode);
        Assert.DoesNotContain("perfMode", JsonSerializer.Serialize<IpcCommand>(bare, IpcJson.Options));

        var legacy = Assert.IsType<StartRunCommand>(JsonSerializer.Deserialize<IpcCommand>(
            """{"type":"startRun","runId":"r1","mode":"build","rootPath":"D:\\repo","configuration":"Debug","parallelism":2}""",
            IpcJson.Options));
        Assert.Null(legacy.PerfMode);
    }

    // [T20-b/K11] runStarted, o run için GERÇEKTEN uygulanmış cap'i taşır (App'in doğrulama/acceptance kanıtı).
    // Cap yoksa (Full ya da perf modu hiç gelmediyse) alan JSON'a YAZILMAZ — eski okuyucular etkilenmez.
    [Fact]
    public void RunStartedEvent_carries_the_applied_cpu_cap_and_omits_it_when_uncapped()
    {
        var capped = new RunStartedEvent("r1", RunMode.Build, 3, 4, "Debug", 0, CpuCapPercent: 70);
        string json = JsonSerializer.Serialize<IpcEvent>(capped, IpcJson.Options);
        Assert.Contains("\"cpuCapPercent\":70", json);
        Assert.Equal(capped, JsonSerializer.Deserialize<IpcEvent>(json, IpcJson.Options));

        var uncapped = new RunStartedEvent("r1", RunMode.Build, 3, 6, "Debug", 0);
        Assert.Null(uncapped.CpuCapPercent);
        Assert.DoesNotContain("cpuCapPercent", JsonSerializer.Serialize<IpcEvent>(uncapped, IpcJson.Options));
    }

    // [A5/T69] Sync artık will-build pass'ini de koşar; §3.1 konsol satırları ("N changed projects, M to build" /
    // "K projects up to date") ve D2'nin idle şeridi bu üç sayacı OKUR. "changed" (doğrudan dirty) will-build
    // kümesinden TÜRETİLEMEZ (o küme transitive dependent'ları da içerir) — bu yüzden ayrı bir alandır.
    [Fact]
    public void SyncCompleted_carries_changed_toBuild_and_upToDate_counts()
    {
        var ev = new SyncCompletedEvent("main", "b7e91d4c0a", false, 36, 1,
            ChangedCount: 7, ToBuildCount: 14, UpToDateCount: 22);
        string json = JsonSerializer.Serialize<IpcEvent>(ev, IpcJson.Options);
        Assert.Contains("\"changedCount\":7", json);
        Assert.Contains("\"toBuildCount\":14", json);
        Assert.Contains("\"upToDateCount\":22", json);
        Assert.Equal(ev, JsonSerializer.Deserialize<IpcEvent>(json, IpcJson.Options));
    }
}
