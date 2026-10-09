using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Threading;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// Motor olaylarını UI thread'ine taşıyan TEK yol (<see cref="ProjectLogEvent"/> hariç — o kilitsiz konsol kuyruğuna
/// okuma thread'inden doğrudan gider). Olaylar geliş sırasıyla tek bir kuyruğa girer ve tek bir boşaltıcı onları ZAMAN
/// DİLİMLERİ hâlinde işler: ilk dilim olay gelir gelmez Normal öncelikte koşar (tek bir olayın gecikmesi değişmez); dilim
/// <see cref="SliceBudgetMs"/>'yi doldurduğunda kuyrukta hâlâ olay varsa kalan iş girdinin ve çizimin ALTINDAKİ önceliğe
/// (<see cref="DispatcherPriority.Background"/>) devredilir — patlamanın arasında kare çizilir, tuş ve tıklama işlenir.
/// Sıra korunur: kuyruğu yalnız boşaltıcı tüketir ve aynı anda en fazla bir boşaltıcı kuruludur.
///
/// <para><b>Neden (ÖLÇÜLDÜ, 2026-10-10):</b> koşu başında motor <c>runStarted</c> + <c>buildPreview</c> + her atlanan
/// proje için bir <c>projectSkipped</c> gönderir (OSYS'te değişmemiş bir Build: 186 olay aynı anda). Pencere her olayı ayrı
/// bir <c>Dispatcher.InvokeAsync</c> ile Normal öncelikte taşıyordu; Normal çizimin ve girdinin ÜSTÜNDE olduğu için hepsi
/// tek blokta koştu — iz: UI thread'i 312 ms aralıksız, gerçek koşuda her koşunun başında 115–145 ms girdi gecikmesi ve
/// 210–260 ms'lik kare boşluğu. Pin: <c>EngineEventBurstTests</c>.</para>
///
/// <para>Bir işleyici fırlatırsa istisna bugünkü gibi dispatcher'a yükselir; kuyrukta kalan olaylar için boşaltıcı yine de
/// yeniden kurulur, yani pompa takılı kalmaz.</para>
/// </summary>
internal sealed class EngineEventPump
{
    /// <summary>Bir dilimin işleyicilere ayırdığı en uzun süre — 120 Hz'lik bir ekranın bir karesi. Dilim bu süre dolunca
    /// (o anki olay bittikten sonra) kapanır.</summary>
    public const double SliceBudgetMs = 8;

    private readonly Dispatcher _dispatcher;
    private readonly Action<IpcEvent> _handle;
    private readonly ConcurrentQueue<IpcEvent> _queue = new();
    private int _drainScheduled; // 0/1 — kuyruğu boşaltacak bir iş dispatcher'da bekliyor ya da koşuyor

    public EngineEventPump(Dispatcher dispatcher, Action<IpcEvent> handle)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _handle = handle ?? throw new ArgumentNullException(nameof(handle));
    }

    /// <summary>Olayı kuyruğa ekler; boşaltıcı kurulu değilse Normal öncelikte kurar. Her thread'den çağrılabilir.</summary>
    public void Post(IpcEvent ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        _queue.Enqueue(ev);
        if (Interlocked.Exchange(ref _drainScheduled, 1) == 0)
            _dispatcher.InvokeAsync(Drain, DispatcherPriority.Normal);
    }

    private void Drain()
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            while (_queue.TryDequeue(out var ev))
            {
                _handle(ev);
                if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= SliceBudgetMs) break;
            }
        }
        finally
        {
            if (!_queue.IsEmpty)
            {
                // Dilim doldu (ya da bir işleyici fırlattı): devamı girdi ve çizimden SONRA.
                _dispatcher.InvokeAsync(Drain, DispatcherPriority.Background);
            }
            else
            {
                Volatile.Write(ref _drainScheduled, 0);
                // Kuyruk boş göründükten hemen sonra gelen bir olay boşaltıcıyı kuramadı (bayrak hâlâ 1'di) — burada kurulur.
                if (!_queue.IsEmpty && Interlocked.Exchange(ref _drainScheduled, 1) == 0)
                    _dispatcher.InvokeAsync(Drain, DispatcherPriority.Normal);
            }
        }
    }
}
