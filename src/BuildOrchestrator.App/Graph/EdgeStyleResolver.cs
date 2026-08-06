using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.App.Graph;

/// <summary>
/// [T63] Bir kenarın çizim stili: fırça ANAHTARI (hex DEĞİL — Foundation <c>Tokens.xaml</c>'dan
/// <c>SetResourceReference</c> ile çözülür), kalınlık, opaklık, dash deseni ve akan mı.
/// </summary>
/// <param name="Dash">
/// Dash deseni <b>StrokeThickness ÇARPANI</b> cinsindendir (WPF'te dash birimi px DEĞİL — feasibility §3.4,
/// doğrulanmış). A13.2'nin İKİ kuralı da harfiyen uygulanır:
/// <list type="number">
/// <item><b>1.6px seçili kenarda değerler BÖLÜNÜR</b> — desen 1px'te <c>{4,7}</c>, 1.6px'te <c>{4/1.6, 7/1.6}</c>
/// ⇒ MUTLAK desen her iki kalınlıkta da 4px dolu / 7px boş (tasarımın <c>stroke-dasharray: 4 7</c>'si).</item>
/// <item><b>Tüm akan kenarlar TEK paylaşımlı clock'a bağlanır</b> — bölünmüş desenin periyodu da bölündüğü için
/// (11 → 6.875 çarpan-birimi) offset hedefi de bölünür: <see cref="FlowDashOffsetFor"/> = −22/kalınlık. İki
/// kalınlık da 0.9s'de <b>22px MUTLAK</b> yol alır ve her biri KENDİ deseninin tam 2 periyodunu tamamlar ⇒
/// faz-kilitli, dikişsiz.</item>
/// </list>
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

    /// <summary>Seçim-dim VE sinema sisi ortak opaklığı (design-v1 §2.3 `op .16`; kopya yasak — iki kural
    /// da BU sabiti okur).</summary>
    public const double DimmedOpacity = 0.16;
    /// <summary>[sinema] Succeeded/failed'e varan renkli kenarın sis opaklığı — biten bölge sakinleşir ama
    /// hikâye silinmez (spec §3.2).</summary>
    public const double FogFinishedOpacity = 0.35;

    /// <summary>Akan dash'in bir turda aldığı MUTLAK yol (px) — mutlak desenin (4+7=11px) tam 2 periyodu ⇒
    /// dikişsiz loop, HER kalınlıkta (feasibility §3.4 / A13.2).</summary>
    public const double FlowTravelPx = 22.0;
    /// <summary>Akan dash'in bir tur süresi (design-v1 §2.3: "dash 4 7, 0.9s kayar").</summary>
    public const double FlowDurationMs = 900.0;

    /// <summary>Akan (hedefi building) 1px kenarın dash deseni — bkz. <see cref="EdgeStyle.Dash"/> birim notu.</summary>
    public static readonly IReadOnlyList<double> FlowDash = [4.0, 7.0];
    /// <summary>Aynı desenin 1.6px kenar için BÖLÜNMÜŞ hâli (A13.2) ⇒ mutlakta yine 4px/7px.</summary>
    public static readonly IReadOnlyList<double> FlowDashThick = [4.0 / SelectedThickness, 7.0 / SelectedThickness];
    /// <summary>Hatanın taşındığı dalın STATİK dash deseni (design-v1 §2.3: "kırmızı, statik kesikli 3 4").</summary>
    public static readonly IReadOnlyList<double> ErrorDash = [3.0, 4.0];
    /// <summary>Statik hata deseninin 1.6px için BÖLÜNMÜŞ hâli (A13.2) ⇒ mutlakta yine 3px/4px. Bu desen HİÇ
    /// clock'a bağlanmaz (<c>IsFlowing=false</c>), dolayısıyla tek-clock gerekçesi buraya hiç uygulanmaz.</summary>
    public static readonly IReadOnlyList<double> ErrorDashThick = [3.0 / SelectedThickness, 4.0 / SelectedThickness];

    /// <summary>Akan dash'in verilen kalınlıktaki hedef offset'i: −<see cref="FlowTravelPx"/>/kalınlık. Dash birimi
    /// kalınlık çarpanı olduğundan bu, HER kalınlıkta aynı 22px MUTLAK yola ve o kalınlığın (bölünmüş) deseninin
    /// tam 2 periyoduna denk gelir — paylaşılan tek clock'un iki dalı bu yüzden faz-kilitli kalır.</summary>
    public static double FlowDashOffsetFor(double thickness) => -FlowTravelPx / thickness;

    /// <summary>Verilen kalınlık için doğru (gerekirse bölünmüş) desen örneğini seçer. Dönen örnekler STATİK'tir —
    /// <c>GraphView</c>'ın "stil değişmediyse kablajı yenileme" hızlı yolu referans eşitliğine dayanır.</summary>
    private static IReadOnlyList<double> DashFor(double thickness, IReadOnlyList<double> thin, IReadOnlyList<double> thick)
        => thickness == SelectedThickness ? thick : thin;

    /// <summary>
    /// Prototipteki zincirin portu (sıra önemlidir — sonraki dallar öncekini ezer). Zincir BİREBİR aynıdır;
    /// tek eklenti <c>fogged</c>'dir — prototipte YOKTUR, büyük graf için app tarafında eklenmiştir ([sinema]
    /// işaretli iki satır). <c>fogged=false</c> iken çıktı prototiple birebir aynı kalır:
    /// <code>
    /// let stroke = border, w = 1, op = (selected || fogged) ? .16 : .8   // [sinema] sis, seçim-dim seviyesine iner
    /// if (flow)            → amber, op .2/.85, akan
    /// else if (succeeded)  → success-border
    /// else if (failed)     → fail-border
    /// if (!selected && fogged && (succeeded || failed)) → op .35         // [sinema] biten dal sakinleşir, silinmez
    /// if (bad)             → fail-border, op .3/.95, (akmıyorsa) statik dash 3 4
    /// if (hot)             → bad ? fail-border : amber-border, w 1.6, op 1, (bad değilse) akış İPTAL
    /// </code>
    /// </summary>
    /// <param name="source">Kenarın kaynağı (bağımlılık) — "hata bu daldan aşağı taşınıyor mu" kararını verir.</param>
    /// <param name="sourceHasDepIssue">Kaynak düğüm bir dep-hatası taşıyor mu (failed olmasa bile).</param>
    /// <param name="target">Kenarın hedefi (bağımlı proje) — akış/renk kararını verir.</param>
    /// <param name="touchesSelection">Kenarın iki ucundan biri seçili düğüm mü.</param>
    /// <param name="hasSelection">Grafta herhangi bir seçim var mı (seçim yokken "hot" hiç oluşmaz, sönme de).</param>
    /// <param name="fogged">[sinema] Büyük grafta koşuya karışmamış kenar geri çekilsin mi — YALNIZ seçim
    /// yokken etkilidir; koşu hikâyesine (akan/hata) hiç dokunmaz.</param>
    public static EdgeStyle Resolve(
        GraphStatus source, bool sourceHasDepIssue, GraphStatus target, bool touchesSelection, bool hasSelection,
        bool fogged = false)
    {
        bool flow = target == GraphStatus.Building;
        bool bad = source == GraphStatus.Failed || sourceHasDepIssue;
        bool hot = hasSelection && touchesSelection;

        string brushKey = "Brush.Border";
        double thickness = DefaultThickness;
        // [sinema] Sis, seçim-dim ile AYNI seviyeye iner — koşuya karışmamış kenar büyük grafta fısıltıdır.
        double opacity = hasSelection || fogged ? DimmedOpacity : 0.8;
        bool hasErrorDash = false;
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

        // [sinema] Biten dallar sisi TEK yerde alır (kopya yasak). Zincirden sonra ama bad/hot'tan ÖNCE durur —
        // o iki blok bu değeri ezmeye devam eder (davranış birebir aynı: `flow` ile succeeded/failed ayrık
        // kümelerdir, dolayısıyla koşul yalnız zincirin o iki dalının girdiği durumlarda doğrudur).
        if (!hasSelection && fogged && target is GraphStatus.Succeeded or GraphStatus.Failed)
            opacity = FogFinishedOpacity;

        if (bad)
        {
            brushKey = "Brush.StatusFailBorder";
            opacity = hasSelection ? 0.3 : 0.95;
            if (!flow)
                hasErrorDash = true; // akmayan hata dalı: statik kesikli
        }

        if (hot)
        {
            brushKey = bad ? "Brush.StatusFailBorder" : "Brush.AmberBorder";
            thickness = SelectedThickness;
            opacity = 1.0;
            if (!bad)
                isFlowing = false; // seçime değen SAĞLIKLI kenar düz çizilir (prototip: `cls = undefined`)
        }

        // Desen SEÇİMİ en sona bırakılır: doğru (bölünmüş) örnek ancak `hot` kalınlığı kesinleştirdikten sonra
        // bilinebilir (A13.2 "1.6px seçili kenarda değerler bölünür").
        IReadOnlyList<double>? dash =
            isFlowing ? DashFor(thickness, FlowDash, FlowDashThick) :
            hasErrorDash ? DashFor(thickness, ErrorDash, ErrorDashThick) :
            null;

        return new EdgeStyle(brushKey, thickness, opacity, dash, isFlowing);
    }
}
