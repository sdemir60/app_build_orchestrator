using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Scheduling;

namespace BuildOrchestrator.Tests.Scheduling;

// [Task-13/T55] RetryPlanning: SAF snapshot dönüşümleri — retry (failed+dependents) kümesi ve Continue'da
// reason=stopped Failed'ların re-queue'u. I/O yok, saat yok — testler pure state üzerinde [D8].
public class RetryFailedTests
{
    // ContinueRunTests'teki üretici ile aynı desen — minimal ProjectNode.
    private static ProjectNode N(string id, string[]? deps = null, int buildOrder = 0, bool inCycle = false) =>
        new(Id: id, Name: id, ProjectPath: id, SolutionNames: [], Dependencies: deps ?? [],
            BuildOrder: buildOrder, LayerIndex: null, LayerName: null, InCycle: inCycle, WillBuild: null);

    private static BuildPlan Plan(params ProjectNode[] nodes) =>
        new(nodes, Cycles: [], Configuration: "Debug");

    private static Dictionary<string, BuildResult> Completed(params (string Id, BuildResult Result)[] entries) =>
        new(entries.ToDictionary(e => e.Id, e => e.Result), StringComparer.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- RequeueFailedAndDependents (retry set)

    [Fact]
    public void retry_set_is_failed_projects_plus_their_transitive_dependents_excluding_succeeded_and_skipped()
    {
        // F1 fail etti; D1 F1'e, D2 D1'e bağımlı (transitive dependent) — "hata derlemeyi öldürmez" (A3) sayesinde
        // ikisi de bu run içinde build edilmiş (Succeeded) ama F1 failed olduğu için retry kümesine girmeliler.
        // S bağımsız ve succeeded — retry kümesine GİRMEMELİ.
        var plan = Plan(
            N("F1", buildOrder: 0),
            N("D1", deps: ["F1"], buildOrder: 1),
            N("D2", deps: ["D1"], buildOrder: 2),
            N("S", buildOrder: 3));
        var snapshot = new RunSnapshot(
            Completed(("F1", BuildResult.Failed), ("D1", BuildResult.Succeeded), ("D2", BuildResult.Succeeded), ("S", BuildResult.Succeeded)),
            Queued: [],
            ElapsedMs: 777);

        var transformed = RetryPlanning.RequeueFailedAndDependents(plan, snapshot);

        Assert.False(transformed.Completed.ContainsKey("F1"));
        Assert.False(transformed.Completed.ContainsKey("D1"));
        Assert.False(transformed.Completed.ContainsKey("D2"));
        Assert.True(transformed.Completed.ContainsKey("S"));
        Assert.Equal(BuildResult.Succeeded, transformed.Completed["S"]);
        Assert.Equal(777, transformed.ElapsedMs); // elapsed asla sıfırlanmaz

        var resumed = new ReadySetScheduler(plan, transformed);

        Assert.True(resumed.TryDispatch(out var first));
        Assert.Equal("F1", first); // build-order'da en önde, retry kümesinin kökü
        resumed.Complete(first, BuildResult.Succeeded);
        Assert.True(resumed.TryDispatch(out var second));
        Assert.Equal("D1", second);
        resumed.Complete(second, BuildResult.Succeeded);
        Assert.True(resumed.TryDispatch(out var third));
        Assert.Equal("D2", third);
        resumed.Complete(third, BuildResult.Succeeded);
        Assert.False(resumed.TryDispatch(out _)); // S zaten Completed — asla yeniden dispatch edilmez
        Assert.True(resumed.IsDone);
    }

    [Fact]
    public void retry_set_is_unchanged_when_there_are_no_failed_projects()
    {
        var plan = Plan(N("A", buildOrder: 0), N("B", buildOrder: 1));
        var snapshot = new RunSnapshot(
            Completed(("A", BuildResult.Succeeded), ("B", BuildResult.Succeeded)), Queued: [], ElapsedMs: 10);

        var transformed = RetryPlanning.RequeueFailedAndDependents(plan, snapshot);

        Assert.Equal(snapshot.Completed.Keys.OrderBy(k => k), transformed.Completed.Keys.OrderBy(k => k));
        Assert.Empty(transformed.Queued);
    }

    [Fact]
    public void retry_set_does_not_touch_a_failed_project_s_sibling_that_shares_no_dependency_edge()
    {
        // F failed; U (unrelated, hiçbir bağımlılığı F ile kesişmiyor) succeeded — retry kümesine girmemeli.
        var plan = Plan(N("F", buildOrder: 0), N("U", buildOrder: 1));
        var snapshot = new RunSnapshot(
            Completed(("F", BuildResult.Failed), ("U", BuildResult.Succeeded)), Queued: [], ElapsedMs: 0);

        var transformed = RetryPlanning.RequeueFailedAndDependents(plan, snapshot);

        Assert.False(transformed.Completed.ContainsKey("F"));
        Assert.True(transformed.Completed.ContainsKey("U"));
        Assert.Equal(["F"], transformed.Queued);
    }

    [Fact]
    public void Retry_requeues_a_dependent_that_appears_before_its_failed_dependency_in_plan_order()
    {
        // [A1/T15] plan.Nodes TOPOLOJİK OLMAYABİLİR: LayerEngine'ın sert faz bariyeri bir projeyi kendi
        // bağımlılığından ÖNCE koyabilir (warn-only, kasıtlı). Down (dependent) dizide Up'tan (failed
        // dependency) ÖNCE geliyor; retry kümesi buna rağmen Down'ı içermeli — aksi halde Down, torn/eski
        // Up çıktısına karşı derlenmiş hâliyle Completed'ta kalırdı.
        var plan = Plan(
            N(@"C:\r\Down.csproj", deps: [@"C:\r\Up.csproj"], buildOrder: 0),
            N(@"C:\r\Up.csproj", buildOrder: 1));
        var snapshot = new RunSnapshot(
            Completed((@"C:\r\Up.csproj", BuildResult.Failed), (@"C:\r\Down.csproj", BuildResult.Succeeded)),
            Queued: [], ElapsedMs: 0);

        var transformed = RetryPlanning.RequeueFailedAndDependents(plan, snapshot);

        Assert.Contains(@"C:\r\Down.csproj", transformed.Queued);
        Assert.False(transformed.Completed.ContainsKey(@"C:\r\Down.csproj"));
    }

    [Fact]
    public void Retry_requeues_the_whole_scc_and_its_downstream_when_any_member_is_affected()
    {
        // [A3] SCC={A,B} (A→B, B→A) + D: cycle DIŞINDA, A'ya bağımlı. Sabit-nokta döngüsü DÖNGÜSEL bir kenar
        // kümesinde sonsuza girmeden kapanmalı ve kapanış SCC'nin TAMAMINI + downstream'ini içermeli: A
        // etkilenmişse B (A'ya bağımlı) ve D (A'ya bağımlı) de etkilenmiştir ⇒ {A, B, D}. PİN testi: A1'in
        // sabit-nokta kapanışı bunu zaten sağlıyor, burada SCC üzerinde bir daha bozulmasın diye sabitlenir.
        var plan = new BuildPlan(
            [N("A", deps: ["B"], buildOrder: 0, inCycle: true),
             N("B", deps: ["A"], buildOrder: 1, inCycle: true),
             N("D", deps: ["A"], buildOrder: 2)],
            Cycles: [["A", "B"]], Configuration: "Debug");
        var snapshot = new RunSnapshot(
            Completed(("A", BuildResult.Failed), ("B", BuildResult.Succeeded), ("D", BuildResult.Succeeded)),
            Queued: [], ElapsedMs: 0);

        var transformed = RetryPlanning.RequeueFailedAndDependents(plan, snapshot);

        Assert.Empty(transformed.Completed);
        Assert.Equal(["A", "B", "D"], transformed.Queued.OrderBy(id => id, StringComparer.Ordinal));
    }

    // ---------------------------------------------------------------- RequeueStoppedFailed (Continue re-queue)

    [Fact]
    public void reason_stopped_failed_projects_are_requeued_but_other_failure_reasons_are_left_alone()
    {
        var plan = Plan(N("A", buildOrder: 0), N("B", buildOrder: 1));
        var snapshot = new RunSnapshot(
            Completed(("A", BuildResult.Failed), ("B", BuildResult.Failed)), Queued: [], ElapsedMs: 555);
        var stoppedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A" }; // yalnız A reason=stopped

        var transformed = RetryPlanning.RequeueStoppedFailed(snapshot, stoppedIds);

        Assert.False(transformed.Completed.ContainsKey("A")); // torn-DLL guard: yeniden Queued
        Assert.True(transformed.Completed.ContainsKey("B"));  // farklı reason (ör. "exit 1") — Failed kalır
        Assert.Equal(BuildResult.Failed, transformed.Completed["B"]);
        Assert.Equal(["A"], transformed.Queued);
        Assert.Equal(555, transformed.ElapsedMs);

        var resumed = new ReadySetScheduler(plan, transformed);
        Assert.True(resumed.TryDispatch(out var first));
        Assert.Equal("A", first); // yeniden derlenir
        resumed.Complete(first, BuildResult.Succeeded);
        Assert.False(resumed.TryDispatch(out _)); // B asla yeniden dispatch edilmez (Failed olarak Completed'ta kalır)
        Assert.True(resumed.IsDone);
    }

    [Fact]
    public void requeue_stopped_failed_is_a_no_op_when_the_set_is_empty()
    {
        var plan = Plan(N("A", buildOrder: 0));
        var snapshot = new RunSnapshot(Completed(("A", BuildResult.Failed)), Queued: [], ElapsedMs: 1);

        var transformed = RetryPlanning.RequeueStoppedFailed(snapshot, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Same(snapshot, transformed);
    }

    [Fact]
    public void requeue_stopped_failed_ignores_an_id_that_is_no_longer_failed_in_the_snapshot()
    {
        // Savunmacı: stoppedFailedIds'te bir id olsa da snapshot'ta artık Failed değilse (ör. başka bir yoldan
        // zaten Succeeded'a dönmüşse) dokunulmaz.
        var plan = Plan(N("A", buildOrder: 0));
        var snapshot = new RunSnapshot(Completed(("A", BuildResult.Succeeded)), Queued: [], ElapsedMs: 1);

        var transformed = RetryPlanning.RequeueStoppedFailed(snapshot, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "A" });

        Assert.Same(snapshot, transformed);
    }
}
