using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;
using H.NotifyIcon;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [ui gap cleanup/Task 3] Tepsi menüsündeki Stop maddesi <see cref="RunViewModel.StopCommand"/>'a bağlıdır —
/// etkinliği doğrudan <c>CanExecute</c>'tan gelir, ActionBar'daki Stop düğmesiyle AYNI kaynak (bkz.
/// <c>ActionBarTests.The_stop_button_reads_Stop_now_and_is_enabled_while_stopping_until_the_hard_stop_is_sent</c>). Eskiden madde
/// HER ZAMAN etkindi ve tıklamada <c>MainWindow</c>'un kendi kapısı <c>CanExecute</c>'u sorardı — durdurulacak
/// bir derleme yokken madde tıklanabilir GÖRÜNÜYORDU, yalnız tıklama sessizce hiçbir şey yapmıyordu.
///
/// <para>Menü <see cref="AppTrayIcon.CreateMenu"/> ile gerçek bir <c>TaskbarIcon</c> kurmadan sınanır —
/// <c>AppTrayIcon</c>'un ctor'u gerçek bir tepsi ikonu ister ve headless süitte kurulamaz.</para>
/// </summary>
public sealed class TrayMenuTests
{
    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), MainWindowHost.NeverTickingBatcher(), () => "r1")
        { RootPath = @"D:\repo" };

    /// <summary>Stop maddesi menünün komutu olan TEK maddesidir (Exit'in komutu yoktur). Başlığı aşamaya göre değişir
    /// (<see cref="StopText.Label"/>), bu yüzden başlıkla ARANMAZ.</summary>
    private static MenuItem StopItem(ContextMenu menu) =>
        menu.Items.Cast<MenuItem>().Single(i => i.Command is not null);

    // ---------------------------------------------------------------- etkinlik = CanExecute

    /// <summary>Uçuşta hiçbir run yokken (<c>CanExecute</c> baştan <c>false</c>) madde KURULUŞTA pasif
    /// görünmelidir — eskiden her zaman etkindi.</summary>
    [StaFact]
    public void Tray_stop_is_disabled_while_no_build_can_be_stopped()
    {
        var vm = NewVm();
        Assert.False(vm.StopCommand.CanExecute(null)); // ön-koşul: durdurulacak bir şey yok

        var menu = AppTrayIcon.CreateMenu(vm.StopCommand, () => { }, vm);

        Assert.False(StopItem(menu).IsEnabled);
    }

    /// <summary>Madde koşu boyunca komutu TAKİP eder: run başlayınca etkinleşir; Stop'a basılmış gibi faz
    /// <see cref="AppPhase.Stopping"/>'e alınınca ETKİN KALIR (ikinci basış hard stop'tur) ve hard stop gidince pasifleşir —
    /// ActionBar'ın Stop düğmesiyle AYNI kural (<c>CanStop = (IsRunning || IsStarting) &amp;&amp; !HardStopRequested</c>).
    ///
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-03]</b> ESKİ İDDİA: Stopping'de madde pasifleşir (ikinci bir
    /// <c>stopRun</c> önlenir). GEREKÇE: ikinci Stop artık hard stop'tur ve tepsiden de verilebilmelidir; madde ile düğme
    /// komutun AYNI örneğini okur — iki yüzeyin kuralı ayrışmaz.</para></summary>
    [StaFact]
    public void Tray_stop_follows_the_stop_command_through_a_run()
    {
        var vm = NewVm();
        var menu = AppTrayIcon.CreateMenu(vm.StopCommand, () => { }, vm);
        var stop = StopItem(menu);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug"));

        Assert.True(stop.IsEnabled);

        vm.Phase = AppPhase.Stopping;

        Assert.True(stop.IsEnabled);

        vm.HardStopRequested = true;

        Assert.False(stop.IsEnabled);
    }

    /// <summary>[Stop now] Maddenin başlığı Stop düğmesiyle AYNI aşamayı okur (<see cref="RunViewModel.StopStage"/>; metin tek
    /// kaynak <see cref="StopText"/>): Stopping'de ikinci basış hard stop olduğundan madde "Stop now" der — "Stop" demek,
    /// basışın artık zararsız olmadığını gizlerdi — ve hard gidince "Terminating…" der (pasif). Koşu bitince başlık "Stop"a
    /// döner. Eskiden başlık her aşamada "Stop" idi. Faz ve hard bayrağı doğrudan set edilir: buraya NASIL girildiği
    /// <c>StopNowTests</c>'te pinli.</summary>
    [StaFact]
    public void Tray_stop_label_follows_the_stop_stage_through_a_run()
    {
        var vm = NewVm();
        var stop = StopItem(AppTrayIcon.CreateMenu(vm.StopCommand, () => { }, vm));
        Assert.Equal(StopText.Label(StopStage.Stop), (string)stop.Header); // ön-koşul
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug"));
        Assert.Equal(StopText.Label(StopStage.Stop), (string)stop.Header);

        vm.Phase = AppPhase.Stopping; // graceful gitti

        Assert.Equal(StopText.Label(StopStage.StopNow), (string)stop.Header);
        Assert.True(stop.IsEnabled);

        vm.HardStopRequested = true; // hard gitti

        Assert.Equal(StopText.Label(StopStage.Terminating), (string)stop.Header);
        Assert.False(stop.IsEnabled);

        vm.OnEvent(new RunStoppedEvent("r1", WasHard: true)); // koşu bitti

        Assert.Equal(StopText.Label(StopStage.Stop), (string)stop.Header);
    }

    // ---------------------------------------------------------------- tıklama → gerçek stopRun

    /// <summary>Madde UIA ile çağrıldığında (ekran okuyucu/otomasyon yolunun ta kendisi) TEK bir
    /// <c>stopRun</c> gider, hem de KOŞAN run'ın kimliğiyle — <c>StopAsync</c>, <c>_currentRunId</c> yokken
    /// hiçbir şey göndermediği için kurulum yalnız <c>IsStarting</c> DEĞİL gerçek bir <see cref="RunStartedEvent"/>
    /// ister.</summary>
    [StaFact]
    public void Invoking_the_tray_stop_item_stops_the_run()
    {
        var vm = NewVm();
        var menu = AppTrayIcon.CreateMenu(vm.StopCommand, () => { }, vm);
        var stop = StopItem(menu);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug"));
        Assert.True(stop.IsEnabled); // ön-koşul: madde GERÇEKTEN tıklanabilir
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        ((IInvokeProvider)new MenuItemAutomationPeer(stop)).Invoke();
        DispatcherPump.PumpUntil(() => sent.Count > 0, TimeSpan.FromSeconds(2));

        var cmd = Assert.Single(sent.OfType<StopRunCommand>());
        Assert.Equal("r1", cmd.RunId);
        Assert.Equal(StopKind.Graceful, cmd.Kind);
    }

    // ---------------------------------------------------------------- menüyü kim açar

    /// <summary>[tray menu position] Kütüphane menüyü açmaya kalkınca (<c>TaskbarIcon.ShowContextMenu</c> — sağ tıkın kütüphanedeki
    /// gerçek yolu) menüyü KENDİSİ açmaz, açışı uygulama devralır. Kütüphanenin açışı tık noktasını SAKLADIĞI bir DPI çarpanıyla çevirir;
    /// ekran ölçüsü uygulama açıkken değişince menü tıktan yüzlerce piksel kayıyordu (gerçek tepsiyle ölçüm:
    /// <c>TrayMenuPositionProbeTests</c>). Burada kütüphanenin yerleşimi menüye hiç yazılmaz ve uygulamanın açışı bir kez çağrılır.
    /// <para>Gerçek bir tepsi ikonu kurulmaz: çıplak bir <see cref="TaskbarIcon"/> ne pencere ne tepsi ikonu yaratır (ikisini de
    /// <c>ForceCreate</c> yapar) — test bu yüzden headless koşar.</para></summary>
    [StaFact]
    public void The_app_opens_the_tray_menu_itself_instead_of_the_tray_library()
    {
        var menu = AppTrayIcon.CreateMenu(AppTrayIcon.NoRunToStop, () => { });
        using var icon = new TaskbarIcon { ContextMenu = menu };
        int opened = 0;
        AppTrayIcon.OpenMenuOurselves(icon, () => opened++);
        try
        {
            icon.ShowContextMenu(new System.Drawing.Point(100, 100));

            Assert.Equal(1, opened);
            Assert.False(menu.IsOpen); // kütüphane açmadı
            Assert.Equal(PlacementMode.MousePoint, menu.Placement); // kütüphanenin noktası menüye yazılmadı: menü imlece bağlı kalır
        }
        finally
        {
            menu.IsOpen = false;
        }
    }

    // ---------------------------------------------------------------- görev çubuğu tıkı

    /// <summary>Ölçülen ekran (<c>TrayMenuPositionProbeTests</c>): 2880×1800 %125, altta 60 px görev çubuğu — çalışma alanı 1740'ta biter.</summary>
    private static DisplayMonitor Laptop(double scale = 1.25) => new("test", new Int32Rect(0, 0, 2880, 1740), scale);

    private static Win32.POINT At(int x, int y) => new() { X = x, Y = y };

    /// <summary>[tray menu above the taskbar] İmleç çalışma alanının içindeyse (ör. gizli simgeler kutusundaki bir ikon) menü tık
    /// noktasında açılır: kaydırma yoktur.</summary>
    [StaFact]
    public void A_click_inside_the_work_area_opens_the_menu_at_the_click()
    {
        var menu = AppTrayIcon.CreateMenu(AppTrayIcon.NoRunToStop, () => { });

        AppTrayIcon.KeepOffTheTaskbar(menu, At(1200, 900), Laptop());

        Assert.Equal(0, menu.HorizontalOffset);
        Assert.Equal(0, menu.VerticalOffset);
    }

    /// <summary>[tray menu above the taskbar] Görev çubuğundaki tıkta açılış noktası görev çubuğunun üst kenarına çekilir: kaydırma
    /// fiziksel pikselde (1770 → 1740, 30 px) hesaplanır ve WPF birimine ekranın ölçeğiyle çevrilir — %125'te 24, %150'de 20. Yatay
    /// kaydırma yoktur: menü tıkın hizasında kalır. Eskiden kaydırma yoktu ve menünün alt kenarı imlecin hizasında, görev çubuğunun
    /// içinde kalıyordu (gerçek tepsiyle ölçüm: <c>TrayMenuPositionProbeTests</c>).</summary>
    [StaTheory]
    [InlineData(1.25, -24)]
    [InlineData(1.5, -20)]
    [InlineData(1.0, -30)]
    public void A_click_on_the_taskbar_pulls_the_menus_anchor_up_to_the_taskbars_edge(double scale, double expected)
    {
        var menu = AppTrayIcon.CreateMenu(AppTrayIcon.NoRunToStop, () => { });

        AppTrayIcon.KeepOffTheTaskbar(menu, At(2720, 1770), Laptop(scale));

        Assert.Equal(0, menu.HorizontalOffset);
        Assert.Equal(expected, menu.VerticalOffset, 9);
    }

    /// <summary>[tray menu above the taskbar] Kaydırma her açılışta yeniden yazılır: görev çubuğundaki bir tıkın kaydırması menüde kalıp
    /// sonraki, çalışma alanındaki tıkta menüyü tıktan uzağa açmaz.</summary>
    [StaFact]
    public void A_later_click_inside_the_work_area_drops_an_earlier_taskbar_shift()
    {
        var menu = AppTrayIcon.CreateMenu(AppTrayIcon.NoRunToStop, () => { });
        AppTrayIcon.KeepOffTheTaskbar(menu, At(2720, 1770), Laptop());
        Assert.NotEqual(0, menu.VerticalOffset); // ön-koşul: görev çubuğu tıkı kaydırdı

        AppTrayIcon.KeepOffTheTaskbar(menu, At(1200, 900), Laptop());

        Assert.Equal(0, menu.HorizontalOffset);
        Assert.Equal(0, menu.VerticalOffset);
    }

    /// <summary>[tray menu above the taskbar] Ekran okunamazsa menü yine açılır, tık noktasında: önceki bir kaydırma da kalmaz.</summary>
    [StaFact]
    public void An_unreadable_screen_opens_the_menu_at_the_click()
    {
        var menu = AppTrayIcon.CreateMenu(AppTrayIcon.NoRunToStop, () => { });
        AppTrayIcon.KeepOffTheTaskbar(menu, At(2720, 1770), Laptop());

        AppTrayIcon.KeepOffTheTaskbar(menu, At(2720, 1770), screen: null);

        Assert.Equal(0, menu.HorizontalOffset);
        Assert.Equal(0, menu.VerticalOffset);
    }

    /// <summary>[tray menu above the taskbar] Açılış noktası tık noktasının çalışma alanındaki en yakın noktasıdır — görev çubuğu hangi
    /// kenarda olursa olsun, ekran sol üst köşede olmasa da (ikinci ekran negatif koordinatta). Alanın sağ ve alt kenarı dışlayıcıdır
    /// (Win32 <c>RECT</c>): nokta tam kenara çekilir ve menü görev çubuğunun kenarına oturur.</summary>
    [Theory]
    [InlineData(2720, 1770, 0, 0, 2880, 1740, 2720, 1740)]          // altta görev çubuğu
    [InlineData(500, 20, 0, 48, 1920, 1032, 500, 48)]                // üstte
    [InlineData(20, 500, 60, 0, 1860, 1080, 60, 500)]                // solda
    [InlineData(1900, 500, 0, 0, 1860, 1080, 1860, 500)]             // sağda
    [InlineData(-500, 1050, -1920, 0, 1920, 1032, -500, 1032)]       // soldaki ikinci ekran, altta görev çubuğu
    [InlineData(1200, 900, 0, 0, 2880, 1740, 1200, 900)]             // alanın içinde: kendisi
    public void The_menus_anchor_is_the_nearest_point_of_the_work_area(
        int x, int y, int left, int top, int width, int height, int expectedX, int expectedY)
    {
        var anchor = AppTrayIcon.AnchorInWorkArea(At(x, y), new Int32Rect(left, top, width, height));

        Assert.Equal((expectedX, expectedY), (anchor.X, anchor.Y));
    }

    /// <summary>[kaynak] Menüyü açan TEK yol (<c>OpenMenu</c>) açılış noktasını menü açılmadan HEMEN önce çalışma alanına çeker. Kural
    /// kaynağın kendisinde pinlenir çünkü <c>OpenMenu</c> gerçek bir tepsi ikonu ister (headless süitte kurulamaz); gerçek açılışı
    /// <c>TrayMenuPositionProbeTests.The_tray_menu_stays_above_the_taskbar_when_the_click_is_on_it</c> ölçer.</summary>
    [Fact]
    public void The_menu_is_kept_off_the_taskbar_right_before_every_opening()
    {
        string source = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "Shell", "AppTrayIcon.cs"));

        Assert.Single(new Regex(@"KeepOffTheTaskbar\(menu,[^;]*\);\s*menu\.IsOpen = true;").Matches(source));
        Assert.Single(new Regex(@"menu\.IsOpen = true;").Matches(source));
    }

    // ---------------------------------------------------------------- kablo (kaynak)

    /// <summary>[kaynak] <c>MainWindow</c>, tepsiyi TAM BİR kez kurar ve Stop maddesine doğrudan
    /// <see cref="RunViewModel.StopCommand"/>'ı verir — action bar'ın Stop düğmesinin okuduğu AYNI örnek,
    /// yani maddenin etkin/pasif durumunun TEK kaynağı vardır; VM'in kendisini de verir, böylece etiketin kaynağı da
    /// (<see cref="RunViewModel.StopStage"/>) düğmeyle ortaktır. Kural kaynağın KENDİSİNDE pinlenir çünkü
    /// <see cref="AppTrayIcon"/> kurulamaz (ctor'u gerçek bir <c>TaskbarIcon</c> yaratır, headless süitte tepsi
    /// yoktur) — <c>TrayIndicatorBinderTests.Clicking_a_balloon_takes_the_same_restore_path_as_the_tray_icon</c>
    /// ile AYNI gerekçe/desen.</summary>
    [Fact]
    public void MainWindow_wires_the_tray_stop_item_to_the_run_view_models_stop_command()
    {
        string source = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "MainWindow.xaml.cs"));
        var wiring = new Regex(@"new AppTrayIcon\(_vm\.StopCommand, _vm\)");

        Assert.Single(wiring.Matches(source));
    }
}
