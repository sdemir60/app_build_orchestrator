using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Scheduling;

namespace BuildOrchestrator.Tests.Scheduling;

// [K2] ReadySetScheduler: sıra-koruyan ready-set, ileri atlamalı. Testler saf plan state üzerinde,
// I/O yok — plan doğrudan bu dosyada, TopoSort'tan bağımsız kuruluyor (Nodes zaten build-order kabulü).
public class ReadySetSchedulerTests
{
    // Minimal ProjectNode üretici: yalnız scheduler'ın önemsediği alanlar (Id, Dependencies, BuildOrder, InCycle) dolduruluyor.
    private static ProjectNode N(string id, string[]? deps = null, int buildOrder = 0, bool inCycle = false) =>
        new(Id: id, Name: id, ProjectPath: id, SolutionNames: [], Dependencies: deps ?? [],
            BuildOrder: buildOrder, LayerIndex: null, LayerName: null, InCycle: inCycle, WillBuild: null);

    private static BuildPlan Plan(params ProjectNode[] nodes) =>
        new(nodes, Cycles: [], Configuration: "Debug");

    [Fact]
    public void forward_skip_dispatches_ready_node_past_blocked_earlier_one()
    {
        // A (build-order 0) "Dep"e bağımlı ve Dep henüz bitmemiş ⇒ bloklu. B (build-order 1) bağımlılıksız ⇒ ready.
        // Nodes dizisinde A, B'den önce ama TryDispatch B'yi vermeli (A'nın üzerinden atlanır).
        var plan = Plan(
            N("A", deps: ["Dep"], buildOrder: 0),
            N("B", buildOrder: 1),
            N("Dep", buildOrder: 2));
        var sut = new ReadySetScheduler(plan);

        Assert.True(sut.TryDispatch(out var first));
        Assert.Equal("B", first);
    }

    [Fact]
    public void determinism_same_plan_and_completion_order_yields_identical_dispatch_sequence()
    {
        // Diamond: A→B, A→C, B→D, C→D (A, B'ye ve C'ye bağımlı; B ve C, D'ye bağımlı).
        ProjectNode[] Nodes() =>
        [
            N("D", buildOrder: 0),
            N("B", deps: ["D"], buildOrder: 1),
            N("C", deps: ["D"], buildOrder: 2),
            N("A", deps: ["B", "C"], buildOrder: 3),
        ];

        List<string> RunOnce()
        {
            var sut = new ReadySetScheduler(Plan(Nodes()));
            var sequence = new List<string>();
            while (sut.TryDispatch(out var id))
            {
                sequence.Add(id);
                sut.Complete(id, BuildResult.Succeeded); // her dispatch'i hemen tamamla, aynı sırayla
            }
            return sequence;
        }

        var baseline = RunOnce();
        for (int i = 0; i < 4; i++)
            Assert.Equal(baseline, RunOnce());
    }

    [Fact]
    public void diamond_dependency_dispatches_in_correct_order()
    {
        var plan = Plan(
            N("D", buildOrder: 0),
            N("B", deps: ["D"], buildOrder: 1),
            N("C", deps: ["D"], buildOrder: 2),
            N("A", deps: ["B", "C"], buildOrder: 3));
        var sut = new ReadySetScheduler(plan);

        Assert.True(sut.TryDispatch(out var d));
        Assert.Equal("D", d);
        Assert.False(sut.TryDispatch(out _)); // B ve C, D bitmeden ready değil
        sut.Complete("D", BuildResult.Succeeded);

        Assert.True(sut.TryDispatch(out var b));
        Assert.Equal("B", b);
        Assert.True(sut.TryDispatch(out var c));
        Assert.Equal("C", c);
        Assert.False(sut.TryDispatch(out _)); // A, B ve C bitmeden ready değil
        sut.Complete("B", BuildResult.Succeeded);
        Assert.False(sut.TryDispatch(out _)); // C hâlâ in-flight
        sut.Complete("C", BuildResult.Succeeded);

        Assert.True(sut.TryDispatch(out var a));
        Assert.Equal("A", a);
        Assert.False(sut.TryDispatch(out _));
    }

