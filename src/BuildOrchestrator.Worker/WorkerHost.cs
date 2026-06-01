using System.Diagnostics;
using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Configuration;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.Persistence;
using BuildOrchestrator.Worker.Building;
using BuildOrchestrator.Worker.Ipc;
using BuildOrchestrator.Worker.ProcessControl;

namespace BuildOrchestrator.Worker;

/// <summary>
/// Orchestrates the Worker side of the contract (Section 8): handles commands, runs sync and
/// builds, guarantees a single active run, and emits events back to the UI.
/// </summary>
public sealed class WorkerHost
{
    private readonly IpcChannel _channel;
    private readonly ProjectScanner _scanner = new();
    private readonly DependencyGraphBuilder _graphBuilder = new();
    private readonly GraphStore _graphStore = new();
    private readonly BuildStateStore _stateStore = new();
    private readonly GitService _git = new();
    private readonly DiffAnalyzer _diff = new();
    private readonly IncrementalPlanner _planner = new();

    private readonly object _runLock = new();
    private CancellationTokenSource? _activeRun;
    private bool _isRunning;

    private DependencyGraph? _graph;
    private string? _rootPath;
    private string? _repoRoot;
    private string _selectedBranch = string.Empty;

    private static readonly TimeSpan GracefulStopTimeout = TimeSpan.FromSeconds(5);

    public WorkerHost(IpcChannel channel)
    {
        _channel = channel;
        _graph = _graphStore.Load();
        _rootPath = _graph?.RootPath;
    }

    public void Run()
    {
        foreach (var command in _channel.ReadCommands())
        {
            try
            {
                Dispatch(command);
            }
            catch (Exception ex)
            {
                _channel.Send(new WorkerErrorEvent($"Command failed: {command.GetType().Name}", ex.Message));
            }

            if (command is ShutdownCommand)
                break;
        }
    }

    private void Dispatch(WorkerCommand command)
    {
        switch (command)
        {
            case SyncWorkspaceCommand sync:
                _rootPath = sync.RootPath;
                Reanalyze();
                break;
            case ReanalyzeCommand:
                Reanalyze();
                break;
            case ListBranchesCommand:
                SendBranches();
                break;
            case SelectBranchCommand sb:
                _selectedBranch = sb.Branch;
                break;
            case StartRunCommand start:
                StartRun(start);
                break;
            case StopRunCommand:
                StopRun();
                break;
            case OpenPathCommand op:
                OpenPath(op.ProjectId);
                break;
            case OpenInVSCommand ov:
                OpenInVS(ov.ProjectId);
                break;
            case ShutdownCommand:
                StopRun();
                break;
        }
    }

    // ---- Sync (Section 5) ----

    private void Reanalyze()
    {
        if (string.IsNullOrWhiteSpace(_rootPath) || !Directory.Exists(_rootPath))
        {
            _channel.Send(new WorkerErrorEvent("Root path is not set or does not exist."));
            return;
        }

        _channel.Send(new SyncProgressEvent("Scanning workspace...", 0, 0));
        var scan = _scanner.Scan(_rootPath);
        _channel.Send(new SyncProgressEvent($"Found {scan.ProjectFiles.Count} projects, building graph...",
            0, scan.ProjectFiles.Count));

        var graph = _graphBuilder.Build(_rootPath, scan,
            (name, current, total) => _channel.Send(new SyncProgressEvent(name, current, total)));

        _graph = graph;
        _graphStore.Save(graph);

        _repoRoot = _git.FindRepoRootAsync(_rootPath).GetAwaiter().GetResult();
        if (_repoRoot != null && string.IsNullOrEmpty(_selectedBranch))
            _selectedBranch = _git.GetCurrentBranchAsync(_repoRoot).GetAwaiter().GetResult() ?? string.Empty;

        _channel.Send(new SyncCompletedEvent(graph));
        SendBranches();
    }

