using System.Runtime.InteropServices;

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

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentThread();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryThreadCycleTime(nint thread, out ulong cycles);

    /// <summary>[B2 fix 3 · madde 14] Çağıran thread'in o ana dek harcadığı CPU döngüsü (<c>QueryThreadCycleTime</c>) — UI thread
    /// ölçümlerinin TEK sayacı (<see cref="HiddenSurfaceMeasurementTests"/>, <see cref="ConsolePageEndMeasurementTests"/>). Çağrı başarısız
    /// olursa 0 basmak yerine fırlatır: sessiz 0, okumayı sahte bir "sıfır maliyet"e çevirirdi.</summary>
    public static ulong ThreadCycles()
    {
        if (!QueryThreadCycleTime(GetCurrentThread(), out ulong cycles))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return cycles;
    }

    /// <summary>[B2 fix 3 · madde 14] <see cref="ThreadCycles"/>'ın milisaniyedeki döngüsü — CPU zamanına çevirmenin TEK kalibrasyonu: thread
    /// bir süre kesintisiz döner ve döngü sayacının o süredeki artışı okunur; üç denemenin en büyüğü alınır (bir kesinti oranı yalnız
    /// düşürür). Sayaç sabit hızlıdır: sonuç işlemcinin anma frekansıdır (ms başına milyon döngü ≈ GHz).</summary>
    public static double CyclesPerMs()
    {
        double best = 0;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            ulong start = ThreadCycles();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 200) { }
            best = Math.Max(best, (ThreadCycles() - start) / clock.Elapsed.TotalMilliseconds);
        }
        return best;
    }

    /// <summary>[idle-memory · dalga 2 · fix 1] Eylemin TEST THREAD'İNDE ayırdığı bayt (<c>GC.GetAllocatedBytesForCurrentThread</c> —
    /// süreç geneli değil: paralel koşan başka bir testin ayırması sayıya girmez). Ayırma pinlerinin TEK ölçüm yeri (konsol görünümünün
    /// kopya pinleri ve kompakt konsolun sıcak yol pinleri). Ölçülecek eylemi kuran lambda çağrıdan ÖNCE yaratılır: kendi kapanışı
    /// sayılmaz.</summary>
    public static long AllocatedBy(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
