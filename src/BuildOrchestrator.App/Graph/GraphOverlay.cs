using System.Windows;

namespace BuildOrchestrator.App.Graph;

/// <summary>
/// [quiet] design v1.3.0 §2.3'ün EKRAN KOORDİNATLI katmanının konum aritmetiği — hover tooltip'i ve seçim
/// ad etiketi. Saf; WPF nesnesine dokunmaz.
///
/// <para><b>Neden ekran koordinatı:</b> §2.3 "Tooltip ekran koordinatında konumlanır (zoom/pan
/// transform'undan bağımsız, her zoom'da net)". Kamera transform'unun ALTINDA yaşasalardı 5× zoom'da metin de
/// 5× ölçeklenip bulanıklaşırdı. Bu yüzden katman <c>World</c>'ün KARDEŞİDİR ve hiçbir
/// <c>RenderTransform</c> taşımaz; konum ise düğümün dünya noktasının kameradan GEÇİRİLMİŞ hâlidir.</para>
///
/// <para><b>Kutu ankraja ORTALANIR; tooltip sığmadığında yalnız TAŞTIĞI KADAR kayar.</b> Ankraj (düğümün
/// ekran noktası) panelin iç payına kelepçelenir (BuildApp.jsx:470) ve kutu ona ortalanır — sığdığı her yerde
/// hiçbir şey kaymaz. Tooltip bunun üstüne kutusunun TAMAMINI iç payın içinde tutar: yalnız ankraja ortalı
/// kutunun yarısı kenar sütunlardaki düğümlerde panelin dışına taşıyor ve <c>Ground</c>'un kırpması o yarıyı
/// siliyordu (kullanıcı gözlemi: "en sağdakinin üzerine hover olunca yarısı dışarıda kalıyor"). Kayan kutu
/// kelepçeli ankrajı her zaman örter; iç payın içindeki bir düğümde ankraj düğümün kendisidir, yani tooltip
/// düğümünün üstünde kalır. Yatayda kaymak kutuyu düğümün üstüne bindirmez (aradaki dikey boşluk durur);
/// dikeyde bindirirdi, o yüzden dikeyde takla vardır. Bir ara sürümde kutunun tamamına uygulanan kelepçe
/// tooltip'i düğümünden koparıyor görünmüştü — o gözlem, kutu genişliğinin her adda bayat ölçüldüğü dönemdeydi
/// (bkz. <c>GraphView.PlaceOverlayBox</c>). Ad etiketi kaymaz: onun yerini kamera açar
/// (<see cref="NameLabelTopLeft"/>).</para>
///
/// <para><b>Kelepçe payı panelin İÇ PAYIDIR</b> (<see cref="QuietGraphLayout.ContentInset"/>, ayrı bir sayı
/// değil): odak kipinde kamera yakınlaşıp düğümü kenara ittiğinde etiket köşeye YAPIŞMAZ, grafın kendi
/// nefes payının içinde durur.</para>
/// </summary>
public static class GraphOverlay
{
    /// <summary>Overlay kutusunun düğümün BOYANMIŞ kenarına mesafesi — tooltip de ad etiketi de AYNI sayıyı
    /// kullanır.
    ///
    /// <para><b>§2.3 tooltip için 8px, ad etiketi için 6px der; ikisi 6'da birleştirildi</b> (kullanıcı
    /// kararı: "proje adı ile amber border arasındaki mesafe ne ise, tooltip ile node border arasındaki de o
    /// olsun"). İki ayrı sayı, aynı düğümün etrafındaki iki yüzeyde tutarsız okunuyordu — ve bir mesafe iki
    /// yerde tanımlanmaz (kopya YASAK).</para></summary>
    public const double OverlayGapPx = 6.0;

    /// <summary>Bir içerik noktasının EKRAN karşılığı: dünya ötelemesi + kamera.</summary>
    public static Point Project(Point contentCentre, CameraTransform camera) => new(
        (contentCentre.X + QuietGraphLayout.ContentInset) * camera.Scale + camera.Tx,
        (contentCentre.Y + QuietGraphLayout.ContentInset) * camera.Scale + camera.Ty);

    /// <summary>Düğümün merkezi o anki kadrajda (panelin içinde) mi. Listeden yansıyan hover YALNIZ bu durumda
    /// işler: tooltip'in ankrajı panele kelepçelendiği için kadraj dışındaki bir düğümün tooltip'i kenarda,
    /// hiçbir şeyi göstermeden belirirdi.</summary>
    public static bool IsOnScreen(Point contentCentre, CameraTransform camera, Size panel)
    {
        var screen = Project(contentCentre, camera);
        return screen.X >= 0 && screen.X <= panel.Width && screen.Y >= 0 && screen.Y <= panel.Height;
    }

