using System.Collections.Concurrent;
using Microsoft.Build.Framework;

namespace BuildOrchestrator.Worker.Building;

/// <summary>
/// A shared MSBuild <see cref="ILogger"/> that attributes log/error/warning events to the
/// originating project (by project file path) so per-project console output works even when
/// many projects build concurrently (Section 7 console behaviour).
/// </summary>
public sealed class ProjectRoutingLogger : ILogger
{
    private readonly Func<string, string?> _pathToProjectId;
    private readonly Action<string, string, bool> _onLog; // projectId, line, isError
    private readonly bool _errorsOnly;

    // ProjectContextId -> project file path
    private readonly ConcurrentDictionary<int, string> _contextToPath = new();

    public ProjectRoutingLogger(
        Func<string, string?> pathToProjectId,
        Action<string, string, bool> onLog,
        bool errorsOnly)
    {
        _pathToProjectId = pathToProjectId;
        _onLog = onLog;
        _errorsOnly = errorsOnly;
    }

    public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Normal;
    public string? Parameters { get; set; }

    public void Initialize(IEventSource eventSource)
    {
        eventSource.ProjectStarted += (_, e) =>
        {
            if (e.BuildEventContext != null)
                _contextToPath[e.BuildEventContext.ProjectContextId] = e.ProjectFile ?? string.Empty;
        };

        eventSource.ErrorRaised += (_, e) =>
        {
            var id = Resolve(e.BuildEventContext, e.ProjectFile);
            if (id != null)
                _onLog(id, FormatError(e), true);
        };

        eventSource.WarningRaised += (_, e) =>
        {
            if (_errorsOnly)
                return;
            var id = Resolve(e.BuildEventContext, e.ProjectFile);
            if (id != null)
                _onLog(id, FormatWarning(e), false);
        };

        eventSource.MessageRaised += (_, e) =>
        {
            if (_errorsOnly)
                return;
            if (e.Importance == MessageImportance.Low)
                return;
            var id = Resolve(e.BuildEventContext, null);
            if (id != null)
                _onLog(id, e.Message ?? string.Empty, false);
        };
    }

    private string? Resolve(BuildEventContext? ctx, string? projectFile)
    {
        if (!string.IsNullOrEmpty(projectFile))
        {
            var direct = _pathToProjectId(projectFile);
            if (direct != null)
                return direct;
        }
        if (ctx != null && _contextToPath.TryGetValue(ctx.ProjectContextId, out var path) && !string.IsNullOrEmpty(path))
            return _pathToProjectId(path);
        return null;
    }

    private static string FormatError(BuildErrorEventArgs e)
        => $"{Path.GetFileName(e.File)}({e.LineNumber},{e.ColumnNumber}): error {e.Code}: {e.Message}";

    private static string FormatWarning(BuildWarningEventArgs e)
        => $"{Path.GetFileName(e.File)}({e.LineNumber},{e.ColumnNumber}): warning {e.Code}: {e.Message}";

    public void Shutdown() { }
}
