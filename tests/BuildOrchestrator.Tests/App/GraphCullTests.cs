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
/// <para><b>Küçük graf güvencesi</b> (<see cref="GraphView.ShapesPathMaxNodes"/> ve altı) ayrıca pinlenir:
/// bugünkü tipik boyutta cull HİÇ devreye girmez, hiçbir etiket kaybolmaz, rozetler aynı görünür.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphCullTests
{
    private const int Layers = 6;
    private const double AvgFanIn = 1.6;
    private static readonly Size Panel = new(600, 400);

    private static GraphView NewView(Size size)
    {
        var view = new GraphView { AnimationsEnabledProvider = () => false };
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
        var (nodes, edges) = SyntheticGraph.Build(GraphView.ShapesPathMaxNodes, Layers, AvgFanIn);
        var view = NewView(Panel);

        view.SetGraph(nodes, edges);

        Assert.False(view.IsCullEnabled);
        Assert.Equal(nodes.Count, view.NodeVisuals.Count);
        Assert.Equal(edges.Count, view.EdgeVisuals.Count);

        var (more, moreEdges) = SyntheticGraph.Build(GraphView.ShapesPathMaxNodes + 1, Layers, AvgFanIn);
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
        var view = new GraphView { AnimationsEnabledProvider = () => false };
        var window = DsResources.Realize(host, view);

        view.SetGraph(nodes, edges);
        view.UpdateLayout();
        Assert.True(view.IsCullEnabled);

        // (1) Cull edilmiş bir düğüm SONRADAN materyalize edilir (seçim yoluyla) — token'ları çözülmeli.
        var culled = nodes.First(n => !view.NodeVisuals.ContainsKey(n.Name) && n.Status == GraphStatus.Succeeded);
        view.SelectedNode = culled.Name;
        view.UpdateLayout();

        var visual = view.NodeVisuals[culled.Name];
        Assert.Equal(host.FindResource("Brush.StatusSuccess"), visual.Square.Stroke);
        Assert.Equal(host.FindResource("Brush.StatusSuccessSoft"), visual.Square.Fill);
        Assert.Same(host.FindResource(GraphView.PackageIconKey), visual.Icon.Data);
        Assert.Equal(Visibility.Visible, visual.SelectionRing.Visibility);
        Assert.Equal(host.FindResource("Brush.FocusRing"), visual.SelectionRing.Stroke);
        // [G2/LOD] Bu ölçekte (katman başına ≫9 düğüm) etiketler zaten üst üste binerdi → hiç kurulmaz.
        Assert.Null(visual.Label);

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
