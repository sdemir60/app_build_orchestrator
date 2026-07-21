namespace BuildOrchestrator.App.Graph;

/// <summary>
/// [T63] design-v1 DS <c>STATUS_META</c> / <c>DependencyGraphNode</c> statü kümesi — graf düğümünün renk/çerçeve
/// ailesini seçen TEK enum. (Çekirdeğin <c>ProjectRowState</c>'i ile birebir aynı değildir: graf, tasarımın
/// <c>discovered</c>/<c>cycle</c> gibi görsel durumlarını da taşır; eşleme çağıranın işidir.)
/// </summary>
public enum GraphStatus
{
    Discovered,
    Queued,
    Building,
    Succeeded,
    Failed,
    Skipped,
    Cycle,
}

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
