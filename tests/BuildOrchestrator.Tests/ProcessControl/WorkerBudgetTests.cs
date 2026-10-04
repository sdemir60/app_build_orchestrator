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
/// beklenen değerler ona göre yeniden hesaplandı. Bellek kuralı güvenlik ağı olarak planın değerleriyle KALDI (işçi
/// başına ~2 GB commit, makineye 2 GB pay; ölçümdeki Rebuild çoğunlukla güncellik denetimi olduğundan commit artışı
/// bunun çok altında kaldı — kural düşük bellekli makinede devreye girer). Eşik gevşetilmedi; ölçüme uydu.</para>
/// </summary>
public class WorkerBudgetTests
{
    private const long Gb = 1024L * 1024 * 1024;

    [Theory]
    [InlineData(4, 2, 16)]   // Balanced, iki işlemci: plan hipotezi 1'e kırpıyordu; ölçümde 2x kazandırdığı için KIRPILMAZ
    [InlineData(6, 4, 32)]   // Full, dört işlemci
    [InlineData(2, 1, 16)]   // Light hiçbir makinede kırpılmaz: tek işlemcide bile iki işçi = çarpanın kendisi
    [InlineData(6, 3, 64)]   // sınır: tam çarpan kadar işçi hâlâ serbest
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
    [InlineData(4, 8, 5, 1, "5 GB free memory")]
    [InlineData(6, 8, 7, 2, "7 GB free memory")]
    [InlineData(6, 64, 3, 1, "3 GB free memory")]   // rezervden sonra bir işçilik yer yok → yine de 1 (hiç 0 olmaz)
    [InlineData(6, 64, 2, 1, "2 GB free memory")]
    public void Free_memory_caps_the_request_after_a_reserve_is_left_to_the_machine(
        int requested, int cores, int freeGb, int expectedWorkers, string expectedReason)
    {
        var decision = WorkerBudget.Clamp(requested, cores, freeGb * Gb);

        Assert.Equal(expectedWorkers, decision.Workers);
        Assert.Equal(expectedReason, decision.Reason);
    }

    [Fact]
    public void When_both_limits_bind_equally_the_memory_is_named()
    {
        // Bellek genelde ilk sınırdır (ARCHITECTURE §11.1) ve kullanıcının elinde çevirebileceği tek şey odur.
        Assert.Equal(new WorkerBudgetDecision(4, "10 GB free memory"), WorkerBudget.Clamp(6, 2, 10 * Gb));
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
}
