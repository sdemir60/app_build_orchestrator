using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace BuildOrchestrator.Core.Incremental;

/// <summary>
/// [D3] Kaynak dosya içerik özetlerinin kalıcı önbelleği: <c>yol → (boyut, mtime, sha256)</c>. Karar diskten
/// verildiği için her Sync/Build tüm girdi kümesinin özetini ister; bu önbellek olmadan bedel her koşuda
/// dosyaları BAŞTAN okumak olurdu. Önbellekle ödenen bedel yalnız <b>stat</b>'tir: boyut VE mtime aynıysa
/// dosya açılmaz.
///
/// <para><b>§4 ile çelişmez.</b> "DLL/bin timestamp'ı asla okunmaz" kuralı ÇIKTI zaman damgaları içindir —
/// karar oradan verilemez. Burada okunan şey bir KAYNAK dosyanın stat bilgisidir ve karar da ondan değil,
/// içeriğin özetinden verilir: stat yalnız "özeti yeniden hesaplamaya gerek var mı" sorusunun ucuz cevabıdır.
/// <c>evaluation-cache.json</c> bugün zaten aynı hızlı yolu kullanır.</para>
///
/// <para><b>Racy dosya koruması (git'in index kuralının aynısı):</b> mtime'ı önbelleğin YAZILDIĞI ana çok
/// yakın (son <see cref="RacyWindow"/>) olan girdiler diske YAZILMAZ. Aynı saniye içinde, aynı boyutta
/// yeniden yazılan bir dosya aksi hâlde bayat bir özetle "değişmemiş" görünebilirdi.</para>
///
/// <para><b>İlk geçiş paraleldir.</b> Soğuk diskte bedel dosya BAŞINA açılış giderindedir (on-access tarama);
/// ölçümde sıralı okuma dosya başına 8,89 ms, 16 kanallı paralel okuma 1,86 ms sürdü. Bu yüzden ilk doldurma
/// <see cref="Prefill"/> ile paralel koşar ve kullanıcıya bir satır yazılır.</para>
///
/// <para>Bozuk/eksik önbellek dosyası sessizce boş kabul edilir; kaydetme atomiktir (temp + rename) ve IO
/// hatasında yutulur — önbellek SALT bir optimizasyondur, kaybı yalnız bir sonraki koşuda yeniden okumaya
/// mal olur.</para>
/// </summary>
public sealed class SourceHashCache
{
    /// <summary>Önbelleğin dosya adı — cacheRoot altında, <c>evaluation-cache.json</c>'un yanında. Adın TEK
    /// sahibi burasıdır (kopya YASAK): Sync ve run yolları aynı dosyayı paylaşır.</summary>
    public const string FileName = "source-hash-cache.json";

    /// <summary>Kaydetme anına bu kadar yakın mtime'lı girdiler kalıcı hâle getirilmez (git'in "racy" kuralı).</summary>
    public static readonly TimeSpan RacyWindow = TimeSpan.FromSeconds(2);

    /// <summary>Konsola "ilk indeksleme" satırı yazmaya değecek dosya sayısı — altındaki her şey sessizdir
    /// (her koşuda değişen birkaç dosya için satır yazmak gürültü olurdu).</summary>
    public const int NoisyPrefillThreshold = 500;

    private sealed record Entry(long Length, long MtimeTicks, string Hash);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly string _cachePath;
    private readonly ConcurrentDictionary<string, Entry> _entries;

    public SourceHashCache(string cachePath)
    {
        _cachePath = cachePath ?? throw new ArgumentNullException(nameof(cachePath));
        _entries = Load(cachePath);
        WasEmpty = _entries.IsEmpty;
    }

    /// <summary>Önbellek diskte yoktu ya da boştu — bu koşu ilk indekslemeyi yapacak.</summary>
    public bool WasEmpty { get; }

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
            return hash;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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
    /// bulunur (bu sayı <paramref name="announce"/>'a verilir, satırı yazma kararı çağıranındır), sonra
    /// yalnız o küme okunur.
    /// </summary>
    /// <param name="announce">Eksik dosya sayısı — okuma BAŞLAMADAN çağrılır (kullanıcı beklemenin nedenini
    /// önceden görsün). Sayı 0 ise hiç çağrılmaz.</param>
    /// <returns>Gerçekten okunup özetlenen dosya sayısı.</returns>
    public int Prefill(IEnumerable<string> paths, Action<int>? announce = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        // Eksik kümeyi bulmak 23 bin dosyalık bir stat geçişidir — o da IO'dur ve paralelleştirilebilir.
        var missing = paths.Distinct(StringComparer.OrdinalIgnoreCase)
            .AsParallel().WithDegreeOfParallelism(16)
            .Where(p => !IsCached(p))
            .ToList();
        if (missing.Count == 0) return 0;

        announce?.Invoke(missing.Count);

        Parallel.ForEach(
            missing,
            new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = ct },
            path => HashOf(path));

        return missing.Count;
    }

    /// <summary>Önbelleği diske yazar (atomik temp + rename). Racy girdiler DIŞARIDA bırakılır.</summary>
    public void Flush()
    {
        long cutoff = DateTime.UtcNow.Add(-RacyWindow).Ticks;
        var persistable = _entries
            .Where(kv => kv.Value.MtimeTicks < cutoff)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        string tmp = _cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            File.WriteAllText(tmp, JsonSerializer.Serialize(persistable, Json));
            File.Move(tmp, _cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(tmp); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
        }
    }

    private static ConcurrentDictionary<string, Entry> Load(string path)
    {
        var empty = new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return empty;
        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path), Json);
            return loaded is null ? empty : new ConcurrentDictionary<string, Entry>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return empty; // bozuk önbellek → yeniden kurulur
        }
    }
}
