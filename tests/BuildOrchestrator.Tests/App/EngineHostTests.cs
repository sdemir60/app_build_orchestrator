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
        Assert.Equal(ready2.Pid, readyEvent.Task.Result.Pid);
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
