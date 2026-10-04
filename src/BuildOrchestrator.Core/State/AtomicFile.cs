using BuildOrchestrator.Core.Scheduling;

namespace BuildOrchestrator.Core.State;

/// <summary>
/// Durum dosyalarının (<c>build-state.json</c>, <c>run-inflight.json</c>) ve iki büyük defterin
/// (<c>evaluation-cache.json</c>, <c>source-hash-cache.json</c>) TEK okuma/yazım yolu: geçici dosyaya yaz → hedefin üstüne
/// rename (<see cref="File.Move(string, string, bool)"/>), rename'in geçici sharing-violation penceresi sınırlı bir retry
/// ile absorbe edilir; okuma Delete-share'lidir. Okuyan hiçbir zaman yarım/bozuk bir dosya görmez. Dört dosya aynı
/// protokolü paylaşır, kopyalamaz (CLAUDE.md kopya yasağı): küçük durum dosyaları metin varyantlarını
/// (<see cref="WriteAllText"/>, <see cref="ReadAllTextSharingDelete"/>), birkaç MB'lık defterler tüm içeriği UTF-16 ara
/// string'e çevirmeyen akış varyantlarını (<see cref="Write"/>, <see cref="OpenReadSharingDelete"/>) kullanır — paylaşım ve
/// retry kuralı ikisinde de AYNI koddur.
///
/// <para>Gecikme GÖMÜLMEZ, çağırandan gelir (D8): üretim varsayılanı ve test dikişi
/// <see cref="BuildStateStore.RenameRetryDelay"/>'dedir.</para>
/// </summary>
internal static class AtomicFile
{
    /// <summary>Atomik rename'in retry bütçesi: 20 deneme x üretim backoff'u (5ms) ≈ 100ms üst sınır.</summary>
    internal const int RenameAttempts = 20;

    /// <summary>
    /// <paramref name="text"/>'i <paramref name="path"/>'e atomik olarak yazar (iskelet: <see cref="WriteViaTemp"/>).
    /// </summary>
    /// <param name="retryDelay">Başarısız bir rename denemesinden SONRAKİ gecikme (parametre: 1-based deneme no).</param>
    internal static void WriteAllText(string path, string text, Action<int> retryDelay) =>
        WriteViaTemp(path, tmp => File.WriteAllText(tmp, text), retryDelay);

    /// <summary>
    /// <see cref="WriteAllText"/>'in AKIŞ varyantı: <paramref name="write"/> geçici dosyanın akışına yazar — büyük defterler
    /// tüm içeriği tek bir UTF-16 string'e çevirmeden serileştirir. Akış rename'den ÖNCE kapanır (açık kalsa
    /// <see cref="File.Move(string, string, bool)"/> kendi tutamağına takılırdı); gerisi (klasör, tmp adı, retry'lı rename,
    /// başarısızlıkta temizlik) <see cref="WriteViaTemp"/> ile birebir aynıdır.
    /// </summary>
    /// <param name="retryDelay">Başarısız bir rename denemesinden SONRAKİ gecikme (parametre: 1-based deneme no).</param>
    internal static void Write(string path, Action<Stream> write, Action<int> retryDelay) =>
        WriteViaTemp(path, tmp =>
        {
            using var stream = File.Create(tmp);
            write(stream);
        }, retryDelay);

