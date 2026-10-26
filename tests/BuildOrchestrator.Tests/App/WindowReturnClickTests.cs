using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı kararı 2026-10-19 · 2026-10-26] <b>Arka plandaki pencereye bir tıkla dönmek.</b> Windows tıkı, pencereyi etkinleştirdikten
/// SONRA teslim eder; dönüş (son Sync'ten beş saniye geçtiyse) o anda sessiz bir Sync ister ve Sync istendiği ANDA kapattığı her kontrolü —
/// koşu, bakım ve pull komutlarını, Sync düğmesini, Debug|Release'i — tıkın altından kapatır. Tık kendi tazelemesini yapan AÇIK bir
/// kontrole iniyorsa dönüşün Sync'i istenmez ve tık normal çalışır: alt çubukta Build'in birincil yarısı ve chevron'u, Sync düğmesi,
/// <c>N behind</c> chip'i, bakım kutusunun Clean / Optimize / Resolve cycles düğmeleri ve Debug|Release'in seçili olmayan öğesi; satırda
/// play ve ⋯ ile satıra SAĞ basış (satırın neresine olursa olsun); title bar'da Exit ve ×. Satırdakiler yalnız satırın koşu kapısı açıkken,
/// Exit ve × yalnız uçuşta iş yokken sayılır: iş sürerken basış tazeleyen bir iş başlatmaz, dönüşün tetiği bekler ve iş bitince Sync'ler.
/// Başka her dönüş — klavye, görev çubuğu, tepsi, branch chip'i (kullanıcı kararı: tık branch'i doğrudan değiştirmez, kullanıcı
/// seçmeyebilir), bir sayaç chip'i, seçili configuration, satır gövdesine sol tık, başka bir yere sağ basış — bugünkü gibi Sync'ler.
///
/// <para>Ölçülen kusur (kullanıcı bildirimi, iki monitör — 2026-10-19): pencere öbür ekranda açıkken Build'e tıklamak pencereyi öne
/// getiriyor, dönüşün Sync'i Build'i tıkın altından kapatıyor ve tık hiçbir şey yapmıyordu; Debug|Release'te ilk tık yutuluyor, ikincisi
/// geçiyordu. 2026-10-26: aynı yutulma Sync düğmesinde, bakım kutusunda, N behind'da, chevron'da ve satırın düğmelerinde de vardı; Exit
/// hemen kapanmak yerine onay kartını açıyordu (sessiz Sync iş sayılır), × bir Sync başlatıyordu.</para>
///
/// <para>Harness: gösterilmeyen pencerede ne <c>Activated</c> ne <c>WM_MOUSEACTIVATE</c> üretilebilir — testler pencerenin etkinleşme
/// girişini (<see cref="MainWindow.OnActivated"/>) basışın pencerenin içerik köküne göre konumuyla (<see cref="CenterInRoot"/>) ve tuşuyla
/// doğrudan sürer. Kabuk ekran dışı gerçek bir pencerede yüklenir (<see cref="MainWindowHost.WithLoadedActionBar"/>: Build düğmesinin
/// komutu <c>Loaded</c>'da bağlanır). Basış WPF'in yaptığı gibidir: kapalı öğeye basılamaz (<see cref="CommandPress"/>). Gönderimler sahte
/// ama canlı bir motora gider; saat enjekte edilir ve son Sync'ten bir dakika sonrasına alınır. Kökte git dizini yoktur: dönüş HEAD'i
/// okuyamaz ve sessiz Sync'ler.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class WindowReturnClickTests
{
    /// <summary>Kendi tazelemesini yapmayan dönüşler: basış yok, ya da basış muaf olmayan bir yere ya da muaf olmayan tuşla iner.</summary>
    public enum OtherReturn
    {
        Keyboard, ClickOnAChip, ClickOnTheSelectedConfiguration, ClickOnTheBranchChip, LeftClickOnARowBody, RightPressOnBuild,
    }

    /// <summary>Kendi tazelemesini yapabilen ama bir iş uçuştayken HİÇBİR işi başlatamayan basışlar: satırın ⋯'si, satıra sağ basış, Exit ve ×.</summary>
    public enum PressWhileBusy
    {
        RowEllipsis, RowRightPress, Exit, Close,
    }

    /// <summary>Kullanıcı bir dakika başka pencerede çalıştı: tek projeli, Sync'lenmiş kabuk; gönderimler toplanır, çubuk yüklüdür.
    /// Gövde dönüşü kendisi yapar.</summary>
    private static void AfterAMinuteElsewhere(Action<MainWindow, RunViewModel, ActionBar, List<IpcCommand>> body) =>
        AfterAMinuteElsewhere(beforeTheMinute: null, body);

    /// <param name="beforeTheMinute">Kabuk Sync'lendikten SONRA, saat bir dakika ilerlemeden ÖNCE koşar — dönüşün ön-koşulunu kuran
    /// testler içindir (ör. döngülü bir topoloji). Dönüş son Sync'in başlangıcından ya da bitişinden beş saniye sonra gelmelidir; saat
    /// bundan sonra ilerlediği için burada akan bir Sync de o eşiği bozmaz.</param>
    /// <param name="body">Dönüşü yapan gövde.</param>
    private static void AfterAMinuteElsewhere(Action<RunViewModel>? beforeTheMinute,
        Action<MainWindow, RunViewModel, ActionBar, List<IpcCommand>> body)
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm, _) = MainWindowHost.NewWithProjectsAndClock(dir, () => now, ["A"]);
        MainWindowHost.AcceptSends(vm);
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;
        beforeTheMinute?.Invoke(vm);
        now += 60_000;
        MainWindowHost.WithLoadedActionBar(window, (_, bar) => body(window, vm, bar, sent));
        GC.KeepAlive(window);
    }

    /// <summary>Bir öğenin ortası, pencerenin içerik köküne (<see cref="MainWindow.RootShell"/>: title bar + gövde) göre — pencereyi öne
    /// getiren basışın konumu <see cref="MainWindow.OnActivated"/>'e bu kökte verilir.</summary>
    private static Point CenterInRoot(MainWindow window, FrameworkElement element) => ActionBarTests.CenterIn(window.RootShell, element);

    /// <summary>Basış pencereyi <paramref name="target"/>'ın ortasından öne getirdi. Ön-koşul: hedef pencere arkadayken açık. Dönüşün Sync'i
    /// istenmez ve hedef basışın altında açık kalır — basış ona inebilir.</summary>
    private static void BringBackByPressingOn(MainWindow window, List<IpcCommand> sent, FrameworkElement target,
        MouseButton button = MouseButton.Left)
    {
        Assert.True(target.IsEnabled);                                      // ön-koşul: pencere arkadayken açık
        window.OnActivated(CenterInRoot(window, target), button);           // basış pencereyi hedefin üstünden öne getirdi
        Assert.Empty(sent.OfType<SyncWorkspaceCommand>());                  // dönüşün Sync'i istenmedi…
        Assert.True(target.IsEnabled);                                      // …hedef basışın altında açık kaldı
    }

    /// <summary>Tek projeli kabuğun çizilmiş tek satırı.</summary>
    private static ProjectRow TheRow(MainWindow window) =>
        DsResources.RealizedObjects(window.RootShell).OfType<ProjectRow>().Single();

    /// <summary>İmleç satırın üstünde: hover bloğu kurulur (<c>ProjectRow.EnsureActions</c>) ve yerleşir. Pencere arkadayken de imleç
    /// satıra girer — play ve ⋯ basıştan önce görünürdür.</summary>
    private static ProjectRowActions Hover(ProjectRow row)
    {
        row.SimulateHover(true);
        row.UpdateLayout();
        return row.Actions!;
    }

    /// <summary>Kusurun kendisi (2026-10-19): pencereyi Build'in üstünden öne getiren tık dönüşün Sync'ini istemez; Build açık kalır ve tık
    /// onu başlatır.</summary>
    [StaFact]
    public void A_click_on_build_that_brings_the_window_back_requests_no_return_sync_and_builds() =>
        AfterAMinuteElsewhere((window, vm, bar, sent) =>
        {
            var build = (Button)bar.Split.PrimaryHalf!;
            BringBackByPressingOn(window, sent, build);
            CommandPress.Invoke(build);                                     // tık Build'e iner
            DispatcherPump.DrainToIdle();
            Assert.True(vm.IsStarting);                                     // koşu tıkla başladı (kilit tıkta iner)
            Assert.Empty(sent.OfType<SyncWorkspaceCommand>());              // tazelemeyi koşunun kendi planı yapar
        });

    /// <summary>Asıl bulgu (2026-10-19): pencereyi Debug|Release'in seçili olmayan öğesinin üstünden öne getiren tık geçişi yapar — ikinci
    /// tık gerekmez. Tek Sync gider, o da geçişin kendi Sync'idir (yeni configuration'la).</summary>
    [StaFact]
    public void A_click_on_the_configuration_not_selected_that_brings_the_window_back_switches_it() =>
        AfterAMinuteElsewhere((window, vm, bar, sent) =>
        {
            var release = ActionBarTests.ConfigurationItem(bar, "Release");
            Assert.Equal("Debug", vm.Configuration);                        // ön-koşul
            BringBackByPressingOn(window, sent, release);
            CommandPress.Select(release);                                   // tık seçili olmayan öğeye iner
            Assert.Equal("Release", vm.Configuration);
            Assert.Equal("Release", Assert.Single(sent.OfType<SyncWorkspaceCommand>()).Configuration);
        });

    /// <summary>[bulgu 1] Sync düğmesi kendisi tam bir Sync'tir. Pencereyi onun üstünden öne getiren tık yerine sessiz bir Sync istenip düğme
    /// kapanıyordu; artık dönüş Sync istemez ve tık düğmenin kendi görünür (Manual) Sync'ini gönderir — tek Sync gider.</summary>
    [StaFact]
    public void A_click_on_sync_that_brings_the_window_back_requests_no_return_sync_and_runs_the_visible_sync() =>
        AfterAMinuteElsewhere((window, vm, bar, sent) =>
        {
            BringBackByPressingOn(window, sent, bar.SyncButton);
            CommandPress.Invoke(bar.SyncButton);                            // tık Sync'e iner
            DispatcherPump.DrainToIdle();
            Assert.Single(sent.OfType<SyncWorkspaceCommand>());             // tek Sync: düğmenin kendisi…
            Assert.Equal(OperationLabel.Sync, vm.CurrentOperation);         // …görünür Sync (sessiz Sync pill yazmaz)
        });

    /// <summary>[bulgu 2] Bakım kutusunun Clean'i bitince Sync zincirler (kendi tazelemesi). Pencereyi onun üstünden öne getiren tık dönüşün
    /// Sync'ini istemez ve Clean başlar.</summary>
    [StaFact]
    public void A_click_on_clean_that_brings_the_window_back_requests_no_return_sync_and_cleans() =>
        AfterAMinuteElsewhere((window, _, bar, sent) =>
        {
            var clean = bar.MaintenanceBoxControl.CleanButton;
            BringBackByPressingOn(window, sent, clean);
            CommandPress.Invoke(clean);                                     // tık Clean'e iner
            DispatcherPump.DrainToIdle();
            Assert.Single(sent.OfType<CleanWorkspaceCommand>());
        });

    /// <summary>[bulgu 3] Optimize de bitince Sync zincirler. Pencereyi onun üstünden öne getiren tık dönüşün Sync'ini istemez ve Optimize
    /// başlar.</summary>
    [StaFact]
    public void A_click_on_optimize_that_brings_the_window_back_requests_no_return_sync_and_optimizes() =>
        AfterAMinuteElsewhere((window, _, bar, sent) =>
        {
            var optimize = bar.MaintenanceBoxControl.OptimizeButton;
            BringBackByPressingOn(window, sent, optimize);
            CommandPress.Invoke(optimize);                                  // tık Optimize'a iner
            DispatcherPump.DrainToIdle();
            Assert.Single(sent.OfType<OptimizeWorkspaceCommand>());
        });

    /// <summary>[bulgu 4] Resolve cycles döngü koşusunu başlatır — her koşu diski baştan planlar. Pencereyi düğmenin üstünden öne getiren tık
    /// dönüşün Sync'ini istemez ve döngü koşusunu başlatır.</summary>
    [StaFact]
    public void A_click_on_resolve_cycles_that_brings_the_window_back_requests_no_return_sync_and_starts_the_cycle_run() =>
        AfterAMinuteElsewhere(
            vm => VmTopology.SeedCycle(vm, MainWindowHost.IdOf("A"), MainWindowHost.IdOf("B")), // A ↔ B: Resolve cycles açık
            (window, vm, bar, sent) =>
            {
                var resolve = bar.MaintenanceBoxControl.ResolveButton;
                BringBackByPressingOn(window, sent, resolve);
                CommandPress.Invoke(resolve);                               // tık Resolve cycles'a iner
                DispatcherPump.DrainToIdle();
                Assert.True(vm.IsStarting);                                 // koşu tıkla başladı…
                Assert.Equal(OperationLabel.Resolve, vm.CurrentOperation);  // …döngü koşusu
            });

    /// <summary>[bulgu 5] <c>N behind</c> chip'i ana repoyu ff-only ilerletir; başarılı pull Sync zincirler. Pencereyi chip'in üstünden öne
    /// getiren tık dönüşün Sync'ini istemez ve pull'u ister.</summary>
    [StaFact]
    public void A_click_on_the_behind_chip_that_brings_the_window_back_requests_no_return_sync_and_pulls() =>
        AfterAMinuteElsewhere((window, vm, bar, sent) =>
        {
            vm.Behind = 2;                                                  // fetch iki commit buldu: chip görünür
            bar.UpdateLayout();
            BringBackByPressingOn(window, sent, bar.BehindChip);
            CommandPress.Invoke(bar.BehindChip);                            // tık chip'e iner
            DispatcherPump.DrainToIdle();
            Assert.Single(sent.OfType<BuildOrchestrator.Contracts.Ipc.PullRepositoryCommand>());
        });

    /// <summary>[bulgu 6] Build'in chevron'u menüyü açar; menünün maddeleri koşudur. Pencereyi chevron'un üstünden öne getiren tık dönüşün
    /// Sync'ini istemez: chevron açık kalır ve menüyü açar.</summary>
    [StaFact]
    public void A_click_on_the_build_chevron_that_brings_the_window_back_requests_no_return_sync_and_opens_the_menu() =>
        AfterAMinuteElsewhere((window, _, bar, sent) =>
        {
            var chevron = bar.Split.MenuToggle!;
            BringBackByPressingOn(window, sent, chevron);
            CommandPress.Toggle(chevron);                                   // tık chevron'a iner
            Assert.True(bar.Split.IsMenuOpen);                              // menü açıldı
            bar.Split.IsMenuOpen = false;
        });

    /// <summary>[bulgu 7] Satırın play'i yalnız o projeyi derler — koşu diski baştan planlar. Pencereyi play'in üstünden öne getiren tık
    /// dönüşün Sync'ini istemez ve tek proje koşusunu başlatır.</summary>
    [StaFact]
    public void A_click_on_a_rows_play_that_brings_the_window_back_requests_no_return_sync_and_builds_that_project() =>
        AfterAMinuteElsewhere((window, vm, _, sent) =>
        {
            var play = Hover(TheRow(window)).BuildButton;
            BringBackByPressingOn(window, sent, play);
            CommandPress.Invoke(play);                                      // tık play'e iner
            DispatcherPump.DrainToIdle();
            Assert.True(vm.IsStarting);                                     // koşu tıkla başladı…
            Assert.Equal(MainWindowHost.IdOf("A"), vm.RunTargetId);         // …yalnız satırın projesi
        });

    /// <summary>[bulgu 8] Satırın ⋯'si menüyü açar; maddeler (Build / Rebuild / Clean — tek proje koşusu) kapılarını menü açılırken okur.
    /// Menü açılıyor ama dönüşün Sync'i kapıyı kapattığı için maddeler pasifti; artık dönüş Sync istemez ve maddeler açık.</summary>
    [StaFact]
    public void A_click_on_a_rows_ellipsis_that_brings_the_window_back_opens_its_menu_with_the_items_open() =>
        AfterAMinuteElsewhere((window, _, _, sent) =>
        {
            var actions = Hover(TheRow(window));
            BringBackByPressingOn(window, sent, actions.MoreButton);
            var items = MainWindowHost.OpenRowMenu(actions, () => CommandPress.Toggle(actions.MoreButton)); // tık ⋯'ye iner
            Assert.Equal(3, items.Count);
            Assert.All(items, item => Assert.True(item.IsEnabled));
            actions.MoreButton.IsChecked = false;                           // menü kapanır
        });

    /// <summary>[bulgu 9] Satıra SAĞ basış satırın neresine inerse insin ⋯ ile aynı menüyü açar (<c>ProjectRow.OnRowRightClicked</c>).
    /// Pencereyi satırın ortasına sağ basışla öne getirmek dönüşün Sync'ini istemez ve menü açık maddelerle açılır.</summary>
    [StaFact]
    public void A_right_press_on_a_row_that_brings_the_window_back_requests_no_return_sync_and_opens_its_menu() =>
        AfterAMinuteElsewhere((window, _, _, sent) =>
        {
            var row = TheRow(window);
            var actions = Hover(row);                                       // imleç satırda
            BringBackByPressingOn(window, sent, row, MouseButton.Right);    // satırın ortası
            var items = MainWindowHost.OpenRowMenu(actions, () => MouseInput.Release(row, MouseButton.Right));
            Assert.Equal(3, items.Count);
            Assert.All(items, item => Assert.True(item.IsEnabled));
            actions.MoreButton.IsChecked = false;                           // menü kapanır
        });

    /// <summary>[bulgu 10] Exit uçuşta iş yoksa onay sormadan kapatır. Pencereyi Exit'in üstünden öne getiren tıkta dönüşün sessiz Sync'i iş
    /// sayılıyor ve onay kartı açılıyordu; artık dönüş Sync istemez ve uygulama kartsız, hemen kapanır.</summary>
    [StaFact]
    public void A_click_on_exit_that_brings_the_window_back_requests_no_return_sync_and_quits_without_the_card() =>
        AfterAMinuteElsewhere((window, _, _, sent) =>
        {
            var exit = new ExitSeamFakes(window);                           // kapanış sayılır, uygulama kapanmaz
            BringBackByPressingOn(window, sent, window.ExitButton);
            CommandPress.Invoke(window.ExitButton);                         // tık Exit'e iner
            DispatcherPump.PumpUntil(() => exit.Shutdowns > 0, TimeSpan.FromSeconds(2));
            Assert.False(DsChrome.GetIsActive(window.ExitButton));          // onay kartı açılmadı…
            Assert.Equal(1, exit.Shutdowns);                                // …uygulama hemen kapandı
        });

    /// <summary>[bulgu 11] × pencereyi kapatır ya da tepsiye indirir — Sync anlamsızdır. Pencereyi ×'ın üstünden öne getiren tık dönüşün
    /// Sync'ini istemez.</summary>
    [StaFact]
    public void A_click_on_close_that_brings_the_window_back_requests_no_return_sync() =>
        AfterAMinuteElsewhere((window, _, _, sent) => BringBackByPressingOn(window, sent, window.CloseButton));

    /// <summary>
    /// <b>[kontrolcü kararı R7] Kontrol yalnız işi başlayabilecekken sayılır.</b> Bir koşu uçuştayken satırın ⋯'si ve satıra sağ basış yalnız
    /// pasif maddeli bir menü açar; Exit ve × hiçbir tazeleme başlatmaz. Sayılsalardı dönüşün Sync'i istenmez ve tetik de saklanmazdı: koşu
    /// bitince bekleyen bir şey kalmaz, ekran bayat kalırdı. Sayılmadıklarında dönüş bugünkü gibi davranır — iş sürerken tetik bekler
    /// (<c>AutoSyncCoordinator</c> meşgulken saklar), koşu bitince değerlendirilir ve sessiz Sync gider. Dört kontrol de AÇIKTIR: sayılmamaları
    /// kapıdan değil kuraldandır.
    /// </summary>
    [StaTheory]
    [InlineData(PressWhileBusy.RowEllipsis)]
    [InlineData(PressWhileBusy.RowRightPress)]
    [InlineData(PressWhileBusy.Exit)]
    [InlineData(PressWhileBusy.Close)]
    public void A_press_that_can_start_nothing_while_a_run_is_in_flight_leaves_the_return_refresh_waiting(PressWhileBusy how) =>
        AfterAMinuteElsewhere((window, vm, _, sent) =>
        {
            var row = TheRow(window);
            var actions = Hover(row);                                       // imleç satırda: ⋯ görünür
            MainWindowHost.StartBuild(vm, "A");                             // bir koşu uçuşta…
            DispatcherPump.DrainToIdle();                                   // …yerleşim oturdu
            Assert.True(vm.IsOperationInFlight);                            // ön-koşul
            (FrameworkElement Target, MouseButton Button) press = how switch
            {
                PressWhileBusy.RowEllipsis => (actions.MoreButton, MouseButton.Left),
                PressWhileBusy.RowRightPress => (row, MouseButton.Right),
                PressWhileBusy.Exit => (window.ExitButton, MouseButton.Left),
                _ => (window.CloseButton, MouseButton.Left),
            };
            Assert.True(press.Target.IsEnabled);                            // ön-koşul: kontrol açık

            window.OnActivated(CenterInRoot(window, press.Target), press.Button); // pencere o kontrolün üstünden öne geldi
            Assert.Empty(sent.OfType<SyncWorkspaceCommand>());              // iş sürüyor: dönüşün Sync'i şimdi gitmez…

            window.TrimGraceAfterFinale = TimeSpan.Zero;                    // koşu sonu trimi zamanlayıcı kurmasın (fixture'ın yaptığı gibi)
            MainWindowHost.FinishBuild(vm, "A");                            // …koşu bitti
            DispatcherPump.DrainToIdle();                                   // bekleyen tetik yeniden değerlendirilir
            Assert.Single(sent.OfType<SyncWorkspaceCommand>());             // …ve dönüşün sessiz Sync'i gitti
        });

    /// <summary>Kural dar kalır: kendi tazelemesini yapmayan her dönüş bugünkü gibi Sync'ler ve Build Sync istendiği ANDA kapanır — klavye,
    /// görev çubuğu, tepsi (basış yok), bir sayaç chip'ine tık, zaten seçili configuration'a tık (geçiş no-op), branch chip'ine tık
    /// (kullanıcı kararı 2026-10-26: tık branch'i doğrudan değiştirmez, kullanıcı seçmeyebilir), satır gövdesine sol tık (seçim) ve Build'e
    /// SAĞ basış (sağ basış yalnız satırda muaftır).</summary>
    [StaTheory]
    [InlineData(OtherReturn.Keyboard)]
    [InlineData(OtherReturn.ClickOnAChip)]
    [InlineData(OtherReturn.ClickOnTheSelectedConfiguration)]
    [InlineData(OtherReturn.ClickOnTheBranchChip)]
    [InlineData(OtherReturn.LeftClickOnARowBody)]
    [InlineData(OtherReturn.RightPressOnBuild)]
    public void Any_other_return_still_syncs_and_closes_build_at_once(OtherReturn how) =>
        AfterAMinuteElsewhere((window, _, bar, sent) =>
        {
            var build = (Button)bar.Split.PrimaryHalf!;
            Assert.True(build.IsEnabled);                                   // ön-koşul
            Assert.True(bar.BranchChip.IsEnabled);                          // ön-koşul: branch chip'i açık — sayılmaması kuraldandır

            (Point? At, MouseButton Button) press = how switch
            {
                OtherReturn.Keyboard => (null, MouseButton.Left),
                OtherReturn.ClickOnAChip => (CenterInRoot(window, bar.SigmaChip), MouseButton.Left),
                OtherReturn.ClickOnTheSelectedConfiguration =>
                    (CenterInRoot(window, ActionBarTests.ConfigurationItem(bar, "Debug")), MouseButton.Left),
                OtherReturn.ClickOnTheBranchChip => (CenterInRoot(window, bar.BranchChip), MouseButton.Left),
                OtherReturn.LeftClickOnARowBody => (CenterInRoot(window, TheRow(window)), MouseButton.Left),
                _ => (CenterInRoot(window, build), MouseButton.Right),
            };
            window.OnActivated(press.At, press.Button);

            Assert.Single(sent.OfType<SyncWorkspaceCommand>());             // dönüş Sync'ledi
            Assert.False(build.IsEnabled);                                  // Build o anda kapandı
        });
}
