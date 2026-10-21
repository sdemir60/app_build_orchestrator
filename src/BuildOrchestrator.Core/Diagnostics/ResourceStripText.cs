using System.Globalization;
using BuildOrchestrator.Core.Formatting;
using BuildOrchestrator.Core.ProcessControl;

namespace BuildOrchestrator.Core.Diagnostics;

/// <summary>
/// [kaynak şeridi · design v1.25.0 §2.1 · karar K3-K6] Title bar'ın ortasındaki kaynak şeridinin (CPU · RAM · paralellik + mod)
/// SAF hesabı ve metinleri. Hiçbiri süreç ya da saat okumaz, UI bilmez: sayılar parametredir, çıktı metindir — birim testi sayıyı
/// verir, metni görür. Okuma Win32'dedir (<c>JobObject</c>, <see cref="MemoryLine"/>), örnekleme App'te, görüntü view model ve
/// XAML'dedir; burası yalnız "örnekler arası fark → yüzde" ve "sayı → metin" kurallarının TEK yeridir (kopya YASAK).
///
/// <para>Sayı biçimi kültürden bağımsızdır: ondalık ayracı her zaman noktadır, Türkçe Windows'ta <c>4,8 GB</c> çıkmaz. Mod adı
/// <see cref="PerfMode"/>'un adıdır (<c>mode.ToString()</c>); modun paralelliği (hücrenin sınırı) ve cap <see cref="PerfProfile.For"/>
/// tablosundan okunur — burada ikinci bir "Full/Balanced/Light" ya da 6/4/2 tablosu YOKTUR. Koşunun paralelliği motorun cevabıdır
/// (<c>runStarted.Parallelism</c>) ve parametre olarak gelir.</para>
///
/// <para>Hayalet sabitleri (<see cref="CpuGhost"/> ...) şeridin sabit çerçevesinin ölçüsüdür: her hücre, o an görünen metin ne
/// olursa olsun en geniş değer kadar yer ayırır, değer değişince hiçbir şey kaymaz.</para>
///
/// <para>Ürün adı burada YAZILMAZ: adı içeren iki tooltip (<see cref="CpuTooltip"/>, <see cref="MemoryTooltip"/>) onu parametre olarak
/// alır — App'te <c>AppIdentity.Product</c>, adın tek kaynağı. Bu dosya AppIdentityTests'in kaynak guard'ı tarafından taranır; adı bir
/// literalin içine gömen satır süiti kırar.</para>
/// </summary>
public static class ResourceStripText
{
    /// <summary>
    /// Okunamayan değer: bir hücrede ya da tooltip parçasında ölçüm yoksa yazılan TEK işaret (U+2014). <c>0</c> DEĞİLDİR —
    /// uyuyan motorun <c>0 MB</c>'ı gerçek bir ölçümdür, <c>—</c> ölçümün yokluğudur. Değer <see cref="MissingValue.Mark"/>'tır: süre ve
    /// About'un olmayan değeriyle aynı işaret, tek tanım.
    /// </summary>
    public const string Unreadable = MissingValue.Mark;

    /// <summary>CPU hücresinin hayaleti: en geniş değer (tavan %100).</summary>
    public const string CpuGhost = "100%";

    /// <summary>RAM hücresinin hayaleti: en geniş değer (iki haneli GB, bir ondalık).</summary>
    public const string MemoryGhost = "12.3 GB";

    /// <summary>Paralellik hücresinin sayı hayaleti: bu koşunun iki sayısı, iki haneli (<c>koşan/koşunun paralelliği</c>). Sınırın hayaleti
    /// ayrıdır (<see cref="LimitGhost"/>) — sınır mod sözcüğünün arkasında durur: <c>0/0 Balanced · 4</c>.</summary>
    public const string ParallelGhost = "12/12";

    /// <summary>Paralellik hücresinin mod sözcüğü hayaleti: en uzun mod adı. Değeri enum üyesinin adından gelir — mod adı
    /// ikinci kez yazılmaz.</summary>
    public const string ModeGhost = nameof(PerfMode.Balanced);

    /// <summary>Paralellik hücresinde mod sözcüğü ile sınırın arasındaki ayraç: <c>Balanced · 4</c>. Mod sözcüğünün yazı tipiyle yazılır; aralığı
    /// kendi boşlukları verir (ölçü değil).</summary>
    public const string LimitSeparator = " · ";

    /// <summary>Paralellik hücresinin sınır hayaleti: en geniş modun sayısı (iki hane).</summary>
    public const string LimitGhost = "12";

    /// <summary>Gigabaytta megabayt: GB'ye geçişin eşiği ve GB değerinin böleni.</summary>
    private const long MegabytesPerGigabyte = 1024;

