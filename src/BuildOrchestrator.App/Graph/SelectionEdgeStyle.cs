using System.Windows;
using System.Windows.Media;

namespace BuildOrchestrator.App.Graph;

/// <summary>
/// [quiet] design v1.3.0 §2.3 "Seçim — odakla & sığdır": bağımlılık çizgileri YALNIZ seçimde çizilir —
/// deps→node ve node→dependents, dikey kübik bezier, amber akan kesikler
/// (prototype/app/BuildApp.jsx satır 386-396, 413 ve CSS satır 34).
///
/// <para><b>Kalıcı kenar ağı YOKTUR.</b> Seçim yokken graf çizgisizdir; bu, §2.3'ün "100+ projede bile
/// sakin, tek bakışta okunan bir yüzey" hedefinin merkezindedir ve yan etkisi olarak koşarken her tick'te
/// binlerce kenarın stillenmesini de ortadan kaldırır.</para>
///
/// <para><b>Dash birimi notu (A13.2):</b> WPF'te <c>StrokeDashArray</c>/<c>StrokeDashOffset</c> px DEĞİL
/// <c>StrokeThickness</c> ÇARPANI cinsindendir. Tasarımın MUTLAK deseni (4px dolu / 8px boş) ve mutlak yolu
/// (24px) bu yüzden kalınlığa BÖLÜNÜR — çizgi 1.2px olduğu hâlde ekranda tam 4/8 px görünür.</para>
/// </summary>
public static class SelectionEdgeStyle
{
    /// <summary>Çizgi kalınlığı (§2.3: "1.2px").</summary>
    public const double Thickness = 1.2;
    /// <summary>Çizgi opaklığı (§2.3: "opacity 0.75").</summary>
    public const double Opacity = 0.75;
    /// <summary>Bir turda alınan MUTLAK yol (§2.3: "offset −24").</summary>
    public const double FlowTravelPx = 24.0;
    /// <summary>Bir turun süresi (§2.3: "640ms linear infinite").</summary>
    public const double FlowDurationMs = 640.0;
    /// <summary>Çizgi fırçasının token anahtarı — hex DEĞİL.</summary>
    public const string BrushKey = "Brush.Amber";

    /// <summary>Tasarımın MUTLAK dash deseni (§2.3: "dasharray 4 8").</summary>
    public static readonly IReadOnlyList<double> AbsoluteDash = [4.0, 8.0];

    /// <summary>Deseni kalınlığa BÖLÜNMÜŞ hâliyle verir ⇒ mutlakta yine 4px/8px. DONMUŞ, paylaşımlı.</summary>
    public static DoubleCollection DashArray { get; } = FrozenDivided(AbsoluteDash, Thickness);

    /// <summary>Akan kesiğin hedef offset'i: mutlak yol / kalınlık (aynı bölme kuralı).</summary>
    public static double DashOffsetTarget => -FlowTravelPx / Thickness;

    /// <summary>
    /// İki düğüm arasındaki DİKEY kübik bezier — kontrol noktaları iki ucun ORTA YÜKSEKLİĞİNDEDİR
    /// (JSX:391: <c>M x1 y1 C x1 my, x2 my, x2 y2</c>, <c>my = (y1+y2)/2</c>). Uçlar DÜNYA koordinatıdır.
    /// Donmuş döner — seçim boyunca geometri değişmez.
    /// </summary>
    public static Geometry Curve(Point from, Point to)
    {
        double midY = (from.Y + to.Y) / 2;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(from, isFilled: false, isClosed: false);
            ctx.BezierTo(new Point(from.X, midY), new Point(to.X, midY), to, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static DoubleCollection FrozenDivided(IReadOnlyList<double> absolute, double thickness)
    {
        var dash = new DoubleCollection(absolute.Select(v => v / thickness));
        dash.Freeze();
        return dash;
    }
}
