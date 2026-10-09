using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Threading;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// Motor olaylarını UI thread'ine taşıyan TEK yol (<see cref="ProjectLogEvent"/> hariç — o kilitsiz konsol kuyruğuna
/// okuma thread'inden doğrudan gider). Olaylar geliş sırasıyla tek bir kuyruğa girer ve tek bir boşaltıcı onları ZAMAN
/// DİLİMLERİ hâlinde işler. Kuyruk boşken gelen olay hemen, Normal öncelikte işlenir — tek bir olayın yolu eskisiyle aynıdır.
/// Bir dilim <see cref="SliceBudgetMs"/> dolduğunda (elindeki olay bittikten sonra) kapanır; kuyrukta hâlâ olay varsa kalan
/// iş girdinin ve çizimin ALTINDAKİ önceliğe (<see cref="DispatcherPriority.Background"/>) devredilir — patlamanın arasında
/// kare çizilir, tuş ve tıklama işlenir. O sırada gelen olay da kuyruğun sonuna eklenir ve devamla birlikte işlenir. Sıra
/// korunur: kuyruğu yalnız boşaltıcı (ya da <see cref="DrainNow"/>) tüketir ve aynı anda en fazla bir boşaltıcı kuruludur.
///
/// <para><b>Neden (ÖLÇÜLDÜ, 2026-10-10):</b> koşu başında motor <c>runStarted</c> + <c>buildPreview</c> + her atlanan
/// proje için bir <c>projectSkipped</c> gönderir (OSYS'te değişmemiş bir Build: 186 olay aynı anda). Pencere her olayı ayrı
/// bir <c>Dispatcher.InvokeAsync</c> ile Normal öncelikte taşıyordu; Normal çizimin ve girdinin ÜSTÜNDE olduğu için hepsi
/// tek blokta koştu — iz: UI thread'i 312 ms aralıksız, gerçek koşuda her koşunun başında 115–145 ms girdi gecikmesi ve
/// 210–260 ms'lik kare boşluğu. Pin: <c>EngineEventBurstTests</c>, <c>EngineEventPumpTests</c>.</para>
///
/// <para><b>İstisna:</b> boşaltıcı <c>InvokeAsync</c> ile kurulur; bir işleyicinin istisnası — eskiden olay başına kurulan
/// işte olduğu gibi — o dispatcher işinin görevine düşer, uygulamayı düşürmez ve yalnız o olay kaybolur: kuyrukta kalanlar
/// için boşaltıcı yeniden kurulur, pompa takılı kalmaz.</para>
/// </summary>
internal sealed class EngineEventPump
{
    /// <summary>Bir dilimin işleyicilere ayırdığı süre — 120 Hz'lik bir ekranın bir karesi. Dilim bu süre dolunca, elindeki
    /// olay bittikten sonra kapanır (tek bir ağır olay — ör. topoloji — kendi süresi kadar sürer).</summary>
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

    /// <summary>Kuyruktaki her olayı ŞİMDİ, dilimsiz işler — yalnız UI thread'inden. Motor çıkışının yolu: çıkış, motorun ondan
    /// önce gönderdiği olaylardan SONRA uygulanmalı (aksi halde geç uygulanan bir <c>runStarted</c> ölü motorla koşuyu yeniden
    /// açar). Kurulu bir boşaltıcı varsa sonradan boş kuyruk bulur ve kendini kapatır.</summary>
    public void DrainNow()
    {
        _dispatcher.VerifyAccess();
        while (_queue.TryDequeue(out var ev)) _handle(ev);
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
                // Bayrak TAM BARİYERLE sıfırlanır: Volatile.Write sonrasındaki kuyruk okuması x64'te o yazmanın önüne geçebilir
                // (mağaza tamponu) ve tam o anda Post eden üretici hâlâ 1 görüp boşaltıcı kurmaz — olay kuyrukta, boşaltıcı yok.
                // Interlocked.Exchange'ten sonra iki taraftan biri mutlaka görür: ya burada kuyruk dolu okunur ya da üretici 0 okur.
                Interlocked.Exchange(ref _drainScheduled, 0);
                // Kuyruk boş göründükten hemen sonra gelen bir olay boşaltıcıyı kuramadı (bayrak hâlâ 1'di) — burada kurulur.
                if (!_queue.IsEmpty && Interlocked.Exchange(ref _drainScheduled, 1) == 0)
                    _dispatcher.InvokeAsync(Drain, DispatcherPriority.Normal);
            }
        }
    }
}
