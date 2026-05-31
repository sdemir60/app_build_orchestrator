using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Graph;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Worker.ProcessControl;

namespace BuildOrchestrator.Worker.MsBuild;

/// <summary>Callbacks the runner uses to emit protocol events (decoupled from transport).</summary>
public interface IRunEventSink
{
    void RunStarted(string runId, IReadOnlyList<string> plannedProjectIds);
    void ProjectStarted(string runId, string projectId);
    void ProjectLog(string runId, string projectId, string line, bool isError);
    void ProjectSucceeded(string runId, string projectId, long elapsedMs);
    void ProjectFailed(string runId, string projectId, string reason, long elapsedMs);
    void ProjectSkipped(string runId, string projectId, string reason);
    void RunCompleted(string runId, RunSummary summary);
    void RunCancelled(string runId, string reason);
}

public sealed record RunSummary(int Total, int Built, int Succeeded, int Failed, int Skipped, long ElapsedMs);

/// <summary>How to map performance mode to project-level concurrency (Section 6 parallelism).</summary>
public static class ParallelDegree
{
    public static int For(PerformanceMode mode) => mode switch
    {
        PerformanceMode.FullPower => Math.Max(1, Environment.ProcessorCount),
        PerformanceMode.Balanced => Math.Max(1, Environment.ProcessorCount / 2),
        PerformanceMode.Light => 1,
        _ => Math.Max(1, Environment.ProcessorCount / 2)
    };
}

/// <summary>
/// Executes a build plan: independent projects run concurrently while dependencies are respected; a
/// single project failure does not stop the queue (Section 6); Stop is graceful first then escalates
/// to terminating the Job Object (Section 6.1).
/// </summary>
public sealed class BuildRunner
{
    private readonly DependencyGraph _graph;
    private readonly JobObject _job;
    private readonly TimeSpan _gracefulStopTimeout;
    private readonly Func<string, string, string?, Action<string, bool>, CancellationToken, Task<ProjectBuildResult>> _buildProject;

    /// <param name="buildProject">
    /// Builds one project: (projectPath, configuration, baseIntermediateOutputPath, logLine, ct).
    /// Injected so the scheduler can be tested without MSBuild; the Worker supplies the real engine.
    /// </param>
    public BuildRunner(
        DependencyGraph graph,
        JobObject job,
        Func<string, string, string?, Action<string, bool>, CancellationToken, Task<ProjectBuildResult>> buildProject,
        TimeSpan? gracefulStopTimeout = null)
    {
        _graph = graph;
        _job = job;
        _buildProject = buildProject;
        _gracefulStopTimeout = gracefulStopTimeout ?? TimeSpan.FromSeconds(5);
    }

    /// <summary>Creates a runner backed by a dedicated <see cref="MsBuildEngine"/> per project.</summary>
    public static BuildRunner CreateWithMsBuild(
        DependencyGraph graph,
        JobObject job,
        System.Collections.Concurrent.ConcurrentDictionary<string, MsBuildEngine> engineRegistry,
        TimeSpan? gracefulStopTimeout = null)
    {
        return new BuildRunner(graph, job, (path, config, obj, log, ct) =>
        {
            var engine = new MsBuildEngine();
            // Track the engine so a Stop can cancel its in-flight submissions.
            var key = path;
            engineRegistry[key] = engine;
            return Task.Run(() =>
            {
                try
                {
                    return engine.Build(path, config, obj, maxNodeCount: 1, onLine: log, ct: ct);
                }
                finally
                {
                    engineRegistry.TryRemove(key, out _);
                }
            }, CancellationToken.None);
        }, gracefulStopTimeout);
    }

    /// <summary>Resolves the isolated obj path for a project inside a worktree (Section 4).</summary>
    public Func<string, string?>? IntermediateOutputResolver { get; set; }

