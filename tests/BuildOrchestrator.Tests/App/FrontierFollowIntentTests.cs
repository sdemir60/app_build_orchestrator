using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Frontier follow, kullanıcının AÇIK NİYETİNE saygı duyar: bir kart seçtiyse ya da listeyi filtrelediyse
/// liste onun altından kaymaz. İkisi de "şu an şuna bakıyorum" beyanıdır; otomatik takip onları ezemez.
///
/// <para>Zincir <b>üretim yolundan</b> sürülür (<c>MainWindow._elapsedTimer</c> → <c>FollowFrontier</c> →
/// arbiter → liste), çünkü kusur tam olarak halkalar arasında oluşur: seçim kapısı arbiter'da SAF olarak
/// pinliydi ama koşan bir run'da gerçekten uygulanıp uygulanmadığı sınanmıyordu.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class FrontierFollowIntentTests(ITestOutputHelper output)
{
    private const int ProjectCount = 60;

    private static List<ProjectNode> Topology() =>
        [.. Enumerable.Range(0, ProjectCount).Select(i => new ProjectNode(
            $@"C:\p\Proj{i}.csproj", $"Proj{i}", $@"C:\p\Proj{i}.csproj",
            ["Osys"], [], i, null, null, false, null))];

    private static (MainWindow window, RunViewModel vm, StickyLayerList list, List<ProjectNode> nodes)
        RunningWithFrontier(TempDir temp, int frontierIndex)
    {
        var (window, vm) = MainWindowHost.New(temp);
        var content = MainWindowHost.Realize(window);
        vm.RootPath = @"C:\src\OSYS";
        var nodes = Topology();
        vm.OnEvent(new WorkspaceTopologyEvent(nodes, [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha12345", false, nodes.Count, 0));
        content.UpdateLayout();

        var list = window.Shell.ProjectsList;
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, nodes.Count, 4, "Debug", 0, null));
        vm.OnEvent(new ProjectStartedEvent("r1", nodes[frontierIndex].Id, nodes[frontierIndex].Name));
        DispatcherPump.PumpUntil(() => list.Scroll.VerticalOffset > 1, TimeSpan.FromSeconds(3));
        return (window, vm, list, nodes);
    }

    /// <summary>Frontier'i ilerletir; liste kaydıysa true.</summary>
    private static bool FollowsTo(RunViewModel vm, StickyLayerList list, List<ProjectNode> nodes, int from, int to)
    {
        double before = list.Scroll.VerticalOffset;
        vm.OnEvent(new ProjectSucceededEvent("r1", nodes[from].Id, 100));
        vm.OnEvent(new ProjectStartedEvent("r1", nodes[to].Id, nodes[to].Name));
        DispatcherPump.PumpUntil(() => Math.Abs(list.Scroll.VerticalOffset - before) > 1, TimeSpan.FromSeconds(2));
        return Math.Abs(list.Scroll.VerticalOffset - before) > 1;
    }

    [StaFact]
    public void A_selected_row_stops_the_list_from_following_the_frontier()
    {
        using var temp = new TempDir();
        var (window, vm, list, nodes) = RunningWithFrontier(temp, frontierIndex: 20);

        vm.SelectProject(nodes[20].Id);          // kullanıcı bir kart seçti (logunu okuyor)
        bool followed = FollowsTo(vm, list, nodes, 20, 45);

        output.WriteLine($"[niyet] kart seçiliyken takip: {followed}");
        Assert.False(followed, "kart seçiliyken liste frontier'i takip etti — seçim ezildi.");

        // Seçim kalkınca takip geri gelmeli (kapı KALICI değil).
        vm.SelectProject(null);
        Assert.True(FollowsTo(vm, list, nodes, 45, 55), "seçim kalktı ama takip geri gelmedi.");
        GC.KeepAlive(window);
    }

    [StaFact]
    public void An_active_filter_stops_the_list_from_following_the_frontier()
    {
        using var temp = new TempDir();
        var (window, vm, list, nodes) = RunningWithFrontier(temp, frontierIndex: 20);

        vm.ToggleFilter("building");             // kullanıcı listeyi süzdü — "şu an şuna bakıyorum"
        bool followed = FollowsTo(vm, list, nodes, 20, 45);

        output.WriteLine($"[niyet] filtre açıkken takip: {followed}");
        Assert.False(followed, "filtre açıkken liste frontier'i takip etti — filtre niyeti ezildi.");

        // Filtre kalkınca takip geri gelmeli.
        vm.ToggleFilter("building");
        Assert.True(FollowsTo(vm, list, nodes, 45, 55), "filtre kalktı ama takip geri gelmedi.");
        GC.KeepAlive(window);
    }
}
