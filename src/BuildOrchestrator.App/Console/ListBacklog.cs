namespace BuildOrchestrator.App.Console;

/// <summary>
/// [idle-memory · dalga 1] Görünümün KENDİ satır listesi olan backlog: verilen satırların kopyası (çağıranın listesi değişmez) ve canlı
/// akışta belgeden kırpılan satırlar (<see cref="AcceptTrimmed"/>) — kırpılan hiçbir satır erişilmez olmaz. Üretimde anlatı bağlanmadan
/// önceki görünümündür ve proje sayfasında boştur (sayfa logun tamamını belgesinde taşır, hiç kırpılmaz — dalga 3 · karar 44; eskiden
/// proje sayfasının backlog'u diskten gelen logun kopyasıydı). Bağlı anlatıda kullanılmaz: orada backlog VM'in store'udur
/// (<see cref="NarrativeStore"/>).
/// </summary>
public sealed class ListBacklog(IEnumerable<string> lines) : IConsoleBacklog
{
    private readonly List<string> _lines = [.. lines];

    public int Count => _lines.Count;

    /// <summary>Liste kırpılan canlı satırları SAKLAR (<see cref="AcceptTrimmed"/>): başka hiçbir yerde durmazlar.</summary>
    public bool KeepsTrimmedLines => true;

    public string Join(int from, int to) => ConsoleLines.Join(_lines, from, to);

    public void AcceptTrimmed(IReadOnlyList<string> lines) => _lines.AddRange(lines);
}
