using System.IO;
using System.Windows;
using System.Windows.Threading;
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

    /// <summary>[E2/FIX1] İkinci-instance balloon'unu shell (explorer.exe) RENDER edene kadar geçici tray'in
    /// yaşadığı süre. Balloon isteğiyle AYNI dispatcher turn'ünde tray'i yıkmak (NIM_DELETE) balloon'u iptal
    /// eder → kullanıcı hiçbir şey görmez; bu gecikmeden sonra tray bırakılır ve süreç AYRIŞAN kodla kapanır.</summary>
    private static readonly TimeSpan SecondInstanceBalloonLinger = TimeSpan.FromSeconds(2.5);

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
            // [E2/FIX1] Karar (balloon?/çıkış kodu) saf, WPF'siz dikişe (SecondInstanceGate) taşındı — iki dalı da
            // test edilir. Öne getirildiyse: sessiz ve temiz kapan (kod 0).
            var outcome = SecondInstanceGate.Decide(_singleInstance.ActivateExistingInstance(TimeSpan.FromSeconds(3)));
            if (!outcome.ShowBalloon)
            {
                Shutdown(outcome.ExitCode);
                return;
            }
            // Öne getirilemedi → SESSİZ KALMA: tek-satırlık OS balloon göster. ShowNotification balloon'u
            // explorer.exe'ye ASENKRON teslim eder; tray'i AYNI dispatcher turn'ünde yıkarsak (Shutdown → OnExit →
            // Dispose → NIM_DELETE) balloon milisaniyeler içinde iptal olur. Bu yüzden yıkımı ERTELE (aşağıda).
            _secondInstanceTray = new AppTrayIcon();
            _secondInstanceTray.ShowNotification(
                "Build Orchestrator",
                "Already running — could not bring the existing window forward. Use the tray icon or Alt+B.");
            ScheduleSecondInstanceShutdown(outcome.ExitCode);
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

    /// <summary>[E2/FIX1] İkinci-instance balloon'unun explorer tarafından RENDER edilmesine zaman tanır: geçici
    /// tray'i balloon isteğiyle AYNI dispatcher turn'ünde YIKMAK yerine kısa bir <see cref="DispatcherTimer"/>
    /// gecikmesiyle bırakır, sonra AYRIŞAN çıkış koduyla kapanır. Tick'te tray null'lanır → sonraki
    /// <see cref="OnExit"/>'in Dispose'u no-op olur (çift-dispose yok).</summary>
    private void ScheduleSecondInstanceShutdown(int exitCode)
    {
        var timer = new DispatcherTimer { Interval = SecondInstanceBalloonLinger };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _secondInstanceTray?.Dispose();
            _secondInstanceTray = null;
            Shutdown(exitCode);
        };
        timer.Start();
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
