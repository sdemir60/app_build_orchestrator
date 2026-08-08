using System.Windows;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// design v1.3.0 §2.3: "Tooltip ekran koordinatında konumlanır (zoom/pan transform'undan bağımsız, her
/// zoom'da net)" — prototype/app/BuildApp.jsx satır 468-485.
///
/// <para><b>Eski iddia (bu dosyanın tamamı için):</b> testler dikey konumu prototipin
/// <c>TooltipRisePerNode</c> (0.9) / <c>LabelDropPerNode</c> (0.95) katsayılarına, yani DÜĞÜM KENARININ
/// katlarına pinliyordu. <b>Değişme gerekçesi:</b> o katsayılar prototipin kendi düğümü için kalibreliydi —
/// orada seçili düğüm büyümez ve halkası CSS outline'dır. Bizim seçili düğümümüz hover ölçeğinde DURUR ve
/// halkası kareden taşar; sonuç, ad etiketinin halkanın içine düşmesiydi. Konum artık düğümün BOYANMIŞ
/// yarım yüksekliğinden hesaplanır ve bu değer çağırana (görünüm katmanına) aittir.</para>
/// </summary>
public class GraphOverlayTests
{
    private static readonly Size Panel = new(600, 400);
    private const double Inset = QuietGraphLayout.ContentInset;
    /// <summary>Düğümün ekranda kapladığı yarım yükseklik — testlerde sabit, gerçekte
    /// <c>GraphView.PaintedHalfExtent</c>.</summary>
    private const double HalfExtent = 12.0;

    /// <summary>Ankraj bir DÜNYA koordinatı değil, kameradan GEÇİRİLMİŞ ekran noktasıdır.</summary>
    [Fact]
    public void A_content_point_is_projected_through_the_camera_into_screen_space()
    {
        var screen = GraphOverlay.Project(new Point(100, 50), new CameraTransform(2.0, 40, -30));

        Assert.Equal((100 + Inset) * 2 + 40, screen.X, 6);
        Assert.Equal((50 + Inset) * 2 - 30, screen.Y, 6);
    }

    /// <summary>Tooltip düğümün BOYANMIŞ üst kenarının 8px üstündedir ve yatayda ona ortalanır.</summary>
    [Fact]
    public void The_tooltip_sits_8px_above_the_painted_edge_and_is_centred_on_the_node()
    {
        var camera = new CameraTransform(1.0, 0, 0);
        var box = new Size(120, 20);

        var topLeft = GraphOverlay.TooltipTopLeft(new Point(200, 150), camera, HalfExtent, Panel, box);

        var screen = GraphOverlay.Project(new Point(200, 150), camera);
        Assert.Equal(screen.X - box.Width / 2, topLeft.X, 6);
        Assert.Equal(screen.Y - HalfExtent - GraphOverlay.TooltipGapPx - box.Height, topLeft.Y, 6);
    }

    /// <summary>
    /// AYIRT EDİCİ — boşluk düğümün BOYANMIŞ ölçüsünden hesaplanır, düğüm kenarından değil. Vurgulu bir
    /// düğüm hem büyür hem halkasıyla taşar; kutu ikisinin de dışında durmalıdır.
    /// </summary>
    [Fact]
    public void A_bigger_painted_extent_pushes_the_box_further_out()
    {
        var camera = new CameraTransform(1.0, 0, 0);
        var box = new Size(120, 20);
        var node = new Point(200, 150);

        double small = GraphOverlay.TooltipTopLeft(node, camera, HalfExtent, Panel, box).Y;
        double big = GraphOverlay.TooltipTopLeft(node, camera, HalfExtent * 2, Panel, box).Y;

        Assert.Equal(small - HalfExtent, big, 6);
    }

    /// <summary>
    /// AYIRT EDİCİ — kutu düğüme ORTALI kalır; kelepçe ANKRAJA uygulanır, kutuya değil.
    ///
    /// <para><b>Eski iddia:</b> <c>A_node_at_the_edge_still_gets_a_fully_readable_tooltip_because_the_whole_box_is_clamped</c>
    /// kelepçeyi kutunun tamamına uyguluyordu; gerekçesi §2.3'ün "node kenardayken bile TAMAMEN okunur"
    /// cümlesiydi. <b>Değişme gerekçesi (ölçüm):</b> gerçek panelde proje adları uzundur — 500px'lik bir
    /// panelde 30 karakterlik bir ad ~215px'lik kutu demektir, dolayısıyla kenardaki HER düğümde tooltip
    /// düğümden onlarca piksel uzağa kayıyordu. Prototip yalnız ankrajı kelepçeler (JSX:470).</para>
    /// </summary>
    [Fact]
    public void A_node_near_the_edge_keeps_its_tooltip_centred_even_if_the_box_overflows()
    {
        var camera = new CameraTransform(1.0, 190, 0); // düğüm sağ kenara yakın
        var box = new Size(180, 20);

        var topLeft = GraphOverlay.TooltipTopLeft(new Point(300, 150), camera, HalfExtent, Panel, box);

        var screen = GraphOverlay.Project(new Point(300, 150), camera);
        Assert.True(screen.X < Panel.Width, "kurulum hatalı: düğüm panelin içinde olmalı");
        Assert.Equal(screen.X - box.Width / 2, topLeft.X, 6);
        Assert.True(topLeft.X + box.Width > Panel.Width, "kurulum hatalı: kutu taşmalıydı");
    }

