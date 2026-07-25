using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.App.Graph;

// [D1 fold — B3 review] GraphStatus enum'ı nötr Controls namespace'ine taşındı (Controls/GraphStatus.cs) —
// StatusGlyph'in graf-dışı ilk tüketicisi D1'dir. Buradaki kullanımlar için yukarıdaki using yeterlidir.

/// <summary>[T63] Graf düğümü — kimlik (tam proje adı), katman indeksi, statü, dep-hata bayrağı ve
/// (D5) etiketten atılacak veri-türevli ortak önek (<see cref="Prefix"/>).</summary>
public sealed record GraphNode(string Name, int Layer, GraphStatus Status, bool HasDepIssue = false, string Prefix = "")
{
    /// <summary>[D5] design-v1 §2.3: düğüm etiketinde ortak önek atılır (prototype <c>BO.shortName</c> portu).
    /// Önek artık HARDCODED <c>"OSYS."</c> DEĞİL: <paramref name="prefix"/> workspace proje adlarından türetilir
    /// (<see cref="CommonDotPrefix"/>) — TEK önek otoritesi odur. Ad önekle başlamıyorsa kırpılmaz.</summary>
    public static string ShortLabel(string fullName, string prefix) =>
        prefix.Length > 0 && fullName.StartsWith(prefix, StringComparison.Ordinal)
            ? fullName[prefix.Length..] : fullName;

    public string ShortName => ShortLabel(Name, Prefix);

    /// <summary>[D5] Verilen adların en uzun ortak NOKTA-SINIRLI öneki (sondaki nokta DAHİL). Örn. tümü
    /// <c>OSYS.</c> altındaysa → <c>"OSYS."</c>; ortak nokta-segmenti yoksa → <c>""</c>. Bir adın TAMAMI asla önek
    /// olamaz (her adın son/yaprak segmenti hariç tutulur) — aksi halde etiket boşalırdı. Hardcode edilmiş
    /// <c>"OSYS."</c>'in yerini alan TEK önek otoritesidir (graf etiketi + şerit chip'i + dep-tooltip hep buradan).</summary>
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
