using System.IO;
using System.Text.RegularExpressions;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T6 · t1+t2] Açılış yolunun argümana bağlı dalları: <c>--autostart</c> (Windows ile açılış — [P4] Start
/// minimized to tray açıksa tepside, kapalıysa pencereyle),
/// <c>--font-ab</c> (dev kabuğu) ve TANINMAYAN argümanın yutulması. Bugüne kadar <c>App.OnStartup</c>'ın
/// <c>e.Args</c> ayrıştırmasına değen TEK test yoktu.
///
/// <para><b>Neden karar bir dikişte, <see cref="Application"/> kurulmadan ölçülüyor:</b> <c>App</c> headless
/// ayağa kaldırılamaz ve <c>OnStartup</c>'ın kendisi geri dönülemez makine-global yan etkiler üretir
/// (single-instance mutex'i, tepsi ikonu, global kısayol kaydı, registry autostart hizalaması). Karar bu yüzden
/// <see cref="StartupArgs"/>'a taşındı — <see cref="SecondInstanceGate"/> ile AYNI desen ve AYNI gerekçe.</para>
///
/// <para><b>KAPSAM DIŞI (bilinçli — makine-global durum değiştiren test YAZILMAZ, kullanıcı kararı):</b>
/// (a) <c>StartInTray</c>'in GERÇEKTEN tepsi ikonu kurduğu — <c>EnsureHandle</c> → <c>OnSourceInitialized</c>
/// gerçek bir shell notification ikonu yaratır, Alt+B global kısayolunu KAYDEDER ve DWM çağrıları yapar;
/// (b) registry autostart yazımı — o uzlaşma AYRI ve zaten pinli (<c>AutostartServiceTests</c>);
/// (c) single-instance mutex'inin gerçekten alındığı (<c>SingleInstanceTests</c> kendi kapsamında).
/// Bunlar T6 raporunun "artık liste"sindedir.</para>
/// </summary>
public class StartupPathTests
{
    // ---------------------------------------------------------------- t2: argüman dalları (saf karar)

    [Fact]
    public void An_unrecognised_argument_is_swallowed_and_leaves_the_normal_show_route()
    {
        // Tanınmayan argüman uygulamayı ÇÖKERTMEZ ve davranışını DEĞİŞTİRMEZ — normal açılış (pencere gösterilir).
        // `--it4a-lab` gerçek bir örnektir: T35'te kaldırılan lab kabuğunun argümanı (App.xaml.cs:62 notu).
        // [P4] Start minimized to tray AÇIKKEN bile: o ayar yalnız "Windows ile açıldım" işaretli açılışı etkiler.
        Assert.Equal(StartupRoute.ShowWindow, StartupArgs.Decide([], startMinimizedToTray: true));
        Assert.Equal(StartupRoute.ShowWindow, StartupArgs.Decide(["--it4a-lab"], startMinimizedToTray: true));
        Assert.Equal(StartupRoute.ShowWindow, StartupArgs.Decide([@"C:\src\OSYS\OSYS.sln", "-v", "--autostart-please"], startMinimizedToTray: true));
        // Argüman EŞLEŞMESİ tam metindir: benzeyen ama aynı olmayan bir bayrak dalı AÇMAZ.
        Assert.Equal(StartupRoute.ShowWindow, StartupArgs.Decide(["--autostartx"], startMinimizedToTray: true));
        Assert.Equal(StartupRoute.ShowWindow, StartupArgs.Decide(["--font-abx"], startMinimizedToTray: true));
        // [yayın hattı] Velopack kanca argümanları Program.Main'de tüketilip process biter; buraya ulaşsalar da yutulur.
        Assert.Equal(StartupRoute.ShowWindow, StartupArgs.Decide(["--veloapp-install", "1.8.0"], startMinimizedToTray: true));
        Assert.Equal(StartupRoute.ShowWindow, StartupArgs.Decide(["--veloapp-updated", "1.8.0"], startMinimizedToTray: false));
    }

    [Fact]
    public void The_font_ab_developer_shell_wins_over_every_other_route()
    {
        // [T65/K9] --font-ab dalı üretimde DI'dan ve single-instance kapısından ÖNCE döner (App.xaml.cs), yani
        // --autostart ile birlikte verilse bile (Start minimized to tray açık olsa bile) tepsi yolu HİÇ çalışmaz.
        // Önceliğin sahibi bu dikiştir.
        Assert.Equal(StartupRoute.FontAbSpike, StartupArgs.Decide([StartupArgs.FontAbArg], startMinimizedToTray: true));
        Assert.Equal(StartupRoute.FontAbSpike, StartupArgs.Decide([BuildOrchestrator.App.App.AutostartArg, StartupArgs.FontAbArg], startMinimizedToTray: true));
        Assert.Equal(StartupRoute.FontAbSpike, StartupArgs.Decide([StartupArgs.FontAbArg, BuildOrchestrator.App.App.AutostartArg], startMinimizedToTray: true));
        Assert.Equal("--font-ab", StartupArgs.FontAbArg); // otorite: App.xaml.cs'te bugüne dek inline duran literal
    }

    // ---------------------------------------------------------------- t1: autostart yolu

    /// <summary>[E2/T16 · P4] Registry autostart komutunun exe'ye eklediği argüman (<c>App.AutostartCommandFor</c>)
    /// "Windows ile açıldım" işaretidir — bu dal Windows oturum açılışında GERÇEKTEN koşan daldır. Pencere mi tepsi mi
    /// kararını ui-state.json'daki Start minimized to tray verir (tek doğruluk kaynağı; registry komutu değişmez).
    /// <para><b>[DEĞİŞEN KURAL — P4, kullanıcı kararı 2026-09-29]</b> Eski iddia
    /// (<c>The_autostart_argument_selects_the_tray_start_instead_of_showing_the_window</c>): <c>--autostart</c> HER
    /// ZAMAN tepsiye giderdi. Artık yalnız Start minimized to tray açıkken; kapalıyken Windows ile açılış pencereyi
    /// normal gösterir.</para></summary>
    [Fact]
    public void The_autostart_argument_starts_in_the_tray_only_when_start_minimized_is_on()
    {
        Assert.Equal("--autostart", BuildOrchestrator.App.App.AutostartArg);
        Assert.Equal(StartupRoute.StartInTray, StartupArgs.Decide([BuildOrchestrator.App.App.AutostartArg], startMinimizedToTray: true));
        // Diğer argümanların arasında da tanınır (Windows Run anahtarı exe yolundan sonra ekler).
        Assert.Equal(StartupRoute.StartInTray, StartupArgs.Decide(["--whatever", BuildOrchestrator.App.App.AutostartArg], startMinimizedToTray: true));
        // Ayar kapalıyken Windows ile açılış pencereyi gösterir.
        Assert.Equal(StartupRoute.ShowWindow, StartupArgs.Decide([BuildOrchestrator.App.App.AutostartArg], startMinimizedToTray: false));
    }

    /// <summary>[P4] Elle açılış (kısayol, exe) her zaman pencereyi gösterir — Start minimized to tray yalnız Windows
    /// ile başlamayı etkiler. (Mevcut davranışın pini.)</summary>
    [Fact]
    public void A_manual_start_always_shows_the_window()
    {
        Assert.Equal(StartupRoute.ShowWindow, StartupArgs.Decide([], startMinimizedToTray: true));
        Assert.Equal(StartupRoute.ShowWindow, StartupArgs.Decide([], startMinimizedToTray: false));
    }

    [Fact]
    public void App_startup_reads_its_arguments_through_the_single_seam_and_nowhere_else()
    {
        // KABLO: yukarıdaki saf kararlar, üretim onları GERÇEKTEN kullanmıyorsa hiçbir şey pinlemez. App headless
        // kurulamadığı için bağ KAYNAK üzerinden pinlenir (SourceGuard deseni): `e.Args` App ağacında TEK bir
        // yerde okunur ve o yer StartupArgs.Decide çağrısıdır. İkinci bir inline `e.Args.Contains(...)` dalı
        // (bugün kaldırılan hâl) bu testi kırar.
        var hits = SourceGuard.ScanApp("*.cs", new Regex(@"e\.Args"), skipCommentLines: true);
        string hit = Assert.Single(hits);
        Assert.StartsWith("App.xaml.cs:", hit, StringComparison.Ordinal);

        // [P4] Karar argümanla BİRLİKTE kalıcı ayarı da alır: pencere/tepsi kararının tek kaynağı ui-state.json'daki
        // Start minimized to tray'dir (eski iddia: çağrı yalnız e.Args taşırdı).
        string startup = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "App.xaml.cs"));
        Assert.Contains("StartupArgs.Decide(e.Args, ShellSwitches.StartMinimizedToTray(uiState))", startup, StringComparison.Ordinal);
        // Ve kararın İKİ tüketicisi de enum üzerinden gider (ham string karşılaştırması geri sızmasın).
        Assert.Contains("StartupRoute.FontAbSpike", startup, StringComparison.Ordinal);
        Assert.Contains("StartupRoute.StartInTray", startup, StringComparison.Ordinal);
        // Ön-koşul (vakum yasak): tarama gerçekten App.xaml.cs'i gördü.
        Assert.Contains("App.xaml.cs", SourceGuard.ScannedAppFiles("*.cs"));
    }

    /// <summary>[motor · Task 12] Kablo kaynak üzerinden pinlenir (App headless kurulamaz): motor DI'da, kontrol pencere
    /// gösterildikten SONRA başlar (açılış koreografisiyle yarışmaz), Restart isteği servise iner, OnExit motor
    /// kapandıktan sonra kurulumu başlatır.</summary>
    [Fact]
    public void The_update_engine_is_wired_after_the_window_shows_and_installs_on_exit()
    {
        string startup = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "App.xaml.cs"));
        Assert.Contains("new VelopackUpdater(UpdateFeed.CreateSource(", startup, StringComparison.Ordinal);
        Assert.Contains("UpdateFeed.SourceOverrideVariable", startup, StringComparison.Ordinal);
        Assert.Contains("UpdateFeed.PrereleaseVariable", startup, StringComparison.Ordinal);
        int show = startup.IndexOf("else window.Show();", StringComparison.Ordinal);
        int start = startup.IndexOf("GetRequiredService<UpdateService>().Start()", StringComparison.Ordinal);
        Assert.True(show > 0 && start > show, "UpdateService.Start() pencere gösterildikten sonra çağrılmalı.");
        Assert.Contains("RestartToUpdateRequested += (_, _) =>", startup, StringComparison.Ordinal);
        Assert.Contains(".RequestRestart()", startup, StringComparison.Ordinal);
        int dispose = startup.IndexOf("AppShutdown.WaitForAsyncDisposal(", StringComparison.Ordinal);
        int apply = startup.IndexOf("GetService<UpdateService>()?.ApplyOnExit()", StringComparison.Ordinal);
        Assert.True(dispose > 0 && apply > dispose, "ApplyOnExit motor kapandıktan sonra çağrılmalı.");
        // Kurulumu başlatma zincirinin (Update.exe yok, Process.Start atar) ya da servisin ilk kurulumunun atması çıkış
        // temizliğini atlamamalı: çağrı temizlik satırlarından SONRA, base.OnExit'ten hemen önce durur.
        int trayDispose = startup.IndexOf("_secondInstanceTray?.Dispose(); // [E2/triaj-f]", StringComparison.Ordinal);
        int singleDispose = startup.IndexOf("_singleInstance?.Dispose();", StringComparison.Ordinal);
        int baseExit = startup.IndexOf("base.OnExit(e);", StringComparison.Ordinal);
        Assert.True(trayDispose > 0 && singleDispose > 0 && baseExit > 0, "OnExit'in temizlik satırları bulunamadı.");
        Assert.True(apply > trayDispose && apply > singleDispose && apply < baseExit,
            "ApplyOnExit çıkış temizliğinden sonra, base.OnExit'ten önce çağrılmalı.");
    }

    // ---------------------------------------------------------------- t1: açılış seed'i motora komut göndermez

    [StaFact]
    public void A_remembered_repository_is_seeded_at_startup_without_sending_a_single_engine_command()
    {
        // [t1] Açılış repo'yu HATIRLAR (MainWindow.xaml.cs `_vm.RootPath = repo`) ve seed'in KENDİSİ motora hiçbir
        // komut göndermez — doğrudan RootPath set'i yalnız Empty→Boot sürer; Sync tetikleyen yol (Settings Save'in
        // kök değişimi) DEĞİLDİR. Açılışın Sync'i motor hazır olunca RunViewModel.OnEngineReady'den gider (bu testte
        // motor başlatılmaz; o kural RunViewModelTests.The_first_engine_ready_syncs_with_the_transcript'te pinli).
        //
        // Yol ÜRETİMDEKİ yoldur: kalıcı duruma gerçek store ile yazılır, pencere gerçek ctor'undan geçer.
        // Pencere Show() EDİLMEZ — bu testte de, autostart yolunda da (tepsi/hotkey yan etkisi yok).
        using var temp = new TempDir();
        var store = MainWindowHost.UiStateStore(temp);
        var saved = store.Load();
        saved.RepositoryRoot = @"C:\src\OSYS";
        store.Save(saved);

        var sent = new List<IpcCommand>();
        var (window, vm) = MainWindowHost.New(temp, beforeVm: v => v.DebugOnCommandSent = sent.Add);

        // Ön-koşullar: seed GERÇEKTEN koştu (aksi halde "komut gitmedi" iddiası vakum olurdu).
        Assert.Equal(@"C:\src\OSYS", vm.RootPath);
        Assert.Equal(AppPhase.Boot, vm.Phase);
        Assert.True(vm.HasWorkspace);

        // ASIL İDDİA: motora TEK bir komut bile gitmedi — Sync de, run da, envanter sorgusu da.
        Assert.Empty(sent);
        Assert.DoesNotContain(sent, c => c is SyncWorkspaceCommand or StartRunCommand);
        GC.KeepAlive(window);
    }
}

