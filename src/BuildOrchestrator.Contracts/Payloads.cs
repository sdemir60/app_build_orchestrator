namespace BuildOrchestrator.Contracts;

// ---- Command payloads (UI -> Worker) ----

public sealed record SyncWorkspacePayload(string RootPath);

public sealed record SelectBranchPayload(string Branch);

public sealed record StartRunPayload(RunRequest Request);

public sealed record StopRunPayload(string RunId);

public sealed record OpenPathPayload(string ProjectId);

public sealed record OpenInVsPayload(string ProjectId);

// ---- Event payloads (Worker -> UI) ----

public sealed record SyncProgressPayload(string Phase, int Scanned, int Total, string? Current);

public sealed record SyncCompletedPayload(IReadOnlyList<ProjectNode> Projects, bool HasCycles);

public sealed record BranchListPayload(IReadOnlyList<string> Branches, string Current);

public sealed record RunStartedPayload(string RunId, IReadOnlyList<string> PlannedProjectIds);

public sealed record ProjectStartedPayload(string RunId, string ProjectId);

/// <summary>A single line of build output. <paramref name="IsError"/> drives errors-only filtering.</summary>
public sealed record ProjectLogPayload(string RunId, string ProjectId, string Line, bool IsError);

public sealed record ProjectSucceededPayload(string RunId, string ProjectId, string? Commit, long ElapsedMs);

public sealed record ProjectFailedPayload(string RunId, string ProjectId, string Reason, long ElapsedMs);

public sealed record ProjectSkippedPayload(string RunId, string ProjectId, string Reason);

public sealed record RunCompletedPayload(
    string RunId,
    int Total,
    int Built,
    int Succeeded,
    int Failed,
    int Skipped,
    long ElapsedMs);

public sealed record RunCancelledPayload(string RunId, string Reason);

public sealed record ErrorPayload(string Message, string? Detail);
