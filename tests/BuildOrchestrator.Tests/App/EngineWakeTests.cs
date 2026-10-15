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
/// [idle-memory · dalga 3 · spec §6.4 · Task 21] Uyuyan motorun TEK kapısı (<c>RunViewModel.EnsureEngineAwakeAsync</c> — kuralın tek ifadesi
/// onun özetindedir): motoru isteyen her yol göndermeden önce uyuyan motoru uyandırır; tablo tek yerdedir (<see cref="EngineWakeRule"/>,
/// <c>EngineWakeRuleTests</c>). Kapının işi yalnız geçişlerdir — uyku sürüyor, uyuyor, uyanıyor; hazır, başlıyor, ölü ya da erişilemez motorla
/// gelen istek bugünkü gibi gönderilir (ölü/erişilemez motorda bugünkü "failed to send" satırlarıyla — R2).
///
/// <para><b>Bu sınıfın pinledikleri:</b> her uyandıran yol önce uyandırır ve komutu uyanmadan SONRA gönderir; uyanma sessizdir — konsola
/// "Engine ready" yazmaz, açılış Sync'ini göndermez (karar 20; iddia gönderilen komut listesindedir, anlatının satır sayısında değil — C12);
/// hiç uyandırmayan yollar (tik, çıkış, boştaki Stop/Esc) uyandırmaz; ölü ve erişilemez motor bugünkü gönderimini ve satırını korur (R2);
/// yarışlar bekletilen (TCS) sahtelerle: uyku sürerken gelen Sync uykuyu bekler ve TEK uyanmayla gider, iki yol tek uyanmayı paylaşır, uyanma
/// başlarken kapıya yeniden giren yol o uyanmaya katılır, uyanma ya da uyku sürerken Stop/çıkış kapıda bekleyeni komutsuz geri alır ve yeni
/// uyanma başlatmaz, perf chip'i koşuyla gider (karar 18, 24; R3); düşen uyanma bugünkü TEK sinyali verir ve hiç başlamamış bir işlemi
/// bitmiş saymaz, kapıda bekleyen yeniden denemez; ölüm sinyali hiç gelmeyen uyanma bir sonraki tıklamada ölüm olur (karar 19, 34; C5, C23);
/// Settings Save'in Sync'i Save'in tek uyanmasıdır (karar 30).</para>
///
/// <para><b>Test yüzeyleri:</b> uyku ve uyanma konağa gitmez — ortak sahte (<see cref="EngineSleepProbe"/>) sayar ve bitmiş, bekletilen ya da
/// düşen bir görev döner. Kabuk gerçek kablajıyla kurulur (<see cref="EngineSleepProbe.ReadyShell"/>) ve motor gerçek tikle uyutulur. Giden
/// komutlar gönderildikleri andaki motor durumuyla toplanır. Ölü ya da erişilemez motor, fixture'ın hiç başlamamış konağıyla modellenir
/// (gönderim kabulü kaldırılır: yazım konakta düşer). Dikişin üretim varsayılanını (konağın uyanması) bir test gerçek motorla sınar.</para>
///
/// <para><b>Neden <c>[WpfFact]</c>:</b> kabuk testleri üretimin UI bağlamında koşar (<see cref="System.Windows.Threading.DispatcherSynchronizationContext"/>
/// + pompa). Uyanmaya katılan İKİNCİ yolun devamı .NET'te zorla asenkrondur (aynı göreve birden çok await): üretimde UI bağlamına post
/// edilir ve UI thread'inde koşar. <c>[StaFact]</c> bağlam kurmaz — orada o devam thread havuzuna gider, VM'i havuzda koşturur ve bağlı bir
/// düğmenin <c>CanExecute</c>'una dokunduğu an test host'unu düşürür (ölçüldü: iki yolun tek uyanmayı paylaştığı testler).</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF STA kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class EngineWakeTests
{
    /// <summary>[O-4 · round 2] Giden komutlar — her biri gönderim anındaki VM motor durumuyla TEK kayıtta (<see cref="Sent"/>); iki paralel liste yok.
    /// Ortak <see cref="SentCommands"/> yalnız komutu kaydeder (gerçek motorun başka thread'de tamamladığı gönderimler için thread-safe); bu sınıfın
    /// testleri dispatcher thread'inde koşar ve iddiaları komutu durumuyla birlikte okur. <see cref="Commands"/> ve <see cref="States"/> aynı
    /// kayıtların iki görünüşüdür.</summary>
    private sealed class SentLog
    {
        private readonly List<Sent> _sends = [];

        /// <summary>Gönderilen bir komut ve gönderildiği andaki VM motor durumu.</summary>
        public readonly record struct Sent(IpcCommand Command, EngineState State);

        /// <summary>Bir gönderimi kaydeder (gözlemci gönderimden hemen ÖNCE çağrılır).</summary>
        public void Add(IpcCommand command, EngineState state) => _sends.Add(new Sent(command, state));

        /// <summary>Giden komutlar, gönderim sırasıyla.</summary>
        public IEnumerable<IpcCommand> Commands => _sends.Select(s => s.Command);

        /// <summary>Her gönderimin anındaki motor durumu, aynı sırayla.</summary>
        public IEnumerable<EngineState> States => _sends.Select(s => s.State);
    }

    /// <summary>Giden komutları gönderim anındaki motor durumuyla toplar.</summary>
    private static SentLog Record(RunViewModel vm)
    {
        var sent = new SentLog();
        vm.DebugOnCommandSent = command => sent.Add(command, vm.EngineState);
        return sent;
    }

    /// <summary>Uyuyan kabuk: hazır kabuk (<see cref="EngineSleepProbe.ReadyShell"/>), beş işsiz dakika ve bir tik — motor uyudu (sahte uyku
    /// hemen biter). <c>Advance</c> testin saatini ileri alır.</summary>
    private static (MainWindow Window, RunViewModel Vm, EngineSleepProbe Probe, SentLog Sent, Action<long> Advance) AsleepShell(TempDir dir)
    {
        long now = 1_000;
        var (window, vm, probe) = ReadyShell(dir, () => now);
        now += IdleAfterMs;
        window.OnElapsedTick();
        Assert.Equal(EngineState.Asleep, vm.EngineState); // ön-koşul: motor uyudu
        Assert.Null(vm.EnginePid);
        return (window, vm, probe, Record(vm), ms => now += ms);
    }

    /// <summary>[Go5] Uykusu sürerken kabuk: hazır kabuk, bekletilen uyku (TCS), beş işsiz dakika ve bir tik — durum <c>Sleeping</c>.
    /// Uykuyu bitirmek dönen kaynağın işidir.</summary>
    private static (MainWindow Window, RunViewModel Vm, EngineSleepProbe Probe, SentLog Sent, TaskCompletionSource Sleeping) SleepingShell(
        TempDir dir)
    {
        long now = 1_000;
        var (window, vm, probe) = ReadyShell(dir, () => now);
        var sent = Record(vm);
        var sleeping = new TaskCompletionSource();
        probe.NextSleep = () => sleeping.Task;
        now += IdleAfterMs;
        window.OnElapsedTick();
        Assert.Equal(EngineState.Sleeping, vm.EngineState); // ön-koşul: uyku uçuşta
        return (window, vm, probe, sent, sleeping);
    }

    /// <summary>[T21 · fix dalgası] Uyuyan kabuk (<see cref="AsleepShell"/>) + bekletilen uyanma (TCS): uyanmayı ilk isteyen yol onu başlatır,
    /// bitirmek dönen kaynağın işidir. Bekletilen uyanmanın TEK kurulumu.</summary>
    private static (MainWindow Window, RunViewModel Vm, EngineSleepProbe Probe, SentLog Sent, TaskCompletionSource<EngineReadyEvent> Waking)
        AsleepShellWithHeldWake(TempDir dir)
    {
        var (window, vm, probe, sent, _) = AsleepShell(dir);
        var waking = new TaskCompletionSource<EngineReadyEvent>();
        probe.NextWake = () => waking.Task;
        return (window, vm, probe, sent, waking);
    }

    /// <summary>[Go5] Uyanmayı bekleyen koşu: uyuyan kabuk, bekletilen uyanma (TCS) ve Build — koşunun komutu bekleyen-koşu penceresinde kapıda,
    /// uyanma sürüyor. <c>PhaseBefore</c> tıklamadan önceki fazdır (koşunun kendi geri alımı oraya döner).</summary>
    private static (MainWindow Window, RunViewModel Vm, EngineSleepProbe Probe, SentLog Sent, TaskCompletionSource<EngineReadyEvent> Waking,
        Task Build, AppPhase PhaseBefore) BuildWaitingForWake(TempDir dir)
    {
        var (window, vm, probe, sent, waking) = AsleepShellWithHeldWake(dir);
        var phaseBefore = vm.Phase;
        var build = vm.BuildCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.Equal(EngineState.Waking, vm.EngineState); // ön-koşul: koşu kapıda, uyanma sürüyor
        Assert.True(vm.IsStarting);
        return (window, vm, probe, sent, waking, build, phaseBefore);
    }

    /// <summary>Ölü ya da erişilemez motorun gönderimi: gönderim kabulü kaldırılır — komut fixture'ın hiç başlamamış konağına gider ve orada
    /// bugünkü gibi düşer ("Engine is not running.").</summary>
    private static void SendsReachTheDeadEngine(RunViewModel vm) => vm.DebugSendOverride = null;

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
    [WpfTheory]
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
        Assert.NotEmpty(sent.Commands);
        Assert.All(sent.States, s => Assert.Equal(EngineState.Ready, s));
        Assert.IsType(firstCommand, sent.Commands.First());
        Assert.Equal(syncs, sent.Commands.Count(c => c is SyncWorkspaceCommand));
        if (syncs > 0) Assert.IsType<ListBranchesCommand>(sent.Commands.Last());
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
    [WpfFact]
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
        Assert.Empty(sent.Commands);
        Assert.Equal(EngineState.Asleep, vm.EngineState);
        GC.KeepAlive(window);
    }

    /// <summary>[spec §6.4] Uyku "erişilemez" değildir: uyuyan motorda koşu kapısı açıktır — tepsideki Build kısayolu yok sayılmaz, balon
    /// gösterilmez (kısayolun uyandırması yukarıdaki tablodadır) — ve "Restart engine" uyku için gösterilmez; uyanma sürerken de gösterilmez.
    /// Uyku kısmının iddiaları PİN (uyandırmasız kod da yeşil). [Fix round 1 · Mi6] Uyanma kısmında kırmızı veren uyanmanın ön-koşuluydu
    /// (uyandırmasız kodda motor hiç <c>Waking</c> olmaz); "uyanırken Restart gizli" iddiasının kendisi PİN'dir — Restart'ı yalnız ölüm ya da
    /// sessizlik metni açar ve uyanma ikisini de yazmaz.</summary>
    [WpfFact]
    public void A_sleeping_engine_keeps_the_run_gate_open_and_offers_no_restart()
    {
        using var dir = new TempDir();
        var (window, vm, _, _, waking) = AsleepShellWithHeldWake(dir);
        DispatcherPump.DrainToIdle();

        Assert.Null(vm.WhyRunCannotStart());
        Assert.True(vm.BuildCommand.CanExecute(null));
        Assert.False(vm.IsEngineUnavailable);
        Assert.Equal(Visibility.Collapsed, window.Shell.Ribbon.RestartEngineAction.Visibility);

        _ = vm.SyncCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.Equal(EngineState.Waking, vm.EngineState); // ön-koşul (kırmızının yeri)
        Assert.Equal(Visibility.Collapsed, window.Shell.Ribbon.RestartEngineAction.Visibility); // pin
        waking.SetResult(Woken());
        DispatcherPump.DrainToIdle();
        GC.KeepAlive(window);
    }

    /// <summary>[C21 · pin] Uyanma sessizdir ama gerçek bilgi susturulmaz: uyanan motor açılışta yarım kalmış bir koşu bulduysa (defter
    /// temizlenemedi) kendi hazır olayı <c>InterruptedProjects</c> taşır ve satır olay akışından (<c>OnEvent</c>) bugünkü gibi düşer — hazır
    /// yolunun "Engine ready" satırı ise düşmez. PİN: olay yolu bu görevde değişmedi.</summary>
    [WpfFact]
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
    [WpfFact]
    public async Task Switching_the_setting_off_wakes_nothing_by_itself()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, _) = AsleepShell(dir);

        await vm.ApplySettingsAsync([], "", [], stopEngineWhenIdle: false);
        DispatcherPump.DrainToIdle();

        Assert.False(vm.StopEngineWhenIdle);
        Assert.Contains("the engine is no longer stopped when idle", vm.GetRunDocumentText());
        Assert.Equal(0, probe.WakeCalls);
        Assert.Empty(sent.Commands);
        Assert.Equal(EngineState.Asleep, vm.EngineState);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- önceki koşunun proje logu (karar 37 · C2)

    /// <summary>[karar 37 · C2] Uyanmış motor yeni bir süreçtir ve önceki koşuyu bilmez; App bilir. Koşu biter, motor beş işsiz dakikada uyur,
    /// kullanıcı o koşunun bir satırına tıklar: istek motoru uyandırır ve App'in gördüğü son koşunun klasörünü taşır
    /// (<c>runStarted.LogDirectory</c> — satırların anlattığı koşu); motor logu oradan okur (Supervisor tarafı:
    /// <c>ProjectLogStreamTests.A_fresh_engine_reads_an_earlier_runs_project_log_from_the_folder_the_request_names</c>).
    /// <para>KIRMIZI: istek klasör taşımıyordu (<c>RunDirectory</c> null) — uyanan motor "log yok" cevabı veriyordu.</para></summary>
    [WpfFact]
    public void A_project_log_click_after_a_wake_names_the_previous_runs_folder()
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm, probe) = ReadyShell(dir, () => now);
        using var run = new SpoolDir(vm.Narrative);
        MainWindowHost.StartBuildWithLogDirectory(vm, run.Path, "A");
        MainWindowHost.FinishBuild(vm, "A");
        DispatcherPump.DrainToIdle();
        now += IdleAfterMs;
        window.OnElapsedTick();
        Assert.Equal(EngineState.Asleep, vm.EngineState); // ön-koşul: koşu bitti, motor uyudu
        var sent = Record(vm);

        _ = vm.LoadProjectLogAsync(MainWindowHost.IdOf("A"));
        DispatcherPump.DrainToIdle();

        Assert.Equal(1, probe.WakeCalls);
        var request = Assert.IsType<GetProjectLogCommand>(Assert.Single(sent.Commands));
        Assert.Equal(run.Path, request.RunDirectory);
        Assert.Equal(EngineState.Ready, Assert.Single(sent.States));
        GC.KeepAlive(window);
    }

    /// <summary>[karar 37 · PİN] App henüz bir koşu başladığını görmediyse (uygulama yeni açıldı) istek klasör taşımaz — motor bugünkü gibi
    /// yalnız kendi koşularına bakar. PİN: klasörü hiç göndermeyen bugünkü kod da yeşildir; bu test alanın bir koşu olmadan dolmasını yakalar.</summary>
    [WpfFact]
    public void A_project_log_click_before_any_run_names_no_folder()
    {
        using var dir = new TempDir();
        var (window, vm, _, sent, _) = AsleepShell(dir);

        _ = vm.LoadProjectLogAsync(MainWindowHost.IdOf("A"));
        DispatcherPump.DrainToIdle();

        Assert.Null(Assert.IsType<GetProjectLogCommand>(Assert.Single(sent.Commands)).RunDirectory);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- ölü ve erişilemez motor: bugünkü gönderim (R2)

    /// <summary>[Fix round 1 · I1 · R2] Ölü motor kapının işi değildir: uyandırma yok, gönderim bugünkü gibi gider ve bugünkü satırlarını yazar. Hazır
    /// motor kendiliğinden ölür (şerit ölüm satırı + "Restart engine"); kullanıcı Sync'e basar: Sync ve ardından branch envanteri ölü motora
    /// gönderilir, ikisi de bugünkü "[error] failed to send …" satırını yazar (C23'ün iki satırı bugünkü davranıştır — yalnız düşen bir uyanmadan
    /// sonra kapıda bekleyen tek satır yazmaz). Sync'in kendi geri alımı çalışır, uyandırma denenmez.
    /// <para>KIRMIZI (e843d516): kapı ölü motoru reddediyordu — iki satır da yoktu, tıklama sessiz kalıyordu.</para></summary>
    [WpfFact]
    public async Task A_dead_engine_still_takes_todays_send_and_writes_its_two_lines()
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm, probe) = ReadyShell(dir, () => now);
        vm.OnEngineExited(1);
        SendsReachTheDeadEngine(vm);
        var sent = Record(vm);

        await vm.SyncCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();

        string console = vm.GetRunDocumentText();
        Assert.Equal(1, MainWindowHost.Occurrences(console, "[error] failed to send sync: "));
        Assert.Equal(1, MainWindowHost.Occurrences(console, "[error] failed to send listBranches: "));
        Assert.Equal([typeof(SyncWorkspaceCommand), typeof(ListBranchesCommand)], sent.Commands.Select(c => c.GetType()));
        Assert.All(sent.States, s => Assert.Equal(EngineState.Dead, s));
        Assert.Equal(0, probe.WakeCalls);
        Assert.False(vm.SyncBusy);
        GC.KeepAlive(window);
    }

    /// <summary>[Fix round 1 · I1 · R2] Erişilemez motora proje logu tıklaması bugünkü gibi gider ve bugünkü satırını yazar: "[error] could not load
    /// project log: …"; yükleme bugünkü düşen gönderim geri alımıyla biter (awaiter serbest, cevabı beklenmez). Uyandırma yok.
    /// <para>KIRMIZI (e843d516): kapı erişilemez motoru reddediyordu — satır yoktu, istek hiç gönderilmiyordu.</para></summary>
    [WpfFact]
    public async Task An_unavailable_engine_still_answers_a_project_log_click_with_todays_line()
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm, probe) = ReadyShell(dir, () => now);
        vm.OnEngineUnavailable(@"C:\app\supervisor\BuildOrchestrator.Supervisor.exe");
        SendsReachTheDeadEngine(vm);
        var sent = Record(vm);

        await vm.LoadProjectLogAsync(MainWindowHost.IdOf("A"));

        Assert.Equal(1, MainWindowHost.Occurrences(vm.GetRunDocumentText(), "[error] could not load project log: "));
        Assert.IsType<GetProjectLogCommand>(Assert.Single(sent.Commands));
        Assert.Equal(0, probe.WakeCalls);
        Assert.False(vm.HasPendingLogLoad);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- yarışlar (karar 18, 24 · R3 · C3, C4) — bekletilen sahteler

    /// <summary>[karar 24 · C3] Uyku sürerken gelen Sync ölmekte olan motora gitmez: kapı uykunun bitmesini bekler, sonra motoru TEK uyanmayla
    /// uyandırır ve Sync (ardından branch envanteri) uyanan motora gider. Uyku bitmeden uyandırma başlamaz.
    /// <para>KIRMIZI (kapısız kod): Sync uyku sürerken gönderiliyordu (konakta "Engine is not running." ya da kaybolan istek).</para></summary>
    [WpfFact]
    public async Task A_sync_pressed_during_the_sleep_waits_for_it_and_goes_with_one_wake()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, sleeping) = SleepingShell(dir);

        var sync = vm.SyncCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.Empty(sent.Commands);
        Assert.Equal(0, probe.WakeCalls);
        Assert.True(vm.SyncBusy); // Sync düğmesi istek penceresinde

        sleeping.SetResult();
        DispatcherPump.DrainToIdle();
        await sync;

        Assert.Equal(1, probe.SleepCalls);
        Assert.Equal(1, probe.WakeCalls);
        Assert.Equal([typeof(SyncWorkspaceCommand), typeof(ListBranchesCommand)], sent.Commands.Select(c => c.GetType()));
        Assert.All(sent.States, s => Assert.Equal(EngineState.Ready, s));
        GC.KeepAlive(window);
    }

    /// <summary>[karar 24 · C3] İki yol aynı uyanmayı paylaşır: proje logu tıklaması uyanmayı başlatır, uyanma sürerken pencereye dönüşün
    /// Sync'i ona katılır — tek uyanma (tek süreç, tek kimlik), uyanma bitince ikisi de gider. Uyanma konsola satır yazmaz.
    /// <para>KIRMIZI (kapısız kod): uyanma 0, iki komut da uyuyan motora gidiyordu.</para></summary>
    [WpfFact]
    public void Two_paths_during_one_wake_share_it()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, waking) = AsleepShellWithHeldWake(dir);
        int readyLines = ReadyLines(vm);

        _ = vm.LoadProjectLogAsync(MainWindowHost.IdOf("A")); // uyanmayı başlatır
        vm.OnWindowActivated();                                // uyanma sürerken: katılır
        DispatcherPump.DrainToIdle();
        Assert.Equal(EngineState.Waking, vm.EngineState);
        Assert.Empty(sent.Commands);
        Assert.True(vm.SyncBusy); // pencereye dönüşün Sync'i istek penceresinde, kapıda bekliyor

        waking.SetResult(Woken());
        DispatcherPump.DrainToIdle();

        Assert.Equal(1, probe.WakeCalls);
        Assert.Contains(sent.Commands, c => c is GetProjectLogCommand);
        Assert.Contains(sent.Commands, c => c is SyncWorkspaceCommand);
        Assert.All(sent.States, s => Assert.Equal(EngineState.Ready, s));
        Assert.Equal(WokenPid, vm.EnginePid);
        Assert.Equal(readyLines, ReadyLines(vm));
        GC.KeepAlive(window);
    }

    /// <summary>[Fix round 1 · Mi4 kancası] Uyanma sürerken yerini yenisi alan proje logu yüklemesi (ikinci tıklama) uyanmadan sonra GÖNDERİLMEZ:
    /// kapı her bekleyişin ardından isteğin hâlâ istenip istenmediğini sorar — yükleme için koşul, yerini yenisinin almamış olmasıdır. Eskisinin
    /// dikişi bugünkü gibi ikinci tıklamada biter (<c>CompletePendingLoad</c>); çağrısı kapıda beklediği için uyanmanın sonunda, göndermeden
    /// döner. Uyanmadan sonra yalnız yeni isteğin kimliği gider, tek uyanma.
    /// <para>KIRMIZI (mutasyon: koşul kaldırıldı — e843d516'nın davranışı): uyanmadan sonra iki istek de gidiyordu, cevabı hiç beklenmeyen eski
    /// istek dahil.</para></summary>
    [WpfFact]
    public void A_project_log_replaced_while_the_wake_runs_is_not_sent_after_it()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, waking) = AsleepShellWithHeldWake(dir);

        var first = vm.LoadProjectLogAsync(MainWindowHost.IdOf("A"));  // uyanmayı başlatır
        _ = vm.LoadProjectLogAsync(MainWindowHost.IdOf("A"));          // uyanma sürerken ikinci tıklama: yerini alır
        DispatcherPump.DrainToIdle();
        Assert.Equal(EngineState.Waking, vm.EngineState); // ön-koşul: iki yükleme de kapıda, uyanma sürüyor
        Assert.Empty(sent.Commands);

        waking.SetResult(Woken());
        DispatcherPump.DrainToIdle();

        Assert.True(first.IsCompleted); // eski yüklemenin çağrısı döndü — asılı kalmadı
        var request = Assert.IsType<GetProjectLogCommand>(Assert.Single(sent.Commands));
        Assert.Equal(2, request.RequestId); // ikinci tıklamanın kimliği (kabukta bundan önce yükleme yok)
        Assert.Equal(1, probe.WakeCalls);
        GC.KeepAlive(window);
    }

    /// <summary>[Fix round 1 · Mi5] Uyanma başlarken — durum <c>Waking</c> olarak yayımlanırken — kapıya yeniden giren bir yol (burada uyanmanın
    /// dikişi içinden gelen proje logu tıklaması; durumun gözlemlenebilir olduğu günün senkron bir tepkisini temsil eder) O uyanmaya katılır:
    /// uyanmanın tutamağı durumdan ÖNCE yazılır, yeniden giren yol eski (bitmiş) bir tutamağı göremez. Uyanma bitince iki komut da gider, tek
    /// uyanma.
    /// <para>KIRMIZI (e843d516): durum tutamaktan önce yayımlanıyordu — yeniden giren yol eski, "uyanamadı" diyen tutamağı buldu ve isteği hiç
    /// gönderilmedi (o tutamak başarılı bir uyanmanınki olsaydı kapının döngüsü sonsuza dek dönerdi).</para></summary>
    [WpfFact]
    public void A_command_reentering_the_gate_as_the_wake_starts_joins_that_wake()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, _) = AsleepShell(dir);
        var waking = new TaskCompletionSource<EngineReadyEvent>();
        Task? reentrant = null;
        probe.NextWake = () =>
        {
            reentrant ??= vm.LoadProjectLogAsync(MainWindowHost.IdOf("A")); // uyanma başlarken kapıya yeniden giren yol
            return waking.Task;
        };

        _ = vm.SyncCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.NotNull(reentrant); // ön-koşul: yol kapıya uyanmanın içinden girdi
        Assert.Empty(sent.Commands);

        waking.SetResult(Woken());
        DispatcherPump.DrainToIdle();

        Assert.Equal(1, probe.WakeCalls);
        Assert.Contains(sent.Commands, c => c is GetProjectLogCommand);
        Assert.Contains(sent.Commands, c => c is SyncWorkspaceCommand);
        Assert.All(sent.States, s => Assert.Equal(EngineState.Ready, s));
        Assert.Equal(EngineState.Ready, vm.EngineState);
        GC.KeepAlive(window);
    }

    /// <summary>[karar 18 · 24 · C4] Uyanma koşunun bekleyen-koşu penceresinin İÇİNDEDİR: uyanma sürerken Stop (ya da Esc) komutu değil
    /// isteği geri alır — "Cancelled — build not started", motora ne <c>startRun</c> ne <c>stopRun</c> gider. Uyanma paylaşılan iştir ve biter.
    /// <para>KIRMIZI (kapısız kod): <c>startRun</c> uyuyan motora hemen gidiyor, Stop bekleyen koşu yerine <c>stopRun</c> gönderiyordu.</para></summary>
    [WpfFact]
    public async Task Stop_during_a_wake_cancels_the_build_and_sends_nothing()
    {
        using var dir = new TempDir();
        var (window, vm, _, sent, waking, build, _) = BuildWaitingForWake(dir);

        await vm.StopCommand.ExecuteAsync(null);
        Assert.False(vm.IsStarting);
        Assert.Contains(RunViewModel.RunCancelledLine, vm.GetRunDocumentText());

        waking.SetResult(Woken());
        DispatcherPump.DrainToIdle();
        await build;

        Assert.Empty(sent.Commands);
        Assert.Equal(EngineState.Ready, vm.EngineState); // motor uyandı; boşta kalırsa kendi kuralıyla yine uyur
        GC.KeepAlive(window);
    }

    /// <summary>[Fix round 1 · Mi4] Uyku sürerken kapıda bekleyen koşu geri alınırsa (Stop/Esc — bekleyen-koşu penceresi) uyku bitince uyanma
    /// BAŞLAMAZ: kapı uyanmayı başlatmadan önce isteğin hâlâ istenip istenmediğini sorar. Komut gitmez, motor uyumaya devam eder.
    /// <para>KIRMIZI (e843d516): uyku bitince kapı geri alınmış koşu için motoru yine de uyandırıyordu (boşa bir süreç başlatma).</para></summary>
    [WpfFact]
    public async Task A_build_cancelled_while_the_gate_waits_for_the_sleep_starts_no_wake()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, sleeping) = SleepingShell(dir);
        var build = vm.BuildCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.True(vm.IsStarting); // ön-koşul: koşunun komutu kapıda, uykunun bitmesini bekliyor

        await vm.StopCommand.ExecuteAsync(null);
        Assert.Contains(RunViewModel.RunCancelledLine, vm.GetRunDocumentText());
        sleeping.SetResult();
        DispatcherPump.DrainToIdle();
        await build;

        Assert.Equal(0, probe.WakeCalls);
        Assert.Empty(sent.Commands);
        Assert.Equal(EngineState.Asleep, vm.EngineState);
        GC.KeepAlive(window);
    }

    /// <summary>[karar 24 · C4] Uyanma sürerken tam çıkış: bekleyen koşu komutsuz geri alınır ve çıkış hemen hazırdır — motora komut gitmez.
    /// İki bitiş: uyanma tamamlanır (komut yine gitmez) ya da çıkışın dispose'u uyanmanın önüne geçer (<see cref="OperationCanceledException"/>:
    /// satır yok, ölüm yok).
    /// <para>KIRMIZI (kapısız kod): <c>startRun</c> hemen gidiyor, çıkış onu <c>stopRun</c> ile durdurmaya çalışıp koşunun bitişini bekliyordu.</para></summary>
    [WpfTheory]
    [InlineData(false)] // uyanma tamamlanır
    [InlineData(true)]  // dispose uyanmanın önüne geçer
    public async Task Exit_during_a_wake_sends_nothing_and_is_ready_at_once(bool disposePreempts)
    {
        using var dir = new TempDir();
        var (window, vm, _, sent, waking, build, _) = BuildWaitingForWake(dir);
        int exitReady = 0;
        vm.ExitReady += (_, _) => exitReady++;

        vm.RequestExit();
        Assert.Equal(1, exitReady);

        if (disposePreempts) waking.SetCanceled();
        else waking.SetResult(Woken());
        DispatcherPump.DrainToIdle();
        await build;

        Assert.Empty(sent.Commands);
        Assert.Null(vm.EngineDiedMessage);
        Assert.DoesNotContain("[error]", vm.GetRunDocumentText());
        GC.KeepAlive(window);
    }

    /// <summary>[Fix round 1 · I2 · R3] Uyanma sürerken çıkış istenirse kapıda bekleyen hiçbir şey gönderilmez: Sync'in kendi geri alımı çalışır
    /// (ne Sync ne branch envanteri gider, satır yok), uçuşta iş kalmaz ve çıkış o anda — bir kez — hazır olur.
    /// <para>KIRMIZI (e843d516): uyanma bitince Sync ve envanter uyanan motora gidiyor, çıkış o Sync'in bitişini bekliyordu.</para></summary>
    [WpfFact]
    public async Task An_exit_requested_during_a_wake_withdraws_the_sync_waiting_in_the_gate()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, waking) = AsleepShellWithHeldWake(dir);
        int exitReady = 0;
        vm.ExitReady += (_, _) => exitReady++;
        var sync = vm.SyncCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.Equal(EngineState.Waking, vm.EngineState); // ön-koşul: Sync kapıda

        vm.RequestExit();
        Assert.Equal(0, exitReady); // çıkış kapıda bekleyen Sync'i bekliyor (istek uçuşta sayılır)
        waking.SetResult(Woken());
        DispatcherPump.DrainToIdle();
        await sync;

        Assert.Empty(sent.Commands);
        Assert.Equal(1, exitReady);
        Assert.False(vm.SyncBusy);
        Assert.DoesNotContain("[error]", vm.GetRunDocumentText());
        GC.KeepAlive(window);
    }

    /// <summary>[Fix round 1 · I2 · R3 · Mi4] Uyku sürerken çıkış istenirse uyku bitince uyanma BAŞLAMAZ: kapı uyanmayı başlatmadan önce çıkışı
    /// sorar. Kapıda bekleyen Sync gönderilmez, motor uyur, çıkış hazır olur.
    /// <para>KIRMIZI (e843d516): uyku bitince kapı motoru çıkışın ortasında uyandırıyordu.</para></summary>
    [WpfFact]
    public async Task An_exit_requested_during_a_sleep_starts_no_wake()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, sleeping) = SleepingShell(dir);
        int exitReady = 0;
        vm.ExitReady += (_, _) => exitReady++;
        var sync = vm.SyncCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();

        vm.RequestExit();
        sleeping.SetResult();
        DispatcherPump.DrainToIdle();
        await sync;

        Assert.Equal(0, probe.WakeCalls);
        Assert.Empty(sent.Commands);
        Assert.Equal(EngineState.Asleep, vm.EngineState);
        Assert.Equal(1, exitReady);
        GC.KeepAlive(window);
    }

    /// <summary>[Fix round 1 · I2 · R3] Kapıda yalnız bir proje logu yüklemesi beklerken çıkış istenirse: yükleme çıkışın beklediği bir iş değildir —
    /// çıkış istendiği anda hazırdır (bekleyiş açılmaz) — ve uyanma bitince istek GÖNDERİLMEZ, yükleme geri alınır. Kapının sorduğu bayrak bu
    /// yüzden "çıkış istendi"dir, "çıkış bekliyor" değil: ikincisi bu yolda hiç açılmaz.
    /// <para>KIRMIZI (e843d516): uyanma bitince proje logu isteği kapanan uygulamanın motoruna gidiyordu. Kapı "çıkış bekliyor"u sorsaydı da
    /// kırmızıydı (bekleyiş açılmadığı için).</para></summary>
    [WpfFact]
    public void An_exit_requested_while_only_a_project_log_waits_withdraws_it()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, waking) = AsleepShellWithHeldWake(dir);
        int exitReady = 0;
        vm.ExitReady += (_, _) => exitReady++;
        var load = vm.LoadProjectLogAsync(MainWindowHost.IdOf("A"));
        DispatcherPump.DrainToIdle();
        Assert.Equal(EngineState.Waking, vm.EngineState); // ön-koşul: yükleme kapıda

        vm.RequestExit();
        Assert.Equal(1, exitReady); // yükleme çıkışın beklediği iş değildir
        Assert.False(vm.ExitPending);
        waking.SetResult(Woken());
        DispatcherPump.DrainToIdle();

        Assert.Empty(sent.Commands);
        Assert.True(load.IsCompleted);
        Assert.False(vm.HasPendingLogLoad);
        GC.KeepAlive(window);
    }

    /// <summary>[karar 18 · 24 · C4 · C13] Perf chip uyuyan motoru uyandırmaz ve uyanık olmayan motora hiç gönderilmez: koşu istendikten sonra
    /// (koşu kilidi açık) chip'e basılırsa not yazılır, <c>setPerfMode</c> GİTMEZ (hata satırı da yok) ve koşunun komutu uyanmadan SONRA
    /// kurulduğu için yeni profili taşır. İki pencere: açılış koreografisi sürerken (tıklamada başlayan uyanma da sürüyor) ve koreografi
    /// bittikten sonra uyanma sürerken.
    /// <para><b>[DEĞİŞEN KURULUM — uyanma gecikmesi, kontrolcü kararı]</b> Eskiden koreografi satırında motor koreografi boyunca UYUYORDU — koşunun
    /// uyanması koreografiden sonra başlıyordu. Uyanma artık tıklamada başlar ve koreografiyle örtüşür; anlık biten bir uyanma motoru koreografi
    /// sırasında hazır ederdi ve o anki chip bugünkü gibi hazır motora giderdi. Pencereyi "motor henüz uyanık değil"de tutmak için uyanma da
    /// bekletilir. İddia aynı.</para>
    /// <para>KIRMIZI (kapısız kod): <c>setPerfMode</c> uyuyan motora gidiyordu; uyanma penceresinde koşu eski profille çoktan gitmişti.</para></summary>
    [WpfTheory]
    [InlineData(true)]  // koreografi sürerken (uyanma da sürüyor)
    [InlineData(false)] // koreografiden sonra, uyanma sürerken
    public async Task A_perf_change_before_the_engine_is_awake_goes_with_the_build(bool duringChoreography)
    {
        using var dir = new TempDir();
        MainWindow window;
        RunViewModel vm;
        EngineSleepProbe probe;
        SentLog sent;
        Task build;
        Action release;
        if (duringChoreography)
        {
            TaskCompletionSource<EngineReadyEvent> waking;
            (window, vm, probe, sent, waking) = AsleepShellWithHeldWake(dir);
            var choreography = new TaskCompletionSource();
            vm.OperationChoreography = _ => choreography.Task;
            build = vm.BuildCommand.ExecuteAsync(null);
            DispatcherPump.DrainToIdle();
            Assert.Equal(EngineState.Waking, vm.EngineState); // ön-koşul: koreografi sürüyor, tıklamada başlayan uyanma da
            release = () =>
            {
                choreography.SetResult();
                waking.SetResult(Woken());
            };
        }
        else
        {
            TaskCompletionSource<EngineReadyEvent> waking;
            (window, vm, probe, sent, waking, build, _) = BuildWaitingForWake(dir);
            release = () => waking.SetResult(Woken());
        }
        Assert.True(vm.IsMidRunLocked); // ön-koşul: chip koşuya etki eder
        string before = vm.PerfMode;

        await vm.CyclePerfAsync();
        string chosen = vm.PerfMode;
        Assert.NotEqual(before, chosen);
        release();
        DispatcherPump.DrainToIdle();
        await build;

        var start = Assert.IsType<StartRunCommand>(Assert.Single(sent.Commands));
        Assert.Equal(chosen, start.PerfMode);
        Assert.Equal(1, probe.WakeCalls);
        Assert.DoesNotContain("[error]", vm.GetRunDocumentText());
        GC.KeepAlive(window);
    }

    /// <summary>[uyanma gecikmesi · round 2 · PİN] Tıklamada başlayan uyanma koreografi sürerken bittiyse motor hazırdır: o anda basılan perf chip'i
    /// bugünkü gibi hazır motora gider (<c>setPerfMode</c>) ve koşu da yeni profille gider. Uyku döngüsü bittiği için kapı chip'i tutmaz — kontrolcü
    /// kararıyla kabul edilen davranış. PİN: kırmızısı gösterilemez; ileride hazır motora giden bir chip'in kapıda tutulmasını yakalar.</summary>
    [WpfFact]
    public async Task A_perf_change_during_the_choreography_after_the_wake_goes_to_the_ready_engine()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, _) = AsleepShell(dir); // sahte uyanma anında biter
        var choreography = new TaskCompletionSource();
        vm.OperationChoreography = _ => choreography.Task;
        var build = vm.BuildCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.Equal(EngineState.Ready, vm.EngineState); // ön-koşul: uyanma tıklamada bitti, koreografi sürüyor
        Assert.True(vm.IsMidRunLocked);
        string before = vm.PerfMode;

        await vm.CyclePerfAsync();
        string chosen = vm.PerfMode;
        Assert.NotEqual(before, chosen);
        choreography.SetResult();
        DispatcherPump.DrainToIdle();
        await build;

        Assert.Equal([typeof(SetPerfModeCommand), typeof(StartRunCommand)], sent.Commands.Select(c => c.GetType()));
        Assert.All(sent.States, s => Assert.Equal(EngineState.Ready, s));
        Assert.Equal(chosen, Assert.IsType<StartRunCommand>(sent.Commands.Last()).PerfMode);
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
    [WpfFact]
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
        Assert.Empty(sent.Commands);
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
    /// Sync'e motora hiç gitmeden: motor kaybı onu bitmiş SAYMAZ (sahte işlem sonu yok), geri alımı Sync'in kendisidir; branch envanteri ne
    /// gönderilir ne ikinci bir uyandırma dener. Ölüm sinyali ile kapının dönüşü arasındaki sıra garanti değildir — iki sıra aynı sonucu verir.
    /// Ölü motor sonraki Sync'te uyandırılmaz; o Sync bugünkü gibi gönderilir.
    /// <para><b>[DEĞİŞEN KURAL — Fix round 1 · R2]</b> ESKİ İDDİA: ölü motorda sonraki Sync hiçbir şey göndermez (kapı reddeder, konsola satır
    /// düşmez). GEREKÇE (kontrolcü kararı R2, "mevcut sistemi bozma"): ölü ve erişilemez motor kapının işi değildir — uyandırma yoktur ama
    /// gönderim bugünkü yolundan gider ve bugünkü satırını yazar; red yalnız kapıda bir geçişi BEKLEMİŞ istek içindir (o geçişin kendi tek sinyali
    /// vardır). Bu kabukta gönderim kabul edildiği için sonraki Sync gönderilir; ölü motorun gerçek gönderiminin satırları
    /// <see cref="A_dead_engine_still_takes_todays_send_and_writes_its_two_lines"/>'tadır.</para>
    /// <para>KIRMIZI (kapısız kod): Sync uyuyan motora hemen gidiyordu; motor kaybı uçuştaki Sync'i bitmiş sayıyordu.</para></summary>
    [WpfTheory]
    [InlineData(true)]  // ölüm sinyali kapının dönüşünden önce
    [InlineData(false)] // kapı önce döner, ölüm sinyali sonra
    public async Task A_wake_whose_engine_dies_reports_once_through_the_death_and_ends_nothing(bool deathFirst)
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, waking) = AsleepShellWithHeldWake(dir);
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
        Assert.Empty(sent.Commands);
        Assert.Equal(1, probe.WakeCalls);
        Assert.False(vm.SyncBusy);
        Assert.Equal(ended, vm.EndedOperationSerial);

        Assert.True(await vm.SyncSilentlyAsync(SilentSyncReason.Refresh));
        Assert.Equal(1, probe.WakeCalls);
        Assert.Equal([typeof(SyncWorkspaceCommand), typeof(ListBranchesCommand)], sent.Commands.Select(c => c.GetType()));
        Assert.All(sent.States, s => Assert.Equal(EngineState.Dead, s));
        GC.KeepAlive(window);
    }

    /// <summary>[Fix round 1 · Mi1] Ölüm sinyali hiç gelmeyen düşmüş uyanma çıkmaz sokak değildir: uyanma düşer ve konaktan ölüm bildirimi gelmez
    /// (konakta öldürmenin kendisi fırladı) — durum askıda <c>Waking</c> kalır, Restart görünmez. Kapıya YENİ giren ilk yol (uçuştaki bir uyanmaya
    /// katılan değil) bitmiş ve düşmüş uyanmayı bulunca motoru kayıp bildirir: şeritte "Engine stopped unexpectedly (wake failed)" ve "Restart
    /// engine"; o yolun kendi isteği gitmez, konsola satır düşmez. Geç gelen bir ölüm sinyali yalnız şeridin metnini yeniler, satır yazmaz.
    /// <para>KIRMIZI (e843d516): sonraki tıklama düşmüş uyanmaya katılıp sessizce reddediliyordu — durum <c>Waking</c>'de kalıyor, Restart hiç
    /// görünmüyordu.</para></summary>
    [WpfFact]
    public async Task A_wake_stranded_without_a_death_signal_becomes_a_death_at_the_next_click()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, waking) = AsleepShellWithHeldWake(dir);
        var sync = vm.SyncCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        waking.SetException(new InvalidOperationException("the kill failed"));
        DispatcherPump.DrainToIdle();
        await sync;
        Assert.Equal(EngineState.Waking, vm.EngineState); // ön-koşul: uyanma düştü, ölüm sinyali gelmedi — durum askıda
        Assert.Equal(Visibility.Collapsed, window.Shell.Ribbon.RestartEngineAction.Visibility);
        string console = vm.GetRunDocumentText();

        await vm.LoadProjectLogAsync(MainWindowHost.IdOf("A")); // sonraki tıklama
        DispatcherPump.DrainToIdle();

        Assert.Equal(EngineState.Dead, vm.EngineState);
        Assert.Equal("Engine stopped unexpectedly (wake failed)", vm.EngineDiedMessage); // metnin TEK literal pini (M-5)
        Assert.Equal(vm.EngineDiedMessage, window.Shell.Ribbon.PhaseText.Text);
        Assert.Equal(Visibility.Visible, window.Shell.Ribbon.RestartEngineAction.Visibility);
        Assert.Empty(sent.Commands);
        Assert.Equal(1, probe.WakeCalls);
        Assert.Equal(console, vm.GetRunDocumentText());

        vm.OnEngineExited(-1); // geç gelen ölüm sinyali
        DispatcherPump.DrainToIdle();
        Assert.Equal("Engine stopped unexpectedly (exit -1)", vm.EngineDiedMessage);
        Assert.Equal(console, vm.GetRunDocumentText());
        GC.KeepAlive(window);
    }

    /// <summary>[karar 34] Koşu kapıda beklerken (bekleyen-koşu penceresinde uyanma sürüyor) uyanmanın motoru ölürse motor kaybı koşunun açılışını
    /// geri ALMAZ — komutu hiç gitmedi; geri alma TEK yerdedir ve sırası tektir: kapı dönünce koşunun kendi "gönderim düştü" geri alımı (Starting
    /// kapanır, faz tıklamadan önceki faza döner). Koşu bitmiş sayılmaz, konsola satır düşmez.
    /// <para>KIRMIZI (kapısız kod): <c>startRun</c> uyuyan motora gidiyor, motor kaybı koşuyu kendi yolundan bırakıyordu.</para></summary>
    [WpfFact]
    public async Task A_build_waiting_for_a_wake_whose_engine_dies_is_rolled_back_once_by_itself()
    {
        using var dir = new TempDir();
        var (window, vm, _, sent, waking, build, phaseBefore) = BuildWaitingForWake(dir);
        int ended = vm.EndedOperationSerial;
        Assert.Equal(AppPhase.Starting, vm.Phase);

        vm.OnEngineExited(-1);
        Assert.True(vm.IsStarting);
        Assert.Equal(AppPhase.Starting, vm.Phase);

        waking.SetException(new InvalidOperationException("Engine died during startup (exit -1)."));
        DispatcherPump.DrainToIdle();
        await build;

        Assert.False(vm.IsStarting);
        Assert.Equal(phaseBefore, vm.Phase);
        Assert.Empty(sent.Commands);
        Assert.Equal(ended, vm.EndedOperationSerial);
        Assert.DoesNotContain("[error]", vm.GetRunDocumentText());
        Assert.Equal(EngineState.Dead, vm.EngineState);
        GC.KeepAlive(window);
    }

    /// <summary>[Fix round 1 · Mi2 · karar 34] Uyutulan motorun ölüm sinyali uyku bittikten SONRA gelebilir (bekçi ölümü sahiplendi, uyku sessizce
    /// döndü, sinyal VM'e geç ulaştı); o arada konak ne uyuyor ne canlıdır — uyandırması bugünkü "motor çalışmıyor" hatasıyla düşer. Bir Build'in açılış
    /// koreografisi sürerken gelen bu geç ölüm koşunun açılışını geri ALMAZ (Starting ve faz durur): kayıp uyku döngüsündedir ve koşu kapıda
    /// bekliyor. Koreografi bitince kapı koşuyu satırsız reddeder (beklediği geçiş motoru kaybetti) ve koşunun kendi geri alımı TEK kez çalışır;
    /// komut gitmez, koşu bitmiş sayılmaz.
    /// <para><b>[DEĞİŞEN KURAL — uyanma gecikmesi, kontrolcü kararı]</b> ESKİ İDDİA: koreografi sürerken motor uyuyordu (koşunun uyanması
    /// koreografiden SONRA başlıyordu); koreografi bitince koşu ölü motora bugünkü gibi gidiyor (R2), tek "[error] failed to send build" satırı
    /// yazılıyor ve uyandırma hiç denenmiyordu. GEREKÇE: koşunun uyanması artık tıklamada başlar — koşu koreografi boyunca kapıda uyanmayı bekler
    /// ve uyanmanın düşmesi onu kapının kuralıyla satırsız reddeder (o geçişin tek sinyali ölüm şerididir). Kaybın pencereyi bırakmaması ve geri
    /// alımın tekliği değişmedi.</para>
    /// <para>KIRMIZI (e843d516): uyurken gelen ölüm sinyali pencereleri bırakıyordu — koreografi sürerken Starting kapanmış, faz sıfırlanmıştı
    /// (koşunun kendi geri alımı ikinci kez çalışacaktı).</para></summary>
    [WpfFact]
    public async Task A_late_death_of_the_slept_engine_during_a_builds_choreography_rolls_it_back_once()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, _) = AsleepShell(dir);
        probe.NextWake = () => Task.FromException<EngineReadyEvent>(new InvalidOperationException("Engine is not running."));
        var choreography = new TaskCompletionSource();
        vm.OperationChoreography = _ => choreography.Task;
        var phaseBefore = vm.Phase;
        int ended = vm.EndedOperationSerial;
        var build = vm.BuildCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.True(vm.IsStarting); // ön-koşul: koşu istendi, koreografi sürüyor; uyanma tıklamada denendi ve düştü, ölüm sinyali henüz yok
        Assert.Equal(EngineState.Waking, vm.EngineState);

        vm.OnEngineExited(-1);
        Assert.True(vm.IsStarting);
        Assert.Equal(AppPhase.Starting, vm.Phase);
        Assert.Equal(EngineState.Dead, vm.EngineState);

        choreography.SetResult();
        DispatcherPump.DrainToIdle();
        await build;

        Assert.False(vm.IsStarting);
        Assert.Equal(phaseBefore, vm.Phase);
        Assert.Empty(sent.Commands);
        Assert.DoesNotContain("[error]", vm.GetRunDocumentText());
        Assert.Equal(1, probe.WakeCalls);
        Assert.Equal(ended, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    /// <summary>[karar 34] Uyku sürerken kapıda bekleyen Sync varken uyku düşerse (ör. öldürme fırlattı) motor kaybı bugünkü ölüm yolundan bildirilir
    /// (şerit ölüm satırı) — ama uyku başladığından beri motora hiçbir şey gitmedi: kayıp bekleyen Sync'i bitmiş SAYMAZ, geri alımı Sync'in
    /// kendisidir. Kaybedilen motor uyandırılmaz, komut gitmez, konsola satır düşmez.
    /// <para>KIRMIZI (kapısız kod): Sync uyku sürerken gönderiliyordu; kayıp onu bitmiş sayıyordu.</para></summary>
    [WpfFact]
    public async Task A_failed_sleep_with_a_sync_waiting_for_it_ends_nothing()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, sleeping) = SleepingShell(dir);
        int ended = vm.EndedOperationSerial;

        var sync = vm.SyncCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.Empty(sent.Commands);
        string console = vm.GetRunDocumentText();

        sleeping.SetException(new InvalidOperationException("the kill failed"));
        DispatcherPump.DrainToIdle();
        await sync;

        Assert.Equal(RunViewModel.EngineDiedText(RunViewModel.IdleShutdownFailedCause), vm.EngineDiedMessage); // literal pini EngineSleepTests'te (M-5)
        Assert.Equal(EngineState.Dead, vm.EngineState);
        Assert.Empty(sent.Commands);
        Assert.Equal(0, probe.WakeCalls);
        Assert.False(vm.SyncBusy);
        Assert.Equal(ended, vm.EndedOperationSerial);
        Assert.Equal(console, vm.GetRunDocumentText());
        GC.KeepAlive(window);
    }

    /// <summary>[M-3 · T21 N1] Askıdaki uyanmanın raporundan (Mi1) SONRA gelen gerçek ölüm sinyali AYNI kaybı anlatır: yalnız şeridin metnini
    /// kendi nedeniyle yeniler, hiçbir pencereyi bırakmaz. Mi1 motoru zaten ölü bildirmiştir; arada istenen bir Build açılış koreografisindeyken
    /// geç ölüm gelir — koşunun açılışı (Starting, faz) durur. Koreografi bitince koşu ölü motora bugünkü gibi gider (R2), gönderim düşer ve
    /// koşunun kendi geri alımı TEK kez çalışır; tek "failed to send build" satırı.
    /// <para>KIRMIZI (değişiklikten önce): ölü motorun ikinci bildirimi pencereleri bırakıyordu — koreografi sürerken Starting kapanmış, faz
    /// dinlenme fazına çekilmişti (koşunun kendi geri alımı ikinci kez çalışacaktı).</para></summary>
    [WpfFact]
    public async Task A_late_death_after_the_stranded_wakes_report_only_renews_the_ribbon_text()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, waking) = AsleepShellWithHeldWake(dir);
        var sync = vm.SyncCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        waking.SetException(new InvalidOperationException("the kill failed")); // ölüm sinyali gelmeyen düşmüş uyanma
        DispatcherPump.DrainToIdle();
        await sync;
        await vm.LoadProjectLogAsync(MainWindowHost.IdOf("A")); // kapıya yeni giren ilk yol kaybı bildirir (Mi1)
        DispatcherPump.DrainToIdle();
        Assert.Equal(EngineState.Dead, vm.EngineState); // ön-koşul: Mi1'in raporu
        var choreography = new TaskCompletionSource();
        vm.OperationChoreography = _ => choreography.Task;
        SendsReachTheDeadEngine(vm);
        var phaseBefore = vm.Phase;
        int ended = vm.EndedOperationSerial;
        var build = vm.BuildCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.True(vm.IsStarting); // ön-koşul: koşu açılış koreografisinde

        vm.OnEngineExited(-1); // aynı motorun geç gelen gerçek ölüm sinyali
        Assert.Equal("Engine stopped unexpectedly (exit -1)", vm.EngineDiedMessage);
        Assert.True(vm.IsStarting);
        Assert.Equal(AppPhase.Starting, vm.Phase);

        choreography.SetResult();
        DispatcherPump.DrainToIdle();
        await build;

        Assert.False(vm.IsStarting);
        Assert.Equal(phaseBefore, vm.Phase);
        Assert.Equal(1, MainWindowHost.Occurrences(vm.GetRunDocumentText(), "[error] failed to send build: "));
        Assert.IsType<StartRunCommand>(Assert.Single(sent.Commands));
        Assert.Equal(ended, vm.EndedOperationSerial);
        Assert.Equal(1, probe.WakeCalls);
        GC.KeepAlive(window);
    }

    /// <summary>[T21 OOS-1 · karar 34] Uyanma başarılı olur ve motor hemen ölür — kapıda uyanmayı bekleyen bir Sync'in devamı henüz koşmamışken
    /// (uyanmanın bitişi katılanların devamını dispatcher'a post eder; ölüm sinyali onları öne geçer): motor kaybı o Sync'i bitmiş SAYMAZ — istek
    /// motora hiç gitmedi ve kendi geri alımı kapıdan dönünce çalışır; sahte işlem sonu yoktur (ne <c>EndSync</c> ne <c>EndedOperationSerial</c>
    /// artışı). Kayıp yalnız motora GİTMİŞ isteklerin pencerelerini bırakır, kapıda bekleyenlerinkini bırakmaz.
    /// <para><b>Kurgu (sıra deterministik):</b> proje logu tıklaması uyanmayı başlatır, Sync ona katılır. Uyanma bittiği anda ölüm gelir — uyanmanın
    /// devamı tamamlayan bağlamda satır içi koştuysa hemen, post edildiyse onun hemen ardından (aynı öncelik, sıra): katılan Sync'in devamı ikisinden
    /// sonradır. Ön-koşul bunu gösterir: ölüm anında motor hazır, Sync gönderilmemiş, istek penceresi açık.</para>
    /// <para>KIRMIZI (değişiklikten önce): kayıp kapıda bekleyen Sync'i bitirdi — <c>EndedOperationSerial</c> bir arttı.</para></summary>
    [WpfFact]
    public void A_death_right_after_a_wake_ends_no_request_still_waiting_at_the_gate()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, waking) = AsleepShellWithHeldWake(dir);
        _ = vm.LoadProjectLogAsync(MainWindowHost.IdOf("A")); // uyanmayı başlatır
        var sync = vm.SyncCommand.ExecuteAsync(null);         // katılır
        Assert.Equal(EngineState.Waking, vm.EngineState);     // ön-koşul: Sync kapıda, uyanma sürüyor
        int ended = vm.EndedOperationSerial;
        EngineState atDeath = default;
        bool syncSentAtDeath = true, syncWindowOpenAtDeath = false;
        void Die()
        {
            atDeath = vm.EngineState;
            syncSentAtDeath = sent.Commands.Any(c => c is SyncWorkspaceCommand);
            syncWindowOpenAtDeath = vm.SyncBusy;
            vm.OnEngineExited(-1);
        }

        waking.SetResult(Woken());
        if (vm.EngineState == EngineState.Ready) Die(); // uyanmanın devamı satır içi koştu — katılanların devamı post edildi
        else System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeAsync(Die); // uyanmanın devamından hemen sonra
        DispatcherPump.DrainToIdle();

        Assert.Equal(EngineState.Ready, atDeath); // ön-koşul: uyanma bitmişti...
        Assert.False(syncSentAtDeath);            // ...Sync henüz gönderilmemişti...
        Assert.True(syncWindowOpenAtDeath);       // ...ve istek penceresi açıktı
        Assert.Equal(EngineState.Dead, vm.EngineState);
        Assert.Equal(ended, vm.EndedOperationSerial); // kapıda bekleyen Sync bitmiş sayılmadı
        Assert.False(vm.SyncBusy);                    // Sync'in kendi geri alımı
        Assert.True(sync.IsCompleted);
        Assert.DoesNotContain(sent.Commands, c => c is SyncWorkspaceCommand or ListBranchesCommand); // Sync gönderilmedi
        Assert.Equal(1, probe.WakeCalls);
        GC.KeepAlive(window);
    }

    /// <summary>[T21 OOS-1 · round 2] Kapının "gönder" kararı ile isteğin motora teslimi arasında bir dispatcher sıçrayışı vardır: her <c>await</c>
    /// devamını post eder. Ölüm tam bu sıçrayışta gelirse — uyanma başarılı, kapıda bekleyen Sync "gönder" kararını aldı ama çağıranı
    /// (<c>SyncCoreAsync</c>) henüz devam etmedi — motor kaybı o Sync'i bitmiş SAYMAZ: istek kapıdan geçişini hâlâ tutuyor, motora teslim edilmedi.
    /// Çağıranı devam eder, ölü motora gönderim bugünkü satırlarıyla düşer (Sync ve ardındaki envanter, birer kez) ve Sync'in kendi geri alımı
    /// TEK kez çalışır; sahte işlem sonu yoktur.
    /// <para><b>Kurgu (ölçülen sıra — b11):</b> üç yol aynı uyanmayı bekler: ilk proje logu (uyanmayı başlatır, yerini ikincisi alır), ikinci log ve
    /// Sync. Uyanma bitince kapıdaki devamlar kayıt sırasıyla koşar ve her biri çağıranının devamını kuyruğun sonuna ekler — ikinci logunki Sync'in
    /// kapı devamından SONRA, Sync'in çağıran devamından ÖNCE. İkinci logun isteği teslim edilirken motor ölür (gözlemcide <c>OnEngineExited</c>):
    /// Sync'in kapısı o an "gönder" demiştir, çağıranı kuyruktadır. Ölümden sonraki gönderimler bugünkü gibi düşer (hiç başlamamış konak).</para>
    /// <para>KIRMIZI (kayıt kapıdan dönünce siliniyordu): ölüm "gönder" kararını almış Sync'i bitirdi — <c>EndedOperationSerial</c> bir arttı.</para></summary>
    [WpfFact]
    public void A_death_between_the_gates_send_verdict_and_the_send_ends_nothing()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, waking) = AsleepShellWithHeldWake(dir);
        SendsReachTheDeadEngine(vm);
        var record = vm.DebugOnCommandSent!;
        EngineState? atDeath = null;
        bool syncSentAtDeath = true, syncWindowOpenAtDeath = false;
        vm.DebugOnCommandSent = command =>
        {
            record(command);
            if (command is not GetProjectLogCommand || atDeath is not null) return;
            atDeath = vm.EngineState;
            syncSentAtDeath = sent.Commands.Any(c => c is SyncWorkspaceCommand);
            syncWindowOpenAtDeath = vm.SyncBusy;
            vm.OnEngineExited(-1); // uyanan motor ilk teslimde ölür
        };
        _ = vm.LoadProjectLogAsync(MainWindowHost.IdOf("A")); // uyanmayı başlatır
        _ = vm.LoadProjectLogAsync(MainWindowHost.IdOf("A")); // katılır, ilkinin yerini alır — kapıya Sync'ten önce girdi
        var sync = vm.SyncCommand.ExecuteAsync(null);         // katılır
        Assert.Equal(EngineState.Waking, vm.EngineState);     // ön-koşul: üçü de kapıda
        int ended = vm.EndedOperationSerial;

        waking.SetResult(Woken());
        DispatcherPump.DrainToIdle();

        Assert.Equal(EngineState.Ready, atDeath); // ön-koşul: ölüm uyanmadan sonra...
        Assert.False(syncSentAtDeath);            // ...Sync teslim edilmeden...
        Assert.True(syncWindowOpenAtDeath);       // ...istek penceresi açıkken geldi
        Assert.Equal(EngineState.Dead, vm.EngineState);
        Assert.Equal(ended, vm.EndedOperationSerial); // ölüm, teslim edilmemiş Sync'i bitirmedi
        string console = vm.GetRunDocumentText();
        Assert.Equal(1, MainWindowHost.Occurrences(console, "[error] failed to send sync: "));         // bugünkü satır, bir kez
        Assert.Equal(1, MainWindowHost.Occurrences(console, "[error] failed to send listBranches: ")); // envanterinki de
        Assert.False(vm.SyncBusy); // Sync'in kendi geri alımı
        Assert.True(sync.IsCompleted);
        Assert.Equal(1, probe.WakeCalls);
        GC.KeepAlive(window);
    }

    /// <summary>[T21 OOS-1 · round 2] Kapının geçişindeki kayıt TESLİMDE biter, gönderimin sonunda değil: kapıda beklemiş Sync motora teslim edildikten
    /// sonra — yazımı henüz bitmeden — gelen motor kaybı onu gönderilmiş sayar ve penceresini bırakır (dalga öncesi anlam: Sync'in sonu bildirilir);
    /// yazım ardından başarıyla biterse pencere açık kalmaz. Kayıt gönderimin sonuna dek yaşasaydı kayıp pencereyi atlar, başarılı yazım geri alım
    /// çalıştırmaz ve Sync, motoru ölmüş bir istek için kilitli kalırdı (yalnız "Restart engine" açardı).
    /// <para>KIRMIZI (geçici bozma: teslimdeki bırakma silindi — kayıt geçişin <c>using</c>'iyle, gönderimin sonunda biter): kayıptan sonra
    /// <c>SyncBusy</c> açık kaldı.</para></summary>
    [WpfFact]
    public void A_death_after_the_hand_over_ends_the_sync_like_any_sent_request()
    {
        using var dir = new TempDir();
        var (window, vm, _, sent, waking) = AsleepShellWithHeldWake(dir);
        var write = new TaskCompletionSource();
        vm.DebugSendOverride = command => command is SyncWorkspaceCommand ? write.Task : Task.CompletedTask; // Sync'in yazımı sürer
        var sync = vm.SyncCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.Equal(EngineState.Waking, vm.EngineState); // ön-koşul: Sync kapıda
        int ended = vm.EndedOperationSerial;
        waking.SetResult(Woken());
        DispatcherPump.DrainToIdle();
        Assert.IsType<SyncWorkspaceCommand>(Assert.Single(sent.Commands)); // ön-koşul: teslim edildi, yazımı sürüyor
        Assert.True(vm.SyncBusy);

        vm.OnEngineExited(-1);

        Assert.False(vm.SyncBusy);                        // teslim edilmiş istek: kayıp penceresini bıraktı
        Assert.Equal(ended + 1, vm.EndedOperationSerial); // Sync'in sonu bildirildi — dalga öncesi anlam
        write.SetResult();                                // yazım yine de başarıyla biter
        DispatcherPump.DrainToIdle();
        Assert.True(sync.IsCompleted);
        Assert.False(vm.SyncBusy);                        // pencere açık kalmadı
        Assert.Equal(ended + 1, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }

    /// <summary>[T21 OOS-1 · round 2] Kapının "gönderme" dediği geçiş de kayıt bırakmaz: uyanma sürerken geri alınan koşunun geçişi kapsamının
    /// sonunda bırakılır. Sonraki koşu hazır motora gider ve motoru ölürse kayıp onun açılışını bugünkü gibi bırakır. Sızan bir kayıt kaybın koşu
    /// penceresini atlatır — gönderilmiş koşu, motoru ölmüşken Starting'te asılı kalırdı.
    /// <para>KIRMIZI (geçici bozma: <c>BeginRunAsync</c> geçişi <c>using</c>'siz tutar — reddedilen geçiş hiç bırakılmaz): ölümden sonra
    /// <c>IsStarting</c> açık kaldı.</para></summary>
    [WpfFact]
    public async Task A_refused_gate_pass_leaves_no_registration_behind()
    {
        using var dir = new TempDir();
        var (window, vm, _, sent, waking, build, _) = BuildWaitingForWake(dir);
        await vm.StopCommand.ExecuteAsync(null); // bekleyen koşu geri alınır — kapı "gönderme" der
        waking.SetResult(Woken());
        DispatcherPump.DrainToIdle();
        await build;
        Assert.Empty(sent.Commands);                     // ön-koşul: ilk koşu gitmedi...
        Assert.Equal(EngineState.Ready, vm.EngineState); // ...ve motor uyanık

        var second = vm.BuildCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        await second;
        Assert.IsType<StartRunCommand>(Assert.Single(sent.Commands)); // ön-koşul: ikinci koşu hazır motora gitti
        Assert.True(vm.IsStarting);

        vm.OnEngineExited(-1);

        Assert.False(vm.IsStarting); // kayıp gönderilmiş koşunun açılışını bıraktı — geride kayıt yok
        GC.KeepAlive(window);
    }

    /// <summary>[T21 OOS-3 · R3 · PİN] Kapıda bekleyen bakım Clean'i de çıkışta geri çekilir — checkout ve pull da aynı yoldan geçer (gönderimin
    /// varsayılan yolu <c>TrySendAsync</c>): uyanma sürerken çıkış istenir; uyanma bitince Clean gönderilmez, kendi geri alımı (istek penceresi)
    /// pencereyi bırakır ve çıkış bir kez hazır olur. PİN: kapı R3'ten beri her komut türüne aynı soruyu sorar — kırmızısı gösterilemez; ileride
    /// bu yoldaki bir komutun çıkış sorusunu atlamasını yakalar.</summary>
    [WpfFact]
    public async Task An_exit_requested_during_a_wake_withdraws_a_clean_waiting_in_the_gate()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, waking) = AsleepShellWithHeldWake(dir);
        int exitReady = 0;
        vm.ExitReady += (_, _) => exitReady++;
        var clean = vm.CleanCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.Equal(EngineState.Waking, vm.EngineState); // ön-koşul: Clean kapıda
        Assert.True(vm.CleanBusy);

        vm.RequestExit();
        Assert.Equal(0, exitReady); // çıkış kapıda bekleyen Clean'i bekliyor (istek uçuşta sayılır)
        waking.SetResult(Woken());
        DispatcherPump.DrainToIdle();
        await clean;

        Assert.Empty(sent.Commands);
        Assert.Equal(1, exitReady);
        Assert.False(vm.CleanBusy);
        Assert.DoesNotContain("[error]", vm.GetRunDocumentText());
        GC.KeepAlive(window);
    }

    /// <summary>[uyanma gecikmesi — kontrolcü kararı, inceleme §8] Koşunun uyanması TIKLAMADA başlar ve açılış koreografisiyle örtüşür
    /// (bekleyen-koşu penceresinin içinde): koreografi sürerken uyanma da sürer; komut ancak ikisi de bitince — ve koşu hâlâ bekliyorsa — gider.
    /// İki bitiş sırası aynı sonucu verir: tek uyanma, komut hazır motora.
    /// <para><b>[DEĞİŞEN KURAL — karar 18]</b> ESKİ: uyanma koreografiden SONRA başlıyordu; uyku sonrası ilk Build koreografiyi ve uyanmayı arka
    /// arkaya ödüyordu. GEREKÇE: ikisi örtüşünce bekleyiş yalnız uzun olanınki kadardır; komutun yeri (ikisinden sonra, pencere hâlâ açıkken)
    /// değişmedi.</para>
    /// <para>KIRMIZI (değişiklikten önce): koreografi sürerken uyanma başlamamıştı (<c>WakeCalls</c> 0, motor uyuyordu).</para></summary>
    [WpfTheory]
    [InlineData(true)]  // önce uyanma biter
    [InlineData(false)] // önce koreografi biter
    public async Task A_builds_wake_starts_at_the_click_and_overlaps_the_choreography(bool wakeFirst)
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, waking) = AsleepShellWithHeldWake(dir);
        var choreography = new TaskCompletionSource();
        vm.OperationChoreography = _ => choreography.Task;

        var build = vm.BuildCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.Equal(1, probe.WakeCalls); // uyanma tıklamada başladı — koreografi sürüyor
        Assert.Equal(EngineState.Waking, vm.EngineState);

        if (wakeFirst) waking.SetResult(Woken());
        else choreography.SetResult();
        DispatcherPump.DrainToIdle();
        Assert.Empty(sent.Commands); // ikisinden biri sürerken komut gitmez
        Assert.True(vm.IsStarting);

        if (wakeFirst) choreography.SetResult();
        else waking.SetResult(Woken());
        DispatcherPump.DrainToIdle();
        await build;

        Assert.IsType<StartRunCommand>(Assert.Single(sent.Commands));
        Assert.Equal(EngineState.Ready, Assert.Single(sent.States));
        Assert.Equal(1, probe.WakeCalls);
        GC.KeepAlive(window);
    }

    /// <summary>[uyanma gecikmesi] Koreografi sürerken Stop (ya da Esc) isteği geri alır — "Cancelled — build not started", motora ne
    /// <c>startRun</c> ne <c>stopRun</c> gider. Tıklamada başlayan uyanma paylaşılan iştir ve biter: uyanan motor hazır kalır (boşta kalırsa kendi
    /// kuralıyla yine uyur).
    /// <para>KIRMIZI (değişiklikten önce): koreografi sırasında uyanma hiç başlamamıştı (<c>WakeCalls</c> 0, motor uyuyordu).</para></summary>
    [WpfFact]
    public async Task A_build_cancelled_during_its_choreography_sends_nothing_and_leaves_the_woken_engine_ready()
    {
        using var dir = new TempDir();
        var (window, vm, probe, sent, _) = AsleepShell(dir);
        var choreography = new TaskCompletionSource();
        vm.OperationChoreography = _ => choreography.Task;
        var build = vm.BuildCommand.ExecuteAsync(null);
        DispatcherPump.DrainToIdle();
        Assert.True(vm.IsStarting); // ön-koşul: koreografi sürüyor

        await vm.StopCommand.ExecuteAsync(null);
        Assert.Contains(RunViewModel.RunCancelledLine, vm.GetRunDocumentText());
        choreography.SetResult();
        DispatcherPump.DrainToIdle();
        await build;

        Assert.Empty(sent.Commands); // ne startRun ne stopRun
        Assert.Equal(1, probe.WakeCalls);
        Assert.Equal(EngineState.Ready, vm.EngineState);
        Assert.False(vm.IsStarting);
        GC.KeepAlive(window);
    }

    /// <summary>[T20 M5 · defter 9] Uykunun ve uyanmanın bitişi motor durumunu UI thread'inde yazar: VM dikişin görevini bağlamı yakalayarak bekler
    /// (<c>ConfigureAwait</c> yok) — görev havuzdan tamamlansa da (konağın uykusu ve uyanması havuzda biter) devam UI'ın dispatcher bağlamına post
    /// edilir, tamamlayan thread'de satır içi koşmaz. Durum kilitsizdir ve yalnız UI thread'inde yazılır (<c>RunViewModel.EngineState</c>'in
    /// özeti); bu pin o sözleşmeyi tutar.
    /// <para><b>Nasıl gözlenir:</b> dispatcher thread'i bloklu beklerken görev havuzdan tamamlanır ve durum AYNI havuz thread'inde hemen okunur:
    /// devam post edildiyse durum henüz değişmemiştir; dispatcher pompalanınca değişir.</para>
    /// <para>KIRMIZI (geçici bozma: dikişin await'i <c>ConfigureAwait(false)</c>): devam tamamlayan havuz thread'inde satır içi koştu — durum
    /// orada yazılmıştı.</para></summary>
    [WpfTheory]
    [InlineData(true)]  // uyku
    [InlineData(false)] // uyanma
    public void The_sleep_and_the_wake_write_the_engine_state_on_the_ui_thread(bool sleep)
    {
        using var dir = new TempDir();
        MainWindow window;
        RunViewModel vm;
        Action complete;
        EngineState settled;
        if (sleep)
        {
            TaskCompletionSource sleeping;
            (window, vm, _, _, sleeping) = SleepingShell(dir);
            complete = () => sleeping.SetResult();
            settled = EngineState.Asleep;
        }
        else
        {
            TaskCompletionSource<EngineReadyEvent> waking;
            (window, vm, _, _, waking) = AsleepShellWithHeldWake(dir);
            _ = vm.LoadProjectLogAsync(MainWindowHost.IdOf("A")); // uyanmayı başlatır
            complete = () => waking.SetResult(Woken());
            settled = EngineState.Ready;
        }
        var transition = vm.EngineState;
        Assert.True(transition is EngineState.Sleeping or EngineState.Waking, $"ön-koşul: geçiş uçuşta olmalıydı ({transition})");

        var onTheCompletingThread = Task.Run(() =>
        {
            complete();
            return vm.EngineState;
        }).GetAwaiter().GetResult(); // dispatcher bloklu: post edilen devam henüz koşamaz

        Assert.Equal(transition, onTheCompletingThread); // devam tamamlayan havuz thread'inde koşmadı — dispatcher'a post edildi
        DispatcherPump.DrainToIdle();
        Assert.Equal(settled, vm.EngineState); // dispatcher pompalanınca UI thread'inde yazıldı
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
