using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.App;

public partial class MainWindow : Window
{
    private readonly EngineHost _engine;

    public MainWindow(EngineHost engine)
    {
        InitializeComponent();
        _engine = engine;
        _engine.EngineExited += code => Dispatcher.Invoke(() =>
        {
            EngineStatusText.Text = $"engine: died (exit {code})"; // It-4'te sticky şerit kalıcı hata moduna taşınır (T37)
            RestartEngineButton.Visibility = Visibility.Visible;
        });
        Loaded += async (_, _) => await StartEngineAsync();
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
