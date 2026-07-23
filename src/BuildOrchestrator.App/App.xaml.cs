using System.IO;
using System.Windows;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Core.Processes;
using Microsoft.Extensions.DependencyInjection;

namespace BuildOrchestrator.App;

public partial class App : Application
{
    public static ServiceProvider Services { get; private set; } = null!;
    public static IMotionSettings Motion { get; private set; } = null!;

    private SingleInstanceGuard? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // [It-4a Foundation / Global Constraints — reduced-motion] Tüm yollarda (font-ab/normal) TEK
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
        // [T62 / feasibility §4.3] Single-instance. Dev kabuğu (--font-ab) BİLİNÇLİ olarak bu kapının DIŞINDADIR:
        // çalışan bir ana pencere varken bile ayrı açılabilmelidir. (--it4a-lab lab kabuğu T35'te kaldırıldı —
        // primitifleri gerçek pencereye (ShellRoot) taşındı.)
        _singleInstance = SingleInstanceGuard.Acquire(SingleInstanceGuard.DefaultKey);
        if (!_singleInstance.IsFirst)
        {
            // İkinci instance: çalışan pencereyi öne getir ve sessizce kapan (AllowSetForegroundWindow sinyalden
            // ÖNCE — sırayı SingleInstanceProtocol garanti eder).
            _singleInstance.ActivateExistingInstance(TimeSpan.FromSeconds(3));
            Shutdown();
            return;
        }

        var sc = new ServiceCollection();
        sc.AddSingleton(_ => new EngineHost(
            Path.Combine(AppContext.BaseDirectory, "supervisor", "BuildOrchestrator.Supervisor.exe")));
        // Üretimde ~50ms tick — Task 11'in kanıtladığı batching davranışı; test'te enjekte edilen tick kullanılır.
        sc.AddSingleton(_ => new ConsoleBatcher(ct => Task.Delay(50, ct)));
        // [E1/T67] Satır hover ikonlarının OS eylemleri: gerçek Process.Start başlatıcısı + gerçek ProcessRunner
        // (vswhere→devenv). Testler osActions=null default'u kullanır (VM eylemleri güvenle no-op).
        sc.AddSingleton(sp => new RunViewModel(
            sp.GetRequiredService<EngineHost>(), sp.GetRequiredService<ConsoleBatcher>(), () => Guid.NewGuid().ToString(),
            osActions: new OsActions(new ProcessLauncher(), new ProcessRunner())));
        sc.AddSingleton<MainWindow>();
        Services = sc.BuildServiceProvider();
        var window = Services.GetRequiredService<MainWindow>();
        // İkinci instance'ın sinyali arka plan thread'inden gelir — UI thread'ine burada marshal edilir.
        _singleInstance.StartListening(() => Dispatcher.Invoke(window.ShowFromTray));
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // --font-ab yolunda DI hiç kurulmaz — Services null kalır.
        AppShutdown.WaitForAsyncDisposal(Services?.GetService<EngineHost>(), AppShutdown.DisposalTimeout);
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
