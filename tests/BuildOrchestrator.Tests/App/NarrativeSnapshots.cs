using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 1] Konsol testlerinin anlatı snapshot'ı — TEK yer (kopya YASAK). Görünüm anlatı belgesini metinden
/// değil bir snapshot'tan kurar (<see cref="NarrativeSnapshot"/>: backlog + o anki satır sayısı; belge son dilimi backlog'dan
/// ister). Backlog her yolda üretimdeki gibi bir anlatı store'udur (<see cref="NarrativeStore"/>).
/// <list type="bullet">
/// <item><see cref="FromText"/>: yalnız görünümü sınayan testler için metinden snapshot — eski
/// <c>ShowRunDocument(string)</c>/<c>ReplaceRunDocument(string)</c> çağrılarının karşılığı: satırlar konsolun kuralıyla
/// (<see cref="ConsoleLines.Split"/> — store'un da kuralı) bölünüp store'a yazılır, snapshot store'un tamamıdır.</item>
/// <item><see cref="Of"/> + <see cref="Deliver"/>: anlatının ÜRETİM düzeni — satır önce store'a yazılır, sonra pompanın yapacağı
/// gibi batch olarak görünüme gelir; görünüm geçmişi store'dan ister, kopya tutmaz.</item>
/// </list>
/// <para>[DEĞİŞEN KURULUM — B3 · O2 adım 2] <see cref="FromText"/> eskiden LİSTE tabanlı bir snapshot kuruyordu (<c>ListBacklog</c>):
/// belgeden kırpılan canlı satırlar listeye eklenirdi. Liste üretimde kullanılmadığı için kalktı; yardımcı üretimin backlog'unu kurar.
/// Fark yalnız store'a yazılmadan doğrudan görünüme verilen canlı satırlardadır: kırpılırlarsa geri gelmezler — üretimde böyle bir
/// satır yoktur (her anlatı satırı önce store'a yazılır); geçmişi sınayan testler <see cref="Deliver"/> kullanır.</para>
/// </summary>
internal static class NarrativeSnapshots
{
    /// <summary>'\n' sonekli metnin satırlarından, o satırları taşıyan bir store'un snapshot'ı (boş metin → boş anlatı).</summary>
    public static NarrativeSnapshot FromText(string text)
    {
        var store = new NarrativeStore();
        foreach (var line in ConsoleLines.Split(text)) store.Append(line);
        return Of(store);
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
