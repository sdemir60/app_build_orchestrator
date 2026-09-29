using System.IO;
using System.Windows;
using System.Windows.Input;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T2] <see cref="MainWindow"/> kuran testlerin TEK kurulum yeri.
///
/// <para>T1 öncesinde bu blok iki dosyada (<c>MainWindowRealizeTests</c>, <c>MainWindowInputTests</c>) AYRI AYRI
/// duruyordu ve T2 üçüncü/dördüncüsünü yazacaktı — tek yere toplandı (kopya YASAK, CLAUDE.md).</para>
///
/// <para><b>İki değişmez burada zorlanır:</b>
/// (a) motor ASLA doğmaz — var olmayan bir supervisor yolu verilir ve pencere hiç <c>Show()</c> edilmez
/// (<c>Loaded</c>/<c>OnSourceInitialized</c> tetiklenmez; bkz. <see cref="MainWindowRealizeTests"/> sınıf özeti);
/// (b) <b>kalıcı durum store'u ZORUNLU olarak temp'e yönlendirilir</b> — parametre opsiyonel DEĞİLDİR, çünkü
/// unutulduğu anda test kullanıcının GERÇEK <c>%LOCALAPPDATA%\BuildOrchestrator\ui-state.json</c> dosyasını
/// yeniden yazar (T1/C1'de ölçülen yan etki: persist zinciri <c>Show()</c>'a bağlı değildir, abonelik ctor'da
/// kurulur). Parmak-izi guard'ı <see cref="MainWindowInputTests"/>'tedir.</para>
/// </summary>
internal static class MainWindowHost
{
    /// <summary>Konsol pompası test boyunca hiç tick etmesin — batcher sonsuza dek bekler.</summary>
    public static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    /// <summary>Üretim kablajının TAMAMIYLA kurulu bir <see cref="MainWindow"/>'u + onun VM'i.</summary>
    /// <param name="beforeVm">[A13/T6 · t1] VM kurulduktan SONRA, pencere ctor'u onu SEED etmeden ÖNCE koşar.
    /// Pencerenin ctor'unda olan biteni (kalıcı durumdan repo/branch/perf seed'i — <c>MainWindow.xaml.cs:126</c>)
    /// gözlemek isteyen tek yol budur: <c>New</c> döndüğünde seed ÇOKTAN akmıştır, sonradan takılan bir prob onu
    /// göremez. Verilmezse davranış birebir eskisi gibidir.</param>
    /// <param name="saved">[P3] Kalıcı durum dosyasını pencere kurulmadan ÖNCE tohumlar
    /// (<c>SettingsDialogHost.OpenRealized</c>'ın <c>saved</c>'ıyla AYNI desen) — kayıtlı bir ayarla açılan kabuğu
    /// sınayan testler içindir (ör. Close to tray kapalı). <c>null</c> ⇒ dosya yazılmaz, davranış birebir eskisi
    /// gibidir.</param>
    /// <param name="autostart">[P4] Pencerenin Windows başlangıç kaydı servisi (üretimde DI verir). Verilmezse
    /// <c>null</c> — Windows yüzeyi yok; gerçek registry'ye giden bir servis testte ASLA kurulmaz.</param>
    public static (MainWindow window, RunViewModel vm) New(TempDir uiStateDir, Action<RunViewModel>? beforeVm = null,
        UiState? saved = null, AutostartService? autostart = null)
    {
        ArgumentNullException.ThrowIfNull(uiStateDir);
        var engine = new EngineHost(Path.Combine(AppContext.BaseDirectory, "no-such-supervisor.exe"));
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1")
        {
            LegacyWorktreePoolRoot = BuildOrchestrator.Tests.Supervisor.TestPaths.MissingLegacyPoolRoot, // [final review M8]
        };
        beforeVm?.Invoke(vm);
        var store = UiStateStore(uiStateDir);
        if (saved is not null) store.Save(saved);
        return (new MainWindow(engine, vm, NeverTickingBatcher(), DsResources.NewScope(), store, autostart), vm);
    }

