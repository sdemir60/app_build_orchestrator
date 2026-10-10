using System.Globalization;
using BuildOrchestrator.Core.Paths;

namespace BuildOrchestrator.Core.Logs;

/// <summary>
/// [PERF Faz C/C2 · karar 1] Koşu logu saklama: <c>&lt;logsRoot&gt;\run-&lt;ts&gt;\</c> klasörleri <see cref="KeepFor"/> kadar
/// saklanır; EN SON koşunun klasörü ne kadar eski olursa olsun kalır (son koşunun logu her zaman okunabilsin).
/// Karar saftır (<see cref="Select"/>: yalnız ad + saat), silme ince bir kabuktur (<see cref="Prune"/>) — Supervisor
/// açılışta bir kez, arka planda çağırır.
///
/// <para><b>Silmenin sınırı</b> [değişmez: OutDir'e dokunulmaz — log kökü bir çıktı yolu değildir]: silme yalnız log
/// kökünün DOĞRUDAN altındaki, adı <see cref="RunLogPaths.RunDirName"/> kalıbına TAM uyan klasörlerde olur. Başka bir
/// klasör, adı kalıba uyan bir dosya ve yeniden-ayrıştırma noktaları (sembolik bağ, junction) hiçbir koşulda SEÇİLMEZ.
/// Silme sırasında da bağlantıya inilmez: klasörün İÇİNDEKİ bağlantılar için ve seçimle silme arasında bir bağlantıyla
/// yer değiştirmiş klasörün KENDİSİ için yalnız bağlantının kendisi kaldırılır, hedefe dokunulmaz
/// (<c>DeleteWithoutFollowingLinks</c>). Aktif koşunun klasörü de kendiliğinden güvendedir: damgası şimdiye yakındır,
/// pencerenin çok içindedir.</para>
/// </summary>
public static class RunLogRetention
{
    /// <summary>[karar 1] Üç gün. TEK tanım: README ve ARCHITECTURE (§8.5, §10.2, §16, §22) süreyi buradan anlatır.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(3);

    /// <summary>Bir <see cref="Prune"/> çağrısının silebileceği en çok klasör: yüzlerce eski klasörün birikmiş olduğu bir
    /// açılış (saklamanın ilk kez devreye girdiği makine) motorun arka planını ve diski tek seferde yormasın — kalanı
    /// sonraki açılışta gider.</summary>
    internal const int MaxFoldersPerPrune = 200;

    /// <summary>
    /// Silinecek klasörler: adı <see cref="RunLogPaths.RunDirName"/> kalıbına TAM uyan, damgası <c>now - KeepFor</c>'dan
    /// KESİN eski olan ve EN YENİ koşu klasörü OLMAYAN her giriş, en eskiden başlayarak ve girişteki yazımıyla. Kalıba
    /// uymayan ad ne seçilir ne de "en yeni"yi belirler. "En yeni" yalnız BAŞLAMIŞ koşular (damga &lt;= <paramref name="now"/>)
    /// arasından seçilir: damgası <paramref name="now"/>'dan yeni olan klasör (saati geri alınmış makine, elle verilmiş
    /// ad) hiçbir koşulda seçilmez ve gerçek son koşunun korumasını da sökemez.
    /// </summary>
    /// <param name="runDirectories">Klasör yolları ya da yalnız adlar; damga son yol parçasından okunur.</param>
    public static IReadOnlyList<string> Select(IReadOnlyList<string> runDirectories, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(runDirectories);

        var runs = new List<(string Dir, DateTimeOffset StartedAt)>();
        foreach (string dir in runDirectories)
        {
            string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(dir));
            // Damgası now'dan yeni olan klasör ne silinir (eski değil) ne de "en yeni" olur: saati geri alınmış makinedeki ya da
            // elle ileri tarihli verilmiş bir ad, gerçek son koşunun korumasını sökmesin.
            if (RunLogPaths.TryParseRunDirName(name, out var startedAt) && startedAt <= now) runs.Add((dir, startedAt));
        }
        if (runs.Count == 0) return [];

