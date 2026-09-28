using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [P3 · Task 3] Pencere kapanışının kabuk kablosu: × / Alt+F4 / sistem menüsü Kapat (üçü de
/// <c>MainWindow.OnClosing</c>'e iner) ve tepsi → Exit. Kararın kendisi saf <see cref="WindowCloseRule"/>'dadır
/// (<see cref="WindowCloseRuleTests"/>); burada pencerenin onu GERÇEKTEN uyguladığı ve VM'in güvenli çıkışına
/// (<see cref="RunViewModel.RequestExit"/>) bağlandığı pinlenir:
/// <list type="bullet">
/// <item>Close to tray açık (varsayılan) → × pencereyi tepsiye gizler, uygulama sürer.</item>
/// <item>Kapalı → × güvenli tam çıkıştır: uçuşta iş yoksa uygulama hemen kapanır; varsa derleme graceful durur,
/// pencere görünür kalır ve iş bitince kapanır. Bekleyişteki ikinci × ikinci bir durdurma üretmez.</item>
/// <item>Tepsi → Exit anahtardan bağımsız olarak HER ZAMAN güvenli çıkıştır; beklerken pencere öne gelir.</item>
/// </list>
///
/// <para>Harness: pencere GÖSTERİLMEZ (<see cref="MainWindowHost"/> — motor doğmaz, tepsi kurulmaz). Hiç gösterilmemiş
/// bir pencerede <c>Close()</c> WPF'in HWND'siz yolundan <c>OnClosing</c>'i doğrudan çağırır; iptal edilen kapanış
/// pencereyi yerinde bırakır. Uygulamayı kapatan ve pencereyi öne getiren çağrılar kabuğun iki dikişidir
/// (<see cref="MainWindow.ShutdownApplication"/>, <see cref="MainWindow.BringForward"/>) — her tetikten ÖNCE sayaçlı
/// sahtelerle değiştirilir, yani ne uygulama kapanır ne pencere görünür. Gönderim
/// <see cref="MainWindowHost.AcceptSends"/> ile kabul edilir, motorun cevabı <c>vm.OnEvent(...)</c> ile verilir.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public sealed class CloseToTrayTests
{
    /// <summary>Kabuk + VM + sayaçlı dikişler + giden komutlar.</summary>
    private sealed class Harness
    {
        public MainWindow Window { get; }
        public RunViewModel Vm { get; }
        public List<IpcCommand> Sent { get; } = [];
        public int Shutdowns { get; private set; }
        public int BroughtForward { get; private set; }

        /// <summary>Pencere GERÇEKTEN kapandı (<c>Closed</c>) — iptal edilen bir kapanış bunu yazmaz.</summary>
        public bool Closed { get; private set; }

        public Harness(MainWindow window, RunViewModel vm)
        {
            Window = window;
            Vm = vm;
            window.ShutdownApplication = () => Shutdowns++;
            window.BringForward = () => BroughtForward++;
            window.Closed += (_, _) => Closed = true;
            MainWindowHost.AcceptSends(vm);
            vm.DebugOnCommandSent = Sent.Add;
        }

        public static Harness New(TempDir temp, UiState? saved = null)
        {
            var (window, vm) = MainWindowHost.New(temp, saved: saved);
            return new Harness(window, vm);
        }

        /// <summary>Motor bir derlemeye başladı: koşu kilidi açık, Stop yapılabilir.</summary>
        public void StartBuild() => Vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));

        /// <summary>Uygulamanın kapanışını bekle — üretimde kapanış dispatcher kuyruğuna ertelenir.</summary>
        public void PumpUntilShutdown() => DispatcherPump.PumpUntil(() => Shutdowns > 0, TimeSpan.FromSeconds(2));
    }

    private static UiState CloseToTrayOff() => new() { CloseToTray = false };

    // ---------------------------------------------------------------- × — Close to tray açık (varsayılan)

    /// <summary>Anahtar hiç kaydedilmemiş (katalog varsayılanı: açık): × kapanışı iptal eder ve pencereyi tepsiye
    /// gizler — uygulama kapanmaz, çıkış istenmez (bugünkü davranış, K5).</summary>
    [StaFact]
    public void By_default_the_close_button_hides_to_the_tray()
    {
        using var temp = new TempDir();
        var h = Harness.New(temp);
        Assert.Equal(Visibility.Collapsed, h.Window.Visibility); // ön-koşul: pencere hiç gösterilmedi

        h.Window.Close();

        Assert.False(h.Closed);
        Assert.Equal(Visibility.Hidden, h.Window.Visibility);
        Assert.Equal(0, h.Shutdowns);
        Assert.False(h.Vm.ExitPending);
        Assert.Empty(h.Sent);
    }

    // ---------------------------------------------------------------- × — Close to tray kapalı

    /// <summary>Close to tray kapalı ve uçuşta iş yok: × uygulamayı kapatır — bir kez. Ne bekleyiş açılır ne motora
    /// komut gider; pencere tepsiye inmez.</summary>
    [StaFact]
    public void With_close_to_tray_off_the_close_button_quits_when_nothing_is_in_flight()
    {
        using var temp = new TempDir();
        var h = Harness.New(temp, CloseToTrayOff());

        h.Window.Close();
        h.PumpUntilShutdown();

        Assert.Equal(1, h.Shutdowns);
        Assert.NotEqual(Visibility.Hidden, h.Window.Visibility);
        Assert.False(h.Vm.ExitPending);
        Assert.Empty(h.Sent);
    }

    /// <summary>Close to tray kapalı ve bir derleme koşuyor: × derlemeyi graceful durdurur (TEK <c>stopRun</c>) ve
    /// uygulama drain'i bekler — pencere ne kapanır ne tepsiye iner. Bekleyişteki ikinci × ikinci bir durdurma
    /// üretmez; motor koşuyu kapatınca uygulama bir kez kapanır.</summary>
    [StaFact]
    public void With_close_to_tray_off_the_close_button_waits_for_a_running_build()
    {
        using var temp = new TempDir();
        var h = Harness.New(temp, CloseToTrayOff());
        h.StartBuild();

        h.Window.Close();

        Assert.Equal(new StopRunCommand("r1", StopKind.Graceful), Assert.Single(h.Sent));
        Assert.True(h.Vm.ExitPending);
        Assert.Equal(0, h.Shutdowns);
        Assert.False(h.Closed);
        Assert.NotEqual(Visibility.Hidden, h.Window.Visibility);

        h.Window.Close(); // bekleyişte ikinci ×

        Assert.Single(h.Sent.OfType<StopRunCommand>());
        Assert.Equal(0, h.Shutdowns);
        Assert.False(h.Closed);
        Assert.NotEqual(Visibility.Hidden, h.Window.Visibility);

        h.Vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));
        h.PumpUntilShutdown();

        Assert.Equal(1, h.Shutdowns);
    }

    // ---------------------------------------------------------------- tepsi → Exit

    /// <summary>Tepsi → Exit HER ZAMAN güvenli çıkıştır — Close to tray açıkken de (varsayılan). Bir derleme koşuyorsa
    /// graceful durur ve bekleyiş sürerken pencere ÖNE gelir (bir kez): gizli bir pencerede bekleyen çıkış "hiçbir şey
    /// olmuyor" gibi görünürdü. Drain bitince uygulama kapanır.</summary>
    [StaFact]
    public void The_tray_exit_brings_the_window_forward_while_it_waits()
    {
        using var temp = new TempDir();
        var h = Harness.New(temp);
        h.StartBuild();

        h.Window.ExitFromTray();

        Assert.Equal(1, h.BroughtForward);
        Assert.Equal(0, h.Shutdowns);
        Assert.Equal(new StopRunCommand("r1", StopKind.Graceful), Assert.Single(h.Sent));

        h.Vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));
        h.PumpUntilShutdown();

        Assert.Equal(1, h.Shutdowns);
        Assert.Equal(1, h.BroughtForward);
    }

    /// <summary>Uçuşta iş yokken tepsi → Exit uygulamayı hemen kapatır — pencere GÖSTERİLMEDEN.</summary>
    [StaFact]
    public void The_tray_exit_quits_without_showing_the_window_when_idle()
    {
        using var temp = new TempDir();
        var h = Harness.New(temp);

        h.Window.ExitFromTray();
        h.PumpUntilShutdown();

        Assert.Equal(0, h.BroughtForward);
        Assert.Equal(1, h.Shutdowns);
        Assert.Empty(h.Sent);
    }

    /// <summary>[design v1.11.0 §3.1] Açılış koreografisi oynarken (koşu komutu henüz GİTMEDİ) tepsi → Exit: bekleyiş
    /// açılır ve AYNI çağrıda biter — Stop bekleyen koşuyu senkron geri alır ve <c>ExitReady</c>
    /// <c>RequestExit</c>'in içinden gelir. <c>ExitPending</c> geri dönmediği için yalnız ona bakan bir kabuk pencereyi
    /// kapanıştan hemen önce bir kare için öne getirirdi; kapanış zaten başladıysa getirilmez. Uygulama bir kez kapanır
    /// ve motora ne <c>startRun</c> ne <c>stopRun</c> gider — koreografi sonradan bitse de.</summary>
    [StaFact]
    public async Task The_tray_exit_does_not_flash_the_window_when_the_wait_ends_at_once()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(temp, ("Alpha", null));
        var h = new Harness(window, vm);
        var choreography = new TaskCompletionSource();
        vm.OperationChoreography = _ => choreography.Task;
        var build = vm.BuildCommand.ExecuteAsync(null);
        Assert.True(vm.IsStarting); // ön-koşul: işlem başladı, komut henüz gitmedi

        window.ExitFromTray();
        h.PumpUntilShutdown();

        Assert.True(vm.ExitPending); // bekleyiş açıldı ve aynı çağrıda bitti
        Assert.Equal(1, h.Shutdowns);
        Assert.Equal(0, h.BroughtForward);

        choreography.SetResult();
        await build;

        Assert.Empty(h.Sent);
        Assert.Equal(1, h.Shutdowns);
        Assert.Equal(0, h.BroughtForward);
    }

    // ---------------------------------------------------------------- kablo (kaynak)

    /// <summary>[kaynak] Tepsi menüsündeki Exit'in TEK aboneliği güvenli çıkıştır (<c>ExitFromTray</c>) ve MainWindow'da
    /// uygulamayı kapatan TEK çağrı yeri vardır (<see cref="MainWindow.ShutdownApplication"/>'ın üretim değeri): her tam
    /// çıkış <c>ExitReady</c> → <c>ExitNow</c> yolundan geçer, bekleyişi atlayan ikinci bir kapanış yolu yazılamaz.
    /// Kural kaynakta pinlenir çünkü tepsi <c>OnSourceInitialized</c>'da (HWND) kurulur ve <see cref="AppTrayIcon"/>
    /// headless süitte kurulamaz — <c>TrayMenuTests.MainWindow_wires_the_tray_stop_item_to_the_run_view_models_stop_command</c>
    /// ile AYNI gerekçe/desen. Yorum satırları sayılmaz: kuralı ANLATAN doküman, onu ihlal eden kod değildir.</summary>
    [Fact]
    public void MainWindow_wires_the_tray_exit_item_to_the_safe_exit()
    {
        const string relative = "MainWindow.xaml.cs";
        string source = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, relative));

        Assert.Single(SourceGuard.ScanText(relative, source, new Regex(@"ExitRequested\s*\+="), skipCommentLines: true));
        Assert.Single(SourceGuard.ScanText(relative, source,
            new Regex(@"_tray\.ExitRequested\s*\+=\s*ExitFromTray\s*;"), skipCommentLines: true));
        Assert.Single(SourceGuard.ScanText(relative, source, new Regex(@"\.Shutdown\("), skipCommentLines: true));
    }
}
