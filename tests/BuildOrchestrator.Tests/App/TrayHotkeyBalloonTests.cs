using BuildOrchestrator.App;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [perf Faz B · B2] <b>Tepsideyken yok sayılan Build kısayolu nedenini balonla söyler.</b> Global Build kısayolu pencere
/// içindeki Build ile AYNI komuttur (<c>GlobalHotkeys.CommandFor</c> → <c>BuildCommand</c>) ve aynı kapıya tabidir: bir
/// Sync, Clean, Optimize, checkout ya da pull sürerken komut kapalıdır ve kısayol hiçbir şey yapmaz — basış kuyruğa
/// alınmaz ([kullanıcı kararı 2026-10-02], bkz. <c>RunRequestDuringWorkTests</c>). Pencere görünürken ekran bunu zaten
/// söyler (Sync düğmesi meşgul, şerit); pencere TEPSİDEYKEN söyleyen hiçbir şey yoktur ve kullanıcı kısayolun yutulduğunu
/// sanır. Bu sınıf onu pinler: balon yalnız (kapı kapalı VE yüzey gizli VE Show notifications açık) iken gösterilir ve
/// nedeni komutun kapısının KENDİ sorusundan (<c>RunViewModel.WhyRunCannotStart</c>) gelir.
///
/// <para><b>Test yüzeyleri:</b> <c>MainWindow.OnSourceInitialized</c> headless süitte koşmaz — gerçek <c>TaskbarIcon</c>
/// kurulmaz ve <c>_tray</c> <c>null</c>'dır; balonun sahte bir <see cref="ITrayRunNotifier"/>'a gitmesi
/// <c>MainWindow.TrayNotifierForTest</c> ile sağlanır (<c>OnGlobalHotkey</c> da internal: <c>WM_HOTKEY</c> gösterilmeyen
/// pencerede üretilemez). Gizli yüzey sinyali <c>SetSurfaceHidden</c> ile doğrudan yazılır. Balonun METNİ kısayol
/// testlerinde okunmaz: <c>AppTrayIcon.BuildIgnoredBody</c> tek yerdedir ve gerçek bir balon göstermeden çağrılabilir
/// (aşağıda pinlenir).</para>
///
/// <para><b>"Sync sürüyor" ön-koşulu</b> motorun <c>SyncStartedEvent</c> cevabıyla kurulur
/// (<c>MainWindowInputTests.F5_during_a_sync_does_nothing</c> ile AYNI yol): işin SÜRDÜĞÜ hâlin en doğrudan kurulumu.
/// Kapı kararı iş türüne bakmaz — Clean/Optimize/checkout/pull'un kapıyı kapatması <c>RunRequestDuringWorkTests</c>'te
/// pinlidir; burada nedenin kapıyla aynı kararı verdiği ve balonun koşulları sınanır.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class TrayHotkeyBalloonTests
{
    /// <summary>Topolojisi olan, sahte tepsili kabuk. Pencere görünür başlar (üretimin açılışı); tepsi senaryoları
    /// <c>SetSurfaceHidden(true)</c>'yu kendisi çağırır ki her testte durum okunur olsun.</summary>
    private static (MainWindow window, RunViewModel vm, RecordingTrayNotifier tray) NewShell(TempDir dir)
    {
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, MainWindowHost.ProjectNames(2));
        var tray = new RecordingTrayNotifier();
        window.TrayNotifierForTest = tray;
        return (window, vm, tray);
    }

    /// <summary>Motorun "Sync başladı" cevabı: iş sürüyor, koşu komutlarının kapısı kapalı.</summary>
    private static void SyncIsRunning(RunViewModel vm)
    {
        vm.OnEvent(new SyncStartedEvent(@"C:\src\OSYS", "main"));
        Assert.True(vm.SyncBusy); // ön-koşul: kapı gerçekten kapalı olmalı
        Assert.False(vm.BuildCommand.CanExecute(null));
    }

    /// <summary>Neden ile kapı AYNI kararı verir: neden varsa komut kapalıdır, yoksa açıktır. Ayrışma, yok sayılan
    /// bir kısayolun sessiz kalması (kapalı kapı, neden yok) ya da kapı açıkken bir neden söylenmesi demektir.</summary>
    private static void AssertReasonMatchesGate(RunViewModel vm, string? expectedReason)
    {
        Assert.Equal(expectedReason, vm.WhyRunCannotStart());
        Assert.Equal(expectedReason is null, vm.BuildCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- kısayol → balon

    [StaFact]
    public void A_build_hotkey_ignored_while_the_window_is_in_the_tray_shows_one_balloon_with_the_reason()
    {
        using var dir = new TempDir();
        var (window, vm, tray) = NewShell(dir);
        window.SetSurfaceHidden(true);
        SyncIsRunning(vm);

        window.OnGlobalHotkey(GlobalHotkeyAction.Build);

        Assert.Equal(RunGateText.SyncInProgress, Assert.Single(tray.IgnoredReasons));
        Assert.False(vm.IsStarting); // kısayol yok sayıldı: komut çalışmadı, kuyruğa da girmedi
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_visible_window_gets_no_balloon_because_the_screen_already_says_it()
    {
        using var dir = new TempDir();
        var (window, vm, tray) = NewShell(dir);
        window.SetSurfaceHidden(false);
        SyncIsRunning(vm);

        window.OnGlobalHotkey(GlobalHotkeyAction.Build);

        Assert.Empty(tray.IgnoredReasons);
        Assert.False(vm.IsStarting);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_balloon_asks_the_Show_notifications_switch_at_the_moment_it_would_appear()
    {
        using var dir = new TempDir();
        var (window, vm, tray) = NewShell(dir);
        window.SetSurfaceHidden(true);
        SyncIsRunning(vm);
        var store = MainWindowHost.UiStateStore(dir);

        store.Save(new UiState { ShowNotifications = false });
        window.OnGlobalHotkey(GlobalHotkeyAction.Build);
        Assert.Empty(tray.IgnoredReasons); // her balon tek anahtara bağlı (ARCHITECTURE §12.3)

        store.Save(new UiState { ShowNotifications = true }); // anahtar uygulama çalışırken açıldı: taze okunur
        window.OnGlobalHotkey(GlobalHotkeyAction.Build);
        Assert.Equal(RunGateText.SyncInProgress, Assert.Single(tray.IgnoredReasons));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void When_the_gate_is_open_the_hotkey_starts_the_build_and_shows_no_balloon()
    {
        using var dir = new TempDir();
        var (window, vm, tray) = NewShell(dir);
        var sent = new List<object>();
        vm.DebugSendOverride = command => { sent.Add(command); return Task.CompletedTask; };
        window.SetSurfaceHidden(true);
        Assert.Null(vm.WhyRunCannotStart()); // ön-koşul: kapı açık

        window.OnGlobalHotkey(GlobalHotkeyAction.Build);

        Assert.True(vm.IsStarting);
        Assert.Contains(sent, command => command is StartRunCommand);
        Assert.Empty(tray.IgnoredReasons);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_second_press_while_the_first_build_is_starting_is_answered_with_a_run_is_already_in_flight()
    {
        using var dir = new TempDir();
        var (window, vm, tray) = NewShell(dir);
        MainWindowHost.AcceptSends(vm);
        window.SetSurfaceHidden(true);

        window.OnGlobalHotkey(GlobalHotkeyAction.Build); // kapı açık: derleme başlar, balon yok
        Assert.True(vm.IsStarting);
        Assert.Empty(tray.IgnoredReasons);

        window.OnGlobalHotkey(GlobalHotkeyAction.Build); // kapı kapandı: ikinci basış yok sayılır

        Assert.Equal(RunGateText.RunInFlight, Assert.Single(tray.IgnoredReasons));
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- balon metni (tek yer)

    /// <summary>Balon gövdesi nedeni söyler ve orada BİTER: çerçeve nedenin arkasına kendi cümlesini eklemez. Beklenen
    /// metin üretim cümlesinin kopyası DEĞİLDİR — gövde <see cref="AppTrayIcon.BuildIgnoredBody"/> ile kurulur, yalnız
    /// nedenin metni iddia edilir.
    /// <para><b>[DEĞİŞEN KURAL — B2 incelemesi · I1, 2026-10-03]</b> ESKİ iddia: gövde nedenden sonra "Try again when it
    /// finishes." diye sürer (cümle literal pinliydi). Gerekçe: nedenlerin bir kısmı kendiliğinden BİTMEZ — motor
    /// erişilemiyor, uygulama kapanıyor, proje listesi yok — ve "no project list yet — Sync first. Try again when it
    /// finishes." kendisiyle çelişiyordu (bitecek iş yokken beklemeyi söylüyordu). Bitişi söyleyen neden ("a Sync is in
    /// progress") bunu kendi cümlesinde taşır; çerçeve ipucu taşımaz. Test yeni kuralı pinler: neden gövdenin SON
    /// sözüdür.</para></summary>
    [Fact]
    public void The_balloon_body_ends_with_the_reason_and_adds_no_hint_of_its_own()
    {
        const string reason = "whatever the gate answers"; // yalnız biçim sınanır: gerçek bir neden cümlesinin kopyası değil

        Assert.EndsWith($"{reason}.", AppTrayIcon.BuildIgnoredBody(reason));
    }

    // ---------------------------------------------------------------- neden = kapının kendi sorusu

    [StaFact]
    public void With_nothing_in_the_way_there_is_no_reason_and_the_gate_is_open()
    {
        using var dir = new TempDir();
        var (window, vm, _) = NewShell(dir);

        AssertReasonMatchesGate(vm, null);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_sync_in_flight_is_the_reason_and_closes_the_gate()
    {
        using var dir = new TempDir();
        var (window, vm, _) = NewShell(dir);
        SyncIsRunning(vm);

        AssertReasonMatchesGate(vm, RunGateText.SyncInProgress);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_run_in_flight_is_the_reason_and_closes_the_gate()
    {
        using var dir = new TempDir();
        var (window, vm, _) = NewShell(dir);
        MainWindowHost.AcceptSends(vm);
        window.SetSurfaceHidden(true);
        vm.BuildCommand.Execute(null); // kilit tıkta başlar, runStarted'ı beklemez
        Assert.True(vm.IsStarting);

        AssertReasonMatchesGate(vm, RunGateText.RunInFlight);
        GC.KeepAlive(window);
    }

    /// <summary>Workspace var ama proje listesi yok (henüz Sync olmadı): neden Sync'e yönlendirir ve kapı kapalıdır.
    /// <para><b>[DEĞİŞEN KURAL — son toparlama · F-M2]</b> ESKİ İDDİA (vaka workspace'siz bir VM ile kurulurdu): kök
    /// boşken de neden <c>no project list yet — Sync first</c>. GEREKÇE: workspace yokken Sync de kapalıdır (ActionBar'da
    /// Sync düğmesi workspace'e bağlı) — yönlendirme çıkmaz sokaktı. Vaka artık gerçekten bir workspace kurar;
    /// workspace'siz durum kendi vakasındadır (aşağıda).</para></summary>
    [StaFact]
    public void With_a_workspace_but_no_project_list_the_reason_points_to_Sync_and_the_gate_is_closed()
    {
        using var dir = new TempDir();
        var (window, vm) = MainWindowHost.New(dir);
        MainWindowHost.AcceptSends(vm);
        vm.RootPath = @"D:\repo"; // workspace var, Sync henüz yok → topoloji yok
        Assert.True(vm.HasWorkspace); // ön-koşul

        AssertReasonMatchesGate(vm, RunGateText.NoProjectList);
        GC.KeepAlive(window);
    }

    /// <summary>[F-M2] Workspace yokken neden "Sync first" DEMEZ: Sync de kapalıdır, doğru yol Settings'ten bir kök seçmektir
    /// (konsolun aynı durum için metni "Waiting for a workspace"). Kapı yine kapalıdır ve neden onunla aynı kararı verir.</summary>
    [StaFact]
    public void Without_a_workspace_the_reason_is_the_missing_workspace_not_a_Sync()
    {
        using var dir = new TempDir();
        var (window, vm) = MainWindowHost.New(dir);
        Assert.False(vm.HasWorkspace); // ön-koşul: kök boş

        AssertReasonMatchesGate(vm, RunGateText.NoWorkspace);
        GC.KeepAlive(window);
    }
}
