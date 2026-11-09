using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using BuildOrchestrator.Core.Io;
using BuildOrchestrator.Core.State;

namespace BuildOrchestrator.Core.Incremental;

/// <summary>
/// [D3] Kaynak dosya içerik özetlerinin kalıcı önbelleği: <c>yol → (boyut, mtime, sha256)</c>. Karar diskten
/// verildiği için her Sync/Build tüm girdi kümesinin özetini ister; bu önbellek olmadan bedel her koşuda
/// dosyaları BAŞTAN okumak olurdu. Önbellekle ödenen bedel yalnız <b>stat</b>'tir: boyut VE mtime aynıysa
/// dosya açılmaz.
///
/// <para><b>İçerik kararıyla çelişmez.</b> Aracın kendi çıktısında karar ÇIKTI zaman damgasından verilemez
/// (tarihlere yalnız başkasının derlediği çıktıda bakılır — <see cref="OutputEvidence"/>). Burada okunan
/// şey bir KAYNAK dosyanın stat bilgisidir ve karar da ondan değil,
/// içeriğin özetinden verilir: stat yalnız "özeti yeniden hesaplamaya gerek var mı" sorusunun ucuz cevabıdır.
/// <c>evaluation-cache.json</c> bugün zaten aynı hızlı yolu kullanır.</para>
///
/// <para><b>Racy dosya koruması (git'in index kuralının aynısı):</b> mtime'ı önbelleğin YAZILDIĞI ana çok
/// yakın (son <see cref="RacyWindow"/>) olan girdiler diske YAZILMAZ. Aynı saniye içinde, aynı boyutta
/// yeniden yazılan bir dosya aksi hâlde bayat bir özetle "değişmemiş" görünebilirdi.</para>
///
/// <para><b>İlk geçiş paraleldir.</b> Soğuk diskte bedel dosya BAŞINA açılış giderindedir (on-access tarama);
/// ölçümde sıralı okuma dosya başına 8,89 ms, 16 kanallı paralel okuma 1,86 ms sürdü. Bu yüzden ilk doldurma
/// <see cref="Prefill"/> ile paralel koşar; ilerlemesi gözlemciye (<see cref="IndexingReport"/> kullanıcıya satır
/// yazar) bildirilir ve defter sürerken ara kayıt alınır.</para>
///
/// <para>Bozuk/eksik önbellek dosyası sessizce boş kabul edilir; kaydetme atomiktir (temp + rename) ve IO
/// hatasında yutulur — önbellek SALT bir optimizasyondur, kaybı yalnız bir sonraki koşuda yeniden okumaya
/// mal olur.</para>
///
/// <para><b>[PERF Faz C/C1] Yalnız kirliyse yazılır, akışla okunur ve yazılır.</b> Gerçek OSYS'te defter birkaç
/// MB'lık JSON'dur; hiçbir özeti değişmeyen bir Sync/koşu onu yeniden yazmaz. Bayrağı yalnız
/// <see cref="MarkDirty"/> kaldırır; <see cref="Flush"/> onu yazmadan ÖNCE indirir, böylece yazım sürerken araya
/// giren yeni bir özet bayrağı yeniden kaldırır ve kaybolmaz. Yazılamayan defter (yutulan IO hatası) ve racy
/// pencerede dışarıda bırakılan girdi bayrağı kirli bırakır — pencere geçince sonraki <see cref="Flush"/> o
/// girdiyi de yazar. Defter UTF-16 ara string'e çevrilmez: okuma <c>AtomicFile.ReadJson</c>, yazım <c>AtomicFile.WriteJson</c> — Delete-share'li okuma ve
/// retry'lı atomik rename durum dosyalarıyla ORTAK koddur.</para>
/// </summary>
public sealed class SourceHashCache
{
    /// <summary>Önbelleğin dosya adı — cacheRoot altında, <c>evaluation-cache.json</c>'un yanında. Adın TEK
    /// sahibi burasıdır (kopya YASAK): Sync ve run yolları aynı dosyayı paylaşır.</summary>
    public const string FileName = "source-hash-cache.json";

    /// <summary>Kaydetme anına bu kadar yakın mtime'lı girdiler kalıcı hâle getirilmez (git'in "racy" kuralı).</summary>
    public static readonly TimeSpan RacyWindow = TimeSpan.FromSeconds(2);

    /// <summary>Konsola "ilk indeksleme" satırı yazmaya değecek dosya sayısı — altındaki her şey sessizdir
    /// (her koşuda değişen birkaç dosya için satır yazmak gürültü olurdu). Eşiği <see cref="IndexingReport"/> uygular.</summary>
    public const int NoisyPrefillThreshold = 500;

    private sealed record Entry(long Length, long MtimeTicks, string Hash);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly string _cachePath;
    private readonly ConcurrentDictionary<string, Entry> _entries;

