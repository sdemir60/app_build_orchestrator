using System.Globalization;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [design v1.11.0 §2.4-6 · §9-8] Proje satırındaki <b>TEK uyarı üçgeninin</b> metni. Üçgen statüden
/// bağımsızdır, sabit 14px slotta durur ve <b>HER ZAMAN AMBERDIR</b>; tooltip'i <b>TEK SATIRDIR</b>.
///
/// <para><b>[DEĞİŞEN KURAL]</b> v1.7.0'da aynı slot iki bilgiyi RENKLE ayırıyordu (yapısal döngü → turuncu,
/// geçici dep-issue → amber) ve tooltip nedenleri ALT ALTA listeliyordu — döngü cümlesi + döngü yolu +
/// dep-issue satırı. v1.11.0 turuncuyu UI'dan çıkardı ve tooltip'i tek satıra indirdi: <i>döngü yolu, üye
/// listesi ve gerekçe proje logundadır</i> (<c>skipped — in a dependency cycle</c>,
/// <c>built with last good output of X</c>). Üçgen "bir şey ters" der; ayrıntıyı log söyler.</para>
///
/// <para>Sınıf SAFtır (WPF'siz test edilir): kart yalnız uygular.</para>
/// </summary>
public static class RowWarning
{
    /// <summary>Sıradan döngü üyeliği (prototip <c>warnText</c>, BuildApp.jsx:583).</summary>
    public const string InCycle = "In a dependency cycle";

    /// <summary>[cycle rounds] Grup tur tavanına dayanarak bitti: derleme başarılı ama çıktı bir kuşak geride
    /// OLABİLİR. Tek satır kalır — v1.11.0'ın kuralı metni kısaltmak değil, SATIR SAYISINI birde tutmaktır;
    /// bu cümle üyelikten daha KESİN bir şey söyler ve onu ezer.</summary>
    public const string CycleUnsettled = "Cycle did not fully settle — output may be one generation stale";

    /// <summary>[cycle rounds] Grup yakınsamadı: üyeler hâlâ güncel değil. "Bir daha denenmez" DEMEZ — açık bir
    /// Resolve basışı grubu her zaman yeniden dener.</summary>
    public const string CycleUnconverged = "Cycle did not converge — its projects are still out of date";

    /// <summary>Satırın uyarı metni; uyarı yoksa <c>null</c> (üçgen çizilmez).
    /// <para>Öncelik EN KESİNDEN en genele: yakınsamama → oturmama → sıradan üyelik → dep-issue. Döngü
    /// üyeliği dep-issue'yu ezer (prototip de öyle yapar): yapısal olan, geçici olandan önce gelir.</para></summary>
    /// <param name="namePrefix">Veri-türevli ortak ad öneki (ör. <c>OSYS.</c>) — dep adları kısaltılırken atılır.</param>
    public static string? For(bool inCycle, bool cycleUnsettled, bool cycleUnconverged,
        IReadOnlyList<string>? depIssues, string namePrefix)
    {
        if (cycleUnconverged) return CycleUnconverged;
        if (cycleUnsettled) return CycleUnsettled;
        if (inCycle) return InCycle;
        if (depIssues is not { Count: > 0 }) return null;

        // `Dependency issue: Sales.Core +2` — İLK adın kısası + kalanların SAYISI. Tam liste proje logundadır.
        string first = GraphNode.ShortLabel(depIssues[0], namePrefix);
        return depIssues.Count == 1
            ? "Dependency issue: " + first
            : string.Format(CultureInfo.InvariantCulture, "Dependency issue: {0} +{1}", first, depIssues.Count - 1);
    }
}
