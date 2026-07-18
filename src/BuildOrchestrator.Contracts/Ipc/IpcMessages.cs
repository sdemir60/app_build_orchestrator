using System.Text.Json;
using System.Text.Json.Serialization;
using BuildOrchestrator.Contracts.Model;

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
[JsonDerivedType(typeof(SyncWorkspaceCommand), "syncWorkspace")]
public abstract record IpcCommand;

public sealed record PingCommand(int Seq) : IpcCommand;
public sealed record ShutdownCommand : IpcCommand;
public enum StopKind { Graceful, Hard }
public sealed record StopRunCommand(string RunId, StopKind Kind) : IpcCommand;
public sealed record GetProjectLogCommand(string ProjectId) : IpcCommand;
public sealed record DebugSpawnChildrenCommand(int Count, bool Breakaway) : IpcCommand;

public enum RunMode { Rebuild, Build, Continue, RetryFailed }
/// <summary>Dependent'ların RetryFailed sonrası nasıl ele alınacağı: Safe = failed + tüm transitive dependent'lar
/// yeniden derlenir; Fast = yalnız failed projeler (dependent'lar riske rağmen atlanır). [It-3]</summary>
public enum DependentMode { Safe, Fast }
/// <param name="Mode">Rebuild = tüm projeler; Build = incremental (yalnız dirty); Continue = önceki run'ın
/// queued'larından sürer (elapsed korunur); RetryFailed = önceki run'da failed olanlar + (DependentMode'a göre)
/// dependent'ları. [v7Δ-4] [It-3]</param>
/// <param name="Branch">Sync/build hedefi branch adı. [It-3]</param>
/// <param name="UseWorktree">true ise derleme ayrı bir git worktree üzerinde yapılır. [It-3]</param>
/// <param name="WorktreeName">UseWorktree=true iken kullanılacak worktree adı; null ise varsayılan ad türetilir. [It-3]</param>
/// <param name="DependentMode">Genel incremental dependent-propagation kapısı (bkz. <c>IncrementalPlanner</c>
/// Safe/Fast — Task 7): Build modunda WillBuild hesaplamasını besler (Safe = dirty+transitive cascade, Fast =
/// yalnız dirty, cascade yok), RetryFailed modunda failed+dependent kapsamını belirler. Varsayılan Safe. [It-3]</param>
public sealed record StartRunCommand(string RunId, RunMode Mode, string RootPath, string Configuration, int Parallelism,
    string Branch = "", bool UseWorktree = false, string? WorktreeName = null, DependentMode DependentMode = DependentMode.Safe) : IpcCommand;

/// <summary>Workspace'i verilen branch'e senkronize et (fetch + checkout/reset). [It-3]</summary>
public sealed record SyncWorkspaceCommand(string RootPath, string Branch) : IpcCommand;

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
[JsonDerivedType(typeof(SyncStartedEvent), "syncStarted")]
[JsonDerivedType(typeof(SyncProgressEvent), "syncProgress")]
[JsonDerivedType(typeof(SyncCompletedEvent), "syncCompleted")]
[JsonDerivedType(typeof(BranchListEvent), "branchList")]
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
/// <param name="DepIssues">Bu proje için tespit edilen dependency-uyarıları (ör. "dependent X henüz derlenmedi");
/// yoksa null (JSON'a yazılmaz). [It-3]</param>
public sealed record ProjectSucceededEvent(string RunId, string ProjectId, long DurationMs,
    IReadOnlyList<string>? DepIssues = null) : IpcEvent;
/// <param name="DepIssues">Bu proje için tespit edilen dependency-uyarıları; yoksa null (JSON'a yazılmaz). [It-3]</param>
public sealed record ProjectFailedEvent(string RunId, string ProjectId, long DurationMs, string Reason,
    IReadOnlyList<string>? DepIssues = null) : IpcEvent;
public sealed record ProjectSkippedEvent(string RunId, string ProjectId, string Reason) : IpcEvent;
/// <param name="DepIssueCount">Run genelinde depIssues taşıyan proje-sonucu sayısı. [It-3]</param>
public sealed record RunCompletedEvent(string RunId, RunOutcome Outcome, int Succeeded, int Failed, int Skipped,
    int Queued, long DurationMs, int DepIssueCount = 0) : IpcEvent;

/// <summary>Sync (fetch + checkout/reset) başladı. [It-3]</summary>
public sealed record SyncStartedEvent(string RootPath, string Branch) : IpcEvent;
/// <param name="Level">dim/info/warn — App tarafında satır rengini belirler. [It-3]</param>
public sealed record SyncProgressEvent(string Line, string Level) : IpcEvent;
/// <param name="TargetSha">Sync sonrası HEAD sha'sı; belirlenemediyse null.</param>
/// <param name="FetchDegraded">true ise fetch başarısız/kısıtlı oldu ve sync yerel state ile devam etti.</param>
public sealed record SyncCompletedEvent(string Branch, string? TargetSha, bool FetchDegraded,
    int ProjectCount, int CycleCount) : IpcEvent;
public sealed record BranchListEvent(IReadOnlyList<BranchRef> Branches) : IpcEvent;
