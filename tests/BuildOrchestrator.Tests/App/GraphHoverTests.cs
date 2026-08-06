using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// design v1.3.0 §2.3 "Hover": node scale(1.7) (120ms ease-out), border 2px, opacity 1 (soluk moddayken
/// bile), z-index öne; tooltip GECİKMESİZ, TAM proje adıyla ve EKRAN koordinatında.
///
/// <para><b>Eski iddia (<c>GraphCullTests</c>, artık geçersiz):</b> ad yolu "etiketi düşen düğümde native
/// WPF <c>ToolTip</c>"ti — yani bir İSTİSNAYDI. v1.3.0 §2.3 node üstü etiketleri tamamen kaldırdı, böylece
/// tooltip ANA isim yolu oldu ve konumu native popup yerleşimine değil §2.3'ün 8px/6px kuralına uyuyor.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphHoverTests
{
    private const string LongName = "OSYS.Orchestration.Service.WorkOrder";

    private static IReadOnlyList<GraphNode> Nodes() =>
    [
        new(LongName, 0, GraphStatus.Queued),
        new("OSYS.Base", 1, GraphStatus.Queued),
    ];

    private static GraphView Built(bool animations = true)
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => animations);
        view.SetGraph(Nodes(), [new(LongName, "OSYS.Base")]);
        return view;
    }

    [StaFact]
    public void Hovering_a_node_scales_it_to_1_7_and_thickens_its_border_to_2px()
    {
        var view = Built(animations: false);
        var visual = view.NodeVisuals[LongName];

        view.SetHoverForTest(LongName);

        var scale = Assert.IsType<ScaleTransform>(visual.Body.RenderTransform);
        Assert.Equal(GraphView.HoverScale, scale.ScaleX, 6);
        Assert.Equal(GraphView.HoverScale, scale.ScaleY, 6);
        Assert.Equal(new Point(0.5, 0.5), visual.Body.RenderTransformOrigin);
        Assert.Equal(GraphView.HoverBorderThickness, visual.Square.StrokeThickness, 6);
        Assert.Equal(1, System.Windows.Controls.Panel.GetZIndex(visual.Cell)); // öne alındı

        view.SetHoverForTest(null);
        Assert.Equal(1.0, scale.ScaleX, 6);
        Assert.Equal(GraphView.NodeBorderThickness, visual.Square.StrokeThickness, 6);
        Assert.Equal(0, System.Windows.Controls.Panel.GetZIndex(visual.Cell));
    }

    /// <summary>Soluk moddayken bile hover opaklığı 1'e çeker (§2.3) — komşu düğüm sönük kalır.</summary>
    [StaFact]
    public void A_hovered_node_is_fully_opaque_even_while_the_run_has_faded_everything_else()
    {
        var view = Built(animations: false);
        view.RunPhase = GraphRunPhase.Running;
        Assert.Equal(GraphNodeOpacity.RunDim, view.NodeVisuals[LongName].Body.Opacity, 6);

        view.SetHoverForTest(LongName);

        Assert.Equal(1.0, view.NodeVisuals[LongName].Body.Opacity, 6);
        Assert.Equal(GraphNodeOpacity.RunDim, view.NodeVisuals["OSYS.Base"].Body.Opacity, 6);
    }

    /// <summary>Tooltip GECİKMESİZ görünür ve TAM adı taşır — kısaltma yok (§2.3).</summary>
    [StaFact]
    public void The_tooltip_appears_with_no_delay_and_carries_the_FULL_project_name()
    {
        var view = Built();
        Assert.Equal(Visibility.Collapsed, view.TooltipVisibility);

        view.SetHoverForTest(LongName);

        Assert.Equal(Visibility.Visible, view.TooltipVisibility);
        Assert.Equal(LongName, view.TooltipContent);
    }

    /// <summary>Tooltip TEK bir öğedir — düğüm başına kurulmaz (177 projede 177 Border olmaz).</summary>
    [StaFact]
    public void The_overlay_reuses_one_tooltip_element_instead_of_building_one_per_node()
    {
        var view = Built();
        view.SetHoverForTest(LongName);
        var first = view.TooltipElement;

        view.SetHoverForTest("OSYS.Base");

        Assert.Same(first, view.TooltipElement);
        Assert.Equal("OSYS.Base", view.TooltipContent);
    }

    /// <summary>
    /// AYIRT EDİCİ — RİSK: tooltip kamera transform'unun DIŞINDA yaşar. Zoom onu TAŞIR ama ÖLÇEKLEMEZ;
    /// katmanın kendisi hiçbir transform taşımaz.
    /// </summary>
    [StaFact]
    public void Zooming_moves_the_tooltip_but_never_scales_it()
    {
        var view = Built(animations: false);
        view.SetHoverForTest(LongName);
        var sizeBefore = view.TooltipBoxSize;
        var whereBefore = view.TooltipTopLeft;

        view.HandleWheel(new Point(430, 120), 120);

        Assert.Equal(sizeBefore, view.TooltipBoxSize);
        Assert.NotEqual(whereBefore, view.TooltipTopLeft);
        Assert.Null(view.OverlayLayerTransform);
    }

    /// <summary>Hover bırakılınca tooltip kaybolur ve opaklık koşu kararına döner.</summary>
    [StaFact]
    public void Leaving_the_node_hides_the_tooltip_and_returns_the_opacity_to_the_run_decision()
    {
        var view = Built(animations: false);
        view.RunPhase = GraphRunPhase.Running;
        view.SetHoverForTest(LongName);

        view.SetHoverForTest(null);

        Assert.Equal(Visibility.Collapsed, view.TooltipVisibility);
        Assert.Equal(GraphNodeOpacity.RunDim, view.NodeVisuals[LongName].Body.Opacity, 6);
    }

    /// <summary>§2.3: "Seçim değişince hover temizlenir (odak kayması sonrası imleç altında bayat hover
    /// kalmaz)."</summary>
    [StaFact]
    public void Changing_the_selection_clears_a_stale_hover()
    {
        var view = Built(animations: false);
        view.SetHoverForTest(LongName);
        Assert.Equal(Visibility.Visible, view.TooltipVisibility);

        view.SelectedNode = "OSYS.Base";

        Assert.Null(view.HoveredNode);
        Assert.Equal(Visibility.Collapsed, view.TooltipVisibility);
    }

    /// <summary>Seçili düğümün kalın çerçevesi hover bırakılınca İNCELMEZ — iki kural birbirini ezmez.</summary>
    [StaFact]
    public void Leaving_a_hovered_SELECTED_node_keeps_its_thick_border()
    {
        var view = Built(animations: false);
        view.SelectedNode = LongName;
        view.SetHoverForTest(LongName);

        view.SetHoverForTest(null);

        Assert.Equal(GraphView.SelectedNodeBorderThickness,
            view.NodeVisuals[LongName].Square.StrokeThickness, 6);
    }

    /// <summary>Ctor kablosu GERÇEK routed event'le: gövdeye giren fare hover'ı başlatır, çıkan bitirir.</summary>
    [StaFact]
    public void The_real_mouse_enter_and_leave_events_drive_the_hover()
    {
        var view = Built(animations: false);
        var body = view.NodeVisuals[LongName].Body;

        body.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        Assert.Equal(LongName, view.HoveredNode);

        body.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
        Assert.Null(view.HoveredNode);
    }

    /// <summary>[REALIZE TESTİ] Overlay katmanı YENİ bir XAML kökü parçasıdır — headless Measure/Arrange XAML
    /// runtime çözümlemesini GÖRMEZ. Gerçek bir pencerede, uygulamanın merge zinciriyle realize olmalı ve
    /// token'ları (surface-overlay / border-strong / radius-md / popover gölgesi) GERÇEKTEN çözmeli.</summary>
    [StaFact]
    public void The_overlay_realizes_in_a_real_window_with_its_tokens_resolved()
    {
        var host = DsResources.NewHost();
        var view = new GraphView { AnimationsEnabledProvider = () => false };
        var window = DsResources.Realize(host, view);
        view.SetGraph(Nodes(), [new(LongName, "OSYS.Base")]);
        view.UpdateLayout();

        view.SetHoverForTest(LongName);
        view.UpdateLayout();

        var box = view.TooltipElement;
        Assert.Equal(Visibility.Visible, box.Visibility);
        Assert.Same(view.FindResource("Brush.SurfaceOverlay"), box.Background);
        Assert.Same(view.FindResource("Brush.BorderStrong"), box.BorderBrush);
        Assert.Equal(view.FindResource("Radius.Md"), box.CornerRadius);
        Assert.NotNull(box.Effect);
        Assert.True(box.ActualWidth > 0, "tooltip realize olmadı (genişlik 0)");
        Assert.Null(view.OverlayLayerTransform);
        GC.KeepAlive(window);
    }
}
