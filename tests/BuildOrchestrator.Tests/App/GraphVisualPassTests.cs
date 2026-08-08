using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using ShapePath = System.Windows.Shapes.Path;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [quiet · görsel geçiş] Uygulama gerçek OSYS reposuyla açılıp gözle karşılaştırıldığında bulunan kusurlar.
/// Hepsi tek tek KIRMIZI gösterildi; her testin doc'unda kusurun kök nedeni yazılıdır.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphVisualPassTests
{
    private static IReadOnlyList<GraphNode> Nodes() =>
    [
        new("OSYS.Base", 0, GraphStatus.Succeeded),
        new("OSYS.Data", 1, GraphStatus.Building),
        new("OSYS.Api", 2, GraphStatus.Queued),
    ];

    private static IReadOnlyList<GraphEdge> Edges() =>
        [new("OSYS.Base", "OSYS.Data"), new("OSYS.Data", "OSYS.Api")];

    private static GraphView Built(bool animations = false)
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => animations);
        view.SetGraph(Nodes(), Edges());
        return view;
    }

    // ---------------------------------------------------------------- B1: overlay kamerayı İZLEMİYORDU

    /// <summary>
    /// <b>Kusur:</b> seçili düğümün ad etiketi ve hover tooltip'i yanlış yerde duruyordu — "ortalamıyor,
    /// sağda solda çıkıyor".
    ///
    /// <para><b>Kök neden:</b> overlay konumu kameranın HEDEFİNDEN (<c>CurrentCamera</c>) hesaplanıyor ve
    /// yalnız <c>SnapCameraTo</c>'dan tazeleniyordu. Kamera ANİMASYONLA kaydığında (seçim 460ms, wheel
    /// 160ms) o yol hiç çalışmıyor ⇒ etiket, kameranın henüz varmadığı bir noktaya göre konumlanıp orada
    /// kalıyordu. Konum artık CANLI transform'dan okunur ve transform her değiştiğinde tazelenir.</para>
    /// </summary>
    [StaFact]
    public void The_overlay_follows_the_camera_while_it_is_still_moving()
    {
        var view = Built();
        view.SetHoverForTest("OSYS.Data");
        var before = view.TooltipTopLeft;

        // Kameranın CANLI hâlini oynat (animasyonun bir ara karesinin yaptığı şey) — hedef DEĞİŞMEDİ.
        view.MoveLiveCameraForTest(new CameraTransform(1.0, 120, -40));

        var moved = new CameraTransform(1.0, 120, -40);
        // Hover edilen (ama seçili OLMAYAN) düğümün boyanmış yarım yüksekliği: kare yarısı × vurgu ölçeği,
        // halka yok.
        double halfExtent = view.NodeSize / 2 * GraphView.HoverScale * moved.Scale;

        Assert.NotEqual(before, view.TooltipTopLeft);
        Assert.Equal(
            GraphOverlay.TooltipTopLeft(
                view.NodeCenter("OSYS.Data"), moved, halfExtent, view.ViewportSize, view.TooltipBoxSize),
            view.TooltipTopLeft);
    }

    /// <summary>Ad etiketi de aynı kanaldan tazelenir — seçimde kamera 460ms kaydığı için asıl kusur oradaydı.</summary>
    [StaFact]
    public void The_selection_label_follows_the_camera_while_it_is_still_moving()
    {
        var view = Built();
        view.SelectedNode = "OSYS.Data";
        var before = view.SelectionLabelTopLeft;

        view.MoveLiveCameraForTest(new CameraTransform(2.0, -60, 30));

        Assert.NotEqual(before, view.SelectionLabelTopLeft);
    }

    // ---------------------------------------------------------------- B2: seçili düğüm arkada kalıyordu

    /// <summary>
    /// <b>Kusur:</b> "amber çerçeve yok" ve "köşelerinde noktalar beliriyor".
    ///
    /// <para><b>Kök neden:</b> seçim halkası düğümden <see cref="GraphView.SelectionRingInset"/> kadar
    /// dışarı taşar. Dar pitch'te bu taşma komşu hücrelerin üstüne düşer ve düğümler ekleme sırasına göre
    /// çizildiği için sonraki komşular halkanın üstünü örter — geriye yalnız komşuların arasına denk gelen
    /// köşe parçaları kalır ("noktalar"). Seçili düğüm artık hover gibi ÖNE alınır.</para>
    /// </summary>
    [StaFact]
    public void A_selected_node_is_pulled_to_the_front_so_its_ring_is_never_covered()
    {
        var view = Built();
        Assert.Equal(0, Panel.GetZIndex(view.NodeVisuals["OSYS.Data"].Cell));

        view.SelectedNode = "OSYS.Data";

        Assert.Equal(1, Panel.GetZIndex(view.NodeVisuals["OSYS.Data"].Cell));
        Assert.Equal(0, Panel.GetZIndex(view.NodeVisuals["OSYS.Base"].Cell));

        view.SelectedNode = null;
        Assert.Equal(0, Panel.GetZIndex(view.NodeVisuals["OSYS.Data"].Cell));
    }

    // ---------------------------------------------------------------- Ö2: seçili düğüm hover boyutunda kalır

    /// <summary>
    /// <b>Kusur:</b> "hover'da büyüyordu, tıkladığımda o boyutta kalıyordu, amber bir border ile
    /// çerçeveleniyordu — burada yok."
    ///
    /// <para><b>Karar (kullanıcı):</b> bu davranış Graph Lab prototipinde vardı ve ana prototipe
    /// taşınmamış; ana prototipte seçim düğümü BÜYÜTMEZ (BuildApp.jsx:442 — <c>scale</c> yalnız hover'a
    /// bağlı). Kullanıcı istenen davranışın Graph Lab'deki olduğunu doğruladı: seçili düğüm hover ölçeğinde
    /// KALIR ve amber halkasıyla çerçevelenir. §2.3'ten bilinçli sapma.</para>
    /// </summary>
    [StaFact]
    public void A_selected_node_keeps_the_hover_scale_and_its_amber_ring()
    {
        var view = Built();
        var visual = view.NodeVisuals["OSYS.Data"];

        view.SelectedNode = "OSYS.Data";

        var scale = Assert.IsType<ScaleTransform>(visual.Body.RenderTransform);
        Assert.Equal(GraphView.HoverScale, scale.ScaleX, 6);
        Assert.Equal(Visibility.Visible, visual.SelectionRing.Visibility);
        Assert.Same(view.FindResource("Brush.FocusRing"), visual.SelectionRing.Stroke);

        // Hover gelip gitse bile seçili düğüm küçülmez.
        view.SetHoverForTest("OSYS.Base");
        view.SetHoverForTest(null);
        Assert.Equal(GraphView.HoverScale, scale.ScaleX, 6);

        view.SelectedNode = null;
        Assert.Equal(1.0, scale.ScaleX, 6);
    }

    // ---------------------------------------------------------------- B3: beads hover'la birlikte büyümüyordu

    /// <summary>
    /// <b>Kusur:</b> derlenen düğümün yörüngesi "estetik değil" — hover'da kare büyürken yörünge yerinde
    /// kalıyor ve kare onun içinden taşıyordu.
    ///
    /// <para><b>Kök neden:</b> prototipte ölçek İKİ öğeye birden uygulanır (BuildApp.jsx:442 kare div'i,
    /// :457 beads SVG'si); bizde yalnız gövdeye uygulanıyordu. İkisi artık AYNI transform nesnesini
    /// paylaşır — ayrı iki transform olsaydı zamanla ayrışabilirlerdi.</para>
    /// </summary>
    [StaFact]
    public void The_beads_orbit_scales_together_with_the_node()
    {
        var view = Built(animations: true);
        var visual = view.NodeVisuals["OSYS.Data"]; // building → yörüngesi var
        Assert.NotNull(visual.Beads);

        view.SetHoverForTest("OSYS.Data");

        // İddia PAYLAŞIM'dır, bir değer değil: aynı nesne olduğu sürece ikisi hangi ölçeğe giderse BİRLİKTE
        // gider ve ayrışamazlar. (Değerin kendisi animasyonla varır; headless'ta compositor saati ilerlemez,
        // ölçeğin 1.7'ye oturduğu A_selected_node_keeps_the_hover_scale… testinde pinli.)
        Assert.Same(visual.Body.RenderTransform, visual.Beads!.RenderTransform);
        Assert.IsType<ScaleTransform>(visual.Beads.RenderTransform);
        Assert.Equal(new Point(0.5, 0.5), visual.Beads.RenderTransformOrigin);
        Assert.Equal(visual.Body.RenderTransformOrigin, visual.Beads.RenderTransformOrigin);
    }

    // ---------------------------------------------------------------- Ö1: çizgi düğümün içinden görünüyordu

    /// <summary>
    /// <b>Kusur:</b> seçim çizgileri "bazen node'ların üzerinden geçiyor".
    ///
    /// <para><b>Kök neden:</b> çizgiler düğüm MERKEZİNE gider ve statü zemini <c>--status-*-soft</c>, yani
    /// %12 alfadır — altından geçen amber çizgi karenin içinden görünüyordu. Kareye panel zemini renginde
    /// OPAK bir taban konur; statü rengi onun üstünde durur, dolayısıyla görünüm değişmez ama çizgi artık
    /// düğümün arkasında kalır.</para>
    /// </summary>
    [StaFact]
    public void A_node_has_an_opaque_base_so_a_selection_edge_never_shows_through_it()
    {
        var view = Built();
        var visual = view.NodeVisuals["OSYS.Data"];

        Assert.Same(view.FindResource("Brush.SurfaceBase"), visual.Base.Fill);
        Assert.Equal(view.NodeSize, visual.Base.Width, 6);
        Assert.Equal(GraphView.NodeCornerRadius, visual.Base.RadiusX, 6);

        // Taban statü karesinin ALTINDA çizilir — üstünde olsaydı rengi yutardı.
        var children = visual.Body.Children.Cast<UIElement>().ToList();
        Assert.True(children.IndexOf(visual.Base) < children.IndexOf(visual.Square));
    }

    /// <summary>Kenar katmanı düğüm katmanının ALTINDA kalır — sıra artık ekleme sırasına değil AÇIK bir
    /// z-index'e bağlıdır, çünkü "bazen üstünde" gözlemi sıraya güvenilemeyeceğini gösterdi.</summary>
    [StaFact]
    public void The_edge_layer_declares_an_explicit_z_index_below_the_node_layer()
    {
        var view = Built();
        view.SelectedNode = "OSYS.Data";

        var world = view.World;
        var edgeLayer = world.Children.Cast<UIElement>()
            .Single(c => c is Canvas canvas && canvas.Children.Contains(view.SelectionEdgePaths[0]));
        var nodeLayer = world.Children.Cast<UIElement>()
            .Single(c => c is Canvas canvas && canvas.Children.Contains(view.NodeVisuals["OSYS.Data"].Cell));

        Assert.True(Panel.GetZIndex(edgeLayer) < Panel.GetZIndex(nodeLayer),
            "kenar katmanının z-index'i düğüm katmanınınkinden küçük değil");
    }

    // ---------------------------------------------------------------- K1-K3: kullanıcı kararları

    // [kopya YASAK] Hold süresi (1400ms) ve kenar payı (28px) kararları SABİTLERİN SAHİBİ olan testlerde
    // pinlidir — GraphNodeOpacityTests ve QuietGraphLayoutTests; gerekçeleri de orada yazılıdır.

    /// <summary><b>Karar (kullanıcı):</b> sağ alt köşedeki mono ipucu satırı kaldırıldı.</summary>
    [StaFact]
    public void The_bottom_right_hint_line_is_gone()
    {
        var view = Built();

        Assert.DoesNotContain(
            DsResources.RealizedObjects(view.Ground).OfType<TextBlock>(),
            t => t.Text.Contains("scroll", StringComparison.OrdinalIgnoreCase)
              || t.Text.Contains("click again", StringComparison.OrdinalIgnoreCase));
    }
}
