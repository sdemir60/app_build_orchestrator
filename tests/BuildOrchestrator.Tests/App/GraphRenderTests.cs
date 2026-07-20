using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App.Graph;
using IoPath = System.IO.Path;
using ShapePath = System.Windows.Shapes.Path;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T63] <see cref="GraphView"/> Shapes yolu (≤~150 düğüm) — design-v1 §2.3 düğüm/kenar/seçim/rozet/stagger
/// render'ı. Saf aritmetik <see cref="GraphLayout"/>/<see cref="GraphCamera"/>/<see cref="EdgeStyleResolver"/>'da
/// (ayrı testler); burada YALNIZ WPF kablajı doğrulanır.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphRenderTests
{
    // 3 katman: Base → Data.Core → (Server.Api, Web.Portal). Bütün dalları (building/failed/depIssue) sergiler.
    private static IReadOnlyList<GraphNode> Nodes(
        GraphStatus baseStatus = GraphStatus.Discovered,
        GraphStatus dataStatus = GraphStatus.Discovered,
        GraphStatus apiStatus = GraphStatus.Discovered,
        GraphStatus portalStatus = GraphStatus.Discovered,
        bool portalDepIssue = false) =>
    [
        new("OSYS.Base", 0, baseStatus),
        new("OSYS.Data.Core", 1, dataStatus),
        new("OSYS.Server.Api", 2, apiStatus),
        new("OSYS.Web.Portal", 2, portalStatus, HasDepIssue: portalDepIssue),
    ];

    private static IReadOnlyList<GraphEdge> Edges() =>
    [
        new("OSYS.Base", "OSYS.Data.Core"),
        new("OSYS.Data.Core", "OSYS.Server.Api"),
        new("OSYS.Data.Core", "OSYS.Web.Portal"),
    ];

    private static GraphView NewView(bool animationsEnabled, double width = 600, double height = 400)
    {
        var view = new GraphView { AnimationsEnabledProvider = () => animationsEnabled };
        // pack:// / Application.Resources olmadan (headless host) token'lar çözülmez — Tokens/Motion sözlükleri
        // dosyadan merge edilir (FontAssetTests/TokenBrushesTests ile AYNI TestAssets deseni). Böylece
        // SetResourceReference ile bağlanan fırçalar ve Duration/KeySpline token'ları gerçekten çözülür.
        foreach (string name in new[] { "Tokens.xaml", "Motion.xaml" })
        {
            using var stream = File.OpenRead(IoPath.Combine(AppContext.BaseDirectory, "TestAssets", "Resources", name));
            view.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
        }
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        return view;
    }

    // ---------------------------------------------------------------- düğüm (26px, 4px radius KARE)

    [StaFact]
    public void A_node_is_a_26px_square_with_a_4px_corner_radius()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());

        var square = view.NodeVisuals["OSYS.Base"].Square;
        Assert.Equal(26.0, square.Width);
        Assert.Equal(26.0, square.Height);
        Assert.Equal(4.0, square.RadiusX);
        Assert.Equal(4.0, square.RadiusY);
    }

    [StaFact]
    public void A_discovered_node_gets_a_dashed_frame_wpf_border_cannot_dash_so_it_is_a_rectangle()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(baseStatus: GraphStatus.Discovered, dataStatus: GraphStatus.Succeeded), Edges());

        Assert.NotEmpty(view.NodeVisuals["OSYS.Base"].Square.StrokeDashArray);
        Assert.Empty(view.NodeVisuals["OSYS.Data.Core"].Square.StrokeDashArray);
    }

    [StaFact]
    public void The_node_label_is_the_short_name_in_10px_mono_with_a_LOCAL_Ideal_formatting_mode()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());

        var label = view.NodeVisuals["OSYS.Server.Api"].Label;
        Assert.Equal("Server.Api", label.Text);
        Assert.Equal(10.0, label.FontSize);
        // feasibility §3.4/§4.4: Display, scale transform altında BOZULUR — graf etiketlerinde LOKAL Ideal override
        // (kök MainWindow Display'i DEĞİŞMEZ, T65).
        Assert.Equal(TextFormattingMode.Ideal, TextOptions.GetTextFormattingMode(label));
        // DS: etiket text-dim, seçiliyken text-primary (varsayılan siyah Foreground'u miras almaz).
        Assert.Equal(view.TryFindResource("Brush.TextDim"), label.Foreground);
        view.SelectedNode = "OSYS.Server.Api";
        Assert.Equal(view.TryFindResource("Brush.TextPrimary"), label.Foreground);
    }

    [StaFact]
    public void Selecting_a_node_shows_its_amber_ring()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());
        Assert.Equal(Visibility.Collapsed, view.NodeVisuals["OSYS.Base"].SelectionRing.Visibility);

        view.SelectedNode = "OSYS.Base";

        Assert.Equal(Visibility.Visible, view.NodeVisuals["OSYS.Base"].SelectionRing.Visibility);
        Assert.Equal(Visibility.Collapsed, view.NodeVisuals["OSYS.Data.Core"].SelectionRing.Visibility);
    }

    // ---------------------------------------------------------------- dep-hata rozeti

    [StaFact]
    public void A_dep_issue_node_gets_a_13px_circle_badge_holding_a_filled_red_triangle()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(portalDepIssue: true), Edges());

        var withBadge = view.NodeVisuals["OSYS.Web.Portal"];
        Assert.Equal(Visibility.Visible, withBadge.Badge.Visibility);
        Assert.Equal(13.0, withBadge.Badge.Width);
        Assert.Equal(13.0, withBadge.Badge.Height);
        // 13px daire: zemin surface-base, 1px kırmızı border
        Assert.Equal(view.TryFindResource("Brush.SurfaceBase"), withBadge.BadgeCircle.Fill);
        Assert.Equal(view.TryFindResource("Brush.StatusFailBorder"), withBadge.BadgeCircle.Stroke);
        Assert.Equal(1.0, withBadge.BadgeCircle.StrokeThickness);
        // İçinde DOLU kırmızı üçgen ▲ (stroke YOK — dolu)
        Assert.Equal(view.TryFindResource("Brush.StatusFailText"), withBadge.BadgeTriangle.Fill);
        Assert.Null(withBadge.BadgeTriangle.Stroke);
        Assert.False(withBadge.BadgeTriangle.Data.IsEmpty());

        Assert.Equal(Visibility.Collapsed, view.NodeVisuals["OSYS.Server.Api"].Badge.Visibility);
    }

    [StaFact]
    public void Node_and_edge_colours_are_resolved_from_the_foundation_token_brushes_not_hardcoded_hex()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(baseStatus: GraphStatus.Failed, dataStatus: GraphStatus.Building), Edges());

        var failed = view.NodeVisuals["OSYS.Base"];
        Assert.Equal(view.TryFindResource("Brush.StatusFail"), failed.Square.Stroke);
        Assert.Equal(view.TryFindResource("Brush.StatusFailSoft"), failed.Square.Fill);
        Assert.Equal(view.TryFindResource("Brush.StatusFailText"), failed.Icon.Stroke);

        // Base failed ⇒ Base→Data.Core hata dalıdır; hedefi building olduğu için AKAR ama kırmızı çizilir.
        var edge = view.EdgeVisuals.Single(e => e.Model.From == "OSYS.Base");
        Assert.Equal(view.TryFindResource("Brush.StatusFailBorder"), edge.Path.Stroke);
        Assert.True(edge.Style!.IsFlowing);
    }

    // ---------------------------------------------------------------- ilk açılış: katman stagger'ı

    [Fact]
    public void The_layer_stagger_is_55ms_per_layer_capped_at_330ms()
    {
        Assert.Equal(0.0, GraphView.RevealDelayMs(0));
        Assert.Equal(55.0, GraphView.RevealDelayMs(1));
        Assert.Equal(275.0, GraphView.RevealDelayMs(5));
        Assert.Equal(330.0, GraphView.RevealDelayMs(6));
        Assert.Equal(330.0, GraphView.RevealDelayMs(20)); // tavan
    }

    [StaFact]
    public void Nodes_start_fully_transparent_so_the_staggered_reveal_never_flashes()
    {
        var view = NewView(true);
        view.SetGraph(Nodes(), Edges());

        // CSS `both` fill paritesi (feasibility §3.4): gecikme boyunca opaklık 0 tutulur — flash YOK.
        Assert.All(view.NodeVisuals.Values, v => Assert.Equal(0.0, v.Cell.Opacity));
        Assert.NotNull(view.NodeVisuals["OSYS.Base"].Cell.RenderTransform); // 5px yukarıdan gelir
    }

    [StaFact]
    public void Reduced_motion_places_the_nodes_instantly_with_no_stagger()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());

        Assert.All(view.NodeVisuals.Values, v => Assert.Equal(1.0, v.Cell.Opacity));
    }

    // ---------------------------------------------------------------- akan dash — TEK paylaşımlı clock

    [StaFact]
    public void Flowing_edges_are_UIElement_paths_bound_to_one_single_shared_dash_clock()
    {
        var view = NewView(true);
        // İki kenar birden akar: Data.Core→Server.Api ve Data.Core→Web.Portal (ikisi de building).
        view.SetGraph(Nodes(apiStatus: GraphStatus.Building, portalStatus: GraphStatus.Building), Edges());

        Assert.Equal(2, view.FlowingEdgePaths.Count);
        Assert.All(view.FlowingEdgePaths, p => Assert.IsType<ShapePath>(p)); // DrawingContext Pen.DashStyle.Offset GÜVENİLMEZ (A13.2)
        Assert.All(view.FlowingEdgePaths, p => Assert.Equal([4.0, 7.0], p.StrokeDashArray));
        var clock = view.SharedDashClock;
        Assert.NotNull(clock);

        // Akan küme değişse bile clock AYNI nesnedir — tüm akan kenarlar tek clock'ta faz-kilitli kalır.
        view.UpdateStatuses(Nodes(dataStatus: GraphStatus.Building, apiStatus: GraphStatus.Building));
        Assert.Same(clock, view.SharedDashClock);
        Assert.Equal(2, view.FlowingEdgePaths.Count);
    }

    [StaFact]
    public void The_shared_dash_animation_loops_two_full_periods_at_30fps()
    {
        var view = NewView(true);
        view.SetGraph(Nodes(apiStatus: GraphStatus.Building), Edges());

        var timeline = Assert.IsType<System.Windows.Media.Animation.DoubleAnimation>(view.SharedDashClock!.Timeline);
        Assert.Equal(-22.0, timeline.To);
        Assert.Equal(TimeSpan.FromMilliseconds(900), timeline.Duration.TimeSpan);
        Assert.Equal(System.Windows.Media.Animation.RepeatBehavior.Forever, timeline.RepeatBehavior);
        // Dekoratif sonsuz animasyon → 30fps tavanı (feasibility §3.4).
        Assert.Equal(30, System.Windows.Media.Animation.Timeline.GetDesiredFrameRate(timeline));
    }

    [StaFact]
    public void Reduced_motion_keeps_the_dash_but_never_starts_a_clock()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(apiStatus: GraphStatus.Building), Edges());

        Assert.Null(view.SharedDashClock);
        var flowing = Assert.Single(view.FlowingEdgePaths);
        Assert.Equal([4.0, 7.0], flowing.StrokeDashArray); // statik kesikli
        Assert.Equal(0.0, flowing.StrokeDashOffset);
    }

    // ---------------------------------------------------------------- seçim sönmesi

    [StaFact]
    public void Selection_dims_every_non_neighbour_node_to_25_percent_and_untouched_edges_to_16_percent()
    {
        var view = NewView(false); // reduced-motion: sönme ANINDA uygulanır (deterministik)
        view.SetGraph(Nodes(), Edges());

        view.SelectedNode = "OSYS.Server.Api";

        Assert.Equal(1.0, view.NodeVisuals["OSYS.Server.Api"].Body.Opacity); // seçili
        Assert.Equal(1.0, view.NodeVisuals["OSYS.Data.Core"].Body.Opacity);  // komşu
        Assert.Equal(0.25, view.NodeVisuals["OSYS.Base"].Body.Opacity);      // uzak
        Assert.Equal(0.25, view.NodeVisuals["OSYS.Web.Portal"].Body.Opacity);

        var untouched = view.EdgeVisuals.Single(e => e.Model.From == "OSYS.Base");
        Assert.Equal(0.16, untouched.Path.Opacity);
        var touching = view.EdgeVisuals.Single(e => e.Model.To == "OSYS.Server.Api");
        Assert.Equal(1.0, touching.Path.Opacity);
        Assert.Equal(1.6, touching.Path.StrokeThickness);
    }

    [StaFact]
    public void Clearing_the_selection_restores_every_node_and_edge()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());
        view.SelectedNode = "OSYS.Server.Api";

        view.SelectedNode = null;

        Assert.All(view.NodeVisuals.Values, v => Assert.Equal(1.0, v.Body.Opacity));
        Assert.All(view.EdgeVisuals, e => Assert.Equal(0.8, e.Path.Opacity));
    }

    // ---------------------------------------------------------------- panel başlığı + boş durum

    [StaFact]
    public void The_panel_header_counts_projects_and_dependencies_from_the_data()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());

        Assert.Equal("4 projects · 3 dependencies", view.HeaderCountsText);
    }

    [StaFact]
    public void Before_sync_the_ground_shows_the_dashed_empty_state_box()
    {
        var view = NewView(false);

        Assert.True(view.IsEmptyStateVisible);
        Assert.Equal("Graph appears after Sync", view.EmptyStateText);

        view.SetGraph(Nodes(), Edges());
        Assert.False(view.IsEmptyStateVisible);
    }

    // ---------------------------------------------------------------- kamera

    [StaFact]
    public void The_camera_uses_a_scale_plus_translate_transform_group_and_targets_the_selected_node()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());

        var group = Assert.IsType<TransformGroup>(view.World.RenderTransform);
        Assert.IsType<ScaleTransform>(group.Children[0]);   // CSS `translate(...) scale(...)` = önce ölçek
        Assert.IsType<TranslateTransform>(group.Children[1]);
        Assert.Equal(new Point(0, 0), view.World.RenderTransformOrigin);

        view.SelectedNode = "OSYS.Web.Portal";

        var expected = GraphCamera.Compute(view.ViewportSize, view.GraphSize, view.NodeCenter("OSYS.Web.Portal"));
        Assert.Equal(expected, view.CurrentCamera);
    }

    [StaFact]
    public void Reduced_motion_snaps_the_camera_with_no_animation()
    {
        var view = NewView(false);
        view.SetGraph(Nodes(), Edges());

        view.SelectedNode = "OSYS.Web.Portal";

        Assert.False(view.LastCameraAnimated);
        Assert.Equal(view.CurrentCamera.Scale, ((ScaleTransform)((TransformGroup)view.World.RenderTransform).Children[0]).ScaleX);
        Assert.Equal(view.CurrentCamera.Tx, ((TranslateTransform)((TransformGroup)view.World.RenderTransform).Children[1]).X);
    }

    [StaFact]
    public void With_motion_enabled_the_camera_animates_over_460ms()
    {
        // Dar panel: graf yatayda sığmaz ⇒ seçim GERÇEKTEN yeni bir tx üretir (sığdığında kamera zaten sabittir).
        var view = NewView(true, width: 400, height: 300);
        view.SetGraph(Nodes(), Edges());

        view.SelectedNode = "OSYS.Web.Portal";

        Assert.True(view.LastCameraAnimated);
        Assert.Equal(460.0, GraphCamera.TransitionMs);
    }

    [StaFact]
    public void An_unchanged_camera_target_does_not_restart_the_460ms_transition()
    {
        var view = NewView(true, width: 400, height: 300);
        view.SetGraph(Nodes(), Edges());
        view.SelectedNode = "OSYS.Web.Portal";
        Assert.True(view.LastCameraAnimated);
        var applied = view.CurrentCamera;

        // Statü güncellemesi kamerayı DEĞİŞTİRMEZ (seçim aynı düğümde) — uçuştaki geçiş yeniden doğmamalı.
        view.UpdateStatuses(Nodes(apiStatus: GraphStatus.Building));

        Assert.Equal(applied, view.CurrentCamera);
    }
}
