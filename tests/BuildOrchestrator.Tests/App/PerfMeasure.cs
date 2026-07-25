namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [G1 review round 1] Perf testlerinin ORTAK ölçüm iskeleti: warmup + GC + N örnek + medyan. Desen ilk kez
/// <see cref="ListRealizationPerfTests"/>'te (E6/L1) kuruldu, <see cref="GraphRealizationPerfTests"/>'te (G1)
/// kopyalanmıştı — tek yere toplandı (kopya YASAK, CLAUDE.md).
///
/// <para><b>Neden bu iskelet:</b> tek atış ölçüm JIT/GC sıçramasına açıktır. Önce <paramref name="warmups"/> kez
/// ısıtılır (JIT + kaynak sözlüğü ilk çözümlemeleri), sonra her örnekten ÖNCE tam bir GC turu çevrilir
/// (<c>Collect → WaitForPendingFinalizers → Collect</c>) ve örneklerin MEDYANI alınır (ortalama DEĞİL — tek bir
/// aykırı koşum ortalamayı taşır, medyanı taşımaz).</para>
///
/// <para><b>Örnek tipi çağırana aittir:</b> liste testi tek bir skaler (ms) ölçer, graf testi üç fazlı bir kırılım
/// ölçer ve medyanı FAZ BAZINDA alır. Ortaklaştırılan şey ölçüm SEMANTİĞİDİR (kaç ısıtma, ne zaman GC, hangi
/// medyan indeksi), ölçülen büyüklük değil.</para>
/// </summary>
internal static class PerfMeasure
{
    /// <summary><paramref name="measure"/>'ı <paramref name="warmups"/> kez ısıtır, sonra her biri taze bir GC
    /// turunun ardından gelen <paramref name="samples"/> örnek döndürür (ham, sıralanmamış).</summary>
    public static IReadOnlyList<T> Sample<T>(Func<T> measure, int warmups, int samples)
    {
        ArgumentNullException.ThrowIfNull(measure);

        for (int i = 0; i < warmups; i++) measure();

        var results = new List<T>(samples);
        for (int i = 0; i < samples; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            results.Add(measure());
        }
        return results;
    }

    /// <summary>Medyan — çift örnek sayısında ÜST orta değer (<c>sorted[n/2]</c>). İki perf testi de bu indeksi
    /// kullanıyordu; ortaklaştırma sayıları DEĞİŞTİRMEZ.</summary>
    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.ToList();
        sorted.Sort();
        return sorted[sorted.Count / 2];
    }

    /// <summary>Tek skaler ölçüm için kısa yol: warmup + GC + medyan.</summary>
    public static double MedianOf(Func<double> measure, int warmups, int samples)
        => Median(Sample(measure, warmups, samples));
}
