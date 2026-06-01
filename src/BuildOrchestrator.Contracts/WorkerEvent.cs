using System.Text.Json.Serialization;

namespace BuildOrchestrator.Contracts;

/// <summary>
/// Base type for all Worker -> UI events (Section 8). Serialized polymorphically with a
/// "$kind" discriminator.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(SyncProgressEvent), "syncProgress")]
[JsonDerivedType(typeof(SyncCompletedEvent), "syncCompleted")]
[JsonDerivedType(typeof(BranchListEvent), "branchList")]
[JsonDerivedType(typeof(RunStartedEvent), "runStarted")]
[JsonDerivedType(typeof(ProjectStartedEvent), "projectStarted")]
[JsonDerivedType(typeof(ProjectLogEvent), "projectLog")]
[JsonDerivedType(typeof(ProjectSucceededEvent), "projectSucceeded")]
[JsonDerivedType(typeof(ProjectFailedEvent), "projectFailed")]
[JsonDerivedType(typeof(ProjectSkippedEvent), "projectSkipped")]
[JsonDerivedType(typeof(RunCompletedEvent), "runCompleted")]
[JsonDerivedType(typeof(RunCancelledEvent), "runCancelled")]
[JsonDerivedType(typeof(WorkerErrorEvent), "error")]
public abstract record WorkerEvent;

public sealed record SyncProgressEvent(string Message, int Current, int Total) : WorkerEvent;

public sealed record SyncCompletedEvent(DependencyGraph Graph) : WorkerEvent;

public sealed record BranchListEvent(List<BranchInfo> Branches) : WorkerEvent;

public sealed record RunStartedEvent(string RunId, int TotalProjects, int PlannedProjects) : WorkerEvent;

public sealed record ProjectStartedEvent(string RunId, string ProjectId) : WorkerEvent;

public sealed record ProjectLogEvent(string RunId, string ProjectId, string Line, bool IsError) : WorkerEvent;

public sealed record ProjectSucceededEvent(string RunId, string ProjectId, string? Commit) : WorkerEvent;

public sealed record ProjectFailedEvent(string RunId, string ProjectId, string ErrorSummary) : WorkerEvent;

public sealed record ProjectSkippedEvent(string RunId, string ProjectId, string Reason) : WorkerEvent;

public sealed record RunCompletedEvent(string RunId, int Succeeded, int Failed, int Skipped) : WorkerEvent;

public sealed record RunCancelledEvent(string RunId) : WorkerEvent;

public sealed record WorkerErrorEvent(string Message, string? Detail = null) : WorkerEvent;
