using System.Globalization;
using System.Linq;
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
    /// <summary>[review R1 finding 3 — kopya YASAK] "Dependency issue: " önce satırın <see cref="For"/>'unda
    /// İKİ, sonra başlığın <see cref="DepIssueDetail"/>'inde bir kez daha literal olarak yazılıyordu — üçü de
    /// AYNI sözcüğü taşıdığı için tek kaynağa indirildi. <b>[Task 4 review — M3]</b> <c>internal</c>: <see
    /// cref="WaitingForDependencyText"/> de AYNI kelimeyi kullanır — kayıtlı kökler her zaman "FAILED" değildir
    /// (tek-proje koşusunun bayat bıraktığı bir bağımlılık da kök olabilir, bkz. <c>ProjectRunScope</c>),
    /// "Built against a FAILED dependency" iddiası orada yanlıştı.</summary>
    internal const string DepIssuePrefix = "Dependency issue: ";

    /// <summary>[Task 6 review round 1 — kopya YASAK] <see cref="WaitingForDependencyText"/>'in İKİ çıkışı
    /// (kök varken kuyruk, kök yokken tek başına cümle) bu tek cümlenin İKİ ayrı yazımıydı — kökler varken
    /// küçük harfle kuyruk, yokken büyük harfle tek cümle. Artık İKİSİ de bu TEK sabitten türer. Brief metni
    /// "…once it is healthy again" öneriyordu; MEVCUT ifade korundu (kullanıcı kararı — anlam değişmiyor).</summary>
    private const string HealthyAgainClause = "rebuilds once that dependency is healthy again";

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
            ? DepIssuePrefix + first
            : string.Format(CultureInfo.InvariantCulture, "{0}{1} +{2}", DepIssuePrefix, first, depIssues.Count - 1);
    }

    /// <summary>[Task 6 — design v1.20.0 §2.4] Proje sayfasının "bekliyor" cümlesi: <see cref="DepIssuePrefix"/>
    /// + kökler (virgülle, ortak önek kısaltılarak) + sabit kuyruk. TEK kaynak: <see cref="Console.ConsoleEmptyState"/>'in
    /// hem <c>Pending</c> hem <c>Skipped</c> dalı buradan okur (kopya YASAK) — eskiden bu cümleyi
    /// <c>DecisionLabel.For</c>'un <c>WaitingForDependency</c> dalı üretiyordu, o dal artık <c>UpToDate</c> ile
    /// birleşti (bkz. <see cref="DecisionLabel"/>'in sınıf özeti).
    ///
    /// <para><b>[Task 6 review round 1 — DÜZELTME]</b> Bu cümle üçgenin (<see cref="For"/>) tooltip'iyle AYNI
    /// DEĞİLDİR — yalnız kök adlandırma DİLİ ortak (<see cref="DepIssuePrefix"/> + <see cref="GraphNode.ShortLabel"/>
    /// ile kısaltma). Üçgen daraltılmış slot için kısaltır (<c>Dependency issue: Sales.Core +2</c>, ilk ad + kalan
    /// sayısı); bu metin geniş sayfa alanında TÜM kökleri virgülle yazar ve bekleme kuyruğunu ekler — biri
    /// diğerinin kopyası değil, ikisi de aynı üç parçadan (önek, kısaltma, kuyruk) farklı BİÇİMLER üretir.</para>
    /// </summary>
    /// <param name="dependencyRoots">Bekleyen kök adları; boş/null ise (savunmacı) parantezsiz nötr cümle.</param>
    /// <param name="namePrefix">Kök adlarının kısaltılacağı ortak önek.</param>
    public static string WaitingForDependencyText(IReadOnlyList<string>? dependencyRoots, string namePrefix)
    {
        if (dependencyRoots is not { Count: > 0 })
            return char.ToUpperInvariant(HealthyAgainClause[0]) + HealthyAgainClause[1..];

        string names = string.Join(", ", dependencyRoots.Select(r => GraphNode.ShortLabel(r, namePrefix)));
        return DepIssuePrefix + names + " — " + HealthyAgainClause;
    }

    /// <summary>[v1.18.0 §9] Konsol başlığının dep-issue rozeti — satırın "+N" kısaltmasının AKSİNE tam
    /// listeyi virgülle yazar (prototip <c>BuildApp.jsx:2616</c>: <c>depIssue.map(shortName).join(', ')</c>).
    /// Kısaltma (<see cref="For"/>) daraltılmış slot içindir; başlığın tooltip'inde yer bol olduğu için
    /// hiçbir proje adı gizlenmez. Kısa-ad türetimi AYNI otoriteden gelir (<see cref="GraphNode.ShortLabel"/>,
    /// kopya YASAK).</summary>
    public static string DepIssueDetail(IReadOnlyList<string> depIssues, string namePrefix)
    {
        ArgumentNullException.ThrowIfNull(depIssues);
        string names = string.Join(", ", depIssues.Select(n => GraphNode.ShortLabel(n, namePrefix)));
        return DepIssuePrefix + names + " — last successful output referenced";
    }
}
