using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildOrchestrator.Contracts.Ipc;

public static class IpcJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PingCommand), "ping")]
[JsonDerivedType(typeof(ShutdownCommand), "shutdown")]
[JsonDerivedType(typeof(StopRunCommand), "stopRun")]
[JsonDerivedType(typeof(GetProjectLogCommand), "getProjectLog")]
[JsonDerivedType(typeof(DebugSpawnChildrenCommand), "debugSpawnChildren")]
public abstract record IpcCommand;

public sealed record PingCommand(int Seq) : IpcCommand;
public sealed record ShutdownCommand : IpcCommand;
public enum StopKind { Graceful, Hard }
public sealed record StopRunCommand(string RunId, StopKind Kind) : IpcCommand;
public sealed record GetProjectLogCommand(string ProjectId) : IpcCommand;
public sealed record DebugSpawnChildrenCommand(int Count, bool Breakaway) : IpcCommand;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(EngineReadyEvent), "engineReady")]
[JsonDerivedType(typeof(PongEvent), "pong")]
[JsonDerivedType(typeof(ErrorEvent), "error")]
[JsonDerivedType(typeof(RunStoppedEvent), "runStopped")]
[JsonDerivedType(typeof(ProjectLogChunkEvent), "projectLogChunk")]
[JsonDerivedType(typeof(DebugChildrenSpawnedEvent), "debugChildrenSpawned")]
public abstract record IpcEvent;

public sealed record EngineReadyEvent(int Pid, string EngineVersion) : IpcEvent;
public sealed record PongEvent(int Seq) : IpcEvent;
public sealed record ErrorEvent(string Code, string Message) : IpcEvent;
public sealed record RunStoppedEvent(string RunId, bool WasHard) : IpcEvent;
public sealed record ProjectLogChunkEvent(string ProjectId, int Sequence, string Text, bool IsLast) : IpcEvent;
public sealed record DebugChildrenSpawnedEvent(int[] Pids) : IpcEvent;
