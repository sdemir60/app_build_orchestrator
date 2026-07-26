using System.IO;
using System.Windows;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [D1] Supervisor çıktısı uygulamanın yanında YOKKEN kullanıcı SESSİZ kalmaz. Eskiden bu yol yalnız
/// <c>Debug.WriteLine</c> ile yutuluyordu (Release'te derlenip çıkar) — publish çıktısında <c>supervisor\</c>
/// klasörü olmadığı için uygulama hiçbir şey söylemeden yarım açılıyordu.
/// <para>Pinlenen sözleşme: (1) pre-flight AYRIŞAN bir tür atar ve process HİÇ doğmaz, (2) kullanıcıya TEK
/// sinyal gider (child yok → <c>EngineExited</c> yok), (3) şerit kalıcı hata moduna girer ama "Restart engine"
/// aksiyonu GİZLENİR — yeniden başlatmak eksik dosyayı geri getirmez.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public sealed class EnginePreflightTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    private static string MissingSupervisorPath =>
        SupervisorLayout.ResolveExePath(Path.Combine(AppContext.BaseDirectory, "no-such-install"));

    [Fact]
    public async Task Missing_supervisor_output_fails_the_start_with_a_single_explicit_signal()
    {
        await using var host = new EngineHost(MissingSupervisorPath);
        int exitSignals = 0;
        host.EngineExited += _ => Interlocked.Increment(ref exitSignals);

        var ex = await Assert.ThrowsAsync<EngineUnavailableException>(() => host.StartAsync());

        Assert.Equal(MissingSupervisorPath, ex.ExePath);
        Assert.Equal(0, exitSignals); // child hiç doğmadı → ikinci (çakışan) sinyal ÜRETİLEMEZ
    }

    [Fact]
    public void Engine_unavailable_puts_the_ribbon_into_a_permanent_error_line_that_is_not_restartable()
    {
        var vm = NewVm();

        vm.OnEngineUnavailable(MissingSupervisorPath);

        Assert.Equal(RunViewModel.EngineMissingMessage, vm.EngineDiedMessage);
        Assert.False(vm.EngineRestartable);

        // Şerit metni: EngineDiedMessage önceliği (faz metnini EZER) + kırmızı + ✗ glyph.
        var line = RibbonText.Compose(vm.Phase, hasWorkspace: true, allClean: false, default,
            willBuild: 0, finishedOfWillBuild: 0, totalProjects: 0, elapsedMs: 0, etaMs: null, checkDurMs: null,
            warnings: 0, engineDiedMessage: vm.EngineDiedMessage);
        Assert.Equal(RunViewModel.EngineMissingMessage, line.Text);
        Assert.Equal("Brush.StatusFailText", line.BrushKey);
        Assert.Equal("failed", line.Glyph);
    }

    [Fact]
    public void The_full_path_goes_to_the_console_narrative_not_to_the_ribbon()
    {
        var vm = NewVm();

        vm.OnEngineUnavailable(MissingSupervisorPath);

        Assert.Contains(MissingSupervisorPath, vm.GetRunDocumentText(), StringComparison.Ordinal);
        Assert.DoesNotContain(MissingSupervisorPath, vm.EngineDiedMessage!, StringComparison.Ordinal); // şerit tek satır kalır
    }

    [Fact]
    public void A_normal_engine_death_stays_restartable()
    {
        var vm = NewVm();

        vm.OnEngineExited(2);

        Assert.Equal("Engine stopped unexpectedly (exit 2)", vm.EngineDiedMessage);
        Assert.True(vm.EngineRestartable); // doğmuş bir motor yeniden başlatılabilir — aksiyon görünür kalır
    }

    /// <summary>[Realize] Şerit GERÇEKTEN kurulur (merge zinciri + ekran dışı pencere): headless suite XAML
    /// runtime çözümlemesini görmediği için (bkz. <c>c6e9a21</c>) aksiyon görünürlüğü gerçek görsel ağaçta pinlenir.</summary>
    [StaFact]
    public void The_realized_ribbon_hides_the_restart_action_only_when_the_engine_is_missing()
    {
        var vm = NewVm();
        var host = DsResources.NewHost();
        var ribbon = new StickyRibbon { DataContext = vm, AnimationsEnabledProvider = () => false };
        var window = DsResources.Realize(host, ribbon);

        vm.OnEngineUnavailable(MissingSupervisorPath);
        Assert.Equal(RunViewModel.EngineMissingMessage, ribbon.PhaseText.Text);
        Assert.Equal(Visibility.Collapsed, ribbon.RestartEngineAction.Visibility);

        vm.OnEngineExited(2); // normal ölüm → aynı kalıcı hata modu, ama aksiyon geri gelir
        Assert.Equal("Engine stopped unexpectedly (exit 2)", ribbon.PhaseText.Text);
        Assert.Equal(Visibility.Visible, ribbon.RestartEngineAction.Visibility);

        GC.KeepAlive(window);
    }
}
