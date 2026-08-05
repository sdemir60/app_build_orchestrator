using System.Windows;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [sinema] Büyük grafta (düğüm sayısı > FullDetailMaxNodes — cull/LOD ile AYNI kapı) devreye giren
/// sinema modunun WPF kablajı: kenar sisi, follow-zoom kamera ve zoom'a duyarlı etiketler.
/// Küçük grafta HER ŞEYİN birebir bugünkü gibi kaldığı da burada pinlenir (spec §3.0).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphCinemaTests
{
    private static readonly Size Panel = new(600, 400);

    /// <summary>Sinema bandında deterministik graf: 4 katman, katman başına eşit dağıtım, hepsi Discovered.
    /// Adlar kısa tutulur (etiket senaryoları Task 5'te kendi adlarını üretir).</summary>
    internal static IReadOnlyList<GraphNode> BigNodes(int count = GraphView.FullDetailMaxNodes + 6) =>
        [.. Enumerable.Range(0, count).Select(i => new GraphNode($"N{i}", i % 4, GraphStatus.Discovered))];

    /// <summary>Her düğümü bir üst katmandaki komşusuna bağlayan basit kenar kümesi.</summary>
    internal static IReadOnlyList<GraphEdge> ChainEdges(IReadOnlyList<GraphNode> nodes) =>
        [.. nodes.Where(n => n.Layer > 0)
            .Select(n => new GraphEdge(
                nodes.First(m => m.Layer == n.Layer - 1).Name, n.Name))];

    private static GraphView NewView() => GraphTestView.Realized(Panel, labelFontFamily: DsResources.MonoFontFamily);

    // ---------------------------------------------------------------- kenar sisi kablajı

    [StaFact]
    public void A_large_graph_fogs_its_idle_edges_to_the_dim_level()
    {
        var nodes = BigNodes();
        var view = NewView();

        view.SetGraph(nodes, ChainEdges(nodes));

        Assert.True(view.IsCullEnabled); // sinema kapısı = cull kapısı
        var idle = view.EdgeVisuals.First();
        Assert.Equal(EdgeStyleResolver.DimmedOpacity, idle.Path.Opacity);
    }

    [StaFact]
    public void A_small_graph_keeps_todays_full_opacity_edges()
    {
        var nodes = BigNodes(GraphView.FullDetailMaxNodes); // tam sınırda: sinema KAPALI
        var view = NewView();

        view.SetGraph(nodes, ChainEdges(nodes));

        Assert.False(view.IsCullEnabled);
        Assert.Equal(0.8, view.EdgeVisuals.First().Path.Opacity);
    }
}
