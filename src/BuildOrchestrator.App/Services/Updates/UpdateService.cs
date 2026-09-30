using Velopack;

namespace BuildOrchestrator.App.Services.Updates;

/// <summary>
/// [design v1.23.0 §2.12 · K8] Güncelleme motorunun durum makinesi: kurulu kopyada açılıştan <see cref="FirstCheckDelay"/>
/// sonra ve her <see cref="CheckInterval"/>'de sessiz kontrol; yeni sürüm arka planda iner; indirme bitince teklif
/// <paramref name="publish"/> ile yayımlanır (hap o anda belirir). Hata (kontrol, indirme, yayım ve çıkışta kurulumu
/// başlatma dahil) sessizdir —
/// bir sonraki turda yeniden; sürüm ancak yayım başarılı olunca "hazır" sayılır. Kurulu
/// olmayan kopya (bin'den dev build, publish klasörü) hiç kontrol etmez. Önceki oturumdan indirilmiş paket
/// (<see cref="IAppUpdater.PendingRestart"/>) açılışta hemen teklif edilir.
/// <para><b>Çıkış:</b> hazır teklif varsa <see cref="ApplyOnExit"/> Update.exe'yi başlatır — <c>Restart to update</c>
/// istendiyse yeniden açılır, aksi hâlde (Later + normal çıkış) sessizce kurulur ve bir sonraki açılış yeni sürümdür
/// (tasarım: "If you wait, it installs on the next start").</para>
/// <para>Zaman <see cref="TimeProvider"/> üzerinden (D8: testte duvar saati beklenmez); <paramref name="publish"/>
/// çağıranın thread'inde koşar, UI'a marshal çağıranın işidir.</para>
/// </summary>
public sealed class UpdateService(IAppUpdater updater, TimeProvider time, Action<UpdateOffer> publish) : IDisposable
{
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(4);

    private ITimer? _timer;
    private UpdateCandidate? _ready;
    private Task _cycle = Task.CompletedTask;
    private int _cycleRunning;

    /// <summary>İnmiş ve kuruluma hazır teklif; yoksa <c>null</c> (hap yalnız bu doluyken görünür).</summary>
    public UpdateOffer? Ready { get; private set; }

    /// <summary>Kullanıcı <c>Restart to update</c> dedi mi — çıkışta kurulumdan sonra yeniden açılmayı belirler.</summary>
    public bool RestartRequested { get; private set; }

    /// <summary>Test yüzeyi: uçuştaki turun Task'ı; tur yoksa tamamlanmış Task.</summary>
    internal Task RunningCycle => _cycle;

    /// <summary>Kurulu kopyada zamanlayıcıyı kurar; kurulu değilse hiçbir şey yapmaz. Önceki oturumdan kalan indirilmiş
    /// paket varsa teklif hemen yayımlanır.</summary>
    public void Start()
    {
        if (!updater.IsInstalled) return;
        _timer = time.CreateTimer(_ => _ = RunCycleAsync(), null, FirstCheckDelay, CheckInterval);
        // Zamanlayıcı önce kurulur ve bekleyen teklifin yayımı da döngüyle aynı kurala tabidir: atarsa App açılışına
        // yayılmaz, _ready boş kalır ve ilk tur aynı sürümü yeniden dener.
        try { if (updater.PendingRestart is { } pending) Publish(pending); }
        catch (Exception ex) when (IsSilent(ex)) { }
    }

    /// <summary>Bir kontrol turu (zamanlayıcı da bunu çağırır). Uçuşta tur varsa yenisi başlamaz.</summary>
    internal Task RunCycleAsync()
    {
        if (Interlocked.CompareExchange(ref _cycleRunning, 1, 0) != 0) return Task.CompletedTask;
        _cycle = CycleAsync();
        return _cycle;
    }

    private async Task CycleAsync()
    {
        try
        {
            var candidate = await updater.CheckAsync(CancellationToken.None).ConfigureAwait(false);
            if (candidate is null || !IsNewer(candidate.Version, AppIdentity.Version) || candidate.Version == _ready?.Version) return;
            await updater.DownloadAsync(candidate, CancellationToken.None).ConfigureAwait(false);
            Publish(candidate);
        }
        catch (Exception ex) when (IsSilent(ex)) { /* sessiz: çevrimdışı, limit, hash, yayım — sonraki tur */ }
        finally { Volatile.Write(ref _cycleRunning, 0); }
    }

    /// <summary>Hata sessizdir (tanı logu bilinçli yok): yalnız bellek tükenmesi yutulmaz.</summary>
    private static bool IsSilent(Exception ex) => ex is not OutOfMemoryException;

    /// <summary>Önce teklif kurulur ve yayımlanır, durum sonra işlenir: teklif kurulamaz ya da yayım atarsa <c>_ready</c>
    /// boş kalır — aynı sürüm yinelenen-yayım elemesine takılmadan sonraki turda yeniden denenir ve hiç gösterilmemiş
    /// paket çıkışta kurulmaz.</summary>
    private void Publish(UpdateCandidate candidate)
    {
        var offer = UpdateOffer.From(candidate);
        publish(offer);
        _ready = candidate;
        Ready = offer;
    }

    /// <summary>Velopack yalnız daha yenisini döndürür; yine de eşit/eski sürüm burada da elenir (feed hatası hapı açmasın).
    /// Karşılaştırma SemVer'dir ve feed'i sıralayan <see cref="SemanticVersion"/> ile aynıdır: ön sürüm etiketi de sıralanır
    /// ("1.9.0-beta.1" &gt; "1.8.0", "1.9.0" &gt; "1.9.0-beta.1"). <c>System.Version</c> ön sürüm dizgesini parse edemez —
    /// <c>BO_UPDATE_PRERELEASE=1</c> kapısı (K9) hiçbir teklif üretmezdi. Parse edilemeyen taraf (feed ya da kurulu sürüm)
    /// <c>false</c> döner.</summary>
    internal static bool IsNewer(string incoming, string installed) =>
        SemanticVersion.TryParse(incoming, out var next) && SemanticVersion.TryParse(installed, out var current) && next > current;

    /// <summary><c>Restart to update</c>: çıkışta kurulum yapılır ve uygulama yeniden açılır.</summary>
    public void RequestRestart() => RestartRequested = true;

    /// <summary>Çıkış yolunun son adımı: hazır teklif varsa Update.exe'yi başlatır (kurulum bu process çıkınca olur).
    /// İndirmesi bitmemiş paket hazır sayılmaz — <c>_ready</c> yalnız indirme sonrası dolar. Kurulumu başlatamamak
    /// (Update.exe bulunamadı, <c>Process.Start</c> atar) sessizdir: çağıran <c>App.OnExit</c>'tir, orada yakalayan yoktur
    /// ve atan bir çıkış çöker (Application Error); hazır teklif her açılışta geri geldiğinden çökme her çıkışta tekrarlanırdı.
    /// Kurulum bu çıkışta olmazsa bir sonraki çıkış yeniden dener.</summary>
    public void ApplyOnExit()
    {
        try { if (_ready is { } ready) updater.ApplyOnExit(ready, RestartRequested); }
        catch (Exception ex) when (IsSilent(ex)) { }
    }

    public void Dispose() => _timer?.Dispose();
}