    /// <summary>
    /// CPU yüzdesi: <c>işlemci zamanı / (duvar zamanı × işlemci sayısı) × 100</c>, en yakın tam sayıya (yarımlar sıfırdan uzağa),
    /// [0, 100] aralığına kırpılmış. <paramref name="cpuTime"/> iki örnek arasındaki işlemci zamanı FARKIDIR (uygulama + job),
    /// <paramref name="wallTime"/> aynı iki örnek arasındaki duvar saati farkı. Duvar zamanı ya da işlemci sayısı sıfır/negatifse
    /// payda yoktur ⇒ <c>null</c> (ölçülemedi); işlemci zamanı sıfır/negatifse (sayaç geriye gitmez, sıfır fark = boşta) ⇒ 0.
    /// </summary>
    public static int? CpuPercent(TimeSpan cpuTime, TimeSpan wallTime, int processorCount)
    {
        if (wallTime <= TimeSpan.Zero || processorCount <= 0) return null;
        if (cpuTime <= TimeSpan.Zero) return 0;

        // Bölme tik (tam sayı) üzerinde ve decimal'dedir: çift duyarlıklı sayıyla 3,5 ms / 100 ms 3,4999999999999996 çıkar ve tam
        // yarım aşağı yuvarlanırdı. decimal'de tam yarım tam yarım kalır, "yarımlar sıfırdan uzağa" sözü her girdide tutar.
        decimal percent = (decimal)cpuTime.Ticks * 100m / ((decimal)wallTime.Ticks * processorCount);
        return (int)Math.Min(100m, Math.Round(percent, MidpointRounding.AwayFromZero));
    }

    /// <summary>CPU hücresinin metni: <c>"69%"</c> · okunamayınca <see cref="Unreadable"/>. <paramref name="percent"/>
    /// <see cref="CpuPercent"/>'in çıktısıdır (zaten [0, 100]); burada yalnız yazılır.</summary>
    public static string Cpu(int? percent) =>
        percent is { } value ? string.Create(CultureInfo.InvariantCulture, $"{value}%") : Unreadable;

    /// <summary>
    /// RAM metni: 1 GB altında tam MB (<c>"310 MB"</c>), 1 GB ve üstünde tek ondalıklı GB (<c>"4.8 GB"</c>), ölçüm yoksa
    /// <see cref="Unreadable"/>.
    ///
    /// <para>MB sayısı <see cref="MemoryLine.RoundedMegabytes"/>'ten, yazısı <see cref="MemoryLine.Megabytes"/>'tan gelir — motorun
    /// <c>memory:</c> satırı ve About'un tanı satırlarıyla AYNI yuvarlama; şerit kendi MB yuvarlamasını yazmaz ve yazıyı geri okumaz. GB'ye
    /// geçiş YUVARLANMIŞ MB 1024'e ulaşınca olur (1023,5 MiB bile <c>"1.0 GB"</c> yazılır, hiçbir yerde <c>"1024 MB"</c> görünmez) ve GB
    /// değeri de aynı yuvarlanmış MB'den türer; 0,1 GB'ye yarımlar sıfırdan uzağa yuvarlanır (1280 MB = 1,25 GB → <c>"1.3 GB"</c>).
    /// Ondalık ayracı her zaman noktadır.</para>
    ///
    /// <para><c>ByteFormat.Size</c> kullanılmaz: o dosya boyutları içindir ve 10 GB üstünde ondalığı atar (<c>"12 GB"</c>); şerit hep tek
    /// ondalık yazar, hayaleti <see cref="MemoryGhost"/> <c>12.3 GB</c>'dir.</para>
    ///
    /// <para>Eksi bayt bir okuma hatasıdır (süreç belleği eksi olamaz; iki ayrı okumanın farkı ters çıkmış olabilir) ve
    /// <see cref="Unreadable"/> olur: <c>"0 MB"</c> gerçek bir ölçümü (uyuyan motor) anlatır, hatayı değil.</para>
    /// </summary>
    public static string Memory(long? bytes)
    {
        if (bytes is not { } value || value < 0) return Unreadable;

        long megabytes = MemoryLine.RoundedMegabytes(value);
        if (megabytes < MegabytesPerGigabyte) return MemoryLine.Megabytes(value) + " MB";

        // 0,1 GB'ye AÇIKÇA yuvarla (tam sayı aritmetiği, yarımlar sıfırdan uzağa); metin yalnız gösterimdir. Kayan noktalı bölme ve
        // biçim dizgisinin örtük yuvarlaması kullanılmaz (DurationFormat ile aynı disiplin).
        long tenths = (megabytes * 10 + MegabytesPerGigabyte / 2) / MegabytesPerGigabyte;
        return string.Create(CultureInfo.InvariantCulture, $"{tenths / 10}.{tenths % 10} GB");
    }

