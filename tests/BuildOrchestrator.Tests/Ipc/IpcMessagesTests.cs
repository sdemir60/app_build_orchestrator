using System.Text.Json;
using BuildOrchestrator.Contracts.Ipc;

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
}
