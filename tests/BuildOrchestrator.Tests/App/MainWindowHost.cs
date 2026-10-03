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
    /// <param name="nowMs">[perf Faz A · A6] VM'in elapsed/bekçi saati (<c>RunViewModel</c> ctor'unun <c>nowMs</c>'i). Verilmezse
    /// üretimdeki <c>Environment.TickCount64</c>; canlı süreleri ve motor sessizlik bekçisini saati ileri alarak sınayan
    /// testler saati kendisi sürer.</param>
    public static (MainWindow window, RunViewModel vm) New(TempDir uiStateDir, Action<RunViewModel>? beforeVm = null,
        UiState? saved = null, AutostartService? autostart = null, Func<long>? nowMs = null)
    {
        ArgumentNullException.ThrowIfNull(uiStateDir);
        var engine = new EngineHost(Path.Combine(AppContext.BaseDirectory, "no-such-supervisor.exe"));
        // Üretimde pencere ile VM AYNI ConsoleBatcher'ı paylaşır (App.xaml.cs: tek singleton). Fixture de paylaşır: ayrı
        // örneklerle pencerenin reseed nesli VM'in SeedRunDocument sentinel'ini hiç göremez, uçuştaki bayat batch'in
        // düşmesi sınanamazdı.
        var batcher = NeverTickingBatcher();
        var vm = new RunViewModel(engine, batcher, () => "r1", nowMs)
        {
            LegacyWorktreePoolRoot = BuildOrchestrator.Tests.Supervisor.TestPaths.MissingLegacyPoolRoot, // [final review M8]
        };
        beforeVm?.Invoke(vm);
        var store = UiStateStore(uiStateDir);
        if (saved is not null) store.Save(saved);
        return (new MainWindow(engine, vm, batcher, DsResources.NewScope(), store, autostart), vm);
    }

    /// <summary>[design v1.23/v1.24 review C13] <see cref="New"/> + <see cref="Realize"/> (üretimin açılış boyutunda):
    /// realize edilmiş kabuk ve VM'i, veri akmadan. Böyle bir kabukla başlayan testlerin TEK kurulumu —
    /// <c>UpdatePillTests.Realized</c> ile <c>UpdateCardTests.Shell</c> aynı iki satırı ayrı ayrı yazmıştı;
    /// <see cref="NewWithProjects"/> da buradan başlar.</summary>
    public static (MainWindow window, RunViewModel vm) NewRealized(TempDir uiStateDir, Func<long>? nowMs = null)
    {
        var (window, vm) = New(uiStateDir, nowMs: nowMs);
        Realize(window);
        return (window, vm);
    }

    /// <summary>[motor · Task 8] <see cref="NewRealized"/> + kuruluma hazır bir teklif (<see cref="UpdateOffers.Sample"/>):
    /// hap görünür, kartın içeriği dolu. Uygulama teklifsiz açılır; hapı/kartı sınayan kabuk testleri teklifi burada,
    /// kurulumdan SONRA alır (üretimdeki gibi teklif sonradan gelir) ve görünür olan hap bir layout turuyla ölçülür.</summary>
    public static (MainWindow window, RunViewModel vm) NewRealizedWithOffer(TempDir uiStateDir)
    {
        var (window, vm) = NewRealized(uiStateDir);
        vm.AvailableUpdate = UpdateOffers.Sample();
        ((FrameworkElement)window.Content).UpdateLayout();
        return (window, vm);
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
        TempDir uiStateDir, params (string Name, string? Layer)[] nodes) =>
        NewWithProjectsAndClock(uiStateDir, null, nodes);

    /// <summary>[perf Faz A · A6] <see cref="NewWithProjects"/> + VM'in elapsed/bekçi saati ENJEKTE: canlı süreleri ve motor
    /// sessizlik bekçisini saati ileri alarak sınayan testler içindir (<see cref="New"/>'ün <c>nowMs</c>'i; <c>null</c> ⇒ üretim
    /// saati). Gövde TEK yerdedir: <see cref="NewWithProjects"/> buna devreder.</summary>
    public static (MainWindow window, RunViewModel vm, StickyLayerList list) NewWithProjectsAndClock(
        TempDir uiStateDir, Func<long>? nowMs, params (string Name, string? Layer)[] nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var (window, vm) = NewRealized(uiStateDir, nowMs);
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

    /// <summary>[perf A1 fix 1] Motorun planı (<c>buildPreview</c>): bu projeler derlenecek.
    /// <see cref="RunViewModel.ScopeFor"/> kapsamı bu bayraktan (<c>WillBuild</c>) türer — plansız bir fixture'da kapsam
    /// BOŞTUR ve açılış koreografisi görünür pencerede bile hiç oynamaz (<c>OperationChoreographer.Play</c>, n == 0);
    /// koreografiyi sınayan testler önce bunu verir. Bir koşuyu sürmenin ÜÇ adımı: <see cref="PreviewBuild"/> →
    /// <see cref="StartBuild"/> → <see cref="FinishBuild"/> (aynı proje adlarıyla).</summary>
    public static void PreviewBuild(RunViewModel vm, params string[] names)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(names);
        vm.OnEvent(new BuildPreviewEvent([.. names.Select(n => new BuildPreviewItem(IdOf(n), n, true))]));
    }

    /// <summary>[P3 · final review O5] Motor bir derlemeye başladı (<c>runStarted</c>, <see cref="New"/>'ün koşu
    /// kimliğiyle): koşu kilidi (<see cref="RunViewModel.IsMidRunLocked"/>) açık, Stop yapılabilir. Güvenli çıkışın VM
    /// (<see cref="SafeExitTests"/>) ve kabuk (<see cref="CloseToTrayTests"/>) testlerinin ORTAK başlangıcı — iki
    /// harness'ta ayrı ayrı yazılıyordu. <paramref name="names"/> koşunun proje adlarıdır (toplam proje = adet);
    /// verilmezse tek projelik koşu (eski davranış birebir). Aynı adlar <see cref="FinishBuild"/>'e verilir.</summary>
    public static void StartBuild(RunViewModel vm, params string[] names)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(names);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, Math.Max(names.Length, 1), 1, "Debug", 0));
    }

    /// <summary>[perf A1 fix 1] Koşu biter: her proje derlenir (<c>projectStarted</c> + <c>projectSucceeded</c>) ve
    /// <c>runCompleted</c> gelir. Bitiş finali grafa bu akıştaki <c>Phase</c> değişiminden gelir
    /// (<c>MainWindow.OnVmPropertyChangedForGraph</c>); derlenen proje yoksa final zaten oynamaz.</summary>
    public static void FinishBuild(RunViewModel vm, params string[] names)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(names);
        BuildProjects(vm, names);
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, names.Length, 0, 0, 0, 100));
    }

    /// <summary>[perf A3/A4 fix] Her proje derlenir (<c>projectStarted</c> + <c>projectSucceeded</c>); koşu BİTMEZ
    /// (<c>runCompleted</c> yok). <see cref="FinishBuild"/> bunu çağırıp koşuyu bitirir; koşu sürerken proje olayları
    /// gereken testler (gizli pencerede graf itişleri) doğrudan bunu kullanır — olay çifti tek yerde durur.</summary>
    public static void BuildProjects(RunViewModel vm, params string[] names)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(names);
        foreach (var name in names)
        {
            StartProject(vm, name);
            SucceedProject(vm, name);
        }
    }

    /// <summary>[perf Faz A temizlik] Motor bir projenin derlemesine başladı (<c>projectStarted</c>): satır "building". Olayın
    /// kimlik/ad çifti (<see cref="IdOf"/>) tek yerde durur; <see cref="BuildProjects"/> ve tek bir projeyi süren testler bunu çağırır.</summary>
    public static void StartProject(RunViewModel vm, string name)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(name);
        vm.OnEvent(new ProjectStartedEvent("r1", IdOf(name), name));
    }

    /// <summary>[perf Faz A temizlik] <paramref name="name"/> projesinin derlemesi başarıyla bitti (<c>projectSucceeded</c>).
    /// <paramref name="durationMs"/> ETA'nın ortalamasını besler; uzun bir ortalama isteyen testler (canlı süre/ETA) kendisi verir.</summary>
    public static void SucceedProject(RunViewModel vm, string name, int durationMs = 100)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(name);
        vm.OnEvent(new ProjectSucceededEvent("r1", IdOf(name), durationMs));
    }

    /// <summary>[perf A3/A4 fix] Bir derlemenin TÜM olay akışı: plan (<see cref="PreviewBuild"/>), başlangıç
    /// (<see cref="StartBuild"/>), her projenin derlenmesi ve bitiş (<see cref="FinishBuild"/>). Gizli-pencere testleri
    /// akışın bütününü tek çağrıyla sürer.</summary>
    public static void RunBuild(RunViewModel vm, params string[] names)
    {
        PreviewBuild(vm, names);
        StartBuild(vm, names);
        FinishBuild(vm, names);
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
