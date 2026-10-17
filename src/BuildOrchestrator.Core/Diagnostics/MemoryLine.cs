using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using BuildOrchestrator.Core.ProcessControl;

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
/// okumayı satıra çevirir; <see cref="Report"/> satırı bir kanala bağlar ve hata YUTAR (<see cref="DiagnosticLine.Write"/>) — bir tanı
/// satırı Sync'i ya da koşu kapanışını asla bozmamalıdır.</para>
///
/// <para>Sayılar MB'dir (1 MB = 1024 × 1024 bayt; en yakın tam sayıya yuvarlanır). <c>private</c> sürecin özel
/// baytlarıdır — yönetilen heap, yerel bellek ve geri kalan her şey dahil. <c>committed</c> yönetilen heap'in
/// taahhüt edilmiş baytlarıdır ve SON GC'nin kaydıdır (<see cref="GC.GetGCMemoryInfo()"/>; hiç GC olmadıysa 0).
/// <c>heap</c> yönetilen heap'in şu anki boyutudur (<see cref="GC.GetTotalMemory"/>, toplamadan: henüz toplanmamış
/// çöp dahil). <c>private</c> − <c>committed</c> yönetilen heap'in DIŞINDAKİ belleği, <c>committed</c> − <c>heap</c>
/// GC'nin işletim sistemine geri vermediği boşluğu gösterir.</para>
///
/// <para>[kaynak şeridi · R1] Özel baytların TEK okuma yolu <c>GetProcessMemoryInfo</c>'dur: <see cref="CurrentPrivateBytes"/> bu süreci,
/// <see cref="PrivateBytesOf"/> PID'iyle başka bir süreci (motoru, bir derleyiciyi) okur; <see cref="Read"/> da <c>private</c>'ı aynı
/// yoldan alır, yani motorun satırı, App'in tanı satırları ve title bar'ın kaynak şeridi AYNI sayıyı okur. <c>Process.PrivateMemorySize64</c>
/// kullanılmaz: ölçüldü, o HER çağrıda makinedeki tüm süreçlerin anlık görüntüsünü alır (çağrı başına milisaniyeler ve birkaç KB) ve sayıya
/// kendi anlık görüntü tamponunu da katar; şerit App, motor ve derleyicilerin özel baytlarını 3 sn'de bir okur ve makine çapında süreç
/// taraması yasaktır. <c>GetProcessMemoryInfo</c> mikrosaniyeler sürer, bayt ayırmaz ve aynı ölçüyü verir. Satırın biçimi ve anlamı
/// değişmez; yalnız <c>private</c>'ın nereden okunduğu değişti.</para>
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
    /// TEK MB yuvarlaması, SAYI olarak: bayt → en yakın tam MB (yarımlar sıfırdan uzağa, eksi tarafta da). <see cref="Megabytes"/> bunun
    /// yazısıdır; GB'ye geçişi MB sayısına bakarak karar veren kaynak şeridi (<c>ResourceStripText.Memory</c>) yazıyı geri okumak yerine
    /// sayıyı buradan alır — yuvarlama iki yerde yazılmaz.
    /// </summary>
    public static long RoundedMegabytes(long bytes) =>
        (long)Math.Round(bytes / (double)BytesPerMb, MidpointRounding.AwayFromZero);

    /// <summary>
    /// TEK MB biçimi: bayt → en yakın tam MB (<see cref="RoundedMegabytes"/>), kültürden bağımsız, binlik ayırıcısız — <c>"309"</c>.
    /// Satır (<see cref="Format"/>) ve App'in tanı satırları (About → Environment; birim orada eklenir) sayıyı buradan alır.
    /// </summary>
    public static string Megabytes(long bytes) => RoundedMegabytes(bytes).ToString(CultureInfo.InvariantCulture);

    /// <summary>Bu sürecin bellek okuması — süreç okuması BURADA ve yalnız burada: satır (<see cref="Current"/>) da App'in tanı
    /// satırları da bunu kullanır (kaynak guard'ı: <c>MemoryReadGuardTests</c>). Özel baytlar <see cref="CurrentPrivateBytes"/>'tan gelir
    /// (<c>GetProcessMemoryInfo</c>; sınıf özeti). Okuma patlarsa fırlatır; tanı ekranları korumalı hâlini (<see cref="TryRead()"/>)
    /// kullanır.</summary>
    public static MemoryReading Read()
    {
        // Okunamayan özel bayt bugünkü gibi FIRLATIR (TryRead korumalı hâlidir). Win32 hatası burada hâlâ taze: CurrentPrivateBytes
        // başarısız çağrıdan sonra başka bir P/Invoke yapmadan döner.
        long privateBytes = CurrentPrivateBytes() ?? throw new Win32Exception(Marshal.GetLastWin32Error());
        return new(privateBytes, GC.GetGCMemoryInfo().TotalCommittedBytes, GC.GetTotalMemory(false));
    }

    /// <summary>
    /// [kaynak şeridi · R1] Bu sürecin özel baytları (<c>GetProcessMemoryInfo(...).PrivateUsage</c>, sözde tutamaçla); okunamazsa
    /// <c>null</c> — fırlatmaz. <see cref="Read"/> bunun üstünde durur. Isınma sonrası bayt ayırmaz.
    /// </summary>
    public static long? CurrentPrivateBytes() => PrivateUsageOf(NativeMethods.GetCurrentProcess());

    /// <summary>
    /// [kaynak şeridi · R1] <paramref name="processId"/> numaralı sürecin özel baytları: <c>OpenProcess(QUERY_LIMITED)</c> +
    /// <c>GetProcessMemoryInfo(...).PrivateUsage</c> + <c>CloseHandle</c>. Açılamayan (böyle bir süreç yok, erişim reddedildi),
    /// ÇIKMIŞ ya da okunamayan süreç için <c>null</c> — fırlatmaz: bir PID'i listelemek ile onu açmak arasında sürecin çıkması
    /// olağan bir durumdur (çıkan bir derleyici), hata değil; çağıran <c>null</c>'ı "bu sürecin belleği yok" diye ele alır. Isınma sonrası
    /// bayt ayırmaz; makinedeki süreçleri taramaz, yalnız verilen PID'i açar.
    ///
    /// <para>Çıkış AYRICA sorulur (<c>GetExitCodeProcess</c>): ölçüldü, çıkmış bir sürecin tutamacı başka birinde (çağıran, <c>csrss</c>, bir
    /// izleyici) açık kaldıkça <c>OpenProcess</c> BAŞARILIR ve <c>GetProcessMemoryInfo</c> başarıyla ESKİ bir sayı verir (~76 KB); okuma
    /// "açılamadı" diye düşmez. Çıkış kodu <c>STILL_ACTIVE</c>'ten (259) farklıysa süreç yoktur. 259 koduyla çıkmış bir süreç çalışıyor
    /// görünür ve bu eski sayıyı verir — kabul edilen ve zararsız (MB'ye yuvarlanınca 0) tek sınır.</para>
    /// </summary>
    public static long? PrivateBytesOf(int processId)
    {
        // PID bir DWORD'dür: eksi bir numara hiçbir sürece ait olamaz ve OpenProcess onu (ERROR_INVALID_PARAMETER ile) reddeder.
        nint handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, unchecked((uint)processId));
        if (handle == nint.Zero) return null;
        try
        {
            if (!NativeMethods.GetExitCodeProcess(handle, out uint exitCode) || exitCode != NativeMethods.STILL_ACTIVE) return null;
            return PrivateUsageOf(handle);
        }
        finally { NativeMethods.CloseHandle(handle); }
    }

    /// <summary>Verilen tutamaçtaki sürecin özel baytları (<c>PrivateUsage</c> = commit charge); okunamazsa <c>null</c>. Kaynak
    /// guard'ının izin verdiği TEK <c>GetProcessMemoryInfo</c> çağrısı burasıdır.</summary>
    private static long? PrivateUsageOf(nint processHandle)
    {
        var counters = new NativeMethods.PROCESS_MEMORY_COUNTERS_EX();
        uint size = (uint)Marshal.SizeOf<NativeMethods.PROCESS_MEMORY_COUNTERS_EX>();
        counters.cb = size;
        return NativeMethods.GetProcessMemoryInfo(processHandle, ref counters, size) ? (long)counters.PrivateUsage.ToUInt64() : null;
    }

    /// <summary>
    /// <see cref="Read"/>'in tanı için korumalı hâli: okuma patlarsa (süreç sorgusu reddedilir, süreç kapanıyor) <c>null</c> döner,
    /// FIRLATMAZ. App'in tanı ekranı (About → Environment) belleği okuyamadı diye açılamaz — <see cref="Report"/> motorun satırı için
    /// aynı güvenceyi verir. Okuyamayan çağıran değeri "bilinmiyor" diye gösterir.
    /// </summary>
    public static MemoryReading? TryRead() => TryRead(Read);

    /// <summary>
    /// Okuyucuyu çağırandan alan aşırı yükleme: hata yutmanın TEK yeri (<see cref="TryRead()"/> buna devreder). Patlayan bir süreç
    /// okumasını taklit etmek için testler kullanır: <c>internal</c>'dır, çünkü Core derlemesi internal'larını test derlemesine açar
    /// (<c>InternalsVisibleTo BuildOrchestrator.Tests</c>, Core.csproj); üretim çağıranı yalnız parametresiz <see cref="TryRead()"/>'tir.
    /// </summary>
    internal static MemoryReading? TryRead(Func<MemoryReading> read)
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
    /// bu bir tanıdır, çağıran iş (Sync, koşu kapanışı) onun yüzünden düşmemeli. Yutmanın tek yeri <see cref="DiagnosticLine.Write"/>'tır —
    /// okuma satırın kurulumudur, onun hatası da yutulur.
    /// </summary>
    public static void Report(Action<string> sink) => DiagnosticLine.Write(sink, Current);
}