    [Fact]
    public void failed_dependency_unblocks_its_dependent()
    {
        var plan = Plan(N("Dep", buildOrder: 0), N("A", deps: ["Dep"], buildOrder: 1));
        var sut = new ReadySetScheduler(plan);

        Assert.True(sut.TryDispatch(out var dep));
        Assert.Equal("Dep", dep);
        Assert.False(sut.TryDispatch(out _)); // A, Dep bitmeden ready değil

        sut.Complete("Dep", BuildResult.Failed); // hata derlemeyi öldürmez [A3]

        Assert.True(sut.TryDispatch(out var a));
        Assert.Equal("A", a);
    }

    [Fact]
    public void cycle_members_are_pre_skipped_and_their_dependents_still_run()
    {
        // X, Y birbirine bağımlı (SCC) ⇒ InCycle=true. Z, X'e bağımlı.
        var plan = Plan(
            N("X", deps: ["Y"], buildOrder: 0, inCycle: true),
            N("Y", deps: ["X"], buildOrder: 1, inCycle: true),
            N("Z", deps: ["X"], buildOrder: 2));
        var sut = new ReadySetScheduler(plan);

        Assert.Equal(2, sut.PreSkipped.Count);
        Assert.Contains(("X", "in dependency cycle"), sut.PreSkipped);
        Assert.Contains(("Y", "in dependency cycle"), sut.PreSkipped);
        Assert.Equal(BuildResult.Skipped, sut.Completed["X"]);
        Assert.Equal(BuildResult.Skipped, sut.Completed["Y"]);

        // Z'nin tek bağımlılığı X, cycle nedeniyle zaten "çözülmüş" sayılır ⇒ Z doğrudan ready.
        Assert.True(sut.TryDispatch(out var z));
        Assert.Equal("Z", z);
        Assert.False(sut.TryDispatch(out _)); // başka dispatch edilecek yok
    }

    [Fact]
    public void try_dispatch_never_returns_the_same_project_twice_and_tracks_in_flight()
    {
        var plan = Plan(N("A", buildOrder: 0), N("B", buildOrder: 1));
        var sut = new ReadySetScheduler(plan);

        Assert.Equal(0, sut.InFlight);
        Assert.True(sut.TryDispatch(out var a));
        Assert.Equal("A", a);
        Assert.Equal(1, sut.InFlight);
        Assert.True(sut.TryDispatch(out var b));
        Assert.Equal("B", b);
        Assert.Equal(2, sut.InFlight);

        Assert.False(sut.TryDispatch(out var none)); // ikisi de zaten dispatch edildi
        Assert.Null(none);

        sut.Complete("A", BuildResult.Succeeded);
        Assert.Equal(1, sut.InFlight);
        Assert.False(sut.TryDispatch(out _)); // A tamamlandı ama tekrar dispatch edilmez
    }

    [Fact]
    public void request_stop_blocks_further_dispatch_and_queued_lists_never_dispatched_nodes()
    {
        var plan = Plan(N("A", buildOrder: 0), N("B", buildOrder: 1), N("C", buildOrder: 2));
        var sut = new ReadySetScheduler(plan);

        Assert.True(sut.TryDispatch(out var a));
        Assert.Equal("A", a);

        sut.RequestStop();

        Assert.False(sut.TryDispatch(out var none));
        Assert.Null(none);
        Assert.Equal(["B", "C"], sut.QueuedProjectIds);
        Assert.False(sut.IsDone); // A hâlâ in-flight

        sut.Complete("A", BuildResult.Succeeded);
        Assert.True(sut.IsDone);
    }

