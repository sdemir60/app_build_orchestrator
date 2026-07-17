namespace BuildOrchestrator.Core.Scheduling;

/// <summary>
/// [T55] Bir run'ın toplam süresini segment segment biriktiren saat. Zaman kaynağı ENJEKTE edilir
/// (<paramref name="nowMs"/>) — class içinde DateTime.Now/Stopwatch YOK [D3]; testler sahte bir sayacı elle
/// ilerletir, Thread.Sleep/poll-until-elapsed YASAK [D8].
///
/// Continue senaryosu: kullanıcı Stop bastığında Pause edilir (elapsed donar); Continue'da YENİ bir
/// RunClock(nowMs, accumulatedMs: &lt;önceki ElapsedMs&gt;) kurulup Start edilir — UI'nin süre sayacı
/// sıfırlanmadan kaldığı yerden devam eder (K/T55). ElapsedMs = tüm kapanmış [Start,Pause) segmentlerinin
/// toplamı + (çalışıyorsa) mevcut açık segmentin süresi; paused geçen süre HİÇ sayılmaz.
///
/// Start()/Pause() IDEMPOTENT: zaten çalışırken Start() veya zaten duruyorken Pause() no-op'tur, exception
/// atmaz. Gerekçe: bu saat engine'in run-lifecycle'ı tarafından sürülür (Start=run/resume başlangıcı,
/// Pause=Stop); çift-tetikleme (örn. UI'de çift Stop tıklaması, ya da Task 9'un savunmacı bir yeniden-çağrısı)
/// tüm run'ı çökertmemeli — ReadySetScheduler'ın dangling-dependency toleransıyla aynı savunmacı ilke.
///
/// Thread-safety: tek bir lock (_gate). Start/Pause genelde tek bir kontrol akışından (engine loop) çağrılır,
/// ama ElapsedMs Task 9'un UI thread'inden (poll) — Start'ı çağıran thread'den FARKLI bir thread'den —
/// okunabilir; lock bu okumayı güvenli hale getirir. Hot path değildir (saniyede birkaç UI tick), bu yüzden
/// tek kilit yeterli — ince taneli senkronizasyon YAGNI.
/// </summary>
public sealed class RunClock
{
    private readonly object _gate = new();
    private readonly Func<long> _nowMs;

    private long _accumulatedMs;
    private long _segmentStartMs;
    private bool _running;

    public RunClock(Func<long> nowMs, long accumulatedMs = 0)
    {
        ArgumentNullException.ThrowIfNull(nowMs);
        _nowMs = nowMs;
        _accumulatedMs = accumulatedMs;
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
