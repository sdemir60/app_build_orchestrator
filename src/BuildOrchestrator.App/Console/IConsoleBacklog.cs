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

    /// <summary>[from, to) satırlarını '\n' SONEKLİ birleştirir (<see cref="ConsoleLines.Join"/>); boş aralık ⇒ <c>""</c>. Sonuç ya
    /// TAM aralıktır ya <c>""</c> — asla kısmi metin: görünüm dikişi (yükleme indeksi, kaydırma telafisi) satır sayısını metinden değil
    /// indekslerden hesaplar. Store'un diskten okuyamadığı bir aralık da <c>""</c>'dir (<see cref="NarrativeStore.Join"/>).</summary>
    string Join(int from, int to);

    /// <summary>Backlog kırpılan canlı satırları SAKLIYOR mu (<see cref="AcceptTrimmed"/> bir şey yapıyor mu): liste evet, store
    /// hayır — satırlar zaten onun içindedir. Görünüm kırpılan metnin kuyruğunu belgeden YALNIZ saklayan backlog için okur
    /// (<c>ConsoleView.TrimToRenderSlice</c>): saklamayana vermek, atılacak bir metni boşuna üretmektir. Görünümün "kendi kopyasını
    /// tutuyor mu" sorusu (<c>ConsoleView.BacklogOwnsCopy</c>) aynı cevaptır — soru TEK yerde durur.</summary>
    bool KeepsTrimmedLines { get; }

    /// <summary>Canlı akışta belgenin başından kırpılan ve backlog'un ucunu AŞAN satırlar — hiç kaydedilmemiş CANLI satırlar.
    /// Liste onları sonuna ekler; store hiçbir şey yapmaz: belgedeki her anlatı satırı önce store'a yazılmıştır, kopyası
    /// tutulmaz (<see cref="KeepsTrimmedLines"/> false).</summary>
    void AcceptTrimmed(IReadOnlyList<string> lines);
}
