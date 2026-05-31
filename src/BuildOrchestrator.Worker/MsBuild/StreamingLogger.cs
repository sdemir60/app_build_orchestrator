using Microsoft.Build.Framework;

namespace BuildOrchestrator.Worker.MsBuild;

/// <summary>
/// MSBuild <see cref="ILogger"/> that forwards output line-by-line to a callback so the UI can show
/// live console output. Errors are flagged so the UI can honor the "errors-only" default (Section 7).
/// </summary>
public sealed class StreamingLogger : ILogger
{
    private readonly Action<string, bool> _onLine;
    private IEventSource? _source;

    public StreamingLogger(Action<string, bool> onLine, LoggerVerbosity verbosity = LoggerVerbosity.Minimal)
    {
        _onLine = onLine;
        Verbosity = verbosity;
    }

    public LoggerVerbosity Verbosity { get; set; }
    public string? Parameters { get; set; }

    public void Initialize(IEventSource eventSource)
    {
        _source = eventSource;
        eventSource.ErrorRaised += OnError;
        eventSource.WarningRaised += OnWarning;
        eventSource.MessageRaised += OnMessage;
    }

    private void OnError(object sender, BuildErrorEventArgs e)
        => _onLine($"{e.File}({e.LineNumber},{e.ColumnNumber}): error {e.Code}: {e.Message}", true);

    private void OnWarning(object sender, BuildWarningEventArgs e)
        => _onLine($"{e.File}({e.LineNumber},{e.ColumnNumber}): warning {e.Code}: {e.Message}", false);

    private void OnMessage(object sender, BuildMessageEventArgs e)
    {
        // Only surface normal/high importance to keep the stream readable; the UI's full-log toggle
        // still receives these, while low-importance noise is dropped.
        if (e.Importance != MessageImportance.Low)
        {
            _onLine(e.Message ?? string.Empty, false);
        }
    }

    public void Shutdown()
    {
        if (_source is not null)
        {
            _source.ErrorRaised -= OnError;
            _source.WarningRaised -= OnWarning;
            _source.MessageRaised -= OnMessage;
        }
    }
}