    /// <summary>Defter bellekte diskteki hâlinden farklı mı (1 = kirli). Birden çok thread özet ekler (Prefill paralel
    /// okur, derece <see cref="IoParallelism.Degree"/>) → bayrak <c>Volatile</c>/<c>Interlocked</c> ile
    /// yönetilir.</summary>
    private int _dirty;

    public SourceHashCache(string cachePath)
    {
        _cachePath = cachePath ?? throw new ArgumentNullException(nameof(cachePath));
        _entries = Load(cachePath);
        WasEmpty = _entries.IsEmpty;
    }

    /// <summary>Önbellek diskte yoktu ya da boştu — bu koşu ilk indekslemeyi yapacak.</summary>
    public bool WasEmpty { get; }

    /// <summary>
    /// Defteri değiştiren HER yol bunu çağırır (<c>_entries</c> yazımından SONRA); bayrağı kaldıran TEK yer burasıdır
    /// (yeni bir değiştiren yol da çağırmak zorundadır). <see cref="Flush"/> bayrağı <c>Interlocked.Exchange</c> ile
    /// okuyup indirir.
    /// </summary>
    private void MarkDirty() => Volatile.Write(ref _dirty, 1);

    /// <summary>
    /// [D8] Atomik rename retry'ının gecikme dikişi — <see cref="BuildStateStore.RenameRetryDelay"/> ile aynı desen (parametre:
    /// 1-based deneme no). Üretimde null → <see cref="BuildStateStore.DefaultRenameRetryDelay"/> (üretim backoff'unun tek sahibi).
    /// </summary>
    internal Action<int>? RenameRetryDelay { get; set; }

    /// <summary>Gerçekten koşacak gecikme: dikiş kuruluysa o, değilse ÜRETİM varsayılanı
    /// (<see cref="BuildStateStore.EffectiveRenameRetryDelay"/> ile aynı desen). Ayrı üye olmasının sebebi testtir —
    /// varsayılanı no-op'a çeviren bir mutasyon aksi halde süiti yeşil bırakırdı (<c>LedgerRetryDefaultTests</c>).</summary>
    internal Action<int> EffectiveRenameRetryDelay => RenameRetryDelay ?? BuildStateStore.DefaultRenameRetryDelay;

    /// <summary>
    /// Dosyanın içerik özeti. Boyut ve mtime önbellektekiyle aynıysa dosya AÇILMAZ. Okunamayan / var olmayan
    /// dosya <c>null</c> döner — canlı build ↔ tarama yarışında kaybolan dosya kararı düşürmez, yalnız o
    /// terimi eler.
    /// </summary>
    public string? HashOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;

