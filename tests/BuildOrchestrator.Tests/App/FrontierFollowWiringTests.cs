using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Frontier follow'un <b>ÜRETİM KABLAJI</b>: koşan bir run'da derlenmekte olan satır kendiliğinden görünür
/// kalır. Zincir <c>MainWindow._elapsedTimer</c> (200 ms) → <c>FollowFrontier</c> →
/// <c>ScrollArbiter.CanFollowFrontier</c> → <c>StickyLayerList.FollowRow</c> →
/// <c>FollowScrollController</c> → <c>ScrollAnimator</c>'dır.
///
/// <para><b>Neden bu test var:</b> süitte follow'un KARAR çekirdeği (<c>FollowScrollDecision</c>: 550 ms
/// throttle, 54 px dead-band) ve KAPILARI (arbiter + wheel-suppress) ayrı ayrı pinliydi, ama zincirin kendisi —
/// timer'ın gerçekten tetiklemesi, frontier satırın bulunması, listenin GERÇEKTEN kayması — hiçbir yerde
/// sınanmıyordu. Yani halkalardan biri kopsa süit yeşil kalırdı.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class FrontierFollowWiringTests(ITestOutputHelper output)
{
    private const int ProjectCount = 60;
    private const int FrontierIndex = 45; // görünür pencerenin ÇOK altında — takip etmezse ekranda görünmez

    private static List<ProjectNode> Topology() =>
        [.. Enumerable.Range(0, ProjectCount).Select(i => new ProjectNode(
            $@"C:\p\Proj{i}.csproj", $"Proj{i}", $@"C:\p\Proj{i}.csproj",
            ["Osys"], [], i, null, null, false, null))];

    /// <summary>Derlenmeye başlayan satır viewport'un altındaysa liste ona doğru kayar — ve satır görünür olur.</summary>
    [StaFact]
    public void A_building_row_below_the_viewport_pulls_the_list_to_it()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        var content = MainWindowHost.Realize(window);
        vm.RootPath = @"C:\src\OSYS";

        var nodes = Topology();
        vm.OnEvent(new WorkspaceTopologyEvent(nodes, [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha12345", false, nodes.Count, 0));
        content.UpdateLayout();

        var list = window.Shell.ProjectsList;
        double before = list.Scroll.VerticalOffset;

        // Run başlar ve AŞAĞIDAKİ bir proje derlenmeye başlar → follow onu görünür kılmalı.
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, nodes.Count, 4, "Debug", 0, null));
        vm.OnEvent(new ProjectStartedEvent("r1", nodes[FrontierIndex].Id, nodes[FrontierIndex].Name));

        // Üretim tetiği DispatcherTimer'dır — pompalanmadan hiç ateşlenmez (test bu yüzden gerçek zincirdir).
        DispatcherPump.PumpUntil(() => list.Scroll.VerticalOffset > before + 1, TimeSpan.FromSeconds(3));
        content.UpdateLayout();

        double after = list.Scroll.VerticalOffset;
        double rowTop = list.Metrics!.OffsetOfRow(FrontierIndex);
        double viewportBottom = after + list.Scroll.ViewportHeight;
        output.WriteLine($"[follow] offset {before:N1} → {after:N1} · frontier satır Y {rowTop:N1} · viewport {after:N1}..{viewportBottom:N1}");

        Assert.True(after > before,
            $"derlenen satır (index {FrontierIndex}, Y {rowTop:N1}) için liste HİÇ kaymadı — frontier follow zinciri kopuk.");
        Assert.True(rowTop >= after && rowTop + LayoutMetrics.DefaultRowHeight <= viewportBottom,
            $"derlenen satır görünür pencereye girmedi: satır Y {rowTop:N1}, pencere {after:N1}..{viewportBottom:N1}.");
        GC.KeepAlive(window);
    }

    /// <summary>Frontier ilerledikçe takip DEVAM eder — tek seferlik değildir.</summary>
    [StaFact]
    public void The_list_keeps_following_as_the_frontier_advances()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        var content = MainWindowHost.Realize(window);
        vm.RootPath = @"C:\src\OSYS";

        var nodes = Topology();
        vm.OnEvent(new WorkspaceTopologyEvent(nodes, [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha12345", false, nodes.Count, 0));
        content.UpdateLayout();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, nodes.Count, 4, "Debug", 0, null));

        var list = window.Shell.ProjectsList;
        vm.OnEvent(new ProjectStartedEvent("r1", nodes[20].Id, nodes[20].Name));
        DispatcherPump.PumpUntil(() => list.Scroll.VerticalOffset > 1, TimeSpan.FromSeconds(3));
        double atTwenty = list.Scroll.VerticalOffset;

        // Frontier ilerler: 20 biter, 50 başlar.
        vm.OnEvent(new ProjectSucceededEvent("r1", nodes[20].Id, 100));
        vm.OnEvent(new ProjectStartedEvent("r1", nodes[50].Id, nodes[50].Name));
        DispatcherPump.PumpUntil(() => list.Scroll.VerticalOffset > atTwenty + 1, TimeSpan.FromSeconds(3));
        double atFifty = list.Scroll.VerticalOffset;

        output.WriteLine($"[follow] frontier 20 → offset {atTwenty:N1} · frontier 50 → offset {atFifty:N1}");
        Assert.True(atFifty > atTwenty,
            $"frontier 20'den 50'ye ilerledi ama liste takip etmedi ({atTwenty:N1} → {atFifty:N1}).");
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [cycles] Takip, bir döngü grubunun İÇİNDE sıra üyeden üyeye geçerken de sürer.
    ///
    /// <para>Sahada görülen kusur: "Resolve cycles" koşusunda liste ilk üyede kalıyor, derlenen proje
    /// ekranda görünmüyordu (normal Build'de takip çalışıyordu). Nedeni frontier'in HAM motor durumunu
    /// (<c>State == Started</c>) okumasıydı: bir SCC'nin üyeleri tek tek invoke edilir ama ara tur sonuçları
    /// yayılmadığı için grup bitene kadar HEPSİ <c>Started</c>'ta kalır — <c>FrontierRowIndex</c> hep listedeki
    /// İLK üyeyi bulur, dead-band da devreye girince liste bir daha hiç kaymaz.</para>
    ///
    /// <para>Doğru soru <c>ProjectRowViewModel.IsCompiling</c>'dir (Started <b>ve</b> sırası bekleyen değil) —
    /// aynı predicate'i satır glyph'i, sayaçlar, şerit chip'leri, kart nefesi ve süre sütunu da okur. Frontier
    /// o listenin kaçırılmış tüketicisiydi.</para>
    /// </summary>
    [StaFact]
    public void The_list_follows_the_turn_inside_a_running_cycle_group()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        var content = MainWindowHost.Realize(window);
        vm.RootPath = @"C:\src\OSYS";

        // 20 ve 50 AYNI döngünün üyeleri — listede birbirinden uzaktalar ki takip ölçülebilsin.
        var nodes = Topology();
        int[] members = [20, 50];
        foreach (int i in members)
            nodes[i] = nodes[i] with { InCycle = true };
        vm.OnEvent(new WorkspaceTopologyEvent(nodes, [[.. members.Select(i => nodes[i].Id)]], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha12345", false, nodes.Count, 0));
        content.UpdateLayout();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Cycles, nodes.Count, 4, "Debug", 0, null));

        var list = window.Shell.ProjectsList;
        vm.OnEvent(new ProjectStartedEvent("r1", nodes[20].Id, nodes[20].Name)); // sıra ilk üyede
        DispatcherPump.PumpUntil(() => list.Scroll.VerticalOffset > 1, TimeSpan.FromSeconds(3));
        double atFirst = list.Scroll.VerticalOffset;

        // Sıra ikinci üyeye geçer. Grup bitmediği için BİRİNCİ üye hâlâ Started'tır — kusur tam burada çıkar.
        vm.OnEvent(new ProjectStartedEvent("r1", nodes[50].Id, nodes[50].Name));
        DispatcherPump.PumpUntil(() => list.Scroll.VerticalOffset > atFirst + 1, TimeSpan.FromSeconds(3));
        double atSecond = list.Scroll.VerticalOffset;

        Assert.Equal(ProjectRowState.Started, vm.Projects.First(p => p.Id == nodes[20].Id).State); // ön-koşul
        output.WriteLine($"[follow] üye 20 → offset {atFirst:N1} · üye 50 → offset {atSecond:N1}");
        Assert.True(atSecond > atFirst,
            $"döngü grubunda sıra 20'den 50'ye geçti ama liste takip etmedi ({atFirst:N1} → {atSecond:N1}).");
        GC.KeepAlive(window);
    }
}
