using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildOrchestrator.Contracts;
using Message = BuildOrchestrator.Contracts.Message;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// Launches and communicates with the out-of-process Worker over stdio using the Section 8 JSON
/// protocol. The build load runs entirely in the Worker; if the Worker crashes the UI stays alive and
/// a <see cref="WorkerExited"/> event is raised so the UI can recover (Section 2).
/// </summary>
public sealed class WorkerClient : IDisposable
{
    private readonly object _writeGate = new();
    private Process? _process;
    private CancellationTokenSource? _readCts;

    // Strongly-typed events surfaced to the view model.
    public event Action<SyncProgressPayload>? SyncProgress;
    public event Action<SyncCompletedPayload>? SyncCompleted;
    public event Action<BranchListPayload>? BranchList;
    public event Action<RunStartedPayload>? RunStarted;
    public event Action<ProjectStartedPayload>? ProjectStarted;
    public event Action<ProjectLogPayload>? ProjectLog;
    public event Action<ProjectSucceededPayload>? ProjectSucceeded;
    public event Action<ProjectFailedPayload>? ProjectFailed;
    public event Action<ProjectSkippedPayload>? ProjectSkipped;
    public event Action<RunCompletedPayload>? RunCompleted;
    public event Action<RunCancelledPayload>? RunCancelled;
    public event Action<ErrorPayload>? Error;
    public event Action? WorkerExited;

    public bool IsRunning => _process is { HasExited: false };

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        var workerPath = WorkerLocator.Resolve();
        var psi = new ProcessStartInfo
        {
            FileName = workerPath.FileName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false)
        };
        foreach (var arg in workerPath.Arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Exited += (_, _) => WorkerExited?.Invoke();
        _process.Start();

        _readCts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoopAsync(_process.StandardOutput, _readCts.Token));
    }

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null)
            {
                break; // worker stdout closed
            }
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            Message? message;
            try
            {
                message = System.Text.Json.JsonSerializer.Deserialize<Message>(line, ProtocolJson.Options);
            }
            catch
            {
                continue;
            }

            if (message is { Kind: MessageKind.Event })
            {
                Dispatch(message);
            }
        }
    }

    private void Dispatch(Message m)
    {
        switch (m.Name)
        {
            case Events.SyncProgress: Raise(SyncProgress, m); break;
            case Events.SyncCompleted: Raise(SyncCompleted, m); break;
            case Events.BranchList: Raise(BranchList, m); break;
            case Events.RunStarted: Raise(RunStarted, m); break;
            case Events.ProjectStarted: Raise(ProjectStarted, m); break;
            case Events.ProjectLog: Raise(ProjectLog, m); break;
            case Events.ProjectSucceeded: Raise(ProjectSucceeded, m); break;
            case Events.ProjectFailed: Raise(ProjectFailed, m); break;
            case Events.ProjectSkipped: Raise(ProjectSkipped, m); break;
            case Events.RunCompleted: Raise(RunCompleted, m); break;
            case Events.RunCancelled: Raise(RunCancelled, m); break;
            case Events.Error: Raise(Error, m); break;
        }
    }

    private static void Raise<T>(Action<T>? handler, Message m)
    {
        if (handler is null)
        {
            return;
        }
        var payload = m.GetPayload<T>();
        if (payload is not null)
        {
            handler(payload);
        }
    }

    public void Send(Message message)
    {
        if (_process is null || _process.HasExited)
        {
            return;
        }

        var line = System.Text.Json.JsonSerializer.Serialize(message, ProtocolJson.Options);
        lock (_writeGate)
        {
            _process.StandardInput.Write(line);
            _process.StandardInput.Write('\n');
            _process.StandardInput.Flush();
        }
    }

    public void SyncWorkspace(string rootPath) => Send(Message.Command(Commands.SyncWorkspace, new SyncWorkspacePayload(rootPath)));
    public void Reanalyze() => Send(Message.Command(Commands.Reanalyze));
    public void ListBranches() => Send(Message.Command(Commands.ListBranches));
    public void SelectBranch(string branch) => Send(Message.Command(Commands.SelectBranch, new SelectBranchPayload(branch)));
    public void StartRun(RunRequest request) => Send(Message.Command(Commands.StartRun, new StartRunPayload(request)));
    public void StopRun(string runId) => Send(Message.Command(Commands.StopRun, new StopRunPayload(runId)));
    public void OpenPath(string projectId) => Send(Message.Command(Commands.OpenPath, new OpenPathPayload(projectId)));
    public void OpenInVs(string projectId) => Send(Message.Command(Commands.OpenInVs, new OpenInVsPayload(projectId)));

    public void Dispose()
    {
        try
        {
            // Ask the worker to shut down; its Job Object guarantees child processes die regardless.
            Send(Message.Command(Commands.Shutdown));
            _readCts?.Cancel();

            if (_process is { HasExited: false })
            {
                if (!_process.WaitForExit(2000))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
            // best effort
        }
        finally
        {
            _process?.Dispose();
        }
    }
}

/// <summary>Locates the Worker executable next to the app, with dev fallbacks.</summary>
internal static class WorkerLocator
{
    public sealed record Target(string FileName, IReadOnlyList<string> Arguments);

    public static Target Resolve()
    {
        var baseDir = AppContext.BaseDirectory;

        // 1) Published/packaged next to the app.
        var exe = Path.Combine(baseDir, "BuildOrchestrator.Worker.exe");
        if (File.Exists(exe))
        {
            return new Target(exe, Array.Empty<string>());
        }

        var dll = Path.Combine(baseDir, "BuildOrchestrator.Worker.dll");
        if (File.Exists(dll))
        {
            return new Target("dotnet", new[] { dll });
        }

        // 2) Dev fallback: run the Worker project via `dotnet run`.
        var workerProj = Path.GetFullPath(Path.Combine(baseDir,
            "..", "..", "..", "..", "BuildOrchestrator.Worker", "BuildOrchestrator.Worker.csproj"));
        if (File.Exists(workerProj))
        {
            return new Target("dotnet", new[] { "run", "--project", workerProj, "-c", "Debug" });
        }

        // Last resort: assume it is on PATH.
        return new Target("BuildOrchestrator.Worker", Array.Empty<string>());
    }
}
