using System.Reflection;
using System.Windows.Threading;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Motorun bir olay PATLAMASI UI thread'ini tek blokta tutmaz: patlamanın arkasında bekleyen girdi (ve çizim) patlama
/// bitmeden sıra alır; olaylar yine geliş sırasıyla işlenir.
///
/// <para><b>Neden (ÖLÇÜLDÜ, 2026-10-10):</b> koşu başında motor <c>runStarted</c> + <c>buildPreview</c> + her atlanan proje
/// için bir <c>projectSkipped</c> gönderir (OSYS'te değişmemiş bir Build: 184 olay, aynı anda). Pencere her olayı ayrı bir
/// <c>Dispatcher.InvokeAsync</c> ile Normal öncelikte taşıyordu; Normal, çizimin ve girdinin ÜSTÜNDE olduğu için 186 olayın
/// hepsi tek blokta koştu — iz: UI thread'i 312 ms aralıksız (olay işleyicileri 119 ms + toplu yerleşim/çizim 145 ms),
/// gerçek koşuda 115–145 ms girdi gecikmesi ve 210–260 ms'lik kare boşluğu, her koşunun başında.</para>
///
/// <para>Testler üretim yolundan geçer: olay motorun <c>EventReceived</c>'ından (okuma thread'ini taklit eden bir arka plan
/// görevinden) pencerenin kendi aboneliğine iner. <c>EventReceived</c> alan-benzeri bir olay olduğu için çağrı, derleyicinin
/// ürettiği temsilci alanı yansımayla okunarak yapılır — üretim koduna test yüzeyi eklenmez.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class EngineEventBurstTests
{
    private const int ProjectCount = 177;

    private static void RaiseFromReaderThread(MainWindow window, IReadOnlyList<IpcEvent> events)
    {
        var engine = (EngineHost)typeof(MainWindow)
            .GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var received = (Action<IpcEvent>)typeof(EngineHost)
            .GetField(nameof(EngineHost.EventReceived), BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
        Task.Run(() => { foreach (var ev in events) received(ev); }).Wait();
    }

    private static (MainWindow Window, RunViewModel Vm, List<ProjectNode> Nodes) Synced(TempDir temp)
    {
        var (window, vm) = MainWindowHost.New(temp);
        var content = MainWindowHost.Realize(window);
        vm.RootPath = @"C:\src\OSYS";
        var nodes = UiResponsivenessBudgetTests.Topology(ProjectCount);
        vm.OnEvent(new WorkspaceTopologyEvent(nodes, [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha12345", false, nodes.Count, 0));
        content.UpdateLayout();
        return (window, vm, nodes);
    }

    private static int RowsIn(RunViewModel vm, ProjectRowState state) => vm.Projects.Count(p => p.State == state);

    [StaFact]
    public void Input_queued_behind_the_run_start_burst_is_served_before_the_burst_ends()
    {
        using var temp = new TempDir();
        var (window, vm, nodes) = Synced(temp);
        var burst = new List<IpcEvent>
        {
            new RunStartedEvent("r1", RunMode.Build, nodes.Count, 4, "Debug", null),
            new BuildPreviewEvent([.. nodes.Select(n => new BuildPreviewItem(n.Id, n.Name, false, "abc1234"))]),
        };
        burst.AddRange(nodes.Select(n => new ProjectSkippedEvent("r1", n.Id, SkipReasons.UpToDate)));

        RaiseFromReaderThread(window, burst);
        int skippedWhenInputRan = -1;
        window.Dispatcher.InvokeAsync(() => skippedWhenInputRan = RowsIn(vm, ProjectRowState.Skipped), DispatcherPriority.Input);
        DispatcherPump.PumpUntil(() => skippedWhenInputRan >= 0 && RowsIn(vm, ProjectRowState.Skipped) == ProjectCount,
            TimeSpan.FromSeconds(20));

        Assert.Equal(ProjectCount, RowsIn(vm, ProjectRowState.Skipped)); // non-vacuous: patlamanın tamamı işlendi
        Assert.True(skippedWhenInputRan is >= 0 and < ProjectCount,
            $"patlamanın arkasındaki girdi {skippedWhenInputRan}/{ProjectCount} atlanan olaydan sonra koştu — UI thread'i patlama boyunca hiç bırakılmadı");
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_sliced_burst_still_applies_events_in_arrival_order()
    {
        using var temp = new TempDir();
        var (window, vm, nodes) = Synced(temp);
        var burst = new List<IpcEvent> { new RunStartedEvent("r1", RunMode.Build, nodes.Count, 4, "Debug", null) };
        foreach (var n in nodes)
        {
            burst.Add(new ProjectStartedEvent("r1", n.Id, n.Name));
            burst.Add(new ProjectSucceededEvent("r1", n.Id, 120));
        }

        RaiseFromReaderThread(window, burst);
        DispatcherPump.PumpUntil(() => RowsIn(vm, ProjectRowState.Succeeded) == ProjectCount, TimeSpan.FromSeconds(20));

        // Sıra bozulsaydı bir projenin "started"ı "succeeded"ından SONRA işlenir ve satır derleniyor durumda kalırdı.
        Assert.Equal(ProjectCount, RowsIn(vm, ProjectRowState.Succeeded));
        Assert.Equal(0, RowsIn(vm, ProjectRowState.Started));
        GC.KeepAlive(window);
    }
}
