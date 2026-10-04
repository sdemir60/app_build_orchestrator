using System.Runtime.InteropServices;
using BuildOrchestrator.Core.ProcessControl;
using Xunit;

namespace BuildOrchestrator.Tests.ProcessControl;

/// <summary>
/// [PERF Faz D / karar 10] <c>WorkerBudget</c>: profilin istediği işçi sayısı makineye göre kırpılır.
///
/// <para><b>Plan hipotezi ve değişme gerekçesi:</b> ilk plan kuralı "mantıksal işlemci ≤ 2 ise 1 işçi, değilse işlemci − 1"
/// diye varsaydı (zayıf makinede ilk sorunun işlemci olduğu sanıldı) ve bu testin ilk hâli iki işlemcide tek işçi,
/// dört işlemcide üç işçi bekliyordu. D1 ölçümü (gerçek OSYS Rebuild, yakınlık maskesiyle 2 ve 4 mantıksal işlemci,
/// 1-4 işçi, iki tekrar) bunu çürüttü: iki işlemcide tek işçi iki işçinin neredeyse iki katı sürdü, dört işlemcide
/// dört işçi üçten hızlıydı — iş işlemci değil süreç/IO gecikmesi ağırlıklı, çekirdek sayısının üstündeki işçi hâlâ
/// kazandırıyor. Kural bu yüzden "işçi, mantıksal işlemcinin <c>WorkersPerCore</c> katını aşarsa kırpılır" oldu ve
/// beklenen değerler ona göre yeniden hesaplandı. Bellek kuralı da ölçüme göre DÜZELTİLDİ: plan ile ilk uygulama işçi
/// başına 2 GB (makineye 2 GB pay) varsaymıştı; D1'in Rebuild'i çoğunlukla güncellik denetimi olduğundan ölçülen commit
/// artışı bunun 10-20 kat altında kaldı ve varsayılan profil kırpmasız ~10 GB boş bellek isterdi. Gerçek derleme ayrıca
/// ölçüldü (d1b: dört işlemci, Clean → Build, tam csc): makine commit artışı (tepe − başlangıç) tek işçide 0,93 GB, iki
/// işçide 1,34 GB, dört işçide 1,71 GB — motor ile ilk işçi ~1 GB, her ek işçi ~0,2-0,4 GB. <c>BytesPerWorker</c> bu
/// yüzden 512 MB oldu (ölçülen ek işçi maliyetinin ~1,3-2,5 katı); <c>ReserveBytes</c> 2 GB AYNEN kaldı (motor + ilk
/// işçi tabanını ve makinenin geri kalanını karşılar). Değer ölçüme uydu; testi yeşile boyamak için gevşetilmedi:
/// beklenen sayılar yeni sabitlerle yeniden hesaplandı (varsayılan profil dört işçi 4 GB'ta, Full altı işçi 5 GB'ta
/// kırpılmaz; 3 GB'ta iki, 2,5 GB'ta bir işçi).</para>
/// </summary>
public class WorkerBudgetTests
{
    private const long Gb = 1024L * 1024 * 1024;
    private const long Mb = 1024L * 1024;

    [Theory]
    [InlineData(4, 2, 16)]   // Balanced, iki işlemci: plan hipotezi 1'e kırpıyordu; ölçümde 2x kazandırdığı için KIRPILMAZ
    [InlineData(6, 4, 32)]   // Full, dört işlemci
    [InlineData(2, 1, 16)]   // Light hiçbir makinede kırpılmaz: tek işlemcide bile iki işçi = çarpanın kendisi
    [InlineData(6, 3, 64)]   // sınır: tam çarpan kadar işçi hâlâ serbest
    [InlineData(4, 8, 4)]    // varsayılan profil (4 işçi), 4 GB boş: rezervden sonra 2 GB = tam dört işçilik bütçe → KIRPILMAZ
    [InlineData(6, 8, 5)]    // Full (6 işçi), 5 GB boş: rezervden sonra 3 GB = tam altı işçilik bütçe → KIRPILMAZ
    [InlineData(4, 8, 5)]    // bellek bütçenin üstünde → kırpma yok
    public void A_request_the_machine_can_carry_is_not_reduced(int requested, int cores, int freeGb)
    {
        var decision = WorkerBudget.Clamp(requested, cores, freeGb * Gb);

        Assert.Equal(requested, decision.Workers);
        Assert.Null(decision.Reason);
    }

    [Theory]
    [InlineData(6, 2, 16, 4, "2 logical processors")]
    [InlineData(4, 1, 16, 2, "1 logical processor")]
    [InlineData(6, 1, 16, 2, "1 logical processor")]
    [InlineData(100, 3, 1024, 6, "3 logical processors")]
    public void Cores_cap_the_request_at_a_multiple_of_the_logical_processors(
        int requested, int cores, int freeGb, int expectedWorkers, string expectedReason)
    {
        var decision = WorkerBudget.Clamp(requested, cores, freeGb * Gb);

        Assert.Equal(expectedWorkers, decision.Workers);
        Assert.Equal(expectedReason, decision.Reason);
    }

