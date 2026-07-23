using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;

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
    private readonly IUiStateStore _uiState = new JsonUiStateStore(JsonUiStateStore.DefaultPath);
    private readonly FirstCloseBalloonGate _closeBalloon;
    private AppTrayIcon? _tray;
    private HotkeyRegistration? _hotkey;
    private bool _exiting; // tepsi Exit'i (gerçek çıkış) ile X'i (tepsiye küçült) ayıran TEK bayrak

    public MainWindow(EngineHost engine, RunViewModel vm, ConsoleBatcher console)
    {
        InitializeComponent();
        _engine = engine;
        _vm = vm;
        _console = console;
        DataContext = _vm;
        _closeBalloon = new FirstCloseBalloonGate(_uiState);

        // [T35 fold #1] Title-bar yüksekliğinin TEK kaynağı Size.TitleBarHeight token'ıdır: WindowChrome
        // (CaptionHeight = sürüklenebilir başlık bandı) VE title-bar satırı ONDAN türetilir. WindowChrome bir
        // Freezable olduğundan DynamicResource onda güvenilir çözülmez → kod-tarafı kurulur (kesin çalışır).
        // WindowChrome burada (OnSourceInitialized'DAN ÖNCE) atanır: Snap Layouts hook sıralaması bozulmaz (M-9).
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

        // [T35] Kalıcı yerleşimi geri yükle; kullanıcı değişiklikleri (mod düğmesi / split sürükleme sonu) persist.
        // GraphView'ın MotionSettings'i Loaded'dan ÖNCE atanmalı (GraphView.xaml.cs:111-119 sözleşmesi).
        Shell.GraphHost.MotionSettings = App.Motion;
        var saved = _uiState.Load();
        var layout = new LayoutState(saved.LayoutMode, saved.ColPct, saved.LeftPct, saved.RightPct);
        Shell.ApplyLayout(layout);
        SyncModeButtons(layout.Mode);
        Shell.LayoutChanged += OnShellLayoutChanged;

        // [D1] Proje listesini katman gruplarıyla besle. SetGroups YALNIZ topoloji/gruplama değişiminde (tam
        // reset orada meşru — StickyLayerList); statü tikleri satır VM'lerinin INotifyPropertyChanged'inden akar.
        _vm.TopologyChanged += (_, _) => RefreshProjectGroups();
        RefreshProjectGroups();

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
        Shell.ConsoleHeaderControl.BackRequested += (_, _) => OnBack();

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
        Application.Current.SessionEnding += OnSessionEnding;

        _ = RunConsolePumpAsync();
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
    private void RefreshProjectGroups()
    {
        var groups = _vm.BuildLayerGroups()
            .Select(g => new StickyLayerList.LayerGroup(g.Name ?? "", g.Rows.Cast<object>().ToList()))
            .ToList();
        Shell.ProjectsList.SetGroups(groups);
    }

    private async Task StartEngineAsync()
    {
        try
        {
            await _engine.StartAsync();
        }
        catch (Exception ex)
        {
            // Motor başlatılamadı. Görsel bildirim (sticky ribbon + Restart) T37'nin işidir — burada gözlenir.
            System.Diagnostics.Debug.WriteLine($"[engine] start failed — {ex.Message}");
        }
    }

    // ==================================== [T35] Yerleşim ====================================

    private void OnLayoutQuad(object sender, RoutedEventArgs e) => Shell.SetMode(LayoutMode.Quad);
    private void OnLayoutList(object sender, RoutedEventArgs e) => Shell.SetMode(LayoutMode.List);
    private void OnLayoutFocus(object sender, RoutedEventArgs e) => Shell.SetMode(LayoutMode.Focus);

    // Gear: layer-definitions Settings diyaloğu bir sonraki task'ın kapsamıdır (C1'de inert).
    private void OnSettings(object sender, RoutedEventArgs e) { /* Settings dialog — sonraki task */ }

    /// <summary>Split sürükleme sonu ya da mod değişimi → kalıcı UiState'e yaz + aktif mod düğmesini eşle.</summary>
    private void OnShellLayoutChanged(object? sender, LayoutState state)
    {
        var s = _uiState.Load();
        s.LayoutMode = state.Mode; s.ColPct = state.ColPct; s.LeftPct = state.LeftPct; s.RightPct = state.RightPct;
        _uiState.Save(s);
        SyncModeButtons(state.Mode);
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

        AttachSnapLayoutHook(hwnd);
        HwndSource.FromHwnd(hwnd)!.AddHook(HotkeyWndProc);

        // [v7Δ-5] Alt+B (ayarlanabilir) — çakışmada SESSİZ devre dışı.
        if (!HotkeyBinding.TryParse(_uiState.Load().Hotkey, out var binding))
            HotkeyBinding.TryParse(HotkeyBinding.DefaultGesture, out binding);
        _hotkey = HotkeyRegistration.Register(hwnd, GlobalHotkeyId, binding);
    }

    /// <summary>
    /// [T62] Snap Layouts hook'unu bağlar. <c>base.OnSourceInitialized</c>'dan SONRA çağrılması ZORUNLUDUR:
    /// <c>HwndSource.AddHook</c> hook'u LİSTENİN BAŞINA ekler ve mesaj pompası son eklenen hook'u ÖNCE çağırır —
    /// bu yüzden burada eklendiğinde bizim hook <c>WindowChrome</c>'un <c>WM_NCHITTEST</c> yanıtından önce çalışır.
    ///
    /// <para><b>[M-9]:</b> <c>WindowChromeWorker</c> kendi hook'unu <c>WindowChrome</c> ATANDIĞINDA ekler.
    /// WindowChrome BU noktadan (OnSourceInitialized) SONRA yeniden atanırsa, worker'ın hook'u BİZİMKİNDEN SONRA
    /// eklenir → ÖNCE çalışır → Snap Layouts SESSİZCE ÖLÜR. WindowChrome ctor'da (buradan ÖNCE) atanır — sıralama
    /// korunur; ileride yeniden atanırsa bu metot o atamadan SONRA tekrar çağrılmalıdır.</para>
    /// </summary>
    private void AttachSnapLayoutHook(nint hwnd)
    {
        var snapLayout = new SnapLayoutHook(MaxButton, SetMaxButtonHover, ToggleMaximizeRestore);
        HwndSource.FromHwnd(hwnd)!.AddHook(snapLayout.WndProc);
    }

    /// <summary>Global kısayol (Alt+B) → pencereyi tepsiden/arka plandan getir.</summary>
    private nint HotkeyWndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != Win32.WM_HOTKEY || (int)wParam != GlobalHotkeyId) return 0;
        handled = true;
        ShowFromTray();
        return 0;
    }

    /// <summary>Snap Layouts bölgesinin hover'ı: o alan non-client olduğundan WPF'in IsMouseOver'ı çalışmaz,
    /// görsel elle sürülür. Kapanışta yerel değer TEMİZLENİR → şablonun kendi (Transparent) değeri geri gelir.</summary>
    private void SetMaxButtonHover(bool on)
    {
        if (on && TryFindResource("Brush.SurfaceRaised") is Brush hover) MaxButton.Background = hover;
        else MaxButton.ClearValue(BackgroundProperty);
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
        Application.Current.SessionEnding -= OnSessionEnding; // [M-3 fix wave]
        _hotkey?.Dispose();
        _tray?.Dispose();
        base.OnClosed(e);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        uint dpi = (uint)(VisualTreeHelper.GetDpi(this).PixelsPerInchX);
        RootShell.Padding = MaximizeFix.PaddingFor(WindowState,
            Dwm.GetSystemMetricsForDpi(Dwm.SM_CXSIZEFRAME, dpi),
            Dwm.GetSystemMetricsForDpi(Dwm.SM_CYSIZEFRAME, dpi),
            Dwm.GetSystemMetricsForDpi(Dwm.SM_CXPADDEDBORDER, dpi),
            VisualTreeHelper.GetDpi(this).DpiScaleX);
    }

    private void OnMinimize(object s, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    // Buton tıklaması ile Snap Layouts'un WM_NCLBUTTONUP yolu AYNI davranışa gider (kopya YASAK).
    private void OnMaximizeRestore(object s, RoutedEventArgs e) => ToggleMaximizeRestore();
    private void OnClose(object s, RoutedEventArgs e) => Close(); // OnClosing X'i tepsiye çevirir [K5]
}
