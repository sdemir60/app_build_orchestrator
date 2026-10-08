using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.Planning;

/// <summary>
/// [Build cycle derler] "Bu koşu SCC (dependency cycle) üyelerini derler mi" kararının TEK kaynağı. Build ve
/// Rebuild de kirli grupları Cycles'ın tur mekanizmasıyla derler; Clean hiçbir şey derlemez. Sync'in önizlemesi,
/// Supervisor'ın planı, koordinatörün grup kapısı ve App'in tur muhasebesi hep buradan okur (kopya YASAK).
/// </summary>
public class CycleCompilationTests
{
    [Theory]
    [InlineData(RunMode.Build, true)]
    [InlineData(RunMode.Rebuild, true)]
    [InlineData(RunMode.Cycles, true)]
    [InlineData(RunMode.Clean, false)]
    public void Every_compiling_mode_compiles_dirty_cycle_groups_and_clean_does_not(RunMode mode, bool expected)
        => Assert.Equal(expected, CycleCompilation.CompilesCycles(mode));
}