    [Fact]
    public void is_done_true_once_all_nodes_resolved_without_stop()
    {
        var plan = Plan(N("A", buildOrder: 0), N("B", buildOrder: 1));
        var sut = new ReadySetScheduler(plan);

        Assert.False(sut.IsDone);
        sut.TryDispatch(out var a);
        sut.TryDispatch(out var b);
        Assert.False(sut.IsDone);
        sut.Complete(a, BuildResult.Succeeded);
        Assert.False(sut.IsDone); // B hâlâ in-flight
        sut.Complete(b, BuildResult.Succeeded);
        Assert.True(sut.IsDone);
        Assert.Empty(sut.QueuedProjectIds);
    }

    [Fact]
    public void dangling_dependency_id_not_present_in_plan_does_not_strand_the_node()
    {
        // "Ghost" hiçbir node'un Id'si değil; A onu bağımlılık olarak listeliyor ama plan'da yok.
        // Savunma: bilinmeyen bağımlılık node'u sonsuza dek bloklamamalı.
        var plan = Plan(N("A", deps: ["Ghost"], buildOrder: 0));
        var sut = new ReadySetScheduler(plan);

        Assert.True(sut.TryDispatch(out var a));
        Assert.Equal("A", a);
    }

    [Fact]
    public void is_done_becomes_true_when_a_remaining_node_can_never_become_ready_self_loop_safety()
    {
        // [Task 18] X kendine bağımlı VE InCycle=false (sentetik/bozuk bir durum — normalde TopoSort böyle bir
        // düğümü InCycle işaretler ve construction'da pre-skip ederdi; burada bilerek o güvenceyi ATLAYARAK
        // "hiçbir zaman ready olamayacak bir queued düğüm" senaryosu kuruluyor). Eski "queued boş mu"
        // formülasyonu bu düğüm hiç dispatch/complete edilemeyeceği için IsDone'ı SONSUZA dek false döndürürdü
        // (worker'lar WakeSignal'da parklı kalır, run askıda kalırdı). Yeni formülasyon "ready olabilecek bir
        // şey var mı" sorduğu için X asla ready olamayacağından run terminal sayılmalı.
        var plan = Plan(N("X", deps: ["X"], buildOrder: 0));
        var sut = new ReadySetScheduler(plan);

        Assert.False(sut.TryDispatch(out var none)); // X asla ready değil (kendi bağımlılığı kendisi, hiç complete edilmedi)
        Assert.Null(none);
        Assert.Contains("X", sut.QueuedProjectIds); // hâlâ "queued" (hiç dispatch edilmedi) — eski formülasyon burada takılırdı
        Assert.True(sut.IsDone); // ama artık terminal: inFlight=0 ve kalan tek düğüm asla ready olamaz
    }

    private static ProjectNode CycleNode(string id, int order, params string[] deps) =>
        new(id, id, id, [], deps, order, null, null, true, null);

    // Gruplar verilince pre-skip YAPILMAZ — üyeler gerçekten dispatch edilir.
    [Fact]
    public void group_members_are_not_pre_skipped_when_groups_supplied()
    {
        var plan = new BuildPlan(
            [CycleNode("b", 0, "a"), CycleNode("a", 1, "b")], [new[] { "a", "b" }], "Debug");

        var scheduler = new ReadySetScheduler(plan, CycleGroups.From(plan));

        Assert.Empty(scheduler.PreSkipped);
        Assert.True(scheduler.TryDispatch(out string id));
        Assert.Equal("b", id);   // build-order lideri
    }

    // Grup TEK kalem: bir üye dispatch edilince diğerleri de in-flight olur, ikinci worker onları kapamaz.
    [Fact]
    public void dispatching_group_marks_all_members_in_flight()
    {
        var plan = new BuildPlan(
            [CycleNode("b", 0, "a"), CycleNode("a", 1, "b")], [new[] { "a", "b" }], "Debug");

        var scheduler = new ReadySetScheduler(plan, CycleGroups.From(plan));

        Assert.True(scheduler.TryDispatch(out _));
        Assert.Equal(2, scheduler.InFlight);
        Assert.False(scheduler.TryDispatch(out _));   // ikinci worker'a verilecek iş yok
    }

