using System.Windows;
using System.Windows.Input;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Tekerlekle duraklatılan frontier follow'un GERİ AÇILMA yolları — <b>üretim zinciri üzerinden</b>
/// (<c>MainWindow._elapsedTimer</c> → <c>FollowFrontier</c> → liste).
///
/// <para><b>Neden iki yol:</b> tek geri-açılma koşulu "listenin DİBİNE 48 px kalana dön"dü ve bu, konsol/stream'in
/// bottom-anchor eşiğiyle geometrik olarak simetrikti ama ANLAMCA değildi: o iki panelde dip yeni içeriğin geldiği
/// yerdir, proje listesinde ise ilgi çekici yer <b>frontier</b>'dır ve o, koşarken listenin ORTASINDADIR. Sonuç,
/// tek bir tekerlek hareketinin follow'u run'ın geri kalanında park etmesiydi (kullanıcı raporu). İki yol eklendi:
/// kullanıcı frontier'e geri döndüğünde, ya da bir süre listeye hiç dokunmadığında takip sürer.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class FrontierFollowResumeTests(ITestOutputHelper output)
{
    private const int ProjectCount = 60;

    private static List<ProjectNode> Topology() =>
        [.. Enumerable.Range(0, ProjectCount).Select(i => new ProjectNode(
            $@"C:\p\Proj{i}.csproj", $"Proj{i}", $@"C:\p\Proj{i}.csproj",
            ["Osys"], [], i, null, null, false, null))];

    private static void RaiseFrontierWheel(StickyLayerList list) =>
        list.Scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
        { RoutedEvent = UIElement.PreviewMouseWheelEvent });

    /// <summary>Koşan bir run + frontier satırı; liste follow etmiş durumda döner.</summary>
    private static (MainWindow window, RunViewModel vm, StickyLayerList list, List<ProjectNode> nodes, long[] clock)
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
        long[] clock = [0];
        list.NowMs = () => clock[0]; // idle penceresi deterministik sürülür (D8)

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, nodes.Count, 4, "Debug", 0, null));
        vm.OnEvent(new ProjectStartedEvent("r1", nodes[frontierIndex].Id, nodes[frontierIndex].Name));
        DispatcherPump.PumpUntil(() => list.Scroll.VerticalOffset > 1, TimeSpan.FromSeconds(3));
        return (window, vm, list, nodes, clock);
    }

    /// <summary>Frontier'i ilerletir ve listenin onu takip edip etmediğini döndürür.</summary>
    private static bool FollowsTo(RunViewModel vm, StickyLayerList list, List<ProjectNode> nodes, int from, int to)
    {
        double before = list.Scroll.VerticalOffset;
        vm.OnEvent(new ProjectSucceededEvent("r1", nodes[from].Id, 100));
        vm.OnEvent(new ProjectStartedEvent("r1", nodes[to].Id, nodes[to].Name));
        DispatcherPump.PumpUntil(() => Math.Abs(list.Scroll.VerticalOffset - before) > 1, TimeSpan.FromSeconds(2));
        return Math.Abs(list.Scroll.VerticalOffset - before) > 1;
    }

    [StaFact]
    public void Returning_to_the_frontier_resumes_follow()
    {
        using var temp = new TempDir();
        var (window, vm, list, nodes, _) = RunningWithFrontier(temp, frontierIndex: 30);

        // 1) Tekerlek → follow duraklar; kullanıcı UZAĞA (tepeye) gider ve orada kalır.
        RaiseFrontierWheel(list);
        list.Scroll.ScrollToVerticalOffset(0);
        list.UpdateLayout();
        Assert.False(FollowsTo(vm, list, nodes, 30, 40)); // uzaktayken takip ETMEZ (doğru)

        // 2) Kullanıcı O ANKİ frontier satırının (40) yanına geri döner → duraklama kalkmalı.
        double frontierTop = list.Metrics!.OffsetOfRow(40);
        list.Scroll.ScrollToVerticalOffset(Math.Max(0, frontierTop - list.Scroll.ViewportHeight / 2));
        list.UpdateLayout();
        DispatcherPump.PumpUntil(() => !ScrollAnimator.GetIsUserSuppressed(list.Scroll), TimeSpan.FromSeconds(2));

        output.WriteLine($"[resume-frontier] duraklama kalktı mı: {!ScrollAnimator.GetIsUserSuppressed(list.Scroll)}");
        Assert.False(ScrollAnimator.GetIsUserSuppressed(list.Scroll),
            "kullanıcı frontier'e geri döndü ama follow hâlâ duraklı — yalnız 'liste dibi' geri açıyor.");

        // 3) ...ve takip GERÇEKTEN sürer: frontier ilerleyince liste onu izler.
        bool followed = FollowsTo(vm, list, nodes, 40, 50);
        output.WriteLine($"[resume-frontier] frontier ilerleyince takip: {followed}");
        Assert.True(followed, "duraklama kalktı ama liste frontier'i izlemiyor.");
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Leaving_the_list_alone_for_a_while_resumes_follow()
    {
        using var temp = new TempDir();
        var (window, vm, list, nodes, clock) = RunningWithFrontier(temp, frontierIndex: 30);

        // 1) Tekerlek → follow duraklar; kullanıcı uzakta kalır ve HİÇ dokunmaz.
        RaiseFrontierWheel(list);
        list.Scroll.ScrollToVerticalOffset(0);
        list.UpdateLayout();
        Assert.False(FollowsTo(vm, list, nodes, 30, 40)); // hemen ardından takip ETMEZ (doğru)

        // 2) Boşta geçen süre eşiği aşar → takip kendiliğinden sürmeli.
        clock[0] += StickyLayerList.FrontierIdleResumeMs + 1;

        bool followed = FollowsTo(vm, list, nodes, 40, 50);
        output.WriteLine($"[resume-idle] {StickyLayerList.FrontierIdleResumeMs} ms dokunmama sonrası takip: {followed}");
        Assert.True(followed,
            $"listeye {StickyLayerList.FrontierIdleResumeMs} ms dokunulmadı ama follow hâlâ duraklı.");
        GC.KeepAlive(window);
    }

    /// <summary>Kullanıcı AKTİF kaydırırken boşta-geri-açılma tetiklenmez: her tekerlek hareketi saati sıfırlar.</summary>
    [StaFact]
    public void Active_scrolling_keeps_follow_paused_because_each_wheel_restarts_the_idle_window()
    {
        using var temp = new TempDir();
        var (window, vm, list, nodes, clock) = RunningWithFrontier(temp, frontierIndex: 30);

        list.Scroll.ScrollToVerticalOffset(0);
        list.UpdateLayout();
        for (int i = 0; i < 4; i++)
        {
            clock[0] += StickyLayerList.FrontierIdleResumeMs - 1; // eşiğin hep BİR TIK altında kal
            RaiseFrontierWheel(list);
        }

        bool followed = FollowsTo(vm, list, nodes, 30, 40);
        output.WriteLine($"[resume-idle] aktif kaydırma sırasında takip: {followed}");
        Assert.False(followed, "kullanıcı hâlâ kaydırıyorken follow devreye girdi — boşta penceresi sıfırlanmıyor.");
        GC.KeepAlive(window);
    }
}
