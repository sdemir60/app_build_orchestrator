using System.Windows;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// design v1.3.0 §2.3 "İlk açılış (Sync sonrası)": node'lar DERLEME SIRASIYLA belirir — gecikme =
/// build-order index × 9ms, dalganın tamamı en çok 520ms (büyük grafta aralık daralır); dalga üstten alta, soldan
/// sağa akar.
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
            [new("A", "A", 0, GraphStatus.Discovered),
             new("B", "B", 0, GraphStatus.Discovered),
             new("C", "C", 1, GraphStatus.Discovered)],
            []);

        Assert.Equal(0.0, view.RevealDelayOf("A"));
        Assert.Equal(GraphView.RevealStepMs, view.RevealDelayOf("B"));
        Assert.Equal(2 * GraphView.RevealStepMs, view.RevealDelayOf("C"));
    }

    /// <summary>
    /// <b>Dalga grafın TAMAMINA yayılır, tavanda yığılmaz.</b> Tempo 9ms/düğümdür ama dalganın tamamı §2.3'ün
    /// 520ms'sini aşmaz: büyük grafta aralık daralır ve son düğüm tam tavanda başlar — hiçbir düğüm bir öncekiyle
    /// aynı anda başlamaz.
    ///
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı gözlemi]</b> Eski iddia (<c>Everything_past_the_cap_appears_together</c>):
    /// tavan DÜĞÜM başınaydı — gecikme <c>min(sıra × 9, 520)</c> — ve 58. düğümden sonrası aynı anda belirirdi.
    /// Gerçek çalışma alanında (184 proje) bu, düğümlerin 126'sı (%68) demekti: ilk bant (Types, 42 proje) akarak
    /// seriliyor, Business'ın ortasından sonrası ile Orchestration, UI ve Other bantları TEK karede "pat" diye
    /// geliyordu. Toplam süre ve 58 düğüme kadarki tempo aynı kaldı.</para>
    /// </summary>
    [StaFact]
    public void The_wave_spreads_over_the_whole_graph_instead_of_piling_up_at_the_cap()
    {
        var (nodes, edges) = SyntheticGraph.Build(200, 6, 1.6);
        var view = GraphTestView.Realized(new Size(900, 520), () => true);
        view.SetGraph(nodes, edges);

        var delays = nodes.Select(n => view.RevealDelayOf(n.Name)!.Value).ToList();
        Assert.Equal(0.0, delays[0]);
        Assert.Equal(GraphView.RevealDelayCapMs, delays[^1], 6); // dalga tavanda BİTER
        for (int i = 1; i < delays.Count; i++)
            Assert.True(delays[i] > delays[i - 1],
                $"{i}. düğüm {i - 1}. düğümle aynı anda başlıyor ({delays[i]:0.###} ms) — dalga yığılıyor");
    }

    /// <summary>Beliriş GERÇEKTEN oynar: gecikme boyunca opaklık 0 tutulur ve düğüm 5px yukarıdan gelir.</summary>
    [StaFact]
    public void A_node_starts_transparent_and_five_pixels_above_its_place()
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph([new("A", "A", 0, GraphStatus.Discovered)], []);

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
        view.SetGraph([new("A", "A", 0, GraphStatus.Discovered), new("B", "B", 1, GraphStatus.Discovered)], []);

        Assert.All(view.NodeVisuals.Values, visual =>
        {
            Assert.Equal(1.0, visual.Cell.Opacity, 6);
            Assert.Same(Transform.Identity, visual.Cell.RenderTransform);
        });
        Assert.Null(view.RevealDelayOf("A"));
        Assert.Null(view.RevealDelayOf("B"));
    }
}
