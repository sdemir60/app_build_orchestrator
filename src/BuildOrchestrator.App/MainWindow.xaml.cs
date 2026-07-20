using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using ICSharpCode.AvalonEdit.Document;

namespace BuildOrchestrator.App;

public partial class MainWindow : Window
{
    private readonly EngineHost _engine;
    private readonly RunViewModel _vm;
    private readonly ConsoleBatcher _console;
    private readonly CancellationTokenSource _consoleCts = new();
    private readonly DispatcherTimer _elapsedTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public MainWindow(EngineHost engine, RunViewModel vm, ConsoleBatcher console)
    {
        InitializeComponent();
        _engine = engine;
        _vm = vm;
        _console = console;
        DataContext = _vm;

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

    /// <summary>Karta tıkla → tam log [T28]: chunk geçmişi + tamponlanmış canlı satırların dikişi VM'de
    /// yapılır; burada yalnız sonucu konsola (yeni bir doküman olarak) taşırız. [Fix wave 1, Finding 3]
    /// <c>SeedProjectDocument</c> (Get* DEĞİL) kullanılır: VM'in _gate kilidi altında hem metni okur hem de
    /// ConsoleBatcher'daki bekleyen satırları atar — aksi halde pump'ın bir sonraki tick'i aynı satırları
    /// taze dokümana TEKRAR ekler (kopya).</summary>
    private async void OnProjectSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ProjectsList.SelectedItem is not ProjectRowViewModel row) return;
        await _vm.LoadProjectLogAsync(row.Id);
        string seeded = _vm.SeedProjectDocument(row.Id);
        // [T56/3a] Log boşsa design-v1 §2.5 boş-durum metni (kaskat animasyonu Task 3b) — düz render.
        if (seeded.Length == 0) seeded = EmptyStateFor(row) + "\n";
        ConsoleViewControl.Document = new TextDocument(seeded);
        // [T56/3a] Proje-log modu başlığı: ← Back + proje adı + statü glyph/adı + (varsa) ▲ dependency issue.
        ConsoleHeaderControl.ShowProjectLog(row.Name, row.State, row.HasDepIssue, _vm.GetActiveLineCount());
    }

    /// <summary>[Fix wave 1, Finding 3] bkz. <see cref="OnProjectSelected"/> — aynı gerekçeyle <c>SeedRunDocument</c>.
    /// [T56/3a] ConsoleHeader.BackRequested'tan çağrılır; başlık anlatı moduna ActiveProjectId=null PropertyChanged'ı
    /// üzerinden döner (bkz. constructor).</summary>
    private void OnBack()
    {
        _vm.ShowRun();
        ConsoleViewControl.Document = new TextDocument(_vm.SeedRunDocument());
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
    private void OnMaximizeRestore(object s, RoutedEventArgs e)
    { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void OnClose(object s, RoutedEventArgs e) => Close(); // X→tray davranışı It-4 (T62 devamı + K5)
}
