using System.Windows;

namespace BuildOrchestrator.App.Graph;

/// <summary>[T63] Kameranın uygulanacak hâli: ölçek + (piksele yuvarlanmış) öteleme.</summary>
public readonly record struct CameraTransform(double Scale, double Tx, double Ty);

/// <summary>
/// [T63] design-v1 §2.3 kamerasının SAF aritmetiği — prototype/app/BuildApp.jsx <c>GraphPanel</c> portu.
/// Otomatik hedef: seçili düğüm → yoksa building frontier'in ağırlık merkezi → done/stopped'ta merkez →
/// aksi halde varsayılan merkez (y = H×0.3, Ek A #10). Kuşbakışı ölçek grafı panele sığdırır ve 0.68–1.08'e
/// kıstırılır; öteleme 12px kenar payıyla sınırlanır ve tam piksele yuvarlanır (Ek A #10).
///
/// <para><b>Ölçek de bir hedeftir:</b> sinema kipinde ölçeği <see cref="ResolveScale"/> seçer — seçimde sabit
/// okunur yakınlık, koşarken building frontier'ini çerçeveleyen 0.85–1.4 takip bandı, durgunda yine kuşbakışı
/// <see cref="FitScale"/>. Sinema kapalıyken ölçek HER ZAMAN <see cref="FitScale"/>'dir.</para>
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

    // ---------------------------------------------------------------- [sinema] follow-zoom (spec §3.1)

    /// <summary>Takip bandı: frontier çerçevesi bu aralığa kıstırılır (26px kare ekranda ~22–36px).</summary>
    public const double FollowMinScale = 0.85;
    public const double FollowMaxScale = 1.4;
    /// <summary>Seçimde hedef ölçek — listeden tıklanan proje okunur yakınlıkta gelir.</summary>
    public const double SelectionScale = 1.1;
    /// <summary>Ölçek Zeno eşiği: hedef bundan az değiştiyse yeniden hedefleme yok (odak 8px eşiğinin ölçek eşi).</summary>
    public const double ScaleRetargetThreshold = 0.05;
    /// <summary>Manuel (wheel) bandı — otomatik banttan geniştir; istenirse tüm siluet görülebilir.</summary>
    public const double ManualMinScale = 0.45;
    public const double ManualMaxScale = 2.0;
    /// <summary>Wheel kademesi (çarpansal).</summary>
    public const double WheelZoomStep = 1.1;
    /// <summary>Son manuel girdiden takibin kendiliğinden dönüşüne kadar geçen süre (spec §3.5).</summary>
    public const double FollowResumeDelayMs = 4000.0;
    /// <summary>Frontier bbox'ına her yana eklenen yatay pay: yarım hücre + sığdırma payı.</summary>
    public const double FrontierMarginX = GraphLayout.NodeCellWidth / 2 + FitPadding;
    /// <summary>Dikey pay: yarım kare + etiket bandı + sığdırma payı.</summary>
    public const double FrontierMarginY =
        GraphLayout.NodeSize / 2 + GraphLayout.LabelGap + GraphLayout.LabelHeight + FitPadding;

    /// <summary>Ölçek hedefi (spec §3.1 tablosu). <paramref name="previousScale"/> YALNIZ frontier dalında
    /// Zeno eşiği için kullanılır — <see cref="ResolveFocus"/>'un previousFocus sözleşmesinin ölçek eşi.
    /// <paramref name="settled"/> ölçeği DEĞİŞTİRMEZ (settled de idle de kuşbakışı fit'e döner); dal unutulmuş
    /// değildir, parametre imzayı <see cref="ResolveFocus"/> ile paritede tutar — çağıran ikisine AYNI argüman
    /// kümesini geçer. Ayrımı ODAK yapar: settled tam merkez, idle y = H×<see cref="DefaultCenterYFactor"/>.</summary>
    public static double ResolveScale(
        Size viewport, Size graph, bool cinema,
        Point? selected, IReadOnlyList<Point> building, bool settled, double? previousScale)
    {
        ArgumentNullException.ThrowIfNull(building);

        if (!cinema) return FitScale(viewport, graph);
        if (selected is not null) return SelectionScale;
        if (building.Count > 0)
        {
            double next = FrontierScale(viewport, building);
            return previousScale is { } prev && !ShouldRescale(prev, next) ? prev : next;
        }
        return FitScale(viewport, graph); // settled/idle: bugünkü kuşbakışı
    }

    /// <summary>Building merkezlerinin bbox'ını (+ hücre payları) panele çerçeveleyen ölçek, takip bandına
    /// kıstırılmış. Çok geniş cephe tabana kelepçelenir — ağırlık merkezi görünür kalır (spec §3.1).</summary>
    public static double FrontierScale(Size viewport, IReadOnlyList<Point> building)
    {
        ArgumentNullException.ThrowIfNull(building);
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        foreach (var p in building)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }

        double w = maxX - minX + 2 * FrontierMarginX;
        double h = maxY - minY + 2 * FrontierMarginY;
        return Math.Clamp(Math.Min(viewport.Width / w, viewport.Height / h), FollowMinScale, FollowMaxScale);
    }

    /// <summary>Ölçek yeterince değişti mi (frontier dalının Zeno koruması).</summary>
    public static bool ShouldRescale(double previous, double next) =>
        Math.Abs(next - previous) >= ScaleRetargetThreshold;

    /// <summary>Odağı panelin ortasına getiren transform; graf sığıyorsa eksende ortalanır, sığmıyorsa
    /// 12px kenar payıyla sınırlanır. 3-arg biçim = bugünkü fit davranışı (ölçek <see cref="FitScale"/>).</summary>
    public static CameraTransform Compute(Size viewport, Size graph, Point focus) =>
        Compute(viewport, graph, focus, FitScale(viewport, graph));

    public static CameraTransform Compute(Size viewport, Size graph, Point focus, double scale) =>
        ClampPan(viewport, graph, scale,
            viewport.Width / 2 - focus.X * scale,
            viewport.Height / 2 - focus.Y * scale);

    /// <summary>Pan kelepçesi TEK yerde: sığan eksen ortalanır, sığmayan 12px payla sınırlanır, uçlar piksele
    /// yuvarlanır. <see cref="Compute"/> ve (Task 6) manuel Pan/ZoomAt AYNI metodu kullanır — kopya yasak.</summary>
    public static CameraTransform ClampPan(Size viewport, Size graph, double scale, double tx, double ty)
    {
        double scaledW = graph.Width * scale;
        double scaledH = graph.Height * scale;

        tx = scaledW <= viewport.Width
            ? (viewport.Width - scaledW) / 2
            : Math.Min(PanMarginPx, Math.Max(viewport.Width - scaledW - PanMarginPx, tx));
        ty = scaledH <= viewport.Height
            ? (viewport.Height - scaledH) / 2
            : Math.Min(PanMarginPx, Math.Max(viewport.Height - scaledH - PanMarginPx, ty));

        return new CameraTransform(scale, RoundPixels(tx), RoundPixels(ty));
    }

    /// <summary>JS <c>Math.round</c> paritesi: .5 HER ZAMAN yukarı (+∞ yönünde). .NET'in
    /// <c>Math.Round</c>'u banker's rounding yapar — prototiple sapmamak için kullanılmaz.</summary>
    public static double RoundPixels(double value) => Math.Floor(value + 0.5);

    // ---------------------------------------------------------------- [sinema] manuel jestler (spec §3.4)

    /// <summary>[sinema] Manuel pan: <paramref name="delta"/> EKRAN pikselidir (öteleme de ekran uzayındadır,
    /// dolayısıyla ölçeğe bölünmez). Sonuç <see cref="ClampPan"/>'in sınırlarına oturur — kelepçe aritmetiği
    /// TEK yerdedir, burada kopyalanmaz.</summary>
    public static CameraTransform Pan(CameraTransform camera, Vector delta, Size viewport, Size graph) =>
        ClampPan(viewport, graph, camera.Scale, camera.Tx + delta.X, camera.Ty + delta.Y);

    /// <summary>[sinema] İmleç merkezli zoom: imlecin ALTINDAKİ dünya noktası sabit kalır — dünya noktası
    /// <c>w = (cursor − t) / s</c>, yeni öteleme <c>t' = cursor − w·s'</c>. Ölçek otomatik banda değil MANUEL
    /// banda (<see cref="ManualMinScale"/>–<see cref="ManualMaxScale"/>) kıstırılır; öteleme yine
    /// <see cref="ClampPan"/>'den geçer, yani kullanıcı grafı kenar payının dışına süremez.</summary>
    public static CameraTransform ZoomAt(
        CameraTransform camera, Point cursor, double factor, Size viewport, Size graph)
    {
        double scale = Math.Clamp(camera.Scale * factor, ManualMinScale, ManualMaxScale);
        double wx = (cursor.X - camera.Tx) / camera.Scale;
        double wy = (cursor.Y - camera.Ty) / camera.Scale;
        return ClampPan(viewport, graph, scale, cursor.X - wx * scale, cursor.Y - wy * scale);
    }
}
