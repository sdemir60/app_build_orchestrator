using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.Planning;

/// <summary>
/// "Bu koşu defteri dinler mi" kararının TEK kaynağı: incremental koşular (Build, Cycles) güncel olanı atlar ve kökünü
/// bekleyeni sırası gelince koşullu değerlendirir; Rebuild önbelleği yok sayar, Clean hiç derlemez. Koordinatörün güncel
/// tohumu ve <c>ConditionalRebuild</c>'in mod kuralı buradan okur (kopya YASAK).
/// <para>[final inceleme O-1] Kural eskiden iki yerde ayrı ayrı yazılıydı: <c>ConditionalRebuild</c>'in özel metodu ve
/// koordinatörün <c>cmd.Mode == RunMode.Build || cyclesRun</c> koşulu.</para>
/// </summary>
public class IncrementalModesTests
{
    [Theory]
    [InlineData(RunMode.Build, true)]
    [InlineData(RunMode.Cycles, true)]
    [InlineData(RunMode.Rebuild, false)]
    [InlineData(RunMode.Clean, false)]
    public void Build_and_cycles_runs_follow_the_ledger_rebuild_and_clean_do_not(RunMode mode, bool expected)
        => Assert.Equal(expected, IncrementalModes.Includes(mode));
}