    private void SendBranches()
    {
        _repoRoot ??= _rootPath != null ? _git.FindRepoRootAsync(_rootPath).GetAwaiter().GetResult() : null;
        if (_repoRoot == null)
        {
            _channel.Send(new BranchListEvent(new List<BranchInfo>()));
            return;
        }
        var branches = _git.ListBranchesAsync(_repoRoot).GetAwaiter().GetResult();
        if (string.IsNullOrEmpty(_selectedBranch))
            _selectedBranch = branches.FirstOrDefault(b => b.IsCurrent)?.Name ?? string.Empty;
        _channel.Send(new BranchListEvent(branches));
    }

    // ---- Run (Sections 6, 6.1) ----

    private void StartRun(StartRunCommand start)
    {
        lock (_runLock)
        {
            if (_isRunning)
            {
                _channel.Send(new WorkerErrorEvent("A run is already in progress."));
                return;
            }
            _isRunning = true;
            _activeRun = new CancellationTokenSource();
        }

        var ct = _activeRun!.Token;
        // Run off the IPC-reading thread so Stop can still be processed.
        // Observe the task so a JIT/type-load failure (e.g. MSBuild assemblies) is reported
        // instead of being silently swallowed as an unobserved task exception.
        Task.Run(() => ExecuteRun(start.RunId, start.Request, ct))
            .ContinueWith(t =>
            {
                var ex = t.Exception?.GetBaseException();
                if (ex != null)
                {
                    _channel.Send(new WorkerErrorEvent("Run crashed", ex.Message));
                    _channel.Send(new RunCancelledEvent(start.RunId));
                    lock (_runLock)
                    {
                        _isRunning = false;
                        _activeRun?.Dispose();
                        _activeRun = null;
                    }
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private void ExecuteRun(string runId, RunRequest request, CancellationToken ct)
    {
        try
        {
            if (_graph is null)
            {
                _channel.Send(new WorkerErrorEvent("No dependency graph. Run Sync first."));
                return;
            }

            var branch = string.IsNullOrEmpty(request.Branch) ? _selectedBranch : request.Branch;
            _repoRoot ??= _rootPath != null ? _git.FindRepoRootAsync(_rootPath).GetAwaiter().GetResult() : null;

            // Resolve build working tree + obj isolation (Section 4 / 6).
            string buildRoot = _repoRoot ?? _rootPath!;
            string? objBasePath = null;
            DependencyGraph buildGraph = _graph;

            if (request.BranchWorkMode == BranchWorkMode.Worktree && _repoRoot != null && !string.IsNullOrEmpty(branch))
            {
                var worktree = AppPaths.WorktreeFor(branch);
                _git.EnsureWorktreeAsync(_repoRoot, branch, worktree, ct).GetAwaiter().GetResult();
                buildRoot = worktree;
                objBasePath = Path.Combine(worktree, ".bo-obj");
                buildGraph = TranslateGraphTo(_graph, _repoRoot, worktree);
            }

            // Determine which projects to build.
            string? currentCommit = _repoRoot != null
                ? _git.GetCommitAsync(_repoRoot, branch, ct).GetAwaiter().GetResult()
                : null;

            List<string> ordered;
            HashSet<string> skipped = new();

            if (request.Mode == BuildMode.Rebuild)
            {
                ordered = _graph.Projects.OrderBy(p => p.BuildOrder).Select(p => p.Id).ToList();
            }
            else
            {
                var locallyDirty = ComputeLocallyDirty(_graph, ct);
                var plan = _planner.Plan(_graph, _stateStore, branch, currentCommit, locallyDirty, request.DependentMode);
                ordered = plan.ToBuild.ToList();
                skipped = new HashSet<string>(plan.ToSkip);
            }

            _channel.Send(new RunStartedEvent(runId, _graph.Projects.Count, ordered.Count));
            foreach (var id in skipped)
                _channel.Send(new ProjectSkippedEvent(runId, id, "no source change"));

            if (ordered.Count == 0)
            {
                _channel.Send(new RunCompletedEvent(runId, 0, 0, skipped.Count));
                return;
            }

            var engine = new MsBuildBuildEngine();
            engine.ProjectStarted += id => _channel.Send(new ProjectStartedEvent(runId, id));
            engine.ProjectLog += (id, line, isError) => _channel.Send(new ProjectLogEvent(runId, id, line, isError));
            engine.ProjectFinished += (id, ok) =>
            {
                RecordResult(id, branch, currentCommit, ok);
                if (ok)
                    _channel.Send(new ProjectSucceededEvent(runId, id, currentCommit));
                else
                    _channel.Send(new ProjectFailedEvent(runId, id, "build failed"));
            };

            bool errorsOnly = false; // capture full per-project output; UI shows summaries unless a card is selected
            var result = engine.Run(buildGraph, ordered, request, objBasePath, errorsOnly, ct);
            _stateStore.Save();

            if (result.Cancelled || ct.IsCancellationRequested)
                _channel.Send(new RunCancelledEvent(runId));
            else
                _channel.Send(new RunCompletedEvent(runId, result.Succeeded, result.Failed, skipped.Count));
        }
        catch (Exception ex)
        {
            _channel.Send(new WorkerErrorEvent("Run failed", ex.Message));
            _channel.Send(new RunCancelledEvent(runId));
        }
        finally
        {
            lock (_runLock)
            {
                _isRunning = false;
                _activeRun?.Dispose();
                _activeRun = null;
            }
        }
    }

    private HashSet<string> ComputeLocallyDirty(DependencyGraph graph, CancellationToken ct)
    {
        if (_repoRoot == null)
            return new HashSet<string>();
        var changed = _git.GetChangedFilesAsync(_repoRoot, ct).GetAwaiter().GetResult();
        return _diff.GetDirtyProjects(graph, changed);
    }

    private void RecordResult(string id, string branch, string? commit, bool ok)
    {
        _stateStore.Set(new BuildState
        {
            ProjectId = id,
            Branch = branch,
            LastBuiltCommit = ok ? commit : _stateStore.Get(id, branch)?.LastBuiltCommit,
            LastResult = ok ? ProjectStatus.Succeeded : ProjectStatus.Failed,
            LastRunAt = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Clone the graph but repoint each ProjectPath into the worktree (ids/edges unchanged),
    /// so the engine builds the branch's files while final DLLs still land in the shared OutDir.
    /// </summary>
    private static DependencyGraph TranslateGraphTo(DependencyGraph graph, string fromRoot, string toRoot)
    {
        var projects = graph.Projects.Select(p =>
        {
            string newPath = p.ProjectPath;
            var rel = Path.GetRelativePath(fromRoot, p.ProjectPath);
            if (!rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel))
            {
                var candidate = Path.Combine(toRoot, rel);
                if (File.Exists(candidate))
                    newPath = candidate;
            }
            return p with { ProjectPath = newPath };
        }).ToList();

        return graph with { Projects = projects };
    }

    private void StopRun()
    {
        CancellationTokenSource? cts;
        lock (_runLock)
        {
            cts = _activeRun;
            if (cts == null)
                return;
        }

        // Graceful first (Section 6.1 rule 2): CancelAllSubmissions via the token.
        cts.Cancel();

        // Hard fallback after timeout: sweep build child processes (worker stays alive).
        Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < GracefulStopTimeout)
            {
                lock (_runLock)
                    if (!_isRunning)
                        return;
                Thread.Sleep(150);
            }
            ProcessSweeper.SweepDescendants();
        });
    }

    // ---- Open helpers ----

    private void OpenPath(string projectId)
    {
        var project = _graph?.Projects.FirstOrDefault(p => p.Id == projectId);
        if (project == null)
            return;
        var folder = Path.GetDirectoryName(project.ProjectPath);
        if (folder != null && Directory.Exists(folder))
            ShellOpen(folder);
    }

    private void OpenInVS(string projectId)
    {
        var project = _graph?.Projects.FirstOrDefault(p => p.Id == projectId);
        if (project == null)
            return;
        // Prefer the nearest solution; fall back to the project file.
        var target = FindNearestSolution(project.ProjectPath) ?? project.ProjectPath;
        ShellOpen(target);
    }

    private static string? FindNearestSolution(string projectPath)
    {
        var dir = Path.GetDirectoryName(projectPath);
        while (!string.IsNullOrEmpty(dir))
        {
            var sln = Directory.EnumerateFiles(dir, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (sln != null)
                return sln;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static void ShellOpen(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // Best effort.
        }
    }
}
