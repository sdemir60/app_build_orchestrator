using System.Windows;

namespace BuildOrchestrator.App.Graph;

/// <summary>[T63] Kameranın uygulanacak hâli: ölçek + (piksele yuvarlanmış) öteleme.</summary>
public readonly record struct CameraTransform(double Scale, double Tx, double Ty);

/// <summary>
/// [T63] design-v1 §2.3 kamerasının SAF aritmetiği — prototype/app/BuildApp.jsx <c>GraphPanel</c> portu.
/// Otomatik hedef: seçili düğüm → yoksa building frontier'in ağırlık merkezi → done/stopped'ta merkez →
/// aksi halde varsayılan merkez (y = H×0.3, Ek A #10). Ölçek panele sığdırılır ve 0.68–1.08'e kıstırılır;
/// öteleme 12px kenar payıyla sınırlanır ve tam piksele yuvarlanır (Ek A #10).
///
/// <para><b>Yuvarlama ne zaman:</b> dönen <see cref="CameraTransform"/> animasyonun HEDEFİDİR — yuvarlama
/// yalnız uçta uygulanır, ara karelerde DEĞİL (A13.2: sırasında yuvarlamak titretir). CSS'te de aynıdır:
/// tarayıcı yuvarlanmış iki uç arasında pürüzsüz interpolasyon yapar.</para>
/// </summary>
public static class GraphCamera
{
    public const double MinScale = 0.68;
    public const double MaxScale = 1.08;
    /// <summary>Sığdırmada tuvale eklenen pay (prototip: <c>dim.w / (G.W + 30)</c>).</summary>
    public const double FitPadding = 30.0;
    /// <summary>Pan sınırlarında bırakılan kenar payı (Ek A #10).</summary>
    public const double PanMarginPx = 12.0;
    /// <summary>Varsayılan (seçim/frontier yokken, koşu bitmemişken) dikey odak oranı (Ek A #10).</summary>
    public const double DefaultCenterYFactor = 0.3;
    /// <summary>Frontier ağırlık merkezi bu kadar kaymadıysa kamera YENİDEN hedeflenmez (feasibility §3.4).</summary>
    public const double FrontierRetargetThresholdPx = 8.0;
    /// <summary>Kamera geçişinin süresi (design-v1 §2.3: "transform 460ms ease-in-out").</summary>
    public const double TransitionMs = 460.0;

    /// <summary>Grafı panele sığdıran ölçek, 0.68–1.08 bandına kıstırılmış.</summary>
    public static double FitScale(Size viewport, Size graph) => Math.Max(
        MinScale,
        Math.Min(MaxScale, Math.Min(
            viewport.Width / (graph.Width + FitPadding),
            viewport.Height / (graph.Height + FitPadding))));

    /// <summary>Frontier ağırlık merkezi yeterince kaydı mı (küçük sapmada kamera sabit kalır).</summary>
    public static bool ShouldRetarget(Point previous, Point next) =>
        (next - previous).Length >= FrontierRetargetThresholdPx;

    /// <summary>
    /// Kameranın odaklanacağı graf-koordinat noktası.
    /// </summary>
    /// <param name="selected">Seçili düğümün merkezi (yoksa null) — varsa her zaman kazanır, eşik uygulanmaz.</param>
    /// <param name="building">O an derlenen düğümlerin merkezleri — ağırlık merkezleri hedef olur.</param>
    /// <param name="settled">Koşu bitti/durduruldu mu (done/stopped → tam merkez).</param>
    /// <param name="previousFocus">Bir önceki odak — YALNIZ frontier dalında küçük sapma eşiği için kullanılır.</param>
    public static Point ResolveFocus(
        Point? selected, IReadOnlyList<Point> building, bool settled, Size graph, Point? previousFocus)
    {
        ArgumentNullException.ThrowIfNull(building);

        if (selected is { } node)
            return node;

        if (building.Count > 0)
        {
            var cog = new Point(building.Average(p => p.X), building.Average(p => p.Y));
            // Frontier her tick'te birkaç piksel oynar; her oynamada 460ms'lik bir geçiş başlatmak kamerayı
            // titretir — eşiğin altındaki sapmalarda ESKİ odak korunur (feasibility §3.4).
            return previousFocus is { } prev && !ShouldRetarget(prev, cog) ? prev : cog;
        }

        return settled
            ? new Point(graph.Width / 2, graph.Height / 2)
            : new Point(graph.Width / 2, graph.Height * DefaultCenterYFactor);
    }

    /// <summary>Odağı panelin ortasına getiren transform; graf sığıyorsa eksende ortalanır, sığmıyorsa
    /// 12px kenar payıyla sınırlanır.</summary>
    public static CameraTransform Compute(Size viewport, Size graph, Point focus)
    {
        double s = FitScale(viewport, graph);
        double scaledW = graph.Width * s;
        double scaledH = graph.Height * s;

        double tx = viewport.Width / 2 - focus.X * s;
        double ty = viewport.Height / 2 - focus.Y * s;

        tx = scaledW <= viewport.Width
            ? (viewport.Width - scaledW) / 2
            : Math.Min(PanMarginPx, Math.Max(viewport.Width - scaledW - PanMarginPx, tx));
        ty = scaledH <= viewport.Height
            ? (viewport.Height - scaledH) / 2
            : Math.Min(PanMarginPx, Math.Max(viewport.Height - scaledH - PanMarginPx, ty));

        return new CameraTransform(s, RoundPixels(tx), RoundPixels(ty));
    }

    /// <summary>JS <c>Math.round</c> paritesi: .5 HER ZAMAN yukarı (+∞ yönünde). .NET'in
    /// <c>Math.Round</c>'u banker's rounding yapar — prototiple sapmamak için kullanılmaz.</summary>
    public static double RoundPixels(double value) => Math.Floor(value + 0.5);
}
