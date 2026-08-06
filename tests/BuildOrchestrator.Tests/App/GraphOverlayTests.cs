using System.Windows;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// design v1.3.0 §2.3: "Tooltip ekran koordinatında konumlanır (zoom/pan transform'undan bağımsız, her
/// zoom'da net)" ve "Yatayda panel kenarına kelepçeli (6px) — node kenardayken bile tamamen okunur"
/// (prototype/app/BuildApp.jsx satır 468-485).
/// </summary>
public class GraphOverlayTests
{
    private static readonly Size Panel = new(600, 400);
    private const double Inset = QuietGraphLayout.ContentInset;

    /// <summary>Ankraj bir DÜNYA koordinatı değil, kameradan GEÇİRİLMİŞ ekran noktasıdır.</summary>
    [Fact]
    public void A_content_point_is_projected_through_the_camera_into_screen_space()
    {
        var screen = GraphOverlay.Project(new Point(100, 50), new CameraTransform(2.0, 40, -30));

        Assert.Equal((100 + Inset) * 2 + 40, screen.X, 6);
        Assert.Equal((50 + Inset) * 2 - 30, screen.Y, 6);
    }

    /// <summary>Tooltip düğümün 8px ÜSTÜNDEDİR ve yatayda ortalanır (JSX:470-471).</summary>
    [Fact]
    public void The_tooltip_sits_8px_above_the_node_and_is_horizontally_centred_on_it()
    {
        var camera = new CameraTransform(1.0, 0, 0);
        var box = new Size(120, 20);

        var topLeft = GraphOverlay.TooltipTopLeft(new Point(300, 200), camera, nodeSize: 12, Panel, box);

        var screen = GraphOverlay.Project(new Point(300, 200), camera);
        Assert.Equal(screen.X - box.Width / 2, topLeft.X, 6);
        Assert.Equal(
            screen.Y - 12 * GraphOverlay.TooltipRisePerNode - GraphOverlay.TooltipGapPx - box.Height,
            topLeft.Y, 6);
    }

    /// <summary>Yükseklik ÖLÇEKLE büyür — 5× zoom'da düğüm de 5× büyüktür, tooltip ona değmemeli.</summary>
    [Fact]
    public void The_gap_above_the_node_scales_with_the_zoom_because_the_node_does_too()
    {
        var box = new Size(80, 18);
        double atOne = GraphOverlay.TooltipTopLeft(new Point(300, 200), new CameraTransform(1, 0, 0), 12, Panel, box).Y;
        var camera = new CameraTransform(3, 0, 0);
        double atThree = GraphOverlay.TooltipTopLeft(new Point(300, 200), camera, 12, Panel, box).Y;

        var screen = GraphOverlay.Project(new Point(300, 200), camera);
        Assert.Equal(screen.Y - 12 * GraphOverlay.TooltipRisePerNode * 3 - GraphOverlay.TooltipGapPx - box.Height,
            atThree, 6);
        Assert.NotEqual(atOne, atThree);
    }

    /// <summary>
    /// AYIRT EDİCİ — §2.3 "node kenardayken bile TAMAMEN okunur": kelepçe KUTUNUN TAMAMINA uygulanır.
    /// Yalnız ankrajı kelepçeleyen bir uygulama (prototipin tooltip dalı) uzun bir adı panelin dışına
    /// yarı yarıya taşırır ve bu testi düşürür.
    /// </summary>
    [Fact]
    public void A_node_at_the_edge_still_gets_a_fully_readable_tooltip_because_the_whole_box_is_clamped()
    {
        var camera = new CameraTransform(1.0, -400, 0); // düğüm sol kenarın çok dışına itildi
        var box = new Size(180, 20);

        var left = GraphOverlay.TooltipTopLeft(new Point(300, 200), camera, 12, Panel, box);
        Assert.Equal(GraphOverlay.EdgeClampPx, left.X, 6);

        var rightCamera = new CameraTransform(1.0, 900, 0);
        var right = GraphOverlay.TooltipTopLeft(new Point(300, 200), rightCamera, 12, Panel, box);
        Assert.Equal(Panel.Width - box.Width - GraphOverlay.EdgeClampPx, right.X, 6);
        Assert.True(right.X + box.Width <= Panel.Width, "tooltip panelin sağından taşıyor");
    }

    /// <summary>Panel kutudan darsa kelepçe sol kenara oturur — iki yandan birden taşırmaktan iyidir.</summary>
    [Fact]
    public void A_panel_narrower_than_the_box_pins_it_to_the_left_edge()
    {
        var topLeft = GraphOverlay.TooltipTopLeft(
            new Point(50, 50), new CameraTransform(1, 0, 0), 12, new Size(100, 200), new Size(240, 20));

        Assert.Equal(GraphOverlay.EdgeClampPx, topLeft.X, 6);
    }

    /// <summary>Seçim ad etiketi düğümün 6px ALTINDADIR ve aynı yatay kelepçeyi paylaşır (§2.3).</summary>
    [Fact]
    public void The_selection_name_label_sits_below_the_node_and_shares_the_horizontal_clamp()
    {
        var camera = new CameraTransform(1.0, 0, 0);
        var box = new Size(120, 16);

        var topLeft = GraphOverlay.NameLabelTopLeft(new Point(300, 200), camera, nodeSize: 12, Panel, box);

        var screen = GraphOverlay.Project(new Point(300, 200), camera);
        Assert.Equal(screen.X - box.Width / 2, topLeft.X, 6);
        Assert.Equal(screen.Y + 12 * GraphOverlay.LabelDropPerNode + GraphOverlay.LabelGapPx, topLeft.Y, 6);
    }

    /// <summary>Ad etiketi dikeyde de panelden taşmaz: en altta 26px'lik ipucu bandı korunur (JSX:481).</summary>
    [Fact]
    public void The_name_label_never_falls_below_the_hint_band_at_the_bottom_of_the_panel()
    {
        var camera = new CameraTransform(1.0, 0, 380); // düğüm panelin altına itildi

        var topLeft = GraphOverlay.NameLabelTopLeft(new Point(300, 200), camera, 12, Panel, new Size(120, 16));

        Assert.Equal(Panel.Height - GraphOverlay.LabelBottomReservePx, topLeft.Y, 6);
    }

    /// <summary>§2.3'ün sayıları — birinin sessizce kayması bu testi düşürür.</summary>
    [Fact]
    public void The_overlay_numbers_are_pinned_to_their_spec_values()
    {
        Assert.Equal(8.0, GraphOverlay.TooltipGapPx, 6);
        Assert.Equal(6.0, GraphOverlay.EdgeClampPx, 6);
        Assert.Equal(6.0, GraphOverlay.LabelGapPx, 6);
    }
}
