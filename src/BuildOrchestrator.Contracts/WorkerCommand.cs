using System.Text.Json.Serialization;

namespace BuildOrchestrator.Contracts;

/// <summary>
/// Base type for all UI -> Worker commands. Serialized polymorphically with a
/// "$kind" discriminator so a single newline-delimited JSON channel can carry any command.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(SyncWorkspaceCommand), "syncWorkspace")]
[JsonDerivedType(typeof(ReanalyzeCommand), "reanalyze")]
[JsonDerivedType(typeof(ListBranchesCommand), "listBranches")]
[JsonDerivedType(typeof(SelectBranchCommand), "selectBranch")]
[JsonDerivedType(typeof(StartRunCommand), "startRun")]
[JsonDerivedType(typeof(StopRunCommand), "stopRun")]
[JsonDerivedType(typeof(OpenPathCommand), "openPath")]
[JsonDerivedType(typeof(OpenInVSCommand), "openInVS")]
[JsonDerivedType(typeof(ShutdownCommand), "shutdown")]
public abstract record WorkerCommand;

public sealed record SyncWorkspaceCommand(string RootPath) : WorkerCommand;

public sealed record ReanalyzeCommand : WorkerCommand;

public sealed record ListBranchesCommand : WorkerCommand;

public sealed record SelectBranchCommand(string Branch) : WorkerCommand;

public sealed record StartRunCommand(string RunId, RunRequest Request) : WorkerCommand;

public sealed record StopRunCommand(string RunId) : WorkerCommand;

public sealed record OpenPathCommand(string ProjectId) : WorkerCommand;

public sealed record OpenInVSCommand(string ProjectId) : WorkerCommand;

public sealed record ShutdownCommand : WorkerCommand;
