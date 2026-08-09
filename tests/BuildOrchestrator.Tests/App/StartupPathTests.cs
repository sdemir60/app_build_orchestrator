using System.IO;
using System.Text.RegularExpressions;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T6 · t1+t2] Açılış yolunun argümana bağlı dalları: <c>--autostart</c> (tepside sessiz başlangıç),
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
        Assert.Equal(StartupRoute.ShowWindow, StartupArgs.Decide([]));
        Assert.Equal(StartupRoute.ShowWindow, StartupArgs.Decide(["--it4a-lab"]));
        Assert.Equal(StartupRoute.ShowWindow, StartupArgs.Decide([@"C:\src\OSYS\OSYS.sln", "-v", "--autostart-please"]));
        // Argüman EŞLEŞMESİ tam metindir: benzeyen ama aynı olmayan bir bayrak dalı AÇMAZ.
        Assert.Equal(StartupRoute.ShowWindow, StartupArgs.Decide(["--autostartx"]));
        Assert.Equal(StartupRoute.ShowWindow, StartupArgs.Decide(["--font-abx"]));
    }

    [Fact]
    public void The_font_ab_developer_shell_wins_over_every_other_route()
    {
        // [T65/K9] --font-ab dalı üretimde DI'dan ve single-instance kapısından ÖNCE döner (App.xaml.cs), yani
        // --autostart ile birlikte verilse bile tepsi yolu HİÇ çalışmaz. Önceliğin sahibi artık bu dikiştir.
        Assert.Equal(StartupRoute.FontAbSpike, StartupArgs.Decide([StartupArgs.FontAbArg]));
        Assert.Equal(StartupRoute.FontAbSpike, StartupArgs.Decide([BuildOrchestrator.App.App.AutostartArg, StartupArgs.FontAbArg]));
        Assert.Equal(StartupRoute.FontAbSpike, StartupArgs.Decide([StartupArgs.FontAbArg, BuildOrchestrator.App.App.AutostartArg]));
        Assert.Equal("--font-ab", StartupArgs.FontAbArg); // otorite: App.xaml.cs'te bugüne dek inline duran literal
    }

    // ---------------------------------------------------------------- t1: autostart yolu

    [Fact]
    public void The_autostart_argument_selects_the_tray_start_instead_of_showing_the_window()
    {
        // [E2/T16] Registry autostart komutunun exe'ye eklediği argüman (App.AutostartCommand) budur — yani bu
        // dal Windows oturum açılışında GERÇEKTEN koşan daldır.
        Assert.Equal("--autostart", BuildOrchestrator.App.App.AutostartArg);
        Assert.Equal(StartupRoute.StartInTray, StartupArgs.Decide([BuildOrchestrator.App.App.AutostartArg]));
        // Diğer argümanların arasında da tanınır (Windows Run anahtarı exe yolundan sonra ekler).
        Assert.Equal(StartupRoute.StartInTray, StartupArgs.Decide(["--whatever", BuildOrchestrator.App.App.AutostartArg]));
        // Ve yalnız o dal tepsiye gider: normal açılış pencere gösterir.
        Assert.NotEqual(StartupRoute.StartInTray, StartupArgs.Decide([]));
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

        string startup = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "App.xaml.cs"));
        Assert.Contains("StartupArgs.Decide(e.Args)", startup, StringComparison.Ordinal);
        // Ve kararın İKİ tüketicisi de enum üzerinden gider (ham string karşılaştırması geri sızmasın).
        Assert.Contains("StartupRoute.FontAbSpike", startup, StringComparison.Ordinal);
        Assert.Contains("StartupRoute.StartInTray", startup, StringComparison.Ordinal);
        // Ön-koşul (vakum yasak): tarama gerçekten App.xaml.cs'i gördü.
        Assert.Contains("App.xaml.cs", SourceGuard.ScannedAppFiles("*.cs"));
    }

    // ---------------------------------------------------------------- t1: autostart SESSİZ başlar (Sync YOK)

    [StaFact]
    public void A_remembered_repository_is_seeded_at_startup_without_sending_a_single_engine_command()
    {
        // [t1 · asıl değer] Autostart yolunun tek anlamlı riski budur: oturum açılışında SESSİZCE bir Sync/build
        // başlatmak kullanıcının makinesini yorar. Üretimde açılış repo'yu HATIRLAR (MainWindow.xaml.cs:126
        // `_vm.RootPath = repo`) ama SEED-BUT-IDLE'dır — doğrudan RootPath set'i yalnız Empty→Boot sürer,
        // ChangeRepositoryAsync (Sync tetikleyen yol) DEĞİLDİR.
        //
        // Yol ÜRETİMDEKİ yoldur: kalıcı duruma gerçek store ile yazılır, pencere gerçek ctor'undan geçer.
        // Pencere Show() EDİLMEZ — bu testte de, autostart yolunda da (tepsi/hotkey yan etkisi yok).
        using var temp = new TempDir();
        var store = new JsonUiStateStore(Path.Combine(temp.Path, "ui-state.json"));
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

    /// <summary>[Task 11] Kill switch açılışta kalıcı durumdan SEED edilir — diğer iş akışı tercihleriyle
    /// (repo/config/branch/worktree/perf) AYNI blokta. Seed olmasaydı anahtar her açılışta kendiliğinden
    /// ürün varsayılanına (AÇIK) döner, kullanıcının kapatma kararı sessizce kaybolurdu.
    /// <para>Kontrol grubu ikinci iddiadadır: hiç yazılmamış bir kalıcı durumda VM AÇIK kalır — yani birinci
    /// iddia "VM zaten hep false" gibi önemsiz bir sebeple geçmiyor.</para></summary>
    [StaFact]
    public void The_persisted_build_dependency_cycles_switch_is_seeded_at_startup()
    {
        using var off = new TempDir();
        var store = new JsonUiStateStore(Path.Combine(off.Path, "ui-state.json"));
        var saved = store.Load();
        saved.BuildDependencyCycles = false;
        store.Save(saved);

        var (window, vm) = MainWindowHost.New(off);
        Assert.False(vm.BuildDependencyCycles);
        GC.KeepAlive(window);

        using var fresh = new TempDir();   // hiç yazılmamış kalıcı durum → ürün varsayılanı AÇIK
        var (window2, vm2) = MainWindowHost.New(fresh);
        Assert.True(vm2.BuildDependencyCycles);
        GC.KeepAlive(window2);
    }
}
