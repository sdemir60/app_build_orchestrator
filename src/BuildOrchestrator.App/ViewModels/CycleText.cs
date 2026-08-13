namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [design v1.7.0 §2.4/§5] Dairesel bağımlılık metinlerinin TEK kaynağı. Döngüyü ANLATAN cümle ile döngünün
/// YOLUNU gösteren satır burada üretilir; nokta, uyarı üçgeni ve şeridin döngü kümesi hepsi bunu okur —
/// üç yüzey aynı cümleyi kendi içinde yazsaydı sessizce ayrışırlardı (kopya YASAK).
/// </summary>
public static class CycleText
{
    /// <summary>
    /// Üyeliğin ne demek olduğu. Kaç tur süreceğini SÖYLEMEZ: tur sayısı motorun kararıdır
    /// (<c>CycleRoundPolicy</c> yakınsamaya göre 2–3 tur koşar) ve arayüz sabit bir sayı vaat edemez.
    /// </summary>
    public const string Membership =
        "In a dependency cycle — standard builds skip it; Resolve cycles builds it in rounds";

    /// <summary>Şeridin döngü kümesinin ilk satırı (§2.2): kümenin ne anlattığı.</summary>
    public const string ClusterHeadline = "In a dependency cycle — won't be built";

    /// <summary>
    /// Döngünün yolu: <c>A → B → C → A</c>. Halka KAPATILIR (ilk üye sona tekrar yazılır) — döngü olduğunu
    /// gösteren şey tam olarak budur; kapatılmazsa okuyan sıradan bir zincir görür.
    /// </summary>
    /// <param name="memberNames">Üye adları, döngüdeki sıralarıyla.</param>
    /// <returns>Yol metni; üye yoksa boş dize.</returns>
    public static string Path(IReadOnlyList<string> memberNames)
    {
        ArgumentNullException.ThrowIfNull(memberNames);
        if (memberNames.Count == 0) return "";
        return string.Join(" → ", memberNames) + " → " + memberNames[0];
    }

    /// <summary>Bir tooltip gövdesi: satırlar alt alta, boş olanlar atlanır.</summary>
    public static string Lines(params string?[] lines) =>
        string.Join(Environment.NewLine, lines.Where(l => !string.IsNullOrEmpty(l)));
}
