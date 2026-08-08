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
/// <para><b>Kelepçe ANKRAJA uygulanır, kutuya değil</b> (BuildApp.jsx:470, :477) — kutu her zaman ankraja
/// ORTALANIR. Bir ara sürümde kelepçe kutunun tamamına uygulanıyordu ("node kenardayken bile tamamen okunur"
/// okumasıyla) ve <b>ölçülen sonuç kabul edilemezdi</b>: 500px'lik bir panelde 30 karakterlik bir proje adı
/// ~215px'lik bir kutu demektir, dolayısıyla kenardaki her düğümde tooltip düğümden onlarca piksel uzağa
/// kayıyor ve hangi düğüme ait olduğu okunamıyordu. Ortalı durmak, kenarda kırpılmaktan önce gelir.</para>
///
/// <para><b>Kelepçe payı panelin İÇ PAYIDIR</b> (<see cref="QuietGraphLayout.ContentInset"/>, ayrı bir sayı
/// değil): odak kipinde kamera yakınlaşıp düğümü kenara ittiğinde etiket köşeye YAPIŞMAZ, grafın kendi
/// nefes payının içinde durur.</para>
/// </summary>
public static class GraphOverlay
{
    /// <summary>Tooltip'in düğümün üstünde bıraktığı boşluk (§2.3: "node'un üstünde 8px").</summary>
    public const double TooltipGapPx = 8.0;
    /// <summary>Tooltip ankrajının düğüm merkezinden yukarı kayması, düğüm kenarının katı olarak (JSX:471).</summary>
    public const double TooltipRisePerNode = 0.9;
    /// <summary>Ad etiketi ankrajının düğüm merkezinden aşağı kayması (JSX:481).</summary>
    public const double LabelDropPerNode = 0.95;
    /// <summary>Seçili düğüm ile ad etiketi arasındaki boşluk (§2.3: "altında 6px boşlukla").</summary>
    public const double LabelGapPx = 6.0;

    /// <summary>Bir içerik noktasının EKRAN karşılığı: dünya ötelemesi + kamera.</summary>
    public static Point Project(Point contentCentre, CameraTransform camera) => new(
        (contentCentre.X + QuietGraphLayout.ContentInset) * camera.Scale + camera.Tx,
        (contentCentre.Y + QuietGraphLayout.ContentInset) * camera.Scale + camera.Ty);

    /// <summary>Tooltip kutusunun SOL-ÜST köşesi: düğümün 8px üstünde ve düğüme ORTALI.</summary>
    public static Point TooltipTopLeft(
        Point contentCentre, CameraTransform camera, double nodeSize, Size panel, Size box)
    {
        var screen = Project(contentCentre, camera);
        double top = screen.Y - nodeSize * TooltipRisePerNode * camera.Scale - TooltipGapPx - box.Height;
        return new Point(
            ClampAnchor(screen.X, panel.Width) - box.Width / 2,
            ClampInside(top, box.Height, panel.Height));
    }

    /// <summary>Seçim ad etiketinin SOL-ÜST köşesi: düğümün 6px altında ve düğüme ORTALI.</summary>
    public static Point NameLabelTopLeft(
        Point contentCentre, CameraTransform camera, double nodeSize, Size panel, Size box)
    {
        var screen = Project(contentCentre, camera);
        double top = screen.Y + nodeSize * LabelDropPerNode * camera.Scale + LabelGapPx;
        return new Point(
            ClampAnchor(screen.X, panel.Width) - box.Width / 2,
            ClampInside(top, box.Height, panel.Height));
    }

    /// <summary>Ankrajı (düğümün ekran noktasını) panelin iç payına çeker. Kutu buna ORTALANIR, yani düğüm
    /// panelin dışına çıkmadıkça hiçbir şey kaymaz.</summary>
    private static double ClampAnchor(double screenX, double panelWidth) => Math.Clamp(
        screenX, QuietGraphLayout.ContentInset,
        Math.Max(QuietGraphLayout.ContentInset, panelWidth - QuietGraphLayout.ContentInset));

    /// <summary>Kutuyu dikeyde panelin iç payının içinde tutar — yatayın aksine burada kelepçe kutunun
    /// TAMAMINA uygulanır, çünkü dikeyde kaydırmak ortalamayı bozmaz.</summary>
    private static double ClampInside(double top, double boxHeight, double panelHeight) => Math.Clamp(
        top, QuietGraphLayout.ContentInset,
        Math.Max(QuietGraphLayout.ContentInset, panelHeight - QuietGraphLayout.ContentInset - boxHeight));
}
