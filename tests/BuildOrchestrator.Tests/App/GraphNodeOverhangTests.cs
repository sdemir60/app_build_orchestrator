using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [quiet · taşma] Düğümün KARESİNDEN DIŞARI taşan görseller — seçim halkası ve beads yörüngesi — gerçekten
/// çiziliyor mu?
///
/// <para><b>Kusur:</b> "bir node'a tıkladığımda köşelerden sarı noktalar görüyorum, ayrı bir amber çerçeve
/// oluşmuyor" ve "derlerken node etrafında dönen noktalar yok".</para>
///
/// <para><b>Kök neden (ÖLÇÜLDÜ):</b> WPF bir çocuğu ARRANGE SLOT'una kırpar. Halka da yörünge de düğüm
/// kadar (24px) bir kabın içindeydi ama kendileri daha büyüktü (30px / 29.6px) — <c>GetLayoutClip</c> ikisi
/// için de <c>(3,3,24,24)</c> ve <c>(2.8,2.8,24,24)</c> döndürüyordu. Düz kenarlar tamamen kırpılıyor,
/// geriye yalnız yarıçapı büyük olduğu için kırpma dikdörtgeninin İÇİNE giren KÖŞE YAYLARI kalıyordu:
/// kullanıcının gördüğü "köşelerdeki sarı noktalar" halkanın ta kendisiydi.</para>
///
/// <para>Bu yüzden testler iki katmanlı: yapısal iddia (kap taşan her şeye YETER) + <b>piksel</b> iddiası
/// (bant gerçekten boyanıyor). Yapısal iddia tek başına yeterli değildi — eski kod da "halka 30px" diyordu,
/// yalnız o 30px ekrana ulaşmıyordu.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphNodeOverhangTests
{
    private static readonly Size Panel = new(640, 400);

    /// <summary>Kenarsız iki bantlık graf: seçim çizgisi çizilmez, dolayısıyla banttaki amber pikselin tek
    /// olası kaynağı halkanın/yörüngenin kendisidir.</summary>
    private static GraphView Built(GraphStatus status, bool animations)
    {
        var view = GraphTestView.Realized(Panel, () => animations);
        view.SetGraph([new("Solo", 0, status), new("Other", 1, GraphStatus.Discovered)], []);
        return view;
    }

    /// <summary>Headless'ta compositor saati İLERLEMEZ: açılış dalgası hücreleri opaklık 0'da, beads giriş
    /// animasyonu da yörüngeyi 0'da dondurur. Piksel iddiası için animasyonların BİTMİŞ hâli elle kurulur.</summary>
    private static void FinishAnimations(GraphView view)
    {
        foreach (var visual in view.NodeVisuals.Values)
        {
            visual.Cell.BeginAnimation(UIElement.OpacityProperty, null);
            visual.Cell.Opacity = 1.0;
            visual.Cell.RenderTransform = Transform.Identity;
            visual.Body.BeginAnimation(UIElement.OpacityProperty, null);
            visual.Body.Opacity = 1.0;
            if (visual.Beads is { } orbit)
            {
                orbit.BeginAnimation(UIElement.OpacityProperty, null);
                orbit.Opacity = 1.0;
            }
        }
        view.UpdateLayout();
    }

    /// <summary>Panelin gerçek pikselleri. Kırpılmış bir görsel "kurulu ama görünmez"dir — onu ancak boya
    /// yakalar.</summary>
    private static byte[] Render(GraphView view)
    {
        var bitmap = new RenderTargetBitmap(
            (int)Panel.Width, (int)Panel.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        byte[] pixels = new byte[(int)Panel.Width * 4 * (int)Panel.Height];
        bitmap.CopyPixels(pixels, (int)Panel.Width * 4, 0);
        return pixels;
    }

    /// <summary>Amber mi? Zemin <c>surface-base</c> neredeyse nötr-siyahtır (R≈14, B≈16); amber tonlarında
    /// kırmızı maviyi belirgin biçimde geçer. Eşik gevşek tutuldu — bant yarı saydam da olabilir.</summary>
    private static bool IsAmber(byte[] pixels, int x, int y)
    {
        int i = y * (int)Panel.Width * 4 + x * 4;
        return pixels[i + 2] > 60 && pixels[i + 2] > pixels[i] + 30;
    }

    /// <summary>Bir düğümün BOYANMIŞ görüntüdeki merkezi. İki dönüşüm de hesaba katılır: kamera (seçim
    /// odağı yakınlaştırır) ve panel BAŞLIĞI — <c>RenderTargetBitmap</c> tüm UserControl'ü boyar, graf
    /// koordinatları ise başlığın altındaki zemine göredir.</summary>
    private static Point ScreenCentre(GraphView view, string name)
    {
        var screen = GraphOverlay.Project(view.NodeCenter(name), view.CurrentCamera);
        return new Point(screen.X, screen.Y + Panel.Height - view.ViewportSize.Height);
    }

    // ---------------------------------------------------------------- yapısal: kap taşmaya YETER

    /// <summary>
    /// Hücre, düğümden DIŞARI taşan her şeye yer bırakır. Bu iddia kırpılmanın kendisini değil ONU DOĞURAN
    /// koşulu pinler: hücre taşan çocuktan küçük kaldığı an WPF layout clip'i geri gelir.
    /// </summary>
    [StaFact]
    public void The_cell_is_big_enough_for_everything_that_overhangs_the_node_square()
    {
        var view = Built(GraphStatus.Building, animations: true);
        var visual = view.NodeVisuals["Solo"];

        Assert.Equal(view.NodeSize + 2 * GraphView.CellOverhang, visual.Cell.Width, 6);
        Assert.True(visual.Cell.Width >= visual.SelectionRing.Width,
            $"halka ({visual.SelectionRing.Width}) hücreye ({visual.Cell.Width}) sığmıyor — kırpılır");
        Assert.True(visual.Cell.Width >= view.BeadsGeometry.Side,
            $"yörünge ({view.BeadsGeometry.Side}) hücreye ({visual.Cell.Width}) sığmıyor — kırpılır");
        // Gövde düğüm kadardır ve öyle KALIR: tıklama alanı büyüseydi dar pitch'te komşunun üstüne binerdi.
        Assert.Equal(view.NodeSize, visual.Body.Width, 6);
    }

    /// <summary>Halka ve yörünge gövdenin DEĞİL hücrenin çocuğudur — gövde düğüm kadardır, içinde kalan her
    /// şey ona kırpılır.</summary>
    [StaFact]
    public void The_ring_and_the_orbit_live_beside_the_body_not_inside_it()
    {
        var view = Built(GraphStatus.Building, animations: true);
        var visual = view.NodeVisuals["Solo"];

        Assert.Same(visual.Cell, visual.SelectionRing.Parent);
        Assert.Same(visual.Cell, visual.Beads!.Parent);
        // Hover/seçim büyütmesini yine de gövdeyle PAYLAŞIRLAR — ayrı transform'lar zamanla ayrışırdı.
        Assert.Same(visual.Body.RenderTransform, visual.SelectionRing.RenderTransform);
        Assert.Same(visual.Body.RenderTransform, visual.Beads.RenderTransform);
    }

    /// <summary>
    /// §2.3'ün halkası CSS <c>outline: 2px solid var(--focus-ring); outline-offset: 2</c>'dir: iç kenarı
    /// kareden 2px dışarıda, dış kenarı 4px dışarıda. <b>WPF kalemi Rectangle'ın İÇİNE çizer</b> (geometri
    /// yarım kalem kadar içeri alınır), dolayısıyla dikdörtgenin kendisi kareden her yandan
    /// offset + TAM kalem kadar büyük olmalıdır — yarım kalem kadar değil.
    /// </summary>
    [StaFact]
    public void The_selection_ring_matches_the_css_outline_offset_and_width()
    {
        var view = Built(GraphStatus.Succeeded, animations: false);
        view.SelectedNode = "Solo";
        var ring = view.NodeVisuals["Solo"].SelectionRing;

        Assert.Equal(GraphView.SelectedNodeBorderThickness, ring.StrokeThickness, 6);
        Assert.Equal(view.NodeSize + 2 * GraphView.SelectionRingInset, ring.Width, 6);
        // İç kenar = kare + 2×offset; offset = inset − kalem.
        Assert.Equal(2.0, GraphView.SelectionRingInset - GraphView.SelectedNodeBorderThickness, 6);
    }

    // ---------------------------------------------------------------- piksel: bant GERÇEKTEN boyanıyor

    /// <summary>
    /// AYIRT EDİCİ — halkanın DÜZ kenarı boyanıyor mu? Kırpılmış hâlde yalnız köşe yayları hayatta kalıyordu
    /// ("köşelerde sarı noktalar"); düğümün tam üstündeki bant ise zemin rengindeydi.
    /// </summary>
    [StaFact]
    public void The_selection_ring_paints_a_full_band_around_the_node_not_just_its_corners()
    {
        var view = Built(GraphStatus.Succeeded, animations: false);
        view.SelectedNode = "Solo";
        FinishAnimations(view);

        var centre = ScreenCentre(view, "Solo");
        var pixels = Render(view);
        int x = (int)Math.Round(centre.X);
        // Seçim kamerayı yakınlaştırır VE seçili düğüm hover ölçeğinde durur — bant iki çarpanla da büyür.
        double zoom = view.CurrentCamera.Scale * GraphView.HoverScale;
        int from = (int)Math.Ceiling(centre.Y - (view.NodeSize / 2 + GraphView.CellOverhang) * zoom);
        int to = (int)Math.Floor(centre.Y - view.NodeSize / 2 * zoom);

        Assert.True(
            Enumerable.Range(from, Math.Max(1, to - from)).Any(y => IsAmber(pixels, x, y)),
            $"düğümün üstündeki [{from},{to}] bandında hiç amber piksel yok — halka kırpılıyor");
    }

    /// <summary>AYIRT EDİCİ — yörüngenin noktaları boyanıyor mu? Aynı kırpma yörüngeyi de düğümün kenarına
    /// bastırıyordu; noktalar karenin çerçevesiyle çakışıp görünmez oluyordu.</summary>
    [StaFact]
    public void The_beads_orbit_paints_dots_outside_the_node_square()
    {
        var view = Built(GraphStatus.Building, animations: true);
        FinishAnimations(view);

        var centre = ScreenCentre(view, "Solo");
        var pixels = Render(view);
        // Yörüngenin SOL kenarının ORTASI: köşelerden uzak, düz bir koşu (seçim yok → kamera ölçeği 1).
        // Köşeleri bilerek dışarıda bırakır: kırpılmış hâlde köşe yayları hücrenin içine giriyor ve
        // "yörünge var" izlenimi verebiliyordu — kullanıcının gördüğü "köşelerdeki noktalar" tam da buydu.
        // Noktalar 3.4px aralıklı, bu yüzden tek piksel değil kısa bir dilim taranır.
        int left = (int)Math.Round(centre.X - view.BeadsGeometry.Side / 2);
        var band =
            from x in Enumerable.Range(left - 1, 3)
            from y in Enumerable.Range((int)Math.Round(centre.Y) - 6, 13)
            select (x, y);

        Assert.True(band.Any(p => IsAmber(pixels, p.x, p.y)),
            $"x≈{left} yörünge bandında hiç amber piksel yok — noktalar kırpılıyor ya da hiç çizilmiyor");
    }
}
