using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [P3 · Task 3 · Exit] Pencere kapanışının kabuk kablosu: × / Alt+F4 / sistem menüsü Kapat (üçü de
/// <c>MainWindow.OnClosing</c>'e iner) ve tepsi → Exit. Kararın kendisi saf <see cref="WindowCloseRule"/>'dadır
/// (<see cref="WindowCloseRuleTests"/>); burada pencerenin onu GERÇEKTEN uyguladığı ve Exit butonunun kararına bağlandığı pinlenir:
/// <list type="bullet">
/// <item>Close to tray açık (varsayılan) → × pencereyi tepsiye gizler, uygulama sürer.</item>
/// <item>Kapalı → × Exit butonu gibi davranır: uçuşta iş yoksa uygulama hemen kapanır; varsa hiçbir şey durdurulmaz, pencere öne gelir
/// ve onay kartı açılır (kartın kendisi <see cref="ExitCardTests"/>'te).</item>
/// <item>Tepsi → Exit anahtardan bağımsız olarak AYNI karardır — × ile aynı kabuk yolu.</item>
/// <item>Kartın "Stop and exit"i çıkışı başlattıktan sonra gelen × pencereyi yerinde tutar, ikinci bir şey istemez.</item>
/// </list>
///
/// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-19 · Exit]</b> ESKİ İDDİA: × (anahtar kapalıyken) ve tepsi → Exit güvenli tam çıkışı
/// DOĞRUDAN isterdi — uçuştaki iş beklenir, bekleyiş sürerken pencere öne gelirdi (<c>WindowCloseRule.ShouldBringForward</c>). Tasarım:
/// "Kapalıysa × de Exit gibi davranır: hiçbir iş yoksa hemen kapanır, iş varsa onay kartını açar"; tepsi → Exit de aynı karara bağlandı
/// (kullanıcı onayı aynı gün). Pencere artık bekleyişi göstermek için değil, kartı göstermek için öne gelir.</para>
///
/// <para>Harness: pencere GÖSTERİLMEZ (<see cref="MainWindowHost"/> — motor doğmaz, tepsi kurulmaz). Hiç gösterilmemiş
/// bir pencerede <c>Close()</c> WPF'in HWND'siz yolundan <c>OnClosing</c>'i doğrudan çağırır; iptal edilen kapanış
/// pencereyi yerinde bırakır. Uygulamayı kapatan ve onay için pencereyi öne getiren çağrılar kabuğun iki dikişidir
/// (<see cref="MainWindow.ShutdownApplication"/>, <see cref="MainWindow.ConfirmExitInFront"/>) — her tetikten ÖNCE sayaçlı
/// sahtelerle değiştirilir, yani ne uygulama kapanır ne pencere görünür. Sahteler SENKRONDUR: üretim değerlerinin
/// dispatcher'a ertelemesi burada gözlenmez (öne getirmeninki kaynakta pinlidir; ertelenen gövdenin kararları
/// <see cref="MainWindow.ConfirmExitInFrontNow"/> ile doğrudan sürülür). Kapanış ekranının zamanı sahtedir. Gönderim
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
        public int Confirmations { get; private set; }

        /// <summary>Pencere GERÇEKTEN kapandı (<c>Closed</c>) — iptal edilen bir kapanış bunu yazmaz.</summary>
        public bool Closed { get; private set; }

        public Harness(MainWindow window, RunViewModel vm)
        {
            Window = window;
            Vm = vm;
            window.ShutdownApplication = () => Shutdowns++;
            window.ConfirmExitInFront = () => Confirmations++;
            window.ExitCardTimer = new FakePollTimer();
            window.TakeProcessCensus = () => [];
            window.ClosingOverlay.Timer = new FakePollTimer();
            window.Closed += (_, _) => Closed = true;
            MainWindowHost.AcceptSends(vm);
            vm.DebugOnCommandSent = Sent.Add;
        }

        public static Harness New(TempDir temp, UiState? saved = null)
        {
            var (window, vm) = MainWindowHost.New(temp, saved: saved);
            return new Harness(window, vm);
        }

        /// <summary>Kapanış çağrısının kaydını bekle. Sahte SENKRONDUR: sayaç tetiğin kendi çağrısı içinde artar ve
        /// üretimdeki erteleme (<c>Dispatcher.BeginInvoke</c>) burada hiç yoktur — bu testler onu gözlemez. Pompa
        /// yalnız VM'in <c>ExitReady</c>'yi sonraki bir dispatcher turuna bırakacağı bir değişikliğe karşı bekler.</summary>
        public void PumpUntilShutdown() => DispatcherPump.PumpUntil(() => Shutdowns > 0, TimeSpan.FromSeconds(2));

        /// <summary>Exit butonu → kart → "Stop and exit": çıkış uçuştaki işi durdurmaya başladı.</summary>
        public void StopAndExitFromTheCard()
        {
            CommandPress.Click(Window.ExitButton);
            Assert.True(DsChrome.GetIsActive(Window.ExitButton), "ön-koşul: kart açılmadı");
            CommandPress.Click(Window.ExitCardView.PART_Action);
            Assert.True(Vm.ExitPending, "ön-koşul: çıkış başlamadı");
        }
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
    /// komut gider; pencere tepsiye inmez ve onay istenmez.</summary>
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
        Assert.Equal(0, h.Confirmations);
    }

    /// <summary>Close to tray kapalı ve bir derleme koşuyor: × Exit butonu gibi davranır — hiçbir şey durdurulmaz, pencere kapanmaz ve
    /// tepsiye inmez; onay için pencere öne gelir ve kart açılır (bir kez). Görev çubuğundan küçültülmüş hâlde kapatılan bir pencere aksi
    /// hâlde kartı gösteremezdi.
    /// <para><b>[DEĞİŞEN KURAL]</b> ESKİ: × derlemeyi durdurur ve çıkış bekleyişi sürerken pencere öne gelirdi (bkz. tip özeti). KIRMIZI
    /// (değişiklikten önce): stopRun gidiyor ve bekleyiş açılıyordu.</para></summary>
    [StaFact]
    public void With_close_to_tray_off_the_close_button_asks_before_stopping_a_running_build()
    {
        using var temp = new TempDir();
        var h = Harness.New(temp, CloseToTrayOff());
        MainWindowHost.StartBuild(h.Vm);

        h.Window.Close();

        Assert.Equal(1, h.Confirmations);
        Assert.False(h.Vm.ExitPending);
        Assert.Empty(h.Sent);
        Assert.Equal(0, h.Shutdowns);
        Assert.False(h.Closed);
        Assert.NotEqual(Visibility.Hidden, h.Window.Visibility);
    }

    /// <summary>Kartın "Stop and exit"i çıkışı başlattıktan sonra gelen × — anahtar ne olursa olsun — pencereyi yerinde tutar: tepsiye
    /// inmez (kapanmakta olan uygulama görünmeden sürerdi), kapanmaz, ikinci bir durdurma ya da onay istemez
    /// (<see cref="WindowCloseRule"/>'un Stay dalı — kabuk karara VM'in <c>ExitPending</c>'ini gerçekten verir).</summary>
    [StaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_close_while_the_exit_stops_the_work_keeps_the_window_and_asks_nothing(bool closeToTray)
    {
        using var temp = new TempDir();
        var h = Harness.New(temp, new UiState { CloseToTray = closeToTray });
        MainWindowHost.StartBuild(h.Vm);
        h.StopAndExitFromTheCard();

        h.Window.Close();

        Assert.False(h.Closed);
        Assert.NotEqual(Visibility.Hidden, h.Window.Visibility);
        Assert.Single(h.Sent.OfType<StopRunCommand>());
        Assert.Equal(0, h.Confirmations);
        Assert.Equal(0, h.Shutdowns);
    }

    /// <summary>[final review · F3] Close to tray her kapatmada TAZE okunur: pencere varsayılanla (açık) kurulduktan
    /// SONRA pencerenin kullandığı AYNI dosyaya kapalı kaydedilir (Settings'in Save'i gibi) ve bir sonraki × artık
    /// tepsiye gizlemez, uygulamayı kapatır. Değer kurulumda bir kopyaya alınsaydı × hâlâ tepsiye inerdi.</summary>
    [StaFact]
    public void A_close_to_tray_value_saved_after_the_window_opened_applies_to_the_next_close()
    {
        using var temp = new TempDir();
        var h = Harness.New(temp);
        var store = MainWindowHost.UiStateStore(temp);
        var state = store.Load();
        state.CloseToTray = false;
        store.Save(state);

        h.Window.Close();
        h.PumpUntilShutdown();

        Assert.Equal(1, h.Shutdowns);
        Assert.NotEqual(Visibility.Hidden, h.Window.Visibility);
    }

    // ---------------------------------------------------------------- tepsi → Exit

    /// <summary>Tepsi → Exit Close to tray'e bakmaz ve × ile AYNI karardır: bir derleme koşuyorsa hiçbir şey durdurulmaz, onay için
    /// pencere öne gelir ve kart açılır (bir kez) — tepsideki gizli pencere kartı gösteremezdi.
    /// <para><b>[DEĞİŞEN KURAL]</b> ESKİ: tepsi → Exit derlemeyi durdurur, pencereyi öne getirir ve iş bitince kapatırdı. KIRMIZI
    /// (değişiklikten önce): stopRun gidiyordu.</para></summary>
    [StaFact]
    public void The_tray_exit_asks_before_stopping_a_running_build()
    {
        using var temp = new TempDir();
        var h = Harness.New(temp);
        MainWindowHost.StartBuild(h.Vm);

        h.Window.ExitFromTray();

        Assert.Equal(1, h.Confirmations);
        Assert.Empty(h.Sent);
        Assert.False(h.Vm.ExitPending);
        Assert.Equal(0, h.Shutdowns);
    }

    /// <summary>[final review · F4] Çıkış başladıktan sonra (uçuşta iş yok → tepsi → Exit, kapanış kuyrukta) gelen
    /// <c>Closing</c> — Shutdown'ın pencereyi kapatışı — pencereyi GERÇEKTEN kapatır: Close to tray açık olsa da
    /// tepsiye gizlemez ve ilk-kapatma balonunu harcamaz. Gerçek kapanışın işareti kurulmasaydı anahtar söz alırdı:
    /// pencere gizlenir ve kapanış sırasında "tepsiye küçüldü" bilgilendirmesi yanmış sayılırdı. Burada kapanışı
    /// Shutdown'ın yerine düz bir <c>Close()</c> tetikler (kabuk testleri uygulamayı kapatmaz).</summary>
    [StaFact]
    public void Once_the_exit_has_started_the_shutdowns_own_close_really_closes_the_window()
    {
        using var temp = new TempDir();
        var h = Harness.New(temp);
        h.Window.ExitFromTray();
        h.PumpUntilShutdown();
        Assert.Equal(1, h.Shutdowns); // ön-koşul: çıkış başladı, kapanış kuyrukta

        h.Window.Close();

        Assert.True(h.Closed);
        Assert.NotEqual(Visibility.Hidden, h.Window.Visibility);
        Assert.False(MainWindowHost.UiStateStore(temp).Load().TrayBalloonShown);
    }

    /// <summary>Uçuşta iş yokken tepsi → Exit uygulamayı hemen kapatır — pencere GÖSTERİLMEDEN, onay istenmeden.</summary>
    [StaFact]
    public void The_tray_exit_quits_without_showing_the_window_when_idle()
    {
        using var temp = new TempDir();
        var h = Harness.New(temp);

        h.Window.ExitFromTray();
        h.PumpUntilShutdown();

        Assert.Equal(0, h.Confirmations);
        Assert.Equal(1, h.Shutdowns);
        Assert.Empty(h.Sent);
    }

    /// <summary>[design v1.11.0 §3.1] Açılış koreografisi oynarken (koşu komutu henüz GİTMEDİ) koşu yine de uçuştaki iştir: tepsi → Exit
    /// onay ister; hiçbir şey geri alınmaz, motora <c>stopRun</c> gitmez ve uygulama kapanmaz.
    /// <para><b>[DEĞİŞEN KURAL]</b> ESKİ: tepsi → Exit bekleyen koşuyu Stop'un marking kuralıyla geri alır ve uygulama AYNI çağrıda kapanırdı
    /// (pencere bir kare için öne gelmezdi). Geri alma artık kartın "Stop and exit"indedir (<c>SafeExitTests</c>).</para></summary>
    [StaFact]
    public async Task The_tray_exit_during_the_opening_choreography_asks_too()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(temp, ("Alpha", null));
        var h = new Harness(window, vm);
        var choreography = new TaskCompletionSource();
        vm.OperationChoreography = _ => choreography.Task;
        var build = vm.BuildCommand.ExecuteAsync(null);
        Assert.True(vm.IsStarting); // ön-koşul: işlem başladı, komut henüz gitmedi

        window.ExitFromTray();

        Assert.Equal(1, h.Confirmations);
        Assert.False(vm.ExitPending);
        Assert.Equal(0, h.Shutdowns);

        choreography.SetResult();
        await build;
        Assert.Empty(h.Sent.OfType<StopRunCommand>());
        Assert.Equal(0, h.Shutdowns);
    }

    // ---------------------------------------------------------------- ertelenen onay

    /// <summary>Onay pencereyi kendi dispatcher turunda öne getirir (× yolunda çağrı <c>OnClosing</c>'in içindedir); o tur kararı yeniden
    /// sorar: iş arada bittiyse Exit butonunun kuralı geçerlidir — uygulama kartsız, pencere gösterilmeden kapanır.</summary>
    [StaFact]
    public void The_deferred_confirmation_quits_if_the_work_ended_meanwhile()
    {
        using var temp = new TempDir();
        var h = Harness.New(temp);
        MainWindowHost.StartBuild(h.Vm);
        h.Window.ExitFromTray();
        h.Vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));
        h.Vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 0, 0, 0, 1, 400));

        h.Window.ConfirmExitInFrontNow();
        h.PumpUntilShutdown();

        Assert.Equal(1, h.Shutdowns);
        Assert.Equal(Visibility.Collapsed, h.Window.Visibility); // pencere gösterilmedi
    }

    /// <summary>Çıkış arada başladıysa (kartın "Stop and exit"i) ertelenen tur hiçbir şey yapmaz: pencere öne getirilmez, kart açılmaz.</summary>
    [StaFact]
    public void The_deferred_confirmation_does_nothing_once_the_exit_is_under_way()
    {
        using var temp = new TempDir();
        var h = Harness.New(temp);
        MainWindowHost.StartBuild(h.Vm);
        h.StopAndExitFromTheCard();

        h.Window.ConfirmExitInFrontNow();

        Assert.Equal(Visibility.Collapsed, h.Window.Visibility);
        Assert.False(DsChrome.GetIsActive(h.Window.ExitButton));
        Assert.Single(h.Sent.OfType<StopRunCommand>());
    }

    // ---------------------------------------------------------------- tam çıkış: konsol geçmişi diske

    /// <summary>[idle-memory · dalga 2 · spec §6.3 · inceleme M2] <b>Tam çıkış konsol geçmişinin kuyruğunu diske indirir.</b> Koşunun
    /// anlatısı koşu klasöründeki <c>narrative.log</c>'a yayılır; yazıcı bölüm boyunca açık kalır ve tamponunu işlem sonlarında boşaltır.
    /// Son işlemden SONRA, işlem dışında eklenen bir satır (burada motorun koşuyu bitirmeyen bir hatası) tamponda bekler — süreç
    /// kapanırken sonlandırıcı koşmadığı için dosyaya hiç inmezdi. Pencere her tam çıkışta gerçekten kapanır: kapanış kuyruğu boşaltır.</summary>
    [StaFact]
    public void A_full_exit_writes_the_console_history_tail_to_the_run_folder()
    {
        using var temp = new TempDir();
        var h = Harness.New(temp);
        using var logs = new SpoolDir(h.Vm.Narrative);
        MainWindowHost.StartBuildWithLogDirectory(h.Vm, logs.Path, "A");
        MainWindowHost.LogLine(h.Vm, "A", 1, "a line of the run");
        MainWindowHost.FinishBuild(h.Vm, "A");                              // koşunun sonu tamponu boşaltır
        h.Vm.OnEvent(new ErrorEvent("badCommand", "a line after the run")); // işlem dışında bir satır
        Assert.Equal("a line of the run\n", logs.ReadText());              // ön-koşul: koşu diskte, son satır tamponda

        h.Window.ExitFromTray();
        h.Window.Close();
        Assert.True(h.Closed); // ön-koşul: pencere gerçekten kapandı

        Assert.Equal(h.Vm.GetRunDocumentText(), logs.ReadText()); // KIRMIZI: son satır dosyaya hiç inmezdi
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

    /// <summary>[kaynak · dalga 2 son inceleme · defter 1] Pencere son kez kapanırken konsol geçmişinin kuyruğunu diske indirmek
    /// (<c>_vm.Narrative.Flush()</c>) <c>OnClosed</c>'un İLK deyimidir: ondan önce koşan bir bırakma (kısayol kaydı, tepsi simgesi, HEAD
    /// izleyicisi) fırlatsaydı boşaltma hiç koşmaz, son satırlar dosyaya inmezdi. Boşaltma kendisi fırlatmaz (store her hatayı yutar).
    /// Kural kaynakta pinlenir: bırakmaların fırlatmasını sağlayacak bir dikiş yoktur ve tepsi headless süitte kurulmaz —
    /// <see cref="MainWindow_wires_the_tray_exit_item_to_the_safe_exit"/> ile AYNI desen; aradaki yorum satırları sayılmaz.
    /// <para>KIRMIZI: boşaltma, kısayol ve tepsi bırakmalarından, HEAD izleyicisinden ve yoklama zamanlayıcısından SONRA geliyordu.</para></summary>
    [Fact]
    public void MainWindow_writes_the_console_history_tail_first_when_it_closes()
    {
        const string relative = "MainWindow.xaml.cs";
        string source = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, relative));

        Assert.Single(SourceGuard.ScanText(relative, source,
            new Regex(@"protected override void OnClosed\(EventArgs e\)\s*\{(?:\s*//[^\n]*)*\s*_vm\.Narrative\.Flush\(\);")));
    }

    /// <summary>[kaynak · final review F7 · Exit] Onay için pencereyi öne getiren çağrının (<see cref="MainWindow.ConfirmExitInFront"/>) TEK
    /// üretim değeri işini kendi dispatcher turuna ERTELER: × yolunda çağrı <c>OnClosing</c>'in içinden gelir ve WPF, kapanış sürerken
    /// <c>Show</c>'u InvalidOperationException ile reddeder — senkron bir <c>ShowFromTray</c> derleme sürerken basılan her ×'ı çökertirdi.
    /// Headless süit bunu göremez (dikiş sayaçlı bir sahteyle değiştirilir, pencere gösterilmez); kural bu yüzden kaynakta pinlenir —
    /// <see cref="MainWindow_wires_the_tray_exit_item_to_the_safe_exit"/> ile AYNI desen.
    /// <para><b>[DEĞİŞEN KURAL — Exit]</b> ESKİ: dikişin adı <c>BringForward</c>'dı ve bekleyen bir çıkışı göstermek için pencereyi öne
    /// getirirdi; artık onay kartını göstermek için getirir (bkz. tip özeti). Erteleme kuralı aynıdır.</para></summary>
    [Fact]
    public void MainWindow_defers_the_exit_confirmation_out_of_the_closing_handler()
    {
        const string relative = "MainWindow.Exit.cs";
        string source = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, relative));

        Assert.Single(SourceGuard.ScanText(relative, source, new Regex(@"\bConfirmExitInFront\s*=(?!=)"), skipCommentLines: true));
        Assert.Single(SourceGuard.ScanText(relative, source,
            new Regex(@"\bConfirmExitInFront\s*=\s*\(\)\s*=>\s*Dispatcher\.BeginInvoke\("), skipCommentLines: true));
    }
}