        // Aynı damgayı taşıyan iki klasör varsa ikisi de "en yeni"dir: hiçbiri silinmez.
        DateTimeOffset newest = runs.Max(r => r.StartedAt);
        DateTimeOffset cutoff = now - KeepFor;
        return runs
            .Where(r => r.StartedAt < cutoff && r.StartedAt != newest)
            .OrderBy(r => r.StartedAt)
            .ThenBy(r => r.Dir, StringComparer.Ordinal)
            .Select(r => r.Dir)
            .ToList();
    }

    /// <summary>
    /// <see cref="Select"/>'in seçtiklerini siler (en çok <see cref="MaxFoldersPerPrune"/> klasör) ve SİLİNEN sayıyı döner.
    /// IO hatası (açık kalmış bir log, erişim reddi) YUTULUR: o klasör bir sonraki açılışa kalır, sıradakiler silinmeye
    /// devam eder, hiçbir şey fırlatılmaz — saklama bir temizliktir, motoru ya da bir koşuyu asla düşürmez. Bir şey
    /// silindiyse ya da silinemediyse <paramref name="log"/>'a TEK özet satırı yazılır; yapacak bir şey yoksa sessizdir
    /// (motor açılışı her seferinde bir satır basmaz). Satır stderr'e gider (stdout YALNIZ NDJSON [D4]); bu sınıf
    /// konsola doğrudan yazmaz.
    /// </summary>
    public static int Prune(string logsRoot, DateTimeOffset now, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(logsRoot);
        ArgumentNullException.ThrowIfNull(log);

        List<string> victims;
        try
        {
            if (!Directory.Exists(logsRoot)) return 0;
            // Yalnız kökün DOĞRUDAN altındaki klasörler; yeniden-ayrıştırma noktasına (symlink/junction) hiç inilmez.
            var candidates = new DirectoryInfo(logsRoot).EnumerateDirectories(RunLogPaths.RunDirSearchPattern)
                .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0)
                .Select(d => d.FullName)
                .ToList();
            victims = Select(candidates, now).Take(MaxFoldersPerPrune).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"run logs: could not list '{logsRoot}' ({ex.Message}); nothing was removed");
            return 0;
        }

        int removed = 0, failed = 0;
        string? firstError = null;
        foreach (string dir in victims)
        {
            try
            {
                DeleteWithoutFollowingLinks(dir);
                removed++;
            }
            catch (DirectoryNotFoundException)
            {
                // Klasör araya girip biri tarafından (ör. kullanıcı) silinmiş: istenen son durum zaten sağlanıyor.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
                firstError ??= ex.Message;
            }
        }

        if (removed + failed > 0) log(Summary(removed, failed, firstError));
        return removed;
    }

    /// <summary>
    /// Koşu klasörünü içeriğiyle siler — SIKI politikayla: ilk IO hatası fırlar ve klasör sonraki açılışa kalır
    /// (<see cref="Prune"/> yutar). Bağlantılara (junction/symlink) İNMEZ: bağlantının KENDİSİ kaldırılır, hedefin
    /// içeriğine dokunulmaz — klasörün İÇİNDEKİ bağlantılar için de, seçimle silme arasında klasörün yerine bir bağlantı
    /// geçerse (TOCTOU) klasörün KENDİSİ için de (<see cref="LinkSafeTree"/> girişte kendi köküne taze bakar). Gezinme
    /// Clean'in ağaç silmesiyle (<c>CleanWorkspaceService.DeleteTree</c>) ORTAKTIR. <c>Directory.Delete(recursive)</c>
    /// kullanılmaz: bağlantıyı izlemez ama bu makinede (Windows, .NET 10) içinde junction olan bir klasörde bağlantıyı
    /// ve dosyaları kaldırdıktan sonra <see cref="UnauthorizedAccessException"/> fırlatıp boş klasörü geride bırakıyor:
    /// yanlış bir "silinemedi" satırı ve bir açılış gecikmesi. <c>internal</c>: <see cref="Prune"/> bağlantıyı seçimde
    /// eler, kök-bağlantı durumu ancak buradan doğrudan sınanabilir.
    /// </summary>
    internal static void DeleteWithoutFollowingLinks(string dir) =>
        LinkSafeTree.Delete(dir, File.Delete, subDir => Directory.Delete(subDir, recursive: false));

    private static string Summary(int removed, int failed, string? firstError)
    {
        string window = KeepFor.TotalDays.ToString("0.##", CultureInfo.InvariantCulture);
        int total = removed + failed;
        string noun = total == 1 ? "folder" : "folders";
        return failed == 0
            ? $"run logs: removed {removed} {noun} older than {window} days"
            : $"run logs: removed {removed} of {total} {noun} older than {window} days; the rest stay for the next start ({firstError})";
    }
}
