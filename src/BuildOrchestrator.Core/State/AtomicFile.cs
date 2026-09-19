using BuildOrchestrator.Core.Scheduling;

namespace BuildOrchestrator.Core.State;

/// <summary>
/// Durum dosyalarının (<c>build-state.json</c>, <c>run-inflight.json</c>) TEK atomik yazım yolu: geçici dosyaya
/// yaz → hedefin üstüne rename (<see cref="File.Move(string, string, bool)"/>), rename'in geçici
/// sharing-violation penceresi sınırlı bir retry ile absorbe edilir. Okuyan hiçbir zaman yarım/bozuk bir dosya
/// görmez. İki defter aynı protokolü paylaşır, kopyalamaz (CLAUDE.md kopya yasağı).
///
/// <para>Gecikme GÖMÜLMEZ, çağırandan gelir (D8): üretim varsayılanı ve test dikişi
/// <see cref="BuildStateStore.RenameRetryDelay"/>'dedir.</para>
/// </summary>
internal static class AtomicFile
{
    /// <summary>Atomik rename'in retry bütçesi: 20 deneme x üretim backoff'u (5ms) ≈ 100ms üst sınır.</summary>
    internal const int RenameAttempts = 20;

    /// <summary>
    /// <paramref name="text"/>'i <paramref name="path"/>'e atomik olarak yazar; klasör yoksa açılır. Rename bütçeyi
    /// aşarsa (ya da yazım sonrası başka bir şey fırlarsa) geçici dosya best-effort silinir ve ORİJİNAL istisna
    /// yayılır.
    /// </summary>
    /// <param name="retryDelay">Başarısız bir rename denemesinden SONRAKİ gecikme (parametre: 1-based deneme no).</param>
    internal static void WriteAllText(string path, string text, Action<int> retryDelay)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, text);
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
    /// <see cref="File.Move(string, string, bool)"/> hedefte açık bir okuma handle'ı olduğunda — Delete-share
    /// verilmiş olsa BİLE — geçici bir sharing-violation (<see cref="IOException"/>/<see cref="UnauthorizedAccessException"/>)
    /// ile başarısız olabilir (gözlemlenen Windows davranışı: handle kapanışı ile rename arasında kısa bir yarış
    /// penceresi kalıyor). Bu GERÇEK VERİ KAYBI değildir — tmp dosya hâlâ diskte durur; kısa, sınırlı bir retry
    /// bu geçici pencereyi absorbe eder (bkz. RetryingMsBuildInvoker'daki MSB302x contention retry deseni).
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