    public async Task<RunSummary> RunAsync(
        string runId,
        IReadOnlyList<ProjectDecision> plan,
        BuildConfiguration configuration,
        PerformanceMode performance,
        IRunEventSink sink,
        Func<string, string, ProjectStatus, string?, Task> persistState,
        CancellationToken stopToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var toBuild = plan.Where(p => p.ShouldBuild).Select(p => p.ProjectId).ToList();
        var toBuildSet = new HashSet<string>(toBuild, StringComparer.Ordinal);

        sink.RunStarted(runId, toBuild);

        // Emit skips up front so the UI can fade them (Section 7).
        var skipped = 0;
        foreach (var decision in plan.Where(p => !p.ShouldBuild))
        {
            sink.ProjectSkipped(runId, decision.ProjectId, "up-to-date");
            skipped++;
        }

        var results = new System.Collections.Concurrent.ConcurrentDictionary<string, ProjectStatus>(StringComparer.Ordinal);
        var remaining = new HashSet<string>(toBuildSet, StringComparer.Ordinal);
        var gate = new SemaphoreSlim(ParallelDegree.For(performance));
        var configName = configuration.ToString();

        var succeeded = 0;
        var failed = 0;
        var built = 0;
        var stopRequested = false;

        // Link an internal escalation token so graceful stop can hard-cancel after the timeout.
        using var hardStop = new CancellationTokenSource();
        using var linkedStop = CancellationTokenSource.CreateLinkedTokenSource(stopToken, hardStop.Token);

        using var stopRegistration = stopToken.Register(() =>
        {
            stopRequested = true;
            // Graceful: in-flight builds observe the cancellation token (MsBuildEngine maps it to
            // CancelAllSubmissions). Escalate: if not finished within the timeout, terminate the
            // whole process tree via the Job Object (Section 6.1 rule 2).
            _ = Task.Run(async () =>
            {
                await Task.Delay(_gracefulStopTimeout).ConfigureAwait(false);
                if (!linkedStop.IsCancellationRequested)
                {
                    hardStop.Cancel();
                }
                _job.Terminate();
            });
        });

        async Task<bool> DependenciesReadyAsync(string id)
        {
            await Task.CompletedTask;
            foreach (var dep in _graph.DependenciesOf(id))
            {
                if (toBuildSet.Contains(dep) && !results.ContainsKey(dep))
                {
                    return false; // dependency still in flight
                }
            }
            return true;
        }

        var inFlight = new List<Task>();

        while (remaining.Count > 0 && !linkedStop.IsCancellationRequested)
        {
            // Find ready projects whose in-plan dependencies have all completed.
            var ready = new List<string>();
            foreach (var id in remaining.ToList())
            {
                if (await DependenciesReadyAsync(id).ConfigureAwait(false))
                {
                    ready.Add(id);
                }
            }

            if (ready.Count == 0)
            {
                // No project is ready yet; wait for an in-flight one to finish.
                if (inFlight.Count == 0)
                {
                    break; // deadlock guard (e.g. cycle) — nothing can progress
                }
                var done = await Task.WhenAny(inFlight).ConfigureAwait(false);
                inFlight.Remove(done);
                continue;
            }

            ready.Sort((a, b) =>
            {
                _graph.TryGet(a, out var na);
                _graph.TryGet(b, out var nb);
                return (na?.BuildOrder ?? 0).CompareTo(nb?.BuildOrder ?? 0);
            });

            foreach (var id in ready)
            {
                remaining.Remove(id);
                var projectId = id;

                var task = Task.Run(async () =>
                {
                    await gate.WaitAsync().ConfigureAwait(false);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        // If an in-plan dependency failed, fail fast without invoking MSBuild.
                        var failedDep = _graph.DependenciesOf(projectId)
                            .FirstOrDefault(d => results.TryGetValue(d, out var s) && s == ProjectStatus.Failed);
                        if (failedDep is not null)
                        {
                            results[projectId] = ProjectStatus.Failed;
                            Interlocked.Increment(ref failed);
                            sink.ProjectFailed(runId, projectId, $"upstream failed: {failedDep}", sw.ElapsedMilliseconds);
                            return;
                        }

                        if (linkedStop.IsCancellationRequested)
                        {
                            results[projectId] = ProjectStatus.Failed;
                            return;
                        }

                        sink.ProjectStarted(runId, projectId);
                        _graph.TryGet(projectId, out var node);
                        var projectPath = node?.ProjectPath ?? projectId;

                        var objPath = IntermediateOutputResolver?.Invoke(projectId);
                        ProjectBuildResult result;
                        try
                        {
                            result = await _buildProject(
                                projectPath,
                                configName,
                                objPath,
                                (line, isError) => sink.ProjectLog(runId, projectId, line, isError),
                                linkedStop.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            // Stop requested mid-build: record as cancelled, do not fault the run.
                            results[projectId] = ProjectStatus.Failed;
                            return;
                        }
                        catch (Exception ex)
                        {
                            results[projectId] = ProjectStatus.Failed;
                            Interlocked.Increment(ref failed);
                            sink.ProjectFailed(runId, projectId, ex.Message, sw.ElapsedMilliseconds);
                            return;
                        }

                        Interlocked.Increment(ref built);

                        if (result.Success)
                        {
                            results[projectId] = ProjectStatus.Succeeded;
                            Interlocked.Increment(ref succeeded);
                            await persistState(projectId, runId, ProjectStatus.Succeeded, null).ConfigureAwait(false);
                            sink.ProjectSucceeded(runId, projectId, sw.ElapsedMilliseconds);
                        }
                        else
                        {
                            results[projectId] = ProjectStatus.Failed;
                            Interlocked.Increment(ref failed);
                            await persistState(projectId, runId, ProjectStatus.Failed, result.FailureReason).ConfigureAwait(false);
                            sink.ProjectFailed(runId, projectId, result.FailureReason ?? "build failed", sw.ElapsedMilliseconds);
                        }
                    }
                    finally
                    {
                        gate.Release();
                    }
                }, CancellationToken.None);

                inFlight.Add(task);
            }
        }

        await Task.WhenAll(inFlight).ConfigureAwait(false);

        var elapsed = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
        var summary = new RunSummary(plan.Count, built, succeeded, failed, skipped, elapsed);

        if (stopRequested)
        {
            sink.RunCancelled(runId, "stopped by user");
        }
        else
        {
            sink.RunCompleted(runId, summary);
        }

        return summary;
    }
}
