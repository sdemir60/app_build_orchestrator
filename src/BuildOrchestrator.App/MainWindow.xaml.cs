using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.App;

public partial class MainWindow : Window
{
    /// <summary>Bu pencerenin global kısayol kaydının id'si (WM_HOTKEY wParam'ı) — tek hotkey, sabit id.</summary>
    private const int GlobalHotkeyId = 0xB0;

    private readonly EngineHost _engine;
    private readonly RunViewModel _vm;
    private readonly ConsoleBatcher _console;
    private readonly CancellationTokenSource _consoleCts = new();
    private readonly DispatcherTimer _elapsedTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    // [T62] Pencere kabuğu: tepsi + ilk-X balloon (K5) + Snap Layouts hook + Alt+B (v7Δ-5).
    // [A13/T1 fix-1 · C1] Store ARTIK ctor'dan gelir (varsayılan üretim yolu birebir aynı: JsonUiStateStore
    // + DefaultPath). Gerekçe <see cref="MainWindow(EngineHost, RunViewModel, ConsoleBatcher, ResourceDictionary, IUiStateStore)"/>'da.
    private readonly IUiStateStore _uiState;
    private readonly FirstCloseBalloonGate _closeBalloon;
    private AppTrayIcon? _tray;
    private HotkeyRegistration? _hotkey;
    private bool _exiting; // tepsi Exit'i (gerçek çıkış) ile X'i (tepsiye küçült) ayıran TEK bayrak

    // [D5/T50] Graf ↔ VM köprüsü. GraphView düğümleri AD ile anahtarlar, VM seçimi ID (yol) ile; iki yönlü ad↔id
    // haritası topoloji değişince yeniden kurulur. _suppressGraphSelection: VM→view seçim itişinin GraphView'de
    // uyandırdığı SelectionChanged echo'sunu view→VM dalında yok sayar (aksi halde döngü seçimi geri alırdı).
    private readonly Dictionary<string, string> _graphIdByName = new(StringComparer.Ordinal);          // Ad → Id
    private readonly Dictionary<string, string> _graphNameById = new(StringComparer.OrdinalIgnoreCase); // Id → Ad
    private bool _suppressGraphSelection;

    // [E4/T48] Üç panelin auto-scroll'unu hakem eden merkezi arbiter (frontier follow'u seçime göre gate eder;
    // paneller bölgesel suppress'lerini buna bildirir — bir panelde kaydırmak diğerlerini duraklatmaz).
    private readonly ScrollArbiter _scrollArbiter = new();
    // [E4/T48] Liste satır sırası (başlık hariç) — SetGroups ile AYNI sıra; FollowRow/SelectRow satır index'i buradan
    // (her 200ms tick'te BuildLayerGroups'u yeniden kurmamak için yalnız topoloji değişiminde tazelenir).
    private IReadOnlyList<ProjectRowViewModel> _orderedRows = [];
    // [A13/T2 · 2.5] En son listeye verilen GÖRÜNÜR satır kümesinin imzası — bkz. RefreshVisibleRows guard'ı.
    private string _visibleRowSignature = "";

