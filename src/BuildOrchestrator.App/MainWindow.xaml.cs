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
        // (BackButton.Visibility'e bağlı [ObservableProperty]) mutasyona uğratır — bu yüzden DİĞER TÜM
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
        };
        _elapsedTimer.Start();

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(RunViewModel.ActiveProjectId)) return;
            BackButton.Visibility = _vm.ActiveProjectId is null ? Visibility.Collapsed : Visibility.Visible;
        };

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
        ConsoleViewControl.Document = new TextDocument(_vm.SeedProjectDocument(row.Id));
    }

    /// <summary>[Fix wave 1, Finding 3] bkz. <see cref="OnProjectSelected"/> — aynı gerekçeyle <c>SeedRunDocument</c>.</summary>
    private void OnBack(object sender, RoutedEventArgs e)
    {
        _vm.ShowRun();
        ConsoleViewControl.Document = new TextDocument(_vm.SeedRunDocument());
    }

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
