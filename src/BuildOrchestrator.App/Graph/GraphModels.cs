using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.App.Graph;

// [D1 fold — B3 review] GraphStatus enum'ı nötr Controls namespace'ine taşındı (Controls/GraphStatus.cs) —
// StatusGlyph'in graf-dışı ilk tüketicisi D1'dir. Buradaki kullanımlar için yukarıdaki using yeterlidir.

/// <summary>
/// [quiet] Graf düğümü — kimlik (tam proje adı), katman indeksi ve statü. Hepsi bu kadardır.
///
/// <para><b>Ne taşımadığı da bir karardır.</b> v1.3.0 §2.3'te düğümün üstünde ad etiketi yoktur (ad hover
/// tooltip'i ve seçim etiketiyle verilir) ve graf içi dep-issue rozeti kaldırılmıştır (dep bilgisi liste
/// kartlarında yaşar). Bu yüzden kısa-ad öneki ve <c>HasDepIssue</c> bayrağı bu kayıttan SÖKÜLDÜ — grafta
/// hiçbir okuyucuları kalmamıştı.</para>
/// </summary>
public sealed record GraphNode(string Name, int Layer, GraphStatus Status)
{
    /// <summary>[D5] Ortak öneği atılmış kısa ad. Grafın kendisi ARTIK kullanmaz (§2.3: node üstü etiket
    /// yok) ama proje adını dar bir yerde gösteren diğer yüzeyler kullanır: liste kartının dep-tooltip'i
    /// (<c>ProjectRow</c>) ve şeritteki building chip'leri (<c>StickyRibbon</c>). Önek HARDCODED değildir —
    /// <see cref="CommonDotPrefix"/> ile workspace proje adlarından türetilir; TEK önek otoritesi odur.</summary>
    public static string ShortLabel(string fullName, string prefix) =>
        prefix.Length > 0 && fullName.StartsWith(prefix, StringComparison.Ordinal)
            ? fullName[prefix.Length..] : fullName;

    /// <summary>[D5] Verilen adların en uzun ortak NOKTA-SINIRLI öneki (sondaki nokta DAHİL). Örn. tümü
    /// <c>OSYS.</c> altındaysa → <c>"OSYS."</c>; ortak nokta-segmenti yoksa → <c>""</c>. Bir adın TAMAMI asla önek
    /// olamaz (her adın son/yaprak segmenti hariç tutulur) — aksi halde etiket boşalırdı. Hardcode edilmiş
    /// <c>"OSYS."</c>'in yerini alan TEK önek otoritesidir.</summary>
    public static string CommonDotPrefix(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Count == 0) return "";

        string[]? common = null;
        foreach (var name in names)
        {
            var parts = name.Split('.');
            int usable = parts.Length - 1; // yaprak segment (son ad) önek olamaz
            if (usable <= 0) return "";    // noktasız bir ad varsa hiç ortak önek olamaz
            if (common is null) { common = parts[..usable]; continue; }

            int max = Math.Min(common.Length, usable);
            int i = 0;
            while (i < max && string.Equals(common[i], parts[i], StringComparison.Ordinal)) i++;
            if (i == 0) return "";
            common = common[..i];
        }
        return common is { Length: > 0 } ? string.Join('.', common) + "." : "";
    }
}

/// <summary>[T63] Bağımlılık kenarı: <paramref name="From"/> (bağımlılık) → <paramref name="To"/> (bağımlı proje);
/// prototype <c>GRAPH.edges</c> ile AYNI yön (yukarıdan aşağı).</summary>
public sealed record GraphEdge(string From, string To);
