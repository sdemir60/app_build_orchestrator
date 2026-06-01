using System.IO;
using System.Windows;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
namespace BuildOrchestrator.App;

/// <summary>
/// Application entry point. Enforces single-instance (Section 2): a second launch foregrounds the
/// existing window instead of opening a new one.
/// </summary>
public partial class App : Application
{
    private SingleInstanceManager? _singleInstance;
    private MainViewModel? _mainViewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Capture otherwise-silent crashes (background threads, dispatcher) to a log file.
        var logPath = Path.Combine(Path.GetTempPath(), "bo-crash.log");
        DispatcherUnhandledException += (_, args) =>
        {
            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] Dispatcher: {args.Exception}\n\n"); } catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] AppDomain: {args.ExceptionObject}\n\n"); } catch { }
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] Task: {args.Exception}\n\n"); } catch { }
        };

        _singleInstance = new SingleInstanceManager();
        if (!_singleInstance.TryAcquire())
        {
            // Another instance owns the lock: ask it to come forward, then exit.
            _singleInstance.SignalExistingInstance();
            _singleInstance.Dispose();
            Shutdown();
            return;
        }

        _mainViewModel = new MainViewModel();
        var window = new MainWindow { DataContext = _mainViewModel };
        MainWindow = window;

        _singleInstance.ActivationRequested += () =>
            Dispatcher.BeginInvoke(() => BringToFront(window));

        window.Show();
        _mainViewModel.Initialize();
    }

    private static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainViewModel?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}

