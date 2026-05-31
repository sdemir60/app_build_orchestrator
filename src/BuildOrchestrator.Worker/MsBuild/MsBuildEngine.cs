using Microsoft.Build.Execution;
using Microsoft.Build.Framework;

namespace BuildOrchestrator.Worker.MsBuild;

/// <summary>Outcome of building a single project.</summary>
public sealed record ProjectBuildResult(bool Success, string? FailureReason);

/// <summary>
/// Builds a single .csproj using a dedicated <see cref="BuildManager"/> so that concurrent project
/// builds (independent nodes in the same wave) are isolated and individually cancellable, and so each
/// project's output can be routed to its own console (Section 2/6/7).
///
/// Critical build properties (Section 4 / 6.1):
///  * <c>UseSharedCompilation=false</c> — no lingering VBCSCompiler.
///  * <c>BaseIntermediateOutputPath</c> — obj is isolated inside the worktree; OutDir is never set.
/// </summary>
public sealed class MsBuildEngine
{
    private readonly BuildManager _manager = new();
    private volatile bool _cancelled;

    /// <summary>The dedicated BuildManager (exposed so a run can cancel all in-flight submissions).</summary>
    public BuildManager Manager => _manager;

    public void Cancel()
    {
        _cancelled = true;
        try
        {
            _manager.CancelAllSubmissions();
        }
        catch
        {
            // manager may not be in a build; ignore
        }
    }

    public ProjectBuildResult Build(
        string projectPath,
        string configuration,
        string? baseIntermediateOutputPath,
        int maxNodeCount,
        Action<string, bool> onLine,
        CancellationToken ct)
    {
        if (_cancelled || ct.IsCancellationRequested)
        {
            return new ProjectBuildResult(false, "cancelled");
        }

        var globalProperties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = configuration,
            // Section 6.1: keep Roslyn server from lingering in the background.
            ["UseSharedCompilation"] = "false",
        };

        // Section 4: isolate intermediate output (obj) per worktree; never touch OutDir.
        if (!string.IsNullOrWhiteSpace(baseIntermediateOutputPath))
        {
            var path = baseIntermediateOutputPath!;
            if (!path.EndsWith(Path.DirectorySeparatorChar) && !path.EndsWith(Path.AltDirectorySeparatorChar))
            {
                path += Path.DirectorySeparatorChar;
            }
            globalProperties["BaseIntermediateOutputPath"] = path;
        }

        var logger = new StreamingLogger(onLine);
        var parameters = new BuildParameters
        {
            Loggers = new ILogger[] { logger },
            MaxNodeCount = Math.Max(1, maxNodeCount),
            EnableNodeReuse = false,         // do not leave reusable nodes behind (Section 6.1)
            DisableInProcNode = false,
            ShutdownInProcNodeOnBuildFinish = true
        };

        try
        {
            var requestData = new BuildRequestData(
                projectFullPath: projectPath,
                globalProperties: globalProperties,
                toolsVersion: null,
                targetsToBuild: new[] { "Build" },
                hostServices: null);

            _manager.BeginBuild(parameters);

            using var reg = ct.Register(() => Cancel());
            var submission = _manager.PendBuildRequest(requestData);
            var result = submission.Execute();

            if (result.OverallResult == BuildResultCode.Success)
            {
                return new ProjectBuildResult(true, null);
            }

            var reason = _cancelled || ct.IsCancellationRequested
                ? "cancelled"
                : result.Exception?.Message ?? "build failed";
            return new ProjectBuildResult(false, reason);
        }
        catch (Exception ex)
        {
            return new ProjectBuildResult(false, ex.Message);
        }
        finally
        {
            try { _manager.EndBuild(); } catch { /* ignore */ }
        }
    }
}
