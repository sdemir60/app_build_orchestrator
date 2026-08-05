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

    // ---------------------------------------------------------------- zoom'a duyarlı etiketler

    /// <summary>Etiketleri STATİK kararla düşen ama takip tavanında (1.4×) sığan graf: 4 katman × 40 düğüm ⇒
    /// aralık TABANA oturur (<see cref="GraphLayout.MinNodeSpacing"/> = 34px) ve 7 karakterlik adlar Geist
    /// Mono 10px'te tam 42px çizilir (ÖLÇÜLDÜ — karakter başına 6px). Böylece 34 &lt; 42 ⇒ ölçek 1'de sığmaz;
    /// 34 × 1.4 = 47.6 ≥ 42 ⇒ takip tavanında sığar.
    ///
    /// <para>Ad genişliği <c>D3</c> ile SABİTLENİR: 1-2 haneli bir biçim, düğüm sayısı 100'ü geçtiğinde
    /// katmanın en geniş adını sessizce 6'dan 7 karaktere çıkarır ve eşiği fixture'ın büyüklüğüne bağlardı.</para>
    ///
    /// <para><paramref name="count"/> tam-detay bandına indirildiğinde AYNI şekil sinema kapısının ALTINDA
    /// kalır (aralık ve ad genişliği değişmez) — küçük graf güvencesi böylece "etiketleri düşürecek bir grafta"
    /// ölçülür, düşmeyeceği zaten belli olan bir grafta değil.</para></summary>
    private static IReadOnlyList<GraphNode> CrowdedNodes(int count = GraphView.FullDetailMaxNodes + 10) =>
        [.. Enumerable.Range(0, count)
            .Select(i => new GraphNode($"Node{i:D3}", i % 4, GraphStatus.Discovered))];

    /// <summary>Katman 0'ın ORTASINDAKİ düğüm (40'ın 19. sırası, i = 4×19). Kalabalık katmanın UÇLARI 600×400
    /// panelde kuşbakışı (0.68) pencerenin dışında kalır ve hiç materyalize olmaz — etiket iddiaları gerçekten
    /// kurulmuş bir görsele dayanmalıdır.</summary>
    private const string CrowdedTarget = "Node076";

    private static GraphNodeVisual MaterialisedTarget(GraphView view)
    {
        Assert.True(view.NodeVisuals.ContainsKey(CrowdedTarget),
            $"{CrowdedTarget} materyalize olmadı — etiket iddiası boşlukta kalırdı.");
        return view.NodeVisuals[CrowdedTarget];
    }

    [StaFact]
    public void Zooming_into_the_frontier_materialises_the_labels_that_fit_at_that_scale()
    {
        var nodes = CrowdedNodes();
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));

        var target = MaterialisedTarget(view);
        Assert.Null(target.Label); // statik LOD düşürdü (kalabalık katman, ölçek 1 varsayımı)

        view.UpdateStatuses(WithStatus(nodes, CrowdedTarget, GraphStatus.Building)); // kamera 1.4'e çerçeveler

        Assert.Equal(GraphCamera.FollowMaxScale, view.CurrentCamera.Scale); // ön-koşul: gerçekten yakınlaştı
        Assert.NotNull(target.Label);
        Assert.Equal(Visibility.Visible, target.Label!.Visibility);
        Assert.Equal(CrowdedTarget, target.Label.Text);
        Assert.Null(target.Body.ToolTip); // etiket görünürken tam-ad tooltip'i kalkar
    }

    [StaFact]
    public void Zooming_back_out_hides_the_labels_and_restores_the_tooltip()
    {
        var nodes = CrowdedNodes();
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));
        view.UpdateStatuses(WithStatus(nodes, CrowdedTarget, GraphStatus.Building));
        var target = MaterialisedTarget(view);
        Assert.NotNull(target.Label);

        view.UpdateStatuses(nodes);
        view.IsSettled = true; // kuşbakışına dönüş (0.68): r histerezis tabanının çok altında

        Assert.Equal(Visibility.Collapsed, target.Label!.Visibility);
        Assert.Equal(CrowdedTarget, target.Body.ToolTip);
    }

    [StaFact]
    public void Small_graph_labels_are_untouched_by_the_scale_machinery()
    {
        // Fixture KASTEN "düşürülebilir": aynı 34px aralık, aynı 42px adlar — yalnız düğüm sayısı tam-detay
        // bandının SINIRINDA. Ölçek makinesi burada koşsaydı kuşbakışı (0.68) oranı histerezis tabanının çok
        // altında kalır ve HER etiket Collapsed olurdu; "etiket null mı" diye bakmak bu kusuru GÖREMEZ (etiket
        // kurulmuştur, yalnız gizlenmiştir) — bu yüzden görünürlük de tooltip de ayrıca pinlenir.
        var nodes = CrowdedNodes(GraphView.FullDetailMaxNodes);
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));
        Assert.False(view.IsCullEnabled); // ön-koşul: tam-detay bandı
        Assert.False(GraphLayout.LabelsFit(GraphLayout.MinNodeSpacing, 42), "fixture kalabalık değil");

        view.UpdateStatuses(WithStatus(nodes, "Node000", GraphStatus.Building));

        Assert.Equal(nodes.Count, view.NodeVisuals.Count); // cull kapalı: hepsi kurulu
        Assert.All(view.NodeVisuals.Values, v => Assert.NotNull(v.Label)); // tam-detay garantisi
        Assert.All(view.NodeVisuals.Values, v => Assert.Equal(Visibility.Visible, v.Label!.Visibility));
        Assert.All(view.NodeVisuals.Values, v => Assert.Null(v.Body.ToolTip));
    }

    [StaFact]
    public void A_node_materialised_before_the_first_camera_target_keeps_the_static_label_decision()
    {
        // Panel HENÜZ ölçülmemişken (ViewportSize = 0) kamera hedefi HESAPLANMAZ — ApplyCamera erken döner ve
        // CurrentCamera.Scale 0'da kalır. Seçim yine de düğüm materyalize eder (MaterializeSelection ekran
        // dışından gelen seçimi kurar): o düğümün etiketi "ölçek 0" ile değerlendirilseydi HAKSIZ yere düşer,
        // üstelik hiçbir kamera geçişi bunu geri almazdı. Ölçek kararı ancak GERÇEK bir kamera hedefi varken
        // verilir; yoksa statik karar (BuildNodeVisual) geçerli kalır.
        var nodes = BigNodes(); // aralık 34px, adlar ≤4 karakter (24px) ⇒ statik kararla etiket SIĞAR
        var view = GraphTestView.New(labelFontFamily: DsResources.MonoFontFamily); // Measure/Arrange YOK
        view.SetGraph(nodes, ChainEdges(nodes));
        Assert.Equal(0.0, view.CurrentCamera.Scale); // ön-koşul: kamera hedefi yok
        Assert.True(view.IsCullEnabled);             // ön-koşul: sinema bandı (aksi halde LOD hiç koşmaz)

        view.SelectedNode = "N3";

        var visual = view.NodeVisuals["N3"];
        Assert.NotNull(visual.Label);
        Assert.Equal(Visibility.Visible, visual.Label!.Visibility);
        Assert.Null(visual.Body.ToolTip);
    }
}
