using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using IoPath = System.IO.Path;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T51/G2] Viewport cull + tembel materyalizasyonun WPF kablajı. G1 darboğazın <b>nesne kurulumu</b> olduğunu
/// ölçtü (<c>SetGraph</c> %64-72, <c>Measure/Arrange</c> %28-36, saf layout %0,03); G2 bu yüzden görünmeyen
/// düğüm/kenarın ağacını HİÇ kurmaz. Buradaki testler cull'un tuzaklarını kapatır: kaydırma sonrası girip çıkan
/// düğümler, seçili düğümün ve komşularının asla cull edilmemesi, cull edilmişken değişen statünün geri görünür
/// olunca doğru çıkması ve kenarların düğümü cull edilmişken de doğru çizilmesi.
///
/// <para><b>Küçük graf güvencesi</b> (<see cref="GraphView.FullDetailMaxNodes"/> ve altı) ayrıca pinlenir:
/// bugünkü tipik boyutta cull HİÇ devreye girmez, hiçbir etiket kaybolmaz, rozetler aynı görünür.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphCullTests
{
    private const int Layers = 6;
    private const double AvgFanIn = 1.6;
    private static readonly Size Panel = new(600, 400);

    /// <summary>Adları KISA olan, verilen katman yapısında bir graf (LOD eşiğinin ad genişliğine gerçekten
    /// duyarlı olduğunu göstermek için — <c>SyntheticGraph</c>'ın adları uzundur).</summary>
    private static IReadOnlyList<GraphNode> ShortNamedNodes(int perLayer, int layers, string prefix = "P") =>
        [.. Enumerable.Range(0, perLayer * layers)
            .Select(i => new GraphNode($"{prefix}{i}", i / perLayer, GraphStatus.Discovered))];

    private static GraphView NewView(Size size, bool animations = false)
    {
        var view = new GraphView
        {
            AnimationsEnabledProvider = () => animations,
            // pack:// aileler gerçek bir Application olmadan çözülmez → LOD ölçümü için file:// aile enjekte
            // edilir (TrackedTextBlockTests deseni). Üretimde bu seam ASLA set edilmez.
            LabelFontFamily = DsResources.MonoFontFamily,
        };
        foreach (string name in new[] { "Tokens.xaml", "Motion.xaml", "Icons.xaml" })
        {
            using var stream = File.OpenRead(IoPath.Combine(AppContext.BaseDirectory, "TestAssets", "Resources", name));
            view.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
        }
        Layout(view, size);
        return view;
    }

    private static void Layout(FrameworkElement view, Size size)
    {
        view.Measure(size);
        view.Arrange(new Rect(new Point(0, 0), size));
        view.UpdateLayout();
    }

    // ---------------------------------------------------------------- eager bant (küçük graf = değişmedi)

    [StaFact]
    public void A_graph_inside_the_eager_band_still_materialises_every_node_and_edge()
    {
        // [G2] Cull yalnız ShapesPathMaxNodes'un ÜSTÜNDE devreye girer — bugünkü graf (onlarca düğüm) birebir
        // eskisi gibi kurulur. Sınır İKİ TARAFTAN da pinlenir ki eşik sessizce kaymasın.
        var (nodes, edges) = SyntheticGraph.Build(GraphView.FullDetailMaxNodes, Layers, AvgFanIn);
        var view = NewView(Panel);

        view.SetGraph(nodes, edges);

        Assert.False(view.IsCullEnabled);
        Assert.Equal(nodes.Count, view.NodeVisuals.Count);
        Assert.Equal(edges.Count, view.EdgeVisuals.Count);

        var (more, moreEdges) = SyntheticGraph.Build(GraphView.FullDetailMaxNodes + 1, Layers, AvgFanIn);
        view.SetGraph(more, moreEdges);
        Assert.True(view.IsCullEnabled);
        Assert.Equal(more.Count, view.NodeCount);
        Assert.Equal(moreEdges.Count, view.EdgeCount);
    }

    [StaFact]
    public void The_design_sized_graph_is_built_exactly_as_before_the_cull_full_tree_labels_and_no_dead_badges()
    {
        // [G2] "Küçük grafta hiçbir şey değişmedi" güvencesinin TEK testte toplanmış hâli: cull kapalı, TÜM
        // düğüm/kenar kurulu, HER düğümde etiket var, rozet YALNIZ gerçekten dep-hatası olanlarda kurulmuş
        // (eskiden hepsinde kurulup gizleniyordu — görünüm aynı, ölü nesne yok) ve tuval hâlâ 880.
        var (nodes, edges) = SyntheticGraph.Build(36, Layers, AvgFanIn);
        var view = NewView(Panel);

        view.SetGraph(nodes, edges);

        Assert.False(view.IsCullEnabled);
        Assert.Equal(nodes.Count, view.NodeVisuals.Count);
        Assert.Equal(edges.Count, view.EdgeVisuals.Count);
        Assert.Equal(GraphLayout.CanvasWidth, view.GraphSize.Width);
        Assert.All(view.NodeVisuals.Values, v => Assert.NotNull(v.Label));
        foreach (var node in nodes)
        {
            var visual = view.NodeVisuals[node.Name];
            Assert.Equal(node.ShortName, visual.Label!.Text);
            if (node.HasDepIssue)
                Assert.Equal(Visibility.Visible, Assert.IsType<System.Windows.Controls.Grid>(visual.Badge).Visibility);
            else
                Assert.Null(visual.Badge); // ölü rozet alt-ağacı YOK
        }
    }

    // ---------------------------------------------------------------- [fix round 1] etiket LOD

    [StaFact]
    public void A_small_but_crowded_graph_keeps_every_label_because_LOD_shares_the_full_detail_gate()
    {
        // [fix round 1 · A2] LOD, cull ile AYNI kapıya bağlıdır. 30 düğümlük bir grafta 15 düğümlü bir katman
        // olsa bile (aralık (880−70)/14,5 ≈ 55,9px) hiçbir etiket düşmez — "küçük grafta görünüm BİREBİR aynı"
        // pazarlık dışı kısıtı. (İlk turda LOD ayrı bir eşikten koştuğu için bu graf etiketlerini kaybediyordu.)
        var nodes = ShortNamedNodes(perLayer: 15, layers: 2, prefix: "Domain.Vehicle.Registration.Long");
        var view = NewView(Panel);

        view.SetGraph(nodes, []);

        Assert.False(view.IsCullEnabled);
        Assert.True(GraphLayout.NodeSpacingFor(15) < GraphLayout.NodeCellWidth, "fixture kalabalık değil");
        Assert.Equal(nodes.Count, view.NodeVisuals.Count);
        Assert.All(view.NodeVisuals.Values, v => Assert.NotNull(v.Label));
        Assert.All(view.NodeVisuals.Values, v => Assert.Null(v.Body.ToolTip)); // etiket var → tooltip gereksiz
    }

    [StaFact]
    public void Above_the_gate_short_labels_survive_a_crowded_layer_and_long_ones_do_not()
    {
        // [fix round 1 · A1] Eşik, etiketin ÇİZİLEN genişliğidir — hücre kelepçesi (88,4px) DEĞİL. Katman
        // başına 10 düğüm ⇒ aralık ≈ 85,3px. Kısa adlar (≈4 karakter, ≈24px) HİÇ örtüşmez ⇒ etiket KALIR;
        // aynı yapıda uzun adlar gerçekten örtüşür ⇒ DÜŞER. Kelepçeye dayalı eski eşik İKİSİNİ DE düşürürdü.
        Assert.True(GraphLayout.NodeSpacingFor(10) < GraphLayout.NodeCellWidth);

        var shortNamed = ShortNamedNodes(perLayer: 10, layers: 20);
        var shortView = NewView(Panel);
        shortView.SetGraph(shortNamed, []);

        Assert.True(shortView.IsCullEnabled); // 200 düğüm — tam detay bandının DIŞINDA
        Assert.NotEmpty(shortView.NodeVisuals);
        Assert.All(shortView.NodeVisuals.Values, v => Assert.NotNull(v.Label));

        var longNamed = ShortNamedNodes(perLayer: 10, layers: 20, prefix: "Domain.Vehicle.Registration.Long");
        var longView = NewView(Panel);
        longView.SetGraph(longNamed, []);

        Assert.True(longView.IsCullEnabled);
        Assert.NotEmpty(longView.NodeVisuals);
        Assert.All(longView.NodeVisuals.Values, v => Assert.Null(v.Label));
    }

    [StaFact]
    public void A_node_that_lost_its_label_carries_the_full_project_name_as_a_tooltip_without_building_objects()
    {
        // [fix round 1 · A3] Etiketi düşen düğüm ANONİM KALMAZ. Tooltip DÜZ METİNDİR: WPF ToolTip kontrolünü
        // ancak gösterirken kurar ⇒ düğüm başına nesne sayısı ARTMAZ (bu task'ın kazancı geri verilmez).
        var nodes = ShortNamedNodes(perLayer: 10, layers: 20, prefix: "OSYS.Domain.Vehicle.Registration");
        var view = NewView(Panel);
        view.SetGraph(nodes, []);

        var (name, visual) = view.NodeVisuals.First();
        Assert.Null(visual.Label);
        // Hedef, tıklama alanının KENDİSİDİR (düğüm karesinin tamamı) — etiket düşünce daralan hedefi telafi eder.
        Assert.Same(visual.Body, visual.Cell.Children[0]);
        Assert.Equal(name, visual.Body.ToolTip);
        Assert.Contains(visual.SquareHost, visual.Body.Children.Cast<UIElement>());

        // Nesne tavanı hâlâ geçerli: tooltip HİÇBİR nesne eklemez (etiketsiz düğüm 8 + hücre = 9'un altında).
        Layout(view, Panel);
        int objects = DsResources.RealizedObjects(visual.Cell).Count + 1;
        Assert.True(objects <= 9, $"tooltip'li etiketsiz düğüm {objects} nesne kuruyor.");
    }

    // ---------------------------------------------------------------- cull

    [StaFact]
    public void A_large_graph_only_builds_the_visual_tree_of_the_nodes_the_camera_can_see()
    {
        var (nodes, edges) = SyntheticGraph.Build(500, Layers, AvgFanIn);
        var view = NewView(Panel);

        view.SetGraph(nodes, edges);

        Assert.True(view.IsCullEnabled);
        Assert.Equal(nodes.Count, view.NodeCount);       // model TAM
        Assert.Equal(edges.Count, view.EdgeCount);
        Assert.NotEmpty(view.NodeVisuals);               // görünen kısım GERÇEKTEN kuruldu
        Assert.True(view.NodeVisuals.Count < nodes.Count / 2,
            $"cull etkisiz: {nodes.Count} düğümün {view.NodeVisuals.Count}'i materyalize oldu.");
    }

    [StaFact]
    public void Enlarging_the_viewport_materialises_the_nodes_that_scroll_into_view()
    {
        var (nodes, edges) = SyntheticGraph.Build(500, Layers, AvgFanIn);
        var view = NewView(Panel);
        view.SetGraph(nodes, edges);
        int before = view.NodeVisuals.Count;
        var culled = nodes.First(n => !view.NodeVisuals.ContainsKey(n.Name));

        Layout(view, new Size(6000, 3000)); // panel büyüdü → kamera daha geniş bir dünya gösteriyor

        Assert.True(view.NodeVisuals.Count > before,
            $"görünür alan büyüdü ama materyalize düğüm sayısı artmadı ({before}).");
        Assert.True(view.NodeVisuals.ContainsKey(culled.Name), "görünür alana giren düğüm hâlâ kurulmamış.");
    }

    [StaFact]
    public void A_node_whose_status_changed_while_it_was_culled_shows_the_new_status_when_it_appears()
    {
        var (nodes, edges) = SyntheticGraph.Build(500, Layers, AvgFanIn);
        var view = NewView(Panel);
        view.SetGraph(nodes, edges);
        var culled = nodes.First(n => !view.NodeVisuals.ContainsKey(n.Name));

        // Cull edilmişken FAILED olur — hiçbir görsel yokken.
        var updated = nodes
            .Select(n => n.Name == culled.Name ? n with { Status = GraphStatus.Failed, HasDepIssue = true } : n)
            .ToList();
        view.UpdateStatuses(updated);
        Assert.False(view.NodeVisuals.ContainsKey(culled.Name));

        Layout(view, new Size(6000, 3000)); // geri görünür oldu

        var visual = view.NodeVisuals[culled.Name];
        Assert.Equal(GraphStatus.Failed, visual.Model.Status);
        Assert.Equal(view.TryFindResource("Brush.StatusFail"), visual.Square.Stroke);
        Assert.Equal(view.TryFindResource("Brush.StatusFailSoft"), visual.Square.Fill);
        // Rozet de tembeldir: cull edilmişken gelen dep-hatası, materyalizasyonda kurulur.
        Assert.NotNull(visual.Badge);
        Assert.Equal(Visibility.Visible, visual.Badge.Visibility);
    }

    [StaFact]
    public void The_selected_node_and_its_neighbours_are_never_culled_even_when_the_camera_is_elsewhere()
    {
        var (nodes, edges) = SyntheticGraph.Build(500, Layers, AvgFanIn);
        var view = NewView(Panel);
        view.SetGraph(nodes, edges);

        // Ekran dışında kalmış, en az bir komşusu olan bir düğüm seç (seçim listeden de gelebilir).
        var culled = nodes.First(n =>
            !view.NodeVisuals.ContainsKey(n.Name) &&
            edges.Any(e => e.From == n.Name || e.To == n.Name));

        view.SelectedNode = culled.Name;

        var visual = view.NodeVisuals[culled.Name];
        Assert.Equal(Visibility.Visible, visual.SelectionRing.Visibility);
        Assert.Equal(GraphView.SelectedNodeBorderThickness, visual.Square.StrokeThickness);
        Assert.Equal(1.0, visual.Body.Opacity);

        // Komşular da kurulur ve SÖNMEZ — komşuluk TÜM kenarlardan hesaplanır, yalnız materyalize olanlardan değil.
        var neighbours = edges
            .Where(e => e.From == culled.Name || e.To == culled.Name)
            .Select(e => e.From == culled.Name ? e.To : e.From)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(neighbours);
        foreach (string name in neighbours)
            Assert.Equal(1.0, view.NodeVisuals[name].Body.Opacity);

        // Seçim dışı bir düğüm ise söner (%25).
        var stranger = view.NodeVisuals.First(kv =>
            kv.Key != culled.Name && !neighbours.Contains(kv.Key, StringComparer.Ordinal));
        Assert.Equal(GraphView.DimmedNodeOpacity, stranger.Value.Body.Opacity);
    }

    [StaFact]
    public void An_edge_is_styled_from_the_models_even_when_the_node_at_its_end_is_culled()
    {
        var (nodes, edges) = SyntheticGraph.Build(500, Layers, AvgFanIn);
        var view = NewView(Panel);
        view.SetGraph(nodes, edges);
        var byName = nodes.ToDictionary(n => n.Name, StringComparer.Ordinal);

        // Kenar materyalize (bezier ekrana giriyor) ama ucundaki düğüm cull edilmiş — stil MODELDEN gelmeli.
        var edge = view.EdgeVisuals.First(e => !view.NodeVisuals.ContainsKey(e.Model.From));

        Assert.NotNull(edge.Style);
        Assert.Equal(
            EdgeStyleResolver.Resolve(
                byName[edge.Model.From].Status,
                byName[edge.Model.From].HasDepIssue,
                byName[edge.Model.To].Status,
                touchesSelection: false,
                hasSelection: false),
            edge.Style);
        Assert.Equal(view.TryFindResource(edge.Style.BrushKey), edge.Path.Stroke);
    }

    [StaFact]
    public void Jumping_to_a_far_viewport_never_materialises_the_nodes_in_between()
    {
        // [fix round 1 · B1] Materyalizasyon kararı ŞU ANKİ görünür alana göre verilir; görülmüş tüm alanların
        // KÜMÜLATİF birleşimine göre değil. İlk turda birleşim tutuluyordu ⇒ uzak bir görünüme atlamak aradaki
        // HİÇ GÖRÜLMEMİŞ düğümleri de kuruyordu.
        var (nodes, edges) = SyntheticGraph.Build(500, Layers, AvgFanIn);
        var view = NewView(Panel); // animasyon KAPALI ⇒ kamera anlık oturur, ara kare yok
        view.SetGraph(nodes, edges);
        var firstRegion = GraphCulling.VisibleWorldRect(view.ViewportSize, view.CurrentCamera);

        // Grafın en sağındaki düğümü seç → kamera oraya ATLAR.
        var far = nodes.OrderByDescending(n => view.NodeCenter(n.Name).X).First();
        view.SelectedNode = far.Name;
        var secondRegion = GraphCulling.VisibleWorldRect(view.ViewportSize, view.CurrentCamera);
        Assert.True(secondRegion.Left > firstRegion.Right, "iki görünüm örtüşüyor — fixture ayırt edici değil");

        // Seçim ve komşuları kasıtlı olarak her zaman materyalize edilir; onlar dışında materyalize olan HER
        // düğüm iki görünümden BİRİNİN içinde olmalı — arada kalan hiçbir düğüm kurulmamalı.
        var pinned = new HashSet<string>(StringComparer.Ordinal) { far.Name };
        foreach (var e in edges.Where(e => e.From == far.Name || e.To == far.Name))
            pinned.Add(e.From == far.Name ? e.To : e.From);

        var between = new List<string>();
        foreach (var (name, _) in view.NodeVisuals)
        {
            if (pinned.Contains(name)) continue;
            var bounds = GraphCulling.NodeBounds(view.NodeCenter(name));
            if (!firstRegion.IntersectsWith(bounds) && !secondRegion.IntersectsWith(bounds)) between.Add(name);
        }

        Assert.True(between.Count == 0,
            $"iki görünümün ARASINDA kalan {between.Count} düğüm boşuna materyalize oldu: " +
            string.Join(", ", between.Take(3)));
        // ...ve arada gerçekten materyalize EDİLMEMİŞ düğümler var (test tautoloji değil).
        Assert.Contains(nodes, n =>
            !view.NodeVisuals.ContainsKey(n.Name) &&
            !firstRegion.IntersectsWith(GraphCulling.NodeBounds(view.NodeCenter(n.Name))) &&
            !secondRegion.IntersectsWith(GraphCulling.NodeBounds(view.NodeCenter(n.Name))));
    }

    [StaFact]
    public void A_node_materialised_while_the_reveal_is_playing_joins_the_stagger_instead_of_popping_in()
    {
        // [fix round 1 · B2] MOTION SÖZLEŞMESİ: düğüm animasyonu ATLAYARAK, tam opaklıkta belirmez. Bu yol
        // gerçek bir senaryodur — MainWindow her büyük rebuild'de SetGraph'ın ardından IsSettled'ı iter ve
        // kamera yeniden hedeflenir, yani reveal penceresi İÇİNDE yeni düğümler materyalize olur.
        var (nodes, edges) = SyntheticGraph.Build(500, Layers, AvgFanIn);
        var view = NewView(Panel, animations: true);
        view.SetGraph(nodes, edges);
        Assert.All(view.NodeVisuals.Values, v => Assert.Equal(0.0, v.Cell.Opacity)); // stagger oynuyor

        var culled = nodes.First(n => !view.NodeVisuals.ContainsKey(n.Name));
        view.SelectedNode = culled.Name; // reveal penceresinin İÇİNDE materyalize olur

        var late = view.NodeVisuals[culled.Name];
        Assert.Equal(0.0, late.Cell.Opacity);                       // gecikme boyunca 0 — flash yok
        Assert.IsType<TranslateTransform>(late.Cell.RenderTransform); // 5px yukarıdan gelir
        Assert.True(late.Cell.HasAnimatedProperties, "geç gelen düğüm stagger'a katılmadı");

        // Reduced-motion'da (stagger hiç oynamaz) geç gelen düğüm ANİ yerleşir — sözleşme oradaki gibi kalır.
        var still = NewView(Panel);
        still.SetGraph(nodes, edges);
        var stillCulled = nodes.First(n => !still.NodeVisuals.ContainsKey(n.Name));
        still.SelectedNode = stillCulled.Name;
        Assert.Equal(1.0, still.NodeVisuals[stillCulled.Name].Cell.Opacity);
    }

    // ---------------------------------------------------------------- tick maliyeti

    [StaFact]
    public void Pushing_the_same_statuses_again_touches_no_node_at_all()
    {
        // [G2] 200ms'lik tick eskiden HER düğümde 2× SetResourceReference + IconPaint.Apply (ağaç yukarı
        // TryFindResource yürüyüşü) + yeni DoubleCollection allocation yapıyordu. Kanıt DETERMİNİSTİKTİR
        // (duvar saati değil): ApplyNodeStatus sayacı.
        var (nodes, edges) = SyntheticGraph.Build(36, Layers, AvgFanIn);
        var view = NewView(Panel);
        view.SetGraph(nodes, edges);

        view.UpdateStatuses(nodes);
        int afterFirst = view.NodeStatusApplyCount;
        view.UpdateStatuses(nodes);
        view.UpdateStatuses(nodes);

        Assert.Equal(afterFirst, view.NodeStatusApplyCount);

        // Gerçekten DEĞİŞEN tek düğüm yine güncellenir (fast-path sağır değildir).
        var changed = nodes.Select((n, i) => i == 0 ? n with { Status = GraphStatus.Cycle } : n).ToList();
        view.UpdateStatuses(changed);
        Assert.Equal(afterFirst + 1, view.NodeStatusApplyCount);
        Assert.Equal(view.TryFindResource("Brush.StatusCycle"), view.NodeVisuals[nodes[0].Name].Square.Stroke);
    }

    [StaFact]
    public void Dashed_frames_share_one_frozen_collection_instead_of_allocating_per_node_per_tick()
    {
        var (nodes, edges) = SyntheticGraph.Build(36, Layers, AvgFanIn);
        var view = NewView(Panel);
        view.SetGraph(nodes, edges);

        var dashed = nodes.Where(n => n.Status == GraphStatus.Discovered).Take(2).ToList();
        Assert.Equal(2, dashed.Count);
        var first = view.NodeVisuals[dashed[0].Name].Square.StrokeDashArray;
        var second = view.NodeVisuals[dashed[1].Name].Square.StrokeDashArray;

        Assert.Same(first, second);
        Assert.True(first.IsFrozen);
        Assert.Equal([2.0, 2.0], first);

        // Kesikli OLMAYAN düğümler de tek bir donmuş boş koleksiyonu paylaşır.
        var solid = nodes.Where(n => n.Status != GraphStatus.Discovered).Take(2).ToList();
        Assert.Same(
            view.NodeVisuals[solid[0].Name].Square.StrokeDashArray,
            view.NodeVisuals[solid[1].Name].Square.StrokeDashArray);
    }

    // ---------------------------------------------------------------- REALIZE TESTİ (It-4b dersi · c6e9a21)

    [StaFact]
    public void A_lazily_built_node_and_badge_resolve_their_tokens_through_the_real_app_resource_chain()
    {
        // [G2 · REALIZE TESTİ — It-4b dersi, commit c6e9a21] Headless Measure/Arrange XAML runtime çözümlemesini
        // GÖRMEZ. G2 iki alt-ağacı (düğüm ve rozet) artık SetGraph'ta değil SONRADAN, view ağaçtayken kuruyor —
        // "kurulduğu an kaynak zinciri var mı" sorusu bu yüzden gerçek bir risktir. Bu test grafı uygulamanın
        // GERÇEK merge zinciriyle (Motion → Tokens → Icons → Controls) ve gerçek bir HWND içinde ayağa kaldırır,
        // sonra tembel yolları TETİKLER ve fırça/geometrinin gerçekten çözüldüğünü doğrular.
        var (nodes, edges) = SyntheticGraph.Build(500, Layers, AvgFanIn);
        var host = DsResources.NewHost();
        var view = new GraphView
        {
            AnimationsEnabledProvider = () => false,
            LabelFontFamily = DsResources.MonoFontFamily,
        };
        var window = DsResources.Realize(host, view);

        view.SetGraph(nodes, edges);
        view.UpdateLayout();
        Assert.True(view.IsCullEnabled);
        var edgesBefore = new HashSet<GraphEdge>(view.EdgeVisuals.Select(e => e.Model));

        // (1) Cull edilmiş bir düğüm SONRADAN materyalize edilir (seçim yoluyla) — token'ları çözülmeli.
        var culled = nodes.First(n =>
            !view.NodeVisuals.ContainsKey(n.Name) &&
            n.Status == GraphStatus.Succeeded &&
            edges.Any(e => e.From == n.Name || e.To == n.Name));
        view.SelectedNode = culled.Name;
        view.UpdateLayout();

        var visual = view.NodeVisuals[culled.Name];
        Assert.Equal(host.FindResource("Brush.StatusSuccess"), visual.Square.Stroke);
        Assert.Equal(host.FindResource("Brush.StatusSuccessSoft"), visual.Square.Fill);
        Assert.Same(host.FindResource(GraphView.PackageIconKey), visual.Icon.Data);
        Assert.Equal(Visibility.Visible, visual.SelectionRing.Visibility);
        Assert.Equal(host.FindResource("Brush.FocusRing"), visual.SelectionRing.Stroke);
        // [G2/LOD] Bu ölçekte (katman başına ≫9 düğüm, uzun adlar) etiketler zaten üst üste binerdi → hiç
        // kurulmaz; kimlik yerine TOOLTIP taşınır (fix round 1 · A3).
        Assert.Null(visual.Label);
        Assert.Equal(culled.Name, visual.Body.ToolTip);

        // (1b) [fix round 1 · C2] Seçimle birlikte TEMBEL materyalize edilen KENARLAR da kaynak zincirini
        // çözmeli — MaterializeEdge, Stroke'u SetResourceReference ile bağlar ve bu tam c6e9a21'in sınıfıdır
        // (headless suite XAML/kaynak çözümlemesini görmez).
        var lazyEdge = view.EdgeVisuals.First(e => !edgesBefore.Contains(e.Model));
        Assert.NotNull(lazyEdge.Style);
        Assert.Equal(host.FindResource(lazyEdge.Style.BrushKey), lazyEdge.Path.Stroke);
        Assert.Equal(lazyEdge.Style.Thickness, lazyEdge.Path.StrokeThickness);
        Assert.Equal(lazyEdge.Style.Opacity, lazyEdge.Path.Opacity);
        Assert.False(lazyEdge.Path.Data.IsEmpty());

        // (2) Rozet de TEMBEL kurulur: dep-hatası sonradan gelir, alt-ağaç canlı ağaçta doğar.
        var updated = nodes.Select(n => n.Name == culled.Name ? n with { HasDepIssue = true } : n).ToList();
        view.UpdateStatuses(updated);
        view.UpdateLayout();

        Assert.NotNull(visual.Badge);
        Assert.Equal(Visibility.Visible, visual.Badge.Visibility);
        Assert.Equal(host.FindResource("Brush.SurfaceBase"), visual.BadgeCircle!.Fill);
        Assert.Equal(host.FindResource("Brush.StatusFailBorder"), visual.BadgeCircle.Stroke);
        Assert.Same(host.FindResource(GraphView.WarningTriangleIconKey), visual.BadgeTriangle!.Data);
        Assert.Equal(host.FindResource("Brush.StatusFailText"), visual.BadgeTriangle.Fill);
        // İkonun DOLU/KONTURLU kipi de sözlükten gelir (IconPaint) — dolu üçgende stroke YOKTUR.
        Assert.Null(visual.BadgeTriangle.Stroke);

        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_lazily_materialised_node_lands_under_the_node_layer_so_edges_stay_beneath_it()
    {
        // Tembel materyalizasyonda ekleme SIRASI z-order'ı garanti edemez (bir kenar bir düğümden SONRA görünür
        // alana girebilir). İki ayrı katman host'u bunu yapısal olarak garanti eder.
        var (nodes, edges) = SyntheticGraph.Build(500, Layers, AvgFanIn);
        var view = NewView(Panel);
        view.SetGraph(nodes, edges);
        var culled = nodes.First(n => !view.NodeVisuals.ContainsKey(n.Name));

        view.SelectedNode = culled.Name;

        var cell = view.NodeVisuals[culled.Name].Cell;
        var nodeLayer = VisualTreeHelper.GetParent(cell);
        var anyEdgeLayer = VisualTreeHelper.GetParent(view.EdgeVisuals[0].Path);
        Assert.NotSame(nodeLayer, anyEdgeLayer);
        // Kenar katmanı düğüm katmanının ALTINDA (World'ün çocuk sırasında önce) olmalı.
        var world = view.World;
        Assert.Equal(0, world.Children.IndexOf((UIElement)anyEdgeLayer!));
        Assert.Equal(1, world.Children.IndexOf((UIElement)nodeLayer!));
    }
}
