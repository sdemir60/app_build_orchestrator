using System.Windows;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// design v1.3.0 §2.3: "Tooltip ekran koordinatında konumlanır (zoom/pan transform'undan bağımsız, her
/// zoom'da net)" — prototype/app/BuildApp.jsx satır 468-485.
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
        // Düğüm 3× zoom'da bile panelin içinde kalsın — aksi halde dikey kelepçe devreye girer ve ölçüm
        // "boşluk ölçekle büyüyor mu" sorusunu değil kelepçeyi ölçerdi.
        var node = new Point(60, 60);
        double atOne = GraphOverlay.TooltipTopLeft(node, new CameraTransform(1, 0, 0), 12, Panel, box).Y;
        var camera = new CameraTransform(3, 0, 0);
        double atThree = GraphOverlay.TooltipTopLeft(node, camera, 12, Panel, box).Y;

        var screen = GraphOverlay.Project(node, camera);
        Assert.Equal(screen.Y - 12 * GraphOverlay.TooltipRisePerNode * 3 - GraphOverlay.TooltipGapPx - box.Height,
            atThree, 6);
        Assert.NotEqual(atOne, atThree);
    }

    /// <summary>
    /// AYIRT EDİCİ — kutu düğüme ORTALI kalır; kelepçe ANKRAJA uygulanır, kutuya değil.
    ///
    /// <para><b>Eski iddia:</b> <c>A_node_at_the_edge_still_gets_a_fully_readable_tooltip_because_the_whole_box_is_clamped</c>
    /// kelepçeyi kutunun tamamına uyguluyordu ve gerekçesi §2.3'ün "node kenardayken bile TAMAMEN okunur"
    /// cümlesiydi. <b>Değişme gerekçesi (ölçüm):</b> gerçek panelde proje adları uzundur — 500px'lik bir
    /// panelde 30 karakterlik bir ad ~215px'lik kutu demektir, dolayısıyla kenardaki HER düğümde tooltip
    /// düğümden onlarca piksel uzağa kayıyor ve hangi düğüme ait olduğu okunamıyordu ("sağda solda saçma
    /// çıkıyor"). Prototip yalnız ankrajı kelepçeler (JSX:470); ortalı durmak, kenarda kırpılmaktan önce
    /// gelir.</para>
    /// </summary>
    [Fact]
    public void A_node_near_the_edge_keeps_its_tooltip_centred_even_if_the_box_overflows()
    {
        var camera = new CameraTransform(1.0, 190, 0); // düğüm sağ kenara yakın
        var box = new Size(180, 20);

        var topLeft = GraphOverlay.TooltipTopLeft(new Point(300, 200), camera, 12, Panel, box);

        var screen = GraphOverlay.Project(new Point(300, 200), camera);
        Assert.True(screen.X < Panel.Width, "kurulum hatalı: düğüm panelin içinde olmalı");
        Assert.Equal(screen.X - box.Width / 2, topLeft.X, 6);
        Assert.True(topLeft.X + box.Width > Panel.Width, "kurulum hatalı: kutu taşmalıydı");
    }

    /// <summary>
    /// Ankraj panelin İÇ PAYINA kelepçelenir: düğüm (odak kipinde kamera yakınlaştığı için) panelin dışına
    /// çıksa bile etiket köşeye yapışmaz, grafın nefes payının içinde durur.
    /// </summary>
    [Fact]
    public void An_anchor_pushed_outside_the_panel_is_pulled_back_into_the_content_inset()
    {
        var box = new Size(80, 20);

        var left = GraphOverlay.TooltipTopLeft(new Point(300, 200), new CameraTransform(1, -900, 0), 12, Panel, box);
        Assert.Equal(Inset - box.Width / 2, left.X, 6);

        var right = GraphOverlay.TooltipTopLeft(new Point(300, 200), new CameraTransform(1, 900, 0), 12, Panel, box);
        Assert.Equal(Panel.Width - Inset - box.Width / 2, right.X, 6);
    }

    /// <summary>Seçim ad etiketi düğümün 6px ALTINDADIR ve aynı ankraj kelepçesini paylaşır (§2.3).</summary>
    [Fact]
    public void The_selection_name_label_sits_below_the_node_and_shares_the_anchor_clamp()
    {
        var camera = new CameraTransform(1.0, 0, 0);
        var box = new Size(120, 16);

        var topLeft = GraphOverlay.NameLabelTopLeft(new Point(300, 200), camera, nodeSize: 12, Panel, box);

        var screen = GraphOverlay.Project(new Point(300, 200), camera);
        Assert.Equal(screen.X - box.Width / 2, topLeft.X, 6);
        Assert.Equal(screen.Y + 12 * GraphOverlay.LabelDropPerNode + GraphOverlay.LabelGapPx, topLeft.Y, 6);
    }

    /// <summary>
    /// Dikeyde kelepçe kutunun TAMAMINA uygulanır — yatayın aksine, dikeyde kaydırmak ortalamayı bozmaz.
    /// Kutu panelin iç payının dışına çıkmaz.
    ///
    /// <para><b>Eski iddia:</b> <c>The_name_label_never_falls_below_the_hint_band_at_the_bottom_of_the_panel</c>
    /// alt sınırı 26px'lik <c>LabelBottomReservePx</c>'e (sağ alttaki mono ipucu satırının bandı)
    /// pinliyordu. O satır kaldırıldığı için bandın dayanağı da kalmadı; sınır artık grafın kendi iç
    /// payıdır ve kutu YÜKSEKLİĞİNİ de hesaba katar.</para>
    /// </summary>
    [Fact]
    public void A_label_pushed_past_the_bottom_stops_at_the_content_inset()
    {
        var box = new Size(120, 16);

        var low = GraphOverlay.NameLabelTopLeft(new Point(300, 200), new CameraTransform(1, 0, 380), 12, Panel, box);
        Assert.Equal(Panel.Height - Inset - box.Height, low.Y, 6);

        var high = GraphOverlay.NameLabelTopLeft(new Point(300, 200), new CameraTransform(1, 0, -380), 12, Panel, box);
        Assert.Equal(Inset, high.Y, 6);
    }

    /// <summary>§2.3'ün sayıları — birinin sessizce kayması bu testi düşürür. Kelepçe payı AYRI bir sayı
    /// değildir: grafın kendi iç payıdır (kopya YASAK).</summary>
    [Fact]
    public void The_overlay_numbers_are_pinned_to_their_spec_values()
    {
        Assert.Equal(8.0, GraphOverlay.TooltipGapPx, 6);
        Assert.Equal(6.0, GraphOverlay.LabelGapPx, 6);
        Assert.Equal(QuietGraphLayout.ContentInset, Inset, 6);
    }
}
