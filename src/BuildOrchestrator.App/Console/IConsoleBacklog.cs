namespace BuildOrchestrator.App.Console;

/// <summary>
/// [idle-memory · dalga 1 · spec §6.2] Konsol belgesinin ARKASINDAKİ satırlar: render diliminin dışında kalan geçmiş. Konsol
/// görünümü belgenin yalnız son dilimini tutar; kullanıcı tepeye kaydırınca önceki dilimi buradan ister.
///
/// <para>İki kaynak vardır. Anlatıda backlog VM'in anlatı store'unun KENDİSİDİR (<see cref="NarrativeStore"/>) — görünüm
/// satırları kopyalamaz, dilim ister. Proje logunda görünümün kendi satır listesidir (<see cref="ListBacklog"/>): diskten
/// gelen log + kırpılan canlı satırlar.</para>
/// </summary>
public interface IConsoleBacklog
{
    /// <summary>Satır sayısı.</summary>
    int Count { get; }

    /// <summary>[from, to) satırlarını '\n' SONEKLİ birleştirir (<see cref="ConsoleLines.Join"/>); boş aralık ⇒ <c>""</c>.</summary>
    string Join(int from, int to);

    /// <summary>Canlı akışta belgenin başından kırpılan ve backlog'un ucunu AŞAN satırlar — hiç kaydedilmemiş CANLI satırlar.
    /// Liste onları sonuna ekler; store hiçbir şey yapmaz: belgedeki her anlatı satırı önce store'a yazılmıştır, kopyası
    /// tutulmaz.</summary>
    void AcceptTrimmed(IReadOnlyList<string> lines);
}
