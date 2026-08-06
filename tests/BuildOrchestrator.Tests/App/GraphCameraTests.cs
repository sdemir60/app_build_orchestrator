using System.Windows;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// design v1.3.0 §2.3 kamerasının SAF aritmetiği — prototype/app/BuildApp.jsx <c>GraphPanel</c>
/// (satır 322-356, 398-399) portu.
///
/// <para><b>Eski iddialar (artık geçersiz):</b> bu dosya bir "sinema kamerası" pinliyordu — grafı panele
/// sığdıran <c>FitScale</c> (0.68–1.08), building frontier'ini izleyen <c>ResolveFocus</c>/<c>FrontierScale</c>
/// (0.85–1.4 takip bandı), 8px/0.05'lik Zeno eşikleri ve 12px kenar paylı <c>ClampPan</c>. v1.3.0 §2.3
/// hepsini kaldırdı:</para>
/// <list type="bullet">
///   <item><b>Sığdırma kameranın işi değil.</b> Yerleşimin kendisi panele göre hesaplanıyor
///     (<see cref="QuietGraphLayout"/>) ⇒ varsayılan görünüm ölçek 1, öteleme 0.</item>
///   <item><b>Kamera koşu sırasında durur.</b> Frontier takibi ve onun Zeno eşikleri yok; kameranın tek
///     otomatik hedefi SEÇİMDİR.</item>
///   <item><b>Öteleme kelepçesi silindi.</b> Tuval = panel olduğu için "sığan eksen ortalanır" klozu,
///     ölçek 1'in altındaki her seçimde ötelemeyi grafın merkezine zorlar ve <c>FocusAndFit</c>'i tamamen
///     ezerdi. Tasarımın kurtarma yolu kelepçe değil jesttir (boş alana tıkla → varsayılan görünüm).</item>
/// </list>
/// </summary>
public class GraphCameraTests
{
    private static readonly Size Panel = new(600, 400);
    private static readonly Vector Inset =
        new(QuietGraphLayout.ContentInset, QuietGraphLayout.ContentInset);

    /// <summary>Seçim yokken görünüm varsayılandır: ölçek 1, öteleme 0 (JSX:294/355).</summary>
    [Fact]
    public void The_default_view_is_scale_one_at_the_origin_because_the_layout_already_fits_the_panel()
    {
        Assert.Equal(1.0, GraphCamera.Default.Scale, 6);
        Assert.Equal(0.0, GraphCamera.Default.Tx, 6);
        Assert.Equal(0.0, GraphCamera.Default.Ty, 6);
    }

    /// <summary>§2.3: "zoom = min(W/bw, H/bh), 0.7–2.6 kelepçe (padding = 3×node + 48px)" — pay hem düğümün
    /// kendi genişliğini hem nefes payını karşılar, çünkü sınır kutusu MERKEZLERDEN kuruludur.</summary>
    [Fact]
    public void The_focus_set_is_fitted_with_a_3_node_plus_48px_padding()
    {
        var camera = GraphCamera.FocusAndFit(Panel, new Rect(100, 100, 200, 100), nodeSize: 12, Inset);

        double padding = 12 * GraphCamera.SelectionPaddingNodeFactor + GraphCamera.SelectionPaddingPx; // 84
        double expected = Math.Min(Panel.Width / (200 + padding), Panel.Height / (100 + padding));
        Assert.Equal(Math.Clamp(expected, GraphCamera.SelectionMinScale, GraphCamera.SelectionMaxScale),
            camera.Scale, 6);
    }

    [Theory]
    [InlineData(4000.0, 0.7)] // çok geniş odak kümesi → taban
    [InlineData(1.0, 2.6)]    // tek düğüm → tavan
    public void The_selection_zoom_is_clamped_to_the_0_7_to_2_6_band(double span, double expected)
    {
        var camera = GraphCamera.FocusAndFit(Panel, new Rect(0, 0, span, span), 12, Inset);

        Assert.Equal(expected, camera.Scale, 6);
    }

    /// <summary>Sığdırma odak kutusunun MERKEZİNİ panelin merkezine getirir; içerik→dünya ötelemesi
    /// (12px kenar payı) hesaba katılır.</summary>
    [Fact]
    public void The_focus_centre_lands_on_the_panel_centre_including_the_content_inset()
    {
        var bounds = new Rect(100, 50, 40, 20);
        var camera = GraphCamera.FocusAndFit(Panel, bounds, 12, Inset);

        double centreX = bounds.X + bounds.Width / 2 + Inset.X;
        double centreY = bounds.Y + bounds.Height / 2 + Inset.Y;
        // Uçlar tam piksele yuvarlandığı için merkez en fazla yarım piksel kayar.
        Assert.Equal(Panel.Width / 2, centreX * camera.Scale + camera.Tx, 0);
        Assert.Equal(Panel.Height / 2, centreY * camera.Scale + camera.Ty, 0);
    }

