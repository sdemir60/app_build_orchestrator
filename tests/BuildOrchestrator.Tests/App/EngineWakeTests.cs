using System.Windows;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.Tests.App.EngineSleepProbe;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 3 · spec §6.4 · Task 21] Uyuyan motorun TEK kapısı (<c>RunViewModel.EnsureEngineAwakeAsync</c>): motoru isteyen her yol
/// göndermeden önce uyuyan motoru uyandırır — tablo tek yerdedir (<see cref="EngineWakeRule"/>, <c>EngineWakeRuleTests</c>). Kapı yalnız VM'in
/// motor durumunu okur (karar 32): hazır ya da başlıyor → gönder; uyku sürüyor → bitmesini bekle, yeniden oku; uyuyor → TEK paylaşılan
/// uyanma; uyanıyor → ona katıl; ölü ya da erişilemez → uyandırma yok, gönderme yok.
///
/// <para><b>Bu sınıfın pinledikleri:</b> her uyandıran yol önce uyandırır ve komutu uyanmadan SONRA gönderir; uyanma sessizdir — konsola
/// "Engine ready" yazmaz, açılış Sync'ini göndermez (karar 20; iddia gönderilen komut listesindedir, anlatının satır sayısında değil — C12);
/// hiç uyandırmayan yollar (tik, çıkış, boştaki Stop/Esc) uyandırmaz; yarışlar bekletilen (TCS) sahtelerle: uyku sürerken gelen Sync uykuyu
/// bekler ve TEK uyanmayla gider, iki yol tek uyanmayı paylaşır, uyanma sürerken Stop/çıkış komutsuz geri alır, perf chip'i koşuyla gider
/// (karar 18, 24); düşen uyanma bugünkü TEK sinyali verir ve hiç başlamamış bir işlemi bitmiş saymaz, ölü/erişilemez motorda yeniden denenmez
/// (karar 19, 34; C5, C23); Settings Save'in Sync'i Save'in tek uyanmasıdır (karar 30).</para>
///
/// <para><b>Test yüzeyleri:</b> uyku ve uyanma konağa gitmez — ortak sahte (<see cref="EngineSleepProbe"/>) sayar ve bitmiş, bekletilen ya da
/// düşen bir görev döner. Kabuk gerçek kablajıyla kurulur (<see cref="EngineSleepProbe.ReadyShell"/>) ve motor gerçek tikle uyutulur. Giden
/// komutlar gönderildikleri andaki motor durumuyla toplanır. Dikişin üretim varsayılanını (konağın uyanması) bir test gerçek motorla sınar.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class EngineWakeTests
{
    /// <summary>Gönderilen bir komut ve gönderildiği andaki VM motor durumu.</summary>
    private readonly record struct Sent(IpcCommand Command, EngineState State);

    /// <summary>Giden komutları gönderim anındaki motor durumuyla toplar (gözlemci gönderimden hemen ÖNCE çağrılır).</summary>
    private static List<Sent> Record(RunViewModel vm)
    {
        var sent = new List<Sent>();
        vm.DebugOnCommandSent = command => sent.Add(new Sent(command, vm.EngineState));
        return sent;
    }

    /// <summary>Uyuyan kabuk: hazır kabuk (<see cref="EngineSleepProbe.ReadyShell"/>), beş işsiz dakika ve bir tik — motor uyudu (sahte uyku
    /// hemen biter). <c>Advance</c> testin saatini ileri alır.</summary>
    private static (MainWindow Window, RunViewModel Vm, EngineSleepProbe Probe, List<Sent> Sent, Action<long> Advance) AsleepShell(TempDir dir)
    {
        long now = 1_000;
        var (window, vm, probe) = ReadyShell(dir, () => now);
        now += IdleAfterMs;
        window.OnElapsedTick();
        Assert.Equal(EngineState.Asleep, vm.EngineState); // ön-koşul: motor uyudu
        Assert.Null(vm.EnginePid);
        return (window, vm, probe, Record(vm), ms => now += ms);
    }

    private static int ReadyLines(RunViewModel vm) => MainWindowHost.Occurrences(vm.GetRunDocumentText(), "Engine ready");

    // ---------------------------------------------------------------- tablo: her uyandıran yol önce uyandırır (karar 17, 20, 30)

    /// <summary>Motoru isteyen kullanıcı yolları — her biri üretimdeki girişinden sürülür.</summary>
    public enum WakingPath
    {
        Build, Rebuild, ResolveCycles, MenuClean, TrayHotkey, DeepClean, Optimize,
        SyncButton, WindowReturn, AfterCommit, BranchChange, Configuration, SettingsSave, Checkout, Pull, ProjectLog,
    }

    /// <summary>Yolu üretimdeki girişinden tetikler: komutlar (Build, Rebuild, Resolve cycles, Build menüsünün Clean'i, bakım Clean'i, Optimize,
    /// Sync, pull), tepsi Build kısayolu (pencere tepside), pencereye dönüş ve commit sonrası (kendiliğinden Sync koordinatörü — bu kabukta HEAD
    /// okunamaz: koordinatör güvenli yönde sessiz yeniler), dışarıdan branch değişimi (koordinatörün VM girişi), Debug/Release, Settings Save
    /// ("Stop the engine when idle" KAPATILARAK — karar 30: kapatmanın kendi uyandırması yoktur, Save'in Sync'i tek uyandırmadır), branch chip'i
    /// ve proje logu tıklaması.</summary>
    private static async Task Trigger(WakingPath path, MainWindow window, RunViewModel vm)
    {
        switch (path)
        {
            case WakingPath.Build: await vm.BuildCommand.ExecuteAsync(null); break;
            case WakingPath.Rebuild: await vm.RebuildCommand.ExecuteAsync(null); break;
            case WakingPath.ResolveCycles: await vm.BuildCyclesCommand.ExecuteAsync(null); break;
            case WakingPath.MenuClean: await vm.CleanAllCommand.ExecuteAsync(null); break;
            case WakingPath.TrayHotkey:
                window.SetSurfaceHidden(true);
                window.OnGlobalHotkey(GlobalHotkeyAction.Build);
                break;
            case WakingPath.DeepClean: await vm.CleanCommand.ExecuteAsync(null); break;
            case WakingPath.Optimize: await vm.OptimizeCommand.ExecuteAsync(null); break;
            case WakingPath.SyncButton: await vm.SyncCommand.ExecuteAsync(null); break;
            case WakingPath.WindowReturn: vm.OnWindowActivated(); break;
            case WakingPath.AfterCommit: await vm.AutoSync!.HeadTriggerAsync(HeadMove.Commit); break;
            case WakingPath.BranchChange: await vm.SyncAfterExternalBranchChangeAsync("Switched to feature"); break;
            case WakingPath.Configuration: vm.SetConfiguration(vm.Configuration == "Release" ? "Debug" : "Release"); break;
            case WakingPath.SettingsSave: await vm.ApplySettingsAsync([], vm.RootPath, [], stopEngineWhenIdle: false); break;
            case WakingPath.Checkout: await vm.SelectBranch(new BranchRef("feature/x", "bbbbbbbbbbbb", IsActive: false, IsRemoteTracking: false)); break;
            case WakingPath.Pull: await vm.PullRepositoryCommand.ExecuteAsync(null); break;
            case WakingPath.ProjectLog: _ = vm.LoadProjectLogAsync(MainWindowHost.IdOf("A")); break;
        }
        DispatcherPump.DrainToIdle();
    }

    /// <summary>[karar 17 · 20 · 30 · C12] Uyuyan motoru isteyen HER yol önce uyandırır: tek uyanma, ilk komut ve ardından gelenler motor
    /// hazırken gider (uyurken değil), VM uyanan motorun kimliğini alır. Uyanma sessizdir: konsola yeni bir "Engine ready" satırı düşmez ve
    /// yolun kendi Sync'i dışında Sync gitmez — açılış yolu tekrarlanmaz (iddia gönderilen listede; konsol temizleyen yollarda açılışın satırı
    /// zaten gider). Sync yollarında branch envanteri Sync'in ardından, aynı uyanmayla gider.
    /// <para>KIRMIZI (uyandırmasız kod): uyanma 0, komut uyuyan motora gönderiliyordu (durum <c>Asleep</c>).</para></summary>
    [StaTheory]
    [InlineData(WakingPath.Build, typeof(StartRunCommand), 0, true)]
    [InlineData(WakingPath.Rebuild, typeof(StartRunCommand), 0, true)]
    [InlineData(WakingPath.ResolveCycles, typeof(StartRunCommand), 0, true)]
    [InlineData(WakingPath.MenuClean, typeof(StartRunCommand), 0, true)]
    [InlineData(WakingPath.TrayHotkey, typeof(StartRunCommand), 0, true)]
    [InlineData(WakingPath.DeepClean, typeof(CleanWorkspaceCommand), 0, true)]
    [InlineData(WakingPath.Optimize, typeof(OptimizeWorkspaceCommand), 0, true)]
    [InlineData(WakingPath.SyncButton, typeof(SyncWorkspaceCommand), 1, true)]
    [InlineData(WakingPath.WindowReturn, typeof(SyncWorkspaceCommand), 1, false)]
    [InlineData(WakingPath.AfterCommit, typeof(SyncWorkspaceCommand), 1, false)]
    [InlineData(WakingPath.BranchChange, typeof(SyncWorkspaceCommand), 1, true)]
    [InlineData(WakingPath.Configuration, typeof(SyncWorkspaceCommand), 1, true)]
    [InlineData(WakingPath.SettingsSave, typeof(SyncWorkspaceCommand), 1, false)]
    [InlineData(WakingPath.Checkout, typeof(CheckoutBranchCommand), 0, false)]
    [InlineData(WakingPath.Pull, typeof(PullRepositoryCommand), 0, false)]
    [InlineData(WakingPath.ProjectLog, typeof(GetProjectLogCommand), 0, false)]
    public async Task Every_engine_path_wakes_the_sleeping_engine_first_and_silently(WakingPath path, Type firstCommand, int syncs,
        bool clearsConsole)
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, _) = AsleepShell(dir);
        Assert.Equal(1, ReadyLines(vm)); // ön-koşul: açılışın hazır satırı konsolda

        await Trigger(path, window, vm);

        Assert.Equal(1, probe.WakeCalls);
        Assert.NotEmpty(sent);
        Assert.All(sent, s => Assert.Equal(EngineState.Ready, s.State));
        Assert.IsType(firstCommand, sent[0].Command);
        Assert.Equal(syncs, sent.Count(s => s.Command is SyncWorkspaceCommand));
        if (syncs > 0) Assert.IsType<ListBranchesCommand>(sent[^1].Command);
        Assert.Equal(EngineState.Ready, vm.EngineState);
        Assert.Equal(WokenPid, vm.EnginePid);
        Assert.Equal(EngineVersion, vm.EngineVersion);
        Assert.Equal(clearsConsole ? 0 : 1, ReadyLines(vm));
        GC.KeepAlive(window);
    }

    /// <summary>[karar 17 · pin] Komut göndermeyen yollar motoru hiç uyandırmaz: tik — motor sessizlik bekçisi ping atmaz, boşta kuralı yalnız
    /// uyutur; pencere görünürken de tepsideyken de —, çalışan bir şey yokken Stop/Esc ve tam çıkış (uyuyan konakta gönderecek bir şey yoktur:
    /// çıkış hemen hazırdır). PİN: uyandırmasız kod da yeşildir; bu test ileride bir ping ya da çıkış komutunun uyuyan motoru uyandırmasını
    /// yakalar.</summary>
    [StaFact]
    public void Paths_that_send_nothing_never_wake_the_engine()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, advance) = AsleepShell(dir);
        int exitReady = 0;
        vm.ExitReady += (_, _) => exitReady++;

        for (int i = 0; i < 3; i++)
        {
            advance(RunViewModel.EngineSilenceThresholdMs * 2);
            window.OnElapsedTick();
        }
        window.SetSurfaceHidden(true);
        advance(IdleAfterMs * 3);
        window.OnElapsedTick();
        MainWindowHost.PressEscape(window);
        vm.RequestExit();
        DispatcherPump.DrainToIdle();

        Assert.Equal(1, exitReady);
        Assert.Equal(0, probe.WakeCalls);
        Assert.Empty(sent);
        Assert.Equal(EngineState.Asleep, vm.EngineState);
        GC.KeepAlive(window);
    }

    /// <summary>[spec §6.4 · pin] Uyku "erişilemez" değildir: uyuyan motorda koşu kapısı açıktır — tepsideki Build kısayolu yok sayılmaz, balon
    /// gösterilmez (kısayolun uyandırması yukarıdaki tablodadır) — ve "Restart engine" uyku için gösterilmez; uyanma sürerken de gösterilmez.
    /// Uyku kısmı PİN (uyandırmasız kod da yeşil); uyanma kısmı KIRMIZI idi (uyandırmasız kodda motor hiç <c>Waking</c> olmaz).</summary>
    [StaFact]
    public void A_sleeping_engine_keeps_the_run_gate_open_and_offers_no_restart()
    {
        using var dir = new TempDir();
        var (window, vm, probe, _, _) = AsleepShell(dir);
        DispatcherPump.DrainToIdle();

        Assert.Null(vm.WhyRunCannotStart());
        Assert.True(vm.BuildCommand.CanExecute(null));
        Assert.False(vm.IsEngineUnavailable);
        Assert.Equal(Visibility.Collapsed, window.Shell.Ribbon.RestartEngineAction.Visibility);

        var waking = new TaskCompletionSource<EngineReadyEvent>();
        probe.NextWake = () => waking.Task;
        _ = vm.SyncCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.Equal(EngineState.Waking, vm.EngineState);
        Assert.Equal(Visibility.Collapsed, window.Shell.Ribbon.RestartEngineAction.Visibility);
        waking.SetResult(Woken());
        DispatcherPump.DrainToIdle();
        GC.KeepAlive(window);
    }

    /// <summary>[C21 · pin] Uyanma sessizdir ama gerçek bilgi susturulmaz: uyanan motor açılışta yarım kalmış bir koşu bulduysa (defter
    /// temizlenemedi) kendi hazır olayı <c>InterruptedProjects</c> taşır ve satır olay akışından (<c>OnEvent</c>) bugünkü gibi düşer — hazır
    /// yolunun "Engine ready" satırı ise düşmez. PİN: olay yolu bu görevde değişmedi.</summary>
    [StaFact]
    public async Task A_woken_engine_still_reports_interrupted_projects()
    {
        using var dir = new TempDir();
        var (window, vm, _, _, _) = AsleepShell(dir);
        await vm.SyncCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();

        vm.OnEvent(new EngineReadyEvent(WokenPid, EngineVersion, InterruptedProjects: 2));

        string console = vm.GetRunDocumentText();
        Assert.Contains(Core.Planning.PlanProgressLines.PreviousRunInterrupted(2), console);
        Assert.DoesNotContain("Engine ready", console);
        GC.KeepAlive(window);
    }

    /// <summary>[karar 30 · pin] "Stop the engine when idle"ı kapatmanın kendi uyandırması yoktur: Sync göndermeyen bir Save (burada kök boşaltılır —
    /// workspace kapanır) motoru uyutur bırakır; not yazılır, komut gitmez. Save'in Sync gönderdiği hâlde tek uyandırma o Sync'tir (tablonun
    /// <c>SettingsSave</c> satırı). PİN: uyandırmasız kod da yeşildir; bu test kapatmanın uyandırmaya bağlanmasını yakalar.</summary>
    [StaFact]
    public async Task Switching_the_setting_off_wakes_nothing_by_itself()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, _) = AsleepShell(dir);

        await vm.ApplySettingsAsync([], "", [], stopEngineWhenIdle: false);
        DispatcherPump.DrainToIdle();

        Assert.False(vm.StopEngineWhenIdle);
        Assert.Contains("the engine is no longer stopped when idle", vm.GetRunDocumentText());
        Assert.Equal(0, probe.WakeCalls);
        Assert.Empty(sent);
        Assert.Equal(EngineState.Asleep, vm.EngineState);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- yarışlar (karar 18, 24 · C3, C4) — bekletilen sahteler

    /// <summary>[karar 24 · C3] Uyku sürerken gelen Sync ölmekte olan motora gitmez: kapı uykunun bitmesini bekler, sonra motoru TEK uyanmayla
    /// uyandırır ve Sync (ardından branch envanteri) uyanan motora gider. Uyku bitmeden uyandırma başlamaz.
    /// <para>KIRMIZI (kapısız kod): Sync uyku sürerken gönderiliyordu (konakta "Engine is not running." ya da kaybolan istek).</para></summary>
    [StaFact]
    public async Task A_sync_pressed_during_the_sleep_waits_for_it_and_goes_with_one_wake()
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm, probe) = ReadyShell(dir, () => now);
        var sent = Record(vm);
        var sleeping = new TaskCompletionSource();
        probe.NextSleep = () => sleeping.Task;
        now += IdleAfterMs;
        window.OnElapsedTick();
        Assert.Equal(EngineState.Sleeping, vm.EngineState); // ön-koşul: uyku uçuşta

        var sync = vm.SyncCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.Empty(sent);
        Assert.Equal(0, probe.WakeCalls);
        Assert.True(vm.SyncBusy); // Sync düğmesi istek penceresinde

        sleeping.SetResult();
        DispatcherPump.DrainToIdle();
        await sync;

        Assert.Equal(1, probe.SleepCalls);
        Assert.Equal(1, probe.WakeCalls);
        Assert.Equal([typeof(SyncWorkspaceCommand), typeof(ListBranchesCommand)], sent.Select(s => s.Command.GetType()));
        Assert.All(sent, s => Assert.Equal(EngineState.Ready, s.State));
        GC.KeepAlive(window);
    }

    /// <summary>[karar 24 · C3] İki yol aynı uyanmayı paylaşır: proje logu tıklaması uyanmayı başlatır, uyanma sürerken pencereye dönüşün
    /// Sync'i ona katılır — tek uyanma (tek süreç, tek kimlik), uyanma bitince ikisi de gider. Uyanma konsola satır yazmaz.
    /// <para>KIRMIZI (kapısız kod): uyanma 0, iki komut da uyuyan motora gidiyordu.</para></summary>
    [StaFact]
    public void Two_paths_during_one_wake_share_it()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, _) = AsleepShell(dir);
        var waking = new TaskCompletionSource<EngineReadyEvent>();
        probe.NextWake = () => waking.Task;
        int readyLines = ReadyLines(vm);

        _ = vm.LoadProjectLogAsync(MainWindowHost.IdOf("A")); // uyanmayı başlatır
        vm.OnWindowActivated();                                // uyanma sürerken: katılır
        DispatcherPump.DrainToIdle();
        Assert.Equal(EngineState.Waking, vm.EngineState);
        Assert.Empty(sent);
        Assert.True(vm.SyncBusy); // pencereye dönüşün Sync'i istek penceresinde, kapıda bekliyor

        waking.SetResult(Woken());
        DispatcherPump.DrainToIdle();

        Assert.Equal(1, probe.WakeCalls);
        Assert.Contains(sent, s => s.Command is GetProjectLogCommand);
        Assert.Contains(sent, s => s.Command is SyncWorkspaceCommand);
        Assert.All(sent, s => Assert.Equal(EngineState.Ready, s.State));
        Assert.Equal(WokenPid, vm.EnginePid);
        Assert.Equal(readyLines, ReadyLines(vm));
        GC.KeepAlive(window);
    }

    /// <summary>[karar 18 · 24 · C4] Uyanma koşunun bekleyen-koşu penceresinin İÇİNDEDİR: uyanma sürerken Stop (ya da Esc) komutu değil
    /// isteği geri alır — "Cancelled — build not started", motora ne <c>startRun</c> ne <c>stopRun</c> gider. Uyanma paylaşılan iştir ve biter.
    /// <para>KIRMIZI (kapısız kod): <c>startRun</c> uyuyan motora hemen gidiyor, Stop bekleyen koşu yerine <c>stopRun</c> gönderiyordu.</para></summary>
    [StaFact]
    public async Task Stop_during_a_wake_cancels_the_build_and_sends_nothing()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, _) = AsleepShell(dir);
        var waking = new TaskCompletionSource<EngineReadyEvent>();
        probe.NextWake = () => waking.Task;

        var build = vm.BuildCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.Equal(EngineState.Waking, vm.EngineState);
        Assert.True(vm.IsStarting); // ön-koşul: koşu istendi, komutu kapıda bekliyor

        await vm.StopCommand.ExecuteAsync(null);
        Assert.False(vm.IsStarting);
        Assert.Contains(RunViewModel.RunCancelledLine, vm.GetRunDocumentText());

        waking.SetResult(Woken());
        DispatcherPump.DrainToIdle();
        await build;

        Assert.Empty(sent);
        Assert.Equal(EngineState.Ready, vm.EngineState); // motor uyandı; boşta kalırsa kendi kuralıyla yine uyur
        GC.KeepAlive(window);
    }

    /// <summary>[karar 24 · C4] Uyanma sürerken tam çıkış: bekleyen koşu komutsuz geri alınır ve çıkış hemen hazırdır — motora komut gitmez.
    /// İki bitiş: uyanma tamamlanır (komut yine gitmez) ya da çıkışın dispose'u uyanmanın önüne geçer (<see cref="OperationCanceledException"/>:
    /// satır yok, ölüm yok).
    /// <para>KIRMIZI (kapısız kod): <c>startRun</c> hemen gidiyor, çıkış onu <c>stopRun</c> ile durdurmaya çalışıp koşunun bitişini bekliyordu.</para></summary>
    [StaTheory]
    [InlineData(false)] // uyanma tamamlanır
    [InlineData(true)]  // dispose uyanmanın önüne geçer
    public async Task Exit_during_a_wake_sends_nothing_and_is_ready_at_once(bool disposePreempts)
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, _) = AsleepShell(dir);
        var waking = new TaskCompletionSource<EngineReadyEvent>();
        probe.NextWake = () => waking.Task;
        int exitReady = 0;
        vm.ExitReady += (_, _) => exitReady++;

        var build = vm.BuildCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.Equal(EngineState.Waking, vm.EngineState); // ön-koşul

        vm.RequestExit();
        Assert.Equal(1, exitReady);

        if (disposePreempts) waking.SetCanceled();
        else waking.SetResult(Woken());
        DispatcherPump.DrainToIdle();
        await build;

        Assert.Empty(sent);
        Assert.Null(vm.EngineDiedMessage);
        Assert.DoesNotContain("[error]", vm.GetRunDocumentText());
        GC.KeepAlive(window);
    }

    /// <summary>[karar 18 · 24 · C4 · C13] Perf chip uyuyan motoru uyandırmaz ve uyanık olmayan motora hiç gönderilmez: koşu istendikten sonra
    /// (koşu kilidi açık) chip'e basılırsa not yazılır, <c>setPerfMode</c> GİTMEZ (hata satırı da yok) ve koşunun komutu uyanmadan SONRA
    /// kurulduğu için yeni profili taşır. İki pencere: açılış koreografisi sürerken (motor uyuyor) ve uyanma sürerken.
    /// <para>KIRMIZI (kapısız kod): <c>setPerfMode</c> uyuyan motora gidiyordu; uyanma penceresinde koşu eski profille çoktan gitmişti.</para></summary>
    [StaTheory]
    [InlineData(true)]  // koreografi sürerken
    [InlineData(false)] // uyanma sürerken
    public async Task A_perf_change_before_the_engine_is_awake_goes_with_the_build(bool duringChoreography)
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, _) = AsleepShell(dir);
        var choreography = new TaskCompletionSource();
        var waking = new TaskCompletionSource<EngineReadyEvent>();
        if (duringChoreography) vm.OperationChoreography = _ => choreography.Task;
        else probe.NextWake = () => waking.Task;
        string before = vm.PerfMode;

        var build = vm.BuildCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.True(vm.IsMidRunLocked); // ön-koşul: chip koşuya etki eder
        Assert.Equal(duringChoreography ? EngineState.Asleep : EngineState.Waking, vm.EngineState);

        await vm.CyclePerfAsync();
        string chosen = vm.PerfMode;
        Assert.NotEqual(before, chosen);
        if (duringChoreography) choreography.SetResult();
        else waking.SetResult(Woken());
        DispatcherPump.DrainToIdle();
        await build;

        var start = Assert.IsType<StartRunCommand>(Assert.Single(sent).Command);
        Assert.Equal(chosen, start.PerfMode);
        Assert.Equal(1, probe.WakeCalls);
        Assert.DoesNotContain("[error]", vm.GetRunDocumentText());
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- düşen uyanma (karar 19, 34 · C5, C23)

    /// <summary>[karar 19 · C5 · C23] Uyanma motoru bulamazsa (kurulum) bugünkü erişilemezlik yolu: konsolda TEK satır (<c>OnEngineUnavailable</c>),
    /// şerit kalıcı hata, komutlar kapalı. İkinci sinyal yoktur — ne Sync'in ne branch envanterinin "failed to send" satırı, ne ikinci bir uyandırma
    /// denemesi; Sync'in kendi "gönderim düştü" geri alımı çalışır ve hiç başlamamış Sync bitmiş sayılmaz. Sonraki bir Sync de uyandırmayı yeniden
    /// denemez.
    /// <para>KIRMIZI (kapısız kod): Sync uyuyan motora gönderiliyordu — erişilemezlik hiç bildirilmiyordu.</para></summary>
    [StaFact]
    public async Task A_wake_that_finds_no_engine_takes_todays_unavailable_path_once()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, _) = AsleepShell(dir);
        const string exe = @"C:\app\supervisor\BuildOrchestrator.Supervisor.exe";
        probe.NextWake = () => Task.FromException<EngineReadyEvent>(new EngineUnavailableException(exe, EngineUnavailableReason.NotFound));
        int ended = vm.EndedOperationSerial;

        await vm.SyncCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();

        string console = vm.GetRunDocumentText();
        Assert.Equal(1, MainWindowHost.Occurrences(console, $"[error] engine not found: {exe}"));
        Assert.DoesNotContain("failed to send", console);
        Assert.Empty(sent);
        Assert.Equal(1, probe.WakeCalls);
        Assert.Equal(EngineState.Unavailable, vm.EngineState);
        Assert.True(vm.IsEngineUnavailable);
        Assert.False(vm.SyncBusy);
        Assert.Equal(ended, vm.EndedOperationSerial);

        Assert.False(await vm.SyncSilentlyAsync(SilentSyncReason.Refresh));
        Assert.Equal(1, probe.WakeCalls);
        Assert.Equal(console, vm.GetRunDocumentText());
        GC.KeepAlive(window);
    }

    /// <summary>[karar 19 · 34 · C5 · C23] Uyanmak için doğan motor hazır olamadan ölürse (zaman aşımı, erken ölüm) TEK sinyal ölüm yoludur:
    /// <c>EngineExited</c> → şeritte ölüm satırı ve "Restart engine"; kapı konsola satır yazmaz. Ölüm uyanma sürerken bildirilir — kapıda bekleyen
    /// Sync'e motora hiç gitmeden: motor kaybı onu bitmiş SAYMAZ (sahte işlem sonu yok), geri alımı Sync'in kendisidir; branch envanteri ikinci bir
    /// uyandırma denemez; ölü motor sonraki Sync'te de uyandırılmaz. Ölüm sinyali ile kapının dönüşü arasındaki sıra garanti değildir — iki sıra
    /// aynı sonucu verir.
    /// <para>KIRMIZI (kapısız kod): Sync uyuyan motora hemen gidiyordu; motor kaybı uçuştaki Sync'i bitmiş sayıyordu.</para></summary>
    [StaTheory]
    [InlineData(true)]  // ölüm sinyali kapının dönüşünden önce
    [InlineData(false)] // kapı önce döner, ölüm sinyali sonra
    public async Task A_wake_whose_engine_dies_reports_once_through_the_death_and_ends_nothing(bool deathFirst)
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, _) = AsleepShell(dir);
        var waking = new TaskCompletionSource<EngineReadyEvent>();
        probe.NextWake = () => waking.Task;
        int ended = vm.EndedOperationSerial;

        var sync = vm.SyncCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.Equal(EngineState.Waking, vm.EngineState); // ön-koşul: Sync kapıda, uyanma sürüyor
        string console = vm.GetRunDocumentText();

        if (deathFirst)
        {
            vm.OnEngineExited(-1);
            waking.SetException(new TimeoutException());
        }
        else
        {
            waking.SetException(new TimeoutException());
            DispatcherPump.DrainToIdle();
            vm.OnEngineExited(-1);
        }
        DispatcherPump.DrainToIdle();
        await sync;

        Assert.Equal(console, vm.GetRunDocumentText());
        Assert.Equal("Engine stopped unexpectedly (exit -1)", vm.EngineDiedMessage);
        Assert.Equal(vm.EngineDiedMessage, window.Shell.Ribbon.PhaseText.Text);
        Assert.Equal(Visibility.Visible, window.Shell.Ribbon.RestartEngineAction.Visibility);
        Assert.Equal(EngineState.Dead, vm.EngineState);
        Assert.Empty(sent);
        Assert.Equal(1, probe.WakeCalls);
        Assert.False(vm.SyncBusy);
        Assert.Equal(ended, vm.EndedOperationSerial);

        Assert.False(await vm.SyncSilentlyAsync(SilentSyncReason.Refresh));
        Assert.Equal(1, probe.WakeCalls);
        Assert.Empty(sent);
        Assert.Equal(console, vm.GetRunDocumentText());
        GC.KeepAlive(window);
    }

    /// <summary>[karar 34] Koşu kapıda beklerken (bekleyen-koşu penceresinde uyanma sürüyor) uyanmanın motoru ölürse motor kaybı koşunun açılışını
    /// geri ALMAZ — komutu hiç gitmedi; geri alma TEK yerdedir ve sırası tektir: kapı dönünce koşunun kendi "gönderim düştü" geri alımı (Starting
    /// kapanır, faz tıklamadan önceki faza döner). Koşu bitmiş sayılmaz, konsola satır düşmez.
    /// <para>KIRMIZI (kapısız kod): <c>startRun</c> uyuyan motora gidiyor, motor kaybı koşuyu kendi yolundan bırakıyordu.</para></summary>
    [StaFact]
    public async Task A_build_waiting_for_a_wake_whose_engine_dies_is_rolled_back_once_by_itself()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, _) = AsleepShell(dir);
        var waking = new TaskCompletionSource<EngineReadyEvent>();
        probe.NextWake = () => waking.Task;
        var phaseBefore = vm.Phase;
        int ended = vm.EndedOperationSerial;

        var build = vm.BuildCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.Equal(EngineState.Waking, vm.EngineState); // ön-koşul
        Assert.Equal(AppPhase.Starting, vm.Phase);

        vm.OnEngineExited(-1);
        Assert.True(vm.IsStarting);
        Assert.Equal(AppPhase.Starting, vm.Phase);

        waking.SetException(new InvalidOperationException("Engine died during startup (exit -1)."));
        DispatcherPump.DrainToIdle();
        await build;

        Assert.False(vm.IsStarting);
        Assert.Equal(phaseBefore, vm.Phase);
        Assert.Empty(sent);
        Assert.Equal(ended, vm.EndedOperationSerial);
        Assert.DoesNotContain("[error]", vm.GetRunDocumentText());
        Assert.Equal(EngineState.Dead, vm.EngineState);
        GC.KeepAlive(window);
    }

    /// <summary>[karar 34] Uyku sürerken kapıda bekleyen Sync varken uyku düşerse (ör. öldürme fırlattı) motor kaybı bugünkü ölüm yolundan bildirilir
    /// (şerit ölüm satırı) — ama uyku başladığından beri motora hiçbir şey gitmedi: kayıp bekleyen Sync'i bitmiş SAYMAZ, geri alımı Sync'in
    /// kendisidir. Kaybedilen motor uyandırılmaz, komut gitmez, konsola satır düşmez.
    /// <para>KIRMIZI (kapısız kod): Sync uyku sürerken gönderiliyordu; kayıp onu bitmiş sayıyordu.</para></summary>
    [StaFact]
    public async Task A_failed_sleep_with_a_sync_waiting_for_it_ends_nothing()
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm, probe) = ReadyShell(dir, () => now);
        var sent = Record(vm);
        var sleeping = new TaskCompletionSource();
        probe.NextSleep = () => sleeping.Task;
        now += IdleAfterMs;
        window.OnElapsedTick();
        Assert.Equal(EngineState.Sleeping, vm.EngineState); // ön-koşul
        int ended = vm.EndedOperationSerial;

        var sync = vm.SyncCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.Empty(sent);
        string console = vm.GetRunDocumentText();

        sleeping.SetException(new InvalidOperationException("the kill failed"));
        DispatcherPump.DrainToIdle();
        await sync;

        Assert.Equal("Engine stopped unexpectedly (idle shutdown failed)", vm.EngineDiedMessage);
        Assert.Equal(EngineState.Dead, vm.EngineState);
        Assert.Empty(sent);
        Assert.Equal(0, probe.WakeCalls);
        Assert.False(vm.SyncBusy);
        Assert.Equal(ended, vm.EndedOperationSerial);
        Assert.Equal(console, vm.GetRunDocumentText());
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- gerçek motor (izole önbellek)

    /// <summary>Uyanma dikişinin üretim varsayılanı konağın uyanmasıdır (<see cref="EngineHost.WakeAsync"/>): boşta kuralıyla GERÇEKTEN uyutulan motor,
    /// bir proje logu isteğinin kapısında GERÇEKTEN uyanır — konak artık uyumaz, VM yeni sürecin kimliğini alır ve istek o sürece gider. Sahte
    /// uyanmalı testler kararı ve VM'in durumunu sınar; bu test dikişin gerçekten konağa gittiğini pinler.
    /// <para>KIRMIZI (kapısız kod): istek uyuyan konağa gidiyor ("Engine is not running."), motor uyumaya devam ediyordu.</para></summary>
    [Fact]
    public async Task The_gate_wakes_the_real_engine()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = sandbox.IsolatedEngineHost(TestPaths.WideStartupTimeout);
        var ready = await engine.StartAsync();
        long now = 1_000;
        var vm = RealEngineVm(engine, () => now);
        Task? sleeping = null;
        var sleep = vm.SleepEngine;
        vm.SleepEngine = () => sleeping = sleep(); // yalnız gözlem: görevi testin beklemesi için tutar
        vm.OnEngineReady(ready.EngineVersion, ready.Pid);
        now += IdleAfterMs;
        vm.TickElapsed();
        Assert.NotNull(sleeping);
        await sleeping.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(engine.IsAsleep); // ön-koşul: gerçek motor uyudu

        var requested = new TaskCompletionSource<IpcCommand>(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.DebugOnCommandSent = command => requested.TrySetResult(command);
        _ = vm.LoadProjectLogAsync(@"C:\p\A.csproj");
        var request = await requested.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.IsType<GetProjectLogCommand>(request);
        Assert.False(engine.IsAsleep);
        Assert.NotNull(engine.EnginePid);
        Assert.Equal(engine.EnginePid, vm.EnginePid);
        Assert.NotEqual(ready.Pid, vm.EnginePid);
        Assert.Equal(EngineState.Ready, vm.EngineState);
    }
}
