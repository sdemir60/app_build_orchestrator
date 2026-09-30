using System.Diagnostics;
using System.IO;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

public class EngineHostTests
{
    // [B1/F1 · fix-1] Startup timeout GENİŞ geçilir; sabitin TEK sahibi TestPaths.WideStartupTimeout
    // (gerekçe orada). Üretim varsayılanı EngineHost.cs'te 5s'de kalır — bunu Default_startup_timeout_stays_five_seconds pinler.
    private static readonly TimeSpan WideStartupTimeout = TestPaths.WideStartupTimeout;

    [Fact]
    public async Task Start_receives_engineReady_and_ping_pong_works()
    {
        using var sandbox = new SupervisorSandbox();
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout);
        var ready = await host.StartAsync();
        Assert.Equal(host.EnginePid, ready.Pid);
        var pong = new TaskCompletionSource<PongEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.EventReceived += e => { if (e is PongEvent p) pong.TrySetResult(p); };
        await host.SendAsync(new PingCommand(42));
        Assert.Equal(42, (await pong.Task.WaitAsync(TimeSpan.FromSeconds(5))).Seq);
    }

    /// <summary>[T6] Motor öldürülür, yeniden başlatılır. [spec 2026-09-18 §5.5] Yeni motorun <c>engineReady</c>'si
    /// <c>EventReceived</c>'dan da geçer: App'in "previous run was interrupted" satırı o olaydan yazılır
    /// (<c>RunViewModel.OnEvent</c>), yani çökmeden sonra yeniden başlatılan motorun kurtarma sayısı bu yoldan
    /// ekrana ulaşır.</summary>
    [Fact]
    public async Task Supervisor_kill_raises_EngineExited_and_restart_recovers() // T6
    {
        using var sandbox = new SupervisorSandbox();
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout);
        var ready1 = await host.StartAsync();
        var exited = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.EngineExited += code => exited.TrySetResult(code);
        Process.GetProcessById(ready1.Pid).Kill(); // crash simülasyonu
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(2)); // handle-wait ile deterministik tespit
        var readyEvent = new TaskCompletionSource<EngineReadyEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.EventReceived += e => { if (e is EngineReadyEvent r) readyEvent.TrySetResult(r); };
        var ready2 = await host.RestartAsync();
        Assert.NotEqual(ready1.Pid, ready2.Pid);
        await Task.WhenAny(readyEvent.Task, Task.Delay(TimeSpan.FromSeconds(5))); // üst sınır, poll değil
        Assert.True(readyEvent.Task.IsCompletedSuccessfully, "restarted engine's engineReady never reached EventReceived");
        Assert.Equal(ready2.Pid, (await readyEvent.Task).Pid);
        var pong = new TaskCompletionSource<PongEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.EventReceived += e => { if (e is PongEvent p) pong.TrySetResult(p); };
        await host.SendAsync(new PingCommand(1));
        Assert.Equal(1, (await pong.Task.WaitAsync(TimeSpan.FromSeconds(5))).Seq);
    }

    /// <summary>[spec 2026-09-18 §5.5 · Task 10 fix I1] <c>supervisorArgs</c> gerçekten motora gider: sandbox'ın
    /// önbelleğine elle yazılmış bir uçuş defteri hem ilk açılışta hem çökmeden sonraki yeniden başlatmada kurtarılır
    /// ve sayısı <c>engineReady</c>'de döner. Argümanlar kaybolsaydı motor kullanıcının gerçek önbelleğine bakar ve
    /// sayı 0 olurdu — izolasyonun da, restart yolunun kurtarmasının da kanıtı budur.</summary>
    [Fact]
    public async Task An_isolated_engine_recovers_its_own_cache_on_start_and_on_restart()
    {
        using var sandbox = new SupervisorSandbox();
        var crashed = new BuildOrchestrator.Core.State.InFlightLedger(sandbox.CacheRoot);
        crashed.Add(@"C:\r\A\A.csproj");
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout);

        var ready1 = await host.StartAsync();
        Assert.Equal(1, ready1.InterruptedProjects);

        var second = new BuildOrchestrator.Core.State.InFlightLedger(sandbox.CacheRoot);
        second.Add(@"C:\r\A\A.csproj");
        second.Add(@"C:\r\B\B.csproj");
        var ready2 = await host.RestartAsync();
        Assert.Equal(2, ready2.InterruptedProjects);
    }

    /// <summary>[B1/F1 · fix-1 İŞ 1a] Ctor'a geçilen startup timeout GERÇEKTEN kablolu mu — yani
    /// <see cref="EngineHost.StartAsync"/> onu KULLANIYOR mu? Parametre hiç kullanılmasaydı da diğer testler
    /// yeşil kalırdı (geniş değer geçmek, 5s'lik sabitle de çalışırdı) — seam'i pinleyen tek test budur.
    /// <para><b>Ayırt edicilik:</b> <c>EngineHost.cs</c>'te <c>StartupTimeout</c> yerine tekrar sabit
    /// <c>TimeSpan.FromSeconds(5)</c> yazılırsa bu test KIRMIZI olur — gerçek Supervisor ~1sn'de hazır olur,
    /// <c>StartAsync</c> normal döner ve hiçbir exception atılmaz (bkz. task-B1-report.md RED çıktısı).</para>
    /// <para><b>Deterministik ve hızlı:</b> 1 ms'lik pencerede bir process'in doğup <c>engineReady</c> yazması
    /// olanaksız — test gerçek supervisor'ın HAZIR OLMASINI BEKLEMEZ, tam tersini (beklemekten vazgeçmeyi)
    /// sınar. Timeout yolunda <c>StartAsync</c> child'ı öldürür, bu yüzden PID de sızmaz.</para></summary>
    [Fact]
    public async Task StartAsync_gives_up_at_the_injected_startup_timeout()
    {
        using var sandbox = new SupervisorSandbox();
        await using var host = sandbox.IsolatedEngineHost(TimeSpan.FromMilliseconds(1));
        await Assert.ThrowsAsync<TimeoutException>(() => host.StartAsync());
        Assert.Null(host.EnginePid); // vazgeçince child öldürüldü — sızıntı yok
    }

    /// <summary>[final review #1] Motor hazır olamadan vazgeçilince (zaman aşımı, iptal, başlangıçta ölüm — hepsi
    /// <c>StartAsync</c>'in aynı <c>catch</c>'inden geçer) motor öldürülür ve process'in çıkışı en çok
    /// <see cref="EngineHost.KillExitWait"/> (1 s) beklenir. Çağıran UI thread'i olduğunda
    /// (<c>MainWindow.StartEngineAsync</c>) bu bekleme Dispatcher'ı bloklamamalı; ama <c>await _ready.Task.WaitAsync(...)</c>
    /// <c>ConfigureAwait(false)</c> taşımıyordu, <c>catch</c> devamı çağıranın bağlamına post edilir ve öldürme + bekleme
    /// orada koşardı (yavaş ilk açılışta ya da antivirüs taramasında 5 s aşılır — kurulum tam bu durumu üretir).
    /// <para><b>Nasıl gözlenir:</b> öldürme stratejisi (<see cref="EngineHost.KillStrategy"/>) çağrıldığı andaki
    /// <see cref="SynchronizationContext.Current"/>'ı yakalar ve motoru gerçekten öldürür. Çağıranın bağlamı <see cref="CallerContext"/>:
    /// devamı kendisi <c>Current</c> iken bir havuz thread'inde koşturur (pompalanmayan gerçek bir Dispatcher'ın aksine
    /// kusurlu kodda test asılmaz, kırmızı olur).</para>
    /// <para><b>Neden iptal:</b> zaman aşımı da aynı yola girer, ama yarışa açıktır: 1 ms'lik zamanlayıcı <c>await</c>'ten ÖNCE
    /// dolabilir; o zaman bekleyiş hiç askıya girmez, <c>StartAsync</c> öldürmeyi çağıranın thread'inde satır içi koşturup
    /// tamamlanmış döner ve önkoşul (aşağıda, <c>start.IsCompleted</c> yanlış olmalı) düşer. Yani düzeltmeli (doğru) kod da
    /// aralıklı KIRMIZI verirdi — yalancı yeşil değil, yalancı kırmızı. İptal <c>StartAsync</c> bekleyişe girdikten SONRA
    /// verilir; sıra deterministiktir: önkoşul hep sağlanır ve devamın çağıranın bağlamına post edilip edilmediği sonucu tek
    /// başına belirler.</para></summary>
    [Fact]
    public async Task A_start_that_gives_up_kills_the_engine_off_the_callers_context()
    {
        using var sandbox = new SupervisorSandbox();
        var killContext = new TaskCompletionSource<SynchronizationContext?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout, killStrategy: p =>
        {
            killContext.TrySetResult(SynchronizationContext.Current);
            p.Kill(entireProcessTree: true);
        });
        var caller = new CallerContext();
        using var giveUp = new CancellationTokenSource();

        Task<EngineReadyEvent> start;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(caller);
        try { start = host.StartAsync(giveUp.Token); } // bekleyişin devamı ÇAĞIRANIN bağlamını yakalar
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Assert.False(start.IsCompleted, "StartAsync bekleyişe girmeden döndü — önkoşul kurulamadı");

        giveUp.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);

        Assert.True(killContext.Task.IsCompleted, "vazgeçilen başlatma motoru öldürmedi");
        Assert.NotSame(caller, await killContext.Task); // öldürme + ≤1 s bekleme çağıranın bağlamında koşmadı
        Assert.Null(host.EnginePid);
    }

    /// <summary>Çağıranın (UI thread'i) bağlamının taklidi: <c>Post</c> edilen devamı, kendisi <c>Current</c> iken bir havuz
    /// thread'inde koşturur — bağlama dönen bir devam, altında çalıştığı bağlamı gözlemcisine gösterir.</summary>
    private sealed class CallerContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) =>
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var previous = Current;
                SetSynchronizationContext(this);
                try { d(state); }
                finally { SetSynchronizationContext(previous); }
            });
    }

    /// <summary>[B1/F1 · fix-1 İŞ 1b] ÜRETİM VARSAYILANI 5 SANİYEDE KALIR — bu bir yasak sınırdır: büyütülürse
    /// donmuş bir supervisor'da kullanıcının uygulaması asılı kalır (bkz. task-B1-brief.md kural 4). Seam
    /// eklendikten sonra bu değeri koruyan hiçbir şey yoktu; biri bir flake'i "5 → 60" yaparak susturabilirdi.
    /// <para>Beklenen değer ÜRETİMDEN OKUNMAZ, otorite literali olarak yazılır (totoloji yasak) — A13/T4'ün
    /// <c>Assert.Equal(140.0, PopIn.DurationMs)</c> deseninin aynısı.</para></summary>
    [Fact]
    public async Task Default_startup_timeout_stays_five_seconds()
    {
        await using var host = new EngineHost(TestPaths.SupervisorExe); // startupTimeout VERİLMEDİ = üretim yolu
        Assert.Equal(TimeSpan.FromSeconds(5), host.StartupTimeout);
    }

    /// <summary>[motor · Task 11] Güncelleyici (Update.exe) App çıkar çıkmaz <c>current\</c> klasörünü değiştirir;
    /// Supervisor hâlâ <c>current\supervisor\*.dll</c>'leri tutuyorsa kurulum yarım kalır. Dispose, öldürdüğü process'in
    /// GERÇEKTEN çıkmasını bekler (≤ <see cref="EngineHost.KillExitWait"/>) — önceden Kill'den sonra beklenmiyordu:
    /// <c>TerminateProcess</c> asenkrondur, çağrı döndüğünde process birkaç ms daha yaşar (ölçüldü: boştaki bir
    /// Supervisor'da ~4-10 ms).
    /// <para><b>Ölçüt process handle'ının sinyali:</b> <c>Process.GetProcessById</c>'in "yok" demesi YETMEZ — çıkış kodu
    /// Kill'le hemen yazıldığı için process henüz sonlanmamışken de fırlatıyor (ölçüldü). Handle Dispose'dan ÖNCE
    /// açılır, sonra sinyali beklemeden (0 ms) sorulur.</para>
    /// <para><b>Bu test beklemeyi PİNLEMEZ (ölçüldü):</b> ağaç öldürme (<c>Kill(entireProcessTree: true)</c>) Kill'den
    /// SONRA tüm process'leri tarar ve bu tarama boştaki bir Supervisor'ın sonlanmasından çoğu kez uzun sürer —
    /// beklemesiz eski kod bu makinede yeşildi. Üretim yolunu (Dispose → öldür + bekle) uçtan uca koşar; beklemenin
    /// kendisini <see cref="EngineHostKillWaitTests.Kill_and_await_exit_returns_only_after_the_process_has_ended"/> ağaç
    /// taraması olmadan pinler.</para></summary>
    [Fact]
    public async Task Dispose_waits_for_the_supervisor_process_to_exit()
    {
        using var sandbox = new SupervisorSandbox();
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout); // erken bir hata motoru sızdırmasın; ikinci Dispose no-op
        await host.StartAsync();
        using var supervisor = Process.GetProcessById(host.EnginePid!.Value); // handle Dispose'dan ÖNCE açılır

        await host.DisposeAsync();

        Assert.True(supervisor.WaitForExit(TimeSpan.Zero), "Dispose döndüğünde Supervisor process'i hâlâ sonlanmamıştı");
    }

    /// <summary>[motor · Task 11 · fix-1] Öldürülen process'e tanınan süre 1 SANİYEDE KALIR: kısalırsa (ör. 0) güncelleyici
    /// <c>current\</c> klasörünü Supervisor'ın DLL kilitleri bırakılmadan değiştirmeye kalkar; uzarsa çıkış bütçesini
    /// (<c>AppShutdown.DisposalTimeout</c>, 2 s = graceful yazma 500 ms + bu süre) aşar. Beklenen değer üretimden
    /// OKUNMAZ, otorite literali olarak yazılır — <see cref="Default_startup_timeout_stays_five_seconds"/> deseni.</summary>
    [Fact]
    public void Kill_exit_wait_stays_one_second()
        => Assert.Equal(TimeSpan.FromSeconds(1), EngineHost.KillExitWait);

    [Fact]
    public async Task StartAsync_timeout_disposes_child_and_no_leak()
    {
        // [D1] Var olmayan exe → child HİÇ DOĞMAZ: pre-flight (File.Exists) StartAsync'i 5sn timeout'a hiç
        // düşürmeden EngineUnavailableException ile keser; hiçbir process/handle sızmaz.
        await using var host = new EngineHost(Path.Combine(AppContext.BaseDirectory, "does-not-exist.exe"));
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await host.StartAsync(new CancellationTokenSource(TimeSpan.FromSeconds(6)).Token));
        Assert.Null(host.EnginePid); // child referansı sızmadı/temizlendi
    }
}
