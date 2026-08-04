using System.IO;
using System.Windows;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
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

    /// <summary>[D1 review · A2] Dosya VAR ama başlatılamıyor (bozuk binary): pre-flight'ı geçen bu yol eskiden
    /// generic catch'te ham <c>Win32Exception</c> olarak YUTULUYORDU — aynı görünür yüzeye, AYIRT EDİCİ metinle
    /// düşmeli ve yine TEK sinyal üretmeli.</summary>
    [Fact]
    public async Task An_unstartable_supervisor_binary_is_reported_instead_of_being_swallowed()
    {
        string dir = Path.Combine(Path.GetTempPath(), "bo-preflight-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(dir, SupervisorLayout.FolderName));
        string fake = SupervisorLayout.ResolveExePath(dir);
        File.WriteAllText(fake, "this is not a portable executable"); // CreateProcessW → ERROR_BAD_EXE_FORMAT
        try
        {
            await using var host = new EngineHost(fake);
            int exitSignals = 0;
            host.EngineExited += _ => Interlocked.Increment(ref exitSignals);

            var ex = await Assert.ThrowsAsync<EngineUnavailableException>(() => host.StartAsync());

            Assert.Equal(EngineUnavailableReason.CannotStart, ex.Reason);
            Assert.Equal(fake, ex.ExePath);
            Assert.Equal(0, exitSignals); // child doğmadı → EngineExited yok (tek sinyal)
            Assert.Null(host.EnginePid);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_broken_supervisor_gets_its_own_ribbon_line_distinct_from_a_missing_one()
    {
        var vm = NewVm();

        vm.OnEngineUnavailable(MissingSupervisorPath, EngineUnavailableReason.CannotStart);

        Assert.Equal(RunViewModel.EngineCannotStartMessage, vm.EngineDiedMessage);
        Assert.NotEqual(RunViewModel.EngineMissingMessage, RunViewModel.EngineCannotStartMessage);
        Assert.False(vm.EngineRestartable);
        Assert.Contains("engine could not start", vm.GetRunDocumentText(), StringComparison.Ordinal);
    }

    /// <summary>[D1 review · A3] Motor erişilemezken Sync/Build/Rebuild/Retry/Continue ANLAMSIZ: tıklanınca
    /// şeritteki kalıcı mesajla çelişen ikinci bir hata satırı üretirlerdi. Normal (doğmuş) motor ölümü BU
    /// DURUM DEĞİLDİR — orada "Restart engine" sunulur ve komutlar açık kalır (E2/T37 davranışı).</summary>
    [Fact]
    public void An_unreachable_engine_disables_the_actions_but_a_restartable_death_does_not()
    {
        var missing = NewVm();
        VmTopology.Seed(missing); // [topoloji kapısı] run komutlarının ön-koşulu — konu motor erişilebilirliği
        Assert.True(missing.SyncCommand.CanExecute(null));
        Assert.True(missing.BuildCommand.CanExecute(null));

        missing.OnEngineUnavailable(MissingSupervisorPath);

        Assert.True(missing.IsEngineUnavailable);
        Assert.False(missing.SyncCommand.CanExecute(null));
        Assert.False(missing.BuildCommand.CanExecute(null));
        Assert.False(missing.RebuildCommand.CanExecute(null));

        var died = NewVm();
        VmTopology.Seed(died); // [topoloji kapısı] aynı ön-koşul
        died.OnEngineExited(1);

        Assert.False(died.IsEngineUnavailable);
        Assert.True(died.SyncCommand.CanExecute(null)); // Restart engine sunuluyor — eylemler kilitlenmez
        Assert.True(died.BuildCommand.CanExecute(null));
    }

    /// <summary>[D1 review · A3] Uygulama İngilizce-only: engine yokken bir komut gönderilirse konsola düşen
    /// metin de İngilizce olmalı (eskiden <c>"Engine başlatılmadı."</c> sızıyordu).</summary>
    [Fact]
    public async Task Sending_a_command_without_an_engine_reports_in_english()
    {
        await using var host = new EngineHost(MissingSupervisorPath);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => host.SendAsync(new PingCommand(1)));

        Assert.Equal("Engine is not running.", ex.Message);
        Assert.DoesNotMatch("[çğıöşüÇĞİÖŞÜ]", ex.Message);
    }

    /// <summary>[D1 review · C5] Sürüm bilgisi bir yüzeyde GÖRÜNÜR: konsolun boot satırı (design-v1 anlatı dili).</summary>
    [Fact]
    public void Engine_ready_writes_the_version_into_the_console_boot_line()
    {
        var vm = NewVm();

        vm.OnEngineReady("1.0.0+it5");

        Assert.Contains("Engine ready — v1.0.0+it5", vm.GetRunDocumentText(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_normal_engine_death_stays_restartable()
    {
        var vm = NewVm();

        vm.OnEngineExited(2);

        Assert.Equal("Engine stopped unexpectedly (exit 2)", vm.EngineDiedMessage);
        Assert.True(vm.EngineRestartable); // doğmuş bir motor yeniden başlatılabilir — aksiyon görünür kalır
    }

    /// <summary>
    /// [final review I-2] "Restart engine" yolu da D1'in ayrımını UYGULAR. Motor normal öldüğü için şerit
    /// aksiyonu sunar (antivirüs <c>supervisor\</c> klasörünü karantinaya almış olabilir); kullanıcı basınca
    /// preflight <see cref="EngineUnavailableException"/> fırlatır. Eskiden generic <c>catch</c> bunu ayırt
    /// etmiyordu: <see cref="RunViewModel.EngineRestartable"/> true kalıyor, şerit "unexpectedly stopped"
    /// metniyle donuyor ve komutlar AÇIK kalıyordu — her tıklama şeritteki mesajla çelişen ikinci bir hata
    /// satırı üretiyordu (sonsuz "yeniden dene" döngüsü).
    /// </summary>
    [Fact]
    public async Task Restarting_into_a_missing_supervisor_switches_to_the_unavailable_state_instead_of_offering_another_retry()
    {
        await using var host = new EngineHost(MissingSupervisorPath);
        var vm = new RunViewModel(host, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEngineExited(3); // doğmuş motor öldü → aksiyon görünür, komutlar açık
        Assert.True(vm.EngineRestartable);
        Assert.True(vm.SyncCommand.CanExecute(null));

        await vm.RestartEngineCommand.ExecuteAsync(null);

        Assert.False(vm.EngineRestartable);                                   // aksiyon GİZLENİR
        Assert.True(vm.IsEngineUnavailable);
        Assert.Equal(RunViewModel.EngineMissingMessage, vm.EngineDiedMessage); // TEK ve doğru mesaj
        Assert.False(vm.SyncCommand.CanExecute(null));                        // çelişen ikinci hata satırı üretilemez
        Assert.False(vm.BuildCommand.CanExecute(null));
        Assert.False(vm.RebuildCommand.CanExecute(null));
        Assert.DoesNotContain("engine restart failed", vm.GetRunDocumentText(), StringComparison.Ordinal);
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

    /// <summary>Motor YAŞIYOR ama sustu (donmuş run task'ı — ölçülen üretim vakası). Process çıkmadığı için
    /// <c>EngineDiedMessage</c> HİÇ yazılmaz; aksiyon yalnız ona bağlı kalsaydı kullanıcının tek çıkışı
    /// uygulamayı kapatmak olurdu. Kapı, sessizlik uyarısıyla birlikte de açılır.</summary>
    [StaFact]
    public void The_realized_ribbon_offers_the_restart_action_when_the_engine_has_gone_silent()
    {
        var vm = NewVm();
        var host = DsResources.NewHost();
        var ribbon = new StickyRibbon { DataContext = vm, AnimationsEnabledProvider = () => false };
        var window = DsResources.Realize(host, ribbon);
        Assert.Equal(Visibility.Collapsed, ribbon.RestartEngineAction.Visibility); // ön-koşul

        vm.EngineOverdueMessage = "Engine has stopped responding — no reply for 1m 30s · you can restart it";

        Assert.Equal(vm.EngineOverdueMessage, ribbon.PhaseText.Text);
        Assert.Equal(Visibility.Visible, ribbon.RestartEngineAction.Visibility);
        GC.KeepAlive(window);
    }
}
