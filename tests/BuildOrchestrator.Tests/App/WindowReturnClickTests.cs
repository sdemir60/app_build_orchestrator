using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı kararı 2026-10-19 · 2026-10-26] <b>Arka plandaki pencereye bir tıkla dönmek.</b> Windows tıkı, pencereyi etkinleştirdikten
/// SONRA teslim eder; dönüş (son Sync'ten beş saniye geçtiyse) o anda sessiz bir Sync ister ve Sync istendiği ANDA kapattığı her kontrolü —
/// koşu, bakım ve pull komutlarını, Sync düğmesini, Debug|Release'i — tıkın altından kapatır.
///
/// <para><b>Karar (kullanıcı, 2026-10-26, ikinci): ara zincir YOK.</b> Pencereyi öne getiren tıkta bir kontrol ya kendi işlevini doğrudan
/// yapar (dönüşün Sync'i istenmez, tık normal çalışır) ya da tık yutulur ve dönüşün Sync'i çalışır. Ölçüt: işi bitince ekranın TAMAMI
/// tazelenmiş oluyorsa doğrudan. Hangi kontrollerin sayıldığını sahipleri söyler (<c>ActionBar.StartsOwnRefresh</c>,
/// <c>MaintenanceBox.StartsOwnRefresh</c>, title bar için <c>MainWindow.StartsOwnRefreshAt</c>); burada her kontrol için sonuç sınanır:
/// sayılanın tıkı çalışır, sayılmayanın tıkı dönüşün Sync'iyle kapanır. Exit ve × yalnız uçuşta iş yokken sayılır — iş sürerken basış
/// tazeleyen bir iş başlatmaz, dönüşün tetiği bekler ve iş bitince Sync'ler.</para>
///
/// <para>Ölçülen kusur (kullanıcı bildirimi, iki monitör — 2026-10-19): pencere öbür ekranda açıkken Build'e tıklamak pencereyi öne
/// getiriyor, dönüşün Sync'i Build'i tıkın altından kapatıyor ve tık hiçbir şey yapmıyordu; Debug|Release'te ilk tık yutuluyor, ikincisi
/// geçiyordu. 2026-10-26: aynı yutulma Sync düğmesinde, bakım kutusunda, N behind'da, chevron'da ve satırın düğmelerinde de vardı; Exit
/// hemen kapanmak yerine onay kartını açıyordu (sessiz Sync iş sayılır), × bir Sync başlatıyordu. İkinci karar bunlardan yalnız ekranın
/// tamamını tazeleyenleri doğrudan bıraktı.</para>
///
/// <para>Harness: gösterilmeyen pencerede ne <c>Activated</c> ne <c>WM_MOUSEACTIVATE</c> üretilebilir — testler pencerenin etkinleşme
/// girişini (<see cref="MainWindow.OnActivated"/>) basışın pencerenin içerik köküne göre konumuyla (<see cref="CenterInRoot"/>)
/// doğrudan sürer. Kabuk ekran dışı gerçek bir pencerede yüklenir (<see cref="MainWindowHost.WithLoadedActionBar"/>: Build düğmesinin
/// komutu <c>Loaded</c>'da bağlanır). Basış WPF'in yaptığı gibidir: kapalı öğeye basılamaz (<see cref="CommandPress"/>). Gönderimler sahte
/// ama canlı bir motora gider; saat enjekte edilir ve son Sync'ten bir dakika sonrasına alınır. Kökte git dizini yoktur: dönüş HEAD'i
/// okuyamaz ve sessiz Sync'ler.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class WindowReturnClickTests
{
    /// <summary>Ekranın tamamını tazelemeyen dönüşler: basış yok, ya da basış sayılmayan bir yere ya da sayılmayan tuşla iner.</summary>
    public enum OtherReturn
    {
        Keyboard, ClickOnAChip, ClickOnTheSelectedConfiguration, ClickOnTheBranchChip, LeftClickOnARowBody, RightPressOnBuild, RightPressOnARow,
    }

    /// <summary>Title bar'ın pencereye ait iki düğmesi.</summary>
    public enum TitleBarButton
    {
        Exit, Close,
    }

    /// <summary>Uçuştaki iş: bir koşu ya da bir Sync — ikisi de <c>IsOperationInFlight</c>'ı yükseltir (koordinatörün tetiği beklettiği meşguliyet).</summary>
    public enum BusyWith
    {
        Run, Sync,
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

    /// <summary>Pencereyi öne getiren bir SAĞ basışın pencereye ne verdiği. <c>WM_MOUSEACTIVATE</c>'in çözümü
    /// (<see cref="ActivatingClick.IsClientLeftPress"/>) yalnız istemci alanına SOL basışa konum verir (kullanıcı kararı 2026-10-26, ikinci:
    /// sağ basış hiçbir yerde sayılmaz); sağ basış pencereye basışsız bir dönüş gibi (<c>null</c>) gelir — basış nereye inerse insin.</summary>
    private static Point? RightPressOn(MainWindow window, FrameworkElement element) =>
        ActivatingClick.IsClientLeftPress(ActivatingClickTests.LParam(Win32.HTCLIENT, ActivatingClickTests.WM_RBUTTONDOWN))
            ? CenterInRoot(window, element)
            : null;

    /// <summary>Basış pencereyi <paramref name="target"/>'ın ortasından öne getirdi. Ön-koşul: hedef pencere arkadayken açık. Dönüşün Sync'i
    /// istenmez ve hedef basışın altında açık kalır — basış ona inebilir.</summary>
    private static void BringBackByPressingOn(MainWindow window, List<IpcCommand> sent, FrameworkElement target)
    {
        Assert.True(target.IsEnabled);                                      // ön-koşul: pencere arkadayken açık
        window.OnActivated(CenterInRoot(window, target));                   // basış pencereyi hedefin üstünden öne getirdi
        Assert.Empty(sent.OfType<SyncWorkspaceCommand>());                  // dönüşün Sync'i istenmedi…
        Assert.True(target.IsEnabled);                                      // …hedef basışın altında açık kaldı
    }

    /// <summary>Basış pencereyi <paramref name="target"/>'ın ortasından öne getirdi ve hedef ekranın tamamını tazelemiyor: dönüşün Sync'i o
    /// anda istenir (<c>BringBackByPressingOn</c>'un tersi). Hedefin kapanıp kapanmadığını çağıran söyler — Sync'in kapıyı kapattığı
    /// kontrol düğmenin kendisi olmayabilir (⋯'nin kapanan şeyi menünün maddeleridir).</summary>
    private static void BringBackAndSync(MainWindow window, List<IpcCommand> sent, FrameworkElement target)
    {
        Assert.True(target.IsEnabled);                                      // ön-koşul: pencere arkadayken açık
        window.OnActivated(CenterInRoot(window, target));                   // basış pencereyi hedefin üstünden öne getirdi
        Assert.Single(sent.OfType<SyncWorkspaceCommand>());                 // dönüşün Sync'i o anda istendi
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

    /// <summary>
    /// <b>[bulgu 4 · DEĞİŞEN KURAL — kullanıcı kararı 2026-10-26, ikinci]</b> Eski iddia: pencereyi Resolve cycles'ın üstünden öne getiren tık
    /// dönüşün Sync'ini istemez ve döngü koşusunu başlatır (her koşu diski baştan planlar). Değişme gerekçesi: koşunun önizlemesi plan
    /// bayrağını yazmaz (<c>RunViewModel.PreviewWritesPlanFlag</c>) — öbür satırların "derlenecek" bilgisi eski kalır, yani iş bitince ekranın
    /// tamamı tazelenmiş olmaz. Ara zincir yok: dönüş bugünkü gibi Sync'ler ve düğme tıkın altından kapanır.
    /// </summary>
    [StaFact]
    public void A_click_on_resolve_cycles_that_brings_the_window_back_still_syncs_and_the_button_closes_under_it() =>
        AfterAMinuteElsewhere(
            vm => VmTopology.SeedCycle(vm, MainWindowHost.IdOf("A"), MainWindowHost.IdOf("B")), // A ↔ B: Resolve cycles açık
            (window, _, bar, sent) =>
            {
                var resolve = bar.MaintenanceBoxControl.ResolveButton;
                BringBackAndSync(window, sent, resolve);
                Assert.False(resolve.IsEnabled);                            // düğme tıkın altından kapandı
            });

    /// <summary>
    /// <b>[bulgu 5 · DEĞİŞEN KURAL — kullanıcı kararı 2026-10-26, ikinci]</b> Eski iddia: pencereyi <c>N behind</c> chip'inin üstünden öne
    /// getiren tık dönüşün Sync'ini istemez ve pull'u ister (başarılı pull Sync zincirler). Değişme gerekçesi: kirli ağaçta pull reddedilir ve
    /// Sync zincirlenmez — iş bitince ekran tazelenmiş olmayabilir. Ara zincir yok: dönüş bugünkü gibi Sync'ler ve chip tıkın altından kapanır.
    /// </summary>
    [StaFact]
    public void A_click_on_the_behind_chip_that_brings_the_window_back_still_syncs_and_the_chip_closes_under_it() =>
        AfterAMinuteElsewhere((window, vm, bar, sent) =>
        {
            vm.Behind = 2;                                                  // fetch iki commit buldu: chip görünür
            bar.UpdateLayout();
            BringBackAndSync(window, sent, bar.BehindChip);
            Assert.False(bar.BehindChip.IsEnabled);                         // chip tıkın altından kapandı
        });

    /// <summary>
    /// <b>[bulgu 6 · DEĞİŞEN KURAL — kullanıcı kararı 2026-10-26, ikinci]</b> Eski iddia: pencereyi Build'in chevron'unun üstünden öne getiren
    /// tık dönüşün Sync'ini istemez ve menüyü açar. Değişme gerekçesi: chevron'un tıkı yalnız menü açar, kendisi hiçbir şeyi tazelemez. Ara
    /// zincir yok: dönüş bugünkü gibi Sync'ler ve chevron tıkın altından kapanır.
    /// </summary>
    [StaFact]
    public void A_click_on_the_build_chevron_that_brings_the_window_back_still_syncs_and_the_chevron_closes_under_it() =>
        AfterAMinuteElsewhere((window, _, bar, sent) =>
        {
            var chevron = bar.Split.MenuToggle!;
            BringBackAndSync(window, sent, chevron);
            Assert.False(chevron.IsEnabled);                                // chevron tıkın altından kapandı
        });

    /// <summary>
    /// <b>[bulgu 7 · DEĞİŞEN KURAL — kullanıcı kararı 2026-10-26, ikinci]</b> Eski iddia: pencereyi satırın play'inin üstünden öne getiren tık
    /// dönüşün Sync'ini istemez ve tek proje koşusunu başlatır. Değişme gerekçesi: tek proje koşusu yalnız hedefi yeniden kararlaştırır
    /// (<c>ProjectRunScope</c>); öbür satırların bilgisi eski kalır, ekranın tamamı tazelenmiş olmaz. Ara zincir yok: dönüş bugünkü gibi
    /// Sync'ler ve play tıkın altından kapanır.
    /// </summary>
    [StaFact]
    public void A_click_on_a_rows_play_that_brings_the_window_back_still_syncs_and_play_closes_under_it() =>
        AfterAMinuteElsewhere((window, _, _, sent) =>
        {
            var play = Hover(TheRow(window)).BuildButton;
            BringBackAndSync(window, sent, play);
            Assert.False(play.IsEnabled);                                   // play tıkın altından kapandı
        });

    /// <summary>
    /// <b>[bulgu 8 · DEĞİŞEN KURAL — kullanıcı kararı 2026-10-26, ikinci]</b> Eski iddia: pencereyi satırın ⋯'sinin üstünden öne getiren tık
    /// dönüşün Sync'ini istemez ve menü açık maddelerle açılır (maddeler tek proje koşusudur). Değişme gerekçesi: tek proje koşusu yalnız
    /// hedefi yeniden kararlaştırır, ekranın tamamını tazelemez. Ara zincir yok: dönüş bugünkü gibi Sync'ler. ⋯'nin kendisi kapanmaz — tık
    /// ona iner ve menüyü açar — ama Sync kapıyı kapattığı için menünün üç maddesi pasif açılır.
    /// </summary>
    [StaFact]
    public void A_click_on_a_rows_ellipsis_that_brings_the_window_back_still_syncs_and_its_menu_opens_with_the_items_closed() =>
        AfterAMinuteElsewhere((window, _, _, sent) =>
        {
            var actions = Hover(TheRow(window));
            BringBackAndSync(window, sent, actions.MoreButton);
            var items = MainWindowHost.OpenRowMenu(actions, () => CommandPress.Toggle(actions.MoreButton)); // tık ⋯'ye iner
            Assert.Equal(3, items.Count);
            Assert.All(items, item => Assert.False(item.IsEnabled));        // menü açıldı ama maddeler tıkın altından kapandı
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
    /// <b>[kontrolcü kararı R7] Kontrol yalnız işi başlayabilecekken sayılır.</b> Bir iş uçuştayken (bir koşu ya da bir Sync) Exit onay kartı
    /// açar, × pencereyi tepsiye indirir — hiçbiri tazeleme başlatmaz. Sayılsalardı dönüşün Sync'i istenmez ve tetik de saklanmazdı: iş
    /// bitince bekleyen bir şey kalmaz, ekran bayat kalırdı. Sayılmadıklarında dönüş bugünkü gibi davranır — iş sürerken tetik bekler
    /// (<c>AutoSyncCoordinator</c> meşgulken saklar) ve iş bitince değerlendirilir. İki kontrol de AÇIKTIR: sayılmamaları kapıdan değil
    /// kuraldandır.
    /// <para>Koşu uçuştaysa saklanan tetik koşu bitince sessiz Sync'i gönderir. Sync uçuştaysa tetik yine saklanır; ama Sync'in kendi bitişi
    /// son Sync anını yeniler ve bekleyen dönüş bitişte beş saniye eşiğine takılır, ikinci bir Sync gitmez — orada sınanan şey tetiğin
    /// SAKLANMASIDIR.</para>
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-26, ikinci]</b> Eski iddia: satırın ⋯'si ve satıra sağ basış da bu listedeydi (iş
    /// sürerken sayılmazlardı, iş yokken sayılırlardı). İkisi artık hiçbir zaman sayılmaz — dönüş her durumda Sync'ler ya da tetik bekler —
    /// bu yüzden bu teoride işleri kalmadı.</para>
    /// </summary>
    [StaTheory]
    [InlineData(TitleBarButton.Exit, BusyWith.Run)]
    [InlineData(TitleBarButton.Close, BusyWith.Run)]
    [InlineData(TitleBarButton.Exit, BusyWith.Sync)]
    [InlineData(TitleBarButton.Close, BusyWith.Sync)]
    public void A_title_bar_press_that_can_start_nothing_while_work_is_in_flight_leaves_the_return_refresh_waiting(
        TitleBarButton which, BusyWith busy) =>
        AfterAMinuteElsewhere((window, vm, _, sent) =>
        {
            if (busy == BusyWith.Run) MainWindowHost.StartBuild(vm, "A");   // bir koşu uçuşta…
            else vm.OnEvent(new SyncStartedEvent(vm.RootPath, "main"));     // …ya da bir Sync
            DispatcherPump.DrainToIdle();                                   // …yerleşim oturdu
            Assert.True(vm.IsOperationInFlight);                            // ön-koşul
            Assert.Null(vm.AutoSync!.PendingTrigger);                       // ön-koşul: bekleyen tetik yok
            var button = which == TitleBarButton.Exit ? window.ExitButton : window.CloseButton;
            Assert.True(button.IsEnabled);                                  // ön-koşul: kontrol açık

            window.OnActivated(CenterInRoot(window, button));               // pencere o kontrolün üstünden öne geldi
            Assert.Empty(sent.OfType<SyncWorkspaceCommand>());              // iş sürüyor: dönüşün Sync'i şimdi gitmez…
            Assert.NotNull(vm.AutoSync.PendingTrigger);                     // …ama tetik saklandı (sayılsaydı saklanmazdı)
            if (busy == BusyWith.Sync) return;                              // Sync'te bitiş beş saniye eşiğine takılır (yukarıdaki not)

            window.TrimGraceAfterFinale = TimeSpan.Zero;                    // koşu sonu trimi zamanlayıcı kurmasın (fixture'ın yaptığı gibi)
            MainWindowHost.FinishBuild(vm, "A");                            // …koşu bitti
            DispatcherPump.DrainToIdle();                                   // bekleyen tetik yeniden değerlendirilir
            Assert.Single(sent.OfType<SyncWorkspaceCommand>());             // …ve dönüşün sessiz Sync'i gitti
        });

    /// <summary>Kural dar kalır: ekranın tamamını tazelemeyen her dönüş bugünkü gibi Sync'ler ve Build Sync istendiği ANDA kapanır — klavye,
    /// görev çubuğu, tepsi (basış yok), bir sayaç chip'ine tık, zaten seçili configuration'a tık (geçiş no-op), branch chip'ine tık
    /// (kullanıcı kararı 2026-10-26: tık branch'i doğrudan değiştirmez, kullanıcı seçmeyebilir), satır gövdesine sol tık (seçim) ve sağ
    /// basış (kullanıcı kararı 2026-10-26, ikinci: hiçbir yerde sayılmaz — satırda da değil, çünkü satırın menüsünün maddeleri tek proje
    /// koşusudur ve o ekranın tamamını tazelemez).</summary>
    [StaTheory]
    [InlineData(OtherReturn.Keyboard)]
    [InlineData(OtherReturn.ClickOnAChip)]
    [InlineData(OtherReturn.ClickOnTheSelectedConfiguration)]
    [InlineData(OtherReturn.ClickOnTheBranchChip)]
    [InlineData(OtherReturn.LeftClickOnARowBody)]
    [InlineData(OtherReturn.RightPressOnBuild)]
    [InlineData(OtherReturn.RightPressOnARow)]
    public void Any_other_return_still_syncs_and_closes_build_at_once(OtherReturn how) =>
        AfterAMinuteElsewhere((window, _, bar, sent) =>
        {
            var build = (Button)bar.Split.PrimaryHalf!;
            Assert.True(build.IsEnabled);                                   // ön-koşul
            Assert.True(bar.BranchChip.IsEnabled);                          // ön-koşul: branch chip'i açık — sayılmaması kuraldandır

            Point? press = how switch
            {
                OtherReturn.Keyboard => null,
                OtherReturn.ClickOnAChip => CenterInRoot(window, bar.SigmaChip),
                OtherReturn.ClickOnTheSelectedConfiguration => CenterInRoot(window, ActionBarTests.ConfigurationItem(bar, "Debug")),
                OtherReturn.ClickOnTheBranchChip => CenterInRoot(window, bar.BranchChip),
                OtherReturn.LeftClickOnARowBody => CenterInRoot(window, TheRow(window)),
                OtherReturn.RightPressOnBuild => RightPressOn(window, build),
                _ => RightPressOn(window, TheRow(window)),
            };
            window.OnActivated(press);

            Assert.Single(sent.OfType<SyncWorkspaceCommand>());             // dönüş Sync'ledi
            Assert.False(build.IsEnabled);                                  // Build o anda kapandı
        });
}