    /// <summary>[P3 · final review O5] Bir testin geçici kalıcı durum dosyası — <see cref="New"/>'ün pencereye verdiği
    /// store'un yolu. TEK tanım: pencerenin okuduğu dosyayı tohumlayan ya da sonradan okuyan her test yolu buradan
    /// alır. Yol ikinci bir yerde yeniden kurulsaydı ve biri değişseydi, test pencerenin hiç okumadığı bir dosyayı
    /// tohumlar ve sessizce vakumda yeşil kalırdı.</summary>
    public static string UiStatePath(TempDir uiStateDir)
    {
        ArgumentNullException.ThrowIfNull(uiStateDir);
        return Path.Combine(uiStateDir.Path, "ui-state.json");
    }

    /// <summary>[P3 · final review O5] <see cref="UiStatePath"/>'teki dosyanın store'u — pencerenin kullandığıyla AYNI
    /// dosya. <see cref="JsonUiStateStore"/> durum tutmaz (her çağrı diski okur/yazar), yani ayrı bir örnek pencerenin
    /// gördüğünü görür ve pencere bunun yazdığını bir sonraki okumasında görür.</summary>
    public static JsonUiStateStore UiStateStore(TempDir uiStateDir) => new(UiStatePath(uiStateDir));

    /// <summary>
    /// [T2 fix-1 · I-F] Realize edilmiş bir kabuk + topolojisi akmış bir VM — <b>üretim sırasıyla</b> (kabuk
    /// ÖNCE realize, veri SONRA) ve <c>Idle</c> fazında.
    ///
    /// <para>Üç test sınıfı bu fixture'ı ayrı ayrı kopyalamıştı ve ŞİMDİDEN ayrışmıştı: biri <c>RootPath</c>'i
    /// hiç set etmiyordu, yani o kabuk <c>HasWorkspace == false</c> ile ve "Pick a repository" overlay'i
    /// AÇIKKEN koşuyordu. Kopyalanmış fixture'ın sessiz ayrışması, kuralın (kopya YASAK) önlemeye çalıştığı
    /// şeyin ta kendisidir → tek yer.</para>
    /// </summary>
    /// <param name="nodes">Proje adı + (varsa) katman adı, build-order sırasında.</param>
    public static (MainWindow window, RunViewModel vm, StickyLayerList list) NewWithProjects(
        TempDir uiStateDir, params (string Name, string? Layer)[] nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var (window, vm) = New(uiStateDir);
        Realize(window);
        vm.RootPath = @"C:\src\OSYS";
        var projectNodes = nodes.Select((n, i) => Node(n.Name, i, n.Layer)).ToList();
        vm.OnEvent(new WorkspaceTopologyEvent(projectNodes, [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, projectNodes.Count, 0)); // → Idle
        return (window, vm, window.Shell.ProjectsList);
    }

    /// <summary>Test topolojisi düğümü — <c>Id</c> = kanonik csproj yolu (üretimdeki gibi tam yol).</summary>
    public static ProjectNode Node(string name, int order, string? layer = null) =>
        new($@"C:\p\{name}.csproj", name, $@"C:\p\{name}.csproj", ["Osys"], [], order,
            layer is null ? null : order, layer, false, null);

    /// <summary>Bir test projesinin <c>Id</c>'si (<see cref="Node"/> ile BİREBİR aynı kural).</summary>
    public static string IdOf(string name) => $@"C:\p\{name}.csproj";

    /// <summary>[design v1.23/v1.24 review C12] Pencerenin Esc'ine kullanıcı gibi basar: pencere düzeyindeki Esc
    /// bağlamasının komutu, üretimdeki yolun AYNISIYLA sürülür (<see cref="CommandPress.Press"/> — kapı kapalıysa
    /// hiçbir şey olmaz). WPF olay yönlendirmesi gerçek bir HWND olmadan güvenilir değildir, bu yüzden tuş olayı değil
    /// bağlamanın kendisi sürülür. Esc zincirini süren testlerin TEK basış yeri — <c>EscStopTests</c> ve
    /// <c>UpdateCardTests</c> bunu birebir aynı gövdeyle ayrı ayrı yazmıştı.</summary>
    public static void PressEscape(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var escape = window.InputBindings.OfType<KeyBinding>()
            .Single(k => k.Key == Key.Escape && k.Modifiers == ModifierKeys.None);
        CommandPress.Press(escape.Command);
    }

    /// <summary>[task 3] Gönderimler motor yerine başarıyla "gider" (<see cref="RunViewModel.DebugSendOverride"/>) —
    /// motorun cevabını test <c>vm.OnEvent(...)</c> ile verir. Verilmezse gönderim her zaman düşer.</summary>
    public static void AcceptSends(RunViewModel vm) => vm.DebugSendOverride = _ => Task.CompletedTask;

    /// <summary>[P3 · final review O5] Motor bir derlemeye başladı (<c>runStarted</c>, <see cref="New"/>'ün koşu
    /// kimliğiyle): koşu kilidi (<see cref="RunViewModel.IsMidRunLocked"/>) açık, Stop yapılabilir. Güvenli çıkışın VM
    /// (<see cref="SafeExitTests"/>) ve kabuk (<see cref="CloseToTrayTests"/>) testlerinin ORTAK başlangıcı — iki
    /// harness'ta ayrı ayrı yazılıyordu.</summary>
    public static void StartBuild(RunViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
    }

    /// <summary>[task 3] Sync'i verilen kipte, o kipin ÜRETİMDEKİ girişinden başlatır: Sync düğmesi (Manual),
    /// dışarıdan branch değişimi (BranchChange), Debug|Release geçişi (ConfigurationChange — öteki configuration'a),
    /// kendiliğinden Sync (Silent), motor hazır oldu (Appended).</summary>
    public static Task StartSync(RunViewModel vm, SyncMode mode)
    {
        ArgumentNullException.ThrowIfNull(vm);
        switch (mode)
        {
            case SyncMode.Manual: return vm.SyncCommand.ExecuteAsync(null);
            case SyncMode.BranchChange: return vm.SyncAfterExternalBranchChangeAsync("Switched to feature");
            case SyncMode.ConfigurationChange:
                vm.SetConfiguration(vm.Configuration == "Release" ? "Debug" : "Release");
                return Task.CompletedTask;
            case SyncMode.Silent: return vm.SyncSilentlyAsync(SilentSyncReason.Refresh);
            case SyncMode.Appended: vm.OnEngineReady("1.0.0", 1); return Task.CompletedTask;
            default: throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    /// <summary>[task 3] Motorun bir Sync'e cevabı: başlangıç, verilen topoloji (<see cref="Node"/> kuralıyla,
    /// <see cref="NewWithProjects"/> ile AYNI yapı üretilebilsin diye), tamamlanma.</summary>
    public static void ReplySync(RunViewModel vm, params (string Name, string? Layer)[] nodes)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(nodes);
        vm.OnEvent(new SyncStartedEvent(vm.RootPath, "main"));
        vm.OnEvent(new WorkspaceTopologyEvent([.. nodes.Select((n, i) => Node(n.Name, i, n.Layer))], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, nodes.Length, 0));
    }

    /// <summary>
    /// [fix round 1 · A1] Pencerenin İÇERİĞİNİ realize eder — <b>ölçüldü:</b> <c>Window.Measure/Arrange</c>
    /// gerçek bir <c>PresentationSource</c> (HWND) olmadan içeriğe HİÇ İNMEZ; caption butonlarının şablonları
    /// bile genişlemez (<c>MinButton.ApplyTemplate()</c> sonradan hâlâ <c>true</c> döner). İçerik kökü doğrudan
    /// ölçülüp yerleştirildiğinde ise şablonlar genişler ve <c>OnRender</c> koşar — yani <c>Background</c> gibi
    /// RENDER-ONLY özellikler de gerçekten okunur ve yanlış tipli token orada patlar.
    ///
    /// <para>[A13/T3 fix-1 · B4] Boyut ARTIK parametre: <c>TitleBarContextTests</c> "en dar desteklenen pencere"
    /// (<c>Size.WindowMinWidth</c>=1240) senaryosunu ölçmek için bu bloğu inline yeniden yazmıştı — realize
    /// etmenin TEK yeri kuralı delinmişti. Varsayılan üretimin açılış boyutudur (MainWindow.xaml 1400×800).</para>
    /// </summary>
    public static FrameworkElement Realize(MainWindow window, double width = 1400, double height = 800)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.ApplyTemplate();
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        return content;
    }
}
