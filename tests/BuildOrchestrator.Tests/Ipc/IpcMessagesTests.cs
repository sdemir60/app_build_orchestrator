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
            new RunStoppedEvent("r", true), new ProjectLogChunkEvent("p", 0, "t", false), new DebugChildrenSpawnedEvent([1, 2]) ];
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
}
