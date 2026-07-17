using System.IO;
using System.Windows;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BuildOrchestrator.App;

public partial class App : Application
{
    public static ServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var sc = new ServiceCollection();
        sc.AddSingleton(_ => new EngineHost(
            Path.Combine(AppContext.BaseDirectory, "supervisor", "BuildOrchestrator.Supervisor.exe")));
        // Üretimde ~50ms tick — Task 11'in kanıtladığı batching davranışı; test'te enjekte edilen tick kullanılır.
        sc.AddSingleton(_ => new ConsoleBatcher(ct => Task.Delay(50, ct)));
        sc.AddSingleton(sp => new RunViewModel(
            sp.GetRequiredService<EngineHost>(), sp.GetRequiredService<ConsoleBatcher>(), () => Guid.NewGuid().ToString()));
        sc.AddSingleton<MainWindow>();
        Services = sc.BuildServiceProvider();
        Services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (Services.GetService<EngineHost>() as IAsyncDisposable)?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