    /// <summary>
    /// Tooltip kutusunun SOL-ÜST köşesi: düğümün boyanmış üst kenarının <see cref="OverlayGapPx"/> üstünde ve
    /// düğüme ortalı. Üstte yer kalmadıysa düğümün ALTINA taklar; ortalı kutu iç payın yan sınırlarından birini
    /// aşacaksa yalnız aştığı kadar içeri kayar.
    /// </summary>
    /// <param name="halfExtent">Düğümün ekranda kapladığı YARIM yükseklik. Karenin yarısı DEĞİLDİR: vurgu
    /// ölçeği, taşan halka/yörünge ve kamera dahildir (bkz. <c>GraphView.PaintedHalfExtent</c>).</param>
    public static Point TooltipTopLeft(
        Point contentCentre, CameraTransform camera, double halfExtent, Size panel, Size box)
    {
        var screen = Project(contentCentre, camera);
        return new Point(
            ClampInside(ClampAnchor(screen.X, panel.Width) - box.Width / 2, box.Width, panel.Width),
            PlaceAbovePreferred(screen.Y, halfExtent + OverlayGapPx, box.Height, panel.Height));
    }

    /// <summary>
    /// Seçim ad etiketinin SOL-ÜST köşesi: düğümün boyanmış alt kenarının 6px altında ve düğüme ortalı.
    /// <b>HER ZAMAN altta</b> — takla da kelepçe de yok.
    ///
    /// <para>Kullanıcı kararı: "standart olsun, her zaman altta çıksın." Etiketin panele sığması artık
    /// KAMERANIN işi (<c>GraphView.ReserveRoomForSelectionLabel</c>): seçim zaten kamerayı hareket ettirir,
    /// dolayısıyla yer açmanın doğru yeri orasıdır. Etiketi kaçıran her çare — kelepçe onu düğümün üstüne
    /// bindiriyor, takla beklenmedik bir tarafa atıyordu — sonucu tahmin edilemez kılıyordu.</para>
    /// </summary>
    public static Point NameLabelTopLeft(
        Point contentCentre, CameraTransform camera, double halfExtent, Size panel, Size box)
    {
        var screen = Project(contentCentre, camera);
        return new Point(
            ClampAnchor(screen.X, panel.Width) - box.Width / 2,
            screen.Y + halfExtent + OverlayGapPx);
    }

    /// <summary>Ankrajı (düğümün ekran noktasını) panelin iç payına çeker; kutu buna ORTALANIR. Ad etiketinde
    /// son söz budur — tooltip ayrıca kutusunu iç payın içine kaydırır (<see cref="ClampInside"/>).</summary>
    private static double ClampAnchor(double screenX, double panelWidth) => Math.Clamp(
        screenX, QuietGraphLayout.ContentInset,
        Math.Max(QuietGraphLayout.ContentInset, panelWidth - QuietGraphLayout.ContentInset));

    /// <summary>
    /// Kutuyu düğümün ÜSTÜNE koyar; sığmıyorsa ALTINA taklar.
    ///
    /// <para>Takla bir kelepçenin yerine geçer: dikey kelepçe kutuyu panele geri çekerken DÜĞÜMÜN ÜSTÜNE
    /// bindiriyordu. Yalnız TOOLTIP'e uygulanır — hover kamerayı kıpırdatmaz, dolayısıyla en üstteki bantta
    /// yer açmanın başka yolu yoktur. Ad etiketinde ise yeri KAMERA açar ve etiket her zaman altta kalır.</para>
    /// </summary>
    private static double PlaceAbovePreferred(double screenY, double offset, double boxHeight, double panelHeight)
    {
        double above = screenY - offset - boxHeight;
        if (above >= QuietGraphLayout.ContentInset) return above;
        double below = screenY + offset;
        return below + boxHeight <= panelHeight - QuietGraphLayout.ContentInset
            ? below
            : ClampInside(above, boxHeight, panelHeight);
    }

    /// <summary>
    /// Kutuyu TEK eksende panelin iç payının içinde tutar: tooltip'in yatay kaymasının da, dikeyde iki taraf da
    /// sığmadığındaki son çarenin de tek yolu.
    ///
    /// <para>Kutu iç payların arasından genişse baştaki (sol/üst) paya oturur. <c>Math.Max</c> şarttır:
    /// <c>Math.Clamp</c> <c>min &gt; max</c> ile çağrılınca istisna fırlatır ve hover uygulamayı düşürürdü.</para>
    /// </summary>
    private static double ClampInside(double start, double boxExtent, double panelExtent) => Math.Clamp(
        start, QuietGraphLayout.ContentInset,
        Math.Max(QuietGraphLayout.ContentInset, panelExtent - QuietGraphLayout.ContentInset - boxExtent));
}
