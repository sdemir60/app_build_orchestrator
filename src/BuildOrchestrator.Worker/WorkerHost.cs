using System.Diagnostics;
using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Configuration;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Graph;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.Storage;
using BuildOrchestrator.Core.Sync;
using BuildOrchestrator.Worker.MsBuild;
using BuildOrchestrator.Worker.ProcessControl;

namespace BuildOrchestrator.Worker;

/// <summary>
/// Hosts the Worker side of the Section 8 protocol: receives commands, runs Sync/Build/Rebuild via
/// the Core engine + <see cref="BuildRunner"/>, and streams events back. Guarantees a single in-flight
/// run (Section 6) and owns the <see cref="JobObject"/> that guarantees process-tree cleanup (Section 6.1).
/// </summary>
public sealed class WorkerHost : IRunEventSink, IDisposable
{
    private readonly MessageChannel _channel;
    private readonly AppPaths _paths;
    private readonly ConfigStore _configStore;
    private readonly GraphCacheStore _graphCacheStore;
    private readonly BuildStateStore _buildStateStore;
    private readonly SyncService _syncService;
    private readonly JobObject _job;
    private readonly PidTracker _pids;

    private readonly SemaphoreSlim _runGate = new(1, 1);
    private CancellationTokenSource? _activeRunCts;
    private string? _activeRunId;

    private AppConfig _config;
    private DependencyGraph? _graph;
    private string _selectedBranch = string.Empty;

    public WorkerHost(MessageChannel channel, AppPaths? paths = null)
    {
        _channel = channel;
        _paths = paths ?? new AppPaths();
        _paths.EnsureRoot();
        var store = new JsonStore();
        _configStore = new ConfigStore(_paths, store);
        _graphCacheStore = new GraphCacheStore(_paths, store);
        _buildStateStore = new BuildStateStore(_paths, store);
        _syncService = new SyncService(new WorkspaceScanner(), _graphCacheStore);
        _job = new JobObject();
        _pids = new PidTracker();
        _config = _configStore.Load();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        // Warm the graph from cache if a root is already configured (Section 5: read cache on startup).
        if (!string.IsNullOrWhiteSpace(_config.RootPath))
        {
            _graph = _syncService.LoadCachedGraph(_config.RootPath, out _);
        }

        while (!ct.IsCancellationRequested)
        {
            Message? message;
            try
            {
                message = await _channel.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (message is null)
            {
                break; // stdin closed -> UI gone; shut down (Job Object cleans children)
            }

            if (message.Kind != MessageKind.Command)
            {
                continue;
            }

            try
            {
                if (await DispatchAsync(message, ct).ConfigureAwait(false))
                {
                    break; // shutdown requested
                }
            }
            catch (Exception ex)
            {
                _channel.WriteEvent(Events.Error, new ErrorPayload(ex.Message, ex.ToString()), message.CorrelationId);
            }
        }
    }

    private async Task<bool> DispatchAsync(Message message, CancellationToken ct)
    {
        switch (message.Name)
        {
            case Commands.SyncWorkspace:
                HandleSync(message.GetPayload<SyncWorkspacePayload>()?.RootPath ?? _config.RootPath, message.CorrelationId, ct);
                return false;

            case Commands.Reanalyze:
                HandleSync(_config.RootPath, message.CorrelationId, ct);
                return false;

            case Commands.ListBranches:
                await HandleListBranchesAsync(message.CorrelationId, ct).ConfigureAwait(false);
                return false;

            case Commands.SelectBranch:
                _selectedBranch = message.GetPayload<SelectBranchPayload>()?.Branch ?? _selectedBranch;
                return false;

            case Commands.StartRun:
                await HandleStartRunAsync(message.GetPayload<StartRunPayload>()?.Request, message.CorrelationId, ct).ConfigureAwait(false);
                return false;

            case Commands.StopRun:
                HandleStop(message.GetPayload<StopRunPayload>()?.RunId);
                return false;

            case Commands.OpenPath:
                OpenPath(message.GetPayload<OpenPathPayload>()?.ProjectId);
                return false;

            case Commands.OpenInVs:
                OpenInVs(message.GetPayload<OpenInVsPayload>()?.ProjectId);
                return false;

            case Commands.Shutdown:
                HandleStop(_activeRunId);
                return true;

            default:
                _channel.WriteEvent(Events.Error, new ErrorPayload($"Unknown command: {message.Name}", null), message.CorrelationId);
                return false;
        }
    }

    private void HandleSync(string rootPath, string? correlationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            _channel.WriteEvent(Events.Error, new ErrorPayload("No root path configured.", null), correlationId);
            return;
        }

        _config.RootPath = rootPath;
        _configStore.Save(_config);

        var result = _syncService.Reanalyze(rootPath,
            (phase, scanned, total, current) =>
                _channel.WriteEvent(Events.SyncProgress, new SyncProgressPayload(phase, scanned, total, current), correlationId),
            ct);

        _graph = new DependencyGraph(result.Projects);
        _channel.WriteEvent(Events.SyncCompleted, new SyncCompletedPayload(result.Projects, result.HasCycles), correlationId);
    }

