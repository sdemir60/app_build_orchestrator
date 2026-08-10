using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using ShapePath = System.Windows.Shapes.Path;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// design v1.3.0 §2.3: node = <b>İSİMSİZ</b> mini kare — boyut = pitch×0.6 (8–24 kelepçe), radius-sm,
/// 1.5px border, içinde node'un %52'si kadar Lucide <c>box</c> glyph'i. Node üstü ad etiketleri ve graf içi
/// dep-issue rozeti KALDIRILDI (§2.3 "Kaldırılanlar"); ad hover tooltip'i ve seçim etiketiyle verilir.
///
/// <para><b>Eski iddialar (<c>GraphRenderTests</c>/<c>GraphCullTests</c>, artık geçersiz):</b> düğüm 26px
/// SABİT bir kareydi, altında 10px mono kısa ad etiketi taşırdı (kalabalık katmanda LOD ile düşerdi) ve
/// dep-hatası olanda 13px'lik bir rozet kurulurdu. Ayrıca büyük grafta görünmeyen düğümlerin ağacı hiç
/// kurulmazdı (cull).</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class QuietGraphNodeTests
{
    private static IReadOnlyList<GraphNode> Nodes() =>
    [
        new("OSYS.Base", 0, GraphStatus.Succeeded),
        new("OSYS.Data", 1, GraphStatus.Failed),
        new("OSYS.Api", 2, GraphStatus.Queued),
        new("OSYS.Legacy", 2, GraphStatus.Discovered),
    ];

    private static IReadOnlyList<GraphEdge> Edges() =>
    [
        new("OSYS.Base", "OSYS.Data"),
        new("OSYS.Data", "OSYS.Api"),
    ];

    private static GraphView Built(Size size)
    {
        var view = GraphTestView.Realized(size);
        view.SetGraph(Nodes(), Edges());
        return view;
    }

    /// <summary>Kare kenarı YERLEŞİMİN verdiği düğüm boyutudur — 26px sabit DEĞİL.</summary>
    [StaFact]
    public void The_node_square_is_the_layouts_node_size_not_a_fixed_26()
    {
        var view = Built(new Size(640, 400));
        var square = view.NodeVisuals["OSYS.Base"].Square;

        Assert.Equal(view.NodeSize, square.Width, 3);
        Assert.Equal(view.NodeSize, square.Height, 3);
        Assert.Equal(
            Math.Clamp(view.Pitch * QuietGraphLayout.NodeSizeFactor,
                QuietGraphLayout.MinNodeSize, QuietGraphLayout.MaxNodeSize),
            view.NodeSize, 6);
        Assert.Equal(GraphView.NodeCornerRadius, square.RadiusX, 6);   // DS radius-sm
        Assert.Equal(GraphView.NodeBorderThickness, square.StrokeThickness, 6);
    }

    /// <summary>
    /// AYIRT EDİCİ — bu değişikliğin en yüksek regresyon riski: yerleşim artık PANEL ÖLÇÜSÜNÜN
    /// fonksiyonudur. Panel yeniden boyutlanınca düğümler hem YER hem BOYUT değiştirir.
    ///
    /// <para><b>Eski iddia:</b> yerleşim <c>SetGraph</c>'ta bir kez hesaplanır ve panelden bağımsızdır.</para>
    /// </summary>
    [StaFact]
    public void Resizing_the_panel_recomputes_the_layout_so_nodes_move_AND_change_size()
    {
        var view = Built(new Size(1200, 700));
        double bigSize = view.NodeSize;
        var bigCentre = view.NodeCenter("OSYS.Api");

        GraphTestView.Resize(view, new Size(320, 200));
        view.UpdateLayout();

        Assert.True(view.NodeSize < bigSize, $"daralan panelde düğüm küçülmedi: {bigSize} → {view.NodeSize}");
        Assert.NotEqual(bigCentre, view.NodeCenter("OSYS.Api"));
        Assert.Equal(view.NodeSize, view.NodeVisuals["OSYS.Api"].Square.Width, 3);
    }

    /// <summary>Yeniden boyutlanma görselleri YENİDEN KURMAZ, yerinde günceller — splitter sürüklenirken
    /// saniyede onlarca ölçü olayı gelir ve her birinde yüzlerce düğümü baştan inşa etmek paneli dondururdu.</summary>
    [StaFact]
    public void Resizing_updates_the_visuals_in_place_instead_of_rebuilding_them()
    {
        var view = Built(new Size(1200, 700));
        var square = view.NodeVisuals["OSYS.Base"].Square;
        int before = view.LayoutComputeCount;

        GraphTestView.Resize(view, new Size(900, 500));
        view.UpdateLayout();

        Assert.Same(square, view.NodeVisuals["OSYS.Base"].Square);
        Assert.True(view.LayoutComputeCount > before, "yerleşim yeniden hesaplanmadı");
    }

    /// <summary>Hiçbir düğümde ad etiketi kurulmaz (§2.3 "Kaldırılanlar").</summary>
    [StaFact]
    public void No_node_carries_a_name_label_any_more()
    {
        var view = Built(new Size(640, 400));

        foreach (var visual in view.NodeVisuals.Values)
            Assert.Empty(DsResources.RealizedObjects(visual.Cell).OfType<TextBlock>());
    }

    /// <summary>Graf içi dep-issue rozeti kaldırıldı — dep bilgisi kartlarda yaşıyor (§2.3 "Kaldırılanlar").
    /// Düğümde tek bir <see cref="ShapePath"/> kalır: box glyph'i.</summary>
    [StaFact]
    public void A_node_builds_no_dep_issue_badge_because_that_information_lives_on_the_cards()
    {
        var view = Built(new Size(640, 400));

        foreach (var visual in view.NodeVisuals.Values)
        {
            var realized = DsResources.RealizedObjects(visual.Cell);
            Assert.Empty(realized.OfType<Ellipse>());
            Assert.Single(realized.OfType<ShapePath>());
        }
    }

    /// <summary>Glyph node'un %52'sidir ve geometrisi TEK ikon sözlüğünden gelir (kod içinde kopya path YOK).
    /// Ölçek TÜM düğümlerde ORTAK bir <see cref="ScaleTransform"/>'dur — düğüm başına transform kurulmaz.</summary>
    [StaFact]
    public void The_box_glyph_is_52_percent_of_the_node_and_comes_from_the_icon_dictionary()
    {
        var view = Built(new Size(640, 400));
        var icon = view.NodeVisuals["OSYS.Base"].Icon;
        // Görünümün KENDİ kaynak zincirinden çözülen örnek — ikinci bir sözlük yüklemek yeni bir nesne
        // üretirdi ve "kopya değil" iddiası referans eşitliğiyle kanıtlanamazdı.
        var expected = (Geometry)view.FindResource(GraphView.PackageIconKey);

        Assert.Same(expected, icon.Data);
        var box = (FrameworkElement)VisualTreeHelper.GetParent(icon);
        var scale = Assert.IsType<ScaleTransform>(box.RenderTransform);
        Assert.Equal(view.NodeSize * GraphView.IconFactor / 24.0, scale.ScaleX, 6);
        Assert.Equal(new Point(0.5, 0.5), box.RenderTransformOrigin);

        // Aynı ölçek nesnesi bütün düğümlerde paylaşılır (boyut graf genelinde tek).
        var other = (FrameworkElement)VisualTreeHelper.GetParent(view.NodeVisuals["OSYS.Api"].Icon);
        Assert.Same(scale, other.RenderTransform);
    }

    /// <summary>Panel yeniden boyutlanınca PAYLAŞILAN ikon ölçeği de yeni düğüm boyutunu izler — tek nesne
    /// mutasyona uğrar, düğüm başına iş yapılmaz.</summary>
    [StaFact]
    public void The_shared_icon_scale_follows_the_new_node_size_after_a_resize()
    {
        var view = Built(new Size(1200, 700));
        var box = (FrameworkElement)VisualTreeHelper.GetParent(view.NodeVisuals["OSYS.Base"].Icon);
        var scale = (ScaleTransform)box.RenderTransform;

        GraphTestView.Resize(view, new Size(320, 200));
        view.UpdateLayout();

        Assert.Equal(view.NodeSize * GraphView.IconFactor / 24.0, scale.ScaleX, 6);
    }

    /// <summary>discovered düğüm kesikli çerçeve taşır — WPF <c>Border</c> dash desteklemediği için
    /// <see cref="Rectangle"/>. Kesikli koleksiyon TEK, DONMUŞ ve PAYLAŞIMLIDIR (tick başına allocation yok).</summary>
    [StaFact]
    public void A_discovered_node_gets_a_dashed_frame_from_one_shared_frozen_collection()
    {
        var view = Built(new Size(640, 400));

        var discovered = view.NodeVisuals["OSYS.Legacy"].Square;
        Assert.NotEmpty(discovered.StrokeDashArray);
        Assert.True(discovered.StrokeDashArray.IsFrozen);
        Assert.Empty(view.NodeVisuals["OSYS.Base"].Square.StrokeDashArray);

        // İkinci bir discovered düğüm AYNI örneği paylaşır.
        view.UpdateStatuses(
        [
            new("OSYS.Base", 0, GraphStatus.Discovered),
            new("OSYS.Data", 1, GraphStatus.Failed),
            new("OSYS.Api", 2, GraphStatus.Queued),
            new("OSYS.Legacy", 2, GraphStatus.Discovered),
        ]);
        Assert.Same(discovered.StrokeDashArray, view.NodeVisuals["OSYS.Base"].Square.StrokeDashArray);
    }

    /// <summary>Renkler foundation token FIRÇALARINDAN çözülür — kodda hex YOK.</summary>
    [StaFact]
    public void Node_colours_are_resolved_from_the_foundation_token_brushes_not_hardcoded_hex()
    {
        var view = Built(new Size(640, 400));

        Assert.Same(view.FindResource("Brush.StatusSuccess"), view.NodeVisuals["OSYS.Base"].Square.Stroke);
        Assert.Same(view.FindResource("Brush.StatusSuccessSoft"), view.NodeVisuals["OSYS.Base"].Square.Fill);
        Assert.Same(view.FindResource("Brush.StatusFail"), view.NodeVisuals["OSYS.Data"].Square.Stroke);
        Assert.Same(view.FindResource("Brush.BorderStrong"), view.NodeVisuals["OSYS.Legacy"].Square.Stroke);
        Assert.Same(view.FindResource("Brush.FocusRing"), view.NodeVisuals["OSYS.Base"].SelectionRing.Stroke);
    }

    /// <summary>Seçim halkası + kalınlaşan çerçeve (§2.3: "2px <c>--focus-ring</c> outline, offset 2").</summary>
    [StaFact]
    public void Selecting_a_node_shows_its_focus_ring_and_thickens_the_square_border_to_2px()
    {
        var view = Built(new Size(640, 400));
        var visual = view.NodeVisuals["OSYS.Data"];
        Assert.Equal(Visibility.Collapsed, visual.SelectionRing.Visibility);

        view.SelectedNode = "OSYS.Data";

        Assert.Equal(Visibility.Visible, visual.SelectionRing.Visibility);
        Assert.Equal(GraphView.SelectedNodeBorderThickness, visual.Square.StrokeThickness, 6);
        Assert.Equal(view.NodeSize + GraphView.SelectionRingInset * 2, visual.SelectionRing.Width, 6);
    }

    /// <summary>Ad düğümün ÜSTÜNDE değil overlay tooltip'inde yaşar; düğümün kendisi hiçbir isim nesnesi
    /// KURMAZ — ne etiket ne native <see cref="ToolTip"/>. Tooltip'in davranışı
    /// <see cref="GraphHoverTests"/>'te; burada pinlenen, düğüm başına nesne kurulmadığıdır.</summary>
    [StaFact]
    public void A_node_builds_no_naming_objects_because_the_name_lives_in_the_shared_overlay()
    {
        var view = Built(new Size(640, 400));

        foreach (var visual in view.NodeVisuals.Values)
        {
            Assert.Null(visual.Body.ToolTip);
            Assert.DoesNotContain(DsResources.RealizedObjects(visual.Cell), o => o is ToolTip);
        }
    }

    // ---------------------------------------------------------------- [Task 5] döngü üyeliği — kalıcı köşe rozeti

    private static IReadOnlyList<GraphNode> CycleNodes(GraphStatus memberStatus) =>
    [
        new("OSYS.Member", 0, memberStatus, InCycle: true),
        new("OSYS.Other", 1, GraphStatus.Discovered),
    ];

    /// <summary>
    /// [DEĞİŞEN KURAL] <b>Eski davranış</b> (<c>GraphView.ApplyNodeStatus</c>'ın statü switch'i):
    /// <c>GraphStatus.Cycle</c> düğümü komple turuncu aileye boyanıyordu (<c>Brush.StatusCycle</c>/
    /// <c>…Soft</c>/<c>…Text</c> — DS <c>STATUS_META</c>'nın birebir karşılığı). <b>Değişme gerekçesi:</b> koşu
    /// başlar başlamaz satır Cycle'dan Building/Succeeded'e geçtiği an turuncu blok da graftan KAYBOLUYORDU —
    /// üyelik bilgisi koşu boyunca görünmez oluyordu. Ayrıca turuncu blok, düğümün o anki koşu statüsüyle (ör.
    /// building amber'ı) görsel olarak YARIŞIYORDU. <b>Yeni kural:</b> düğüm HER statüde standart (discovered)
    /// ailesinde durur; üyelik artık statüden BAĞIMSIZ, kalıcı bir köşe rozetiyle anlatılır (aşağıdaki test).
    /// </summary>
    [StaFact]
    public void A_cycle_member_node_keeps_the_standard_square_and_wears_a_corner_badge()
    {
        var view = GraphTestView.Realized(new Size(640, 400));
        view.SetGraph(CycleNodes(GraphStatus.Cycle), []);
        var visual = view.NodeVisuals["OSYS.Member"];

        // Standart (discovered) ailesi — turuncu DEĞİL: kesikli Brush.BorderStrong çerçeve.
        Assert.Same(view.FindResource("Brush.BorderStrong"), visual.Square.Stroke);
        Assert.Same(view.FindResource("Brush.SurfaceRaised"), visual.Square.Fill);
        Assert.NotEmpty(visual.Square.StrokeDashArray);

        // Üyelik kalıcı köşe rozetinde.
        Assert.NotNull(visual.CycleBadge);
        Assert.Equal(Visibility.Visible, visual.CycleBadge!.Visibility);

        // Cycle olmayan düğüm hiç rozet kurmaz.
        Assert.Null(view.NodeVisuals["OSYS.Other"].CycleBadge);
    }

    /// <summary>Rozet TALEP ÜZERİNE (bir kez) kurulur ve bir daha SÖKÜLMEZ — <see cref="GraphNodeVisual.Beads"/>
    /// ile AYNI desen. Statü koşu boyunca Cycle'dan uzaklaşsa bile (Building/Succeeded/Failed) üyelik değişmez,
    /// dolayısıyla rozet hep görünür kalır; cycle-dışı düğümde ise hiçbir zaman doğmaz.</summary>
    [StaFact]
    public void The_cycle_badge_survives_every_status_change_and_never_builds_for_non_members()
    {
        var view = GraphTestView.Realized(new Size(640, 400));
        view.SetGraph(CycleNodes(GraphStatus.Cycle), []);
        var badge = view.NodeVisuals["OSYS.Member"].CycleBadge;
        Assert.NotNull(badge);

        foreach (var status in new[] { GraphStatus.Building, GraphStatus.Succeeded, GraphStatus.Failed })
        {
            view.UpdateStatuses(CycleNodes(status));

            // Aynı nesne — yeniden kurulmuyor (talep-üzerine-bir-kez sözleşmesi).
            Assert.Same(badge, view.NodeVisuals["OSYS.Member"].CycleBadge);
            Assert.Equal(Visibility.Visible, view.NodeVisuals["OSYS.Member"].CycleBadge!.Visibility);
        }

        Assert.Null(view.NodeVisuals["OSYS.Other"].CycleBadge);
    }

    /// <summary>Realize testi (IconGeometryTests deseni): rozetin geometrisi Icons.xaml'deki
    /// <c>Icon.StatusCycle</c>'dan (16'lık viewBox, statü glyph ailesi) çözülür — kodda ikinci bir kopya path
    /// YOK. Ölçek, ana glyph'in <see cref="GraphView.IconFactor"/>'ından AYRI bir paylaşımlı transform'dur.</summary>
    [StaFact]
    public void The_cycle_badge_resolves_its_geometry_from_the_icon_dictionary()
    {
        var view = GraphTestView.Realized(new Size(640, 400));
        view.SetGraph(CycleNodes(GraphStatus.Cycle), []);
        var badge = view.NodeVisuals["OSYS.Member"].CycleBadge!;
        var expected = (Geometry)view.FindResource("Icon.StatusCycle");

        Assert.Same(expected, badge.Data);
        Assert.Same(view.FindResource("Brush.StatusCycleText"), badge.Stroke);

        var box = (FrameworkElement)VisualTreeHelper.GetParent(badge);
        var scale = Assert.IsType<ScaleTransform>(box.RenderTransform);
        Assert.Equal(view.NodeSize * GraphView.CycleBadgeFactor / 16.0, scale.ScaleX, 6);
    }

    /// <summary>
    /// Graf artık HER düğümü kurar — tembel materyalizasyon (cull) yok.
    ///
    /// <para><b>Eski iddia:</b> <c>A_large_graph_only_builds_the_visual_tree_of_the_nodes_the_camera_can_see</c>.
    /// v1.3.0 §2.3 grafı her panel boyutuna TAM SIĞDIRDIĞI için varsayılan görünümde her düğüm görünür
    /// alandadır; cull eleyecek bir şey bulamaz. Bu bir gevşetme değil: eskiden ekranda OLMAYAN düğüm
    /// çizilmiyordu, şimdi ekranda OLMAYAN düğüm yok.</para>
    /// </summary>
    [StaFact]
    public void A_thousand_node_graph_builds_every_node_because_the_whole_graph_is_on_screen()
    {
        var (nodes, edges) = SyntheticGraph.Build(1000, 8, 2.0);
        var view = GraphTestView.Realized(new Size(900, 520));

        view.SetGraph(nodes, edges);

        Assert.Equal(1000, view.NodeCount);
        Assert.Equal(1000, view.NodeVisuals.Count);
        // 1000 düğüm bu panelde ancak küçük bir pitch'le sığar — düğüm tavanın çok altına iner.
        Assert.True(view.NodeSize < QuietGraphLayout.MaxNodeSize, $"1000 düğümde node {view.NodeSize} px kaldı");
        Assert.Equal(
            Math.Clamp(view.Pitch * QuietGraphLayout.NodeSizeFactor,
                QuietGraphLayout.MinNodeSize, QuietGraphLayout.MaxNodeSize),
            view.NodeSize, 6);
    }
}
