using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [ui gap cleanup/Task 3] Tepsi menüsündeki Stop maddesi <see cref="RunViewModel.StopCommand"/>'a bağlıdır —
/// etkinliği doğrudan <c>CanExecute</c>'tan gelir, ActionBar'daki Stop düğmesiyle AYNI kaynak (bkz.
/// <c>ActionBarTests.The_stop_button_reads_stopping_and_goes_disabled_while_the_run_drains</c>). Eskiden madde
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