    /// <param name="resourceScope">
    /// [T49 FINAL PASS] ÜRETİMDE null. Pencerenin token'ları (bkz. aşağıdaki <c>FindResource</c>) üretimde
    /// <c>Application.Resources</c>'tan çözülür — App.xaml'in merge zinciri. Headless realize testinde
    /// <see cref="Application"/> YOKTUR ve bir <see cref="Window"/>'un kaynak zincirine dışarıdan girmenin başka
    /// yolu yoktur (üstünde ebeveyn yok); test AYNI zinciri (Motion→Tokens→Icons→Controls) buradan enjekte eder.
    /// Bu dikiş olmadan MainWindow.xaml hiçbir testte realize EDİLEMEZ — c6e9a21'in launch-fatal sınıfının
    /// (Double token → GridLength/Thickness) testsiz kalan son kökü buydu.
    /// </param>
    /// <param name="uiState">
    /// [A13/T1 fix-1 · C1] ÜRETİMDE null → <see cref="JsonUiStateStore"/> + <see cref="JsonUiStateStore.DefaultPath"/>
    /// (<c>%LOCALAPPDATA%\BuildOrchestrator\ui-state.json</c>), yani davranış eskisiyle BİREBİR aynıdır.
    ///
    /// <para><b>Neden gerekli — ölçülen gerçek:</b> kalıcı yazma yolu pencerenin <c>Show()</c> edilmesine BAĞLI
    /// DEĞİLDİR; abonelik ctor'da kurulur (<c>Shell.LayoutChanged += OnShellLayoutChanged</c>) ve oradan
    /// <c>_uiState.Save(...)</c>'a gider. Yani title-bar layout düğmesine basan bir TEST, pencereyi hiç
    /// göstermeden KULLANICININ GERÇEK tercih dosyasını yeniden yazardı (ve testler arası sıraya bağlı bir
    /// yan etki bırakırdı). Bu dikiş, <paramref name="resourceScope"/> ile AYNI desende, o yolu teste
    /// yönlendirilebilir kılar.</para>
    /// </param>
    public MainWindow(EngineHost engine, RunViewModel vm, ConsoleBatcher console,
        ResourceDictionary? resourceScope = null, IUiStateStore? uiState = null)
    {
        InitializeComponent();
        if (resourceScope is not null) Resources.MergedDictionaries.Add(resourceScope);
        _uiState = uiState ?? new JsonUiStateStore(JsonUiStateStore.DefaultPath);
        _engine = engine;
        _vm = vm;
        _console = console;
        DataContext = _vm;
        _closeBalloon = new FirstCloseBalloonGate(_uiState);

        // [T35 fold #1] Title-bar yüksekliğinin TEK kaynağı Size.TitleBarHeight token'ıdır: WindowChrome
        // (CaptionHeight = sürüklenebilir başlık bandı) VE title-bar satırı ONDAN türetilir. WindowChrome bir
        // Freezable olduğundan DynamicResource onda güvenilir çözülmez → kod-tarafı kurulur (kesin çalışır).
        double titleBarHeight = (double)FindResource("Size.TitleBarHeight");
        TitleRow.Height = new GridLength(titleBarHeight);
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = titleBarHeight,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        // [K8] maximize'da restore glyph'i (çizilmiş geometri, T64) + butonun duruma bağlı UIA adı.
        CaptionGlyphs.BindMaxButton(this, MaxButton, MaxGlyph);
        // [dotnet/wpf#3887] Maximize taşma düzeltmesi. Kablaj glyph'le AYNI desende ve AYNI gerekçeyle DP
        // izleyicisidir (bkz. MaximizeFix.Bind): pencere DOĞUŞTAN maximized açıldığından StateChanged'e
        // dayanan bir kurulum ilk açılışta HİÇ koşmaz.
        MaximizeFix.Bind(this, RootShell);

        // [T35] Kalıcı yerleşimi geri yükle; kullanıcı değişiklikleri (mod düğmesi / split sürükleme sonu) persist.
        // GraphView'ın MotionSettings'i Loaded'dan ÖNCE atanmalı (GraphView.xaml.cs:111-119 sözleşmesi).
        Shell.GraphHost.MotionSettings = App.Motion;
        // [E4/T48] Üç paneli merkezi scroll arbiter'a bağla: frontier follow'u seçime göre gate eder, konsol/stream
        // bölgesel suppress'lerini (dibe yapışık mı) bildirir. StickyLayerList reveal-hero'su App.HeroMotion/App.Motion'ı
        // TAZE okur (enjeksiyon gerekmez — üretimde ikisi de kurulu).
        Shell.ProjectsList.Arbiter = _scrollArbiter;
        Shell.ConsoleViewControl.Arbiter = _scrollArbiter;
        Shell.EventStreamControl.Arbiter = _scrollArbiter;
        var saved = _uiState.Load();
        var layout = new LayoutState(saved.LayoutMode, saved.ColPct, saved.LeftPct, saved.RightPct);
        Shell.ApplyLayout(layout);
        SyncModeButtons(layout.Mode);
        Shell.LayoutChanged += OnShellLayoutChanged;

        // [D6 fold — C2] İş akışı tercihlerini kalıcı durumdan SEED et; sonra değişimlerini persist et. Seed ÖNCE,
        // abonelik SONRA — seed'in kendisi kaydetme fırtınası tetiklemesin. Perf'te kalıcı değer yoksa VM varsayılanı
        // (Balanced/4, C2 F2) KORUNUR (SetPerfMode PerfMode + Parallelism'i birlikte tutar).
        // [D7 M3] Son repo'yu SEED et — açılışta hatırlanır ama SEED-BUT-IDLE: DOĞRUDAN RootPath set'i yalnız
        // OnRootPathChanged'i (Empty→Boot) sürer, otomatik Sync YOKtur (ChangeRepositoryAsync DEĞİL — o SyncAsync
        // tetikler). Repo bilinir, kullanıcı hazır olunca Sync/Build'e basar. İlk-koşuda (kayıtlı repo yok →
        // { Length: > 0 } guard'ı) Phase Empty KALIR ve E2 "Pick a repository" daveti korunur.
        if (saved.RepositoryRoot is { Length: > 0 } repo) _vm.RootPath = repo;
        if (saved.Configuration is { } cfg) _vm.Configuration = cfg;
        if (saved.Branch is { } br) _vm.Branch = br;
        _vm.UseWorktree = saved.UseWorktree;
        _vm.WorktreeName = saved.WorktreeName;
        if (saved.PerfMode is { } perf) _vm.SetPerfMode(perf);
        // [D7] Kalıcı katman tanımlarını seed et (D7 bu alanın ilk yazıcısı — diskte bugüne dek hep boş). Boşsa
        // LayerPatterns null kalır (motor Count==0'ı "katman yok" olarak ele alır); Settings Save bunu doldurur.
        // [D7 re-review][Fix2] System.Text.Json bir açık JSON "null" token'ı için `= []` initializer'ını EZER ve
        // alanı gerçek null yapar (JsonUiStateStore.Load yalnız JsonException'ı yutar — bu bir NRE, App'i AÇILIŞTA
        // çökertirdi). Null-safe desen (kardeş guard'larla — saved.Configuration is { }/saved.PerfMode is { } —
        // hizalı).
        if (saved.LayerPatterns is { Count: > 0 }) _vm.LayerPatterns = saved.LayerPatterns;
        _vm.PropertyChanged += OnWorkflowPreferenceChanged;

        // [A13/T2 · 2.1] design-v1 §2.1 title-bar bağlamı. AYRI bir abonelik (persist'le AYNI dört alanı dinler
        // ama ONA BAĞLANMAZ): OnWorkflowPreferenceChanged'in tek sorumluluğu kalıcı duruma yazmaktır, görsel
        // tazeleme oraya karışmamalı. Seed ATAMALARINDAN SONRA kurulur ve hemen bir kez elle sürülür — böylece
        // açılışta hatırlanan repo/branch başlıkta ZATEN doğrudur (seed'ler yukarıda, abonelikten önce akıyor).
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(RunViewModel.RootPath) or nameof(RunViewModel.Branch)
                or nameof(RunViewModel.UseWorktree) or nameof(RunViewModel.WorktreeName)) RefreshTitleContext();
        };
        // [T2 fix-1 · I-G] EffectiveWorktreeName auto-ad dalında <see cref="RunViewModel.Worktrees"/>'e de
        // BAĞLIDIR (AutoWorktreeName mevcut worktree sayısını sayar) — envanter geldiğinde gösterilen ad
        // değişebilir. Yalnız dört özelliği dinlemek bu kaynağı KAÇIRIYORDU.
        _vm.Worktrees.CollectionChanged += (_, _) => RefreshTitleContext();
        RefreshTitleContext();

        // [D1] Proje listesini katman gruplarıyla besle. SetGroups YALNIZ topoloji/gruplama değişiminde (tam
        // reset orada meşru — StickyLayerList); statü tikleri satır VM'lerinin INotifyPropertyChanged'inden akar.
        // [D5] Aynı topoloji sinyalinde grafı da yeniden kur (SetGraph = tam yeniden inşa + reveal stagger).
        _vm.TopologyChanged += (_, _) => { RefreshProjectGroups(); RebuildGraph(); };
        RefreshProjectGroups();
        RebuildGraph();

        // [E2/T10] Proje listesi boş-durum davetleri: repo yok → "Pick a repository…" + Choose Folder; repo
        // Sync'lendi ama 0 proje → "No projects found under this folder." Karar SAF (ListInvite.Resolve); burada
        // yalnız tetik + uygulama. Choose Folder aynı repo-değiştir yolunu kullanır (PickFolder → ChangeRepositoryAsync).
        Shell.ChooseFolderButton.Click += OnChooseFolder;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(RunViewModel.Phase) or nameof(RunViewModel.HasWorkspace)
                or nameof(RunViewModel.RootPath)) RefreshListInvite();
        };
        _vm.Projects.CollectionChanged += (_, _) => RefreshListInvite();
        RefreshListInvite();

        // [A13/T2 · 2.5] Proje listesi ARTIK GÖRÜNÜR kümeyi (VisibleProjects) gösterir. Ölçülen kusur: liste
        // TÜM Projects'ten besleniyordu ve VisibleProjects'in ÜRETİMDE HİÇ TÜKETİCİSİ YOKTU — yani statü
        // chip'leri de Ctrl+F filtre kutusu da listede görsel olarak hiçbir şey yapmıyordu.
        //
        // TEK sinyal VisibleProjects'tir: hem ActiveFilter/ProjectQuery değişimi ([NotifyPropertyChangedFor])
        // hem de satır statüsü değişimi (RefreshRunSurface) onu yayınlar — böylece "Failed" filtresi açıkken
        // YENİ bir hata listeye CANLI düşer. Bu, TopologyChanged yolundan AYRI ve ona DOKUNMAZ (E2/§5-b kararı
        // korunur: graf yalnız YAPI değişince yeniden kurulur).
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(RunViewModel.VisibleProjects)) return;
            RefreshVisibleRows();
            // [A13/T2 · 2.4] Görünür küme boşaldıysa panel NEDENİNİ söyler ("No projects match this filter.")
            // — "hiç proje yok"tan AYRI durum; karar SAF ListInvite.Resolve'de.
            RefreshListInvite();
        };

        // [A13/T2 · 2.3] PROJECTS başlığındaki kaldırılabilir filtre chip'i (design-v1 §2.4). Görünürlük/etiket
        // buradan sürülür (SetListInvite ile AYNI desen: karar dışarıda, kabuk yalnız uygular); chip'e tıklamak
        // Σ chip'iyle AYNI yolu kullanır (ToggleFilter(null)) — ikinci bir "filtreyi temizle" yolu AÇILMAZ.
        Shell.ProjectFilterChip.Click += (_, _) => _vm.ToggleFilter(null);
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(RunViewModel.ActiveFilter)) return;
            RefreshFilterChip();
            // [E4] Filtre, seçimle AYNI SINIFTAN bir "şu an şuna bakıyorum" beyanıdır → frontier follow durur
            // (karar arbiter'da, tek yerde: ScrollArbiter.CanFollowFrontier).
            _scrollArbiter.SetFilter(_vm.ActiveFilter is not null);
        };
        RefreshFilterChip();
        _scrollArbiter.SetFilter(_vm.ActiveFilter is not null); // kalıcı durumdan gelen bir filtreyle açılış

        // [D5] Graf seçimi (AD) → VM seçimi (ID); echo koruması OnGraphSelectionChanged'de. VM statü/seçim/run
        // sinyalleri → grafı besle (UpdateStatuses/IsSettled/SelectedNode) — bkz. OnVmPropertyChangedForGraph.
        Shell.GraphHost.SelectionChanged += OnGraphSelectionChanged;
        _vm.PropertyChanged += OnVmPropertyChangedForGraph;

        _engine.EngineExited += code => Dispatcher.Invoke(() =>
        {
            // [Task 16 — It-2 devir §8] VM'in run-state'i (IsStarting/IsRunning/CanContinue) bu sinyale bağlıdır.
            // Motor durumu görsel şeridi (sticky ribbon) T37'nin işidir — C1'de yalnız VM state'i güncellenir.
            _vm.OnEngineExited(code);
        });
        // [A13.2/Kısıt 4] YALNIZ projectLog YÜKSEK frekanslı akan log satırıdır — VM'in o dalı ConsoleBatcher.Post
        // (kilitsiz) kullanır, ObservableProperty'e DOKUNMAZ; marshal OLMADAN doğrudan çağrılabilir. Diğer TÜM
        // event'ler UI thread'ine taşınır.
        _engine.EventReceived += ev =>
        {
            if (ev is ProjectLogEvent) _vm.OnEvent(ev);
            else Dispatcher.InvokeAsync(() => _vm.OnEvent(ev));
        };

        _elapsedTimer.Tick += (_, _) =>
        {
            _vm.TickElapsed();
            // [T56/3a] "N lines" TAM tampon sayacı — 200ms'de bir aktif tampondan tazelenir (marshal-free log
            // yolundan ObservableProperty tetiklemek yerine; render dilimi DEĞİL, Ek A #23).
            Shell.ConsoleHeaderControl.SetLineCount(_vm.GetActiveLineCount());
            // [D5] Koşarken grafı düzenli besle: kamera frontier'i yumuşak takip etsin, queued→building→done
            // geçişleri ≤200ms'de yansısın. GraphView sık UpdateStatuses'a göre tasarlandı (Zeno/pulse guard'ları).
            // Boşta itmeyiz (statü değişimi zaten Counters/topoloji event'lerinden gelir — gereksiz churn yok).
            // [E4/T48] Koşarken frontier'i (ilk building satır) yumuşak takip et (arbiter seçim varken reddeder).
            if (_vm.IsMidRunLocked) { PushGraphStatuses(); FollowFrontier(); }
        };
        _elapsedTimer.Start();

        // [T56/3a] Konsol modu ActiveProjectId'yi izler: null → anlatı başlığı. (Proje-loguna geçiş başlığı
        // OnSelectedProjectChanged'de senkron kurulur — bkz. reseed flicker / Solution B.)
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(RunViewModel.ActiveProjectId)) return;
            if (_vm.ActiveProjectId is null) Shell.ConsoleHeaderControl.ShowNarrative(_vm.GetActiveLineCount());
        };
        // [D4] Kart seçimi konsol modunu sürer: seçilen proje logunu (dikişli) yükle → başlık+gövde SENKRON
        // proje-loguna geçir; seçim kalkınca run anlatısına dön. Kart vurgusu ayrı akar (OnSelectedProjectIdChanged).
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RunViewModel.SelectedProjectId)) _ = OnSelectedProjectChangedAsync();
        };
        // [E4/T48] Seçim → frontier arbiter (seçim > follow) + liste seçim-scroll (SelectRow 90ms / ClearSelection).
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RunViewModel.SelectedProjectId)) UpdateFrontierSelection();
        };
        Shell.ConsoleHeaderControl.BackRequested += (_, _) => OnBack();
        // [A13/T3 fix-2 · 5] Konsolun idle "ready" damgası VM'in duvar saatinden beslenir — anlatı satırları
        // (RunViewModel.ComposeNarrativeLine) ve event stream ile ORTAK kaynak. Lambda ZORUNLU: değer kopyası
        // alınırsa VM'in saati sonradan değiştiğinde idle satırı eski saatte donar (pin: MainWindowRealizeTests).
        Shell.ConsoleViewControl.WallClock = () => _vm.WallClock();

        Loaded += async (_, _) =>
        {
            // [D4/T56-UI] Boşta (idle/boot) konsol tek satır "ready" (dim) gösterir — ilk anlatı satırı gelince
            // (AppendNarrativeBatch) temizlenir.
            if (_vm.GetActiveLineCount() == 0) Shell.ConsoleViewControl.ShowReady();
            await StartEngineAsync();
        };
        Closed += (_, _) => { _consoleCts.Cancel(); _console.Complete(); _elapsedTimer.Stop(); };

        // [M-3 fix wave] Oturum kapanışı Closing'i tetikler ama e.Cancel'i YOK SAYAR — _exiting hâlâ false ise
        // OnClosing tray'e düşer ve K5 balloon'unu yakabilir. SessionEnding (Closing'den ÖNCE) _exiting'i erken set eder.
        // [T49 FINAL PASS] Null-kontrol yalnız headless realize testi içindir (orada Application YOKTUR); üretimde
        // Application.Current her zaman kuruludur ve abonelik AYNEN kurulur.
        if (Application.Current is { } app) app.SessionEnding += OnSessionEnding;

        SetupKeyboardShortcuts();
        SetupAboutButtonTooltip();
        _ = RunConsolePumpAsync();
    }

    // ==================================== [E5/T46] Klavye kısayolları (K6) ====================================

    /// <summary>[E5/T46 · K6 birebir] Pencere geneli InputBinding'leri kurar. Ctrl/Shift+F5 doğrudan
    /// <see cref="RunViewModel.RebuildCommand"/>'a bağlanır (KeyBinding CanExecute'i ONURLANDIRIR); çıplak F5 durum-
    /// dallı (<see cref="OnF5Pressed"/>); Ctrl+F filtreyi odaklar; Esc zinciri (<see cref="OnEscapePressed"/>) EN
    /// ÜST açık katmanı kapatır. Otorite: <see cref="KeyboardShortcuts"/> (SAF karar) + BuildApp.jsx:1302-1319.</summary>
    private void SetupKeyboardShortcuts()
    {
        // NİYET → ICommand: Rebuild doğrudan VM komutu (CanExecute onurlanır); diğerleri kod-tarafı aksiyonlar.
        // TUŞ→NİYET eşlemesi SAF <see cref="KeyboardShortcuts.WindowBindings"/>'te (test pinler) — burada yalnız
        // niyetleri komutlara bağlar ve tabloyu iterasyonla KeyBinding'lere çeviririz (kablaj tek yerde).
        var commandForIntent = new Dictionary<WindowIntent, ICommand>
        {
            [WindowIntent.Rebuild] = _vm.RebuildCommand,                    // Ctrl/Shift+F5 → doğrudan
            [WindowIntent.F5StateBranch] = new RelayCommand(OnF5Pressed),   // çıplak F5 → Stop/Continue/Build (duruma göre)
            [WindowIntent.FocusFilter] = new RelayCommand(() => Shell.FocusProjectFilter()),
            [WindowIntent.ShowAbout] = new RelayCommand(OnAboutRequested),   // F1 → About (modal açıkken no-op)
            [WindowIntent.Escape] = new RelayCommand(OnEscapePressed),
        };
        foreach (var b in KeyboardShortcuts.WindowBindings)
            InputBindings.Add(new KeyBinding(commandForIntent[b.Intent], b.Key, b.Modifiers));
    }

    /// <summary>Çıplak F5: koşarken → Stop, stopped'ta → Continue, aksi → Build (v7 K6). Karar SAF
    /// <see cref="KeyboardShortcuts.Resolve"/>'te; burada yalnız uygulanır (CanExecute reddederse no-op).</summary>
    private void OnF5Pressed() =>
        DispatchShortcut(KeyboardShortcuts.Resolve(Key.F5, ModifierKeys.None, _vm.IsMidRunLocked));

    private void DispatchShortcut(ShortcutAction action)
    {
        // ShortcutAction→ICommand eşlemesi SAF <see cref="KeyboardShortcuts.CommandFor"/>'da (test pinler); burada
        // yalnız uygulanır (CanExecute reddederse no-op).
        var command = KeyboardShortcuts.CommandFor(action, _vm);
        if (command is not null && command.CanExecute(null)) command.Execute(null); // CanExecute'i onurlandır
    }

    /// <summary>Esc zinciri: EN ÜST açık katmanı kapatır (dialog &gt; popover/menü &gt; seçim), alta sızmaz.
    /// Dialog KATMANI çoğu zaman SettingsDialog'un KENDİ Esc'iyle (odak-tuzağı içinde, handled) kapanır; bu
    /// pencere-seviyesi güvenlik ağı odak dialog dışındayken de doğru katmanı seçer. Filtre input'undaki Esc
    /// buraya HİÇ ULAŞMAZ (ShellRoot.OnFilterKeyDown handled eder).</summary>
    /// <summary>[About] Info butonunun tooltip'i — metin ELLE yazılmaz, <see cref="ShortcutCatalog"/>'dan gelir
    /// (About sekmesindeki F1 satırıyla AYNI cümle; kopya YASAK). XAML'de bir <c>x:Static</c> sarmalayıcı
    /// gerekmesin diye kod-tarafı kurulur — diğer title bar tooltip'leriyle aynı <c>AppTooltip.Side</c>
    /// yerleşimini kullanır.</summary>
    private void SetupAboutButtonTooltip()
    {
        // [design-v1.2.1 §2.1] Tooltip cümlenin SONUNA jesti ekler: "… (F1)". Cümle de jest de katalogdan
        // gelir — ikisi de burada elle yazılmaz.
        var about = ShortcutCatalog.Get(ShortcutId.About);
        var tooltip = new System.Windows.Controls.ToolTip
        {
            Content = $"{about.Description} ({about.Gestures[0]})",
        };
        // Yerleşim gear'ınkiyle AYNI olmalı (ikisi de title bar'da, aşağı açılır) — değer ORADAN okunur,
        // ikinci kez yazılmaz.
        AppTooltip.SetSide(tooltip, AppTooltip.GetSide((System.Windows.Controls.ToolTip)GearButton.ToolTip));
        InfoButton.ToolTip = tooltip;
    }

    /// <summary>[About] Bir modal AÇIK MI — Esc zinciri, F1 kapısı ve gear kapısı bu TEK karardan beslenir
    /// (üç yerde ayrı ayrı sorulsaydı biri güncellenip diğerleri unutulurdu).</summary>
    private bool AnyDialogOpen =>
        SettingsOverlay.Visibility == Visibility.Visible || AboutOverlay.Visibility == Visibility.Visible;

    private void OnEscapePressed()
    {
        switch (KeyboardShortcuts.ResolveEsc(AnyDialogOpen, Shell.AnyPopoverOpen, _vm.SelectedProjectId is not null))
        {
            // [design-v1.2.1 §2.10] About ÖNCE kapanır: iki modal aynı anda açık olabilir (F1, Settings'in
            // üstüne biner) ve Esc her zaman EN ÜST katmanı indirir — alta sızmaz, alttaki taslak durur.
            case EscAction.CloseDialog:
                if (AboutOverlay.Visibility == Visibility.Visible) AboutOverlay.CloseDialog();
                else SettingsOverlay.CloseDialog();
                break;
            case EscAction.ClosePopovers: Shell.CloseAllPopovers(); break;
            case EscAction.ClearSelection: _vm.SelectProject(null); break;
        }
    }

    /// <summary>
    /// [Kısıt 1] <c>ConsoleBatcher.PumpAsync</c> tick'i iptal edilince <see cref="OperationCanceledException"/>
    /// YAKALANMADAN yükselir — burada TEK yerde gözlenir. Flush BATCH BAŞINA TEK <c>Dispatcher.InvokeAsync</c>
    /// ile <see cref="ConsoleView.AppendBatch"/>'e taşınır — satır başına Dispatcher çağrısı YASAK [A13.2].
    /// </summary>
    private async Task RunConsolePumpAsync()
    {
        try
        {
            await _console.PumpAsync((text, gen) => Dispatcher.InvokeAsync(() => AppendConsoleBatch(text, gen)), _consoleCts.Token);
        }
        catch (OperationCanceledException) { /* pencere kapanıyor — beklenen */ }
        catch (Exception ex)
        {
            // [Minor/Fix wave 1] fire-and-forget task'i gözlenmemiş bir exception'la sessizce ölmesin — burada tek
            // gözlem noktası; UI thread affinity garantisi olmadığından doğrudan bir WPF kontrolüne DOKUNULMAZ.
            System.Diagnostics.Debug.WriteLine($"[console pump] gözlenmeyen hata: {ex}");
        }
    }

    /// <summary>[Kısıt 1/A13.2 · D4 review §1] Pump flush'ının hedefi. Kararı SAF <see cref="ConsoleBatchRouter"/>
    /// verir (test edilebilir seam); Window yalnız uygular. <paramref name="batchGen"/>, pump'ın bu batch'i
    /// okuduğu reseed nesli: aradan bir reseed geçtiyse (<c>batchGen &lt; _console.CurrentReseedGen</c>) batch
    /// BAYATTIR ve ATILIR — Solution B'nin senkron doküman-set'inin ardından koşan bir bayat flush'ın taze
    /// dokümana sızmasını (dup/cross-doc) kapatır. Aksi halde: anlatı (null) →
    /// <see cref="ConsoleView.AppendNarrativeBatch"/> (en yeni satır T34 hibrit daktilo); proje-log → ham MSBuild
    /// <see cref="ConsoleView.AppendBatch"/> instant (ham çıktı ASLA harf-harf — DD2).</summary>
    private void AppendConsoleBatch(string text, long batchGen)
    {
        switch (ConsoleBatchRouter.Decide(batchGen, _console.CurrentReseedGen, _vm.ActiveProjectId))
        {
            case ConsoleBatchRouter.Route.Drop: return; // aradan reseed geçti → bayat batch, at
            case ConsoleBatchRouter.Route.Narrative: Shell.ConsoleViewControl.AppendNarrativeBatch(text); break;
            default: Shell.ConsoleViewControl.AppendBatch(text); break; // Route.Raw
        }
    }

    /// <summary>[D4/Solution B] Kart seçimi değişince konsol modunu senkron sürer. Seçim varsa: proje logunu
    /// (dikişli) YÜKLE, sonra başlık + gövde AYNI UI turunda proje-loguna geçir (reseed flicker YOK). Seçim
    /// kalkınca (null): run anlatısına dön. logNotFound/skipped gibi durumlarda ActiveProjectId kurulmaz →
    /// run modunda kalınır.</summary>
    private async Task OnSelectedProjectChangedAsync()
    {
        try
        {
            // [D4 review §3] Karar VM seam'inde (test edilebilir); Window yalnız uygular.
            if (_vm.NextConsoleSelection(out var id) == ConsoleSelection.ShowRun) { ShowRunConsole(); return; }

            await _vm.LoadProjectLogAsync(id!);
            // [D4 review §2/§3] Proje-log yalnız yükleme modu kurduysa (log vardı — guard1) VE seçim hâlâ o
            // projedeyse (guard2, arada select→deselect/başka-id olmadı) gösterilir; aksi halde run modunda kal
            // (§2 donma: deselect-mid-load'da ActiveProjectId zaten null kaldığından burada erken dönülür).
            if (!_vm.ShouldShowLoadedProject(id!)) return;
            var row = _vm.Projects.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (row is null) return;

            Shell.ConsoleHeaderControl.LogTextProvider = () => _vm.GetProjectDocumentText(id!);
            Shell.ConsoleHeaderControl.ShowProjectLog(row.Name, row.State, row.HasDepIssue, _vm.GetActiveLineCount());
            // [Solution B] Doküman TIKLAMA (yükleme tamamlanma) ANINDA senkron kurulur — pump'a bağlı DEĞİL.
            _vm.SeedProjectDocument(id!, text => Shell.ConsoleViewControl.PlayCascade(
                SplitLogLines(text), buildInProgress: row.State == ProjectRowState.Started));
        }
        catch (Exception ex)
        {
            // [RunConsolePumpAsync deseni] fire-and-forget yol gözlenmemiş bir exception'la sessizce ölmesin.
            System.Diagnostics.Debug.WriteLine($"[console mode switch] gözlenmeyen hata: {ex}");
        }
    }

    /// <summary>[3b → D4/Solution B] Run belgesine döner; başlık anlatı moduna ActiveProjectId=null
    /// PropertyChanged'ı üzerinden döner (bkz. constructor). Doküman SENKRON kurulur (reseed flicker YOK).</summary>
    private void ShowRunConsole()
    {
        _vm.ShowRun(); // ActiveProjectId=null → PropertyChanged → ShowNarrative (başlık, aynı tur)
        _vm.SeedRunDocument(text => Shell.ConsoleViewControl.ShowRunDocument(text));
        if (_vm.GetActiveLineCount() == 0) Shell.ConsoleViewControl.ShowReady(); // boş run → idle "ready"
    }

    /// <summary>[3b] ConsoleHeader.BackRequested'tan çağrılır: kart seçimini kaldırır → konsol run anlatısına
    /// döner (bkz. <see cref="OnSelectedProjectChangedAsync"/>).</summary>
    private void OnBack() => _vm.SelectProject(null);

    /// <summary>Dikilmiş proje-log metnini ('\n' sonekli) kaskat için satırlara böler; boş metin → boş dizi
    /// (boş-durum metni ileride buradan gelebilir).</summary>
    private static IReadOnlyList<string> SplitLogLines(string text) =>
        text.Length == 0 ? [] : text.TrimEnd('\n').Split('\n');

    /// <summary>[D1] VM'in katman gruplarını (topolojiden — App'te regex YOK) StickyLayerList'e verir.
    /// <see cref="ProjectRowViewModel"/> nesneleri satır olarak akar; isimsiz grup (null) StickyLayerList'te
    /// başlıksızdır.</summary>
    private void RefreshProjectGroups() => ApplyProjectGroups(reveal: true);

    /// <summary>[A13/T2 · 2.5] Filtre/sorgu (ya da bir satırın statüsü) yüzünden GÖRÜNÜR küme değişti → listeyi
    /// tazele, ama kademeli belirişi (bo-reveal) OYNATMA.
    ///
    /// <para><b>İmza guard'ı (E2/§5-b <c>_lastTopologySignature</c> deseninin eşi):</b> <c>VisibleProjects</c>
    /// bildirimi bir run boyunca HER proje event'inde gelir (<c>RefreshRunSurface</c>). Guard olmadan liste
    /// saniyede onlarca kez tam reset yerdi — oysa filtre yokken küme HİÇ değişmez. Yalnız görünür satırların
    /// sırası/kimliği gerçekten değiştiğinde WPF'e dokunulur.</para></summary>
    private void RefreshVisibleRows()
    {
        if (VisibleRowSignature() == _visibleRowSignature) return;
        ApplyProjectGroups(reveal: false);
    }

    /// <summary>[D1] VM'in katman gruplarını StickyLayerList'e verir. <paramref name="reveal"/> = kademeli
    /// beliriş oynasın mı: topoloji değişimi OYNATIR (yeni liste), filtre tazelemesi OYNATMAZ (aynı listenin
    /// alt kümesi) — gerekçe <see cref="StickyLayerList.SetGroups(IReadOnlyList{StickyLayerList.LayerGroup}, bool)"/>'ta.</summary>
    private void ApplyProjectGroups(bool reveal)
    {
        var groups = _vm.BuildLayerGroups()
            .Select(g => new StickyLayerList.LayerGroup(g.Name ?? "", g.Rows.Cast<object>().ToList()))
            .ToList();
        Shell.ProjectsList.SetGroups(groups, reveal);
        // [E4/T48] Satır sırasını (başlık hariç) önbelleğe al — FollowRow/SelectRow global satır index'i buradan;
        // SetGroups'un kurduğu LayoutMetrics satır sırasıyla BİREBİR (aynı groups kaynağı). Filtre altında da
        // tutarlı kalır: ikisi de AYNI groups'tan türer, yani index'ler görünür listeyi adresler.
        _orderedRows = groups.SelectMany(g => g.Rows).OfType<ProjectRowViewModel>().ToList();
        _visibleRowSignature = VisibleRowSignature();
    }

    /// <summary>Görünür satır kümesinin kimliği+sırası. Ayraç <c>'|'</c>: Windows yol adlarında YASAK bir
    /// karakterdir, dolayısıyla iki farklı küme birleşince aynı imzaya düşemez.</summary>
    private string VisibleRowSignature() => string.Join('|', _vm.VisibleProjects.Select(r => r.Id));

    /// <summary>[E4/T48] Liste sırasındaki (başlık hariç) İLK eşleşen satırın global index'i — <see cref="Controls.LayoutMetrics"/>
    /// satır index'iyle birebir (StickyLayerList.FollowRow/SelectRow bunu bekler). Eşleşme yoksa -1.</summary>
    private int FrontierRowIndex(Func<ProjectRowViewModel, bool> match)
    {
        for (int i = 0; i < _orderedRows.Count; i++)
            if (match(_orderedRows[i])) return i;
        return -1;
    }

    /// <summary>[E4/T48] Seçim değişince: arbiter'a bildir (seçim &gt; follow) + liste seçim-scroll'unu sür (SelectRow
    /// 90ms gecikmeli %35 üst-marjla / ClearSelection follow'u geri açar). design-v1 §2.4/§3.3.</summary>
    private void UpdateFrontierSelection()
    {
        string? id = _vm.SelectedProjectId;
        _scrollArbiter.SetSelection(id is not null);
        if (id is null) { Shell.ProjectsList.ClearSelection(); return; }
        // [E4 fix] AÇIK seçim-scroll frontier'i yeniden devreye alır (ScrollArbiter.Request(Selection) / ScrollAnimator.
        // AnimateTo suppress-temizleme paritesi) — seçimden ÖNCE kurulmuş bir wheel-suppress, kart bırakılınca follow'u
        // bloke etmeye devam etmesin (kart seçmek FollowRow'un ScrollAnimator bayrağını zaten SelectRow→AnimateTo ile temizler).
        _scrollArbiter.Resume(ScrollPanel.Frontier);
        int row = FrontierRowIndex(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (row >= 0) Shell.ProjectsList.SelectRow(row);
    }

    /// <summary>[E4/T48] Koşarken frontier'i (ilk <c>Started</c> satır) yumuşak takip et — arbiter seçim aktifken
    /// bunu reddeder (seçim &gt; follow, <c>BuildApp.jsx:1388</c>). Bölgesel wheel-suppress + throttle/dead-band
    /// kararı <see cref="Controls.FollowScrollController"/>'a aittir (StickyLayerList.FollowRow onu uygular).</summary>
    private void FollowFrontier()
    {
        // [E4 fix] Arbiter'ın CANLI frontier gate'i: seçim YOK **ve** frontier bölgesel wheel-suppress YOK. Böylece
        // arbiter'ın _suppressed[Frontier] bit'i yalnız yazılan değil OKUNAN olur — liste wheel'i onu kurar
        // (NotifyUserScroll), near-bottom'a dönüş temizler (StickyLayerList.ResumeFrontierIfNearBottom → Resume).
        int row = FrontierRowIndex(p => p.State == ProjectRowState.Started);
        if (row < 0) return;
        // [frontier resume] Kapıdan ÖNCE: tekerlekle duraklatılmış takip, kullanıcı yeniden "izliyor"
        // sayılabildiğinde geri açılır — frontier'e döndüğünde ya da listeye bir süre hiç dokunmadığında.
        // Frontier satırın indeksi YALNIZ burada bilinir, bu yüzden yakınlık kararı buradan sürülür.
        Shell.ProjectsList.ResumeFrontierIfReengaged(row);
        if (!_scrollArbiter.CanFollowFrontier) return;
        Shell.ProjectsList.FollowRow(row);
    }

    // ==================================== [D5/T50] Graf beslemesi ====================================

    /// <summary>[D5] Topoloji değişince grafı YENİDEN kurar (<see cref="Graph.GraphView.SetGraph"/> = tam inşa +
    /// reveal stagger). Ad↔Id haritası tazelenir; <c>SetGraph</c> düğüm statülerini zaten uygular (GraphNode.Status
    /// GraphBinder'dan gelir) → ayrıca UpdateStatuses gerekmez. Settled durumu + mevcut seçim de yansıtılır.</summary>
    private void RebuildGraph()
    {
        var topology = _vm.Topology;
        _graphIdByName.Clear();
        _graphNameById.Clear();
        foreach (var node in topology)
        {
            _graphIdByName[node.Name] = node.Id;
            _graphNameById[node.Id] = node.Name;
        }

        Shell.GraphHost.SetGraph(GraphBinder.Nodes(topology, RowsById()), GraphBinder.Edges(topology));
        Shell.GraphHost.IsSettled = !_vm.IsMidRunLocked; // koşarken frontier-follow, boşta/bitince merkeze otur
        PushGraphSelection();                            // mevcut seçim taze grafa yansısın
    }

    /// <summary>[D5] Statü/dep-badge/kenar/kamera'yı YERİNDE günceller (geometri korunur, stagger tekrar oynamaz).
    /// Topoloji yokken no-op.</summary>
    private void PushGraphStatuses()
    {
        // [E2/§5-a] Projects boşken (ör. Rebuild başında OnRunStarted listeyi BuildPreview'dan ÖNCE boşaltır) push
        // ETME: RowsById() boş olurdu ve GraphBinder her topoloji düğümünü bir kare Discovered'a "flash" ederdi
        // (queued/dirty statüleri kaybolur, sonra BuildPreview yeniden doldurunca geri gelir). Guard no-op'tur —
        // A13.2 Clear/reset EKLEMEZ; yalnız statü itişini Projects yeniden dolana dek erteler.
        if (_vm.Topology.Count == 0 || _vm.Projects.Count == 0) return;
        Shell.GraphHost.UpdateStatuses(GraphBinder.Nodes(_vm.Topology, RowsById()));
    }

    /// <summary>[D5] Id → satır VM haritası (GraphBinder statü/dep-badge'i buradan okur). Id'ler Windows yolu → OIC.</summary>
    private IReadOnlyDictionary<string, ProjectRowViewModel> RowsById()
    {
        var dict = new Dictionary<string, ProjectRowViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _vm.Projects) dict[row.Id] = row;
        return dict;
    }

    /// <summary>[D5] VM seçimini (Id) grafa (AD) iter. Echo koruması: itiş sırasında GraphView SelectionChanged
    /// yayınlar → <see cref="OnGraphSelectionChanged"/> bunu bayrakla yok sayar (aksi halde SelectProject toggle'ı
    /// seçimi geri alırdı).</summary>
    private void PushGraphSelection()
    {
        string? name = _vm.SelectedProjectId is { } id && _graphNameById.TryGetValue(id, out var n) ? n : null;
        _suppressGraphSelection = true;
        try { Shell.GraphHost.SelectedNode = name; }
        finally { _suppressGraphSelection = false; }
    }

    /// <summary>[D5] Graf seçimi (AD; boşluğa tıklama = null) → VM seçimi (Id). Kendi push'umuzun echo'su
    /// (<see cref="_suppressGraphSelection"/>) yok sayılır.</summary>
    private void OnGraphSelectionChanged(object? sender, string? name)
    {
        if (_suppressGraphSelection) return;
        string? id = name is { } nm && _graphIdByName.TryGetValue(nm, out var i) ? i : null;
        _vm.SelectProject(id);
    }

    /// <summary>[D5] VM sinyalleri → graf: statü tikleri (Counters), run başlangıç/bitiş (IsSettled + statü),
    /// seçim değişimi (view'e iter).</summary>
    private void OnVmPropertyChangedForGraph(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RunViewModel.Counters):
                PushGraphStatuses();
                break;
            case nameof(RunViewModel.IsRunning):
            case nameof(RunViewModel.IsStarting):
                Shell.GraphHost.IsSettled = !_vm.IsMidRunLocked; // run bitince true → kamera merkeze oturur
                PushGraphStatuses();
                break;
            case nameof(RunViewModel.SelectedProjectId):
                PushGraphSelection();
                break;
        }
    }

    private async Task StartEngineAsync()
    {
        try
        {
            var ready = await _engine.StartAsync();
            // [D1 review · C5] Sürüm bilgisi UI'da: konsolun boot satırı (design-v1 anlatı dili).
            _vm.OnEngineReady(ready.EngineVersion, ready.Pid);
        }
        catch (Services.EngineUnavailableException ex)
        {
            // [D1] Motor HİÇ doğamadı — iki neden: supervisor\ çıktısı yok (kurulum eksik) VEYA dosya var ama
            // başlatılamadı (bozuk exe/erişim reddi/TOCTOU [A2]). Child doğmadığı için EngineExited ATEŞLENMEZ →
            // eskiden kullanıcı HİÇBİR ŞEY görmüyordu (yalnız Debug.WriteLine, Release'te derlenip çıkar).
            // Şerit kalıcı hata moduna alınır; "Restart engine" gösterilmez (bkz. OnEngineUnavailable).
            _vm.OnEngineUnavailable(ex.ExePath, ex.Reason);
        }
        catch (Exception ex)
        {
            // Motor DOĞDU ama hazır olamadı (timeout/framing/erken ölüm): görsel bildirimi EngineExited yolu
            // TEK sinyal olarak zaten üretir (T37) — burada yalnız iz bırakılır, ikinci bir sinyal ÜRETİLMEZ.
            System.Diagnostics.Debug.WriteLine($"[engine] start failed — {ex.Message}");
        }
    }

    // ==================================== [T35] Yerleşim ====================================

    private void OnLayoutQuad(object sender, RoutedEventArgs e) => Shell.SetMode(LayoutMode.Quad);
    private void OnLayoutList(object sender, RoutedEventArgs e) => Shell.SetMode(LayoutMode.List);
    private void OnLayoutFocus(object sender, RoutedEventArgs e) => Shell.SetMode(LayoutMode.Focus);

    /// <summary>[D7/T66] Dişli → Settings modal diyaloğunu açar: canlı katman pattern'lerinin bir taslak
    /// kopyasını kurar + repo yolunu gösterir. Klasör seçici (<see cref="PickFolder"/>) enjekte edilir (E1'in
    /// IOsActions.PickFolder'ı gelene dek <c>OpenFolderDialog</c> doğrudan; testler bu seam'i by-pass eder).</summary>
    /// <para>[About] Bir modal zaten açıksa no-op — iki modal aynı anda duramaz.</para>
    private void OnSettings(object sender, RoutedEventArgs e)
    {
        if (AnyDialogOpen) return;
        SettingsOverlay.Open(_vm, _uiState, PickFolder);
    }

    /// <summary>[About] Info butonu → About modali.</summary>
    private void OnAbout(object sender, RoutedEventArgs e) => OnAboutRequested();

    /// <summary>[design-v1.2.1 §2.10] About'u AÇAR ya da KAPATIR — F1 bir toggle'dır.
    ///
    /// <para>Settings açıkken de açılır: About onun ÜSTÜNE biner (XAML'de sonra geldiği için z-sırası doğru)
    /// ve Esc önce About'u kapatır, yani kaydedilmemiş taslak yerinde kalır. Bu yüzden F1'i modal açıkken
    /// sağır etmeye gerek YOKTUR — eskiden öyleydi, aşırı tedbirliydi.</para>
    ///
    /// <para>Global kısayolun GERÇEKTEN kayıtlı olup olmadığı diyaloğa geçirilir: çakışmada kayıt sessizce
    /// düşer (<see cref="HotkeyRegistration"/>) ve kullanıcının bunu görebileceği tek yer About'tur. Hotkey
    /// yalnız <c>OnSourceInitialized</c>'da kurulur — pencere hiç gösterilmediyse (headless test) null'dır.</para></summary>
    private void OnAboutRequested()
    {
        if (AboutOverlay.Visibility == Visibility.Visible) { AboutOverlay.CloseDialog(); return; }
        AboutOverlay.Open(_vm, _hotkey?.IsRegistered ?? false, ResolveMsBuildAsync);
    }

    /// <summary>[About] MSBuild yolu + sürümü — About'un Environment sekmesi bunu LAZY çağırır (<c>vswhere</c>
    /// bir child process başlatır; About'u AÇMAK onu tetiklememeli). Çözülemezse resolver'ın kendi hata mesajı
    /// olduğu gibi gösterilir — uydurma bir metin YAZILMAZ.</summary>
    private static async Task<string> ResolveMsBuildAsync()
    {
        try
        {
            var location = await new MsBuildResolver(new ProcessRunner()).ResolveAsync();
            return $"{location.MsBuildExePath} (v{location.Version})";
        }
        catch (MsBuildResolveException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>[D7 · K10] Repo kökü seçici — <c>Microsoft.Win32.OpenFolderDialog</c> (E1'den önce doğrudan).
    /// İptal edilirse null.</summary>
    private string? PickFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select repository root" };
        if (_vm.RootPath.Length > 0) dialog.InitialDirectory = _vm.RootPath;
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    /// <summary>[E2/T10] Boş-durum daveti içindeki "Choose Folder": seçilen klasör HEMEN uygulanır —
    /// <see cref="PickFolder"/> → <see cref="RunViewModel.ChangeRepositoryAsync"/> (kök değişir, durumlar sıfırlanır,
    /// otomatik Sync). Settings'in "Change…" düğmesi bu yolu KULLANMAZ: orada seçim yalnız taslağa yazılır ve
    /// uygulanması Save'e ertelenir (<see cref="RunViewModel.ApplySettingsAsync"/>). Diyalog iptal edilirse no-op.</summary>
    private async void OnChooseFolder(object sender, RoutedEventArgs e)
    {
        if (PickFolder() is { } path) await _vm.ChangeRepositoryAsync(path);
    }

    /// <summary>[A13/T2 · 2.1] design-v1 §2.1 başlık bağlamını tazeler — karar SAF <see cref="TitleBarContext"/>'te,
    /// burada YALNIZ uygulanır. Worktree eki boşsa öğe <c>Collapsed</c> olur: boş metin bırakmak 8px'lik marjını
    /// yine de ödetirdi (logo/başlık hizası kayardı).</summary>
    private void RefreshTitleContext()
    {
        ContextText.Text = TitleBarContext.Compose(_vm.RootPath, _vm.Branch);
        // [T2 fix-1 · C1] ETKİN değer — zorunlu worktree'de başlık da worktree'yi göstermeli.
        string suffix = TitleBarContext.WorktreeSuffix(_vm.RootPath, _vm.EffectiveUseWorktree, _vm.EffectiveWorktreeName);
        ContextWorktreeText.Text = suffix;
        ContextWorktreeText.Visibility = suffix.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>[A13/T2 · 2.3] Başlıktaki filtre chip'ini tazeler. Etiketin TEK kaynağı
    /// <see cref="ProjectFilter.Label"/>'dır (action bar'ın chip tooltip'leriyle aynı tablo) — burada yeni bir
    /// eşleme uydurulmaz. Filtre yoksa chip gizlenir.</summary>
    private void RefreshFilterChip() =>
        Shell.SetFilterChip(_vm.ActiveFilter is { } f ? ProjectFilter.Label(f) : null);

    /// <summary>[E2/T10] Liste boş-durum davetinin görünürlüğünü tazeler — karar SAF <see cref="ListInvite.Resolve"/>'te.</summary>
    private void RefreshListInvite() =>
        Shell.SetListInvite(ListInvite.Resolve(_vm.HasWorkspace, _vm.Phase, _vm.Projects.Count, _vm.VisibleProjects.Count));

    /// <summary>Split sürükleme sonu ya da mod değişimi → kalıcı UiState'e yaz + aktif mod düğmesini eşle.</summary>
    private void OnShellLayoutChanged(object? sender, LayoutState state)
    {
        var s = _uiState.Load();
        s.LayoutMode = state.Mode; s.ColPct = state.ColPct; s.LeftPct = state.LeftPct; s.RightPct = state.RightPct;
        _uiState.Save(s);
        SyncModeButtons(state.Mode);
    }

    /// <summary>[D6 fold] İş akışı tercihi (RepositoryRoot/Configuration/Branch/UseWorktree/WorktreeName/PerfMode)
    /// değişince kalıcı duruma yazar — yerleşim persist'iyle AYNI desen (Load → muta → Save; düşük frekans).
    /// [D7 M3] RootPath değişimi (ilk klasör seçimi, Settings→Change, Choose Folder — hepsi RootPath'i set eder)
    /// TEK noktadan buradan persist edilir; açılışta seed edilip hatırlanır.</summary>
    private void OnWorkflowPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RunViewModel.RootPath):
            case nameof(RunViewModel.Configuration):
            case nameof(RunViewModel.Branch):
            case nameof(RunViewModel.UseWorktree):
            case nameof(RunViewModel.WorktreeName):
            case nameof(RunViewModel.PerfMode):
                var s = _uiState.Load();
                s.RepositoryRoot = _vm.RootPath;
                s.Configuration = _vm.Configuration;
                s.Branch = _vm.Branch;
                s.UseWorktree = _vm.UseWorktree;
                s.WorktreeName = _vm.WorktreeName;
                s.PerfMode = _vm.PerfMode;
                _uiState.Save(s);
                break;
        }
    }

    private void SyncModeButtons(LayoutMode mode)
    {
        LayQuadButton.IsChecked = mode == LayoutMode.Quad;
        LayListButton.IsChecked = mode == LayoutMode.List;
        LayFocusButton.IsChecked = mode == LayoutMode.Focus;
    }

    // ==================================== Pencere kabuğu ====================================

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint hwnd = new WindowInteropHelper(this).Handle;
        // [T49] Pencere kenarlığı rengi TOKEN'dan gelir (hardcode YASAK): Brush.Border → COLORREF 0x00BBGGRR.
        int on = 1, round = 2;
        int border = Dwm.ColorRefFrom(((SolidColorBrush)FindResource("Brush.Border")).Color);
        Dwm.DwmSetWindowAttribute(hwnd, Dwm.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        Dwm.DwmSetWindowAttribute(hwnd, Dwm.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        Dwm.DwmSetWindowAttribute(hwnd, Dwm.DWMWA_BORDER_COLOR, ref border, sizeof(int));

        // [T62] Tepsi: X artık kapatmaz (K5) → uygulama tepsiden yönetilir.
        _tray = new AppTrayIcon();
        _tray.RestoreRequested += ShowFromTray;
        _tray.StopRequested += () => { if (_vm.StopCommand.CanExecute(null)) _vm.StopCommand.Execute(null); };
        _tray.ExitRequested += ExitApplication;

        HwndSource.FromHwnd(hwnd)!.AddHook(HotkeyWndProc);

        // [v7Δ-5] Alt+B (ayarlanabilir) — çakışmada SESSİZ devre dışı.
        if (!HotkeyBinding.TryParse(_uiState.Load().Hotkey, out var binding))
            HotkeyBinding.TryParse(HotkeyBinding.DefaultGesture, out binding);
        _hotkey = HotkeyRegistration.Register(hwnd, GlobalHotkeyId, binding);
    }

    /// <summary>Global kısayol (Alt+B) → pencereyi tepsiden/arka plandan getir.</summary>
    private nint HotkeyWndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != Win32.WM_HOTKEY || (int)wParam != GlobalHotkeyId) return 0;
        handled = true;
        ShowFromTray();
        return 0;
    }

    private void ToggleMaximizeRestore()
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    /// <summary>[K5] `X` pencereyi KAPATMAZ — tepsiye küçültür; YALNIZ ilk seferde OS tray balloon'u.</summary>
    private void MinimizeToTray()
    {
        Hide();
        if (_closeBalloon.ClaimShow()) _tray?.ShowClosedToTrayNotification();
    }

    /// <summary>[E2/T16] Autostart ile açılış: pencere GÖSTERİLMEDEN tepside (gizli) başlar. HWND'i erkenden
    /// oluşturmak (<see cref="System.Windows.Interop.WindowInteropHelper.EnsureHandle"/>) <see cref="OnSourceInitialized"/>'ı
    /// tetikler → tepsi ikonu kurulur; pencere hiç <c>Show()</c> edilmediğinden görünmez. Kullanıcı tepsi ikonundan
    /// (ya da Alt+B) <see cref="ShowFromTray"/> ile getirir. Oto-Sync YOKtur (normal açılışta da yok — [D7 M3]
    /// RepositoryRoot açılışta SEED edilir/hatırlanır ama SEED-BUT-IDLE: doğrudan RootPath set'i yalnız Empty→Boot
    /// sürer, Sync tetiklemez; autostart yolu bugünkü "temiz" başlangıcı tepside korur).</summary>
    public void StartInTray() => new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();

    /// <summary>Tepsiden/kısayoldan/ikinci instance'tan pencereyi geri getirir.</summary>
    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>Tepsi → Exit: GERÇEK çıkış. Kaskat: App.Shutdown → App.OnExit → EngineHost.DisposeAsync →
    /// outer Job (KILL_ON_JOB_CLOSE) → Supervisor ve tüm <c>dotnet build</c> child'ları.</summary>
    private void ExitApplication()
    {
        _exiting = true;
        Application.Current.Shutdown();
    }

    /// <summary>[M-3 fix wave] Oturum kapanışı GERÇEK bir çıkıştır — tray/balloon YASAK. <c>e.Cancel</c>'a
    /// DOKUNULMAZ: yalnız aşağı akan <c>Closing</c>'in tray'e sapmasını önleriz.</summary>
    private void OnSessionEnding(object? sender, SessionEndingCancelEventArgs e) => _exiting = true;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // [K5] X / Alt+F4 / sistem menüsü Kapat → tepsiye küçült. Yalnız tepsi Exit'i (veya Application.Shutdown)
        // gerçekten kapatır.
        if (!_exiting)
        {
            e.Cancel = true;
            MinimizeToTray();
            return;
        }
        base.OnClosing(e);
    }

    /// <summary>Kabuk kaynakları BURADA bırakılır: pencere gerçekten kapandığında tam bir kez çalışır ve
    /// iptal edilen (tepsiye küçülen) kapatmalardan etkilenmez.</summary>
    protected override void OnClosed(EventArgs e)
    {
        if (Application.Current is { } app) app.SessionEnding -= OnSessionEnding; // [M-3 fix wave] (bkz. ctor: Application yoksa abonelik de yoktur)
        _hotkey?.Dispose();
        _tray?.Dispose();
        base.OnClosed(e);
    }

    private void OnMinimize(object s, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void OnMaximizeRestore(object s, RoutedEventArgs e) => ToggleMaximizeRestore();
    private void OnClose(object s, RoutedEventArgs e) => Close(); // OnClosing X'i tepsiye çevirir [K5]
}
