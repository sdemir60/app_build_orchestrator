using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Discovery;
using Microsoft.Build.Execution;

namespace BuildOrchestrator.Worker.Building;

public sealed record BuildEngineResult(int Succeeded, int Failed, bool Cancelled);

/// <summary>
/// Parallel MSBuild engine (Sections 6, 6.1) using <see cref="BuildManager"/>:
/// independent projects build concurrently (limited by performance mode); dependent projects
/// are gated until their dependencies finish. Cancellation routes through
/// <see cref="BuildManager.CancelAllSubmissions"/>.
/// </summary>
public sealed class MsBuildBuildEngine
{
    public event Action<string>? ProjectStarted;
    public event Action<string, string, bool>? ProjectLog;   // id, line, isError
    public event Action<string, bool>? ProjectFinished;       // id, success

    public BuildEngineResult Run(
        DependencyGraph graph,
        IReadOnlyList<string> orderedToBuild,
        RunRequest request,
        string? objBasePath,
        bool errorsOnly,
        CancellationToken cancellationToken)
    {
        var toBuild = new HashSet<string>(orderedToBuild);
        var byId = graph.Projects.ToDictionary(p => p.Id);

        // Path -> id map for the routing logger.
        var pathToId = graph.Projects.ToDictionary(
            p => ProjectId.Normalize(p.ProjectPath), p => p.Id, StringComparer.OrdinalIgnoreCase);
        string? PathToId(string path) =>
            pathToId.TryGetValue(ProjectId.Normalize(path), out var id) ? id : null;

        var logger = new ProjectRoutingLogger(
            PathToId,
            (id, line, isError) => ProjectLog?.Invoke(id, line, isError),
            errorsOnly);

        var profile = PerformanceProfile.For(request.Performance);
        profile.ApplyToCurrentProcess();

        var manager = BuildManager.DefaultBuildManager;
        var buildParams = new BuildParameters
        {
            MaxNodeCount = profile.MaxParallel,
            EnableNodeReuse = false,
            DisableInProcNode = false,
            Loggers = new[] { logger }
        };

        // Dependency gating state (within the toBuild set only).
        var remaining = new Dictionary<string, int>();
        var dependents = new Dictionary<string, List<string>>();
        foreach (var id in toBuild)
        {
            dependents[id] = new List<string>();
            remaining[id] = 0;
        }
        foreach (var id in toBuild)
        {
            foreach (var dep in byId[id].Dependencies)
            {
                if (!toBuild.Contains(dep))
                    continue;
                remaining[id]++;
                dependents[dep].Add(id);
            }
        }

        var completions = toBuild.ToDictionary(id => id, _ => new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously));
        int succeeded = 0, failed = 0;
        var gate = new object();
        bool cancelled = false;

        using var ctr = cancellationToken.Register(() =>
        {
            cancelled = true;
            try { manager.CancelAllSubmissions(); } catch { /* ignore */ }
        });

        void Submit(string id)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completions[id].TrySetResult(false);
                return;
            }

            var project = byId[id];
            ProjectStarted?.Invoke(id);

            var globalProps = new Dictionary<string, string?>
            {
                ["Configuration"] = request.Config.ToString(),
                ["UseSharedCompilation"] = "false",      // Section 6.1 rule 3
                ["BuildProjectReferences"] = "false"     // we control ordering ourselves
            };
            if (!string.IsNullOrEmpty(objBasePath))
            {
                // obj isolation per worktree (Section 4): only intermediate output is redirected.
                var objDir = Path.Combine(objBasePath, project.Name) + Path.DirectorySeparatorChar;
                globalProps["BaseIntermediateOutputPath"] = objDir;
                globalProps["IntermediateOutputPath"] = objDir;
            }

            var target = request.Mode == BuildMode.Rebuild ? "Rebuild" : "Build";
            var data = new BuildRequestData(project.ProjectPath, globalProps, null, new[] { target }, null);

            try
            {
                var submission = manager.PendBuildRequest(data);
                submission.ExecuteAsync(s =>
                {
                    bool ok = s.BuildResult is { OverallResult: BuildResultCode.Success };
                    lock (gate)
                    {
                        if (ok) succeeded++; else failed++;
                    }
                    ProjectFinished?.Invoke(id, ok);
                    completions[id].TrySetResult(ok);
                    ReleaseDependents(id);
                }, null);
            }
            catch (Exception ex)
            {
                ProjectLog?.Invoke(id, $"build submission failed: {ex.Message}", true);
                lock (gate) { failed++; }
                ProjectFinished?.Invoke(id, false);
                completions[id].TrySetResult(false);
                ReleaseDependents(id);
            }
        }

        void ReleaseDependents(string id)
        {
            lock (gate)
            {
                foreach (var dep in dependents[id])
                {
                    if (--remaining[dep] == 0)
                        Submit(dep);
                }
            }
        }

        manager.BeginBuild(buildParams);
        try
        {
            // Kick off all projects whose dependencies are already satisfied.
            lock (gate)
            {
                foreach (var id in orderedToBuild)
                    if (remaining[id] == 0)
                        Submit(id);
            }

            Task.WhenAll(completions.Values.Select(c => c.Task)).WaitAsync(cancellationToken).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Stop was requested: stop waiting on in-flight submissions. CancelAllSubmissions
            // (fired by the token registration) tells MSBuild to abandon them; EndBuild() below
            // tears the build down so the run reports as cancelled promptly instead of hanging.
        }
        finally
        {
            try { manager.EndBuild(); } catch { /* ignore */ }
        }

        return new BuildEngineResult(succeeded, failed, cancelled);
    }
}
