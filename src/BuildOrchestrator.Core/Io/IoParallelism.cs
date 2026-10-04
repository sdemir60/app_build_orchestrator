namespace BuildOrchestrator.Core.Io;

/// <summary>
/// [PERF Faz D / karar 10] Dosya IO paralelliğinin TEK derecesi: içerik özetinin ilk doldurması
/// (<c>SourceHashCache.Prefill</c>: eksik taraması ve okumalar), girdi toplama, fingerprint ısıtma ve çıktı kontrolleri
/// (<c>IncrementalRunBinder</c>) aynı derece ile koşar. Dağınık bir literal aynı değeri birden çok yerde
/// tanımlamak olurdu (kopya YASAK) ve biri değişince diğerleri sessizce ayrışırdı. Derece, soğuk diskte sıralı okumanın
/// gecikmesini örtmek için seçilmiştir (ARCHITECTURE'daki ilk doldurma ölçümü); derleme işçisi sayısından
/// (<c>WorkerBudget</c>) BAĞIMSIZDIR — o derleme süreçlerini sayar, bu dosya okumalarını.
/// </summary>
public static class IoParallelism
{
    /// <summary>Aynı anda koşan dosya okuma/özet işi sayısı.</summary>
    public const int Degree = 16;
}