    // Canlılık: grup dispatch edilir, TryDispatch'in hiç DÖNMEDİĞİ üyeler dahil TÜM üyeler Complete edilir
    // (round döngüsünün — Task 6, bu class'ın kapsamı dışı — yapması gereken tam olarak budur) ⇒ run biter.
    [Fact]
    public void completing_every_group_member_including_ones_never_returned_finishes_the_run()
    {
        var plan = new BuildPlan(
            [CycleNode("b", 0, "a"), CycleNode("a", 1, "b")], [new[] { "a", "b" }], "Debug");

        var scheduler = new ReadySetScheduler(plan, CycleGroups.From(plan));
        var groups = CycleGroups.From(plan);

        Assert.True(scheduler.TryDispatch(out string head)); // yalnız "b" (lider) döner
        Assert.Equal("b", head);
        Assert.Equal(2, scheduler.InFlight);                 // ama "a" da in-flight'a girdi

        foreach (string member in groups.MembersOf(head))     // "b" VE TryDispatch'in hiç vermediği "a"
            scheduler.Complete(member, BuildResult.Succeeded);

        Assert.Equal(0, scheduler.InFlight);
        Assert.True(scheduler.IsDone);
        Assert.Empty(scheduler.QueuedProjectIds);
    }

    // Grup, DIŞ bağımlılığı terminal olmadan dispatch EDİLMEZ; grup-içi kenarlar hazırlığı bloklamaz.
    [Fact]
    public void group_waits_for_external_dependency_only()
    {
        var plan = new BuildPlan(
            [
                new ProjectNode("x", "x", "x", [], [], 0, null, null, false, null),
                CycleNode("b", 1, "a", "x"),
                CycleNode("a", 2, "b"),
            ],
            [new[] { "a", "b" }], "Debug");

        var scheduler = new ReadySetScheduler(plan, CycleGroups.From(plan));

        Assert.True(scheduler.TryDispatch(out string first));
        Assert.Equal("x", first);
        Assert.False(scheduler.TryDispatch(out _));      // grup henüz hazır değil
        scheduler.Complete("x", BuildResult.Succeeded);
        Assert.True(scheduler.TryDispatch(out string second));
        Assert.Equal("b", second);
    }

    // Gruplar VERİLMEZSE (kill switch kapalı) bugünkü davranış birebir korunur.
    [Fact]
    public void without_groups_cycle_members_are_still_pre_skipped()
    {
        var plan = new BuildPlan(
            [CycleNode("b", 0, "a"), CycleNode("a", 1, "b")], [new[] { "a", "b" }], "Debug");

        var scheduler = new ReadySetScheduler(plan);

        Assert.Equal(2, scheduler.PreSkipped.Count);
        Assert.All(scheduler.PreSkipped, p => Assert.Equal("in dependency cycle", p.Reason));
    }

    [Fact]
    public async Task concurrent_try_dispatch_from_many_workers_never_double_dispatches()
    {
        // Task 9, N paralel worker'dan TryDispatch/Complete çağıracak. 50 bağımsız node, 8 worker aynı anda
        // yarışarak TryDispatch dener. Sleep-poll yok [D8]: worker'lar Task.WhenAll ile senkron bekleniyor.
        var nodes = Enumerable.Range(0, 50).Select(i => N($"P{i:D2}", buildOrder: i)).ToArray();
        var sut = new ReadySetScheduler(Plan(nodes));

        var dispatched = new System.Collections.Concurrent.ConcurrentBag<string>();
        var workers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            while (sut.TryDispatch(out var id))
            {
                dispatched.Add(id);
                sut.Complete(id, BuildResult.Succeeded);
            }
        })).ToArray();
        await Task.WhenAll(workers);

        Assert.Equal(50, dispatched.Count);
        Assert.Equal(50, dispatched.Distinct(StringComparer.OrdinalIgnoreCase).Count()); // hiçbiri iki kez değil
        Assert.True(sut.IsDone);
        Assert.Empty(sut.QueuedProjectIds);
    }
}
