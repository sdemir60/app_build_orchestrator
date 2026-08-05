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

    // ---------------------------------------------------------------- follow-zoom kablajı

    /// <summary>Tek düğümün statüsünü değiştirir — GraphPanZoomTests de kullanır (fixture tek yerde).</summary>
    internal static IReadOnlyList<GraphNode> WithStatus(
        IReadOnlyList<GraphNode> nodes, string name, GraphStatus status) =>
        [.. nodes.Select(n => n.Name == name ? n with { Status = status } : n)];

    [StaFact]
    public void A_building_frontier_zooms_the_camera_into_the_follow_band()
    {
        var nodes = BigNodes();
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));

        view.UpdateStatuses(WithStatus(nodes, "N0", GraphStatus.Building));

        // Tek düğümlük frontier tavana çerçevelenir (saf tarafı Task 3 pinledi; burada KABLAJ pinlenir).
        Assert.Equal(GraphCamera.FollowMaxScale, view.CurrentCamera.Scale);
    }

    [StaFact]
    public void Settled_returns_the_camera_to_the_overview_fit()
    {
        var nodes = BigNodes();
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));
        view.UpdateStatuses(WithStatus(nodes, "N0", GraphStatus.Building));

        view.UpdateStatuses(nodes); // frontier bitti
        // Ölçek TAM BURADA kuşbakışına döner (takip ölçeği view'da yapışıp kalmaz) — settled'dan ÖNCE ölçülür,
        // aksi halde bu iddia hiçbir adımda test edilmiş olmazdı.
        Assert.Equal(GraphCamera.FitScale(view.ViewportSize, view.GraphSize), view.CurrentCamera.Scale);

        view.IsSettled = true;

        Assert.Equal(GraphCamera.FitScale(view.ViewportSize, view.GraphSize), view.CurrentCamera.Scale);
    }

    [StaFact]
    public void A_small_graph_never_changes_scale_when_building_todays_behavior_pinned()
    {
        var nodes = BigNodes(GraphView.FullDetailMaxNodes);
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));
        double before = view.CurrentCamera.Scale;
        // "Sabit" yetmez, DOĞRU değerde sabit olmalı: küçük grafın kuşbakışı fit'e oturduğu da pinlenir.
        Assert.Equal(GraphCamera.FitScale(view.ViewportSize, view.GraphSize), before);

        view.UpdateStatuses(WithStatus(nodes, "N0", GraphStatus.Building));

        Assert.Equal(before, view.CurrentCamera.Scale); // sinema dışı: ölçek fit'te sabit
    }

    [StaFact]
    public void A_selection_zooms_to_the_selection_scale_in_cinema()
    {
        var nodes = BigNodes();
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));

        view.SelectedNode = "N3";

        Assert.Equal(GraphCamera.SelectionScale, view.CurrentCamera.Scale);
    }

    [StaFact]
    public void Only_a_FRONTIER_scale_is_remembered_so_it_cannot_suppress_the_next_frontier_retarget()
    {
        // [sinema] 0.05'lik "yeniden ölçekleme" eşiği YALNIZ frontier dalında uygulanır
        // (GraphCamera.ResolveScale) — GraphRenderTests.Only_a_FRONTIER_focus_is_remembered... testinin ölçek
        // eşi. Seçimden sızacak 1.1, ilk frontier hedefi [1.05, 1.15]'e düşerse onu eşiğin altında kalarak
        // BASTIRIR ve kamera cepheye hiç yönelmez; bu yüzden yalnız frontier ölçeği saklanır.
        var nodes = BigNodes();
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));
        Assert.Null(view.PreviousScale); // seçim yok + frontier yok → kuşbakışı fit, HATIRLANMAZ

        view.UpdateStatuses(WithStatus(nodes, "N0", GraphStatus.Building));
        Assert.Equal(GraphCamera.FollowMaxScale, view.PreviousScale); // frontier → hatırlanır

        view.SelectedNode = "N3";
        Assert.Null(view.PreviousScale); // seçim dalı → HATIRLANMAZ

        view.SelectedNode = null;
        Assert.Equal(GraphCamera.FollowMaxScale, view.PreviousScale); // frontier yeniden hedeflenir

        view.UpdateStatuses(nodes); // frontier boşaldı → kuşbakışı fit
        Assert.Null(view.PreviousScale);
    }

    [StaFact]
    public void A_small_graph_never_latches_a_follow_scale_the_cinema_gate_closes_the_latch_too()
    {
        var nodes = BigNodes(GraphView.FullDetailMaxNodes); // tam sınırda: sinema KAPALI
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));

        view.UpdateStatuses(WithStatus(nodes, "N0", GraphStatus.Building));

        // Sinema dışında ölçek zaten hep fit'tir; latch'in de HİÇ kurulmaması, bayat bir fit değerinin
        // graf sinema bandına büyüdüğünde ilk frontier hedefini bastıramamasını garanti eder.
        Assert.Null(view.PreviousScale);
    }
}
