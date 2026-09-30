namespace BuildOrchestrator.App.Services.Updates;

/// <summary>
/// [design v1.23.0 §2.12 · K8] Güncelleme motorunun durum makinesi: kurulu kopyada açılıştan <see cref="FirstCheckDelay"/>
/// sonra ve her <see cref="CheckInterval"/>'de sessiz kontrol; yeni sürüm arka planda iner; indirme bitince teklif
/// <paramref name="publish"/> ile yayımlanır (hap o anda belirir). Hata sessizdir — bir sonraki turda yeniden. Kurulu
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
        if (updater.PendingRestart is { } pending) Publish(pending);
        _timer = time.CreateTimer(_ => _ = RunCycleAsync(), null, FirstCheckDelay, CheckInterval);
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
            if (candidate is null || !IsNewer(candidate.Version) || candidate.Version == _ready?.Version) return;
            await updater.DownloadAsync(candidate, CancellationToken.None).ConfigureAwait(false);
            Publish(candidate);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* sessiz: çevrimdışı, limit, hash — sonraki tur */ }
        finally { Volatile.Write(ref _cycleRunning, 0); }
    }

    private void Publish(UpdateCandidate candidate)
    {
        _ready = candidate;
        Ready = UpdateOffer.From(candidate);
        publish(Ready);
    }

    /// <summary>Velopack yalnız daha yenisini döndürür; yine de eşit/eski sürüm burada da elenir (feed hatası hapı açmasın).</summary>
    private static bool IsNewer(string version) =>
        Version.TryParse(version, out var incoming) && Version.TryParse(AppIdentity.Version, out var installed) && incoming > installed;

    /// <summary><c>Restart to update</c>: çıkışta kurulum yapılır ve uygulama yeniden açılır.</summary>
    public void RequestRestart() => RestartRequested = true;

    /// <summary>Çıkış yolunun son adımı: hazır teklif varsa Update.exe'yi başlatır (kurulum bu process çıkınca olur).
    /// İndirmesi bitmemiş paket hazır sayılmaz — <c>_ready</c> yalnız indirme sonrası dolar.</summary>
    public void ApplyOnExit()
    {
        if (_ready is { } ready) updater.ApplyOnExit(ready, RestartRequested);
    }

    public void Dispose() => _timer?.Dispose();
}
