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
    private int _generation; // cross-thread okuma → volatile [it0-devir]
    private int _exitReportedGen = -1; // EngineExited hangi generation için fırlatıldı — monotonik tek-atım [it0-devir]

    public event Action<IpcEvent>? EventReceived;
    public event Action<int?>? EngineExited;
    public int? EnginePid => _child?.Pid;

    public async Task<EngineReadyEvent> StartAsync(CancellationToken ct = default)
    {
        int gen = Interlocked.Increment(ref _generation);
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
                if (TryClaimExit(gen)) EngineExited?.Invoke(code);
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
                        _ready?.TrySetException(new InvalidOperationException("Engine framing hatası ile öldü.")); // startup'ta anlık sinyal [it0-devir]
                        Interlocked.Increment(ref _generation); // exit watcher'ı sustur → EngineExited tek kez
                        KillCurrent();
                        if (TryClaimExit(gen)) EngineExited?.Invoke(null); // tek raporcu kazanır, stale gen yeni geni bloklayamaz
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

    // gen için EngineExited'i monotonik olarak sahiplen. Eski (stale) generation, yeni generation'ın
    // rapor hakkını ÇALAMAZ (prev >= gen ise kaybeder); aynı generation'ın iki raporcusundan yalnız biri kazanır. [it0-devir]
    private bool TryClaimExit(int gen)
    {
        while (true)
        {
            int prev = Volatile.Read(ref _exitReportedGen);
            if (prev >= gen) return false;
            if (Interlocked.CompareExchange(ref _exitReportedGen, gen, prev) == prev) return true;
        }
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
