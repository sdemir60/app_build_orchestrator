namespace BuildOrchestrator.App.Console;

/// <summary>
/// [idle-memory · dalga 1] Görünümün KENDİ satır listesi olan backlog — proje logu sayfası: diskten gelen log satırlarının
/// kopyası (çağıranın listesi değişmez) ve canlı akışta belgeden kırpılan satırlar (<see cref="AcceptTrimmed"/>) — kırpılan
/// hiçbir satır erişilmez olmaz. Anlatıda kullanılmaz: orada backlog VM'in store'udur (<see cref="NarrativeStore"/>).
/// </summary>
public sealed class ListBacklog(IEnumerable<string> lines) : IConsoleBacklog
{
    private readonly List<string> _lines = [.. lines];

    public int Count => _lines.Count;

    public string Join(int from, int to) => ConsoleLines.Join(_lines, from, to);

    public void AcceptTrimmed(IReadOnlyList<string> lines) => _lines.AddRange(lines);
}
