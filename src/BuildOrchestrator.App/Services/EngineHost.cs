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
    private volatile int _generation; // cross-thread okuma → volatile [it0-devir]
    private int _exitReported; // her generation'da bir kez EngineExited — çift raporu engelle [it0-devir]

    public event Action<IpcEvent>? EventReceived;
    public event Action<int?>? EngineExited;
    public int? EnginePid => _child?.Pid;

    public async Task<EngineReadyEvent> StartAsync(CancellationToken ct = default)
    {
        int gen = Interlocked.Increment(ref _generation);
        Interlocked.Exchange(ref _exitReported, 0); // yeni engine → tek raporculuk hakkı sıfırlanır [it0-devir]
        _ready = new TaskCompletionSource<EngineReadyEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        string cmdLine = WindowsCommandLine.Build(supervisorExePath);
        _child = JobProcessLauncher.Launch(_outerJob, cmdLine, new LaunchOptions(RedirectStdio: true));
        _writer = new NdjsonWriter(_child.StandardInput!);
        var reader = new NdjsonReader(_child.StandardOutput!);
        _ = Task.Run(() => ReadLoopAsync(reader, gen), CancellationToken.None);
        var watched = _child;
        _ = Task.Run(async () =>
        {
            int code = await watched.WaitForExitAsync(CancellationToken.None);
            if (Volatile.Read(ref _generation) == gen)
            {
                _ready?.TrySetException(new InvalidOperationException($"Engine startup'ta öldü (exit {code}).")); // startup-crash tek sinyal
                if (Interlocked.CompareExchange(ref _exitReported, 1, 0) == 0)
                    EngineExited?.Invoke(code);
            }
        }, CancellationToken.None);
        try
        {
            return await _ready.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        }
        catch
        {
            if (Volatile.Read(ref _generation) == gen) KillCurrent(); // timeout/iptal → child sızmasın [it0-devir]
            throw;
        }
    }

    public Task SendAsync(IpcCommand command, CancellationToken ct = default) =>
        _writer?.WriteAsync(command, ct) ?? throw new InvalidOperationException("Engine başlatılmadı.");

    public async Task<EngineReadyEvent> RestartAsync(CancellationToken ct = default)
    {
        await ShutdownGracefullyAsync();
        return await StartAsync(ct);
    }

    private async Task ReadLoopAsync(NdjsonReader reader, int gen)
    {
        try
        {
            while (true)
            {
                IpcEvent? ev;
                try { ev = await reader.ReadAsync<IpcEvent>(CancellationToken.None); }
                catch (IpcFramingException)
                {
                    // Bozuk frame = kalıcı sağırlık YARATMAZ; Supervisor exit-2 ile simetri: engine'i öldür + TEK sinyal. [it0-devir]
                    if (Volatile.Read(ref _generation) == gen)
                    {
                        Interlocked.Increment(ref _generation); // exit watcher'ı sustur → EngineExited tek kez
                        KillCurrent();
                        if (Interlocked.CompareExchange(ref _exitReported, 1, 0) == 0) // tek raporcu kazanır [it0-devir]
                            EngineExited?.Invoke(null);
                    }
                    return;
                }
                if (ev is null) return; // EOF — exit watcher bildirir
                if (ev is EngineReadyEvent ready) _ready?.TrySetResult(ready);
                EventReceived?.Invoke(ev);
            }
        }
        catch { /* stream koptu — exit watcher bildirir */ }
    }

    private async Task ShutdownGracefullyAsync()
    {
        Interlocked.Increment(ref _generation); // eski watcher/reader sustur
        try
        {
            if (_writer is not null)
                await _writer.WriteAsync(new ShutdownCommand()).WaitAsync(TimeSpan.FromMilliseconds(500)); // graceful [it0-devir]
        }
        catch { /* zaten ölmüş olabilir */ }
        KillCurrent();
    }

    private void KillCurrent()
    {
        var child = Interlocked.Exchange(ref _child, null); // atomik: yalnız bir thread non-null alır → idempotent [it0-devir]
        if (child is null) return;
        try { System.Diagnostics.Process.GetProcessById(child.Pid).Kill(entireProcessTree: true); }
        catch (ArgumentException) { /* zaten öldü */ }
        child.Dispose();
        _writer = null;
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownGracefullyAsync();
        _outerJob.Dispose(); // KILL_ON_JOB_CLOSE: her koşulda süpürge
    }
}
