using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Tests.ProcessControl;
using BuildOrchestrator.Tests.Supervisor;
using Microsoft.Win32.SafeHandles;

namespace BuildOrchestrator.Tests.App;

public class EngineHostTests
{
    // [B1/F1 · fix-1] Startup timeout GENİŞ geçilir; sabitin TEK sahibi TestPaths.WideStartupTimeout
    // (gerekçe orada). Üretim varsayılanı EngineHost.cs'te 5s'de kalır — bunu Default_startup_timeout_stays_five_seconds pinler.
    private static readonly TimeSpan WideStartupTimeout = TestPaths.WideStartupTimeout;

    /// <summary>Var olmayan bir Supervisor yolu — motoru hiç doğmayan konak (ön kontrol <c>NotFound</c>).</summary>
    private static string MissingExe => Path.Combine(AppContext.BaseDirectory, "does-not-exist.exe");

    /// <summary>Windows saat kesmesinin varsayılan aralığı (15,625 ms). Zaman aşımı token'ının zamanlayıcısı süreyi bu kesmelerle
    /// güncellenen bir saatten okur: "2 s doldu" dediği anda yüksek çözünürlüklü bir kronometre en çok bir kesme kadar eksik
    /// ölçebilir. Alt sınır iddiası bu payı düşer — ölçüm hatasıdır, eşik gevşetmesi değil (anında öldüren bir uyku ms'lerde biter).</summary>
    private static readonly TimeSpan ClockTick = TimeSpan.FromMilliseconds(16);

    /// <summary>[B1 fix 3] Yeni bir motor doğuran işi (<paramref name="startEngine"/>: uyanma ya da yeniden başlatma) başlatır ve o motorun sürecini doğduğu
    /// anda yakalar — iki testin TEK kurulumu: outer job'un IOCP'si işten ÖNCE bağlanır; port bağlandığında job'da kalmış üyeler de doğum olarak
    /// bildirilir (eski motorun conhost'u, henüz çıkmamış eski motor), bu yüzden yeni motor imajıyla ve eski motorun PID'i dışarıda bırakılarak
    /// seçilir (<see cref="TestPaths.IsSupervisorOtherThan"/>) ve tutamacı doğumda alınır (<see cref="JobBirths.WaitForProcess"/>). Port, seçimden
    /// sonra kapanır. Dönen: işin görevi ve yeni motorun süreci (çağıran dispose eder).</summary>
    /// <param name="host">Motorun konağı.</param>
    /// <param name="oldPid">Uyutulan ya da öldürülen eski motorun PID'i.</param>
    /// <param name="startEngine">Yeni motoru doğuran iş.</param>
    /// <param name="what">Doğum hiç gelmezse zaman aşımının metni.</param>
    private static (Task<EngineReadyEvent> Start, Process Engine) StartAndCatchTheNewEngine(EngineHost host, int oldPid,
        Func<Task<EngineReadyEvent>> startEngine, string what)
    {
        using var births = host.OuterJob.AttachCompletionPort();
        var started = startEngine();
        return (started, JobBirths.WaitForProcess(births, WideStartupTimeout, what, TestPaths.IsSupervisorOtherThan(oldPid)));
    }

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
    /// thread'inde koşturur — bağlama dönen bir devam, altında çalıştığı bağlamı gözlemcisine gösterir. Post'ları da sayar: bağlamı
    /// hiç yakalamayan bir yol sayacı 0 bırakır.</summary>
    private sealed class CallerContext : SynchronizationContext
    {
        private int _posts;

