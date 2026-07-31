using System.ComponentModel;
using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.App.Services;

/// <summary>[D1 review · A2] Motorun hiç doğamama nedeni — kullanıcıya gösterilen metni bu ayrım belirler.</summary>
public enum EngineUnavailableReason
{
    /// <summary>Supervisor çalıştırılabiliri uygulamanın yanında YOK (eksik/bozuk kurulum; publish çıktısına
    /// <c>supervisor\</c> klasörü girmemiş).</summary>
    NotFound,

    /// <summary>Dosya var ama BAŞLATILAMADI: geçersiz/bozuk exe, erişim reddi, TOCTOU (pre-flight ile launch
    /// arasında silinme) — <c>CreateProcessW</c> senkron başarısız oldu.</summary>
    CannotStart,
}

/// <summary>
/// [D1] Motor hiç doğamadı. Ham <see cref="System.ComponentModel.Win32Exception"/> yerine bu AYRIŞAN tür
/// atılır: çağıran (MainWindow) bunu kullanıcıya görünür, yeniden başlatması ANLAMSIZ bir kurulum hatasına
/// çevirir. Child hiç doğmadığı için <see cref="EngineHost.EngineExited"/> ASLA ateşlenmez → kullanıcıya TEK
/// sinyal gider. Mesaj metni İngilizce'dir (uygulama İngilizce-only).
/// </summary>
public sealed class EngineUnavailableException(string exePath, EngineUnavailableReason reason, Exception? inner = null)
    : InvalidOperationException(
        reason == EngineUnavailableReason.NotFound
            ? $"Supervisor executable was not found: {exePath}"
            : $"Supervisor executable could not be started: {exePath}", inner)
{
    /// <summary>Aranan Supervisor exe yolu — konsol satırında gösterilir.</summary>
    public string ExePath { get; } = exePath;

    /// <summary>Neden (kullanıcıya gösterilecek metni ayırt eder).</summary>
    public EngineUnavailableReason Reason { get; } = reason;
}

public sealed class EngineHost(string supervisorExePath, TimeSpan? startupTimeout = null) : IAsyncDisposable
{
    /// <summary>[B1/F1] <see cref="StartAsync"/>'in <c>engineReady</c>'yi beklerken vazgeçme süresi.
    /// <b>Üretim varsayılanı 5s'de KALIR</b> — donmuş bir supervisor'da uygulama sonsuza dek asılı kalmasın
    /// diye vazgeçmek şart. Enjekte edilebilir olan yalnız BU süre: testler (yük altında supervisor 5s'de
    /// hazır olamayabilir) daha geniş bir değer geçebilir — desen <c>ConsoleView.WallClock</c>/
    /// <c>NeverTickingBatcher</c> ile aynı: üretim varsayılanı sabit, seam test için var.
    /// <para><b>fix-1 · İŞ 1b:</b> <c>private</c> değil <c>internal</c> — varsayılanın 5s'de kaldığını
    /// <c>EngineHostTests.Default_startup_timeout_is_five_seconds</c> saf literal ile pinler (A13/T4'ün
    /// <c>PopIn.DurationMs</c> deseni). Aksi halde biri flake'i "5 → 60" yaparak susturur ve süit sessiz kalır.</para></summary>
    internal TimeSpan StartupTimeout { get; } = startupTimeout ?? TimeSpan.FromSeconds(5);
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
        // [D1] Pre-flight: exe yoksa CreateProcessW'nin ham Win32Exception'ını beklemeyiz — generation'a
        // DOKUNMADAN (hiçbir yan etki bırakmadan) ayrışan tür atılır.
        if (!File.Exists(supervisorExePath))
            throw new EngineUnavailableException(supervisorExePath, EngineUnavailableReason.NotFound);

        int gen = Interlocked.Increment(ref _generation);
        _ready = new TaskCompletionSource<EngineReadyEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        string cmdLine = WindowsCommandLine.Build(supervisorExePath);
        // [D1 review · A2] Pre-flight'ı GEÇEN ama başlatılamayan exe (bozuk/geçersiz binary, erişim reddi,
        // TOCTOU) de AYNI görünür yüzeye düşer: ham Win32Exception generic catch'te yutulurdu. Child hiç
        // doğmadığı için burada da EngineExited ateşlenmez → tek sinyal.
        try
        {
            _child = JobProcessLauncher.Launch(_outerJob, cmdLine, new LaunchOptions(RedirectStdio: true));
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            // generation GERİ ALINMAZ: monotonik kalmalı (numara yeniden kullanılırsa eski bir watcher yeni
            // generation'ın rapor hakkını çalabilirdi). Bu gen için hiç watcher/reader kurulmadı — sinyal yok.
            throw new EngineUnavailableException(supervisorExePath, EngineUnavailableReason.CannotStart, ex);
        }
        _writer = new NdjsonWriter(_child.StandardInput!);
        var reader = new NdjsonReader(_child.StandardOutput!);
        _ = Task.Run(() => ReadLoopAsync(reader, gen), CancellationToken.None);
        var watched = _child;
        _ = Task.Run(async () =>
        {
            int code = await watched.WaitForExitAsync(CancellationToken.None);
            if (Volatile.Read(ref _generation) == gen)
            {
                // [D1 review · A3] Kullanıcıya ulaşabilen HER metin İngilizce (uygulama İngilizce-only).
                _ready?.TrySetException(new InvalidOperationException($"Engine died during startup (exit {code}).")); // startup-crash tek sinyal
                if (TryClaimExit(gen)) EngineExited?.Invoke(code);
            }
        }, CancellationToken.None);
        try
        {
            return await _ready.Task.WaitAsync(StartupTimeout, ct);
        }
        catch
        {
            if (Volatile.Read(ref _generation) == gen) KillCurrent(); // timeout/iptal → child sızmasın [it0-devir]
            throw;
        }
    }

    // [D1 review · A3] Bu mesaj KULLANICIYA görünür: TrySendAsync onu konsola "[error] failed to send …"
    // satırıyla yazar. Uygulama İngilizce-only olduğundan metin de İngilizce.
    public Task SendAsync(IpcCommand command, CancellationToken ct = default) =>
        _writer?.WriteAsync(command, ct) ?? throw new InvalidOperationException("Engine is not running.");

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
                        _ready?.TrySetException(new InvalidOperationException("Engine died with a framing error.")); // startup'ta anlık sinyal [it0-devir]
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

    // ConfigureAwait(false): bu yol DisposeAsync'ten (App.OnExit → UI/Dispatcher thread'i) çağrılır. Çağıranın
    // SynchronizationContext'i yakalanırsa continuation, o an bekleyişte bloklu olan Dispatcher'a post edilir →
    // sync-over-async deadlock (process çıkamaz). Disposal yolu ASLA çağıranın context'ine bağımlı olmamalı. [T62 fix]
    private async Task ShutdownGracefullyAsync()
    {
        Interlocked.Increment(ref _generation); // eski watcher/reader sustur
        try
        {
            if (_writer is not null)
                await _writer.WriteAsync(new ShutdownCommand()).WaitAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false); // graceful [it0-devir]
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
        await ShutdownGracefullyAsync().ConfigureAwait(false); // bkz. ShutdownGracefullyAsync notu [T62 fix]
        _outerJob.Dispose(); // KILL_ON_JOB_CLOSE: her koşulda süpürge
    }
}
