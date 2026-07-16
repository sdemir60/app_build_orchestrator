using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.App.Services;

public sealed class EngineHost(string supervisorExePath) : IAsyncDisposable
{
    private readonly JobObject _outerJob = JobObject.CreateKillOnClose(); // §3: App = outer Job sahibi
    private JobChildProcess? _child;
    private NdjsonWriter? _writer;
    private TaskCompletionSource<EngineReadyEvent>? _ready;
    private int _generation; // restart/dispose sonrası bayat exit bildirimlerini ele

    public event Action<IpcEvent>? EventReceived;
    public event Action<int?>? EngineExited;
    public int? EnginePid => _child?.Pid;

    public async Task<EngineReadyEvent> StartAsync(CancellationToken ct = default)
    {
        int gen = ++_generation;
        _ready = new TaskCompletionSource<EngineReadyEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        string cmdLine = WindowsCommandLine.Build(supervisorExePath);
        _child = JobProcessLauncher.Launch(_outerJob, cmdLine, new LaunchOptions(RedirectStdio: true));
        _writer = new NdjsonWriter(_child.StandardInput!);
        var reader = new NdjsonReader(_child.StandardOutput!);
        _ = Task.Run(() => ReadLoopAsync(reader), CancellationToken.None);
        var watched = _child;
        _ = Task.Run(async () =>
        {
            int code = await watched.WaitForExitAsync(CancellationToken.None);
            if (gen == _generation) EngineExited?.Invoke(code); // bilinçli ölüm (restart/dispose) değilse bildir
        }, CancellationToken.None);
        return await _ready.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
    }

    public Task SendAsync(IpcCommand command, CancellationToken ct = default) =>
        _writer?.WriteAsync(command, ct) ?? throw new InvalidOperationException("Engine başlatılmadı.");

    public async Task<EngineReadyEvent> RestartAsync(CancellationToken ct = default)
    {
        _generation++; // eski watcher sustur
        KillCurrent();
        return await StartAsync(ct);
    }

    private async Task ReadLoopAsync(NdjsonReader reader)
    {
        try
        {
            while (await reader.ReadAsync<IpcEvent>(CancellationToken.None) is { } ev)
            {
                if (ev is EngineReadyEvent ready) _ready?.TrySetResult(ready);
                EventReceived?.Invoke(ev);
            }
        }
        catch { /* stream koptu — exit watcher bildirir */ }
    }

    private void KillCurrent()
    {
        if (_child is null) return;
        try { System.Diagnostics.Process.GetProcessById(_child.Pid).Kill(entireProcessTree: true); }
        catch (ArgumentException) { /* zaten öldü */ }
        _child.Dispose(); _child = null; _writer = null;
    }

    public ValueTask DisposeAsync()
    {
        _generation++;
        KillCurrent();
        _outerJob.Dispose(); // KILL_ON_JOB_CLOSE: her koşulda süpürge
        return ValueTask.CompletedTask;
    }
}
