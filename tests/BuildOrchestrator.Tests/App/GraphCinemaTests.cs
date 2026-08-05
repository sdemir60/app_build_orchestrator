using System.Globalization;
using System.Windows;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [sinema] Büyük grafta (düğüm sayısı > FullDetailMaxNodes — cull/LOD ile AYNI kapı) devreye giren
/// sinema modunun WPF kablajı: kenar sisi, follow-zoom kamera ve etiket kararı (ölçek-değişmez katman
/// örtüşmesi + odak muafiyeti: building ya da seçili düğüm adını taşır).
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
        WithStatus(nodes, [name], status);

    /// <summary>Bir KÜMENİN statüsünü değiştirir (paralel frontier senaryoları); tek düğümlük hâli buna delege
    /// eder — kopya YASAK.</summary>
    internal static IReadOnlyList<GraphNode> WithStatus(
        IReadOnlyList<GraphNode> nodes, IReadOnlyCollection<string> names, GraphStatus status) =>
        [.. nodes.Select(n => names.Contains(n.Name) ? n with { Status = status } : n)];

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

    // ---------------------------------------------------------------- etiket kuralı: örtüşme + odak muafiyeti

    /// <summary>Kalabalık graf: 4 katman × 40 düğüm ⇒ düğüm aralığı TABANA oturur
    /// (<see cref="GraphLayout.MinNodeSpacing"/> = 34px). Etiket genişliğini yalnız AD UZUNLUĞU belirler —
    /// Geist Mono 10px'te karakter başına tam 6.0px (ÖLÇÜLDÜ) ⇒ genişlik = <paramref name="nameLength"/> × 6px.
    /// Testler bandı bu parametreyle seçer (bkz. <see cref="CrowdedNameLength"/>,
    /// <see cref="NeverFitNameLength"/>, <see cref="RoomyNameLength"/>).
    ///
    /// <para><paramref name="count"/> tam-detay bandına indirildiğinde AYNI şekil sinema kapısının ALTINDA
    /// kalır (aralık ve ad genişliği değişmez) — küçük graf güvencesi böylece "etiketleri düşürecek bir grafta"
    /// ölçülür, düşmeyeceği zaten belli olan bir grafta değil.</para></summary>
    private static IReadOnlyList<GraphNode> CrowdedNodes(
        int count = GraphView.FullDetailMaxNodes + 10, int nameLength = CrowdedNameLength) =>
        [.. Enumerable.Range(0, count)
            .Select(i => new GraphNode(CrowdedName(i, nameLength), i % 4, GraphStatus.Discovered))];

    /// <summary>SABİT genişlikte ad: "Node…" önekinden <paramref name="nameLength"/> − 3 karakter + D3 sıra
    /// numarası. Sıra numarasının D3 olması ŞART: 1-2 haneli bir biçim, düğüm sayısı 100'ü geçtiğinde katmanın
    /// en geniş adını sessizce bir karakter büyütür ve eşiği fixture'ın BÜYÜKLÜĞÜNE bağlardı.</summary>
    private static string CrowdedName(int index, int nameLength) =>
        "Nodexxxxxx"[..(nameLength - 3)] + index.ToString("D3", CultureInfo.InvariantCulture);

    /// <summary>42px etiket: 34 &lt; 42 ⇒ katman kararı FALSE; ama 34 × 1.4 = 47.6 ≥ 42, yani ESKİ oran kuralı
    /// takip tavanında bu etiketleri GÖSTERİRDİ — ekranda 47.6px arayla 58.8px metin, çift başına 11.2px
    /// FİZİKSEL örtüşme.</summary>
    private const int CrowdedNameLength = 7;
    /// <summary>60px etiket: 34 × 1.4 = 47.6 &lt; 60 ⇒ katman HİÇBİR otomatik ölçekte sığmaz. Muafiyet
    /// testlerinin fixture'ı budur: beliren bir etiketin TEK açıklaması muafiyettir, zoom DEĞİL.</summary>
    private const int NeverFitNameLength = 10;
    /// <summary>30px etiket: 30 ≤ 34 ⇒ dünyada 4px BOŞLUK var, örtüşme YOK. Eski oran kuralı bunu yine de
    /// kuşbakışında düşürürdü (34 × 0.68 = 23.12 &lt; 30 × 0.85 = 25.5) — I2 gerilemesinin tam ortası.</summary>
    private const int RoomyNameLength = 5;

    /// <summary>Katman 0'ın ORTASINDAKİ düğümün sırası (40'ın 19'u, i = 4×19). Kalabalık katmanın UÇLARI
    /// 600×372 panelde kuşbakışı ölçekte (0.68) pencerenin DIŞINDA kalır ve hiç materyalize olmaz — etiket
    /// iddiaları gerçekten kurulmuş bir görsele dayanmalıdır.</summary>
    private const int CrowdedTargetIndex = 76;

    /// <summary>Materyalize OLMUŞ görseli döndürür; olmamışsa testi açık bir mesajla düşürür (boşlukta kalan
    /// bir etiket iddiası sessizce yeşil geçerdi).</summary>
    private static GraphNodeVisual Materialised(GraphView view, string name)
    {
        Assert.True(view.NodeVisuals.ContainsKey(name), $"{name} materyalize olmadı — iddia boşlukta kalırdı.");
        return view.NodeVisuals[name];
    }

    /// <summary>Fixture'ın etiket genişliği ÜRETİMİN ölçümünden okunur — sabit bir sayı yazılsaydı font
    /// değiştiğinde testler sessizce anlamsızlaşırdı.</summary>
    private static double LabelWidthOf(IReadOnlyList<GraphNode> nodes) =>
        GraphLabelMetrics.WidestLabelWidth(nodes.Select(n => n.ShortName), DsResources.MonoFontFamily);

    /// <summary>Kalabalık bir fixture'ın geometrisini AÇIK ön-koşula çevirir: (a) katmanın etiketleri
    /// gerçekten örtüşüyor — örtüşmeseydi hiçbir iddia ölçülemezdi; (b) katmanın takip TAVANINDA (1.4×) sığıp
    /// sığmadığı testin ihtiyacına göre seçilir. Muafiyet testleri "sığmayan" bandı ister (beliren etiketin
    /// tek açıklaması muafiyet olsun), ölçek-değişmezlik testinin ikinci yönü ise "sığan" bandı (eski oran
    /// kuralının etiket KAZANDIRDIĞI bant).</summary>
    private static void AssertCrowdedLayer(IReadOnlyList<GraphNode> nodes, bool fitsAtFollowMax)
    {
        double width = LabelWidthOf(nodes);
        Assert.False(GraphLayout.LabelsFit(GraphLayout.MinNodeSpacing, width),
            "fixture'ın etiketleri örtüşmüyor — katman kararı zaten TRUE, iddia ölçülemez.");
        Assert.True(
            (GraphLayout.MinNodeSpacing * GraphCamera.FollowMaxScale >= width) == fitsAtFollowMax,
            fitsAtFollowMax
                ? "fixture 1.4×'te de sığmıyor — eski kuralın 'kazandırdığı' bant ölçülmüyor."
                : "fixture takip tavanında sığıyor — muafiyet zoom'dan ayırt edilemez.");
    }

    [StaFact]
    public void A_building_node_is_named_even_where_its_layers_labels_overlap()
    {
        // ESKİ İDDİA (af6f261 · Zooming_into_the_frontier_materialises_the_labels_that_fit_at_that_scale):
        // "kamera 1.4×'e yakınlaşınca etiket SIĞAR ve belirir". DEĞİŞME GEREKÇESİ (ölçüldü): etiketler
        // kameranın ALTINDA yaşar (World.RenderTransform), ölçüm ise ölçeksiz dünya birimindedir — ekranda hem
        // aralık hem etiket AYNI ölçekle çarpılır, yani örtüşme ölçek-DEĞİŞMEZDİR. O kural 34px aralık / 42px
        // etiket fixture'ında 1.4×'te çift başına 11.2px FİZİKSEL örtüşen etiketleri "sığıyor" sayıyordu.
        // Yeni kural: katman kararı ölçeksiz, building/seçili düğüm ise katman kararından MUAF.
        var nodes = CrowdedNodes(nameLength: NeverFitNameLength);
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));

        string name = CrowdedName(CrowdedTargetIndex, NeverFitNameLength);
        AssertCrowdedLayer(nodes, fitsAtFollowMax: false); // beliren etiketin tek açıklaması MUAFİYET olsun
        var target = Materialised(view, name);
        Assert.Null(target.Label);
        Assert.Equal(name, target.Body.ToolTip);

        view.UpdateStatuses(WithStatus(nodes, name, GraphStatus.Building));

        Assert.NotNull(target.Label);
        Assert.Equal(Visibility.Visible, target.Label!.Visibility);
        Assert.Equal(name, target.Label.Text);
        Assert.Null(target.Body.ToolTip); // etiket görünürken tam-ad tooltip'i kalkar
        // Muafiyet DÜĞÜM başınadır: katman açılmaz, kardeşleri isimsiz silüet olarak kalır.
        var siblings = view.NodeVisuals.Values
            .Where(v => v.Model.Layer == 0 && !string.Equals(v.Model.Name, name, StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(siblings);
        Assert.All(siblings, v => Assert.Null(v.Label));
    }

    [StaFact]
    public void The_selected_node_is_named_even_where_its_layers_labels_overlap()
    {
        var nodes = CrowdedNodes(nameLength: NeverFitNameLength);
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));
        string name = CrowdedName(CrowdedTargetIndex, NeverFitNameLength);
        var target = Materialised(view, name);
        Assert.Null(target.Label);

        view.SelectedNode = name;

        Assert.NotNull(target.Label);
        Assert.Equal(Visibility.Visible, target.Label!.Visibility);
        Assert.Equal(name, target.Label.Text);
        Assert.Null(target.Body.ToolTip);
        // Seçim geçişinin DIŞINDA (muafiyet yoluyla) doğan etiket de doğru boyayı alır — EnsureLabel seçim
        // durumunu kendisi okur, ardından düzeltecek bir ApplyNodeSelection geçişi gelmez.
        Assert.Same(view.FindResource("Brush.TextPrimary"), target.Label.Foreground);

        view.SelectedNode = null; // muafiyet biter → etiket geri düşer (seçim kolu da LATCH değildir)

        Assert.Equal(Visibility.Collapsed, target.Label.Visibility);
        Assert.Equal(name, target.Body.ToolTip);
    }

    [StaFact]
    public void A_finished_node_gives_its_label_back_because_the_exemption_is_not_a_latch()
    {
        // ESKİ İDDİA (af6f261 · Zooming_back_out_hides_the_labels_and_restores_the_tooltip): "kuşbakışına
        // dönünce oran histerezis tabanının (0.85) altına iner ve etiket gizlenir". DEĞİŞME GEREKÇESİ:
        // histerezis — yani görünürlük durumunun karara GERİ BESLENMESİ — kaldırıldı; karar slot.ShowsLabel'ı
        // yalnız YAZAR, okumaz. Etiketi geri alan artık zoom değil, MUAFİYETİN BİTMESİDİR.
        var nodes = CrowdedNodes(nameLength: NeverFitNameLength);
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));
        string name = CrowdedName(CrowdedTargetIndex, NeverFitNameLength);
        view.UpdateStatuses(WithStatus(nodes, name, GraphStatus.Building));
        var target = Materialised(view, name);
        Assert.NotNull(target.Label);
        Assert.Equal(Visibility.Visible, target.Label!.Visibility);

        view.UpdateStatuses(WithStatus(nodes, name, GraphStatus.Succeeded)); // build bitti, seçili DEĞİL

        Assert.Equal(Visibility.Collapsed, target.Label.Visibility);
        Assert.Equal(name, target.Body.ToolTip); // kimlik yine tooltip'e döner
    }

    [StaFact]
    public void The_neighbours_of_the_selected_node_stay_unnamed_because_the_exemption_does_not_spread()
    {
        // [BİLİNÇLİ KAPSAM DIŞI · YAGNI] Seçim zaten komşu-OLMAYANLARI %25'e söndürüyor; komşuları ayrıca
        // adlandırmak ikinci bir vurgu katmanı olurdu. Pinlenmezse "muafiyeti komşulara yay" mutantı sessizce
        // yeşil kalır.
        var nodes = CrowdedNodes(nameLength: NeverFitNameLength);
        var edges = ChainEdges(nodes);
        var view = NewView();
        view.SetGraph(nodes, edges);
        // ChainEdges katman 1'in TÜM düğümlerini katman 0'ın İLK düğümüne bağlar ⇒ onu seçmek 40 komşu üretir
        // ve MaterializeSelection hepsini kurar.
        string selected = CrowdedName(0, NeverFitNameLength);
        string neighbour = CrowdedName(1, NeverFitNameLength);
        Assert.Contains(edges, e =>
            string.Equals(e.From, selected, StringComparison.Ordinal) &&
            string.Equals(e.To, neighbour, StringComparison.Ordinal));

        view.SelectedNode = selected;

        Assert.NotNull(Materialised(view, selected).Label); // seçili düğüm MUAF
        var neighbourVisual = Materialised(view, neighbour);
        Assert.Null(neighbourVisual.Label);                 // komşusu muaf DEĞİL
        Assert.Equal(neighbour, neighbourVisual.Body.ToolTip);
    }

    [StaFact]
    public void The_layer_decision_is_scale_invariant_so_zooming_neither_wins_nor_loses_labels()
    {
        // [fix round 1 · I2] Etiket de aralık da AYNI kamera transform'unun altındadır, dolayısıyla
        // etiket–etiket örtüşmesi ölçek-DEĞİŞMEZDİR. Eski oran kuralı iki uçta da yanılıyordu; bu test ikisini
        // birden kapatır (yalnız bir yön pinlenseydi diğer yönün mutantı yeşil kalırdı).

        // --- yön 1: kuşbakışı (0.68) SIĞAN katmanın etiketini KAYBETMEZ ---
        // Eski kural: 34 × 0.68 = 23.12 < 30 × 0.85 = 25.5 ⇒ dünyada 4px boşluk olmasına rağmen düşürürdü.
        var roomy = CrowdedNodes(nameLength: RoomyNameLength);
        var roomyView = NewView();
        roomyView.SetGraph(roomy, ChainEdges(roomy));

        Assert.True(roomyView.IsCullEnabled); // ön-koşul: sinema bandı (aksi halde karar hiç koşmaz)
        Assert.True(GraphLayout.LabelsFit(GraphLayout.MinNodeSpacing, LabelWidthOf(roomy)),
            "fixture'ın etiketleri zaten örtüşüyor — 'kaybetmiyor' iddiası ölçülemez.");
        Assert.Equal(GraphCamera.MinScale, roomyView.CurrentCamera.Scale); // gerçekten kuşbakışında
        Assert.NotEmpty(roomyView.NodeVisuals);
        Assert.All(roomyView.NodeVisuals.Values, v => Assert.NotNull(v.Label));
        Assert.All(roomyView.NodeVisuals.Values, v => Assert.Equal(Visibility.Visible, v.Label!.Visibility));

        // --- yön 2: takip tavanı (1.4) SIĞMAYAN katmana etiket KAZANDIRMAZ ---
        var crowded = CrowdedNodes();
        var crowdedView = NewView();
        crowdedView.SetGraph(crowded, ChainEdges(crowded));
        string name = CrowdedName(CrowdedTargetIndex, CrowdedNameLength);
        AssertCrowdedLayer(crowded, fitsAtFollowMax: true); // eski kural TAM burada etiket kazandırırdı

        crowdedView.UpdateStatuses(WithStatus(crowded, name, GraphStatus.Building));

        Assert.Equal(GraphCamera.FollowMaxScale, crowdedView.CurrentCamera.Scale); // gerçekten 1.4'e gitti
        var siblings = crowdedView.NodeVisuals.Values
            .Where(v => v.Model.Layer == 0 && !string.Equals(v.Model.Name, name, StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(siblings);
        Assert.All(siblings, v => Assert.Null(v.Label));           // zoom etiket KAZANDIRMAZ...
        Assert.NotNull(Materialised(crowdedView, name).Label);     // ...muaf düğüm hariç
    }

    [StaFact]
    public void A_frontier_swap_that_does_not_move_the_camera_still_refreshes_the_labels()
    {
        // [fix round 1 · brief DIŞI, ölçüldü] Muafiyetin girdileri (statü, seçim) ApplyCamera hunisinden geçer
        // — ama ApplyCamera'nın Zeno erken-dönüşü ("hedef DEĞİŞMEDİYSE hiçbir animasyon yeniden başlatılmaz")
        // o huniyi KESEBİLİR. Eski kuralda zararsızdı: karar yalnız ÖLÇEĞE bakıyordu, ölçek değişmediyse karar
        // da değişmezdi. Yeni kuralda karar kameradan BAĞIMSIZ girdilerle değişir, dolayısıyla erken dönüşün
        // ALTINDA kalmamalıdır.
        //
        // Gerçek senaryo (paralel build, geniş cephe): bir proje biter, bir aralık sağındaki komşusu başlar.
        //   · frontier ağırlık merkezi 34/5 = 6.8px kayar → FrontierRetargetThresholdPx (8px) ALTINDA ⇒ odak
        //     KORUNUR (GraphCamera.ResolveFocus);
        //   · frontier bbox'ı büyür ama ölçek zaten FollowMaxScale'e kelepçelidir ⇒ ölçek AYNI;
        //   ⇒ kamera hedefi BİREBİR aynı çıkar ve etiketler hiç tazelenmezdi.
        var nodes = CrowdedNodes(nameLength: NeverFitNameLength);
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));

        // Katman 0'ın ilk BEŞ sırası (i = 4j) derleniyor; sonra 5. sıra biter, 6. sıra başlar.
        string[] frontier = [.. Enumerable.Range(0, 5).Select(j => CrowdedName(j * 4, NeverFitNameLength))];
        string finishing = frontier[^1];
        string starting = CrowdedName(5 * 4, NeverFitNameLength);
        view.UpdateStatuses(WithStatus(nodes, frontier, GraphStatus.Building));

        var finished = Materialised(view, finishing);
        var started = Materialised(view, starting);
        Assert.NotNull(finished.Label); // ön-koşul: derlenen düğüm adını almış
        Assert.Null(started.Label);     // ön-koşul: henüz derlenmeyen komşusu adsız
        var pinned = view.CurrentCamera;

        var swapped = WithStatus(nodes, [.. frontier[..^1], starting], GraphStatus.Building);
        view.UpdateStatuses(WithStatus(swapped, finishing, GraphStatus.Succeeded));

        Assert.Equal(pinned, view.CurrentCamera); // ön-koşul: kamera hedefi GERÇEKTEN kıpırdamadı
        Assert.Equal(Visibility.Collapsed, finished.Label!.Visibility); // biten proje adını BIRAKIR
        Assert.Equal(finishing, finished.Body.ToolTip);
        Assert.NotNull(started.Label);                                  // başlayan proje adını ALIR
        Assert.Equal(Visibility.Visible, started.Label!.Visibility);
    }

    [StaFact]
    public void Small_graph_labels_are_untouched_by_the_label_decision_machinery()
    {
        // Fixture KASTEN "düşürülebilir": aynı 34px aralık, aynı 42px adlar — yalnız düğüm sayısı tam-detay
        // bandının SINIRINDA. Karar makinesi burada koşsaydı katman kararı FALSE çıkar ve HER etiket düşerdi;
        // "etiket null mı" diye bakmak kusuru tek başına GÖREMEZ (bir kez kurulmuş etiket yalnız gizlenir) —
        // bu yüzden görünürlük de tooltip de ayrıca pinlenir.
        var nodes = CrowdedNodes(GraphView.FullDetailMaxNodes);
        var view = NewView();
        view.SetGraph(nodes, ChainEdges(nodes));
        Assert.False(view.IsCullEnabled); // ön-koşul: tam-detay bandı
        Assert.False(GraphLayout.LabelsFit(GraphLayout.MinNodeSpacing, LabelWidthOf(nodes)),
            "fixture kalabalık değil");

        view.UpdateStatuses(WithStatus(nodes, CrowdedName(0, CrowdedNameLength), GraphStatus.Building));

        Assert.Equal(nodes.Count, view.NodeVisuals.Count); // cull kapalı: hepsi kurulu
        Assert.All(view.NodeVisuals.Values, v => Assert.NotNull(v.Label)); // tam-detay garantisi
        Assert.All(view.NodeVisuals.Values, v => Assert.Equal(Visibility.Visible, v.Label!.Visibility));
        Assert.All(view.NodeVisuals.Values, v => Assert.Null(v.Body.ToolTip));
    }

    [StaFact]
    public void A_node_materialised_before_the_first_camera_target_still_lands_on_the_right_decision()
    {
        // ESKİ İDDİA (af6f261 · A_node_materialised_before_the_first_camera_target_keeps_the_static_label_
        // decision): "kamera hedefi yokken ölçek 0'dır, ölçek kararı verilemez ⇒ !_hasCamera guard'ı statik
        // kararı korur". DEĞİŞME GEREKÇESİ: karar artık ölçeği HİÇ okumuyor (örtüşme ölçek-değişmezdir), guard
        // KONUSUZ kaldı ve kaldırıldı. Yeni iddia daha güçlüdür: kamera hiç hesaplanmamışken bile karar DOĞRU
        // çıkar — kalabalık bir katmanda seçilen düğüm adını alır, üstelik ekran dışından gelen bir seçimle.
        var nodes = CrowdedNodes(nameLength: NeverFitNameLength);
        var view = GraphTestView.New(labelFontFamily: DsResources.MonoFontFamily); // Measure/Arrange YOK
        view.SetGraph(nodes, ChainEdges(nodes));
        Assert.Equal(0.0, view.CurrentCamera.Scale); // ön-koşul: kamera hedefi yok
        Assert.True(view.IsCullEnabled);             // ön-koşul: sinema bandı (aksi halde karar hiç koşmaz)
        Assert.Empty(view.NodeVisuals);              // ön-koşul: viewport yok ⇒ hiçbir şey materyalize olmadı

        string name = CrowdedName(CrowdedTargetIndex, NeverFitNameLength);
        view.SelectedNode = name; // MaterializeSelection kurar — ApplyCamera viewport yokken erken döner

        var visual = Materialised(view, name);
        Assert.NotNull(visual.Label);
        Assert.Equal(Visibility.Visible, visual.Label!.Visibility);
        Assert.Null(visual.Body.ToolTip);

        // ...ve seçim BAŞKA bir düğüme geçtiğinde adını GERİ BIRAKIR. [fix round 2 · Important #1] Bu, aynı
        // hunimin İKİNCİ kesiğidir: ApplyCamera iki erken dönüşle başlar (slot yok / viewport ölçülmemiş) ve
        // MaterializeSelection viewport'a BAKMADAN düğüm kurduğu için bu rejimde materyalize düğüm gerçekten
        // vardır. Karar viewport okumaz — dolayısıyla o guard'ların da ÜSTÜNDE tazelenmelidir. (İlk gerçek
        // SizeChanged kendini onarırdı, ama kural kuraldır: muafiyet biten düğüm adını taşımaya devam edemez.)
        string other = CrowdedName(CrowdedTargetIndex + 4, NeverFitNameLength);
        view.SelectedNode = other;

        Assert.Equal(Visibility.Collapsed, visual.Label.Visibility);
        Assert.Equal(name, visual.Body.ToolTip);
        Assert.NotNull(Materialised(view, other).Label); // yeni seçim adını ALIR
    }
}