        /// <summary>Bu bağlama post edilen devam sayısı.</summary>
        public int Posts => Volatile.Read(ref _posts);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _posts);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var previous = Current;
                SetSynchronizationContext(this);
                try { d(state); }
                finally { SetSynchronizationContext(previous); }
            });
        }
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
    /// açılır, sonra sinyali beklemeden (0 ms) sorulur. [idle-memory · dalga 3 · fix 2] <c>GetProcessById</c> tanıtıcı AÇMAZ (ilk
    /// erişimde açılır): açılış artık açıkça <c>SafeHandle</c> erişimiyle, Dispose'dan önce yapılır — eskiden ilk erişim Dispose'dan sonraki
    /// <c>WaitForExit</c>'ti ve PID'in o arada başka bir sürece geçmediğinin güvencesi yoktu.</para>
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
        using var supervisor = Process.GetProcessById(host.EnginePid!.Value);
        _ = supervisor.SafeHandle; // tanıtıcı Dispose'dan ÖNCE açılır — PID başkasına geçemez

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
        await using var host = new EngineHost(MissingExe);
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
        using var slept = Process.GetProcessById(first.Pid);
        _ = slept.SafeHandle; // tanıtıcı uykudan ÖNCE açılır (GetProcessById açmaz): PID başkasına geçemez, çıkış kodu sonra okunur

        await host.SleepAsync();

        Assert.True(host.IsAsleep);
        Assert.Null(host.EnginePid);
        Assert.True(slept.WaitForExit(TimeSpan.Zero), "the slept Supervisor had not ended when SleepAsync returned");
        Assert.Equal(0, slept.ExitCode); // shutdown'la KENDİSİ çıktı (SupervisorHost 0 döner) — öldürülseydi -1 olurdu
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

        var born = JobBirths.WaitForBirth(births, woken.Pid, WideStartupTimeout);
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
    /// bir yarışla havuza düşüyordu; burada sözleşme pinlenir.</para>
    /// <para><b>[fix 1 · M2] Zarif bekleyiş gerçekten beklenir:</b> öldürme, uykunun başlamasından en az <see cref="EngineHost.SleepExitWait"/>
    /// sonra gelir (alt sınır — yük onu yalnız uzatır; payı <see cref="ClockTick"/>). Bekleyişi Restart'ınki gibi sıfırlayan bir uyku
    /// burada kırmızıdır.</para>
    /// <para><b>[fix 1 · M5] Kasıtlı durdurma sürerken gönderim hemen düşer:</b> kapanış ilk iş yazıcıyı devralır; öldürme anında
    /// (durdurmanın ortasında) gönderilen bir komut bugünkü "motor çalışmıyor" hatasıyla reddedilir — ölmekte olan sürecin borusuna
    /// yazılıp kaybolmaz. Eskiden yazıcı öldürme bitene dek dururdu.</para></summary>
    [Fact]
    public async Task Sleep_kills_a_wedged_engine_off_the_callers_context()
    {
        using var sandbox = new SupervisorSandbox();
        var killContext = new TaskCompletionSource<SynchronizationContext?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sleepClock = new Stopwatch();
        TimeSpan killedAfter = TimeSpan.Zero;
        Exception? sendDuringStop = null;
        EngineHost? stopping = null; // öldürme stratejisi konağa geri döner — konak kurulduktan sonra atanır
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout, killStrategy: p =>
        {
            killedAfter = sleepClock.Elapsed;
            try { _ = stopping!.SendAsync(new PingCommand(9)); }
            catch (Exception ex) { sendDuringStop = ex; } // öldürme yine gelsin — iddia aşağıda
            killContext.TrySetResult(SynchronizationContext.Current);
            p.Kill(entireProcessTree: true);
        });
        stopping = host;
        var notRunning = await Assert.ThrowsAsync<InvalidOperationException>(() => host.SendAsync(new PingCommand(0)));
        var ready = await host.StartAsync();
        using var wedged = Process.GetProcessById(ready.Pid);
        Assert.Equal(0, NtSuspendProcess(wedged.SafeHandle)); // motor kilitlendi: canlı, ama shutdown'ı hiç okumaz
        var caller = new CallerContext();

        Task sleep;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(caller);
        sleepClock.Start();
        try { sleep = host.SleepAsync(); } // devamlar ÇAĞIRANIN bağlamını yakalayabilir
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        await sleep.WaitAsync(WideStartupTimeout);

        Assert.True(killContext.Task.IsCompleted, "the wedged engine was not killed");
        Assert.NotSame(caller, await killContext.Task); // öldürme + ≤1 s bekleme çağıranın bağlamında koşmadı
        Assert.True(killedAfter >= EngineHost.SleepExitWait - ClockTick,
            $"the sleep killed the engine {killedAfter.TotalMilliseconds:0} ms after it began — before its graceful wait ran out");
        Assert.Equal(notRunning.Message, sendDuringStop?.Message); // durdurma sürerken gönderim: bugünkü "çalışmıyor" hatası
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

    // ==================================== [idle-memory · dalga 3 · fix 1] Dispose, yarışlar, bağlam ====================================

    /// <summary>[fix 1 · I1] Çıkış, uçuştaki bir uyanmanın arkasında SIRAYA GİRMEZ: dispose kapıyı beklemeden önce nesli ilerletir
    /// ve konağın ömür token'ını iptal eder; motorun hazır olmasını bekleyen uyanma milisaniyeler içinde vazgeçer
    /// (<see cref="OperationCanceledException"/> — öldürmez, nesil ilerlediği için ölüm sinyali de yoktur) ve kapıyı bırakır; dispose
    /// bugünkü kapanışı koşar (shutdown ≤ 500 ms + öldürme ≤ <see cref="EngineHost.KillExitWait"/>) ve job'ı kapatır.
    /// <para><b>Neden:</b> <c>App.OnExit</c> disposal'ı en çok <see cref="AppShutdown.DisposalTimeout"/> bekler, sonra güncelleyiciyi
    /// başlatır; kuyruğa giren bir dispose vazgeçilmiş sayılır ve <c>Update.exe</c>, Supervisor öldürülüp çıkışı beklenmeden
    /// çalışırdı. Eskiden dispose uyanmanın tüm zaman aşımını (<see cref="EngineHost.StartupTimeout"/>) bekliyordu.</para>
    /// <para><b>Kurgu:</b> uyanmanın yeni motoru hazır olmadan KİLİTLENİR (<see cref="NtSuspendProcess"/>; PID outer job'un doğum
    /// bildiriminden alınır) — uyanma tavanına (burada 60 s) dek bekleyecektir. Ölçüt üretimin çıkış yolunun kendisidir
    /// (<see cref="AppShutdown.WaitForAsyncDisposal"/>, aynı tavan).</para>
    /// <para><b>[DEĞİŞEN KURULUM — idle-memory · dalga 3 · M-2]</b> Eskiden yeni motorun PID'i <c>WakeAsync</c> döner dönmez
    /// <see cref="EngineHost.EnginePid"/>'den okunuyordu: uyanma süreci çağıranın thread'inde, dönmeden doğuruyordu. Uyanma artık başlatmadan önce
    /// havuza sıçrar (<see cref="A_wake_started_on_the_callers_context_leaves_it_before_any_work"/>) ve döndüğü anda süreç henüz yoktur — süreç job'un
    /// IOCP'sinden, doğduğu anda alınır (sleep/poll yok — D8). İddia değişmedi.</para>
    /// <para><b>[DEĞİŞEN KURULUM — dalga 3 · B1 fix 1 · I-3]</b> Eskiden "uykudan sonra bağlanan portun ilk doğumu uyanan motorundur" sayılıyor ve
    /// o PID sonradan açılıyordu. Ön koşul yanlıştı: port bağlandığında job'da kalmış üyeler de doğum olarak bildirilir (uyutulan motorun conhost'u,
    /// çıkmakta olan eski motor) ve bildirilen PID açılmadan gidebilir — bir kez ölçüldü: <c>Process with an Id of 27996 is not running</c>.
    /// Uyanan motor artık imajıyla seçilir ve tutamacı doğumda alınır (<see cref="JobBirths.WaitForProcess"/>); [B1 fix 2 · 4c] uyutulan motorun
    /// PID'i dışarıda bırakılır — henüz çıkmadıysa o da bildirilir ve imajı aynıdır (kurulum tek yerde: <see cref="StartAndCatchTheNewEngine"/>).
    /// İddia değişmedi.</para></summary>
    [Fact]
    public async Task Dispose_preempts_a_wake_that_waits_for_a_frozen_engine()
    {
        using var sandbox = new SupervisorSandbox();
        var host = sandbox.IsolatedEngineHost(WideStartupTimeout);
        Process? frozen = null;
        try
        {
            var first = await host.StartAsync(); // uyutulacak motorun hazır olayı
            await host.SleepAsync();
            (var wake, frozen) = StartAndCatchTheNewEngine(host, first.Pid, () => host.WakeAsync(),
                "the woken engine was never born into the outer job");
            Assert.Equal(0, NtSuspendProcess(frozen.SafeHandle)); // yeni motor hazır olmadan kilitlendi
            const string ReadyBeforeFrozen = "the woken engine was ready before it was frozen — the precondition was not set up";
            Assert.False(wake.IsCompleted, ReadyBeforeFrozen);

            bool disposed = AppShutdown.WaitForAsyncDisposal(host, AppShutdown.DisposalTimeout); // App.OnExit'in yolu

            Assert.True(disposed, "the disposal queued behind the wake and did not finish within AppShutdown.DisposalTimeout");
            Assert.True(frozen.WaitForExit(TimeSpan.Zero), "the frozen engine was still alive after the disposal");
            // [fix 2] Test thread'i bir motor açılışından uzun takılırsa engineReady kilitlemeden önce boruya yazılmış olabilir: uyanma o
            // zaman BAŞARIYLA biter. Bu bir ön koşul kaybıdır — aşağıdaki "hiç istisna yok" iddiası onu yanlış anlatırdı.
            Assert.False(wake.IsCompletedSuccessfully, ReadyBeforeFrozen);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wake); // uyanma vazgeçti — öldürme dispose'undu
        }
        finally
        {
            KillIfAlive(frozen); // kırmızıda uyanma kapıyı bıraksın, bekleyen dispose bitsin
            await host.DisposeAsync();
        }
    }

    /// <summary>[fix 1 · I1] Uykunun zarif bekleyişi de çıkışı tutamaz: kilitli bir motorda uyku <see cref="EngineHost.SleepExitWait"/>
    /// bekleyecekken gelen dispose onu keser — uyku vazgeçer (<see cref="OperationCanceledException"/>, öldürmeden, konak uyumaz),
    /// dispose motoru öldürür ve <see cref="AppShutdown.DisposalTimeout"/> içinde biter. Eskiden dispose uykunun bekleyişini ve
    /// öldürmesini bitirmesini beklerdi; uyku da "uyudum" diye biterdi.</summary>
    [Fact]
    public async Task Dispose_preempts_a_sleep_that_waits_for_a_wedged_engine()
    {
        using var sandbox = new SupervisorSandbox();
        var host = sandbox.IsolatedEngineHost(WideStartupTimeout);
        Process? wedged = null;
        try
        {
            var ready = await host.StartAsync();
            wedged = Process.GetProcessById(ready.Pid);
            Assert.Equal(0, NtSuspendProcess(wedged.SafeHandle)); // shutdown'ı hiç okumaz: uyku tüm zarif bekleyişi bekleyecek
            var sleep = host.SleepAsync(); // kapı alındı — uyku uçuşta

            bool disposed = AppShutdown.WaitForAsyncDisposal(host, AppShutdown.DisposalTimeout);

            Assert.True(disposed, "the disposal queued behind the sleep and did not finish within AppShutdown.DisposalTimeout");
            Assert.True(wedged.WaitForExit(TimeSpan.Zero), "the wedged engine was still alive after the disposal");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sleep); // uyku vazgeçti — öldürme dispose'undu
            Assert.False(host.IsAsleep);
        }
        finally
        {
            KillIfAlive(wedged);
            await host.DisposeAsync();
        }
    }

    /// <summary>[idle-memory · dalga 3 · defter 6] Dikişte tutma — "kontrol geçti, ardından şu oldu" penceresini açık tutmanın TEK yolu. Konağın
    /// test dikişine <see cref="Hold"/> verilir: dikiş çağrıldığı anda <see cref="Reached"/> tamamlanır ve dikişi çağıran thread
    /// <see cref="Release"/>'e dek orada tutulur — en çok <see cref="WideStartupTimeout"/>: kırmızı bir koşu havuz thread'ini sonsuza dek
    /// tutmasın. Testler bırakmayı <c>finally</c>'de yapar: erken düşen bir iddia tutulan thread'i ve onu bekleyen işlemi asılı bırakmaz. Olay
    /// nesnesi bilerek dispose edilmez — bırakılan thread <c>Wait</c>'ten henüz çıkmamış olabilir.</summary>
    private sealed class SeamHold
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new();

        /// <summary>Konağın dikişine verilen eylem: varıldığını bildirir ve bırakılana dek tutar.</summary>
        public void Hold()
        {
            _reached.TrySetResult();
            _release.Wait(WideStartupTimeout);
        }

        /// <summary>Dikişe varılmasını bekler (en çok <see cref="WideStartupTimeout"/>).</summary>
        public Task Reached() => _reached.Task.WaitAsync(WideStartupTimeout);

        /// <summary>Tutulan thread'i bırakır (tekrar çağrılabilir).</summary>
        public void Release() => _release.Set();
    }

    /// <summary>Test temizliği: kilitlenmiş motoru (varsa, hâlâ yaşıyorsa) öldürür ve tanıtıcıyı bırakır — kırmızı bir koşuda
    /// uçuştaki işlem kapıyı bıraksın, bekleyen dispose bitsin.</summary>
    private static void KillIfAlive(Process? process)
    {
        if (process is null) return;
        try { process.Kill(); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { /* zaten çıktı */ }
        process.Dispose();
    }

    /// <summary>[fix 1 · M1] Kendiliğinden ölüm ile kasıtlı uykunun yarışında uyku kazanır — iki yönde de: uyku nesli ilerletirken
    /// GİDEN nesli de sahiplenir, böylece kontrolünü çoktan geçmiş (nesli güncel bulmuş) bir bekçi ölüm sinyalini artık veremez;
    /// ölüm uyku sayılır. Ters yön (bekçi önce sahiplenir) uykuyu hiç başlatmaz — <see cref="Sleep_and_wake_change_nothing_once_the_engine_died"/>.
    /// <para><b>Kurgu (dikişle deterministik):</b> bekçi kontrolü geçtiği anda <see cref="EngineHost.DebugExitWatchPassedCheck"/>'te
    /// tutulur; motor dışarıdan öldürülür, uyku o pencerede gelir, sonra bekçi bırakılır. Eskiden uyku nesli yalnız ilerletiyordu:
    /// bırakılan bekçi ölüm hakkını kazanır ve uyuyan motor için <see cref="EngineHost.EngineExited"/> atardı.</para></summary>
    [Fact]
    public async Task A_death_a_sleep_overtakes_counts_as_the_sleep()
    {
        using var sandbox = new SupervisorSandbox();
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout);
        var watcher = new SeamHold();
        host.DebugExitWatchPassedCheck = watcher.Hold;
        var ready = await host.StartAsync();
        var watch = host.ExitWatch!;
        int exited = 0;
        host.EngineExited += _ => Interlocked.Increment(ref exited);
        try
        {
            using (var engine = Process.GetProcessById(ready.Pid)) engine.Kill(); // kendiliğinden ölüm
            await watcher.Reached(); // bekçi nesli güncel buldu — ölümü henüz sahiplenmedi

            await host.SleepAsync(); // ölümü geçer: nesli ilerletir, giden nesli sahiplenir

            Assert.True(host.IsAsleep);
        }
        finally { watcher.Release(); }
        await watch.WaitAsync(WideStartupTimeout); // bekçi kararını verdi
        Assert.Equal(0, Volatile.Read(ref exited)); // ölüm uyku sayıldı — ölüm sinyali yok
    }

    /// <summary>[fix 2 · N1] Uykunun kontrolünden SONRA bildirilen bir ölüm uykuyu "uyudu" diye bitirmez: konak uyumaz, ölüm sinyali tek
    /// kalır. Uyku, kontrolünün canlı bulduğu nesli sahiplenmeye çalışır; bekçi o nesli zaten sahiplenmişse (ölüm bildirildi) uyku
    /// kaybeder ve uyumadan döner — ölümü Restart toparlar. Eskiden uyku giden nesli sahiplenmeye çalışıp sonucunu yok sayıyordu: ölüm
    /// bildirildiği hâlde konak uyuyor diye biterdi (VM "öldü", konak "uyuyor" — iki görüş ayrışırdı); çerçeve hatası yolunda okuyucu nesli
    /// kendisi ilerlettiği için uykunun sahiplendiği (yeni − 1) nesil ölen nesil bile değildi.
    /// <para><b>Kurgu (dikişle deterministik):</b> uyku, motoru canlı bulup havuza sıçradığı anda
    /// <see cref="EngineHost.DebugSleepPassedCheck"/>'te tutulur; motor öldürülür, ölüm bildirilir (<see cref="EngineHost.EngineExited"/>),
    /// sonra uyku bırakılır. Çerçeve hatası yolunun aynı yarışı kendi dikişleriyle sınanır
    /// (<see cref="A_framing_fault_the_sleeps_check_overlaps_keeps_the_host_awake"/>).</para>
    /// <para><b>[defter 4 · T18 OOS-2] Son durum — PİN:</b> sahiplenmeyi kaybeden uyku konağı tutarlı bırakır: süreç yok
    /// (<see cref="EngineHost.EnginePid"/> null), gönderim ve uyanma bugünkü "motor çalışmıyor" hatasıyla düşer, <see cref="EngineHost.RestartAsync"/>
    /// yeni bir motorla toparlar (VM'in ölüm yolu bunu sunar). Kırmızısı gösterilemez — bugünkü kod da yeşildir; ileride kaybeden uykunun
    /// konağı yarım bırakmasını (yazıcı ya da süreç tanıtıcısı kalır, uyanma eski olayı döner) yakalar.</para></summary>
    [Fact]
    public async Task A_death_reported_after_the_sleeps_check_keeps_the_host_awake()
    {
        using var sandbox = new SupervisorSandbox();
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout);
        var sleeper = new SeamHold();
        host.DebugSleepPassedCheck = sleeper.Hold;
        var notRunning = await Assert.ThrowsAsync<InvalidOperationException>(() => host.SendAsync(new PingCommand(0)));
        var ready = await host.StartAsync();
        var watch = host.ExitWatch!;
        int exited = 0;
        var died = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.EngineExited += _ =>
        {
            Interlocked.Increment(ref exited);
            died.TrySetResult();
        };

        var sleep = host.SleepAsync();
        try
        {
            await sleeper.Reached(); // uyku motoru canlı buldu — nesli henüz sahiplenmedi
            using (var engine = Process.GetProcessById(ready.Pid)) engine.Kill(); // kendiliğinden ölüm
            await died.Task.WaitAsync(WideStartupTimeout); // ölüm bildirildi: bekçi nesli sahiplendi
        }
        finally { sleeper.Release(); }
        await sleep.WaitAsync(WideStartupTimeout);

        Assert.False(host.IsAsleep); // ölüm uykudan önce sahiplenildi — konak uyumaz, Restart toparlar
        await watch.WaitAsync(WideStartupTimeout);
        Assert.Equal(1, Volatile.Read(ref exited)); // tek ölüm sinyali

        // [defter 4 · PİN] son durum: süreç yok, gönderim ve uyanma bugünkü hatayla düşer, Restart toparlar
        Assert.Null(host.EnginePid);
        var send = await Assert.ThrowsAsync<InvalidOperationException>(() => host.SendAsync(new PingCommand(1)));
        Assert.Equal(notRunning.Message, send.Message);
        var wake = await Assert.ThrowsAsync<InvalidOperationException>(() => host.WakeAsync());
        Assert.Equal(notRunning.Message, wake.Message);
        var restarted = await host.RestartAsync();
        Assert.NotEqual(ready.Pid, restarted.Pid);
        Assert.Equal(restarted.Pid, host.EnginePid);
        Assert.Equal(1, Volatile.Read(ref exited)); // yeniden başlatma ölüm sinyali üretmedi
    }

    /// <summary>[defter 3 · T18 OOS-1] Çerçeve hatası yolu nesli KENDİSİ ilerletir (eski bekçiyi susturmak için) ve motoru öldürüp kendi
    /// neslinin ölümünü ancak SONRA sahiplenir. Uykunun kontrolü bu aradaki pencereye düşerse motor hâlâ canlıdır ve uyku ölümle yarışır; kural
    /// <see cref="A_death_reported_after_the_sleeps_check_keeps_the_host_awake"/>'inkiyle aynıdır: ölüm önce sahiplenildiyse konak UYUMAZ, ölüm
    /// sinyali tektir.
    /// <para><b>Kusur (eski):</b> uyku ve canlılık sorusu nesli alandan (<c>_generation</c>) okuyordu; çerçeve hatası alanı çoktan ilerlettiği için
    /// uyku motorun neslini değil bir sonrakini sahiplendi — ölüm kendi neslini sahiplenip bildirildiği hâlde uyku da "kazandı": VM ölü, konak
    /// uyuyor (iki görüş ayrışırdı; Restart toparlıyordu). Artık ikisi de motorun KENDİ neslini — başlatmanın atadığını — okur.</para>
    /// <para><b>Kurgu (iki dikişle deterministik):</b> bir pong bozuk çerçeve sayılır (<see cref="EngineHost.DebugFramingFault"/> — gerçek bir
    /// Supervisor bozuk satır yazdırılamaz); okuyucu nesli ilerlettiği anda, motoru öldürmeden önce tutulur
    /// (<see cref="EngineHost.DebugFramingFaultPassedCheck"/>); uyku motoru canlı bulur ve sahiplenmeden önce tutulur
    /// (<see cref="EngineHost.DebugSleepPassedCheck"/>); okuyucu bırakılır — motoru öldürür, ölümü bildirir —, sonra uyku bırakılır.</para>
    /// <para>KIRMIZI (değişiklikten önce): konak uyuyordu (<c>Assert.False(host.IsAsleep)</c>).</para></summary>
    [Fact]
    public async Task A_framing_fault_the_sleeps_check_overlaps_keeps_the_host_awake()
    {
        using var sandbox = new SupervisorSandbox();
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout);
        var reader = new SeamHold();
        var sleeper = new SeamHold();
        host.DebugFramingFault = e => e is PongEvent; // motorun cevabı bozuk bir çerçeve sayılır
        host.DebugFramingFaultPassedCheck = reader.Hold;
        host.DebugSleepPassedCheck = sleeper.Hold;
        await host.StartAsync();
        int exited = 0;
        var died = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.EngineExited += _ =>
        {
            Interlocked.Increment(ref exited);
            died.TrySetResult();
        };

        Task sleep;
        try
        {
            await host.SendAsync(new PingCommand(1)); // pong'u bozuk çerçeve sayılır
            await reader.Reached(); // okuyucu nesli ilerletti — motor henüz öldürülmedi
            sleep = host.SleepAsync();
            await sleeper.Reached(); // uyku motoru canlı buldu — nesli henüz sahiplenmedi
            reader.Release();
            await died.Task.WaitAsync(WideStartupTimeout); // okuyucu motoru öldürdü ve kendi neslinin ölümünü bildirdi
        }
        finally
        {
            reader.Release();
            sleeper.Release();
        }
        await sleep.WaitAsync(WideStartupTimeout);

        Assert.False(host.IsAsleep); // ölüm uykudan önce sahiplenildi — konak uyumaz, Restart toparlar
        Assert.Equal(1, Volatile.Read(ref exited)); // tek ölüm sinyali
        Assert.Null(host.EnginePid);
    }

    /// <summary>[fix 1 · M1] Nesli geride kalmış bir çıkış bekçisi daha yeni bir başlatmayı ASLA düşüremez: her nesil kendi hazır
    /// görevini taşır ve bekçi yalnız onu bozar. Eskiden bekçi alandaki güncel görevi (<c>_ready</c>) bozuyordu: kontrolünü
    /// geçtikten sonra yeni bir nesil başlamışsa, yeni motor sağlam olduğu hâlde başlatma "Engine died during startup" ile düşer,
    /// yeni motor öldürülür ve bir ölüm sinyali atılırdı.
    /// <para><b>Kurgu (dikişle deterministik):</b> eski motor öldürülür, bekçisi kontrolü geçtiği anda tutulur; Restart yeni nesli
    /// açar; job'ın IOCP'si yeni motorun doğumunu bildirdiği anda yeni neslin hazır görevi vardır ve motor henüz hazır olamaz; bekçi o anda
    /// bırakılır. Okuyucunun aynı yakalaması (kendi neslinin görevi) dikişsizdir: süzgeçle tamamlama arasında tutulabilecek bir an yoktur —
    /// yapı gereği aynı kural.</para>
    /// <para><b>[DEĞİŞEN KURULUM — dalga 3 · B1 fix 1 · I-3 · m-4]</b> Eskiden port eski motor öldükten sonra bağlanıyor ve İLK doğum yeni motorun
    /// sayılıyordu (<c>_ =&gt; true</c>). Port bağlandığında job'da kalmış üyeler de bildirildiği için ilk bildirim eski motorun conhost'u olabilir
    /// ve bekçi yeni nesil doğmadan bırakılırdı (kurgu sessizce zayıflardı). Yeni motor artık imajıyla seçilir
    /// (<see cref="JobBirths.WaitForProcess"/>); [B1 fix 2 · 4c] öldürülen motorun PID'i dışarıda bırakılır — henüz çıkmadıysa o da bildirilir ve
    /// imajı aynıdır (kurulum tek yerde: <see cref="StartAndCatchTheNewEngine"/>). İddia değişmedi.</para></summary>
    [Fact]
    public async Task A_stale_exit_watcher_never_fails_a_newer_start()
    {
        using var sandbox = new SupervisorSandbox();
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout);
        var staleWatcher = new SeamHold();
        host.DebugExitWatchPassedCheck = staleWatcher.Hold;
        var first = await host.StartAsync();
        var staleWatch = host.ExitWatch!;
        int exited = 0;
        host.EngineExited += _ => Interlocked.Increment(ref exited);
        Task<EngineReadyEvent> restart;
        try
        {
            using (var engine = Process.GetProcessById(first.Pid)) engine.Kill(); // kendiliğinden ölüm
            await staleWatcher.Reached(); // eski bekçi nesli güncel buldu

            var caught = StartAndCatchTheNewEngine(host, first.Pid, () => host.RestartAsync(),
                "the restarted engine was never born into the outer job");
            restart = caught.Start;
            caught.Engine.Dispose(); // yalnız doğum anı gerekir: yeni neslin hazır görevi o an vardır, motor henüz hazır olamaz
        }
        finally { staleWatcher.Release(); } // yeni neslin hazır görevi bekliyor — eski bekçi şimdi bırakılır

        var second = await restart;

        Assert.NotEqual(first.Pid, second.Pid);
        Assert.Equal(second.Pid, host.EnginePid);
        await staleWatch.WaitAsync(WideStartupTimeout);
        Assert.Equal(0, Volatile.Read(ref exited)); // ölüm Restart sayıldı; yeni motor ölmedi
    }

    /// <summary>[fix 1 · M3] Dispose edilmiş konak hiçbir başlatmayı kabul etmez ve uyumaz: Start/Restart/Wake HİÇBİR yan etkiden
    /// önce <see cref="ObjectDisposedException"/> atar — exe'si olmayan bir konakta bile ön kontrolden ("not found") önce; uyku
    /// no-op'tur; uyurken dispose edilen konak artık uyumaz. Eskiden dispose uykuyu olduğu gibi bırakıyordu (<c>IsAsleep</c> true),
    /// başlatmalar kapalı job'a süreç doğurmaya çalışıp ancak orada düşüyordu.</summary>
    [Fact]
    public async Task A_disposed_host_refuses_every_start_and_is_not_asleep()
    {
        using var sandbox = new SupervisorSandbox();
        var host = sandbox.IsolatedEngineHost(WideStartupTimeout);
        await host.StartAsync();
        await host.SleepAsync();
        Assert.True(host.IsAsleep); // ön koşul: uyurken dispose edilecek

        await host.DisposeAsync();

        Assert.False(host.IsAsleep);
        await host.SleepAsync(); // no-op
        Assert.False(host.IsAsleep);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => host.WakeAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => host.StartAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => host.RestartAsync());
        Assert.Null(host.EnginePid);

        var missing = new EngineHost(MissingExe); // ret ön kontrolden de önce gelir
        await missing.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => missing.StartAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => missing.RestartAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => missing.WakeAsync());
    }

    /// <summary>[fix 1 · M4] Öldürme fırlatsa bile motorun boruları ve tanıtıcısı bırakılır, yazıcı susturulmuş motoru göstermez:
    /// <see cref="EngineHost.SendAsync"/> bugünkü "motor çalışmıyor" hatasını verir ve borusu kapanan motor kendiliğinden çıkar.
    /// Eskiden fırlatan bir öldürme <c>child.Dispose()</c>'u ve yazıcının temizlenmesini atlıyordu: gönderim, nesli geride kalmış
    /// (sinyali susturulmuş) motora yazılıyor, borular açık kalıyordu.
    /// <para><b>Kurgu:</b> öldürme stratejisi fırlatır (erişim reddi); vazgeçilen başlatma öldürmeyi dener
    /// (<see cref="A_start_that_gives_up_kills_the_engine_off_the_callers_context"/>'in iptal yolu).</para></summary>
    [Fact]
    public async Task A_kill_that_throws_still_releases_the_engines_pipes()
    {
        using var sandbox = new SupervisorSandbox();
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout, killStrategy: _ => throw new Win32Exception(5));
        using var giveUp = new CancellationTokenSource();
        var start = host.StartAsync(giveUp.Token);
        Assert.False(start.IsCompleted, "StartAsync returned before it began waiting — the precondition was not set up");
        using var engine = Process.GetProcessById(host.EnginePid!.Value);
        _ = engine.SafeHandle; // tanıtıcı ŞİMDİ açılır: konak kendi tanıtıcısını bırakınca da PID başkasına geçemez

        giveUp.Cancel(); // vazgeçilen başlatma öldürmeyi dener — öldürme fırlatır

        await Assert.ThrowsAsync<Win32Exception>(() => start);
        Assert.Null(host.EnginePid);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.SendAsync(new PingCommand(1))); // yazıcı bırakıldı
        await engine.WaitForExitAsync().WaitAsync(WideStartupTimeout); // boruları kapandı: motor kendiliğinden çıktı
    }

    /// <summary>[fix 1] Uyku, çağıranın bağlamından (UI thread'i) hiçbir iş yapmadan ÖNCE ayrılır: kapı serbestse çağıranın
    /// thread'inde alınır, sonra açık bir sıçrayışla havuza geçilir — shutdown yazımının devamları çağıranın bağlamına hiç
    /// dönmez. Eskiden yazım çağıranın thread'inde başlıyordu ve <c>NdjsonWriter</c>'ın devamları Dispatcher'a post ediliyordu;
    /// <c>OnExit</c> UI'ı bloklarken o yazım ancak 500 ms'lik zaman aşımıyla sonlanırdı. Ölçüt: çağıranın bağlamına yapılan
    /// post sayısı (<see cref="CallerContext.Posts"/>) sıfır kalır.</summary>
    [Fact]
    public async Task A_sleep_started_on_the_callers_context_leaves_it_before_any_work()
    {
        using var sandbox = new SupervisorSandbox();
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout);
        await host.StartAsync();
        var caller = new CallerContext();

        Task sleep;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(caller);
        try { sleep = host.SleepAsync(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        await sleep.WaitAsync(WideStartupTimeout);

        Assert.True(host.IsAsleep);
        Assert.Equal(0, caller.Posts);
    }

    /// <summary>[idle-memory · dalga 3 · M-2] Uyanma, çağıranın bağlamından (UI thread'i) başlatmanın hiçbir işini yapmadan ÖNCE ayrılır — uykunun
    /// sıçrayışının aynısı: kapı serbestse çağıranın thread'inde alınır, sonra havuza geçilir; ön kontrol (<c>File.Exists</c>), süreç doğurma
    /// (<c>CreateProcessW</c>, job ataması, devam ettirme) ve okuyucu/bekçi kurulumu havuzda koşar. Uyanma etkileşimli yoldadır — boşta kalmış bir
    /// uygulamada Build, Sync ya da proje logu tıklaması — ve <c>CreateProcessW</c> antivirüs taraması altında uzayabilir; açılış ve Restart aynı
    /// işi bugün de çağıranın thread'inde başlatır (bilinçli olarak dokunulmadı).
    /// <para><b>Nasıl gözlenir:</b> <see cref="A_start_that_gives_up_kills_the_engine_off_the_callers_context"/>'in deseni — başlatmanın işi
    /// başlarken çağrılan dikiş (<see cref="EngineHost.DebugStartWorkBegins"/>) <see cref="SynchronizationContext.Current"/>'ı yakalar; uyanma
    /// <see cref="CallerContext"/> altında başlatılır.</para>
    /// <para>KIRMIZI (sıçrayışsız uyanma): başlatmanın işi çağıranın bağlamında başlıyordu (<c>Assert.NotSame</c>).</para></summary>
    [Fact]
    public async Task A_wake_started_on_the_callers_context_leaves_it_before_any_work()
    {
        using var sandbox = new SupervisorSandbox();
        await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout);
        await host.StartAsync();
        await host.SleepAsync();
        Assert.True(host.IsAsleep); // ön-koşul: uyanacak bir motor var
        var workContext = new TaskCompletionSource<SynchronizationContext?>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.DebugStartWorkBegins = () => workContext.TrySetResult(SynchronizationContext.Current);
        var caller = new CallerContext();

        Task<EngineReadyEvent> wake;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(caller);
        try { wake = host.WakeAsync(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        var woken = await wake.WaitAsync(WideStartupTimeout);

        Assert.NotSame(caller, await workContext.Task.WaitAsync(WideStartupTimeout)); // ön kontrol + süreç doğurma çağıranın bağlamında koşmadı
        Assert.Equal(0, caller.Posts);
        Assert.Equal(woken.Pid, host.EnginePid);
    }
}
