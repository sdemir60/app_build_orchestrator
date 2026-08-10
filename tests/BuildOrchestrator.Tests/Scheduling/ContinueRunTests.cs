using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Scheduling;

namespace BuildOrchestrator.Tests.Scheduling;

// [T55] Continue: RunSnapshot ile aynı BuildPlan'dan resume — Completed yeniden derlenmez, Queued
// orijinal build-order'ın kalanı olarak dispatch edilir. Testler saf plan state üzerinde, I/O yok.
public class ContinueRunTests
{
    // ReadySetSchedulerTests'teki üretici ile aynı desen — minimal ProjectNode.
    private static ProjectNode N(string id, string[]? deps = null, int buildOrder = 0, bool inCycle = false) =>
        new(Id: id, Name: id, ProjectPath: id, SolutionNames: [], Dependencies: deps ?? [],
            BuildOrder: buildOrder, LayerIndex: null, LayerName: null, InCycle: inCycle, WillBuild: null);

    private static BuildPlan Plan(params ProjectNode[] nodes) =>
        new(nodes, Cycles: [], Configuration: "Debug");

    [Fact]
    public void snapshot_round_trip_preserves_completed_queued_and_elapsed()
    {
        var plan = Plan(N("A", buildOrder: 0), N("B", buildOrder: 1), N("C", buildOrder: 2));
        var sut = new ReadySetScheduler(plan);

        sut.TryDispatch(out var a);
        sut.Complete(a, BuildResult.Succeeded);

        var snapshot = sut.TakeSnapshot(elapsedMs: 4200);

        Assert.Equal(BuildResult.Succeeded, snapshot.Completed["A"]);
        Assert.Single(snapshot.Completed);
        Assert.Equal(["B", "C"], snapshot.Queued);
        Assert.Equal(4200, snapshot.ElapsedMs);

        var resumed = new ReadySetScheduler(plan, snapshot);

        Assert.Equal(BuildResult.Succeeded, resumed.Completed["A"]);
        Assert.Single(resumed.Completed);
        Assert.Equal(["B", "C"], resumed.QueuedProjectIds);
    }

    [Fact]
    public void continue_does_not_redispatch_completed_projects()
    {
        var plan = Plan(N("A", buildOrder: 0), N("B", buildOrder: 1));
        var sut = new ReadySetScheduler(plan);
        sut.TryDispatch(out var a);
        sut.Complete(a, BuildResult.Succeeded);
        var snapshot = sut.TakeSnapshot(0);

        var resumed = new ReadySetScheduler(plan, snapshot);

        Assert.True(resumed.TryDispatch(out var first));
        Assert.Equal("B", first); // A zaten Completed — asla tekrar dispatch edilmez
        resumed.Complete(first, BuildResult.Succeeded);

        Assert.False(resumed.TryDispatch(out var none));
        Assert.Null(none);
        Assert.True(resumed.IsDone);
    }

    [Fact]
    public void continue_dispatch_order_matches_remaining_build_order()
    {
        // Diamond: D → B,C → A. D "stop"tan önce tamamlandı; resume B,C,A sırasını (build-order'ın kalanı) izlemeli.
        var plan = Plan(
            N("D", buildOrder: 0),
            N("B", deps: ["D"], buildOrder: 1),
            N("C", deps: ["D"], buildOrder: 2),
            N("A", deps: ["B", "C"], buildOrder: 3));

        var sut = new ReadySetScheduler(plan);
        sut.TryDispatch(out var d);
        sut.Complete(d, BuildResult.Succeeded);
        var snapshot = sut.TakeSnapshot(1000);
        Assert.Equal(["B", "C", "A"], snapshot.Queued);

        var resumed = new ReadySetScheduler(plan, snapshot);

        Assert.True(resumed.TryDispatch(out var first));
        Assert.Equal("B", first);
        Assert.True(resumed.TryDispatch(out var second));
        Assert.Equal("C", second);
        Assert.False(resumed.TryDispatch(out _)); // A, B ve C bitmeden ready değil
        resumed.Complete(first, BuildResult.Succeeded);
        resumed.Complete(second, BuildResult.Succeeded);
        Assert.True(resumed.TryDispatch(out var third));
        Assert.Equal("A", third);
    }

    [Fact]
    public void empty_queued_snapshot_is_done_immediately()
    {
        var plan = Plan(N("A", buildOrder: 0));
        var completed = new Dictionary<string, BuildResult>(StringComparer.OrdinalIgnoreCase) { ["A"] = BuildResult.Succeeded };
        var snapshot = new RunSnapshot(completed, Queued: [], ElapsedMs: 999);

        var resumed = new ReadySetScheduler(plan, snapshot);

        Assert.True(resumed.IsDone);
        Assert.False(resumed.TryDispatch(out _));
        Assert.Empty(resumed.QueuedProjectIds);
    }

