using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T2] <see cref="MainWindow"/> kuran testlerin TEK kurulum yeri.
///
/// <para>T1 öncesinde bu blok iki dosyada (<c>MainWindowRealizeTests</c>, <c>MainWindowInputTests</c>) AYRI AYRI
/// duruyordu ve T2 üçüncü/dördüncüsünü yazacaktı — tek yere toplandı (kopya YASAK, CLAUDE.md).</para>
///
/// <para>[son temizlik · madde 20] Pencere kurmayan testlerin de ortak parçalarını taşır: konsol pompasının iki test biçimi — hiç tick
/// etmeyen (<see cref="NeverTickingBatcher"/>) ve testin elinde boşaltılan (<see cref="DrainableBatcher"/>, <see cref="DrainPosted"/>) — VM'i ya
/// da konsolu pencere olmadan kuran testlerin de tek batcher kaynağıdır; pencere kısayolları (<see cref="PressKeyBinding"/>), konsol satırı
/// sayımı (<see cref="Occurrences"/>) ve bir proje satırının menüsünü açıp açılmasını bekleme (<see cref="OpenRowMenu"/> — satırı pencereyle
/// ya da tek başına kuran testlerin ortak bloğu) da buradadır.</para>
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
    /// <summary>Konsol pompası test boyunca hiç tick etmesin — batcher sonsuza dek bekler. VM'i ya da konsolu pompası tick etmeden kuran
    /// HER testin TEK batcher'ı (kopya YASAK — B3 · karar 47b: dosya başına özel kopyalar ve satır içi kurulumlar buraya toplandı).</summary>
    public static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    /// <summary>[idle-memory · dalga 2 · fix 1 · M5] Pompası testin elinde olan konsol: tick beklemez, kanal <see cref="DrainPosted"/> ile
    /// tek seferde boşaltılır — VM'in pompaya NE gönderdiğini sınayan testlerin TEK kurulumu (<see cref="NeverTickingBatcher"/>'ın eşi).</summary>
    public static ConsoleBatcher DrainableBatcher() => new(_ => Task.CompletedTask);

    /// <summary>[fix 1 · M5] Kanalı tamamlar ve üretimin pompasıyla tek seferde boşaltır; postlanan metni döner (her satır '\n' sonekli,
    /// bir reseed sentinel'i kendinden önceki satırları atar — üretimdeki gibi). Pompaya gideni store'la karşılaştıran testlerin TEK
    /// boşaltma yeri.</summary>
    public static async Task<string> DrainPosted(ConsoleBatcher console)
    {
        ArgumentNullException.ThrowIfNull(console);
        var posted = new StringBuilder();
        console.Complete();
        await console.PumpAsync((text, _) => posted.Append(text), CancellationToken.None);
        return posted.ToString();
    }

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
    /// <param name="productionMemoryCollector">[idle-memory] Pencere işlem sonu bellek trimini GERÇEK toplayıcıyla mı yapsın.
    /// Varsayılan <c>false</c>: pencere kurulur kurulmaz boş bir toplayıcı takılır (<see cref="NoRealGc.Collection"/>: "testler
    /// gerçek GC'yi koşturmaz" politikasının Supervisor ile ORTAK tek tanımı) — trim her işlem bitişinde (görünür pencerede de)
    /// toplar ve süit gerçek agresif GC'yi her testte koştururdu. Toplamayı sayan testler kendi sahtesini fixture'DAN SONRA takar.
    /// Üretim toplayıcısını isteyenler: onun geçerliliğini sınayan test (<c>RunBufferReleaseTests</c>) ve kapılı bellek bütçesi
    /// ölçümü (<c>NarrativeBudgetTests</c>: sayacını üretim toplayıcısının üstüne sarar).</param>
    /// <param name="console">[idle-memory] Pencere ile VM'in paylaştığı konsol pompası. Verilmezse hiç tick etmeyen pompa
    /// (<see cref="NeverTickingBatcher"/>): satırlar kanalda bekler, belgeye yalnız testin kendisi batch verir. Pompayı kendisi
    /// süren testler (ör. bellek bütçesi ölçümü: tick etmeyen pompanın kanalında biriken satırlar üretimde yoktur) kendi
    /// pompasını verir.</param>
    /// <param name="compactConsole">[idle-memory · dalga 2 · spec §6.5] Kabuğun konsolu kompakt mı yazsın. Varsayılan KAPALI
    /// (<c>false</c>): üretimin varsayılanı açıktır, ama pencere kuran testlerin anlatı iddiaları (her proje satırı anlatıda, "N
    /// lines" satır satır, model ile belge eşitliği) her satırın geçtiği kipi pinler — kompakt kip bilgi ve komut satırlarını gizler
    /// ve onları sessizce boşa düşürürdü. Kompakt kipi sınayan testler <c>true</c> verir; <c>null</c> ⇒ dokunulmaz: değer pencerenin
    /// kalıcı durumdan tohumladığıdır (açılış seed'ini gözleyen testler). Değer pencere kurulduktan SONRA atanır — ctor kalıcı
    /// durumdan tohumlar ve önce atanan bir değeri ezerdi (<paramref name="beforeVm"/>'deki bir atama da ezilir). İşlem yokken
    /// atandığı için hemen etkindir (<see cref="RunViewModel.CompactConsoleInEffect"/>). Atama tohumdan farklıysa kabuğun kalıcılık
    /// zinciri enjekte edilen store'u yazar (yalnız temp dosyası): store'un henüz yazılmadığını ön-koşul alan test <c>null</c>
    /// verir.</param>
    /// <param name="stopEngineWhenIdle">[idle-memory · dalga 3 · spec §6.4 · karar 9] Kabuğun boştaki motoru durdurması açık mı.
    /// Varsayılan KAPALI (<c>false</c>): üretimin varsayılanı açıktır, ama saati beş dakikadan fazla ileri alan testler (motor sessizlik
    /// bekçisi, boşta kalan uygulama, gizli yüzey tikleri) fixture motorunu uyutmasın — uyku yalnız bunu açıkça isteyen testte
    /// (<c>true</c>) işler. <c>null</c> ⇒ dokunulmaz: değer pencerenin kalıcı durumdan tohumladığıdır (açılış seed'ini gözleyen testler).
    /// Değer pencere kurulduktan SONRA atanır — ctor kalıcı durumdan tohumlar ve önce atanan bir değeri ezerdi
    /// (<paramref name="beforeVm"/>'deki bir atama da ezilir; <paramref name="compactConsole"/> ile aynı gerekçe). Atama tohumdan farklıysa
    /// kabuğun kalıcılık zinciri enjekte edilen store'u yazar (yalnız temp dosyası): store'un henüz yazılmadığını ön-koşul alan test —
    /// <paramref name="compactConsole"/> gibi — <c>null</c> verir.</param>
    public static (MainWindow window, RunViewModel vm) New(TempDir uiStateDir, Action<RunViewModel>? beforeVm = null,
        UiState? saved = null, AutostartService? autostart = null, Func<long>? nowMs = null,
        bool productionMemoryCollector = false, ConsoleBatcher? console = null, bool? compactConsole = false,
        bool? stopEngineWhenIdle = false)
    {
        ArgumentNullException.ThrowIfNull(uiStateDir);
        var engine = new EngineHost(Path.Combine(AppContext.BaseDirectory, "no-such-supervisor.exe"));
        // Üretimde pencere ile VM AYNI ConsoleBatcher'ı paylaşır (App.xaml.cs: tek singleton). Fixture de paylaşır: ayrı
        // örneklerle pencerenin reseed nesli VM'in SeedRunDocument sentinel'ini hiç göremez, uçuştaki bayat batch'in
        // düşmesi sınanamazdı.
        var batcher = console ?? NeverTickingBatcher();
        var vm = new RunViewModel(engine, batcher, () => "r1", nowMs)
        {
            LegacyWorktreePoolRoot = BuildOrchestrator.Tests.Supervisor.TestPaths.MissingLegacyPoolRoot, // [final review M8]
        };
        Engines.Add(vm, engine);
        beforeVm?.Invoke(vm);
        var store = UiStateStore(uiStateDir);
        if (saved is not null) store.Save(saved);
        var window = new MainWindow(engine, vm, batcher, DsResources.NewScope(), store, autostart);
        if (!productionMemoryCollector) window.MemoryCollector = NoRealGc.Collection;
        if (compactConsole is { } compact) vm.CompactConsole = compact; // ctor'un seed'inden SONRA (yukarıdaki param doc'u)
        if (stopEngineWhenIdle is { } stop) vm.StopEngineWhenIdle = stop; // aynı gerekçe: seed'den SONRA
        return (window, vm);
    }

    /// <summary>[idle-memory · dalga 3 · B1 fix 1 · m-1] <see cref="New"/>'ün VM'e verdiği konak — testlerin konağın dikişlerine (ör.
    /// <c>EngineHost.DebugStartWorkBegins</c>) ulaştığı TEK yol. VM konağı dışarı vermez; fixture onu kurduğu anda kaydeder.</summary>
    public static EngineHost EngineOf(RunViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        return Engines.TryGetValue(vm, out var engine)
            ? engine
            : throw new InvalidOperationException("this view model was not built by MainWindowHost.New");
    }

    /// <summary><see cref="EngineOf"/>'un kaydı — VM yaşadıkça (zayıf anahtar: kayıt VM'i tutmaz).</summary>
    private static readonly ConditionalWeakTable<RunViewModel, EngineHost> Engines = new();

    /// <summary>[design v1.23/v1.24 review C13] <see cref="New"/> + <see cref="Realize"/> (üretimin açılış boyutunda):
    /// realize edilmiş kabuk ve VM'i, veri akmadan. Böyle bir kabukla başlayan testlerin TEK kurulumu —
    /// <c>UpdatePillTests.Realized</c> ile <c>UpdateCardTests.Shell</c> aynı iki satırı ayrı ayrı yazmıştı;
    /// <see cref="NewWithProjects"/> da buradan başlar. <paramref name="console"/> ve
    /// <paramref name="productionMemoryCollector"/>: <see cref="New"/>'ünküler.
    /// <para>[temizlik · madde 1] <paramref name="dpi"/> verilirse kabuk o ekran ölçeğinde realize edilir (makinenin ölçeği ne olursa olsun):
    /// pencerenin kendi ölçeği realize'dan ÖNCE (MaximizeFix çerçeve dolgusunu ondan okur), içeriğinki SONRA verilir
    /// (<see cref="RelayoutAt"/>). <c>null</c> ⇒ sistemin ölçeği, davranış birebir eskisi gibi.</para></summary>
    public static (MainWindow window, RunViewModel vm) NewRealized(TempDir uiStateDir, Func<long>? nowMs = null,
        ConsoleBatcher? console = null, bool productionMemoryCollector = false, DpiScale? dpi = null)
    {
        var (window, vm) = New(uiStateDir, nowMs: nowMs, productionMemoryCollector: productionMemoryCollector, console: console);
        if (dpi is { } windowScale) VisualTreeHelper.SetRootDpi(window, windowScale);
        var content = Realize(window);
        if (dpi is { } contentScale) RelayoutAt(content, contentScale);
        return (window, vm);
    }

    /// <summary>[motor · Task 8] <see cref="NewRealized"/> + kuruluma hazır bir teklif (<see cref="UpdateOffers.Sample"/>):
    /// hap görünür, kartın içeriği dolu. Uygulama teklifsiz açılır; hapı/kartı sınayan kabuk testleri teklifi burada,
    /// kurulumdan SONRA alır (üretimdeki gibi teklif sonradan gelir) ve görünür olan hap bir layout turuyla ölçülür.
    /// <para>[temizlik · madde 1] <paramref name="dpi"/>: <see cref="NewRealized"/>'ınki. Teklif hapın şablonunu realize'dan SONRA doğurur ve
    /// sonradan doğan görsel ölçeği almaz (<see cref="RelayoutAt"/>), bu yüzden teklif geldikten sonra ölçek içeriğe yeniden verilir.</para></summary>
    public static (MainWindow window, RunViewModel vm) NewRealizedWithOffer(TempDir uiStateDir, DpiScale? dpi = null)
    {
        var (window, vm) = NewRealized(uiStateDir, dpi: dpi);
        vm.AvailableUpdate = UpdateOffers.Sample();
        var content = (FrameworkElement)window.Content;
        content.UpdateLayout();
        if (dpi is { } scale) RelayoutAt(content, scale);
        return (window, vm);
    }

    /// <summary>[temizlik · madde 1] Realize edilmiş içeriği <paramref name="dpi"/> ölçeğinde yeniden yerleştirir. Başsız süitte içerik kökü
    /// pencerenin görsel çocuğu DEĞİLDİR (pencerenin ContentPresenter'ı onu yalnız gerçek bir ölçüm turunda alır — <see cref="Realize"/>),
    /// yani pencereye verilen ölçek içeriğe inmez; içerik kökü kendi ölçeğini ayrıca alır. <c>SetRootDpi</c> ölçeği yalnız O AN ağaçta olan
    /// görsellere yazar — sonradan şablondan doğan çocuk almaz (ölçüldü: realize'dan önce verilince hapın etiketi ve ikonu 1.0'da kaldı).
    /// Bu yüzden realize'dan SONRA verilir ve yerleşim yuvarlaması yeni ölçekle baştan koşsun diye her öğenin ölçümü geçersizlenir.</summary>
    private static void RelayoutAt(FrameworkElement content, DpiScale dpi)
    {
        VisualTreeHelper.SetRootDpi(content, dpi);
        foreach (var element in DsResources.RealizedObjects(content).OfType<UIElement>()) element.InvalidateMeasure();
        content.UpdateLayout();
    }

    /// <summary>[temizlik madde 4] Title bar'ın İÇ kutusu: <c>TitleBarRow</c>'un (40'lık bant + alt hairline) çocuğu olan Grid — kümeleri taşıyan
    /// DockPanel ile ortadaki kaynak şeridinin ORTAK hücresi, hairline'dan sonra kalan alan. "Bant → Grid → ilk çocuk DockPanel" yapısını bilen
    /// TEK yer (<see cref="TitleBarDock"/> ile): <c>MainWindowRealizeTests</c> ve <c>ResourceStripRealizeTests</c> onu üç kez ayrı ayrı yazıyordu.</summary>
    public static Grid TitleBarGrid(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return Assert.IsType<Grid>(window.TitleBarRow.Child);
    }

    /// <summary>[temizlik madde 4] Title bar'ın iki kümesini (sol logo kilidi, sağ küme + caption butonları) taşıyan DockPanel —
    /// <see cref="TitleBarGrid"/>'in İLK çocuğu; kaynak şeridi aynı Grid'in son çocuğudur.</summary>
    public static DockPanel TitleBarDock(MainWindow window) => Assert.IsType<DockPanel>(TitleBarGrid(window).Children[0]);

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
    /// saati). <see cref="NewWithProjects"/> buna devreder; gövde <see cref="NewWithProjectsOn"/>'dadır.</summary>
    public static (MainWindow window, RunViewModel vm, StickyLayerList list) NewWithProjectsAndClock(
        TempDir uiStateDir, Func<long>? nowMs, params (string Name, string? Layer)[] nodes) =>
        NewWithProjectsOn(uiStateDir, nowMs, console: null, productionMemoryCollector: false, nodes);

    /// <summary>[idle-memory] <see cref="NewWithProjects"/> + pencere ile VM'in paylaştığı konsol pompası ENJEKTE (<see cref="New"/>'ün
    /// <c>console</c>'u): pompayı kendisi süren testler içindir — satırlar üretimdeki yoldan (pompa → Dispatcher →
    /// <c>AppendConsoleBatch</c>) belgeye gider ve kanalda birikmez. <paramref name="productionMemoryCollector"/>:
    /// <see cref="New"/>'ünkü (bellek ölçümü üretimin toplayıcısıyla ölçer). Gövde <see cref="NewWithProjectsOn"/>'dadır.</summary>
    public static (MainWindow window, RunViewModel vm, StickyLayerList list) NewWithProjectsAndConsole(
        TempDir uiStateDir, ConsoleBatcher console, bool productionMemoryCollector, params (string Name, string? Layer)[] nodes)
    {
        ArgumentNullException.ThrowIfNull(console);
        return NewWithProjectsOn(uiStateDir, nowMs: null, console, productionMemoryCollector, nodes);
    }

    /// <summary>Topolojisi akmış kabuğun TEK gövdesi (<see cref="NewWithProjects"/>, <see cref="NewWithProjectsAndClock"/>,
    /// <see cref="NewWithProjectsAndConsole"/>): saat, konsol pompası ve toplayıcı <see cref="New"/>'ünkülerdir (<c>null</c>/<c>false</c>
    /// ⇒ varsayılan).
    /// <para>[idle-memory] Açılış Sync'i bir işlem bitişidir ve pencerenin işlem sonu bellek trimini ister. Fixture pencereyi
    /// OTURMUŞ döndürür: o trim fixture dönmeden (boş toplayıcıyla — üretim toplayıcısı istense de) yürür — pay sıfırlanır, kuyruk
    /// boşaltılır, üretim payı ve pencerenin toplayıcısı geri konur. Test böylece trim edilmemiş bir işlemle başlamaz ve fixture'ın
    /// zamanlayıcısı testin içine (ya da paylaşılan STA dispatcher'ında sonraki bir teste) sızmaz.</para></summary>
    private static (MainWindow window, RunViewModel vm, StickyLayerList list) NewWithProjectsOn(
        TempDir uiStateDir, Func<long>? nowMs, ConsoleBatcher? console, bool productionMemoryCollector,
        (string Name, string? Layer)[] nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var (window, vm) = NewRealized(uiStateDir, nowMs, console, productionMemoryCollector);
        vm.RootPath = @"C:\src\OSYS";
        var projectNodes = nodes.Select((n, i) => Node(n.Name, i, n.Layer)).ToList();
        vm.OnEvent(new WorkspaceTopologyEvent(projectNodes, [], [], []));
        var grace = window.TrimGraceAfterFinale;
        var collector = window.MemoryCollector;
        window.TrimGraceAfterFinale = TimeSpan.Zero;
        window.MemoryCollector = NoRealGc.Collection;
        vm.OnEvent(SyncCompleted(projectNodes.Count)); // → Idle
        DispatcherPump.DrainToIdle();
        window.TrimGraceAfterFinale = grace;
        window.MemoryCollector = collector;
        return (window, vm, window.Shell.ProjectsList);
    }

    /// <summary>Gerçek OSYS çözümünün proje sayısı: ölçek isteyen testler (graf itişi, olay akışı/konsol yükü, kapılı ölçüm rig'i)
    /// bu büyüklükte bir çözüm kurar. TEK tanım — pin, graf itiş testi ve ölçüm testi aynı sayıyı ayrı ayrı yazmıştı.</summary>
    public const int OsysProjectCount = 177;

    /// <summary><c>P0..P{count-1}</c> proje adları — hem fixture'a (<c>NewWithProjects</c> ve <c>ReplySync</c>'in adları alan
    /// aşırı yüklemeleri) hem koşu sürücülerine (<see cref="PreviewBuild"/>, <see cref="StartBuild"/>, <see cref="FinishBuild"/>)
    /// aynı dizi gider.</summary>
    public static string[] ProjectNames(int count) => [.. Enumerable.Range(0, count).Select(i => $"P{i}")];

    /// <summary>Yalnız adları olan (katmansız) topoloji: <c>NewWithProjects</c>'in adları alan aşırı yüklemesi. Gövde TEK yerdedir —
    /// <c>(Name, Layer)</c> çiftli hâl buna değil, bu ona devreder.</summary>
    public static (MainWindow window, RunViewModel vm, StickyLayerList list) NewWithProjects(TempDir uiStateDir, string[] names) =>
        NewWithProjects(uiStateDir, Unlayered(names));

    /// <summary>[perf Faz A · A6] Saat enjekte + yalnız adlar: <c>NewWithProjectsAndClock</c>'un adları alan aşırı yüklemesi.</summary>
    public static (MainWindow window, RunViewModel vm, StickyLayerList list) NewWithProjectsAndClock(
        TempDir uiStateDir, Func<long>? nowMs, string[] names) =>
        NewWithProjectsAndClock(uiStateDir, nowMs, Unlayered(names));

    /// <summary>(ad, katman) çiftleri; katman yok — adları alan aşırı yüklemelerin TEK dönüştürücüsü.</summary>
    private static (string, string?)[] Unlayered(string[] names) => [.. names.Select(n => (n, (string?)null))];

    /// <summary>Test topolojisi düğümü — <c>Id</c> = kanonik csproj yolu (üretimdeki gibi tam yol).</summary>
    public static ProjectNode Node(string name, int order, string? layer = null) =>
        new($@"C:\p\{name}.csproj", name, $@"C:\p\{name}.csproj", ["Osys"], [], order,
            layer is null ? null : order, layer, false, null);

    /// <summary>Bir test projesinin <c>Id</c>'si (<see cref="Node"/> ile BİREBİR aynı kural).</summary>
    public static string IdOf(string name) => $@"C:\p\{name}.csproj";

    /// <summary>Bir test projesinin satır modeli (<c>vm.Projects</c> öğesi; kimlik <see cref="IdOf"/>) — testlerin
    /// <c>vm.Projects.Single(p => p.Id == IdOf(name))</c> deyimi tek yerde durur.</summary>
    public static ProjectRowViewModel ProjectOf(RunViewModel vm, string name) =>
        vm.Projects.Single(p => p.Id == IdOf(name));

    /// <summary>[son temizlik · madde 15/19] Pencere düzeyindeki bir tuş bağlaması — tuş ve değiştirici TEK bağlamaya eşlenir. Bağlamayı
    /// arayan testlerin tek arama yeri (<see cref="PressKeyBinding"/>, <c>MainWindowInputTests</c>).</summary>
    public static KeyBinding KeyBindingOf(MainWindow window, Key key, ModifierKeys modifiers = ModifierKeys.None)
    {
        ArgumentNullException.ThrowIfNull(window);
        return window.InputBindings.OfType<KeyBinding>().Single(k => k.Key == key && k.Modifiers == modifiers);
    }

    /// <summary>[design v1.23/v1.24 review C12 · son temizlik · madde 15/19] Pencere düzeyindeki bir tuş bağlamasına kullanıcı gibi basar:
    /// bağlamanın komutu üretimdeki yolun AYNISIYLA sürülür (<see cref="CommandPress.Press"/> — kapı kapalıysa hiçbir şey olmaz). WPF olay
    /// yönlendirmesi gerçek bir HWND olmadan güvenilir değildir, bu yüzden tuş olayı değil bağlamanın kendisi sürülür. Pencere kısayollarına
    /// basan testlerin TEK basış yeri — <c>AboutWiringTests</c> ve <c>NotesDialogWiringTests</c> bunu birebir aynı gövdeyle ayrı ayrı
    /// yazmıştı (<c>Invoke</c>).</summary>
    public static void PressKeyBinding(MainWindow window, Key key, ModifierKeys modifiers = ModifierKeys.None) =>
        CommandPress.Press(KeyBindingOf(window, key, modifiers).Command);

    /// <summary>[design v1.23/v1.24 review C12] Pencerenin Esc'ine kullanıcı gibi basar (<see cref="PressKeyBinding"/>). Esc zincirini süren
    /// testlerin basış yeri — <c>EscStopTests</c> ve <c>UpdateCardTests</c> bunu birebir aynı gövdeyle ayrı ayrı yazmıştı.</summary>
    public static void PressEscape(MainWindow window) => PressKeyBinding(window, Key.Escape);

    /// <summary>[task 3] Gönderimler motor yerine başarıyla "gider" (<see cref="RunViewModel.DebugSendOverride"/>) —
    /// motorun cevabını test <c>vm.OnEvent(...)</c> ile verir. Verilmezse gönderim her zaman düşer.</summary>
    public static void AcceptSends(RunViewModel vm) => vm.DebugSendOverride = _ => Task.CompletedTask;

    /// <summary>[idle-memory · dalga 3 · T21 fix dalgası · Exit] Kabuğun çıkış dikişlerini no-op yapar: tam çıkış (tepsi → Exit, çıkış isteği)
    /// koşan bir test gerçek uygulamayı kapatmaz, onay için pencereyi öne getirmez ve göstermez. Çağrıyı SAYAN testler kendi sahtelerini takar (<c>CloseToTrayTests</c>,
    /// <c>HiddenSurfaceTests</c>); yalnız etkisizleştiren testlerin TEK satırı budur.</summary>
    public static void NeutralizeExitSeams(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.ShutdownApplication = () => { };
        window.ConfirmExitInFront = () => { };
        window.BringForwardForExit = () => { };
    }

    /// <summary>[perf Faz B · B3] Stop/Esc testlerinin ortak kurulumu: <see cref="New"/> (ya da <paramref name="projectNames"/>
    /// verilirse <see cref="NewWithProjects(TempDir, string[])"/>) + repo kökü + gönderimin kabulü (<see cref="AcceptSends"/>) +
    /// giden komutların toplandığı liste (<c>DebugOnCommandSent</c>). <c>EscStopTests</c> ile <c>StopNowTests</c> aynı kurulumu
    /// paylaşır (kopya YASAK).</summary>
    public static (MainWindow window, RunViewModel vm, List<IpcCommand> sent) NewWithSends(
        TempDir uiStateDir, string[]? projectNames = null)
    {
        MainWindow window;
        RunViewModel vm;
        if (projectNames is null) (window, vm) = New(uiStateDir);
        else (window, vm, _) = NewWithProjects(uiStateDir, projectNames);
        vm.RootPath = @"C:\src\OSYS";
        AcceptSends(vm);
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;
        return (window, vm, sent);
    }

    /// <summary>[perf Faz B · B3 takip] <paramref name="text"/>'in kaç SATIRI <paramref name="line"/>'ı içerir — konsol satırı
    /// sayan testlerin TEK sayım yeri (<c>EscStopTests</c> ile <c>StopNowTests</c> aynı işi ayrı ayrı yazmıştı; [son temizlik · madde 15]
    /// <c>SettingsDialogTests</c>'in <c>CountOccurrences</c>'ı ve <c>StartWithWindowsTests</c>'in <c>Count</c>'u da — ikisi de konsol satırı
    /// sayıyordu).</summary>
    public static int Occurrences(string text, string line) =>
        text.Split('\n').Count(l => l.Contains(line, StringComparison.Ordinal));

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
    public static void StartBuild(RunViewModel vm, params string[] names) => StartBuildIn(vm, logDirectory: null, names);

    /// <summary>[idle-memory · dalga 2 · spec §6.3] <see cref="StartBuild"/> + motorun bildirdiği koşu klasörü
    /// (<c>runStarted.LogDirectory</c>): VM koşu anlatısını o klasördeki dosyaya yayar. Ayrı adlıdır — <see cref="StartBuild"/>'e
    /// <c>params</c>'tan önce isteğe bağlı bir klasör parametresi eklenemezdi: <c>StartBuild(vm, "A")</c> "A"yı klasör sanırdı.
    /// Klasörü <see cref="SpoolDir"/> verir; dispose'u spool'u kapatır.</summary>
    public static void StartBuildWithLogDirectory(RunViewModel vm, string logDirectory, params string[] names)
    {
        ArgumentNullException.ThrowIfNull(logDirectory);
        StartBuildIn(vm, logDirectory, names);
    }

    /// <summary><see cref="StartBuild"/> ile <see cref="StartBuildWithLogDirectory"/>'nin TEK gövdesi: olay tek yerde kurulur.</summary>
    private static void StartBuildIn(RunViewModel vm, string? logDirectory, string[] names)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(names);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, Math.Max(names.Length, 1), 1, "Debug", 0, LogDirectory: logDirectory));
    }

    /// <summary>[perf A1 fix 1] Koşu biter: her proje derlenir (<c>projectStarted</c> + <c>projectSucceeded</c>) ve
    /// <c>runCompleted</c> gelir. Bitiş finali grafa bu akıştaki <c>Phase</c> değişiminden gelir
    /// (<c>MainWindow.OnVmPropertyChangedForGraph</c>); derlenen proje yoksa final zaten oynamaz.</summary>
    public static void FinishBuild(RunViewModel vm, params string[] names)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(names);
        BuildProjects(vm, names);
        CompleteRun(vm, names.Length);
    }

    /// <summary>[perf Faz A temizlik] Koşu biter (<c>runCompleted</c>, tümü başarılı): <paramref name="succeeded"/> koşunun başarılı
    /// proje sayısıdır ve olay akışının "Done" satırı ("N succeeded") onu okur — sayı koşunun gerçek toplamını taşımalıdır.
    /// <see cref="FinishBuild"/> bunu çağırır; koşunun bir kısmını <see cref="StartProject"/>/<see cref="SucceedProject"/> ile ayrı
    /// süren testler (gizli pencere pini) koşuyu bununla kapatır.</summary>
    public static void CompleteRun(RunViewModel vm, int succeeded)
    {
        ArgumentNullException.ThrowIfNull(vm);
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, succeeded, 0, 0, 0, 100));
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

    /// <summary>[perf Faz A · A8] Motor bir projenin çıktısından bir satır yazdı (<c>projectLog</c>): VM'in konsol modeline (koşu
    /// anlatısı ve proje logu) düşer. Olayın kimlik/ad çifti (<see cref="IdOf"/>) tek yerde durur; <paramref name="lineNumber"/>
    /// motorun projeye özgü satır sayacıdır.</summary>
    public static void LogLine(RunViewModel vm, string name, int lineNumber, string text)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(text);
        vm.OnEvent(new ProjectLogEvent("r1", IdOf(name), lineNumber, text));
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

    /// <summary>Motorun "Sync bitti" cevabı (<c>syncCompleted</c>): <paramref name="projectCount"/> proje, döngü yok. Olay TEK
    /// yerde kurulur — fixture'ın açılış Sync'i (<see cref="NewWithProjectsOn"/>), <see cref="ReplySync"/> ve bitişi tek
    /// başına süren testler aynı olayı kullanır (kopya YASAK).</summary>
    public static SyncCompletedEvent SyncCompleted(int projectCount = 1) =>
        new("main", "sha1234", false, projectCount, 0);

    /// <summary>[task 3] Motorun bir Sync'e cevabı: başlangıç, verilen topoloji (<see cref="Node"/> kuralıyla,
    /// <see cref="NewWithProjects"/> ile AYNI yapı üretilebilsin diye), tamamlanma.</summary>
    public static void ReplySync(RunViewModel vm, params (string Name, string? Layer)[] nodes)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(nodes);
        vm.OnEvent(new SyncStartedEvent(vm.RootPath, "main"));
        vm.OnEvent(new WorkspaceTopologyEvent([.. nodes.Select((n, i) => Node(n.Name, i, n.Layer))], [], [], []));
        vm.OnEvent(SyncCompleted(nodes.Length));
    }

    /// <summary>[perf Faz A temizlik] <c>ReplySync</c>'in yalnız adları alan hâli: <c>NewWithProjects</c>'in adlı aşırı yüklemesiyle
    /// AYNI topoloji.</summary>
    public static void ReplySync(RunViewModel vm, string[] names) => ReplySync(vm, Unlayered(names));

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

    /// <summary>
    /// [perf Faz A · A8/A9] Pencerenin kabuk içeriğini ekran dışı GERÇEK bir pencereye taşır; <see cref="MainWindow"/> kurulu kalır
    /// (tik, VM kablajı, kapılar) ama içeriği barındırmaz. <see cref="Realize"/>'ın headless ağacında <c>Loaded</c> hiç ateşlenmez;
    /// görünümünü <c>Loaded</c>'da kuran yüzeyler (ör. <c>ActionBar</c>'ın sayaç chip'leri) ancak burada kurulur — <c>Loaded</c>
    /// dispatcher'dan gelir, çağıran pompalar. <c>DataContext</c> pencereden miras alınıyordu, açıkça taşınır.
    /// <c>ContentPresenter</c> görsel çocuğunu ancak bir şablon/ölçüm turunda bırakır; yeni ebeveyn onu ancak bundan sonra alabilir.
    /// Dönen pencereyi çağıran kapatır.
    /// </summary>
    public static Window HostOffscreen(MainWindow window, double width = 1400, double height = 800)
    {
        ArgumentNullException.ThrowIfNull(window);
        var content = (FrameworkElement)window.Content;
        var context = window.DataContext;
        window.Content = null;
        if (VisualTreeHelper.GetParent(content) is FrameworkElement presenter) presenter.Measure(new Size(width, height));
        content.DataContext = context;
        return DsResources.Realize(DsResources.NewHost(), content, width, height);
    }

    /// <summary>[perf Faz A · temizlik 2b] Kabuğun alt çubuğu: ağaçtaki TEK örnek. Chip'lerini <c>Loaded</c>'da kurduğu için headless
    /// ağaçta chip'siz durur (bkz. <see cref="WithLoadedActionBar"/>).</summary>
    public static ActionBar ActionBarOf(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return DsResources.Descendants(window.Shell).OfType<ActionBar>().Single();
    }

    /// <summary>
    /// [perf Faz A · temizlik 2b] Alt çubuk testlerinin TEK girişi: kabuk içeriğini ekran dışı gerçek bir pencereye taşır
    /// (<see cref="HostOffscreen"/>), çubuğun yüklenmesini bekler (chip'ler ve Build düğmesinin komutu <c>Loaded</c>'da kurulur), gövdeyi
    /// çalıştırır ve pencereyi — gövdedeki bir iddia patlasa da — kapatır. <c>Loaded</c> ÖNCESİNİ de sınayan test
    /// (<see cref="HiddenSurfaceTests.An_action_bar_that_starts_hidden_builds_its_chips_once_from_the_current_counters_on_first_show"/>)
    /// çubuğu önce <see cref="ActionBarOf"/> ile alır, gizli işaretler ve ancak sonra buraya girer; gövdeye gelen çubuk o örnektir.
    /// [kullanıcı kararı 2026-10-19] <c>HiddenSurfaceTests</c>'ten buraya taşındı: pencereye tıkla dönüş testleri
    /// (<c>WindowReturnClickTests</c>) de yüklenmiş çubuk ister.
    /// </summary>
    public static void WithLoadedActionBar(MainWindow window, Action<Window, ActionBar> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var host = HostOffscreen(window);
        try
        {
            var bar = ActionBarOf(window);
            DispatcherPump.PumpUntil(() => bar.IsLoaded, TimeSpan.FromSeconds(5)); // chip'ler Loaded'da kurulur
            body(host, bar);
        }
        finally
        {
            host.Close();
        }
    }

    /// <summary>
    /// Bir proje satırının menüsünü <paramref name="open"/> ile açar, popup'ın GERÇEKTEN açılmasını bekler ve menünün maddelerini verir; menü AÇIK
    /// kalır, kapatmak çağıranın işidir. "Satır menüsünü aç ve açılmasını bekle" bloğunun TEK yeri: <c>ProjectRowInputTests</c>,
    /// <c>RowMenuPlacementTests</c> ve <c>WindowReturnClickTests</c> aynı beş satırı beş ayrı yerde taşıyordu (kopya YASAK).
    /// <para><paramref name="open"/> açma biçimidir ve üretimdeki kapıya varır: ⋯'yi işaretlemek (<c>actions.MoreButton.IsChecked = true</c>),
    /// ⋯'ye kullanıcı gibi basmak (<see cref="CommandPress.Toggle"/>) ya da satıra sağ bırakmak (<see cref="MouseInput.Release"/>) — hepsi
    /// ⋯'nin <c>Checked</c>'inden geçer ve menünün kapısı orada okunur (<c>ProjectRow.EnsureActions</c>). Hover bloğu
    /// (<c>ProjectRow.Actions</c>) açmadan ÖNCE kurulmuş olmalıdır: <c>Opened</c>'a ona abone olunarak beklenir.</para>
    /// <para>Pompalanır, çünkü <c>IsOpen = true</c> tek başına yetmez: popup'ın kendi penceresi ancak dispatcher pompalanınca doğar ve
    /// maddeleri menü o pencerede yüklenince kurar — <c>IsOpen</c>'a bakan bir koşul anında doğru çıkar ve hiç pompalamazdı. Menü
    /// açılmadıysa ön-koşul mesajı bunu söyler.</para>
    /// </summary>
    public static List<Border> OpenRowMenu(ProjectRowActions actions, Action open)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(open);
        bool opened = false;
        void OnOpened(object? sender, EventArgs e) => opened = true;
        actions.RowMenu.Opened += OnOpened;
        open();
        DispatcherPump.PumpUntil(() => opened, TimeSpan.FromSeconds(2));
        actions.RowMenu.Opened -= OnOpened;
        Assert.True(opened, "ön-koşul: satır menüsü açılmadı");
        actions.RowMenuContent.UpdateLayout();
        return actions.RowMenuContent.Rows.ToList();
    }
}
