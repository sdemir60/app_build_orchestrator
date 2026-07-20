using System.Windows;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T63] <see cref="GraphCamera"/> — design-v1 §2.3 kamera aritmetiğinin SAF portu (prototype/app/BuildApp.jsx
/// <c>GraphPanel</c>): ölçek panele sığar + 0.68–1.08 kıstırma, hedef = seçili düğüm / building frontier ağırlık
/// merkezi / done-stopped'ta merkez, varsayılan merkez y=H×0.3, 12px pan payı, tx/ty JS <c>Math.round</c> paritesi
/// (Ek A #10) ve frontier için <b>&lt;8px sapmada retarget etme</b> eşiği (feasibility §3.4 eklemesi).
/// </summary>
public class GraphCameraTests
{
    // GraphLayout'un 6 katmanlı (L0..L5) OSYS örneğiyle BİREBİR ölçü: 880 × (46 + 5×96 + 58) = 880×584.
    private static readonly Size Graph = new(GraphLayout.CanvasWidth, 584);

    // ---------------------------------------------------------------- ölçek

    [Fact]
    public void Scale_fits_the_graph_into_the_panel_with_the_30px_padding()
    {
        // Kıstırma bandının İÇİNDE kalan bir viewport: dar taraf genişliktir (910/910 = 1.0).
        var viewport = new Size(910, 1200);

        double scale = GraphCamera.FitScale(viewport, Graph);

        Assert.Equal(Math.Min(910 / (Graph.Width + 30.0), 1200 / (Graph.Height + 30.0)), scale, 10);
        Assert.InRange(scale, GraphCamera.MinScale, GraphCamera.MaxScale);
    }

    [Fact]
    public void Scale_is_clamped_to_the_0_68_floor_on_a_small_panel()
        => Assert.Equal(GraphCamera.MinScale, GraphCamera.FitScale(new Size(300, 200), Graph));

    [Fact]
    public void Scale_is_clamped_to_the_1_08_ceiling_on_a_huge_panel()
        => Assert.Equal(GraphCamera.MaxScale, GraphCamera.FitScale(new Size(4000, 4000), Graph));

    // ---------------------------------------------------------------- hedef (odak) seçimi

    [Fact]
    public void Focus_is_the_selected_node_whenever_there_is_a_selection()
    {
        var focus = GraphCamera.ResolveFocus(
            selected: new Point(120, 430), building: [new Point(700, 900)], settled: false, Graph, previousFocus: null);

        Assert.Equal(new Point(120, 430), focus);
    }

    [Fact]
    public void Focus_is_the_center_of_gravity_of_the_building_frontier_when_nothing_is_selected()
    {
        var focus = GraphCamera.ResolveFocus(
            selected: null, building: [new Point(100, 200), new Point(300, 400)], settled: false, Graph, previousFocus: null);

        Assert.Equal(new Point(200, 300), focus);
    }

    [Fact]
    public void Focus_defaults_to_the_horizontal_center_at_y_equals_H_times_0_3_when_idle()
    {
        var focus = GraphCamera.ResolveFocus(null, [], settled: false, Graph, previousFocus: null);

        Assert.Equal(new Point(Graph.Width / 2, Graph.Height * 0.3), focus);
        Assert.Equal(0.3, GraphCamera.DefaultCenterYFactor);
    }

    [Fact]
    public void Focus_is_the_true_center_once_the_run_is_done_or_stopped()
    {
        var focus = GraphCamera.ResolveFocus(null, [], settled: true, Graph, previousFocus: null);

        Assert.Equal(new Point(Graph.Width / 2, Graph.Height / 2), focus);
    }

    // ---------------------------------------------------------------- frontier küçük-sapma eşiği

    [Fact]
    public void A_frontier_that_moved_less_than_8px_does_not_retarget_the_camera()
    {
        var previous = new Point(200, 300);
        // Yeni ağırlık merkezi 5px sağa kaydı — eşiğin altında, kamera OLDUĞU yerde kalır (feasibility §3.4).
        var focus = GraphCamera.ResolveFocus(null, [new Point(205, 300)], settled: false, Graph, previous);

        Assert.Equal(previous, focus);
        Assert.False(GraphCamera.ShouldRetarget(previous, new Point(205, 300)));
    }

    [Fact]
    public void A_frontier_that_moved_at_least_8px_retargets_the_camera()
    {
        var previous = new Point(200, 300);
        var next = new Point(200, 312);

        Assert.True(GraphCamera.ShouldRetarget(previous, next));
        Assert.Equal(next, GraphCamera.ResolveFocus(null, [next], settled: false, Graph, previous));
    }

    [Fact]
    public void The_small_deviation_threshold_is_only_about_the_frontier_a_selection_always_retargets()
    {
        var previous = new Point(200, 300);

        // Seçili düğüm 1px bile kaysa kamera onu hedefler — eşik yalnız frontier ağırlık merkezine aittir.
        Assert.Equal(new Point(201, 300),
            GraphCamera.ResolveFocus(new Point(201, 300), [], settled: false, Graph, previous));
    }

    // ---------------------------------------------------------------- transform (tx/ty + pan payı + yuvarlama)

    [Fact]
    public void The_graph_is_centered_on_both_axes_when_it_fits_entirely_inside_the_panel()
    {
        var viewport = new Size(1400, 1400);
        var transform = GraphCamera.Compute(viewport, Graph, new Point(10, 10));

        double s = GraphCamera.FitScale(viewport, Graph);
        Assert.Equal(Math.Floor((1400 - Graph.Width * s) / 2 + 0.5), transform.Tx);
        Assert.Equal(Math.Floor((1400 - Graph.Height * s) / 2 + 0.5), transform.Ty);
    }

    [Fact]
    public void Panning_keeps_a_12px_margin_at_the_leading_edge()
    {
        var viewport = new Size(500, 300); // graf her iki eksende de viewport'tan büyük
        // Sol üst köşedeki bir düğüme odaklanınca ham tx/ty pozitif olur → 12px payla sınırlanır.
        var transform = GraphCamera.Compute(viewport, Graph, new Point(0, 0));

        Assert.Equal(GraphCamera.PanMarginPx, transform.Tx);
        Assert.Equal(GraphCamera.PanMarginPx, transform.Ty);
    }

    [Fact]
    public void Panning_keeps_a_12px_margin_at_the_trailing_edge()
    {
        var viewport = new Size(500, 300);
        var transform = GraphCamera.Compute(viewport, Graph, new Point(Graph.Width, Graph.Height));

        double s = GraphCamera.FitScale(viewport, Graph);
        Assert.Equal(Math.Floor(500 - Graph.Width * s - GraphCamera.PanMarginPx + 0.5), transform.Tx);
        Assert.Equal(Math.Floor(300 - Graph.Height * s - GraphCamera.PanMarginPx + 0.5), transform.Ty);
    }

    [Fact]
    public void Tx_and_ty_are_rounded_to_whole_pixels_js_math_round_parity()
    {
        var transform = GraphCamera.Compute(new Size(501, 401), Graph, new Point(311, 507));

        Assert.Equal(Math.Truncate(transform.Tx), transform.Tx);
        Assert.Equal(Math.Truncate(transform.Ty), transform.Ty);
        // JS Math.round: .5 HER ZAMAN yukarı (banker's rounding DEĞİL).
        Assert.Equal(3.0, GraphCamera.RoundPixels(2.5));
        Assert.Equal(-2.0, GraphCamera.RoundPixels(-2.5));
    }
}
