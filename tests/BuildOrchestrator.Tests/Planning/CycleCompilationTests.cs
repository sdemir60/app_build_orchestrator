using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
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

    private static ProjectNode N(string id, bool inCycle = false) =>
        new(id, id, id, SolutionNames: [], Dependencies: [], BuildOrder: 0, LayerIndex: null, LayerName: null,
            InCycle: inCycle, WillBuild: true);

    /// <summary>[ara inceleme I2] Grup haritası yalnız SCC derleyen bir modun TAM koşusunda ve döngülü planda vardır;
    /// koordinatör ve Sync haritayı buradan alır.</summary>
    [Fact]
    public void Groups_exist_only_for_a_full_run_of_a_compiling_mode_over_a_plan_with_cycles()
    {
        var plan = new BuildPlan([N("A", inCycle: true), N("B", inCycle: true), N("C")], Cycles: [["A", "B"]],
            Configuration: "Debug");

        Assert.Equal(["A", "B"], CycleCompilation.GroupsFor(plan, RunMode.Build, scopedRun: false)!.MembersOf("A"));
        Assert.NotNull(CycleCompilation.GroupsFor(plan, RunMode.Rebuild, scopedRun: false));
        Assert.NotNull(CycleCompilation.GroupsFor(plan, RunMode.Cycles, scopedRun: false));
        Assert.Null(CycleCompilation.GroupsFor(plan, RunMode.Clean, scopedRun: false));
        Assert.Null(CycleCompilation.GroupsFor(plan, RunMode.Build, scopedRun: true));
        Assert.Null(CycleCompilation.GroupsFor(plan with { Cycles = [] }, RunMode.Build, scopedRun: false));
    }
}