    [Fact]
    public void take_snapshot_mid_run_keeps_in_flight_project_in_queued_not_lost()
    {
        // A dispatch edildi ama henüz Complete çağrılmadı (in-flight) — snapshot alındığında A ne
        // Completed'ta ne de "hiçbir yerde" olmamalı: interrupted iş Queued'a düşer, kaybolmaz.
        var plan = Plan(N("A", buildOrder: 0), N("B", buildOrder: 1));
        var sut = new ReadySetScheduler(plan);
        sut.TryDispatch(out _);

        var snapshot = sut.TakeSnapshot(500);

        Assert.DoesNotContain("A", snapshot.Completed.Keys);
        Assert.Equal(["A", "B"], snapshot.Queued);
    }

    [Fact]
    public void resume_preserves_cycle_pre_skip_completed_state_without_re_adding_to_pre_skipped()
    {
        var plan = Plan(
            N("X", deps: ["Y"], buildOrder: 0, inCycle: true),
            N("Y", deps: ["X"], buildOrder: 1, inCycle: true),
            N("Z", deps: ["X"], buildOrder: 2));

        var sut = new ReadySetScheduler(plan); // X,Y construction anında Skipped/pre-skipped
        var snapshot = sut.TakeSnapshot(0);

        Assert.Equal(BuildResult.Skipped, snapshot.Completed["X"]);
        Assert.Equal(BuildResult.Skipped, snapshot.Completed["Y"]);
        Assert.Equal(["Z"], snapshot.Queued);

        var resumed = new ReadySetScheduler(plan, snapshot);

        // Snapshot zaten X/Y'yi Completed taşıyor — bu construction'da YENİDEN pre-skip edilmediler.
        Assert.Empty(resumed.PreSkipped);
        Assert.Equal(BuildResult.Skipped, resumed.Completed["X"]);
        Assert.Equal(BuildResult.Skipped, resumed.Completed["Y"]);
        Assert.True(resumed.TryDispatch(out var z));
        Assert.Equal("Z", z);
    }

    [Fact]
    public void resume_pre_skips_cycle_nodes_even_if_snapshot_omits_them()
    {
        // Savunmacı: snapshot her zaman TakeSnapshot'tan gelmeyebilir (Task 9 farklı bir yoldan kurabilir).
        // Cycle üyeleri Completed'ta yoksa dahi resume ctor onları pre-skip etmeli — aksi halde asla ready
        // olamayacakları için Z sonsuza dek bloklanır.
        var plan = Plan(
            N("X", deps: ["Y"], buildOrder: 0, inCycle: true),
            N("Y", deps: ["X"], buildOrder: 1, inCycle: true),
            N("Z", deps: ["X"], buildOrder: 2));

        var bareSnapshot = new RunSnapshot(
            Completed: new Dictionary<string, BuildResult>(StringComparer.OrdinalIgnoreCase),
            Queued: ["X", "Y", "Z"],
            ElapsedMs: 0);

        var resumed = new ReadySetScheduler(plan, bareSnapshot);

        Assert.Equal(2, resumed.PreSkipped.Count);
        Assert.Contains(("X", SkipReasons.InDependencyCycle), resumed.PreSkipped);
        Assert.Contains(("Y", SkipReasons.InDependencyCycle), resumed.PreSkipped);
        Assert.Equal(BuildResult.Skipped, resumed.Completed["X"]);
        Assert.True(resumed.TryDispatch(out var z));
        Assert.Equal("Z", z);
    }

    [Fact]
    public async Task take_snapshot_is_safe_under_concurrent_dispatch_and_never_loses_or_duplicates_a_project()
    {
        var nodes = Enumerable.Range(0, 30).Select(i => N($"P{i:D2}", buildOrder: i)).ToArray();
        var sut = new ReadySetScheduler(Plan(nodes));

        var workers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (sut.TryDispatch(out var id))
                sut.Complete(id, BuildResult.Succeeded);
        })).ToArray();

        var snapshotter = Task.Run(() =>
        {
            for (int i = 0; i < 50; i++)
            {
                var snap = sut.TakeSnapshot(i);
                // Her node tam olarak bir yerde: Completed veya Queued — asla ikisinde birden, asla hiçbirinde.
                Assert.Equal(30, snap.Completed.Count + snap.Queued.Count);
            }
        });

        await Task.WhenAll(workers.Append(snapshotter));
        Assert.True(sut.IsDone);
    }
}
