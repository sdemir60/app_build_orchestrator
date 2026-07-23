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
    /// <summary>[E2/triaj-f] İkinci instance mevcut pencereyi öne GETİREMEDİĞİNDE kullanılan AYRIŞAN çıkış kodu —
    /// normal ikinci-instance (başarıyla öne getirdi → 0) ile ayırt edilebilsin ve kullanıcı SESSİZ kalmasın
    /// (ayrıca bir tray balloon gösterilir).</summary>
    public const int SecondInstanceActivationFailedExitCode = 3;

    /// <summary>[E2/T16] Registry autostart komutunun exe'ye eklediği argüman — bu argümanla açılan uygulama
    /// tepside (gizli) temiz başlar (bkz. <see cref="MainWindow.StartInTray"/>).</summary>
    public const string AutostartArg = "--autostart";

    public static ServiceProvider Services { get; private set; } = null!;
    public static IMotionSettings Motion { get; private set; } = null!;

    private SingleInstanceGuard? _singleInstance;
    private AppTrayIcon? _secondInstanceTray; // yalnız ikinci-instance-aktive-edilemedi yolunda geçici olarak yaşar

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
            // İkinci instance: çalışan pencereyi öne getir (AllowSetForegroundWindow sinyalden ÖNCE — sırayı
            // SingleInstanceProtocol garanti eder). [E2/triaj-f] Dönüş ARTIK yakalanır: öne getirilebildiyse
            // sessizce kapan (mevcut davranış); GETİRİLEMEDİYSE (pipe cevapsız/hata) SESSİZ KALMA — kullanıcıya
            // tek-satırlık tray balloon göster ve AYRIŞAN bir çıkış koduyla kapan.
            if (_singleInstance.ActivateExistingInstance(TimeSpan.FromSeconds(3)))
            {
                Shutdown();
                return;
            }
            _secondInstanceTray = new AppTrayIcon();
            _secondInstanceTray.ShowNotification(
                "Build Orchestrator",
                "Already running — could not bring the existing window forward. Use the tray icon or Alt+B.");
            Shutdown(SecondInstanceActivationFailedExitCode);
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

        // [E2/T16] Autostart tercihini (UiState.Autostart) registry ile HİZALA (idempotent — her açılışta güvenli):
        // true → HKCU\...\Run altına "<exe> --autostart" yazılır, false → silinir. Registry erişimi seam arkasında.
        var uiState = new JsonUiStateStore(JsonUiStateStore.DefaultPath).Load();
        new AutostartService(new RegistryAutostartRegistry(), AutostartService.DefaultValueName, AutostartCommand())
            .Apply(uiState.Autostart);

        var window = Services.GetRequiredService<MainWindow>();
        // İkinci instance'ın sinyali arka plan thread'inden gelir — UI thread'ine burada marshal edilir.
        _singleInstance.StartListening(() => Dispatcher.Invoke(window.ShowFromTray));

        // [E2/T16] Autostart argümanıyla açıldıysa pencere GÖSTERİLMEDEN tepside temiz başlar (oto-Sync YOK — normal
        // açılışta da yok); aksi halde bugünkü davranış (normal göster).
        if (e.Args.Contains(AutostartArg)) window.StartInTray();
        else window.Show();
    }

    /// <summary>[E2/T16] Registry autostart değerine yazılacak komut: mevcut exe'nin tam yolu + <see cref="AutostartArg"/>.</summary>
    private static string AutostartCommand()
    {
        string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "BuildOrchestrator.App.exe");
        return $"\"{exe}\" {AutostartArg}";
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // --font-ab yolunda DI hiç kurulmaz — Services null kalır.
        AppShutdown.WaitForAsyncDisposal(Services?.GetService<EngineHost>(), AppShutdown.DisposalTimeout);
        _secondInstanceTray?.Dispose(); // [E2/triaj-f] geçici ikinci-instance balloon ikonu (varsa) bırakılır
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
