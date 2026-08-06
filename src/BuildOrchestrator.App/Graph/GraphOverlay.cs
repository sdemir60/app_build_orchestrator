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
/// <para><b>Kelepçe kutunun TAMAMINA uygulanır</b> — prototip tooltip'te yalnız ankrajı kelepçeliyor
/// (BuildApp.jsx:470), ki uzun bir proje adı panel kenarında hâlâ yarı yarıya taşabilir. §2.3'ün sözü daha
/// nettir: "node kenardayken bile TAMAMEN okunur". Ad etiketinde prototipin kendisi de kutuyu kelepçeler
/// (JSX:477-480), yani bu, iki yüzeyi aynı kurala oturtmaktır.</para>
/// </summary>
public static class GraphOverlay
{
    /// <summary>Tooltip'in düğümün üstünde bıraktığı boşluk (§2.3: "node'un üstünde 8px").</summary>
    public const double TooltipGapPx = 8.0;
    /// <summary>Kutunun panel kenarına yaklaşabileceği asgari mesafe (§2.3: "6px").</summary>
    public const double EdgeClampPx = 6.0;
    /// <summary>Tooltip ankrajının düğüm merkezinden yukarı kayması, düğüm kenarının katı olarak (JSX:471).</summary>
    public const double TooltipRisePerNode = 0.9;
    /// <summary>Ad etiketi ankrajının düğüm merkezinden aşağı kayması (JSX:481).</summary>
    public const double LabelDropPerNode = 0.95;
    /// <summary>Seçili düğüm ile ad etiketi arasındaki boşluk (§2.3: "altında 6px boşlukla").</summary>
    public const double LabelGapPx = 6.0;
    /// <summary>Ad etiketinin panelin altında bırakacağı pay (JSX:481) — sağ alttaki ipucu satırının bandı.</summary>
    public const double LabelBottomReservePx = 26.0;

    /// <summary>Bir içerik noktasının EKRAN karşılığı: dünya ötelemesi + kamera.</summary>
    public static Point Project(Point contentCentre, CameraTransform camera) => new(
        (contentCentre.X + QuietGraphLayout.ContentInset) * camera.Scale + camera.Tx,
        (contentCentre.Y + QuietGraphLayout.ContentInset) * camera.Scale + camera.Ty);

    /// <summary>Tooltip kutusunun SOL-ÜST köşesi: düğümün 8px üstünde, yatayda ortalanmış ve panele
    /// kelepçelenmiş.</summary>
    public static Point TooltipTopLeft(
        Point contentCentre, CameraTransform camera, double nodeSize, Size panel, Size box)
    {
        var screen = Project(contentCentre, camera);
        double top = screen.Y - nodeSize * TooltipRisePerNode * camera.Scale - TooltipGapPx - box.Height;
        return new Point(ClampHorizontally(screen.X, box.Width, panel), top);
    }

    /// <summary>Seçim ad etiketinin SOL-ÜST köşesi: düğümün 6px altında, yatayda ortalanmış ve panele
    /// kelepçelenmiş; dikeyde de panelden taşmaz.</summary>
    public static Point NameLabelTopLeft(
        Point contentCentre, CameraTransform camera, double nodeSize, Size panel, Size box)
    {
        var screen = Project(contentCentre, camera);
        double top = screen.Y + nodeSize * LabelDropPerNode * camera.Scale + LabelGapPx;
        double maxTop = Math.Max(EdgeClampPx, panel.Height - LabelBottomReservePx);
        return new Point(ClampHorizontally(screen.X, box.Width, panel), Math.Clamp(top, EdgeClampPx, maxTop));
    }

    /// <summary>Kutuyu yatayda panelin İÇİNDE tutar. Panel kutudan darsa kelepçe sol kenara oturur —
    /// taşmayı sola kaydırmak, iki yandan birden taşırmaktan iyidir.</summary>
    private static double ClampHorizontally(double centreX, double boxWidth, Size panel)
    {
        double maxLeft = Math.Max(EdgeClampPx, panel.Width - boxWidth - EdgeClampPx);
        return Math.Clamp(centreX - boxWidth / 2, EdgeClampPx, maxLeft);
    }
}
