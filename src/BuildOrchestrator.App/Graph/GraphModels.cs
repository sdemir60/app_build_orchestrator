using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.App.Graph;

// [D1 fold — B3 review] GraphStatus enum'ı nötr Controls namespace'ine taşındı (Controls/GraphStatus.cs) —
// StatusGlyph'in graf-dışı ilk tüketicisi D1'dir. Buradaki kullanımlar için yukarıdaki using yeterlidir.

/// <summary>[T63] Graf düğümü — kimlik (tam proje adı), katman indeksi, statü ve dep-hata bayrağı.</summary>
public sealed record GraphNode(string Name, int Layer, GraphStatus Status, bool HasDepIssue = false)
{
    /// <summary>design-v1 §2.3: düğüm etiketinde <c>OSYS.</c> öneki atılır (prototype <c>BO.shortName</c> portu).</summary>
    public static string ShortLabel(string fullName) =>
        fullName.StartsWith("OSYS.", StringComparison.Ordinal) ? fullName["OSYS.".Length..] : fullName;

    public string ShortName => ShortLabel(Name);
}

/// <summary>[T63] Bağımlılık kenarı: <paramref name="From"/> (bağımlılık) → <paramref name="To"/> (bağımlı proje);
/// prototype <c>GRAPH.edges</c> ile AYNI yön (yukarıdan aşağı).</summary>
public sealed record GraphEdge(string From, string To);
