using System.ComponentModel;
using System.Windows;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Core.Configuration;
using BuildOrchestrator.Core.Storage;
using Application = System.Windows.Application;

namespace BuildOrchestrator.App;

/// <summary>
/// Application entry point and composition root (Section 2). Enforces a single instance, wires the
/// Worker client, builds the main view model, and manages the tray icon. The window hides to the tray
/// on close so background Stop remains available (Section 6.1 rule 6).
/// </summary>
public partial class App : Application
{
    private SingleInstanceGuard? _instanceGuard;
    private WorkerClient? _worker;
    private TrayIconService? _tray;
    private MainViewModel? _mainViewModel;
    private MainWindow? _mainWindow;

    private AppPaths _paths = null!;
    private ConfigStore _configStore = null!;
    private GraphCacheStore _graphCacheStore = null!;
    private AppConfig _config = null!;

    private bool _startMinimized;
    private bool _shuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _startMinimized = e.Args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));

        // Single-instance: a second launch activates the first and exits (Section 2).
        _instanceGuard = new SingleInstanceGuard();
        if (!_instanceGuard.IsPrimaryInstance)
        {
            SingleInstanceGuard.SignalPrimary();
            Shutdown();
            return;
        }
        _instanceGuard.ActivationRequested += () => Dispatcher.Invoke(ShowMainWindow);
        _instanceGuard.StartListener();

        // Storage + config.
        _paths = new AppPaths();
        _paths.EnsureRoot();
        var jsonStore = new JsonStore();
        _configStore = new ConfigStore(_paths, jsonStore);
        _graphCacheStore = new GraphCacheStore(_paths, jsonStore);
        _config = _configStore.Load();

        // Worker bridge.
        _worker = new WorkerClient();

        // Main view model (events marshalled onto the UI dispatcher).
        _mainViewModel = new MainViewModel(_worker, _configStore, _config,
            action => Dispatcher.Invoke(action));

        // Tray icon (Section 2/6.1).
        _tray = new TrayIconService();
        _tray.ShowRequested += () => Dispatcher.Invoke(ShowMainWindow);
        _tray.StopRequested += () => Dispatcher.Invoke(() => _mainViewModel!.StopCommand.Execute(null));
        _tray.ExitRequested += () => Dispatcher.Invoke(ExitApplication);

        // Start the Worker process.
        try
        {
            _worker.Start();
        }
        catch
        {
            _mainViewModel.StatusTextSafe("Worker failed to start.");
        }

        // Seed the UI from the cached graph so cards appear before a Sync (Section 5).
        LoadCachedGraph();

        _mainWindow = new MainWindow { DataContext = _mainViewModel };
        _mainWindow.Closing += OnMainWindowClosing;

        if (!_startMinimized)
        {
            _mainWindow.Show();
        }
    }

    /// <summary>Builds a fresh config view model bound to the current config (used by the settings dialog).</summary>
    public ConfigViewModel CreateConfigViewModel() => new(_configStore, _config);

    private void LoadCachedGraph()
    {
        try
        {
            var cache = _graphCacheStore.Load();
            if (cache is { Projects.Count: > 0 })
            {
                _mainViewModel!.LoadProjects(cache.Projects, cache.HasCycles);
            }
        }
        catch
        {
            // ignore corrupt cache; a Sync rebuilds it
        }
    }

    private void ShowMainWindow()
    {
        _mainWindow ??= new MainWindow { DataContext = _mainViewModel };
        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }
        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Focus();
    }

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_shuttingDown)
        {
            return;
        }
        // Hide to tray instead of exiting so background Stop stays available (Section 6.1).
        e.Cancel = true;
        _mainWindow!.Hide();
        _tray?.ShowBalloon("Build Orchestrator", "Still running in the tray. Right-click for Stop / Exit.");
    }

    private void ExitApplication()
    {
        _shuttingDown = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shuttingDown = true;
        try { _worker?.Dispose(); } catch { /* best effort */ }
        try { _tray?.Dispose(); } catch { /* best effort */ }
        try { _instanceGuard?.Dispose(); } catch { /* best effort */ }
        base.OnExit(e);
    }
}
