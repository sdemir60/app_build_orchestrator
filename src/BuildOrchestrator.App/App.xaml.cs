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
    public static IMotionSettings Motion { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // [It-4a Foundation / Global Constraints — reduced-motion] Tüm yollarda (font-ab/it4a-lab/normal) TEK
        // instance: Resources (Application.Resources, App.xaml'de merge edilen Motion.xaml) içindeki Duration.*
        // kaynaklarını OS "animasyonları göster" sinyaline göre canlı 0'a çevirir / geri yükler.
        var motion = new MotionSettings(new SystemParametersMotionSignal());
        motion.Attach(Resources);
        Motion = motion;

        if (e.Args.Contains("--font-ab"))
        {
            // [T65/K9] Font A/B karar penceresi — DI/EngineHost kurulmaz, Supervisor spawn edilmez.
            new Spikes.FontAbWindow().Show();
            return;
        }
        if (e.Args.Contains("--it4a-lab"))
        {
            // [It-4a Foundation] Dev-only lab kabuğu — DI/EngineHost kurulmaz, Supervisor spawn edilmez.
            new Spikes.It4aLabWindow().Show();
            return;
        }
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
        // --font-ab yolunda DI hiç kurulmaz — Services null kalır.
        (Services?.GetService<EngineHost>() as IAsyncDisposable)?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
