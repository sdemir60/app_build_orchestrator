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

    // ---------------------------------------------------------------- [sinema] follow-zoom ölçeği

    [Fact]
    public void Outside_cinema_the_scale_is_always_the_fit_scale()
    {
        var viewport = new Size(600, 400);
        double scale = GraphCamera.ResolveScale(
            viewport, Graph, cinema: false,
            selected: new Point(100, 100), building: [new Point(1, 1)], settled: false, previousScale: null);

        // Sinema dışında seçim de frontier de ölçeği DEĞİŞTİRMEZ — bugünkü davranış birebir (spec §3.0).
        Assert.Equal(GraphCamera.FitScale(viewport, Graph), scale);
    }

    [Fact]
    public void A_selection_beats_the_frontier_and_ignores_the_zeno_guard_in_cinema()
        // GERÇEK senaryo: koşu sürerken listeden bir proje tıklanır. Seçim frontier'in ÖNÜNDE gelmeli —
        // bu building tek başına tabanı (0.85) verirdi, seçim 1.1'i dayatır. previousScale ise seçim
        // ölçeğine eşiğin ALTINDA (|1.1−1.13| = 0.03 < 0.05) tutulur: Zeno guard'ı yanlışlıkla bu dala
        // yayılsaydı 1.13 dönerdi. Böylece "dal sırası" ve "guard yalnız frontier'in" iddiaları ölçülür.
        => Assert.Equal(GraphCamera.SelectionScale, GraphCamera.ResolveScale(
            new Size(600, 400), Graph, cinema: true,
            selected: new Point(100, 100), building: [new Point(100, 200), new Point(1700, 200)],
            settled: false, previousScale: 1.13));

    [Fact]
    public void A_single_building_node_frames_at_the_follow_ceiling()
    {
        // Tek düğümlük frontier: bbox = 2×kenar payı ⇒ ölçek tavana kelepçelenir (26px kare ~36px görünür).
        double scale = GraphCamera.FrontierScale(new Size(600, 400), [new Point(440, 200)]);

        Assert.Equal(GraphCamera.FollowMaxScale, scale);
        Assert.Equal(1.4, GraphCamera.FollowMaxScale);
    }

    [Fact]
    public void A_wide_frontier_clamps_at_the_follow_floor()
    {
        // 1600px'e yayılmış cephe 600'lük panele 0.85'in altında sığardı — tabana kelepçelenir.
        double scale = GraphCamera.FrontierScale(new Size(600, 400), [new Point(100, 200), new Point(1700, 200)]);

        Assert.Equal(GraphCamera.FollowMinScale, scale);
        Assert.Equal(0.85, GraphCamera.FollowMinScale);
    }

    [Fact]
    public void The_frontier_margins_are_derived_from_the_layout_cell_and_the_fit_padding()
    {
        // BAĞIMSIZ oracle: beklenen değerler sabitin kendisinden DEĞİL, GraphLayout ölçülerinden elle
        // yeniden hesaplanır — aksi hâlde iddia kendine referans verir ve her değeri kabul eder.
        //   X = NodeCellWidth/2 + FitPadding = (26×3.4)/2 + 30 = 44.2 + 30 = 74.2
        //   Y = NodeSize/2 + LabelGap + LabelHeight + FitPadding = 13 + 5 + 14 + 30 = 62
        // X'te precision kullanılır: 26.0×3.4 double'da 88.39999999999999 çıkar, yani sabit 74.2'nin
        // bit-birebir eşi değil (fark 1.4e-14). Yanlış bir GraphLayout terimi bu farktan MİLYON kat
        // büyük sapma yaratacağı için pin niteliği bozulmaz.
        Assert.Equal(74.2, GraphCamera.FrontierMarginX, 10);
        Assert.Equal(62.0, GraphCamera.FrontierMarginY);
    }

    [Fact]
    public void The_frontier_frame_includes_the_cell_margins_and_fit_padding()
    {
        // Dikey eksen kısıt olsun: iki düğüm alt alta, panel alçak.
        // h = 200 (bbox) + 2×62 = 324 — elle hesap; sabitten türetilmez ki MarginY nicel olarak kilitlensin.
        var viewport = new Size(2000, 300);
        var building = new List<Point> { new(400, 100), new(400, 300) };

        Assert.Equal(300.0 / 324.0, GraphCamera.FrontierScale(viewport, building), 10);
    }

    [Fact]
    public void The_frontier_frame_applies_the_horizontal_margin_on_a_narrow_panel()
    {
        // Yatay eksen kısıt olsun: iki düğüm yan yana, panel dar ve çok yüksek.
        // w = 200 (bbox) + 2×74.2 = 348.4 → 300/348.4 ≈ 0.8611, bandın İÇİNDE (kelepçe devrede değil),
        // dolayısıyla sonuç MarginX'e nicel olarak bağımlı: pay 0 olsaydı 300/200 = 1.5 → tavana (1.4)
        // kelepçelenirdi. Beklenti yine elle hesaptır.
        var scale = GraphCamera.FrontierScale(new Size(300, 2000), [new Point(0, 0), new Point(200, 0)]);

        Assert.Equal(300.0 / 348.4, scale, 10);
        Assert.InRange(scale, GraphCamera.FollowMinScale, GraphCamera.FollowMaxScale);
    }

    [Fact]
    public void Settled_or_idle_cinema_returns_to_the_overview_fit_scale()
    {
        var viewport = new Size(600, 400);
        // previousScale fit'e (0.68) eşiğin ALTINDA (|0.68−0.70| = 0.02 < 0.05): Zeno guard'ı bu dala da
        // uygulansaydı 0.70'te takılı kalırdık. Guard YALNIZ frontier dalının — burada hiç okunmaz.
        Assert.Equal(GraphCamera.FitScale(viewport, Graph), GraphCamera.ResolveScale(
            viewport, Graph, cinema: true, selected: null, building: [], settled: true, previousScale: 0.70));
        Assert.Equal(GraphCamera.FitScale(viewport, Graph), GraphCamera.ResolveScale(
            viewport, Graph, cinema: true, selected: null, building: [], settled: false, previousScale: null));
    }

    [Fact]
    public void A_scale_change_below_the_threshold_keeps_the_previous_scale_zeno_guard()
    {
        var viewport = new Size(600, 400);
        var building = new List<Point> { new(100, 200), new(1700, 200) }; // taban: 0.85
        // Önceki ölçek hedefe 0.05'ten yakın → ESKİ ölçek korunur (kamera mikro-zoom'la titremez).
        Assert.Equal(0.86, GraphCamera.ResolveScale(
            viewport, Graph, cinema: true, selected: null, building: building, settled: false, previousScale: 0.86));
        // Eşik ve üstü → yeni hedef kazanır.
        Assert.Equal(0.85, GraphCamera.ResolveScale(
            viewport, Graph, cinema: true, selected: null, building: building, settled: false, previousScale: 0.90));
        Assert.False(GraphCamera.ShouldRescale(1.00, 1.04));
        Assert.True(GraphCamera.ShouldRescale(1.00, 1.05));
    }

    [Fact]
    public void The_cinema_scale_values_are_pinned_to_their_spec_numbers()
    {
        // Bu sabitlerin SAHİBİ GraphCamera, dolayısıyla değerleri burada kilitlenir. Tüketici testlerin
        // (Task 6: wheel'in bu kademeyle çarpması, takibe dönüş gecikmesi) iddiası DAVRANIŞTIR, değer değil —
        // ikisi farklı sorular olduğu için bu pin bir kopya değildir.
        Assert.Equal(1.1, GraphCamera.SelectionScale);
        // Manuel bant otomatik bandı KAPSAR: kullanıcı istediğinde tüm silueti görebilsin, istediğinde
        // takibin gittiğinden daha yakına inebilsin.
        Assert.Equal(0.45, GraphCamera.ManualMinScale);
        Assert.Equal(2.0, GraphCamera.ManualMaxScale);
        Assert.True(GraphCamera.ManualMinScale < GraphCamera.FollowMinScale);
        Assert.True(GraphCamera.ManualMaxScale > GraphCamera.FollowMaxScale);
        Assert.Equal(1.1, GraphCamera.WheelZoomStep);
        Assert.Equal(4000.0, GraphCamera.FollowResumeDelayMs);
    }

    [Fact]
    public void Compute_with_an_explicit_scale_centers_the_focus_and_the_3_arg_overload_stays_fit()
    {
        var viewport = new Size(500, 300);
        var t = GraphCamera.Compute(viewport, Graph, new Point(440, 292), 1.4);

        Assert.Equal(1.4, t.Scale);
        // Odak panel merkezinde: tx = vw/2 − fx·s (pan payı sınırları içinde).
        Assert.Equal(Math.Floor(250 - 440 * 1.4 + 0.5), t.Tx);
        // 3-arg overload = FitScale (mevcut testlerin tamamı bunun üstünden yeşil kalır).
        Assert.Equal(GraphCamera.Compute(viewport, Graph, new Point(440, 292),
            GraphCamera.FitScale(viewport, Graph)), GraphCamera.Compute(viewport, Graph, new Point(440, 292)));
    }

    // ---------------------------------------------------------------- [sinema] manuel jest aritmetiği

    /// <summary>Manuel jest testlerinin grafı: her iki eksende de 600×400 panelden BÜYÜK, dolayısıyla kelepçe
    /// "sığmayan eksen" dalını kullanır (sığan eksenin dalı ayrı bir testin konusudur).</summary>
    private static readonly Size BigGraph = new(2000, 1000);
    private static readonly Size Panel = new(600, 400);

    [Fact]
    public void Zooming_at_the_cursor_keeps_the_world_point_under_it_fixed()
    {
        var camera = new CameraTransform(1.0, -500, -300);
        // İmleç panel MERKEZİNİN dışındadır ve bu ŞARTTIR: merkeze konsaydı "imleç merkezli" ile "panel
        // merkezli" zoom AYNI sonucu verir ve iddia hiçbir şey ölçmezdi (ölçüldü — merkeze sabitleyen mutant
        // 300,200 imleciyle TÜM süiti yeşil bırakıyordu).
        var cursor = new Point(450, 120); // dünya: (450−(−500))/1 = 950, (120−(−300))/1 = 420

        var zoomed = GraphCamera.ZoomAt(camera, cursor, GraphCamera.WheelZoomStep, Panel, BigGraph);

        Assert.Equal(camera.Scale * GraphCamera.WheelZoomStep, zoomed.Scale, 10);
        // İmlecin ALTINDAKİ dünya noktası sabit kalır; tx/ty piksele yuvarlandığı için ±0.5px band verilir.
        Assert.InRange((cursor.X - zoomed.Tx) / zoomed.Scale, 950 - 0.5, 950 + 0.5);
        Assert.InRange((cursor.Y - zoomed.Ty) / zoomed.Scale, 420 - 0.5, 420 + 0.5);
    }

    [Fact]
    public void Manual_zoom_is_clamped_to_the_manual_band()
    {
        // Bandın DEĞERLERİ The_cinema_scale_values_are_pinned_to_their_spec_numbers'ta kilitlidir; burada
        // ölçülen DAVRANIŞ: tavandaki bir kamera yakınlaşamaz, tabandaki bir kamera uzaklaşamaz.
        Assert.Equal(GraphCamera.ManualMaxScale, GraphCamera.ZoomAt(
            new CameraTransform(GraphCamera.ManualMaxScale, -500, -300),
            new Point(300, 200), GraphCamera.WheelZoomStep, Panel, BigGraph).Scale);
        Assert.Equal(GraphCamera.ManualMinScale, GraphCamera.ZoomAt(
            new CameraTransform(GraphCamera.ManualMinScale, -10, -10),
            new Point(300, 200), 1 / GraphCamera.WheelZoomStep, Panel, BigGraph).Scale);
    }

    [Fact]
    public void Panning_moves_the_camera_and_stays_inside_the_12px_margins()
    {
        var camera = new CameraTransform(1.0, -500, -300);

        var panned = GraphCamera.Pan(camera, new Vector(40, -25), Panel, BigGraph);
        Assert.Equal(-460, panned.Tx);
        Assert.Equal(-325, panned.Ty);
        Assert.Equal(camera.Scale, panned.Scale); // pan ölçeğe DOKUNMAZ

        // Kelepçe: dev bir delta 12px kenar payında durur (ClampPan tek kaynak — Compute ile AYNI sınır).
        var clamped = GraphCamera.Pan(camera, new Vector(100_000, 100_000), Panel, BigGraph);
        Assert.Equal(GraphCamera.PanMarginPx, clamped.Tx);
        Assert.Equal(GraphCamera.PanMarginPx, clamped.Ty);
    }

    [Fact]
    public void Zooming_out_below_the_fit_band_centers_the_axis_that_now_fits()
    {
        // [Task 3'ten devreden kapsam] ClampPan'in "eksen sığıyor → ortala" dalı bugüne dek YALNIZ fit
        // ölçeğinde (Compute üzerinden) sınanmıştı. Manuel bant fit bandının ALTINA iner (0.45 < 0.68) ve
        // aşağıdaki graf ancak orada panele sığar ⇒ dal fit DIŞI bir ölçekte de doğru çalışmalıdır.
        var graph = new Size(2000, 800);
        Assert.True(graph.Height * GraphCamera.FitScale(Panel, graph) > Panel.Height,
            "fit ölçeğinde de sığıyor — 'yalnız manuel bantta ulaşılan dal' iddiası ölçülemez");

        var panned = GraphCamera.Pan(
            new CameraTransform(GraphCamera.ManualMinScale, -300, -50),
            new Vector(100_000, 100_000), Panel, graph);

        // Dikey eksen SIĞAR ⇒ 12px payı değil, ORTALAMA: (400 − 800×0.45)/2 = 20 (elle hesap).
        Assert.Equal(20.0, panned.Ty);
        Assert.Equal(GraphCamera.PanMarginPx, panned.Tx); // sığmayan eksen payında durur
    }
}
