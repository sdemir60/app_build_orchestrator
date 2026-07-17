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
[JsonDerivedType(typeof(StartRunCommand), "startRun")]
public abstract record IpcCommand;

public sealed record PingCommand(int Seq) : IpcCommand;
public sealed record ShutdownCommand : IpcCommand;
public enum StopKind { Graceful, Hard }
public sealed record StopRunCommand(string RunId, StopKind Kind) : IpcCommand;
public sealed record GetProjectLogCommand(string ProjectId) : IpcCommand;
public sealed record DebugSpawnChildrenCommand(int Count, bool Breakaway) : IpcCommand;

public enum RunMode { Rebuild, Continue }
/// <param name="Mode">Rebuild = tüm projeler; Continue = önceki run'ın queued'larından sürer (elapsed korunur). [v7Δ-4]</param>
public sealed record StartRunCommand(string RunId, RunMode Mode, string RootPath, string Configuration, int Parallelism) : IpcCommand;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(EngineReadyEvent), "engineReady")]
[JsonDerivedType(typeof(PongEvent), "pong")]
[JsonDerivedType(typeof(ErrorEvent), "error")]
[JsonDerivedType(typeof(RunStoppedEvent), "runStopped")]
[JsonDerivedType(typeof(ProjectLogChunkEvent), "projectLogChunk")]
[JsonDerivedType(typeof(DebugChildrenSpawnedEvent), "debugChildrenSpawned")]
[JsonDerivedType(typeof(RunStartedEvent), "runStarted")]
[JsonDerivedType(typeof(ProjectStartedEvent), "projectStarted")]
[JsonDerivedType(typeof(ProjectLogEvent), "projectLog")]
[JsonDerivedType(typeof(ProjectSucceededEvent), "projectSucceeded")]
[JsonDerivedType(typeof(ProjectFailedEvent), "projectFailed")]
[JsonDerivedType(typeof(ProjectSkippedEvent), "projectSkipped")]
[JsonDerivedType(typeof(RunCompletedEvent), "runCompleted")]
public abstract record IpcEvent;

public sealed record EngineReadyEvent(int Pid, string EngineVersion) : IpcEvent;
public sealed record PongEvent(int Seq) : IpcEvent;
public sealed record ErrorEvent(string Code, string Message) : IpcEvent;
public sealed record RunStoppedEvent(string RunId, bool WasHard) : IpcEvent;
/// <param name="ThroughLineNumber">Snapshot anında diske yazılmış son satır no — App canlı `projectLog`
/// satırlarını bununla dikiş yapar (LineNumber &lt;= ThroughLineNumber olanlar zaten chunk'ta). [T28]</param>
public sealed record ProjectLogChunkEvent(string ProjectId, int Sequence, string Text, bool IsLast, int ThroughLineNumber) : IpcEvent;
public sealed record DebugChildrenSpawnedEvent(int[] Pids) : IpcEvent;

public enum RunOutcome { Completed, Stopped }
public sealed record RunStartedEvent(string RunId, RunMode Mode, int TotalProjects, int Parallelism,
    string Configuration, long ElapsedMsAtStart) : IpcEvent;
public sealed record ProjectStartedEvent(string RunId, string ProjectId, string Name) : IpcEvent;
public sealed record ProjectLogEvent(string RunId, string ProjectId, int LineNumber, string Text) : IpcEvent;
public sealed record ProjectSucceededEvent(string RunId, string ProjectId, long DurationMs) : IpcEvent;
public sealed record ProjectFailedEvent(string RunId, string ProjectId, long DurationMs, string Reason) : IpcEvent;
public sealed record ProjectSkippedEvent(string RunId, string ProjectId, string Reason) : IpcEvent;
public sealed record RunCompletedEvent(string RunId, RunOutcome Outcome, int Succeeded, int Failed, int Skipped,
    int Queued, long DurationMs) : IpcEvent;
