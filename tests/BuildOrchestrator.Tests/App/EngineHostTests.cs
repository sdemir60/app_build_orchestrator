using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Tests.Supervisor;
using Microsoft.Win32.SafeHandles;

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

    /// <summary>[idle-memory · dalga 3 · C24] Önceki bir motorun okuyucusunda kalmış GEÇ bir olay, yeni motorunkiymiş gibi
    /// geçmez: okuma döngüsü nesli geride kalmış bir motorun olayını düşürür (<c>engineReady</c> dahil — o, yeni neslin hazır
    /// görevini eski PID ile tamamlardı). Uyku → uyanma da Restart da yeni nesil açar; süzgeç ikisinde de aynı tek kontroldür.
    /// <para><b>Kurgu deterministiktir:</b> okuyucu ilk <c>engineReady</c>'nin işleyicisinde tutulur; motor iki pong yazar ve
    /// testin gönderdiği <c>shutdown</c>'la KENDİLİĞİNDEN çıkar — çıkış iki pong'un da boruda olduğunun kanıtıdır ve kendiliğinden
    /// ölümde boruyu kimse kapatmaz. Bırakılan okuyucu iki pong'u TEK okumada alır ve ilkinin işleyicisinde yeniden tutulur:
    /// ikinci pong artık okuyucunun belleğindedir. Restart nesli değiştirir ve eski boruyu kapatır; okuyucu bırakılınca bellekteki
    /// pong'u işler ve biter (<see cref="EngineHost.ReadLoop"/>).</para>
    /// <para><b>Neden uyku değil Restart:</b> uyku canlı motorda iş yapar ve eski boruyu süreç çıkar çıkmaz kapatır; okuyucunun
    /// belleğine iki olayı birlikte sokmanın bir kanıtı (kendiliğinden çıkış) uykuda kurulamaz. Uykunun riski de tam olarak budur —
    /// okuyucunun belleğinde kalmış bir olay — ve süzgeç onu buradaki aynı satırda düşürür.</para></summary>
    [Fact]
    public async Task A_late_event_of_a_previous_engine_never_passes_as_the_new_ones()
    {
        using var sandbox = new SupervisorSandbox();
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout);
        int readies = 0;
        using var releaseReady = new ManualResetEventSlim();
        using var releaseFirstPong = new ManualResetEventSlim();
        var heldAtFirstPong = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var passed = new ConcurrentQueue<int>();
        host.EventReceived += e =>
        {
            switch (e)
            {
                case EngineReadyEvent when Interlocked.Increment(ref readies) == 1:
                    releaseReady.Wait(WideStartupTimeout); // eski okuyucu burada durur — pong'lar boruda birikir
                    break;
                case PongEvent { Seq: 1 }:
                    heldAtFirstPong.TrySetResult();
                    releaseFirstPong.Wait(WideStartupTimeout); // ikinci pong okuyucunun belleğinde bekler
                    break;
                case PongEvent pong:
                    passed.Enqueue(pong.Seq);
                    break;
            }
        };
        try
        {
            var first = await host.StartAsync();
            var oldReader = host.ReadLoop!;
            using (var oldEngine = Process.GetProcessById(first.Pid))
            {
                await host.SendAsync(new PingCommand(1));
                await host.SendAsync(new PingCommand(2));
                await host.SendAsync(new ShutdownCommand()); // motor iki pong'u yazıp kendiliğinden çıkar
                await oldEngine.WaitForExitAsync().WaitAsync(WideStartupTimeout);
            }
            releaseReady.Set();
            await heldAtFirstPong.Task.WaitAsync(WideStartupTimeout);

            var second = await host.RestartAsync();
            releaseFirstPong.Set();
            await oldReader.WaitAsync(WideStartupTimeout);

            Assert.NotEqual(first.Pid, second.Pid);
            Assert.Empty(passed); // pong 2 eski motorundu — yeni motor çalışırken onunkiymiş gibi geçmedi
        }
        finally
        {
            releaseReady.Set(); // erken düşen bir iddia havuz thread'ini tutulu bırakmasın
            releaseFirstPong.Set();
        }
    }

    // ==================================== [idle-memory · dalga 3] Motor uykusu ====================================

    /// <summary>[idle-memory · dalga 3 · spec §6.4] Uyku işsiz motoru durdurur: süreç gider, <see cref="EngineHost.IsAsleep"/> true,
    /// <see cref="EngineHost.EnginePid"/> null — ve bu kasıtlı çıkış <see cref="EngineHost.EngineExited"/> ÜRETMEZ (yoksa VM uyuyan
    /// motoru "öldü" sanıp kırmızı şerit + Restart gösterirdi). Uyuyan konağa gönderim bugünkü sözleşmeyle reddedilir: başlamadan
    /// önceki "motor çalışmıyor" hatasının aynısı. Uyanma yeni bir süreç başlatır; komutlar ona gider.
    /// <para><b>[pin — kırmızısı yalnız derleme]</b> Plan bu testin "bugün kasıtlı çıkış da ölüm sayılır" diye KIRMIZI vereceğini
    /// söylüyordu; yanlıştı: kapanış yolu nesli öldürmeden ÖNCE artırır, bekçi bayat nesilde susar. Test o sözleşmeyi uyku için
    /// pinler: nesli artırmadan durduran bir uyku burada her koşuda kırmızıdır (ölüm sinyali bekçi kararını verdikten SONRA
    /// sayılır).</para>
    /// <para><b>Ölçütler deterministiktir:</b> süreç tanıtıcısı uykudan ÖNCE açılır ve sinyali sıfır beklemeyle sorulur
    /// (<c>GetProcessById</c>'in yarışı yok); "sinyal gelmedi" sabit bir beklemeyle değil, eski neslin çıkış bekçisi
    /// (<see cref="EngineHost.ExitWatch"/>) kararını verdikten sonra sayılır.</para></summary>
    [Fact]
    public async Task Sleep_stops_the_engine_without_EngineExited_and_wake_starts_a_fresh_one()
    {
        using var sandbox = new SupervisorSandbox();
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout);
        var notRunning = await Assert.ThrowsAsync<InvalidOperationException>(() => host.SendAsync(new PingCommand(0)));
        var first = await host.StartAsync();
        var firstWatch = host.ExitWatch!;
        int exited = 0;
        host.EngineExited += _ => Interlocked.Increment(ref exited);
        using var slept = Process.GetProcessById(first.Pid); // tanıtıcı uykudan ÖNCE açılır

        await host.SleepAsync();

        Assert.True(host.IsAsleep);
        Assert.Null(host.EnginePid);
        Assert.True(slept.WaitForExit(TimeSpan.Zero), "the slept Supervisor had not ended when SleepAsync returned");
        await firstWatch.WaitAsync(WideStartupTimeout); // eski bekçi çıkışı gördü ve kararını verdi
        Assert.Equal(0, Volatile.Read(ref exited));
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => host.SendAsync(new PingCommand(1)));
        Assert.Equal(notRunning.Message, refused.Message); // bugünkü sözleşme: uyuyan motor "çalışmıyor"dur

        var woken = await host.WakeAsync();

        Assert.False(host.IsAsleep);
        Assert.NotEqual(first.Pid, woken.Pid);
        Assert.Equal(woken.Pid, host.EnginePid);
        var pong = new TaskCompletionSource<PongEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.EventReceived += e => { if (e is PongEvent p) pong.TrySetResult(p); };
        await host.SendAsync(new PingCommand(7));
        Assert.Equal(7, (await pong.Task.WaitAsync(TimeSpan.FromSeconds(5))).Seq);
        Assert.Equal(0, Volatile.Read(ref exited)); // uyanma da ölüm sinyali üretmedi
    }

    /// <summary>[idle-memory · dalga 3 · C6] Uyanık ve canlı motorda uyanma hiçbir şey başlatmaz: çalışan motorun hazır olayını
    /// döner (aynı PID). Uyuyan konağın dispose'u temizdir: gönderecek komut, öldürecek süreç yoktur — fırlatmaz.
    /// <para><b>[pin — kırmızısı yalnız derleme]</b> Plan taslağı da bu iki davranışı doğru kuruyordu; test sözleşmeyi pinler.</para></summary>
    [Fact]
    public async Task Wake_on_an_awake_engine_is_a_no_op_and_dispose_after_sleep_is_clean()
    {
        using var sandbox = new SupervisorSandbox();
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout); // erken düşen bir iddia motoru sızdırmasın; ikinci Dispose no-op
        var ready = await host.StartAsync();

        var woken = await host.WakeAsync();

        Assert.Equal(ready.Pid, woken.Pid);
        Assert.Equal(ready.Pid, host.EnginePid);
        Assert.False(host.IsAsleep);

        await host.SleepAsync();
        Assert.True(host.IsAsleep);
        await host.DisposeAsync(); // fırlatmaz
    }

    /// <summary>[idle-memory · dalga 3 · C3] Aynı anda gelen iki uyanma TEK süreç doğurur: yaşam döngüsü kapısı ikinciyi
    /// birincinin sonuna sıralar, ikinci uyanık motoru bulur ve onun hazır olayını döner. Kapı olmasaydı ikisi de uyuyan konağı
    /// görüp birer süreç başlatırdı: ikincisi ilkinin <c>_child</c>'ını ezer (ilk süreç outer job kapanana dek sahipsiz yaşar) ve
    /// iki uyanmadan biri hazır olayını hiç alamayıp zaman aşımına düşerdi.</summary>
    [Fact]
    public async Task Two_concurrent_wakes_start_one_engine()
    {
        using var sandbox = new SupervisorSandbox();
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout);
        await host.StartAsync();
        await host.SleepAsync();

        var first = host.WakeAsync();
        var second = host.WakeAsync();
        var woken = await Task.WhenAll(first, second);

        Assert.Equal(woken[0].Pid, woken[1].Pid);
        Assert.Equal(woken[0].Pid, host.EnginePid);
    }

    /// <summary>[idle-memory · dalga 3 · C6] Hiç başlamamış bir konak uyumaz ve uyandırılamaz: uyku hiçbir durumu değiştirmez
    /// (<see cref="EngineHost.IsAsleep"/> false kalır), uyanma motor BAŞLATMAZ, <see cref="InvalidOperationException"/> atar —
    /// motoru ilk kez başlatmak <see cref="EngineHost.StartAsync"/>'in işidir. Plan taslağı burada konağı "uyuyor" sayıyor ve bir
    /// sonraki uyanma motoru sessizce başlatıyordu.</summary>
    [Fact]
    public async Task Sleep_and_wake_change_nothing_on_a_host_that_never_started()
    {
        using var sandbox = new SupervisorSandbox(); // yanlış bir uyanma motoru başlatsa bile önbellek izole kalır
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout);

        await host.SleepAsync();

        Assert.False(host.IsAsleep);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.WakeAsync());
        Assert.Null(host.EnginePid); // hiçbir motor doğmadı
    }

    /// <summary>[idle-memory · dalga 3 · C6] Kendiliğinden ölmüş motor uyumaz ve uyandırılmaz: ölüm <see cref="EngineHost.EngineExited"/>
    /// ile bildirildikten sonra uyku hiçbir durumu değiştirmez, uyanma <see cref="InvalidOperationException"/> atar — eski PID'li
    /// hazır olayını dönmez. Ölümü toparlamak <see cref="EngineHost.RestartAsync"/>'in işidir ve o yol açık kalır. Plan taslağı ölü
    /// motoru "uyutup" (kapanış yolu çoktan ölmüş süreci öldürmeye çalışır) konağı uyuyor sayıyordu.</summary>
    [Fact]
    public async Task Sleep_and_wake_change_nothing_once_the_engine_died()
    {
        using var sandbox = new SupervisorSandbox();
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout);
        var ready = await host.StartAsync();
        var died = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.EngineExited += code => died.TrySetResult(code);
        Process.GetProcessById(ready.Pid).Kill(); // çökme simülasyonu
        await died.Task.WaitAsync(WideStartupTimeout); // ölüm bildirildi

        await host.SleepAsync();

        Assert.False(host.IsAsleep);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.WakeAsync());
        var restarted = await host.RestartAsync(); // ölümün yolu açık kaldı
        Assert.NotEqual(ready.Pid, restarted.Pid);
    }

    /// <summary>[idle-memory · dalga 3 · CLAUDE.md değişmezi] Uyanan motor, ilk başlatmadaki gibi App'in outer job'ına girer
    /// (askıda başlat → job'a ata → devam ettir): App ölürse <c>KILL_ON_JOB_CLOSE</c> onu da toplar. Ölçüt job'un IOCP'sidir —
    /// port ilk başlatmadan ÖNCE bağlanır, uyanan PID'in doğum bildirimi beklenir (sleep/poll yok — D8); ilk motorun doğumunun da
    /// görülmesi portun gerçekten doğum taşıdığının kanıtıdır (<c>SafeExitProcessTests</c>'teki vakum karşıtı iddianın aynısı).
    /// <para><b>[pin — kırmızısı yalnız derleme]</b> Uyanma ilk başlatmanın yolunu kullandığı sürece yeşildir; job dışında başlatan
    /// bir uyanma burada kırmızıdır.</para></summary>
    [Fact]
    public async Task A_woken_engine_runs_in_the_outer_job_like_the_first_start()
    {
        using var sandbox = new SupervisorSandbox();
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout);
        using var births = host.OuterJob.AttachCompletionPort(); // ilk başlatmadan ÖNCE: iki doğum da görülür
        var first = await host.StartAsync();
        await host.SleepAsync();

        var woken = await host.WakeAsync();

        var born = new List<int>();
        while (!born.Contains(woken.Pid))
        {
            var n = births.WaitNext(WideStartupTimeout)
                ?? throw new TimeoutException("the woken engine was never born into the outer job");
            if (n.MessageId == NativeMethods.JOB_OBJECT_MSG_NEW_PROCESS) born.Add(n.Pid);
        }
        Assert.Contains(first.Pid, born); // vakum karşıtı: port ilk başlatmanın doğumunu da taşıdı
    }

    /// <summary>[idle-memory · dalga 3 · C7] Uyku, kilitlenmiş bir motoru öldürmek zorunda kalsa bile öldürmeyi çağıranın bağlamında
    /// koşturmaz: ağaç öldürme + çıkışın ≤ <see cref="EngineHost.KillExitWait"/> beklenişi havuzda koşar — çağıran tik (UI thread'i)
    /// olduğunda Dispatcher bloklanmaz. Öldürme ancak zarif bekleyiş (≤ <see cref="EngineHost.SleepExitWait"/>) süreç yaşarken dolunca
    /// gelir ve o bekleyiş hep askıya girer; öldürmeyi bekleyişten önce, senkron yapan bir uyku burada kırmızıdır.
    /// <para><b>Nasıl gözlenir:</b> <see cref="A_start_that_gives_up_kills_the_engine_off_the_callers_context"/>'in deseni — öldürme
    /// stratejisi çağrıldığı andaki <see cref="SynchronizationContext.Current"/>'ı yakalar, uyku <see cref="CallerContext"/> altında
    /// başlatılır. Sağlıklı bir motor <c>shutdown</c>'la kendisi çıkar ve öldürme hiç gerekmez; bu yüzden motor uykudan önce
    /// KİLİTLENİR (<see cref="NtSuspendProcess"/>): canlıdır ama <c>shutdown</c>'ı hiç okumaz, zarif bekleyiş dolar, öldürme gelir.</para>
    /// <para><b>[pin — kırmızısı yalnız derleme]</b> Plan taslağında öldürme, <c>shutdown</c> yazımının askıya girip girmemesine bağlı
    /// bir yarışla havuza düşüyordu; burada sözleşme pinlenir.</para></summary>
    [Fact]
    public async Task Sleep_kills_a_wedged_engine_off_the_callers_context()
    {
        using var sandbox = new SupervisorSandbox();
        var killContext = new TaskCompletionSource<SynchronizationContext?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout, killStrategy: p =>
        {
            killContext.TrySetResult(SynchronizationContext.Current);
            p.Kill(entireProcessTree: true);
        });
        var ready = await host.StartAsync();
        using var wedged = Process.GetProcessById(ready.Pid);
        Assert.Equal(0, NtSuspendProcess(wedged.SafeHandle)); // motor kilitlendi: canlı, ama shutdown'ı hiç okumaz
        var caller = new CallerContext();

        Task sleep;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(caller);
        try { sleep = host.SleepAsync(); } // devamlar ÇAĞIRANIN bağlamını yakalayabilir
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        await sleep.WaitAsync(WideStartupTimeout);

        Assert.True(killContext.Task.IsCompleted, "the wedged engine was not killed");
        Assert.NotSame(caller, await killContext.Task); // öldürme + ≤1 s bekleme çağıranın bağlamında koşmadı
        Assert.True(wedged.WaitForExit(TimeSpan.Zero), "the wedged Supervisor had not ended when SleepAsync returned");
        Assert.True(host.IsAsleep);
    }

    /// <summary>Motoru KİLİTLER: sürecin bütün thread'lerini tek çağrıda askıya alır (Process Explorer'ın "Suspend"i). Belgelenmemiş
    /// ama Windows XP'den beri aynı <c>ntdll</c> ucu; belgelenmiş alternatif thread'leri tek tek <c>SuspendThread</c>'le durdurmaktır
    /// ve yarışlıdır — sayımla askıya alma arasında runtime yeni bir thread doğurabilir, o thread <c>shutdown</c>'ı okuyup süreci
    /// kapatırdı. Dönüş NTSTATUS'tur, 0 = başarı.</summary>
    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(SafeProcessHandle process);

    /// <summary>[idle-memory · dalga 3 · spec §6.4] Uykuda motorun <c>shutdown</c>'dan sonra KENDİ çıkmasına tanınan süre 2 SANİYEDE
    /// KALIR (spec: "çıkışı ≤ 2 s bekler, çıkmazsa öldürür"): kısalırsa sağlıklı motor zarif kapanmadan öldürülür, uzarsa kilitli bir
    /// motorun uykusu uzar. Beklenen değer üretimden OKUNMAZ, otorite literali olarak yazılır —
    /// <see cref="Kill_exit_wait_stays_one_second"/> deseni. [pin — kırmızısı yalnız derleme]</summary>
    [Fact]
    public void Sleep_exit_wait_stays_two_seconds()
        => Assert.Equal(TimeSpan.FromSeconds(2), EngineHost.SleepExitWait);
}
