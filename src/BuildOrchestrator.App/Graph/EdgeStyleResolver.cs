namespace BuildOrchestrator.App.Graph;

/// <summary>
/// [T63] Bir kenarın çizim stili: fırça ANAHTARI (hex DEĞİL — Foundation <c>Tokens.xaml</c>'dan
/// <c>SetResourceReference</c> ile çözülür), kalınlık, opaklık, dash deseni ve akan mı.
/// </summary>
/// <param name="Dash">
/// Dash deseni <b>StrokeThickness ÇARPANI</b> cinsindendir (WPF'te dash birimi px DEĞİL — feasibility §3.4,
/// doğrulanmış). 1px kalınlıkta <c>{4,7}</c> tasarımın <c>stroke-dasharray: 4 7</c>'siyle birebir; 1.6px'lik
/// (seçime değen) kenarda desen 1.6× büyür — bu bilinçli bir kabuldür: desen çarpan-birimi olduğu için periyot
/// (4+7=11) HER kalınlıkta aynıdır ve <see cref="FlowDashOffsetTo"/>=-22 tam 2 periyottur, dolayısıyla iki
/// kalınlıktaki akan kenarlar <b>TEK paylaşımlı clock</b>'a bağlanabilir ve dikiş görünmez (A13.2 şartı).
/// Deseni mutlak px'e sabitlemek için kalınlığa bölmek gerekirdi; o durumda periyot kalınlığa göre değişir ve tek
/// clock imkânsız hale gelirdi — bu yüzden REDDEDİLDİ.
/// </param>
public sealed record EdgeStyle(
    string BrushKey,
    double Thickness,
    double Opacity,
    IReadOnlyList<double>? Dash,
    bool IsFlowing);

/// <summary>
/// [T63] design-v1 §2.3 kenar stili — prototype/app/BuildApp.jsx <c>GraphPanel</c> içindeki if-zincirinin SAF,
/// birim-testlenebilir portu (feasibility §3.5). WPF'e hiç dokunmaz; yalnız token ANAHTARLARI döner.
/// </summary>
public static class EdgeStyleResolver
{
    public const double DefaultThickness = 1.0;
    /// <summary>Seçili düğüme değen kenarın kalınlığı (design-v1 §2.3: "1.6px, tam opak").</summary>
    public const double SelectedThickness = 1.6;

    /// <summary>Akan dash'in hedef offset'i — desenin (4+7=11) tam 2 periyodu ⇒ dikişsiz loop (feasibility §3.4).</summary>
    public const double FlowDashOffsetTo = -22.0;
    /// <summary>Akan dash'in bir tur süresi (design-v1 §2.3: "dash 4 7, 0.9s kayar").</summary>
    public const double FlowDurationMs = 900.0;

    /// <summary>Akan (hedefi building) kenarın dash deseni — bkz. <see cref="EdgeStyle.Dash"/> birim notu.</summary>
    public static readonly IReadOnlyList<double> FlowDash = [4.0, 7.0];
    /// <summary>Hatanın taşındığı dalın STATİK dash deseni (design-v1 §2.3: "kırmızı, statik kesikli 3 4").</summary>
    public static readonly IReadOnlyList<double> ErrorDash = [3.0, 4.0];

    /// <summary>
    /// Prototipteki zincirin BİREBİR portu (sıra önemlidir — sonraki dallar öncekini ezer):
    /// <code>
    /// let stroke = border, w = 1, op = selected ? .16 : .8
    /// if (flow)            → amber, op .2/.85, akan
    /// else if (succeeded)  → success-border
    /// else if (failed)     → fail-border
    /// if (bad)             → fail-border, op .3/.95, (akmıyorsa) statik dash 3 4
    /// if (hot)             → bad ? fail-border : amber-border, w 1.6, op 1, (bad değilse) akış İPTAL
    /// </code>
    /// </summary>
    /// <param name="source">Kenarın kaynağı (bağımlılık) — "hata bu daldan aşağı taşınıyor mu" kararını verir.</param>
    /// <param name="sourceHasDepIssue">Kaynak düğüm bir dep-hatası taşıyor mu (failed olmasa bile).</param>
    /// <param name="target">Kenarın hedefi (bağımlı proje) — akış/renk kararını verir.</param>
    /// <param name="touchesSelection">Kenarın iki ucundan biri seçili düğüm mü.</param>
    /// <param name="hasSelection">Grafta herhangi bir seçim var mı (seçim yokken "hot" hiç oluşmaz, sönme de).</param>
    public static EdgeStyle Resolve(
        GraphStatus source, bool sourceHasDepIssue, GraphStatus target, bool touchesSelection, bool hasSelection)
    {
        bool flow = target == GraphStatus.Building;
        bool bad = source == GraphStatus.Failed || sourceHasDepIssue;
        bool hot = hasSelection && touchesSelection;

        string brushKey = "Brush.Border";
        double thickness = DefaultThickness;
        double opacity = hasSelection ? 0.16 : 0.8;
        IReadOnlyList<double>? dash = null;
        bool isFlowing = false;

        if (flow)
        {
            brushKey = "Brush.Amber";
            opacity = hasSelection ? 0.2 : 0.85;
            isFlowing = true;
        }
        else if (target == GraphStatus.Succeeded)
        {
            brushKey = "Brush.StatusSuccessBorder";
        }
        else if (target == GraphStatus.Failed)
        {
            brushKey = "Brush.StatusFailBorder";
        }

        if (bad)
        {
            brushKey = "Brush.StatusFailBorder";
            opacity = hasSelection ? 0.3 : 0.95;
            if (!flow)
                dash = ErrorDash; // akmayan hata dalı: statik kesikli
        }

        if (hot)
        {
            brushKey = bad ? "Brush.StatusFailBorder" : "Brush.AmberBorder";
            thickness = SelectedThickness;
            opacity = 1.0;
            if (!bad)
                isFlowing = false; // seçime değen SAĞLIKLI kenar düz çizilir (prototip: `cls = undefined`)
        }

        if (isFlowing)
            dash = FlowDash;

        return new EdgeStyle(brushKey, thickness, opacity, dash, isFlowing);
    }
}
