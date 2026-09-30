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
    /// kendisini <see cref="Kill_and_await_exit_returns_only_after_the_process_has_ended"/> ağaç taraması olmadan
    /// pinler.</para></summary>
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

    /// <summary>[motor · Task 11 · fix-1] Öldürme + bekleme ilkeli (<see cref="EngineHost.KillAndAwaitExit"/>) ancak
    /// process GERÇEKTEN sonlandıktan sonra döner — öldürmenin etkisi ne kadar geç inerse insin (bütçe
    /// <see cref="EngineHost.KillExitWait"/>). Beklemeyi pinleyen test budur; <see cref="Dispose_waits_for_the_supervisor_process_to_exit"/>
    /// üretim yolunu uçtan uca koşar ama ağaç öldürmenin taraması yarışı örttüğü için beklemesiz kodda da yeşildir.
    /// <para><b>Neden geç inen öldürme:</b> <c>TerminateProcess</c> asenkrondur, gerçek yarış birkaç ms'dir ve
    /// makineye bağlıdır. Öldürme stratejisi dikişin parametresidir; test gerçek bir child'ı (<c>PING.EXE</c>) 100 ms
    /// SONRA öldüren bir strateji verir — yarış deterministik olur: ilke beklemeseydi dönüşte process kesin yaşar.</para></summary>
    [Fact]
    public async Task Kill_and_await_exit_returns_only_after_the_process_has_ended()
    {
        using var child = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "PING.EXE"), "-n 30 127.0.0.1")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        var lateKill = Task.CompletedTask;
        try
        {
            EngineHost.KillAndAwaitExit(child, p => lateKill = KillAfterAsync(p, TimeSpan.FromMilliseconds(100)));

            Assert.True(child.WaitForExit(TimeSpan.Zero), "KillAndAwaitExit döndüğünde process hâlâ sonlanmamıştı");
        }
        finally
        {
            await lateKill; // Process nesnesi dispose edilmeden önce geç öldürme insin
            child.Kill();   // çıkmışsa no-op
            child.WaitForExit();
        }
    }

    /// <summary>Etkisi <paramref name="delay"/> sonra inen bir öldürme — asenkron sonlanmanın abartılmış hâli.</summary>
    private static async Task KillAfterAsync(Process process, TimeSpan delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        process.Kill();
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
