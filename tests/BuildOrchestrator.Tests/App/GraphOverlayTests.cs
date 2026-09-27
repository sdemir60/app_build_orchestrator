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

    /// <summary>Tooltip düğümün BOYANMIŞ üst kenarının bir boşluk kadar üstündedir ve yatayda ona
    /// ortalanır.</summary>
    [Fact]
    public void The_tooltip_sits_one_gap_above_the_painted_edge_and_is_centred_on_the_node()
    {
        var camera = new CameraTransform(1.0, 0, 0);
        var box = new Size(120, 20);

        var topLeft = GraphOverlay.TooltipTopLeft(new Point(200, 150), camera, HalfExtent, Panel, box);

        var screen = GraphOverlay.Project(new Point(200, 150), camera);
        Assert.Equal(screen.X - box.Width / 2, topLeft.X, 6);
        Assert.Equal(screen.Y - HalfExtent - GraphOverlay.OverlayGapPx - box.Height, topLeft.Y, 6);
    }

    /// <summary>
    /// AYIRT EDİCİ — iki yüzey AYNI boşluğu kullanır.
    ///
    /// <para><b>Eski iddia:</b> §2.3'ün iki ayrı sayısı (tooltip 8px, ad etiketi 6px) ayrı sabitlerde
    /// pinliydi. <b>Değişme gerekçesi:</b> aynı düğümün etrafında dönen iki kutunun farklı mesafede durması
    /// gözle tutarsız okunuyordu (kullanıcı: "proje adı ile amber border arasındaki mesafe ne ise, tooltip
    /// ile node border arasındaki de o olsun"). Tek sayı — ve bir mesafe iki yerde tanımlanmaz.</para>
    /// </summary>
    [Fact]
    public void The_tooltip_and_the_name_label_keep_the_same_distance_from_the_painted_edge()
    {
        var camera = new CameraTransform(1.0, 0, 0);
        var node = new Point(200, 150);
        var box = new Size(120, 20);
        var screen = GraphOverlay.Project(node, camera);

        var tooltip = GraphOverlay.TooltipTopLeft(node, camera, HalfExtent, Panel, box);
        var label = GraphOverlay.NameLabelTopLeft(node, camera, HalfExtent, Panel, box);

        double aboveGap = (screen.Y - HalfExtent) - (tooltip.Y + box.Height);
        double belowGap = label.Y - (screen.Y + HalfExtent);
        Assert.Equal(aboveGap, belowGap, 6);
        Assert.Equal(GraphOverlay.OverlayGapPx, belowGap, 6);
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
    /// AYIRT EDİCİ — tooltip sığdığı sürece düğüme ORTALIDIR; sığmadığında yalnız TAŞTIĞI KADAR içeri kayar ve
    /// panelin iç payının içinde BÜTÜN kalır. Kayan kutu yine düğümün üstündedir.
    ///
    /// <para><b>Eski iddia:</b> <c>A_node_near_the_edge_keeps_its_tooltip_centred_even_if_the_box_overflows</c>
    /// kutuyu kenarda da ortalı tutuyor, taşan yarının kırpılmasını bedel sayıyordu ("ortalı durmak, kenarda
    /// kırpılmaktan önce gelir"). <b>Değişme gerekçesi:</b> kullanıcı gözlemi — "en sağdakinin üzerine hover
    /// olunca yarısı dışarıda kalıyor"; <c>Ground</c>'un kırpması o yarıyı siliyor ve ad okunmuyor. Eski
    /// kararın dayanağı ("kutu kelepçesi tooltip'i düğümden koparıyor") kutu genişliğinin her adda ilk
    /// ölçümdeki 24.6px'te takılı kaldığı bayat ölçüm kusuru varken gözlenmişti — o kusurun düzeltmesi 40 dk
    /// sonra geldi. Yanlış genişlikle kelepçelenen kutu gerçekten düğümden kopar; doğru genişlikle kelepçelenen
    /// kutu ise iç payın içindeki düğümünü her zaman örter (aşağıdaki iddia).</para>
    /// </summary>
    [Fact]
    public void A_tooltip_that_would_cross_the_inset_slides_in_just_enough_to_stay_whole()
    {
        var box = new Size(180, 20);
        var node = new Point(300, 150);

        // Sağ kenar: ekran X'i 526, ortalı kutu [436, 616] iç payın sınırını (564) 52px aşardı.
        var rightCamera = new CameraTransform(1.0, 190, 0);
        double rightX = GraphOverlay.Project(node, rightCamera).X;
        Assert.True(rightX + box.Width / 2 > Panel.Width, "kurulum hatalı: ortalı kutu panelden taşmalıydı");

        var right = GraphOverlay.TooltipTopLeft(node, rightCamera, HalfExtent, Panel, box);

        Assert.Equal(Panel.Width - Inset - box.Width, right.X, 6); // tam taştığı kadar — fazlası değil
        Assert.InRange(rightX, right.X, right.X + box.Width);      // kutu hâlâ düğümün üstünde

        // Sol kenar: ayna. Ekran X'i 74, ortalı kutu [-16, 164].
        var leftCamera = new CameraTransform(1.0, -262, 0);
        double leftX = GraphOverlay.Project(node, leftCamera).X;
        Assert.True(leftX - box.Width / 2 < 0, "kurulum hatalı: ortalı kutu panelden taşmalıydı");

        var left = GraphOverlay.TooltipTopLeft(node, leftCamera, HalfExtent, Panel, box);

        Assert.Equal(Inset, left.X, 6);
        Assert.InRange(leftX, left.X, left.X + box.Width);
    }

    /// <summary>Kayma yalnız GEREKTİĞİNDE olur: iç payın sınırına TAM dayanan kutu yerinden oynamaz, düğüme
    /// ortalı kalır.</summary>
    [Fact]
    public void A_tooltip_that_just_fits_beside_the_inset_stays_centred()
    {
        var box = new Size(120, 20);
        var node = new Point(300, 150);
        var camera = new CameraTransform(1.0, 168, 0); // ekran X'i 504, ortalı kutu [444, 564]
        double x = GraphOverlay.Project(node, camera).X;
        Assert.Equal(Panel.Width - Inset, x + box.Width / 2, 6); // kurulum: sağ kenar sınırda

        var topLeft = GraphOverlay.TooltipTopLeft(node, camera, HalfExtent, Panel, box);

        Assert.Equal(x - box.Width / 2, topLeft.X, 6);
    }

    /// <summary>Kutu iç payların arasından genişse (dar panel, çok uzun ad) SOL iç paya oturur — dikeydeki son
    /// çareyle aynı kural. Kelepçe hiçbir zaman <c>min &gt; max</c> ile kurulmaz: <c>Math.Clamp</c> o durumda
    /// istisna fırlatır, yani hover uygulamayı düşürürdü.</summary>
    [Fact]
    public void A_tooltip_wider_than_the_content_area_starts_at_the_left_inset()
    {
        var box = new Size(Panel.Width - Inset, 20); // iç payların arası 528px, kutu 564px
        Assert.True(box.Width > Panel.Width - 2 * Inset, "kurulum hatalı: kutu iç paya sığmamalıydı");

        var topLeft = GraphOverlay.TooltipTopLeft(
            new Point(300, 150), new CameraTransform(1.0, 0, 0), HalfExtent, Panel, box);

        Assert.Equal(Inset, topLeft.X, 6);
    }

    /// <summary>
    /// Ankraj panelin İÇ PAYINA kelepçelenir: düğüm (odak kipinde kamera yakınlaştığı için) panelin dışına
    /// çıksa bile ad etiketi köşeye yapışmaz. Tooltip o durumda da kutusunun tamamıyla iç payın içindedir.
    ///
    /// <para><b>Eski iddia:</b> ankraj kelepçesi TOOLTIP üzerinden pinleniyordu (<c>Inset - box.Width / 2</c>,
    /// yani yarısı panelin dışında bir kutu). <b>Değişme gerekçesi:</b> tooltip artık kutusunun tamamını iç
    /// payın içinde tutar (<see cref="A_tooltip_that_would_cross_the_inset_slides_in_just_enough_to_stay_whole"/>);
    /// yatayda yalnız ankraja kelepçelenen yüzey ad etiketidir, iddia oraya taşındı.</para>
    /// </summary>
    [Fact]
    public void An_anchor_pushed_outside_the_panel_is_pulled_back_into_the_content_inset()
    {
        var box = new Size(80, 20);
        var node = new Point(300, 150);
        var farLeft = new CameraTransform(1, -900, 0);
        var farRight = new CameraTransform(1, 900, 0);

        Assert.Equal(Inset - box.Width / 2,
            GraphOverlay.NameLabelTopLeft(node, farLeft, HalfExtent, Panel, box).X, 6);
        Assert.Equal(Panel.Width - Inset - box.Width / 2,
            GraphOverlay.NameLabelTopLeft(node, farRight, HalfExtent, Panel, box).X, 6);

        Assert.Equal(Inset, GraphOverlay.TooltipTopLeft(node, farLeft, HalfExtent, Panel, box).X, 6);
        Assert.Equal(Panel.Width - Inset - box.Width,
            GraphOverlay.TooltipTopLeft(node, farRight, HalfExtent, Panel, box).X, 6);
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
        Assert.Equal(screen.Y + HalfExtent + GraphOverlay.OverlayGapPx, topLeft.Y, 6);
    }

    /// <summary>
    /// AYIRT EDİCİ — ad etiketi HER ZAMAN düğümün altındadır; kaçmaz.
    ///
    /// <para><b>Eski iddia:</b> <c>A_label_that_cannot_fit_below_flips_above_the_node_instead_of_overlapping_it</c>
    /// etiketi aşağı sığmadığında düğümün üstüne taklatıyordu; ondan önceki sürüm de kelepçeliyordu.
    /// <b>Değişme gerekçesi:</b> kullanıcı kararı — "standart olsun, her zaman altta çıksın". İki çare de
    /// etiketi tahmin edilemez kılıyordu (kelepçe düğümün üstüne bindiriyor, takla beklenmedik tarafa
    /// atıyordu). Yer açmak artık KAMERANIN işi; bu saf fonksiyon yalnız kuralı söyler.</para>
    /// </summary>
    [Fact]
    public void The_name_label_stays_below_the_node_even_when_it_leaves_the_panel()
    {
        var box = new Size(120, 16);
        var camera = new CameraTransform(1, 0, 190); // düğüm panelin dibinin de altında
        var node = new Point(200, 150);
        var screen = GraphOverlay.Project(node, camera);

        var topLeft = GraphOverlay.NameLabelTopLeft(node, camera, HalfExtent, Panel, box);

        Assert.Equal(screen.Y + HalfExtent + GraphOverlay.OverlayGapPx, topLeft.Y, 6);
        Assert.True(topLeft.Y + box.Height > Panel.Height - Inset,
            "kurulum hatalı: etiket paya sığmamalıydı — kurtarma kameranın işi");
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

        Assert.Equal(screen.Y + HalfExtent + GraphOverlay.OverlayGapPx, topLeft.Y, 6);
    }

    /// <summary>
    /// Listeden yansıyan hover'ın kapısı: düğümün ekran noktası panelin içindeyse kadrajdadır. Zoom/focus
    /// düğümü panelin dışına ittiyse (dört yönde de) kadrajda DEĞİLDİR — o durumda tooltip'in kelepçeli
    /// ankrajı onu kenarda, hiçbir şeyi göstermeden çizerdi.
    /// </summary>
    [Fact]
    public void A_node_is_on_screen_only_while_its_projected_centre_lies_inside_the_panel()
    {
        var node = new Point(100, 50);

        Assert.True(GraphOverlay.IsOnScreen(node, new CameraTransform(1.0, 0, 0), Panel));
        Assert.True(GraphOverlay.IsOnScreen(node, new CameraTransform(2.6, -100, -60), Panel)); // focus zoom'u, içeride

        Assert.False(GraphOverlay.IsOnScreen(node, new CameraTransform(1.0, 5000, 0), Panel));  // sağda
        Assert.False(GraphOverlay.IsOnScreen(node, new CameraTransform(1.0, -5000, 0), Panel)); // solda
        Assert.False(GraphOverlay.IsOnScreen(node, new CameraTransform(1.0, 0, 5000), Panel));  // altta
        Assert.False(GraphOverlay.IsOnScreen(node, new CameraTransform(1.0, 0, -5000), Panel)); // üstte
    }

    /// <summary>§2.3'ün sayıları — birinin sessizce kayması bu testi düşürür. Kelepçe payı AYRI bir sayı
    /// değildir: grafın kendi iç payıdır (kopya YASAK).</summary>
    [Fact]
    public void The_overlay_numbers_are_pinned_to_their_spec_values()
    {
        Assert.Equal(6.0, GraphOverlay.OverlayGapPx, 6);
        Assert.Equal(QuietGraphLayout.ContentInset, Inset, 6);
    }
}