            long length = info.Length;
            long mtime = info.LastWriteTimeUtc.Ticks;
            if (_entries.TryGetValue(path, out var hit) && hit.Length == length && hit.MtimeTicks == mtime)
                return hit.Hash;

            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            _entries[path] = new Entry(length, mtime, hash);
            MarkDirty();
            return hash;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// [test seam] Dosyanın GERÇEK boyut+mtime'ıyla ama verilen <paramref name="hash"/> ile bir kayıt yazar ve
    /// önbelleği diske işler. Önbellek boyut+mtime eşleşince dosyayı açmadığı için sonraki okuma bu özeti
    /// döner — kabul koşusu bir kaynağın "düzenlenmiş" hâlini gerçek dosyaya dokunmadan böyle simüle eder.
    /// Disk biçimi yalnız bu sınıfta tanımlı kalsın diye testler kaydı buradan yazar (kopya YASAK).
    /// </summary>
    internal void Seed(string path, string hash)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(hash);
        var info = new FileInfo(path);
        _entries[path] = new Entry(info.Length, info.LastWriteTimeUtc.Ticks, hash);
        MarkDirty();
        Flush();
    }

    /// <summary>Bu yolun özeti önbellekte GEÇERLİ mi — okumadan, yalnız stat ile.</summary>
    public bool IsCached(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists
                && _entries.TryGetValue(path, out var hit)
                && hit.Length == info.Length
                && hit.MtimeTicks == info.LastWriteTimeUtc.Ticks;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Önbellekte olmayan dosyaların özetlerini PARALEL hesaplar. Önce ucuz bir stat geçişiyle eksik küme
    /// bulunur (bu sayı <paramref name="observer"/>'a verilir, satırı yazma kararı gözlemcinindir), sonra
    /// yalnız o küme okunur.
    ///
    /// <para><b>İş başka thread'lerde, döngü çağıranda:</b> hash işi <c>Task.Run</c> içinde koşar; çağıran thread
    /// her <see cref="PrefillTickMs"/>'de bir uyanıp ilerlemeyi gözlemciye bildirir ve
    /// <see cref="PrefillFlushIntervalMs"/>'de bir defteri diske alır. Gözlemci bu yüzden HEP çağıran thread'de
    /// çalışır (kilit gerekmez) ve soğuk ilk indeksleme (~51 sn) yarıda kesilirse o ana kadarki özetler kaybolmaz.</para>
    ///
    /// <para><b>Hata türleri değişmez:</b> işin sonucu <c>GetResult()</c> ile alınır — gövde hatası
    /// <see cref="AggregateException"/> (Parallel.ForEach'in kendi türü), iptal <see cref="OperationCanceledException"/>
    /// olarak çıkar; bekleme fırlatmaz (<see cref="Task.WaitAny(Task[], int)"/>). Hata/iptalde
    /// <see cref="IPrefillObserver.Finished"/> çağrılmaz.</para>
    /// </summary>
    /// <param name="observer">Okuma BAŞLAMADAN (<see cref="IPrefillObserver.Started"/>, yalnız eksik &gt; 0 ise), iş
    /// sürerken her tikte, bitince bildirilir (kullanıcı beklemenin nedenini görsün).</param>
    /// <returns>Gerçekten okunup özetlenen dosya sayısı.</returns>
    public int Prefill(IEnumerable<string> paths, IPrefillObserver? observer = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        // Eksik kümeyi bulmak 23 bin dosyalık bir stat geçişidir — o da IO'dur ve paralelleştirilebilir.
        var missing = paths.Distinct(StringComparer.OrdinalIgnoreCase)
            .AsParallel().WithDegreeOfParallelism(IoParallelism.Degree)
            .Where(p => !IsCached(p))
            .ToList();
        if (missing.Count == 0) return 0;

        observer?.Started(missing.Count);

        var clock = Stopwatch.StartNew();
        var elapsedMs = PrefillElapsedMs ?? (() => clock.ElapsedMilliseconds);
        var tick = PrefillTick ?? (work => Task.WaitAny([work], PrefillTickMs) >= 0);
        var hasher = PrefillHasher ?? HashOf;

        int done = 0;
        var worker = Task.Run(() => Parallel.ForEach(
            missing,
            new ParallelOptions { MaxDegreeOfParallelism = IoParallelism.Degree, CancellationToken = ct },
            path =>
            {
                hasher(path);
                Interlocked.Increment(ref done);
            }));

        long nextFlushMs = PrefillFlushIntervalMs;
        while (!tick(worker))
        {
            long elapsed = elapsedMs();                       // tik başına TEK okuma: Progress ve ara kayıt aynı anı görür
            observer?.Progress(Volatile.Read(ref done), missing.Count, elapsed);
            if (elapsed >= nextFlushMs)
            {
                Flush();                                      // bu ana kadar özetlenenler diske: yarıda kesilen indeksleme kaldığı yerden sürer
                nextFlushMs = elapsed + PrefillFlushIntervalMs;
            }
        }

        worker.GetAwaiter().GetResult();                      // iş hatayla/iptalle bittiyse burada (bugünkü türle) fırlar
        observer?.Finished(missing.Count, elapsedMs());
        return missing.Count;
    }

    /// <summary>[D8] Prefill döngüsünün tik aralığı: ilerleme bu sıklıkla bildirilir, bitiş en geç bu kadar gecikmeyle fark edilir.</summary>
    internal const int PrefillTickMs = 500;

    /// <summary>Prefill sürerken defterin diske alınma aralığı (ms) — ilk ve sonraki sınırlar arasındaki süre.</summary>
    internal const long PrefillFlushIntervalMs = 10_000;

    /// <summary>[D8] Prefill döngüsünün bir tiki: iş bitene kadar bekler, <c>true</c> = iş bitti. Üretimde null →
    /// <see cref="PrefillTickMs"/> zaman aşımlı, fırlatmayan bekleme (testte kapıyla elle sürülür).</summary>
    internal Func<Task, bool>? PrefillTick { get; set; }

    /// <summary>[D8] Prefill'in başından beri geçen süre (ms) — Progress/ara kayıt/Finished buradan okunur.
    /// Üretimde null → gerçek <see cref="Stopwatch"/>.</summary>
    internal Func<long>? PrefillElapsedMs { get; set; }

    /// <summary>[D8] Dosya başına iş (testte kapıda bekletilir). Üretimde null → <see cref="HashOf"/>.</summary>
    internal Func<string, string?>? PrefillHasher { get; set; }

    /// <summary>
    /// Önbelleği diske yazar (atomik temp + rename). Racy girdiler DIŞARIDA bırakılır. Değişen bir girdi yoksa
    /// HİÇ yazmaz; yazılamayan ya da racy olduğu için dışarıda kalan bir girdi varsa defter kirli kalır.
    /// </summary>
    public void Flush()
    {
        // Bayrak yazımdan ÖNCE inerse (Exchange) Flush sürerken gelen her yeni özet bayrağı yeniden kaldırır: işaret
        // kaybolmaz. Kirli değilse hiçbir şey yapılmaz (snapshot, serileştirme ve rename tümüyle atlanır).
        if (Interlocked.Exchange(ref _dirty, 0) == 0) return;

        // Racy pencerenin kesimi, süpürme eşiğiyle AYNI saatten (UtcNow seam'i) okunur; üretimde null → gerçek saat.
        long cutoff = (UtcNow?.Invoke() ?? DateTime.UtcNow).Add(-RacyWindow).Ticks;
        // Doğrudan numaralandırma — ToArray() anlık görüntüsü YOK: o, her kirli Flush'ta girdi sayısı kadar ek bir dizi (LOH)
        // demekti. ConcurrentDictionary numaralandırması eşzamanlı yazımlara karşı güvenlidir ama anlık görüntü DEĞİLDİR; bu
        // yazım için yeterli: girdiler değişmez kayıtlardır (her biri ya eski ya yeni hâliyle görülür) ve bayrak numaralandırmadan
        // ÖNCE indi — numaralandırma sürerken gelen her değişiklik bayrağı yeniden kaldırır, sonraki Flush onu yazar.
        var persistable = new Dictionary<string, Entry>(_entries.Count, StringComparer.OrdinalIgnoreCase);
        bool leftOutRacy = false;
        foreach (var (key, entry) in _entries)
        {
            if (entry.MtimeTicks < cutoff) persistable[key] = entry;
            else leftOutRacy = true;
        }

        try
        {
            AtomicFile.WriteJson(_cachePath, persistable, Json, EffectiveRenameRetryDelay);
            if (leftOutRacy) MarkDirty(); // dışarıda kalan racy girdi: pencere geçince sonraki Flush onu da yazar
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MarkDirty(); // retry bütçesi de tükendi: defter kirli kalır, sonraki Flush yeniden dener
        }
    }

    /// <summary>
    /// [optimize] Kök altındaki ÖLÜ girdileri budar: anahtarı (kaynak dosya yolu) artık diskte olmayan
    /// kayıtlar gider, kaldırılan sayı döner. Bu defter csproj'la değil KAYNAK DOSYA'yla anahtarlandığı için
    /// en hızlı birikendir — silinen/taşınan her dosya burada bir ölü girdi bırakır.
    /// <para>Önbellek SALT bir optimizasyondur: budanan girdi bir sonraki koşuda yeniden özetlenir, hiçbir
    /// karar değişmez. Kök dışındaki girdiler korunur; prefix normalizasyonu
    /// <see cref="Paths.RootScope"/>'tadır. Gerçekten budandıysa <see cref="Flush"/> çağrılır.</para>
    /// </summary>
    public int PruneMissingUnderRoot(string rootPath)
    {
        if (Paths.RootScope.NormalizeRoot(rootPath) is not { } prefix) return 0;

        var dead = _entries.Keys.Where(k => Paths.RootScope.Contains(prefix, k) && !File.Exists(k)).ToList();
        int removed = 0;
        foreach (string key in dead) if (_entries.TryRemove(key, out _)) removed++;
        if (removed > 0) { MarkDirty(); Flush(); }
        return removed;
    }

    /// <summary>[optimize] Yarım kalmış atomik yazımlardan kalan kendi <c>.tmp</c> artıklarını süpürür
    /// (bkz. <see cref="Paths.TempFileSweeper"/>); silinen sayıyı döner.</summary>
    public int SweepOrphanTempFiles(TimeSpan olderThan) =>
        Paths.TempFileSweeper.Sweep(_cachePath, olderThan, UtcNow);

    /// <summary>[D8] Süpürme eşiğinin ve <see cref="Flush"/>'ın racy pencere kesiminin okuduğu saat — testte
    /// ileri alınır ya da enjekte edilir, üretimde <c>null</c> (gerçek saat).</summary>
    internal Func<DateTime>? UtcNow { get; set; }

    private static ConcurrentDictionary<string, Entry> Load(string path)
    {
        var empty = new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return empty;
        try
        {
            // [PERF Faz C/C1] Akışla okuma: defter UTF-16 ara string'e çevrilmez (birkaç MB'lık JSON'un iki katı bellek).
            // Delete-share'li (AtomicFile): durum dosyalarıyla aynı okuma kuralı.
            var loaded = AtomicFile.ReadJson<Dictionary<string, Entry>>(path, Json);
            return loaded is null ? empty : new ConcurrentDictionary<string, Entry>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return empty; // bozuk önbellek → yeniden kurulur
        }
    }
}