    /// <summary>
    /// AYIRT EDİCİ — kelepçesizlik. Grafın SOL ÜST köşesindeki bir düğüm seçilince kamera onu gerçekten
    /// merkeze getirir; öteleme pozitif kalır. Bir <c>ClampPan</c> hâlâ devrede olsaydı (ölçek &lt; 1'de
    /// "sığan eksen ortalanır") öteleme grafın merkezine çekilir ve bu test KIRMIZI verirdi.
    /// </summary>
    [Fact]
    public void A_focus_set_at_the_edge_is_really_centred_because_there_is_no_pan_clamp_left()
    {
        // Geniş bir kutu → ölçek tabana (0.7) iner, yani kelepçenin "ortalama" klozunun tetikleneceği rejim.
        var camera = GraphCamera.FocusAndFit(Panel, new Rect(0, 0, 2000, 2000), 12, Inset);

        Assert.Equal(GraphCamera.SelectionMinScale, camera.Scale, 6);
        Assert.Equal(GraphCamera.RoundPixels(Panel.Width / 2 - (1000 + Inset.X) * camera.Scale), camera.Tx, 6);
        Assert.Equal(GraphCamera.RoundPixels(Panel.Height / 2 - (1000 + Inset.Y) * camera.Scale), camera.Ty, 6);
    }

    /// <summary>Animasyon UÇLARI tam piksele yuvarlanır — JS <c>Math.round</c> paritesiyle (.5 daima yukarı),
    /// .NET'in banker's rounding'iyle DEĞİL.</summary>
    [Fact]
    public void The_translation_ends_on_whole_pixels_with_js_math_round_parity()
    {
        Assert.Equal(3.0, GraphCamera.RoundPixels(2.5), 6);
        Assert.Equal(-2.0, GraphCamera.RoundPixels(-2.5), 6);
        Assert.Equal(2.0, GraphCamera.RoundPixels(2.4), 6);

        var camera = GraphCamera.FocusAndFit(Panel, new Rect(37, 19, 53, 29), 11, Inset);
        Assert.Equal(camera.Tx, Math.Floor(camera.Tx), 6);
        Assert.Equal(camera.Ty, Math.Floor(camera.Ty), 6);
    }

    /// <summary>
    /// İmleç merkezli zoom: imlecin ALTINDAKİ dünya noktası sabit kalır.
    ///
    /// <para><b>Eski iddia:</b> aynı kural pinliydi ama band 0.45–2.0 ve kademe ×1.1'di. §2.3 "Serbest
    /// gezinme" onu 0.7–5.0 / ×1.14 yaptı.</para>
    /// </summary>
    [Fact]
    public void Zooming_at_the_cursor_keeps_the_world_point_under_it_fixed()
    {
        var start = new CameraTransform(1, 0, 0);
        var cursor = new Point(240, 130);

        var zoomed = GraphCamera.ZoomAt(start, cursor, GraphCamera.WheelZoomStep);

        Assert.Equal(GraphCamera.WheelZoomStep, zoomed.Scale, 6);
        // Öteleme tam piksele yuvarlandığı için dünya noktası en fazla yarım ekran pikseli / ölçek kadar
        // kayar — bu bir sapma değil, yuvarlamanın kaçınılmaz sonucudur (kırpma metin/çizgi netliği içindir).
        Assert.Equal((cursor.X - start.Tx) / start.Scale, (cursor.X - zoomed.Tx) / zoomed.Scale, 0);
        Assert.Equal((cursor.Y - start.Ty) / start.Scale, (cursor.Y - zoomed.Ty) / zoomed.Scale, 0);
    }

    /// <summary>Wheel bandı 0.7–5.0 (§2.3). <b>Eski iddia:</b> manuel band 0.45–2.0'dı.</summary>
    [Fact]
    public void Manual_zoom_is_clamped_to_the_0_7_to_5_0_band()
    {
        var camera = new CameraTransform(1, 0, 0);
        for (int i = 0; i < 40; i++) camera = GraphCamera.ZoomAt(camera, new Point(300, 200), GraphCamera.WheelZoomStep);
        Assert.Equal(GraphCamera.ManualMaxScale, camera.Scale, 6);

        for (int i = 0; i < 80; i++) camera = GraphCamera.ZoomAt(camera, new Point(300, 200), 1 / GraphCamera.WheelZoomStep);
        Assert.Equal(GraphCamera.ManualMinScale, camera.Scale, 6);
    }

    /// <summary>Pan ekran deltasını EKLER (ölçeğe bölmez — öteleme de ekran uzayındadır) ve YUVARLAMAZ:
    /// sürükleme ara kareleridir, her karede yuvarlamak eli takip eden grafı titretirdi.</summary>
    [Fact]
    public void Panning_adds_the_screen_delta_and_never_rounds_because_those_are_intermediate_frames()
    {
        var camera = new CameraTransform(2.4, 10, -6);

        var panned = GraphCamera.Pan(camera, new Vector(3.5, -1.25));

        Assert.Equal(2.4, panned.Scale, 6);
        Assert.Equal(13.5, panned.Tx, 6);
        Assert.Equal(-7.25, panned.Ty, 6);
    }

    /// <summary>§2.3'ün sayıları — birinin sessizce kayması bu testi düşürür.</summary>
    [Fact]
    public void The_camera_numbers_are_pinned_to_their_spec_values()
    {
        Assert.Equal(0.7, GraphCamera.SelectionMinScale, 6);
        Assert.Equal(2.6, GraphCamera.SelectionMaxScale, 6);
        Assert.Equal(48.0, GraphCamera.SelectionPaddingPx, 6);
        Assert.Equal(3.0, GraphCamera.SelectionPaddingNodeFactor, 6);
        Assert.Equal(0.7, GraphCamera.ManualMinScale, 6);
        Assert.Equal(5.0, GraphCamera.ManualMaxScale, 6);
        Assert.Equal(1.14, GraphCamera.WheelZoomStep, 6);
        Assert.Equal(460.0, GraphCamera.TransitionMs, 6);
        Assert.Equal(160.0, GraphCamera.WheelTransitionMs, 6);
    }
}