    /// <summary>Ankraj panelin İÇ PAYINA kelepçelenir: düğüm (odak kipinde kamera yakınlaştığı için) panelin
    /// dışına çıksa bile etiket köşeye yapışmaz.</summary>
    [Fact]
    public void An_anchor_pushed_outside_the_panel_is_pulled_back_into_the_content_inset()
    {
        var box = new Size(80, 20);

        var left = GraphOverlay.TooltipTopLeft(new Point(300, 150), new CameraTransform(1, -900, 0), HalfExtent, Panel, box);
        Assert.Equal(Inset - box.Width / 2, left.X, 6);

        var right = GraphOverlay.TooltipTopLeft(new Point(300, 150), new CameraTransform(1, 900, 0), HalfExtent, Panel, box);
        Assert.Equal(Panel.Width - Inset - box.Width / 2, right.X, 6);
    }

    /// <summary>Seçim ad etiketi düğümün BOYANMIŞ alt kenarının 6px altındadır ve aynı ankraj kelepçesini
    /// paylaşır.</summary>
    [Fact]
    public void The_selection_name_label_sits_below_the_painted_edge_and_shares_the_anchor_clamp()
    {
        var camera = new CameraTransform(1.0, 0, 0);
        var box = new Size(120, 16);

        var topLeft = GraphOverlay.NameLabelTopLeft(new Point(200, 150), camera, HalfExtent, Panel, box);

        var screen = GraphOverlay.Project(new Point(200, 150), camera);
        Assert.Equal(screen.X - box.Width / 2, topLeft.X, 6);
        Assert.Equal(screen.Y + HalfExtent + GraphOverlay.LabelGapPx, topLeft.Y, 6);
    }

    /// <summary>
    /// AYIRT EDİCİ — en alttaki bant. Etiket aşağı sığmıyorsa düğümün ÜSTÜNE taklar; ASLA düğümün üstüne
    /// binmez.
    ///
    /// <para><b>Eski iddia:</b> <c>A_label_pushed_past_the_bottom_stops_at_the_content_inset</c> kutuyu
    /// panelin iç payına kelepçeliyordu. <b>Değişme gerekçesi:</b> kelepçe kutuyu düğüme doğru geri çekiyor
    /// ve gözle görüldüğü gibi düğümle ÜST ÜSTE bindiriyordu (ekran görüntüsü: en alt banttaki düğümün adı
    /// düğümün içinde). Kelepçe yerine takla: kutu okunur kalır ve sahibini göstermeye devam eder.</para>
    /// </summary>
    [Fact]
    public void A_label_that_cannot_fit_below_flips_above_the_node_instead_of_overlapping_it()
    {
        var box = new Size(120, 16);
        var camera = new CameraTransform(1, 0, 190); // düğüm panelin dibine yakın
        var node = new Point(200, 150);
        var screen = GraphOverlay.Project(node, camera);

        var topLeft = GraphOverlay.NameLabelTopLeft(node, camera, HalfExtent, Panel, box);

        Assert.True(screen.Y + HalfExtent + GraphOverlay.LabelGapPx + box.Height > Panel.Height - Inset,
            "kurulum hatalı: etiket aşağıya sığmamalıydı");
        Assert.Equal(screen.Y - HalfExtent - GraphOverlay.LabelGapPx - box.Height, topLeft.Y, 6);
        Assert.True(topLeft.Y + box.Height <= screen.Y - HalfExtent, "etiket düğümün üstüne biniyor");
    }

    /// <summary>Simetrik kural: tooltip yukarı sığmıyorsa (en üstteki bant) düğümün ALTINA taklar.</summary>
    [Fact]
    public void A_tooltip_that_cannot_fit_above_flips_below_the_node()
    {
        var box = new Size(120, 20);
        var camera = new CameraTransform(1, 0, -170); // düğüm panelin tepesine yakın
        var node = new Point(200, 150);
        var screen = GraphOverlay.Project(node, camera);

        var topLeft = GraphOverlay.TooltipTopLeft(node, camera, HalfExtent, Panel, box);

        Assert.Equal(screen.Y + HalfExtent + GraphOverlay.TooltipGapPx, topLeft.Y, 6);
    }

    /// <summary>İki taraf da sığmazsa (kutu panelden büyük) son çare kelepçedir — kutu iç payın dışına
    /// çıkmaz.</summary>
    [Fact]
    public void A_box_that_fits_on_neither_side_falls_back_to_the_inset_clamp()
    {
        var tiny = new Size(200, 120);
        var box = new Size(80, 60);

        var topLeft = GraphOverlay.NameLabelTopLeft(
            new Point(0, 0), new CameraTransform(1, 0, 0), HalfExtent, tiny, box);

        Assert.InRange(topLeft.Y, Inset - 0.001, Math.Max(Inset, tiny.Height - Inset - box.Height) + 0.001);
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
