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
}
