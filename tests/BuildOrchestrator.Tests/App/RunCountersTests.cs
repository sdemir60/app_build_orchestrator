using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [C2] <see cref="RunCounters.From"/>: satır durumlarından sayaç özeti. <c>DepAffected</c> yalnız
/// <b>succeeded</b> + dep-issue taşıyan satırları sayar (build-data.js:524-528) — chip/filtre "dep"inden
/// (herhangi statü) kasıtlı olarak FARKLIDIR.
/// </summary>
public class RunCountersTests
{
    private static ProjectRowViewModel Row(string name, ProjectRowState state, bool dep = false, bool stuck = false)
    {
        var r = new ProjectRowViewModel($@"C:\p\{name}.csproj", name, state);
        if (dep) r.DepIssues = ["X"];
        if (stuck) r.CycleUnconverged = true;
        return r;
    }

    [Fact]
    public void Dep_counter_counts_only_succeeded_rows_that_carry_a_dep_issue()
    {
        var rows = new[]
        {
            Row("A", ProjectRowState.Succeeded, dep: true),  // sayılır
            Row("B", ProjectRowState.Failed, dep: true),     // dep var AMA succeeded değil → sayılmaz
            Row("C", ProjectRowState.Succeeded, dep: false), // succeeded AMA dep yok → sayılmaz
        };

        var c = RunCounters.From(rows);

        Assert.Equal(1, c.DepAffected);
        Assert.Equal(2, c.Succeeded);
        Assert.Equal(1, c.Failed);
        Assert.Equal(3, c.Total);
    }

    [Fact]
    public void Every_row_state_is_tallied_into_its_own_bucket()
    {
        var rows = new[]
        {
            Row("A", ProjectRowState.Started),
            Row("B", ProjectRowState.Pending),
            Row("C", ProjectRowState.Pending),
            Row("D", ProjectRowState.Succeeded),
            Row("E", ProjectRowState.Failed),
            Row("F", ProjectRowState.Skipped),
        };

        var c = RunCounters.From(rows);

        Assert.Equal(6, c.Total);
        Assert.Equal(1, c.Building);   // Started
        Assert.Equal(2, c.Queued);     // Pending
        Assert.Equal(1, c.Succeeded);
        Assert.Equal(1, c.Failed);
        Assert.Equal(1, c.Skipped);
        Assert.Equal(0, c.DepAffected);
        Assert.Equal(0, c.StuckCycles);
    }

    // [cycle rounds/Task 8] StuckCycles: kalıcı kırık bir döngünün pre-skip'i sıradan "güncel" skip'ten
    // GÖRÜNMEZ AYNI görünürdü (ikisi de plain "skipped") — bu sayaç ayrımı satır durumundan TÜRETİR
    // (ProjectRowViewModel.CycleUnconverged), yığmaz.
    [Fact]
    public void Stuck_counter_counts_only_skipped_rows_flagged_cycle_unconverged()
    {
        var rows = new[]
        {
            Row("A", ProjectRowState.Skipped, stuck: true),   // sayılır
            Row("B", ProjectRowState.Skipped, stuck: false),  // güncel skip → sayılmaz
            Row("C", ProjectRowState.Failed, stuck: true),    // stuck AMA skipped değil → sayılmaz (savunmacı)
        };

        var c = RunCounters.From(rows);

        Assert.Equal(1, c.StuckCycles);
        Assert.Equal(2, c.Skipped);
        Assert.Equal(3, c.Total);
    }
}
