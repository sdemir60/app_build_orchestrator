namespace BuildOrchestrator.App.Console;

/// <summary>
/// [idle-memory · dalga 1 · spec §6.2] Konsol belgesinin ARKASINDAKİ satırlar: render diliminin dışında kalan geçmiş. Konsol
/// görünümü anlatı belgesinin yalnız son dilimini tutar; kullanıcı tepeye kaydırınca önceki dilimi buradan ister.
///
/// <para>Anlatıda backlog VM'in anlatı store'unun KENDİSİDİR (<see cref="NarrativeStore"/>) — görünüm satırları kopyalamaz, dilim
/// ister. Arkasında bir şey olmayan iki yer tutucu — anlatı bağlanmadan önceki görünüm ve proje sayfası ([dalga 3 · karar 44] sayfa logun
/// tamamını belgesinde taşır) — boş backlog'dur (<see cref="EmptyBacklog"/>, B2 fix 1 O2). Sözleşme yalnız okumadır: belgeden kırpılan
/// satırlar backlog'a VERİLMEZ — anlatıda kırpılan her satır zaten store'dadır (önce store, sonra pompa), yer tutucuların arkasında bir şey
/// yoktur.</para>
/// <para>[DEĞİŞEN KURAL — B3 · O2 adım 2] Sözleşme eskiden kırpılan canlı satırları da alıyordu (<c>AcceptTrimmed</c>, "saklıyor musun"
/// sorusu <c>KeepsTrimmedLines</c>): onları saklayan tek backlog görünümün kendi satır listesiydi (<c>ListBacklog</c> — eskiden proje
/// sayfasının backlog'u, diskten gelen log + kırpılan canlı satırlar). B2'nin O2 adım 1'inden beri listenin üretimde kullanıcısı yoktu;
/// liste, iki üye ve görünümün kırpılan kuyruğu okuması birlikte kalktı.</para>
/// </summary>
public interface IConsoleBacklog
{
    /// <summary>Satır sayısı.</summary>
    int Count { get; }

    /// <summary>[from, to) satırlarını '\n' SONEKLİ birleştirir (<see cref="ConsoleLines.Join"/>); boş aralık ⇒ <c>""</c>. Sonuç ya
    /// TAM aralıktır ya <c>""</c> — asla kısmi metin: görünüm dikişi (yükleme indeksi, kaydırma telafisi) satır sayısını metinden değil
    /// indekslerden hesaplar. Store'un diskten okuyamadığı bir aralık da <c>""</c>'dir (<see cref="NarrativeStore.Join"/>).</summary>
    string Join(int from, int to);
}
