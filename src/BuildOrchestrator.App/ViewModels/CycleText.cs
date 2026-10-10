namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [design v1.7.0 §2.4/§5] Dairesel bağımlılık metinlerinin TEK kaynağı: döngünün YOLUNU gösteren satır burada
/// üretilir; satırlar ve şeridin döngü kümesi bunu okur — iki yüzey aynı satırı kendi içinde yazsaydı sessizce
/// ayrışırlardı (kopya YASAK).
/// <para>[Build cycle derler] Eskiden burada iki sabit daha vardı (<c>Membership</c>: "standard builds skip it;
/// Resolve cycles builds it in rounds", <c>ClusterHeadline</c>: "won't be built"). Hiçbir yüzey onları okumuyordu
/// ve ikisi de düz Build'in döngüyü derlemediği kuralını anlatıyordu; kural değişince silindiler.</para>
/// </summary>
public static class CycleText
{
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