    private async Task HandleListBranchesAsync(string? correlationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.RootPath))
        {
            _channel.WriteEvent(Events.Error, new ErrorPayload("No root path configured.", null), correlationId);
            return;
        }

        var git = new GitService(_config.RootPath);
        var branches = await git.ListBranchesAsync(ct).ConfigureAwait(false);
        var current = await git.GetCurrentBranchAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(_selectedBranch))
        {
            _selectedBranch = current; // Section 6: user's active branch is selected on startup
        }
        _channel.WriteEvent(Events.BranchList, new BranchListPayload(branches, current), correlationId);
    }

    private async Task HandleStartRunAsync(RunRequest? request, string? correlationId, CancellationToken ct)
    {
        if (request is null)
        {
            _channel.WriteEvent(Events.Error, new ErrorPayload("Missing run request.", null), correlationId);
            return;
        }

        if (_graph is null)
        {
            _channel.WriteEvent(Events.Error, new ErrorPayload("Run a Sync first.", null), correlationId);
            return;
        }

        // Section 6: only a single run at a time.
        if (!await _runGate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            _channel.WriteEvent(Events.Error, new ErrorPayload("A run is already in progress.", null), correlationId);
            return;
        }

        var runId = Guid.NewGuid().ToString("N");
        _activeRunId = runId;
        _activeRunCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _activeRunCts.Token;

        try
        {
            var branch = string.IsNullOrEmpty(request.Branch) ? _selectedBranch : request.Branch;
            var workingDir = await PrepareWorkingDirAsync(branch, token).ConfigureAwait(false);
            var git = new GitService(_config.RootPath);

            var planner = new IncrementalPlanner(_graph);
            IReadOnlyList<ProjectDecision> plan;

            if (request.Mode == BuildMode.Rebuild)
            {
                plan = planner.PlanRebuild();
            }
            else
            {
                var commit = await git.GetCurrentCommitAsync("HEAD", token).ConfigureAwait(false);
                var changes = await git.GetStatusChangesAsync(workingDir, token).ConfigureAwait(false);
                var mapper = new ProjectFileMapper(_graph.Nodes);
                var dirty = mapper.MapToDirtyProjects(changes, _config.RootPath);

                plan = planner.PlanBuild(new IncrementalContext
                {
                    Branch = branch,
                    CurrentCommit = commit,
                    LocallyDirty = dirty,
                    DependentMode = request.DependentMode,
                    StateLookup = id => _buildStateStore.Get(id, branch)
                });
            }

            var engineRegistry = new System.Collections.Concurrent.ConcurrentDictionary<string, MsBuildEngine>(StringComparer.Ordinal);
            var runner = BuildRunner.CreateWithMsBuild(_graph, _job, engineRegistry);
            runner.IntermediateOutputResolver = id => ResolveObjPath(id, workingDir);

            await runner.RunAsync(
                runId,
                plan,
                request.Config,
                request.Performance,
                this,
                (projectId, _, status, _) =>
                {
                    PersistState(projectId, branch, status, token);
                    return Task.CompletedTask;
                },
                token).ConfigureAwait(false);
        }
        finally
        {
            _pids.SweepTracked();
            _pids.SweepStragglers();
            _activeRunCts?.Dispose();
            _activeRunCts = null;
            _activeRunId = null;
            _runGate.Release();
        }
    }

    private void PersistState(string projectId, string branch, ProjectStatus status, CancellationToken ct)
    {
        string? commit = null;
        if (status == ProjectStatus.Succeeded)
        {
            try
            {
                commit = new GitService(_config.RootPath).GetCurrentCommitAsync("HEAD", ct).GetAwaiter().GetResult();
            }
            catch
            {
                // leave commit null if git unavailable
            }
        }

        _buildStateStore.Set(new BuildState
        {
            ProjectId = projectId,
            Branch = branch,
            LastResult = status,
            LastBuiltCommit = status == ProjectStatus.Succeeded ? commit : _buildStateStore.Get(projectId, branch)?.LastBuiltCommit,
            LastRunAt = DateTimeOffset.UtcNow
        });
    }

    private async Task<string> PrepareWorkingDirAsync(string branch, CancellationToken ct)
    {
        if (_config.BranchMode != BranchMode.Worktree || string.IsNullOrEmpty(branch))
        {
            return _config.RootPath;
        }

        var git = new GitService(_config.RootPath);
        var current = await git.GetCurrentBranchAsync(ct).ConfigureAwait(false);
        if (string.Equals(current, branch, StringComparison.Ordinal))
        {
            // Building the active branch: use the main tree (read-only operations only).
            return _config.RootPath;
        }

        var worktree = _paths.WorktreeFor(branch);
        await git.PrepareWorktreeAsync(branch, worktree, ct).ConfigureAwait(false);
        return worktree;
    }

    /// <summary>Section 4: isolate obj inside the working dir; OutDir is never touched.</summary>
    private string? ResolveObjPath(string projectId, string workingDir)
    {
        if (_graph is null || !_graph.TryGet(projectId, out var node))
        {
            return null;
        }

        var rel = Path.GetRelativePath(_config.RootPath, Path.GetDirectoryName(node.ProjectPath) ?? _config.RootPath);
        return Path.Combine(workingDir, ".bo-obj", rel, "obj");
    }

    private void HandleStop(string? runId)
    {
        _ = runId;
        _activeRunCts?.Cancel();
    }

    private void OpenPath(string? projectId)
    {
        if (projectId is null || _graph is null || !_graph.TryGet(projectId, out var node))
        {
            return;
        }
        var dir = Path.GetDirectoryName(node.ProjectPath);
        if (dir is null)
        {
            return;
        }

        TryShellOpen(OperatingSystem.IsWindows() ? "explorer.exe" : "xdg-open", dir);
    }

    private void OpenInVs(string? projectId)
    {
        if (projectId is null || _graph is null || !_graph.TryGet(projectId, out var node))
        {
            return;
        }

        // Prefer the owning solution; fall back to the project file.
        var target = node.ProjectPath;
        if (OperatingSystem.IsWindows())
        {
            TryShellOpen("cmd.exe", "/c", "start", "", target);
        }
        else
        {
            TryShellOpen("xdg-open", target);
        }
    }

    private static void TryShellOpen(string file, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = file, UseShellExecute = false };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }
            Process.Start(psi);
        }
        catch
        {
            // best effort; opening UI helpers must never crash the worker
        }
    }

    // ---- IRunEventSink ----

    public void RunStarted(string runId, IReadOnlyList<string> plannedProjectIds)
        => _channel.WriteEvent(Events.RunStarted, new RunStartedPayload(runId, plannedProjectIds));

    public void ProjectStarted(string runId, string projectId)
        => _channel.WriteEvent(Events.ProjectStarted, new ProjectStartedPayload(runId, projectId));

    public void ProjectLog(string runId, string projectId, string line, bool isError)
        => _channel.WriteEvent(Events.ProjectLog, new ProjectLogPayload(runId, projectId, line, isError));

    public void ProjectSucceeded(string runId, string projectId, long elapsedMs)
        => _channel.WriteEvent(Events.ProjectSucceeded, new ProjectSucceededPayload(runId, projectId, null, elapsedMs));

    public void ProjectFailed(string runId, string projectId, string reason, long elapsedMs)
        => _channel.WriteEvent(Events.ProjectFailed, new ProjectFailedPayload(runId, projectId, reason, elapsedMs));

    public void ProjectSkipped(string runId, string projectId, string reason)
        => _channel.WriteEvent(Events.ProjectSkipped, new ProjectSkippedPayload(runId, projectId, reason));

    public void RunCompleted(string runId, RunSummary s)
        => _channel.WriteEvent(Events.RunCompleted,
            new RunCompletedPayload(runId, s.Total, s.Built, s.Succeeded, s.Failed, s.Skipped, s.ElapsedMs));

    public void RunCancelled(string runId, string reason)
        => _channel.WriteEvent(Events.RunCancelled, new RunCancelledPayload(runId, reason));

    public void Dispose()
    {
        _activeRunCts?.Cancel();
        _pids.SweepTracked();
        // Closing the Job Object terminates any surviving child processes (Section 6.1).
        _job.Dispose();
    }
}