    /// <summary>
    /// CPU hücresinin tooltip'i: <c>"CPU used by {product} — app, engine and compilers · cap 70%"</c>. <paramref name="product"/> ürün
    /// adıdır ve ÇAĞIRANDAN gelir (App'te <c>AppIdentity.Product</c>); burada yazılmaz. Cap modun TABLO değeridir
    /// (<c>PerfProfile.For(mode).CpuCapPercent</c>); copy-floor penceresindeki geçici yükselme ve koşu ortası değişim burada
    /// gösterilmez (konsol notu zaten yazar). Cap'siz profilde (Full) <c>· cap …</c> eki YOKTUR — "cap off" yazılmaz. Cap'in <c>70%</c>
    /// yazısı konsol notunun yazısıdır (<see cref="PerfNoteText.CapValue"/>).
    /// </summary>
    public static string CpuTooltip(string product, PerfMode mode)
    {
        string text = $"CPU used by {product} — app, engine and compilers";
        return PerfProfile.For(mode).CpuCapPercent is { } cap ? $"{text} · cap {PerfNoteText.CapValue(cap)}" : text;
    }

    /// <summary>
    /// RAM hücresinin tooltip'i: <c>"Memory used by {product} · app 310 MB · engine 85 MB · compilers 4.4 GB"</c>.
    /// <paramref name="product"/> ürün adıdır ve ÇAĞIRANDAN gelir (App'te <c>AppIdentity.Product</c>); burada yazılmaz. Her parça
    /// <see cref="Memory"/> ile yazılır: okunamayan parça <c>—</c>, uyuyan motor ve derleyiciler (job boş) çağıranın verdiği 0 ile
    /// <c>0 MB</c>. Git'in çocukları (Sync) derleyicilere sayılır.
    /// </summary>
    public static string MemoryTooltip(string product, long? app, long? engine, long? compilers) =>
        $"Memory used by {product} · app {Memory(app)} · engine {Memory(engine)} · compilers {Memory(compilers)}";

    /// <summary>Paralellik hücresinin sınırı (en sondaki soluk sayı, mod sözcüğünden sonra <see cref="LimitSeparator"/> ile): <c>"4"</c> — modun
    /// tablo paralelliği (<c>PerfProfile.For(mode).Parallelism</c>), yalın sayı. Pay (o an derlenen proje sayısı) ayrı bir metindir ve burada
    /// değil view model'dedir; ortadaki sayı <see cref="ParallelRun"/>.</summary>
    public static string ParallelAllowed(PerfMode mode) =>
        PerfProfile.For(mode).Parallelism.ToString(CultureInfo.InvariantCulture);

    /// <summary>Paralellik hücresinin ortadaki sayısı: <c>"/4"</c> — motorun koşu başında belirlediği paralellik (<c>runStarted.Parallelism</c>:
    /// modun sayısı, makineye göre kırpılmış olabilir). Koşu yokken paralel derlenen bir şey yoktur: <c>"/0"</c> — boştaki hücre <c>0/0 Balanced · 4</c>.
    /// Tire (<see cref="MissingValue.Mark"/>) yazılmaz: boşta hücrenin üç sayısı da bir paralellik sayısıdır, tire "hesaplanamadı" diye okunur.</summary>
    public static string ParallelRun(int? runParallelism) =>
        string.Create(CultureInfo.InvariantCulture, $"/{runParallelism ?? 0}");

    /// <summary>
    /// Paralellik hücresinin tooltip'i, üç hâl. BOŞTA (<paramref name="runParallelism"/> <c>null</c> — koşu yok): <c>"Balanced — up to 4
    /// projects build in parallel"</c> — mod sözcüğü ve modun tablo paralelliği; <paramref name="building"/> ve <paramref name="reducedReason"/>
    /// boşta söze karışmaz. KOŞUDA: <c>"3 of 4 projects building in parallel"</c> — <c>of N</c> koşunun paralelliğidir
    /// (<paramref name="runParallelism"/>, motorun koşu başında belirlediği sayı), pay 0 iken de koşu metnidir, boşta metnine dönmez (hücre
    /// koşuyu anlatır). KOŞUDA VE KISILMIŞ (<paramref name="reducedReason"/> dolu): <c>"2 of 2 — reduced: 3 GB free memory"</c>.
    ///
    /// <para>Gerekçe motorun yazdığı metindir (<see cref="PerfNoteText.FreeMemoryLimit"/>, <see cref="PerfNoteText.LogicalProcessorsLimit"/>)
    /// ve AYNEN geçer: çevrilmez, kısaltılmaz. Boş ya da yalnız boşluk olan gerekçe "kısılmadı" demektir.
    /// <paramref name="mode"/> hücrenin modudur (koşuda koşu başındaki mod; hangisinin verileceği çağıranın işidir).</para>
    /// </summary>
    public static string ParallelTooltip(PerfMode mode, int building, int? runParallelism, string? reducedReason)
    {
        if (runParallelism is not { } parallelism)
            return string.Create(CultureInfo.InvariantCulture,
                $"{mode.ToString()} — up to {PerfProfile.For(mode).Parallelism} projects build in parallel");

        return string.IsNullOrWhiteSpace(reducedReason)
            ? string.Create(CultureInfo.InvariantCulture, $"{building} of {parallelism} projects building in parallel")
            : string.Create(CultureInfo.InvariantCulture, $"{building} of {parallelism} — reduced: {reducedReason}");
    }
}
