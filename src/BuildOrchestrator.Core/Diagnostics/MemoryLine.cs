using System.Diagnostics;
using System.Globalization;

namespace BuildOrchestrator.Core.Diagnostics;

/// <summary>
/// [PERF Faz C/C3] Motorun bellek tanı satırı: <c>memory: private=&lt;MB&gt; committed=&lt;MB&gt; heap=&lt;MB&gt;</c>.
/// Supervisor onu bir Sync bittiğinde ve bir koşu sona erdiğinde konsol (stderr) kanalına yazar; stdout YALNIZ
/// NDJSON'dır. Satırın biçimi TEK yerdedir (kopya YASAK): onu okuyan test ya da ölçüm betiği <see cref="Prefix"/>
/// ile tanır.
///
/// <para>Üç katman bilerek ayrıdır: <see cref="Format"/> SAFTIR (sayılar parametre, süreç okumaz — birim testi bayt
/// verir, satırı görür); <see cref="Current"/> süreci okuyan ince katmandır; <see cref="Report"/> ikisini bir
/// kanala bağlar ve hata YUTAR — bir tanı satırı Sync'i ya da koşu kapanışını asla bozmamalıdır.</para>
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
    /// Saf biçimlendirme: üç bayt sayısı → <c>memory: private=… committed=… heap=…</c> (MB). Süreç okumaz, kültürden
    /// bağımsızdır (binlik ayırıcı yok).
    /// </summary>
    public static string Format(long privateBytes, long committedBytes, long heapBytes) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{Prefix} private={ToMb(privateBytes)} committed={ToMb(committedBytes)} heap={ToMb(heapBytes)}");

    /// <summary>Bu sürecin şu anki satırı — süreç okuması BURADA ve yalnız burada.</summary>
    public static string Current()
    {
        using var process = Process.GetCurrentProcess();
        return Format(process.PrivateMemorySize64, GC.GetGCMemoryInfo().TotalCommittedBytes, GC.GetTotalMemory(false));
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

    private static long ToMb(long bytes) =>
        (long)Math.Round(bytes / (double)BytesPerMb, MidpointRounding.AwayFromZero);
}
