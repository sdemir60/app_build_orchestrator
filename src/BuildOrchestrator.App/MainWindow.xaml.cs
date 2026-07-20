using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        CaptionGlyphs.BindMaxButton(this, MaxButton); // [K8] maximize'da restore glyph'i

        _engine.EngineExited += code => Dispatcher.Invoke(() =>
        {
            EngineStatusText.Text = $"engine: died (exit {code})"; // It-4'te sticky şerit kalıcı hata moduna taşınır (T37)
            RestartEngineButton.Visibility = Visibility.Visible;
            // [Task 16 — It-2 devir §8] VM'in run-state'i (IsStarting/IsRunning/CanContinue) eskiden bu
            // sinyale hiç BAĞLI değildi — Restart bile Rebuild/Stop/Continue'yu açmıyordu. Aynı Dispatcher.Invoke
            // marshal'ı altında (ObservableProperty/CanExecuteChanged'a dokunduğundan UI thread gerekir).
            _vm.OnEngineExited(code);
        });
        // [A13.2/Kısıt 4] YALNIZ projectLog YÜKSEK frekanslı akan log satırıdır (MSBuild çıktısının HER satırı) —
        // VM'in o dalı yalnız ConsoleBatcher.Post (kilitsiz) + iç kilitli arabellek kullanır, ObservableProperty'e
        // DOKUNMAZ; marshal OLMADAN doğrudan çağrılabilir (satır başına Dispatcher YASAK). projectLogChunk ise
        // proje başına yalnız birkaç adettir (LogChunker parça sayısı) VE son parçada ActiveProjectId'yi
        // (konsol modunu/ConsoleHeader'ı süren [ObservableProperty]) mutasyona uğratır — bu yüzden DİĞER TÜM
        // event'ler gibi UI thread'ine taşınır.
        _engine.EventReceived += ev =>
        {
            if (ev is ProjectLogEvent) _vm.OnEvent(ev);
            else Dispatcher.InvokeAsync(() => _vm.OnEvent(ev));
        };

        _elapsedTimer.Tick += (_, _) =>
        {
            _vm.TickElapsed();
            ElapsedText.Text = TimeSpan.FromMilliseconds(_vm.ElapsedMs).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
            // [T56/3a] "N lines" TAM tampon sayacı — 200ms'de bir aktif tampondan tazelenir (marshal-free log
            // yolundan ObservableProperty tetiklemek yerine; render dilimi DEĞİL, Ek A #23).
            ConsoleHeaderControl.SetLineCount(_vm.GetActiveLineCount());
        };
        _elapsedTimer.Start();

        // [T56/3a] Konsol modu ActiveProjectId'yi izler: null → anlatı başlığı (proje seçimi OnProjectSelected'te
        // zengin proje-log başlığını kurar; buradaki null dalı Rebuild/Continue'nun temizlemesini de kapsar).
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(RunViewModel.ActiveProjectId)) return;
            if (_vm.ActiveProjectId is null) ConsoleHeaderControl.ShowNarrative(_vm.GetActiveLineCount());
        };
        ConsoleHeaderControl.BackRequested += (_, _) => OnBack();

        Loaded += async (_, _) => await StartEngineAsync();
        Closed += (_, _) => { _consoleCts.Cancel(); _console.Complete(); _elapsedTimer.Stop(); };

        _ = RunConsolePumpAsync();
    }

    /// <summary>
    /// [Kısıt 1] <c>ConsoleBatcher.PumpAsync</c> tick'i (üretimde <c>Task.Delay(50, ct)</c>) iptal edilince
    /// <see cref="OperationCanceledException"/> YAKALANMADAN yükselir — burada TEK yerde gözlenir, aksi halde
    /// pencere kapanırken gözlemlenmemiş (unobserved) bir exception kalırdı. Flush BATCH BAŞINA TEK
    /// <c>Dispatcher.InvokeAsync</c> ile <see cref="ConsoleView.AppendBatch"/>'e taşınır — satır başına
    /// Dispatcher çağrısı YASAK [A13.2].
    /// </summary>
    private async Task RunConsolePumpAsync()
    {
        try
        {
            await _console.PumpAsync(text => Dispatcher.InvokeAsync(() => ConsoleViewControl.AppendBatch(text)), _consoleCts.Token);
        }
        catch (OperationCanceledException) { /* pencere kapanıyor — beklenen */ }
        catch (Exception ex)
        {
            // [Minor/Fix wave 1] fire-and-forget (`_ = RunConsolePumpAsync()`) task'i gözlenmemiş bir
            // exception'la sessizce ölmesin — burada tek gözlem noktası; UI thread affinity garantisi
            // olmadığından (PumpAsync ConfigureAwait(false) kullanır) doğrudan bir WPF kontrolüne DOKUNULMAZ.
            System.Diagnostics.Debug.WriteLine($"[console pump] gözlenmeyen hata: {ex}");
        }
    }

    /// <summary>Karta tıkla → tam log [T28]: chunk geçmişi + tamponlanmış canlı satırların dikişi VM'de yapılır.
    /// [3b] Reseed pump'ın TEK okuyucusundan geçer (<c>SeedProjectDocument(id, apply)</c>): VM _gate altında
    /// snapshot okur + kanala reseed sentinel'i yazar; pump sentinel'e uğrayınca <paramref name="apply"/>'ı çağırır
    /// ve konsolu kaskatla kurar — yarım-dequeue kopya satırı residual'ı kapanır (It-4 backlog). Başlık + copy-log
    /// provider HEMEN (UI thread) kurulur; konsol içeriği reseed ile ~tick sonra kaskatla belirir.</summary>
    private async void OnProjectSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ProjectsList.SelectedItem is not ProjectRowViewModel row) return;
        await _vm.LoadProjectLogAsync(row.Id);

        // [T56/3a] Proje-log modu başlığı: ← Back + proje adı + statü glyph/adı + (varsa) ▲ dependency issue.
        ConsoleHeaderControl.ShowProjectLog(row.Name, row.State, row.HasDepIssue, _vm.GetActiveLineCount());
        // [3b/Ek A #3] Copy log = TAM proje logu (render dilimi DEĞİL) — VM'in tam tamponundan.
        ConsoleHeaderControl.LogTextProvider = () => _vm.GetProjectDocumentText(row.Id);

        bool building = row.State == ProjectRowState.Started;
        _vm.SeedProjectDocument(row.Id, seeded => Dispatcher.InvokeAsync(() =>
        {
            // [T56/3b] Log boşsa design-v1 §2.5 boş-durum metni. Kaskat: 26ms'de 3 satır + 140ms/satır fade.
            var lines = SplitLines(seeded.Length == 0 ? EmptyStateFor(row) + "\n" : seeded);
            ConsoleViewControl.PlayCascade(lines, buildInProgress: building);
        }));
    }

    /// <summary>[3b] bkz. <see cref="OnProjectSelected"/> — aynı gerekçeyle <c>SeedRunDocument(apply)</c> (reseed
    /// pump'tan geçer). ConsoleHeader.BackRequested'tan çağrılır; başlık anlatı moduna ActiveProjectId=null
    /// PropertyChanged'ı üzerinden döner (bkz. constructor).</summary>
    private void OnBack()
    {
        _vm.ShowRun();
        _vm.SeedRunDocument(text => Dispatcher.InvokeAsync(() => ConsoleViewControl.ShowRunDocument(text)));
    }

    // seeded metni ('\n' sonekli satırlar) satır listesine böler — sondaki boş satırı (final '\n' artefaktı) atar.
    private static IReadOnlyList<string> SplitLines(string text)
    {
        var parts = text.Split('\n');
        int count = parts.Length > 0 && parts[^1].Length == 0 ? parts.Length - 1 : parts.Length;
        var lines = new string[count];
        Array.Copy(parts, lines, count);
        return lines;
    }

    // [T56/3a] Boş proje logu için design-v1 §2.5 metinleri. sha/deps gerçek kaynağı 3a'da YOK — design örnek
    // değerleri (placeholder) kullanılır; gerçek "son başarılı build"/bekleyen-bağımlılık verisi ileride bağlanır.
    private static string EmptyStateFor(ProjectRowViewModel row) => row.State switch
    {
        ProjectRowState.Skipped => Console.ConsoleEmptyState.Skipped("a3f81c2"),
        ProjectRowState.Pending => Console.ConsoleEmptyState.Queued(row.DepIssues ?? ["Sales.Core", "Security"]),
        _ => Console.ConsoleEmptyState.NoLog,
    };

    private async Task StartEngineAsync()
    {
        try
        {
            RestartEngineButton.Visibility = Visibility.Collapsed;
            var ready = await _engine.StartAsync();
            EngineStatusText.Text = $"engine: ready (pid {ready.Pid}, v{ready.EngineVersion})";
        }
        catch (Exception ex)
        {
            EngineStatusText.Text = $"engine: start failed — {ex.Message}";
            RestartEngineButton.Visibility = Visibility.Visible;
        }
    }

    private async void OnRestartEngine(object sender, RoutedEventArgs e)
    {
        try
        {
            RestartEngineButton.Visibility = Visibility.Collapsed;
            var ready = await _engine.RestartAsync();
            EngineStatusText.Text = $"engine: ready (pid {ready.Pid}, v{ready.EngineVersion})";
        }
        catch (Exception ex)
        {
            EngineStatusText.Text = $"engine: restart failed — {ex.Message}";
            RestartEngineButton.Visibility = Visibility.Visible;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint hwnd = new WindowInteropHelper(this).Handle;
        int on = 1, round = 2, border = 0x002A2A2A; // COLORREF 0x00BBGGRR
        Dwm.DwmSetWindowAttribute(hwnd, Dwm.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        Dwm.DwmSetWindowAttribute(hwnd, Dwm.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        Dwm.DwmSetWindowAttribute(hwnd, Dwm.DWMWA_BORDER_COLOR, ref border, sizeof(int));

        // [T62] Tepsi: X artık kapatmaz (K5) → uygulama tepsiden yönetilir.
        _tray = new AppTrayIcon();
        _tray.RestoreRequested += ShowFromTray;
        _tray.StopRequested += () => { if (_vm.StopCommand.CanExecute(null)) _vm.StopCommand.Execute(null); };
        _tray.ExitRequested += ExitApplication;

        // [T62] Snap Layouts: hook, base'den SONRA eklenir — HwndSource son eklenen hook'u ÖNCE çağırır, yani
        // WindowChrome'un kendi WM_NCHITTEST yanıtından (IsHitTestVisibleInChrome → HTCLIENT) önce davranırız.
        // Hook nesnesini alanda tutmaya gerek yok: HwndSource'un tuttuğu delegate zaten onu canlı tutar.
        var snapLayout = new SnapLayoutHook(MaxButton, SetMaxButtonHover, ToggleMaximizeRestore);
        var source = HwndSource.FromHwnd(hwnd)!;
        source.AddHook(snapLayout.WndProc);
        source.AddHook(HotkeyWndProc);

        // [v7Δ-5] Alt+B (ayarlanabilir) — çakışmada SESSİZ devre dışı.
        if (!HotkeyBinding.TryParse(_uiState.Load().Hotkey, out var binding))
            HotkeyBinding.TryParse(HotkeyBinding.DefaultGesture, out binding);
        _hotkey = HotkeyRegistration.Register(hwnd, GlobalHotkeyId, binding);
    }

    /// <summary>Global kısayol (Alt+B) → pencereyi tepsiden/arka plandan getir. WM_HOTKEY alan process'e Windows
    /// foreground hakkı verdiğinden burada <see cref="Window.Activate"/> yeterlidir (single-instance yolundaki
    /// <c>AllowSetForegroundWindow</c> devrine gerek yok — bkz. <see cref="SingleInstanceProtocol"/>).</summary>
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

    /// <summary>[K5] `X` pencereyi KAPATMAZ — tepsiye küçültür; YALNIZ ilk seferde OS tray balloon'u
    /// (uygulama içi toast design §8'de yasak).</summary>
    private void MinimizeToTray()
    {
        Hide();
        if (_closeBalloon.ClaimShow()) _tray?.ShowClosedToTrayNotification();
    }

    /// <summary>Tepsiden/kısayoldan/ikinci instance'tan pencereyi geri getirir. Getiriliş animasyonu
    /// reduced-motion'a TABİDİR: sinyal TAZE okunur, kapalıysa anında görünür (Foundation sözleşmesi).</summary>
    public void ShowFromTray()
    {
        bool wasHidden = !IsVisible || WindowState == WindowState.Minimized;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (!wasHidden) return; // zaten görünürken (ikinci instance öne getirme) yeniden fade YOK

        BeginAnimation(OpacityProperty, null); // uçuştaki önceki getiriliş animasyonunu bırak
        if (!(App.Motion?.AnimationsEnabled ?? false)) { Opacity = 1; return; } // TAZE okuma [Foundation]
        Opacity = 0;
        var duration = MotionTokens.ResolveDuration(this, "Duration.Fast", 120);
        var spline = MotionTokens.ResolveKeySpline(this, "KeySpline.EaseOut", new KeySpline(0.22, 1, 0.36, 1));
        BeginAnimation(OpacityProperty, MotionTokens.SplineTo(1.0, duration.TimeSpan, spline));
    }

    /// <summary>Tepsi → Exit: GERÇEK çıkış. Kaskat: App.Shutdown → App.OnExit → EngineHost.DisposeAsync →
    /// outer Job (KILL_ON_JOB_CLOSE) → Supervisor ve tüm <c>dotnet build</c> child'ları.</summary>
    private void ExitApplication()
    {
        _exiting = true;
        Application.Current.Shutdown();
    }

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

    /// <summary>Kabuk kaynakları BURADA bırakılır, OnClosing'de DEĞİL: pencere gerçekten kapandığında tam olarak
    /// bir kez çalışır ve iptal edilen (tepsiye küçülen) kapatmalardan etkilenmez; oturum kapanışı gibi
    /// "iptal yok sayılan" kapanışları da kapsar.</summary>
    protected override void OnClosed(EventArgs e)
    {
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
