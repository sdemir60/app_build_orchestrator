using System.Diagnostics;
using System.IO;
using BuildOrchestrator.Contracts;

namespace BuildOrchestrator.App.Ipc;

/// <summary>
/// UI-side IPC client (Section 8). Spawns the Worker process, streams commands to its stdin and
/// raises <see cref="WorkerEvent"/>s parsed from its stdout. The build load never touches the UI
/// thread, and killing the worker tears down its whole build process tree (Section 6.1).
/// </summary>
public sealed class WorkerClient : IDisposable
{
    private Process? _process;
    private StreamWriter? _stdin;
    private Thread? _readThread;
    private Thread? _errThread;
    private readonly object _writeLock = new();
    private bool _disposed;

    /// <summary>Raised for every event from the worker (NOT on the UI thread).</summary>
    public event Action<WorkerEvent>? EventReceived;

    /// <summary>Raised if the worker process exits unexpectedly.</summary>
    public event Action? WorkerExited;

    /// <summary>Raised for diagnostic/lifecycle messages (worker start/exit, stderr lines). NOT on the UI thread.</summary>
    public event Action<string, bool>? Diagnostic;

    public bool IsRunning => _process is { HasExited: false };

    public void Start()
    {
        if (IsRunning)
            return;

        var exePath = ResolveWorkerPath();
        var selfPid = Environment.ProcessId;

        ProcessStartInfo psi;
        if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            psi = new ProcessStartInfo("dotnet")
            {
                ArgumentList = { exePath, "--parent", selfPid.ToString() }
            };
        }
        else
        {
            psi = new ProcessStartInfo(exePath)
            {
                ArgumentList = { "--parent", selfPid.ToString() }
            };
        }

        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.StandardOutputEncoding = System.Text.Encoding.UTF8;
        psi.StandardErrorEncoding = System.Text.Encoding.UTF8;
        psi.WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Exited += (_, _) =>
        {
            if (_disposed)
                return;
            int code = -1;
            try { code = _process?.ExitCode ?? -1; } catch { /* ignore */ }
            Diagnostic?.Invoke($"Worker process exited (code {code}).", code != 0);
            WorkerExited?.Invoke();
        };
        _process.Start();
        _stdin = _process.StandardInput;
        Diagnostic?.Invoke($"Worker started (PID {_process.Id}).", false);

        _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "WorkerReader" };
        _readThread.Start();

        _errThread = new Thread(ErrorLoop) { IsBackground = true, Name = "WorkerError" };
        _errThread.Start();
    }

    private void ErrorLoop()
    {
        var reader = _process!.StandardError;
        string? line;
        try
        {
            while ((line = reader.ReadLine()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    Diagnostic?.Invoke(line, true);
            }
        }
        catch
        {
            // Stream closed (worker exited).
        }
    }

    private void ReadLoop()
    {
        var reader = _process!.StandardOutput;
        string? line;
        try
        {
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                WorkerEvent? evt = null;
                try
                {
                    evt = IpcJson.DeserializeEvent(line);
                }
                catch
                {
                    // Ignore malformed lines.
                }
                if (evt != null)
                    EventReceived?.Invoke(evt);
            }
        }
        catch
        {
            // Stream closed (worker exited).
        }
    }

    public void Send(WorkerCommand command)
    {
        if (_stdin == null)
            return;
        var json = IpcJson.SerializeCommand(command);
        lock (_writeLock)
        {
            try
            {
                _stdin.WriteLine(json);
                _stdin.Flush();
            }
            catch
            {
                // Worker pipe broken; ignore.
            }
        }
    }

    private static string ResolveWorkerPath()
    {
        var baseDir = AppContext.BaseDirectory;

        // Primary: copied into <app>\Worker\ by the build target.
        var candidates = new[]
        {
            Path.Combine(baseDir, "Worker", "BuildOrchestrator.Worker.exe"),
            Path.Combine(baseDir, "Worker", "BuildOrchestrator.Worker.dll"),
            Path.Combine(baseDir, "BuildOrchestrator.Worker.exe"),
            Path.Combine(baseDir, "BuildOrchestrator.Worker.dll")
        };
        foreach (var c in candidates)
            if (File.Exists(c))
                return c;

        // Dev fallback: sibling worker bin.
        var dev = Path.GetFullPath(Path.Combine(baseDir,
            "..", "..", "..", "..", "BuildOrchestrator.Worker", "bin",
            "Debug", "net8.0", "BuildOrchestrator.Worker.exe"));
        return File.Exists(dev) ? dev : candidates[0];
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            Send(new ShutdownCommand());
        }
        catch { /* ignore */ }

        try
        {
            if (_process is { HasExited: false })
            {
                if (!_process.WaitForExit(1500))
                    _process.Kill(entireProcessTree: true); // closes worker job -> kills build tree
            }
        }
        catch { /* ignore */ }
        finally
        {
            _process?.Dispose();
        }
    }
}
