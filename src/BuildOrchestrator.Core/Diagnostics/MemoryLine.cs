using System.Diagnostics;
using System.Globalization;

namespace BuildOrchestrator.Core.Diagnostics;

/// <summary>
/// [idle-memory · spec §6.6] Bir sürecin bellek okuması, bayt olarak — <see cref="MemoryLine.Read"/>'in tek çıktısı. Alanların
/// anlamı <see cref="MemoryLine"/>'ın doc'undadır: <see cref="PrivateBytes"/> sürecin özel baytları, <see cref="CommittedBytes"/>
/// GC'nin taahhüt ettiği yığın (son GC'nin kaydı), <see cref="HeapBytes"/> yönetilen yığının şu anki boyutu.
/// </summary>
public readonly record struct MemoryReading(long PrivateBytes, long CommittedBytes, long HeapBytes);

/// <summary>
/// [PERF Faz C/C3] Motorun bellek tanı satırı: <c>memory: private=&lt;MB&gt; committed=&lt;MB&gt; heap=&lt;MB&gt;</c>.
/// Supervisor onu bir koşu sona erdiğinde ve bir Sync, Clean ya da Optimize bittiğinde konsol (stderr) kanalına yazar;
/// stdout YALNIZ NDJSON'dır. Koşu slotu boşsa satırdan önce <see cref="MemoryTrim"/> toplar, yani satır toplamanın bıraktığı
/// değeri taşır. Satırın biçimi TEK yerdedir (kopya YASAK): onu okuyan test ya da ölçüm betiği <see cref="Prefix"/> ile tanır.
///
/// <para>[idle-memory · spec §6.6] App de AYNI okumayı ve AYNI MB kuralını kullanır: About → Environment'ın RUNTIME grubundaki
/// bellek satırları (<c>App private</c> · <c>GC committed</c> · <c>Managed heap</c>) <see cref="Read"/>'in değerleridir ve
/// sayıları <see cref="Megabytes"/>'tan gelir — motorun satırıyla App'in satırları aynı kuralla yazılır.</para>
///
/// <para>Katmanlar bilerek ayrıdır: <see cref="Read"/> süreci okuyan TEK yerdir; <see cref="TryRead()"/> onu hata YUTAN bir korumayla
/// sarar (App'in tanı ekranı okuyamazsa "bilinmiyor" gösterir); <see cref="Megabytes"/> TEK MB biçimidir;
/// <see cref="Format"/> SAFTIR (sayılar parametre, süreç okumaz — birim testi bayt verir, satırı görür); <see cref="Current"/>
/// okumayı satıra çevirir; <see cref="Report"/> satırı bir kanala bağlar ve hata YUTAR — bir tanı satırı Sync'i ya da koşu
/// kapanışını asla bozmamalıdır.</para>
///
/// <para>Sayılar MB'dir (1 MB = 1024 × 1024 bayt; en yakın tam sayıya yuvarlanır). <c>private</c> sürecin özel
/// baytlarıdır — yönetilen heap, yerel bellek ve geri kalan her şey dahil. <c>committed</c> yönetilen heap'in
/// taahhüt edilmiş baytlarıdır ve SON GC'nin kaydıdır (<see cref="GC.GetGCMemoryInfo()"/>; hiç GC olmadıysa 0).
/// <c>heap</c> yönetilen heap'in şu anki boyutudur (<see cref="GC.GetTotalMemory"/>, toplamadan: henüz toplanmamış
/// çöp dahil). <c>private</c> − <c>committed</c> yönetilen heap'in DIŞINDAKİ belleği, <c>committed</c> − <c>heap</c>
/// GC'nin işletim sistemine geri vermediği boşluğu gösterir.</para>
/// </summary>
public static class MemoryLine
{
    /// <summary>Satırın değişmez başı — satırı tanıyan herkes (test, ölçüm betiği) bunu okur.</summary>
    public const string Prefix = "memory:";

    private const long BytesPerMb = 1024 * 1024;

    /// <summary>
    /// Saf biçimlendirme: üç bayt sayısı → <c>memory: private=… committed=… heap=…</c> (MB, <see cref="Megabytes"/>). Süreç
    /// okumaz.
    /// </summary>
    public static string Format(long privateBytes, long committedBytes, long heapBytes) =>
        $"{Prefix} private={Megabytes(privateBytes)} committed={Megabytes(committedBytes)} heap={Megabytes(heapBytes)}";

    /// <summary>
    /// TEK MB biçimi: bayt → en yakın tam MB (yarımlar sıfırdan uzağa), kültürden bağımsız, binlik ayırıcısız — <c>"309"</c>.
    /// Satır (<see cref="Format"/>) ve App'in tanı satırları (About → Environment; birim orada eklenir) sayıyı buradan alır.
    /// </summary>
    public static string Megabytes(long bytes) =>
        ((long)Math.Round(bytes / (double)BytesPerMb, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

    /// <summary>Bu sürecin bellek okuması — süreç okuması BURADA ve yalnız burada: satır (<see cref="Current"/>) da App'in tanı
    /// satırları da bunu kullanır (kaynak guard'ı: <c>MemoryReadGuardTests</c>). Okuma patlarsa fırlatır; tanı ekranları korumalı
    /// hâlini (<see cref="TryRead()"/>) kullanır.</summary>
    public static MemoryReading Read()
    {
        using var process = Process.GetCurrentProcess();
        return new(process.PrivateMemorySize64, GC.GetGCMemoryInfo().TotalCommittedBytes, GC.GetTotalMemory(false));
    }

    /// <summary>
    /// <see cref="Read"/>'in tanı için korumalı hâli: okuma patlarsa (süreç sorgusu reddedilir, süreç kapanıyor) <c>null</c> döner,
    /// FIRLATMAZ. App'in tanı ekranı (About → Environment) belleği okuyamadı diye açılamaz — <see cref="Report"/> motorun satırı için
    /// aynı güvenceyi verir. Okuyamayan çağıran değeri "bilinmiyor" diye gösterir.
    /// </summary>
    public static MemoryReading? TryRead() => TryRead(Read);

    /// <summary>
    /// Okuyucuyu çağırandan alan aşırı yükleme: hata yutmanın TEK yeri (<see cref="TryRead()"/> buna devreder). Patlayan bir süreç
    /// okumasını taklit etmenin tek yolu olduğu için testler kullanır — Core derlemesi internal'larını testlere açmaz.
    /// </summary>
    public static MemoryReading? TryRead(Func<MemoryReading> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        try { return read(); }
        catch (Exception) { return null; } // tanı: okunamadı — çağıran "bilinmiyor" gösterir, tanı ekranı bozulmaz
    }

    /// <summary>Bu sürecin şu anki satırı: <see cref="Read"/>'in değerleri <see cref="Format"/>'ın biçiminde.</summary>
    public static string Current()
    {
        var reading = Read();
        return Format(reading.PrivateBytes, reading.CommittedBytes, reading.HeapBytes);
    }

    /// <summary>
    /// <see cref="Current"/> satırını <paramref name="sink"/>'e yazar. Okuma ya da kanal yazımı patlarsa hata YUTULUR:
    /// bu bir tanıdır, çağıran iş (Sync, koşu kapanışı) onun yüzünden düşmemeli.
    /// </summary>
    public static void Report(Action<string> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        try { sink(Current()); }
        catch (Exception) { /* tanı: okunamadı ya da yazılamadı — sessizce vazgeç, işi bozma */ }
    }
}
