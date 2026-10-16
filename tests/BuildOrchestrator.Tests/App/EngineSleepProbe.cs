using BuildOrchestrator.App;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 3 · karar 35] Motor uykusunun ve uyanmasının TEK test sahtesi: VM'in iki dikişini (<see cref="RunViewModel.SleepEngine"/>,
/// <see cref="RunViewModel.WakeEngine"/>) konağın yerine alır, çağrıları sayar ve her çağrıda testin verdiği görevi döner — bitmiş (varsayılan),
/// bekletilen (TCS: uyku ya da uyanma sürüyor) ya da düşen. Yarış pencereleri böylece gerçek zamanlama olmadan açık tutulur. Uyku
/// (<c>EngineSleepTests</c>), uyandırma kapısı (<c>EngineWakeTests</c>) ve sessizlik bekçisi (<c>EngineSilenceWatchdogTests</c>) testleri bunu
/// paylaşır; ilk hâli <c>EngineSleepTests</c>'in iç <c>SleepProbe</c>'uydu. Uyuyabilen kabuğun kurulumu (<see cref="ReadyShell"/>), gerçek
/// motorlu çıplak VM (<see cref="RealEngineVm"/>) ve boşta penceresinin ms hâli (<see cref="IdleAfterMs"/>) de burada tek yerdedir.
/// </summary>
internal sealed class EngineSleepProbe
{
    /// <summary>Motorun sürümü — hazır yolunun motoru ve uyanan motor aynı ikilidir.</summary>
    public const string EngineVersion = "1.0.0";

    /// <summary>Hazır yolunun (açılışın) motor süreci.</summary>
    public const int Pid = 4242;

    /// <summary>Uyanan motorun süreci — yeni bir süreç, açılışınkinden farklı.</summary>
    public const int WokenPid = 5150;

    /// <summary><see cref="EngineIdleRule.IdleAfter"/>'ın testlerdeki ms hâli — TEK tanım.</summary>
    public static readonly long IdleAfterMs = (long)EngineIdleRule.IdleAfter.TotalMilliseconds;

    /// <summary>Uyku dikişi kaç kez çağrıldı.</summary>
    public int SleepCalls { get; private set; }

    /// <summary>Uyanma dikişi kaç kez çağrıldı.</summary>
    public int WakeCalls { get; private set; }

    /// <summary>Uykunun her çağrıda döndüğü görev — varsayılan: uyku hemen biter.</summary>
    public Func<Task> NextSleep { get; set; } = () => Task.CompletedTask;

    /// <summary>Uyanmanın her çağrıda döndüğü görev — varsayılan: uyanma hemen biter, yeni süreç <see cref="WokenPid"/>.</summary>
    public Func<Task<EngineReadyEvent>> NextWake { get; set; } = () => Task.FromResult(Woken());

    /// <summary>Uyanan motorun hazır olayı (<see cref="WokenPid"/>, <see cref="EngineVersion"/>).</summary>
    public static EngineReadyEvent Woken() => new(WokenPid, EngineVersion);

    private Task Sleep()
    {
        SleepCalls++;
        return NextSleep();
    }

    private Task<EngineReadyEvent> Wake()
    {
        WakeCalls++;
        return NextWake();
    }

    /// <summary>VM'in uyku ve uyanma dikişlerini yeni bir sahteye bağlar.</summary>
    public static EngineSleepProbe Attach(RunViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        var probe = new EngineSleepProbe();
        vm.SleepEngine = probe.Sleep;
        vm.WakeEngine = probe.Wake;
        return probe;
    }

    /// <summary>Uyuyabilen kabuk: topolojisi akmış pencere (tek proje "A"), saat testin elinde, iki dikiş sahte, ayar AÇIK (fixture döndükten
    /// sonra — <paramref name="stopWhenIdle"/>), gönderimler kabul edilir ve motor HAZIR: kabuğun açılış yolu — hazır bildirimi
    /// (<c>OnEngineReady</c>) açılış Sync'ini gönderir, motor onu cevaplar. Dönüşte VM boştadır ve son motor etkinliği şimdidir. Tam çıkış
    /// isteyen testler gerçek uygulamayı kapatmaz ve pencere göstermez: kabuğun iki çıkış dikişi no-op'tur
    /// (<see cref="MainWindowHost.NeutralizeExitSeams"/>).</summary>
    public static (MainWindow Window, RunViewModel Vm, EngineSleepProbe Probe) ReadyShell(TempDir dir, Func<long> nowMs,
        bool stopWhenIdle = true)
    {
        var (window, vm, _) = MainWindowHost.NewWithProjectsAndClock(dir, nowMs, ("A", null));
        MainWindowHost.NeutralizeExitSeams(window);
        if (stopWhenIdle) vm.StopEngineWhenIdle = true;
        var probe = Attach(vm);
        MainWindowHost.AcceptSends(vm);
        vm.OnEngineReady(EngineVersion, Pid);
        MainWindowHost.ReplySync(vm, ("A", null));
        Assert.False(vm.IsOperationInFlight); // ön-koşul: boşta
        return (window, vm, probe);
    }

    /// <summary>Gerçek motorlu çıplak VM: konak testin (izole önbellekli — <c>SupervisorSandbox.IsolatedEngineHost</c>), saat testin elinde,
    /// workspace yok (hazır yolu Sync göndermez), uyku ayarı AÇIK. Dikişlerin üretim varsayılanlarını (konağın kendisi) sınayan testlerin TEK
    /// kurulumu.</summary>
    public static RunViewModel RealEngineVm(EngineHost engine, Func<long> nowMs) =>
        new(engine, MainWindowHost.NeverTickingBatcher(), () => "r1", nowMs)
        {
            LegacyWorktreePoolRoot = TestPaths.MissingLegacyPoolRoot,
            StopEngineWhenIdle = true,
        };
}
