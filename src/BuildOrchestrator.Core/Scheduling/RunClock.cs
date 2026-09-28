namespace BuildOrchestrator.Core.Scheduling;

/// <summary>
/// [T55] Bir koşunun süre saati. Zaman kaynağı ENJEKTE edilir (<paramref name="nowMs"/>) — class içinde
/// DateTime.Now/Stopwatch YOK [D3]; testler sahte bir sayacı elle ilerletir, Thread.Sleep/poll-until-elapsed
/// YASAK [D8].
///
/// RunCoordinator her koşu için TAZE bir RunClock kurar: <c>runStarted</c> olayından hemen ÖNCE Start edilir,
/// koşunun <c>finally</c>'sinde (Stop/tamamlanma/hata FARK ETMEKSİZİN, HER koşuda) Pause edilir — değer orada
/// dondurulur ki <c>runCompleted.DurationMs</c> ile decision.log AYNI süreyi yazsın. Üretimde tek akıştan
/// (bir Start, bir Pause) okunur; tohum/devir yoktur — bir sonraki koşu her zaman sıfırdan sayar.
///
/// Start()/Pause() IDEMPOTENT: zaten çalışırken Start() veya zaten duruyorken Pause() no-op'tur, exception
/// atmaz — savunmacı: bu class'ı sürecek akış tek Start/tek Pause çağırsa da, ReadySetScheduler'ın
/// dangling-dependency toleransıyla aynı ilkeyle çift-tetiklemeye karşı çökmez.
///
/// Thread-safety: tek bir lock (_gate). Start/Pause tek bir kontrol akışından (engine loop) çağrılır, ama
/// ElapsedMs Task 9'un UI thread'inden (poll) — Start'ı çağıran thread'den FARKLI bir thread'den — okunabilir;
/// lock bu okumayı güvenli hale getirir. Hot path değildir (saniyede birkaç UI tick), bu yüzden tek kilit
/// yeterli — ince taneli senkronizasyon YAGNI.
/// </summary>
public sealed class RunClock
{
    private readonly object _gate = new();
    private readonly Func<long> _nowMs;

    private long _accumulatedMs;
    private long _segmentStartMs;
    private bool _running;

    public RunClock(Func<long> nowMs)
    {
        ArgumentNullException.ThrowIfNull(nowMs);
        _nowMs = nowMs;
    }

    /// <summary>Yeni bir segment açar. Zaten çalışıyorsa no-op (idempotent) — mevcut segment kesintiye uğramaz.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_running) return;
            _segmentStartMs = _nowMs();
            _running = true;
        }
    }

    /// <summary>Açık segmenti kapatır, süresini accumulated'a ekler. Zaten duruyorsa no-op (idempotent).</summary>
    public void Pause()
    {
        lock (_gate)
        {
            if (!_running) return;
            _accumulatedMs += _nowMs() - _segmentStartMs;
            _running = false;
        }
    }

    /// <summary>Kapanmış segmentlerin toplamı + (çalışıyorsa) açık segmentin şu ana kadarki süresi.</summary>
    public long ElapsedMs
    {
        get
        {
            lock (_gate)
            {
                return _running ? _accumulatedMs + (_nowMs() - _segmentStartMs) : _accumulatedMs;
            }
        }
    }
}
