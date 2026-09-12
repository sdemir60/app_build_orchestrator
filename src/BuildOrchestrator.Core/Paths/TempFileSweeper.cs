namespace BuildOrchestrator.Core.Paths;

/// <summary>
/// [optimize] Atomik-yazma artığı <c>&lt;hedef&gt;.&lt;guid&gt;.tmp</c> dosyalarının TEK süpürücüsü. Aynı
/// geçici-ad desenini kullanan her defter (bugün <c>BuildStateStore</c>, <c>EvaluationCache</c> ve
/// <c>SourceHashCache</c>) buradan süpürülür; desen zaten yazan tarafta yaşıyor, süpüren tarafta ikinci kez
/// YAZILMAZ.
///
/// <para>Normalde bu dosyalar hiç kalmaz: yazım ya rename ile biter ya da catch bloğu tmp'yi siler. Süreç
/// yazımın ortasında öldürülürse (job object hard-kill, güç kesintisi) artık diskte kalır — süpürme yalnız
/// onu hedefler. <b>Eşik</b> bu yüzden vardır: taze bir tmp AKTİF bir yazımın olabilir ve silinmesi o yazımı
/// bozardı; saatlerce duran bir tmp ise ancak düşmüş bir yazımdan kalmıştır.</para>
///
/// <para>Süpürme hedef-başınadır: her defter yalnız KENDİ adının desenini görür
/// (<c>build-state.json.*.tmp</c> ≠ <c>source-hash-cache.json.*.tmp</c>), bu yüzden aynı klasörü paylaşan
/// defterler birbirinin yarım yazımını silemez.</para>
/// </summary>
internal static class TempFileSweeper
{
    /// <summary><paramref name="targetPath"/>'in yanında duran kendi desenli bayat tmp'leri siler; silinen
    /// sayısını döner. Best-effort: kilitli/erişilemez dosya sessizce atlanır (hijyen adımı hiçbir zaman
    /// çağıranı düşürmez), dizin yoksa 0 döner.</summary>
    /// <param name="utcNow">[D8] Eşiğin ölçüldüğü saat; <c>null</c> ise <see cref="DateTime.UtcNow"/>.</param>
    internal static int Sweep(string targetPath, TimeSpan olderThan, Func<DateTime>? utcNow)
    {
        string? dir = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;

        DateTime cutoff = (utcNow?.Invoke() ?? DateTime.UtcNow) - olderThan;
        int removed = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(dir, Path.GetFileName(targetPath) + ".*.tmp"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) > cutoff) continue;
                    File.Delete(file);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* kilitli/erişilemez → atla */ }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* dizin okunamadı → süpürme yok */ }
        return removed;
    }
}
