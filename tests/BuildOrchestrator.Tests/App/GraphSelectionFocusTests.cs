using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// design v1.3.0 §2.3 "Seçim — odakla & sığdır" (+ §3.3). Prototip: BuildApp.jsx satır 344-356 (sığdırma),
/// 386-396 + 413 (kenarlar), 476-485 (ad etiketi).
///
/// <para><b>Eski iddialar (<c>EdgeStyleResolverTests</c> / <c>GraphCinemaTests</c>, artık geçersiz):</b>
/// kenar ağı KALICIYDI ve koşunun hikâyesini taşıyordu — varsayılan tel çizgi, building hedefe amber akan
/// kesik, biten dala yeşil/kırmızı, hatayı taşıyan dala statik kırmızı kesik, koşuya karışmayana sis.
/// v1.3.0 §2.3 ağı tamamen kaldırdı: kenarlar YALNIZ seçimde ve TEK bir görünümle çizilir.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphSelectionFocusTests
{
    /// <summary>Base → Data → Api zinciri + bağlantısız Other.</summary>
    private static IReadOnlyList<GraphNode> Nodes() =>
    [
        new("OSYS.Base", 0, GraphStatus.Succeeded),
        new("OSYS.Other", 0, GraphStatus.Queued),
        new("OSYS.Data", 1, GraphStatus.Building),
        new("OSYS.Api", 2, GraphStatus.Queued),
    ];

    private static IReadOnlyList<GraphEdge> Edges() =>
        [new("OSYS.Base", "OSYS.Data"), new("OSYS.Data", "OSYS.Api")];

    private static GraphView Wired(bool animations = false)
    {
        var view = GraphTestView.Realized(new Size(600, 400), () => animations);
        view.SetGraph(Nodes(), Edges());
        return view;
    }

    /// <summary>Seçim yokken graf ÇİZGİSİZDİR (§2.3 "Kaldırılanlar": kalıcı bağımlılık çizgi ağı).</summary>
    [StaFact]
    public void With_no_selection_the_graph_carries_no_edges_at_all()
    {
        var view = Wired();

        Assert.Empty(view.SelectionEdgePaths);
        Assert.Null(view.EdgeFlowClock);
    }

    /// <summary>Seçim, seçili düğüme değen çizgileri kurar — BAŞKA hiçbirini.</summary>
    [StaFact]
    public void Selecting_a_node_draws_edges_to_its_deps_and_dependents_and_NOTHING_else()
    {
        var view = Wired();

        view.SelectedNode = "OSYS.Data"; // 1 dep (Base) + 1 dependent (Api)

        Assert.Equal(2, view.SelectionEdgePaths.Count);
        Assert.All(view.SelectionEdgePaths, p =>
        {
            Assert.Equal(SelectionEdgeStyle.Thickness, p.StrokeThickness, 6);
            Assert.Equal(SelectionEdgeStyle.Opacity, p.Opacity, 6);
            Assert.Same(SelectionEdgeStyle.DashArray, p.StrokeDashArray);
            Assert.Same(view.FindResource(SelectionEdgeStyle.BrushKey), p.Stroke);
        });

        // Bağlantısız bir düğüm seçilirse hiç çizgi doğmaz.
        view.SelectedNode = "OSYS.Other";
        Assert.Empty(view.SelectionEdgePaths);
    }

    [StaFact]
    public void Clearing_the_selection_tears_the_edges_down_again()
    {
        var view = Wired();
        view.SelectedNode = "OSYS.Data";
        Assert.NotEmpty(view.SelectionEdgePaths);

        view.SelectedNode = null;

        Assert.Empty(view.SelectionEdgePaths);
        Assert.Null(view.EdgeFlowClock);
    }

    /// <summary>Akan kesikler TEK paylaşımlı saate bağlanır (beads ile aynı gerekçe) ve reduced-motion'da
    /// hiç doğmaz (§2.3: "prefers-reduced-motion: beads ve akan çizgiler tamamen kapalı").</summary>
    [StaFact]
    public void The_flowing_dashes_share_one_clock_and_never_start_under_reduced_motion()
    {
        var moving = Wired(animations: true);
        moving.SelectedNode = "OSYS.Data";
        var clock = moving.EdgeFlowClock;
        Assert.NotNull(clock);
        var flow = Assert.IsType<DoubleAnimation>(clock.Timeline);
        Assert.Equal(0.0, flow.From);
        Assert.Equal(SelectionEdgeStyle.DashOffsetTarget, flow.To!.Value, 9);
        Assert.Equal(TimeSpan.FromMilliseconds(SelectionEdgeStyle.FlowDurationMs), flow.Duration.TimeSpan);
        Assert.Equal(RepeatBehavior.Forever, flow.RepeatBehavior);

        var still = Wired(animations: false);
        still.SelectedNode = "OSYS.Data";
        Assert.NotEmpty(still.SelectionEdgePaths); // çizgiler VAR…
        Assert.Null(still.EdgeFlowClock);          // …ama akmıyorlar
    }

    /// <summary>WPF'te dash birimi kalınlık çarpanıdır: MUTLAK desen (4/8 px) ve mutlak yol (24 px) 1.2'ye
    /// BÖLÜNÜR ⇒ ekranda tasarımın verdiği ölçüler görünür.</summary>
    [Fact]
    public void The_absolute_4_by_8_dash_survives_the_1_2px_thickness_because_the_values_are_divided()
    {
        Assert.Equal(4.0 / SelectionEdgeStyle.Thickness, SelectionEdgeStyle.DashArray[0], 9);
        Assert.Equal(8.0 / SelectionEdgeStyle.Thickness, SelectionEdgeStyle.DashArray[1], 9);
        Assert.Equal(-24.0 / SelectionEdgeStyle.Thickness, SelectionEdgeStyle.DashOffsetTarget, 9);
        Assert.True(SelectionEdgeStyle.DashArray.IsFrozen);
    }

    /// <summary>Eğri DİKEY kübik bezier'dir: kontrol noktaları iki ucun ORTA yüksekliğinde (JSX:391).</summary>
    [Fact]
    public void The_edge_is_a_vertical_cubic_bezier_with_control_points_at_the_mid_height()
    {
        var geometry = Assert.IsType<StreamGeometry>(
            SelectionEdgeStyle.Curve(new Point(10, 20), new Point(90, 120)));

        Assert.True(geometry.IsFrozen);
        // Kontrol noktaları uçların X'lerinde ve ORTA yükseklikte olduğu için eğri, uçların sınır
        // kutusunun DIŞINA çıkmaz — dikey bir S çizer. Kontrol noktaları başka yerde olsaydı (ör. sabit
        // ±54px, eski yerleşimin yaptığı gibi) kutu taşardı.
        Assert.Equal(new Rect(10, 20, 80, 100), geometry.Bounds, new RectRounding());
    }

    /// <summary>Kenarlar düğümlerin ALTINDA kalır — büyüyen bir düğüm çizgiyi örtebilmeli.</summary>
    [StaFact]
    public void Selection_edges_live_under_the_node_layer()
    {
        var view = Wired();
        view.SelectedNode = "OSYS.Data";

        var world = view.World;
        int edgeLayer = -1, nodeLayer = -1;
        for (int i = 0; i < world.Children.Count; i++)
        {
            if (world.Children[i] is System.Windows.Controls.Canvas c && c.Children.Contains(view.SelectionEdgePaths[0]))
                edgeLayer = i;
            if (world.Children[i] is System.Windows.Controls.Canvas n && n.Children.Contains(view.NodeVisuals["OSYS.Data"].Cell))
                nodeLayer = i;
        }
        Assert.True(edgeLayer >= 0 && nodeLayer >= 0);
        Assert.True(edgeLayer < nodeLayer, "kenar katmanı düğüm katmanının üstünde çiziliyor");
    }

    // ---------------------------------------------------------------- ad etiketi

    /// <summary>Seçili düğümün altında ad etiketi (§2.3) — EKRAN koordinatında, TEK öğe.</summary>
    [StaFact]
    public void The_selected_node_gets_a_clamped_name_label_below_it()
    {
        var view = Wired();
        Assert.Equal(Visibility.Collapsed, view.SelectionLabelVisibility);

        view.SelectedNode = "OSYS.Data";

        Assert.Equal(Visibility.Visible, view.SelectionLabelVisibility);
        Assert.Equal("OSYS.Data", view.SelectionLabelContent);
        // Düğümün ALTINDA: etiketin üst kenarı düğüm merkezinin ekran karşılığından büyüktür.
        var screen = GraphOverlay.Project(view.NodeCenter("OSYS.Data"), view.CurrentCamera);
        Assert.True(view.SelectionLabelTopLeft.Y > screen.Y);

        view.SelectedNode = null;
        Assert.Equal(Visibility.Collapsed, view.SelectionLabelVisibility);
    }

    /// <summary>[REALIZE TESTİ] Ad etiketi yeni bir XAML parçasıdır — gerçek pencerede token'larını
    /// (surface-overlay / amber-border / amber-text / radius-sm) çözerek realize olmalı.</summary>
    [StaFact]
    public void The_selection_label_realizes_in_a_real_window_with_its_tokens_resolved()
    {
        var host = DsResources.NewHost();
        var view = new GraphView { AnimationsEnabledProvider = () => false };
        var window = DsResources.Realize(host, view);
        view.SetGraph(Nodes(), Edges());
        view.UpdateLayout();

        view.SelectedNode = "OSYS.Data";
        view.UpdateLayout();

        Assert.Equal(Visibility.Visible, view.SelectionLabelVisibility);
        Assert.Equal("OSYS.Data", view.SelectionLabelContent);
        GC.KeepAlive(window);
    }

    /// <summary>Sınır kutusu karşılaştırmasında piksel gürültüsünü yutan karşılaştırıcı.</summary>
    private sealed class RectRounding : IEqualityComparer<Rect>
    {
        public bool Equals(Rect a, Rect b) =>
            Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01 &&
            Math.Abs(a.Width - b.Width) < 0.01 && Math.Abs(a.Height - b.Height) < 0.01;

        public int GetHashCode(Rect value) => value.GetHashCode();
    }
}