    [Theory]
    [InlineData(4, 8, 3 * 1024, 2, "3 GB free memory")]   // 3 GB boş: rezervden sonra 1 GB = iki işçi
    [InlineData(6, 8, 2560, 1, "2 GB free memory")]       // 2,5 GB boş: rezervden sonra yarım GB = bir işçi (gerekçe tam GB'a yuvarlanır)
    [InlineData(4, 8, 1024, 1, "1 GB free memory")]       // 1 GB boş: rezerv bile yok → yine de 1 (hiç 0 olmaz)
    [InlineData(6, 64, 4096, 4, "4 GB free memory")]      // 4 GB boş: rezervden sonra 2 GB = dört işçi (altı istenmişti)
    [InlineData(6, 64, 3584, 3, "3 GB free memory")]      // 3,5 GB boş: rezervden sonra 1,5 GB = üç işçi
    [InlineData(6, 64, 2048, 1, "2 GB free memory")]      // tam rezerv: bir işçilik yer yok → yine de 1
    public void Free_memory_caps_the_request_after_a_reserve_is_left_to_the_machine(
        int requested, int cores, int freeMb, int expectedWorkers, string expectedReason)
    {
        var decision = WorkerBudget.Clamp(requested, cores, freeMb * Mb);

        Assert.Equal(expectedWorkers, decision.Workers);
        Assert.Equal(expectedReason, decision.Reason);
    }

    [Fact]
    public void When_both_limits_bind_equally_the_memory_is_named()
    {
        // Bellek genelde ilk sınırdır (ARCHITECTURE §11.1) ve kullanıcının elinde çevirebileceği tek şey odur.
        // İki işlemci = dört işçi; rezervden sonra 2 GB de tam dört işçi (rezerv + 4 × BytesPerWorker = 4 GB).
        Assert.Equal(new WorkerBudgetDecision(4, "4 GB free memory"), WorkerBudget.Clamp(6, 2, 4 * Gb));
    }

    [Fact]
    public void The_memory_rule_changes_by_exactly_one_worker_per_BytesPerWorker()
    {
        long threeWorkers = WorkerBudget.ReserveBytes + 3 * WorkerBudget.BytesPerWorker;

        Assert.Equal(3, WorkerBudget.Clamp(6, 64, threeWorkers).Workers);
        Assert.Equal(2, WorkerBudget.Clamp(6, 64, threeWorkers - 1).Workers);
    }

    [Fact]
    public void The_core_limit_is_WorkersPerCore_times_the_logical_processors()
    {
        Assert.Equal(3 * WorkerBudget.WorkersPerCore, WorkerBudget.Clamp(1000, 3, 1024 * Gb).Workers);
    }

    [Fact]
    public void The_result_is_never_below_one_never_above_the_request_and_never_overflows()
    {
        foreach (int requested in new[] { -1, 0, 1, 4, 6 })
        {
            foreach (int cores in new[] { -1, 0, 1, 2, 64 })
            {
                foreach (long free in new[] { long.MinValue, -1L, 0L, WorkerBudget.ReserveBytes, 3 * Gb, 1024 * Gb, long.MaxValue })
                {
                    var decision = WorkerBudget.Clamp(requested, cores, free);

                    Assert.InRange(decision.Workers, 1, Math.Max(1, requested));
                    if (free <= WorkerBudget.ReserveBytes) Assert.Equal(1, decision.Workers);
                }
            }
        }

        // Çok bol bellek (long.MaxValue) int'e taşmaz: kırpma yok, istenen sayı aynen çıkar.
        Assert.Equal(4, WorkerBudget.Clamp(4, 8, long.MaxValue).Workers);
    }

    [Fact]
    public void The_reduction_note_names_the_actual_count_and_the_reason()
    {
        Assert.Equal("workers reduced to 1 (5 GB free memory)", PerfNoteText.WorkersReduced(1, "5 GB free memory"));
    }

    [Fact]
    public void Machine_resources_read_a_plausible_machine()
    {
        Assert.True(MachineResources.LogicalCores >= 1);

        long free = MachineResources.FreePhysicalBytes();
        Assert.InRange(free, 1, MachineResources.FreeBytesUnknown - 1);   // gerçek okuma: pozitif ve "bilinmiyor" işareti DEĞİL

        var snapshot = MachineResources.Snapshot();
        Assert.Equal(MachineResources.LogicalCores, snapshot.Cores);
        Assert.InRange(snapshot.FreeBytes, 1, MachineResources.FreeBytesUnknown - 1);
    }

    /// <summary>
    /// [PERF Faz D fix1 / M4] Doğru ALANIN okunduğunu pinler: yalnız "pozitif ve bilinmiyor değil" demek,
    /// <c>ullAvailPhys</c> yerine <c>ullTotalPhys</c> (ya da bir commit/sayfa dosyası alanı) okuyan bir regresyonu yeşil
    /// bırakırdı — bütçe sessizce bol belleğe bakardı. Bağımsız bir ham okumayla karşılaştırılır: boş fiziksel bellek toplam
    /// fiziksel belleğin KESİNLİKLE altındadır ve iki okuma arasında (mikrosaniyeler) toplamın sekizde birinden fazla
    /// oynamaz.
    /// </summary>
    [Fact]
    public void Free_bytes_are_the_available_physical_memory_not_another_field()
    {
        var raw = new NativeMethods.MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>() };
        Assert.True(NativeMethods.GlobalMemoryStatusEx(ref raw));
        long total = (long)raw.ullTotalPhys;
        long available = (long)raw.ullAvailPhys;

        long free = MachineResources.FreePhysicalBytes();

        Assert.InRange(free, 1, total - 1);                       // toplam okunsaydı free == total olurdu
        Assert.True(Math.Abs(free - available) < total / 8,       // commit/sayfa dosyası alanı okunsaydı uzaklaşırdı
            $"free={free}, ullAvailPhys={available}, ullTotalPhys={total}");
    }
}
