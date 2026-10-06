using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Pencere tepsiye inince kalan SONSUZ dekoratif saatler — satır nefesi, şerit süpürmesi, graf beads'i ve seçim
/// kenarı akışı — durmalıdır (ARCHITECTURE §14.5: "every infinite animation is gated on IsVisible").
/// <see cref="HiddenCursorClockTests"/>'in imleç saatleri için pinlediği kuralın aynısı, aynı gerekçeyle:
///
/// <para><b>Neden yalnız "gizlenince durdur" yetmez:</b> <c>X</c> pencereyi gizler, görünümleri boşaltmaz —
/// <c>Unloaded</c> hiç ateşlenmez ve saatler tepside de dönmeye devam eder. Başlatıcılar da olaylarla yeniden
/// çağrılır (satırda VM değişimi → <c>ApplyBreathing</c>, şeritte faz/boyut → <c>ApplyIndeterminate</c>, grafta
/// statü itişi ve seçim → beads/akış saati) ve tepsideyken de koşar: yalnız <c>IsVisibleChanged</c>'de durduran
/// bir kapı saati bir sonraki olayda geri kurardı. Kapı başlatıcının kendisindedir; her sahip için ikinci test
/// tam olarak bunu pinler (gizliyken yeni building geçişi saat kurmaz), üçüncüsü geri gelişi.</para>
///
/// <para><b>Görünümler GERÇEK host pencerede kurulur</b> (<c>DsResources.Realize</c> / <see cref="GraphTestView.Shown"/>):
/// bağlı olmayan bir ağaçta <c>IsVisible</c> her zaman false'tur, yani HWND'siz bir görünümde bu saatler artık hiç
/// kurulmaz. Saat OLMADIĞINI bekleyen reduced-motion testleri de (<c>GraphRenderTests</c>, <c>GraphSelectionFocusTests</c>,
/// <c>ReducedMotionCoverageTests</c>) aynı nedenle gösterilen host'ta kurulur: HWND'siz görünümde "saat yok" iddiası
/// reduced-motion'ı değil görünmezliği ölçerdi. Görünmezliğin kendisini bilerek HWND'siz görünümde pinleyen test bu sınıftaki
/// <c>A_never_shown_graph_builds_no_infinite_clock_even_while_a_node_builds</c>'dir.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class HiddenDecorativeClockTests
{
    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)), () => "r1")
        { RootPath = @"D:\repo" };

    // ---------------------------------------------------------------- proje satırı nefesi

    private static ProjectRow RealizeRow(ProjectRowViewModel vm, out Window window)
    {
        var row = new ProjectRow { AnimationsEnabledProvider = () => true, DataContext = vm };
        window = DsResources.Realize(DsResources.NewHost(), row);
        return row;
    }

    [StaFact]
    public void Hiding_the_window_stops_the_row_breathing_clock()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Started);
        var row = RealizeRow(vm, out var window);
        Assert.True(row.BreathLayer.HasAnimatedProperties, "ön-koşul: building satır GERÇEKTEN nefes almalı");

        window.Hide();

        Assert.False(row.BreathLayer.HasAnimatedProperties);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_hidden_row_does_not_start_breathing_when_it_begins_building()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Succeeded);
        var row = RealizeRow(vm, out var window);
        window.Hide();

        vm.State = ProjectRowState.Started; // tepsideyken bir koşu başlar — başlatıcıyı yeniden çağıran üretim yolu

        Assert.Equal(Visibility.Visible, row.BreathLayer.Visibility); // non-vacuous: satır building'i GERÇEKTEN işledi…
        Assert.False(row.BreathLayer.HasAnimatedProperties);            // …ve saat kurmadı
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Showing_the_window_again_resumes_the_row_breathing_clock()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Started);
        var row = RealizeRow(vm, out var window);
        window.Hide();
        Assert.False(row.BreathLayer.HasAnimatedProperties); // non-vacuous: gizlenince gerçekten durdu

        window.Show();
        window.UpdateLayout();

        Assert.True(row.BreathLayer.HasAnimatedProperties);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_row_that_starts_building_while_hidden_breathes_once_shown()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Succeeded);
        var row = RealizeRow(vm, out var window);
        window.Hide();
        vm.State = ProjectRowState.Started; // model gizliyken building'e geçti
        Assert.False(row.BreathLayer.HasAnimatedProperties); // non-vacuous: gizliyken gerçekten kurulmadı

        window.Show();
        window.UpdateLayout();

        Assert.Equal(Visibility.Visible, row.BreathLayer.Visibility);
        Assert.True(row.BreathLayer.HasAnimatedProperties); // dönüşte güncel model durumundan nefes başlar
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_row_that_stops_building_while_hidden_is_collapsed_and_clockless_once_shown()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Started);
        var row = RealizeRow(vm, out var window);
        Assert.True(row.BreathLayer.HasAnimatedProperties, "ön-koşul: building satır GERÇEKTEN nefes almalı");
        window.Hide();
        vm.State = ProjectRowState.Succeeded; // model gizliyken building'i terk etti

        window.Show();
        window.UpdateLayout();

        Assert.Equal(Visibility.Collapsed, row.BreathLayer.Visibility);
        Assert.False(row.BreathLayer.HasAnimatedProperties); // dönüşte bayat "building" durumundan saat geri gelmez
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- şerit belirsiz süpürmesi

    private static StickyRibbon RealizeRibbon(RunViewModel vm, out Window window)
    {
        var ribbon = new StickyRibbon { DataContext = vm, AnimationsEnabledProvider = () => true };
        window = DsResources.Realize(DsResources.NewHost(), ribbon);
        return ribbon;
    }

    [StaFact]
    public void Hiding_the_window_stops_the_ribbon_sweep()
    {
        var vm = NewVm();
        var ribbon = RealizeRibbon(vm, out var window);
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main")); // → Syncing: belirsiz mod
        Assert.True(ribbon.IsIndeterminate);
        Assert.True(ribbon.IndicatorTranslate.HasAnimatedProperties, "ön-koşul: süpürme GERÇEKTEN dönmeli");

        window.Hide();

        Assert.False(ribbon.IndicatorTranslate.HasAnimatedProperties);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_hidden_ribbon_does_not_start_the_sweep_when_a_sync_begins()
    {
        var vm = NewVm();
        var ribbon = RealizeRibbon(vm, out var window);
        window.Hide();

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main")); // tepsideyken Sync başlar

        Assert.True(ribbon.IsIndeterminate);                           // non-vacuous: şerit fazı GERÇEKTEN işledi…
        Assert.False(ribbon.IndicatorTranslate.HasAnimatedProperties); // …ve süpürme kurmadı
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Showing_the_window_again_resumes_the_ribbon_sweep()
    {
        var vm = NewVm();
        var ribbon = RealizeRibbon(vm, out var window);
        window.Hide();
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(ribbon.IndicatorTranslate.HasAnimatedProperties); // non-vacuous: gizliyken gerçekten kurulmadı

        window.Show();
        window.UpdateLayout();

        Assert.True(ribbon.IsIndeterminate);
        Assert.True(ribbon.IndicatorTranslate.HasAnimatedProperties);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- graf beads + seçim kenarı akışı

    private static IReadOnlyList<GraphNode> GraphNodes(GraphStatus data) =>
    [
        new("OSYS.Base", "OSYS.Base", 0, GraphStatus.Succeeded),
        new("OSYS.Data", "OSYS.Data", 1, data),
        new("OSYS.Api", "OSYS.Api", 2, GraphStatus.Queued),
    ];

    private static GraphView RealizeGraph(GraphStatus data, out Window window)
    {
        var view = GraphTestView.Shown(new Size(600, 400), out window, () => true);
        view.SetGraph(GraphNodes(data), [new("OSYS.Base", "OSYS.Data"), new("OSYS.Data", "OSYS.Api")]);
        return view;
    }

    [StaFact]
    public void Hiding_the_window_stops_the_graph_beads_and_edge_flow_clocks()
    {
        var view = RealizeGraph(GraphStatus.Building, out var window);
        view.SelectedNode = "OSYS.Data"; // seçim kenarları kurulur → akış saati
        Assert.NotNull(view.BeadsClock);    // ön-koşul: building düğüm GERÇEKTEN dönüyor
        Assert.NotNull(view.EdgeFlowClock); // ön-koşul: seçim kenarları GERÇEKTEN akıyor

        window.Hide();

        Assert.Null(view.BeadsClock);
        Assert.Null(view.EdgeFlowClock);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_hidden_graph_builds_no_clock_when_a_node_starts_building_or_the_selection_changes()
    {
        var view = RealizeGraph(GraphStatus.Queued, out var window);
        window.Hide();

        view.UpdateStatuses(GraphNodes(GraphStatus.Building)); // tepsideyken statü itişi
        view.SelectedNode = "OSYS.Data";                       // …ve seçim (kenarlar yeniden kurulur)

        Assert.True(view.NodeVisuals["OSYS.Data"].BeadsVisible); // non-vacuous: düğüm building'i GERÇEKTEN işledi…
        Assert.NotEmpty(view.SelectionEdgePaths);                // …kenarlar kurulu…
        Assert.Null(view.BeadsClock);                            // …ama saat yok
        Assert.Null(view.EdgeFlowClock);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_never_shown_graph_builds_no_infinite_clock_even_while_a_node_builds()
    {
        // HWND'siz görünüm: bağlı olmayan ağaçta IsVisible hep false — kapı bilerek kapalıdır. Sonsuz saat bekleyen testler
        // bu yüzden GraphTestView.Shown ile gösterilen host'ta kurulur (eski iddia ve gerekçe orada).
        var view = GraphTestView.Sized(new Size(600, 400), () => true);
        view.SetGraph(GraphNodes(GraphStatus.Building), [new("OSYS.Base", "OSYS.Data"), new("OSYS.Data", "OSYS.Api")]);
        view.SelectedNode = "OSYS.Data";

        Assert.True(view.NodeVisuals["OSYS.Data"].BeadsVisible); // non-vacuous: düğüm building'i GERÇEKTEN işledi…
        Assert.NotEmpty(view.SelectionEdgePaths);                // …kenarlar kurulu…
        Assert.Null(view.BeadsClock);                            // …ama görünmeyen ağaçta saat yok
        Assert.Null(view.EdgeFlowClock);
    }

    [StaFact]
    public void Showing_the_window_again_resumes_the_graph_clocks()
    {
        var view = RealizeGraph(GraphStatus.Queued, out var window);
        window.Hide();
        view.UpdateStatuses(GraphNodes(GraphStatus.Building));
        view.SelectedNode = "OSYS.Data";
        Assert.Null(view.BeadsClock); // non-vacuous: gizliyken gerçekten kurulmadı
        Assert.Null(view.EdgeFlowClock);

        window.Show();
        window.UpdateLayout();

        Assert.NotNull(view.BeadsClock);
        Assert.NotNull(view.EdgeFlowClock);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- bırakılan paylaşımlı saat zaman ağacından da çıkar

    /// <summary>
    /// Bırakılan paylaşımlı saat (beads, seçim kenarı akışı) hedeflerinden SÖKÜLMEKLE kalmaz, zaman ağacından da ÇIKAR.
    ///
    /// <para><b>Neden (ÖLÇÜLDÜ):</b> <c>CreateClock</c> ile kurulan saat zaman ağacının KÖK saatidir; <c>ApplyAnimationClock(null)</c>
    /// onu yalnız öğelerden söker, saat <c>Active</c> kalır ve bir <c>AnimationClock</c> aktifken HER karede tik ister. Gizli
    /// pencerede her tik kanalı yeniden gönderip UI thread'ini ekranın tazeleme hızında uyandırır: koşu görünürken bitip 3 s
    /// sonra gizlenen pencere tepside boşta 118 Mdöngü/s ölçüldü (koşu sürerken gizlenince 13; hedef ≤ 40). Yetim saati
    /// zayıf referansla tutan ağaç onu ancak bir GC'de bırakır ve boşta duran süreçte o GC hiç gelmez. Kabuk bütününde aynı
    /// kusuru <c>HiddenShellClockTests</c> pinler; burada iki sahip, bırakmanın üç yolunda (gizlenme, son derlemenin
    /// spin-down'ı, seçimin kalkması) ayrı ayrı sınanır.</para>
    /// </summary>
    private static void AssertRetired(AnimationClock clock)
    {
        // Çıkarma isteği zaman ağacının bir sonraki tik'inde işlenir — bir kare pompalanır.
        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(100));
        Assert.NotEqual(ClockState.Active, clock.CurrentState);
        Assert.DoesNotContain(ActiveClockInspector.ActiveRootClocks(Dispatcher.CurrentDispatcher), c => ReferenceEquals(c.Clock, clock));
    }

    [StaFact]
    public void Hiding_the_window_removes_the_beads_clock_from_the_timing_tree()
    {
        var view = RealizeGraph(GraphStatus.Building, out var window);
        var clock = view.BeadsClock;
        Assert.NotNull(clock); // ön-koşul: building düğüm GERÇEKTEN dönüyor

        window.Hide();

        Assert.Null(view.BeadsClock); // non-vacuous: sahip saati GERÇEKTEN bıraktı…
        AssertRetired(clock);         // …ve saat ağaçta yetim kalmadı
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Hiding_the_window_removes_the_edge_flow_clock_from_the_timing_tree()
    {
        var view = RealizeGraph(GraphStatus.Queued, out var window);
        view.SelectedNode = "OSYS.Data";
        var clock = view.EdgeFlowClock;
        Assert.NotNull(clock); // ön-koşul: seçim kenarları GERÇEKTEN akıyor

        window.Hide();

        Assert.Null(view.EdgeFlowClock);
        AssertRetired(clock);
        GC.KeepAlive(window);
    }

    /// <summary>Ölçülen yol: koşu görünürken biter — son building düğüm sonuca oturur, spin-down penceresi dolar, saat bırakılır.</summary>
    [StaFact]
    public void The_spindown_after_the_last_build_removes_the_beads_clock_from_the_timing_tree()
    {
        var view = RealizeGraph(GraphStatus.Building, out var window);
        var clock = view.BeadsClock;
        Assert.NotNull(clock);

        view.UpdateStatuses(GraphNodes(GraphStatus.Succeeded)); // derleme bitti: yörünge söner, spin-down kurulur
        view.HandleBeadsSpindownTick();                         // pencere doldu, boş grafta saat bırakılır

        Assert.Null(view.BeadsClock);
        AssertRetired(clock);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Clearing_the_selection_removes_the_edge_flow_clock_from_the_timing_tree()
    {
        var view = RealizeGraph(GraphStatus.Queued, out var window);
        view.SelectedNode = "OSYS.Data";
        var clock = view.EdgeFlowClock;
        Assert.NotNull(clock);

        view.SelectedNode = null; // kenarlar sökülür, akış saati bırakılır

        Assert.Null(view.EdgeFlowClock);
        AssertRetired(clock);
        GC.KeepAlive(window);
    }
}
