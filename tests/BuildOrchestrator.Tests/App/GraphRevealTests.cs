using System.Windows;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// design v1.3.0 §2.3 "İlk açılış (Sync sonrası)": node'lar DERLEME SIRASIYLA belirir — gecikme =
/// build-order index × 9ms (max 520ms); dalga üstten alta, soldan sağa akar.
///
/// <para><b>Eski iddia:</b> gecikme KATMAN başınaydı (55ms/katman, tavan 330), yani bir bantta 40 düğüm
/// aynı anda beliriyordu ve dalga "soldan sağa" akmıyordu.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphRevealTests
{
    /// <summary>
    /// AYIRT EDİCİ: gecikme BESLEME (build-order) sırasından gelir — aynı bantta olmak onu eşitlemez.
    /// Katman başına bir gecikme A ve B'yi (ikisi de katman 0) aynı anda başlatır ve bu testi düşürür.
    /// </summary>
    [StaFact]
    public void The_wave_follows_build_order_so_two_nodes_in_the_same_band_do_not_start_together()
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph(
            [new("A", 0, GraphStatus.Discovered),
             new("B", 0, GraphStatus.Discovered),
             new("C", 1, GraphStatus.Discovered)],
            []);

        Assert.Equal(0.0, view.RevealDelayOf("A"));
        Assert.Equal(GraphView.RevealStepMs, view.RevealDelayOf("B"));
        Assert.Equal(2 * GraphView.RevealStepMs, view.RevealDelayOf("C"));
    }

    /// <summary>Tavan gerçekten uygulanır: 58. düğümden sonrası aynı anda belirir (§2.3 "max 520ms").</summary>
    [StaFact]
    public void Everything_past_the_cap_appears_together()
    {
        var (nodes, edges) = SyntheticGraph.Build(200, 6, 1.6);
        var view = GraphTestView.Realized(new Size(900, 520), () => true);
        view.SetGraph(nodes, edges);

        Assert.Equal(GraphView.RevealDelayCapMs, view.RevealDelayOf(nodes[100].Name));
        Assert.Equal(GraphView.RevealDelayCapMs, view.RevealDelayOf(nodes[199].Name));
        Assert.True(view.RevealDelayOf(nodes[10].Name) < GraphView.RevealDelayCapMs);
    }

    /// <summary>Beliriş GERÇEKTEN oynar: gecikme boyunca opaklık 0 tutulur ve düğüm 5px yukarıdan gelir.</summary>
    [StaFact]
    public void A_node_starts_transparent_and_five_pixels_above_its_place()
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph([new("A", 0, GraphStatus.Discovered)], []);

        var cell = view.NodeVisuals["A"].Cell;
        Assert.Equal(0.0, cell.Opacity, 6);
        var rise = Assert.IsType<TranslateTransform>(cell.RenderTransform);
        Assert.Equal(-GraphView.RevealRisePx, rise.Y, 6);
    }

    /// <summary>Reduced-motion'da dalga HİÇ oynamaz: düğümler ani ve tam opak yerleşir, transform temizdir
    /// ve hiçbir gecikme kaydedilmez.</summary>
    [StaFact]
    public void Reduced_motion_places_every_node_instantly_with_no_wave()
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => false);
        view.SetGraph([new("A", 0, GraphStatus.Discovered), new("B", 1, GraphStatus.Discovered)], []);

        Assert.All(view.NodeVisuals.Values, visual =>
        {
            Assert.Equal(1.0, visual.Cell.Opacity, 6);
            Assert.Same(Transform.Identity, visual.Cell.RenderTransform);
        });
        Assert.Null(view.RevealDelayOf("A"));
        Assert.Null(view.RevealDelayOf("B"));
    }
}