    /// <summary>
    /// İki yazım varyantının ORTAK iskeleti: <paramref name="writeTemp"/> geçici dosyayı (<c>path.&lt;guid&gt;.tmp</c>) yazar,
    /// sonra hedefin üstüne atomik rename edilir; klasör yoksa açılır. Rename bütçeyi aşarsa (ya da yazım/yazıcı fırlarsa)
    /// geçici dosya best-effort silinir ve ORİJİNAL istisna yayılır.
    /// </summary>
    private static void WriteViaTemp(string path, Action<string> writeTemp, Action<int> retryDelay)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            writeTemp(tmp);
            MoveAtomicWithRetry(tmp, path, retryDelay);
        }
        catch
        {
            // [Review Minor 4] rename retry bütçesini aşarsa (veya yazım sonrası başka bir şey fırlarsa) tmp
            // dosyası diskte öksüz kalmasın — best-effort temizlik, orijinal exception önceliklidir.
            try { File.Delete(tmp); } catch { /* best-effort, temizlik başarısızlığı orijinal hatayı gölgelemez */ }
            throw;
        }
    }

    /// <summary>
    /// Okuma için açılan akış: <c>FileShare.ReadWrite | FileShare.Delete</c> — durum dosyalarının ve iki büyük defterin TEK
    /// okuma açışı. Varsayılan <c>FileShare.Read</c> (<see cref="File.OpenRead(string)"/>, <see cref="File.ReadAllText(string)"/>)
    /// Delete-share İZİN VERMEZ: hedefte DELETE erişimi tutan bir taraf varken (dosyayı silen/yeniden adlandıran başka bir
    /// process) açış sharing-violation ile reddedilir; nazik bir reader Delete-share'i AÇIKÇA verir. DİKKAT: bu, yazıcının
    /// rename'ini açık okuyucuya rağmen GEÇİRMEZ — Windows hedefin üstüne rename'i, açık tutamak Delete-share verse bile
    /// reddeder (ölçüldü); rename o tutamak kapanana kadar <see cref="MoveAtomicWithRetry"/>'ın bütçeli retry'ıyla absorbe
    /// edilir. Akışla okumada tutamak ayrıştırma boyunca açık kaldığı için o pencere string okumasından uzundur.
    /// Okuma hatası (kilit, izin, dosya yok) ÇAĞIRANA yayılır — "okunamadı" ile "bozuk" ayrımı çağıranın kararıdır.
    /// Akışı çağıran kapatır.
    /// </summary>
    internal static FileStream OpenReadSharingDelete(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>
    /// <see cref="File.ReadAllText(string)"/> yerine: bkz. <see cref="OpenReadSharingDelete"/> (paylaşım kuralı tek yerde).
    /// </summary>
    internal static string ReadAllTextSharingDelete(string path)
    {
        using var fs = OpenReadSharingDelete(path);
        using var reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// <see cref="File.Move(string, string, bool)"/> hedefte açık bir okuma tutamağı (handle) VARKEN — tutamak Delete-share
    /// vermiş olsa BİLE — geçici bir sharing-violation (<see cref="IOException"/>/<see cref="UnauthorizedAccessException"/>)
    /// ile reddedilir (ölçüldü, bkz. <see cref="OpenReadSharingDelete"/>): ret tutamak açık kaldığı sürece sürer ve
    /// kapanınca biter — kapanıştan sonra kalan bir "yarış penceresi" değil. Bu GERÇEK VERİ KAYBI değildir — tmp dosya
    /// hâlâ diskte durur; kısa, sınırlı bir retry tutamağın kapanmasını bekler (bütçe tükenirse özgün istisna yayılır;
    /// bkz. RetryingMsBuildInvoker'daki MSB302x contention retry deseni).
    /// </summary>
    private static void MoveAtomicWithRetry(string tmp, string target, Action<int> retryDelay)
        // [B2] Döngünün kendisi ortak (SyncRetry) — burada yalnız BU yolun kararları durur: kaç deneme, hangi
        // istisna geçici, gecikme nereden gelir, bütçe tükenince ne olur (burada: orijinal istisna yayılır).
        => SyncRetry.Run(
            () => File.Move(tmp, target, overwrite: true),
            RenameAttempts,
            ex => ex is IOException or UnauthorizedAccessException,
            // [fix round 2] SyncRetry 0-based index verir; dikiş 1-based deneme no alır — uyarlama burada.
            failedAttemptIndex => retryDelay(failedAttemptIndex + 1),
            rethrowWhenExhausted: true);
}
