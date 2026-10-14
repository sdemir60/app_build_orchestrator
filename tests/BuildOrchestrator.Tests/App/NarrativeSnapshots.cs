using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 1] Konsol testlerinin anlatı snapshot'ı — TEK yer (kopya YASAK). Görünüm anlatı belgesini metinden
/// değil bir snapshot'tan kurar (<see cref="NarrativeSnapshot"/>: backlog + o anki satır sayısı; belge son dilimi backlog'dan
/// ister).
/// <list type="bullet">
/// <item><see cref="FromText"/>: yalnız görünümü sınayan testler için metinden LİSTE tabanlı snapshot — eski
/// <c>ShowRunDocument(string)</c>/<c>ReplaceRunDocument(string)</c> çağrılarının birebir karşılığı (satırlar aynı kuralla
/// bölünür, belgeden kırpılan canlı satırlar listeye eklenir).</item>
/// <item><see cref="Of"/> + <see cref="Deliver"/>: anlatının ÜRETİM düzeni — satır önce store'a yazılır, sonra pompanın yapacağı
/// gibi batch olarak görünüme gelir; görünüm geçmişi store'dan ister, kopya tutmaz.</item>
/// </list>
/// </summary>
internal static class NarrativeSnapshots
{
    /// <summary>'\n' sonekli metnin satırlarından liste tabanlı snapshot (boş metin → boş anlatı).</summary>
    public static NarrativeSnapshot FromText(string text)
    {
        var backlog = new ListBacklog(ConsoleLines.Split(text));
        return new NarrativeSnapshot(backlog, backlog.Count);
    }

    /// <summary>Store'un tamamı — VM'in tohumlamasının (<c>RunViewModel.SeedRunDocument</c>) verdiği snapshot'ın eşi.</summary>
    public static NarrativeSnapshot Of(NarrativeStore store) => new(store, store.Count);

    /// <summary>Satırları store'a ekler ve pompanın yapacağı gibi TEK anlatı batch'i olarak görünüme verir (önce store, sonra
    /// batch — VM'in sırası).</summary>
    public static void Deliver(NarrativeStore store, ConsoleView view, IEnumerable<string> lines)
    {
        int from = store.Count;
        foreach (var line in lines) store.Append(line);
        view.AppendNarrativeBatch(store.Join(from, store.Count));
    }
}
