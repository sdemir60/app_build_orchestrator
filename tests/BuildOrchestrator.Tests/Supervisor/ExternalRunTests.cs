using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Externals;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Supervisor;
using static BuildOrchestrator.Tests.Supervisor.RunCoordinatorTests;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [D6/D9] Harici projelerin koşu içindeki yeri: worker'lar doğmadan ÖNCE, liste sırasıyla ve TEK TEK
/// derlenirler. Bir harici patlarsa ana repo hiç derlenmez — yarım bir koşu, bayat bir harici DLL'e link'lenmiş
/// ana projeler demektir ve bu sessizce yanlış çıktı üretir.
/// </summary>
public class ExternalRunTests
{
    private static readonly string ExternalRoot = Path.Combine(Path.GetTempPath(), "bo-ext-run");

    private static string TargetOf(string name) => Path.Combine(ExternalRoot, name, name + ".sln");

    private static ExternalBuildPlan External(string name, bool willBuild = true, string? revision = "abc123") =>
        new(new ExternalProject(name, Path.Combine(ExternalRoot, name), TargetOf(name)),
            VcsKind.Git, revision, "SIG-" + name, willBuild, willBuild ? WillBuildReason.NeverBuilt : WillBuildReason.UpToDate);

    private static RunPlan PlanWithExternals(RunPlan plan, params ExternalBuildPlan[] externals)
        => plan with { Externals = externals };

    private static string ExternalNameOf(string projectId) => Path.GetFileNameWithoutExtension(projectId);

    // ---------------------------------------------------------------- sıra ve seri yürütme

    [Fact]
    public async Task Externals_build_sequentially_before_any_main_project()
    {
        var plan = PlanWithExternals(PlanOf(Node("A"), Node("B")), External("Mail"), External("Ocr"));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(parallelism: 4), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var started = h.Events.OfType<ProjectStartedEvent>().Select(e => ExternalNameOf(e.ProjectId)).ToList();
        Assert.Equal(["Mail", "Ocr"], started.Take(2));
        Assert.Equal(["A", "B"], started.Skip(2).Order());
        // Hariciler paralelliğe RAĞMEN tek tek koşar: ikisi aynı anda derlenirse ortak paket klasörleri
        // ve post-build copy event'leri birbirini ezerdi.
        Assert.All(invoker.Requests.Take(2), r => Assert.True(r.ExternalTarget));
    }

    [Fact]
    public async Task Externals_are_invoked_with_the_external_argument_contract()
    {
        var plan = PlanWithExternals(PlanOf(Node("A")), External("Mail"));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var request = invoker.Requests[0];
        Assert.Equal(TargetOf("Mail"), request.ProjectId);
        Assert.True(request.ExternalTarget);
        Assert.Null(request.BaseIntermediateOutputPath); // obj izolasyonu haricilere UYGULANMAZ
    }

    [Fact]
    public async Task An_up_to_date_external_is_skipped_with_the_shared_reason()
    {
        var plan = PlanWithExternals(PlanOf(Node("A")), External("Mail", willBuild: false));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var skipped = Assert.Single(h.Events.OfType<ProjectSkippedEvent>());
        Assert.Equal(TargetOf("Mail"), skipped.ProjectId);
        Assert.Equal(SkipReasons.UpToDate, skipped.Reason);
        Assert.DoesNotContain(invoker.Requests, r => r.ExternalTarget);
    }

    // ---------------------------------------------------------------- hata

    [Fact]
    public async Task An_external_failure_aborts_the_run_before_any_main_project_is_built()
    {
        var plan = PlanWithExternals(PlanOf(Node("A"), Node("B")), External("Mail"));
        var invoker = new FakeInvoker((req, _, _) => Task.FromResult(req.ExternalTarget ? Exit(1) : Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var events = h.Events;
        Assert.DoesNotContain(events.OfType<ProjectStartedEvent>(), e => ExternalNameOf(e.ProjectId) is "A" or "B");
        var failed = Assert.Single(events.OfType<ProjectFailedEvent>());
        Assert.Equal(TargetOf("Mail"), failed.ProjectId);
        var completed = Assert.IsType<RunCompletedEvent>(events[^1]);
        Assert.Equal(1, completed.Failed);
        Assert.Equal(2, completed.Queued); // ana projeler hiç dispatch edilmedi
        Assert.Contains(h.ConsoleLines, l => l.Contains("stopping before the main repository build", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_external_failure_skips_the_remaining_externals()
    {
        var plan = PlanWithExternals(PlanOf(Node("A")), External("Mail"), External("Ocr"));
        var invoker = new FakeInvoker((req, _, _) =>
            Task.FromResult(req.ProjectId == TargetOf("Mail") ? Exit(1) : Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.DoesNotContain(invoker.Requests, r => r.ProjectId == TargetOf("Ocr"));
        Assert.DoesNotContain(h.Events.OfType<ProjectStartedEvent>(), e => ExternalNameOf(e.ProjectId) == "Ocr");
    }

    // ---------------------------------------------------------------- defter

    [Fact]
    public async Task A_successful_external_persists_its_state_under_the_build_target()
    {
        string cacheRoot = Directory.CreateTempSubdirectory("bo-ext-state-").FullName;
        var store = new BuildStateStore(cacheRoot);
        var plan = PlanWithExternals(PlanOf(Node("A")), External("Mail", revision: "deadbeef"));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker, stateStore: store);

        await h.Sut.StartAsync(Start(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var record = store.Load()[TargetOf("Mail")];
        Assert.Equal("SIG-Mail", record.BuiltSignature);
        Assert.Equal("deadbeef", record.BuiltCommit);
        Assert.Equal(BuildResult.Succeeded, record.LastResult);
    }

    [Fact]
    public async Task A_failed_external_does_not_persist_a_green_record()
    {
        string cacheRoot = Directory.CreateTempSubdirectory("bo-ext-state-").FullName;
        var store = new BuildStateStore(cacheRoot);
        store.Upsert(new BuildState(TargetOf("Mail"), "SIG-Mail", LastResult: BuildResult.Succeeded));
        var plan = PlanWithExternals(PlanOf(Node("A")), External("Mail"));
        var invoker = new FakeInvoker((req, _, _) => Task.FromResult(req.ExternalTarget ? Exit(1) : Ok()));
        using var h = new Harness(plan, invoker, stateStore: store);

        await h.Sut.StartAsync(Start(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // Başarısız bir harici "bilinen iyi" değildir — bir sonraki koşu onu yeniden derlemelidir.
        Assert.Equal(BuildResult.Failed, store.Load()[TargetOf("Mail")].LastResult);
    }

    // ---------------------------------------------------------------- önizleme ve sayaçlar

    [Fact]
    public async Task The_run_preview_and_the_project_total_include_externals()
    {
        var plan = PlanWithExternals(PlanOf(Node("A"), Node("B")), External("Mail"));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var events = h.Events;
        Assert.Equal(3, events.OfType<RunStartedEvent>().Single().TotalProjects);
        var preview = events.OfType<BuildPreviewEvent>().Single();
        Assert.Equal(TargetOf("Mail"), preview.Items[0].ProjectId);
        Assert.Equal("abc123", preview.Items[0].BuiltCommit);
        Assert.True(preview.Items[0].WillBuild);
    }

    // ---------------------------------------------------------------- stop ve kapsam

    [Fact]
    public async Task A_stop_between_two_externals_stops_before_the_next_one()
    {
        var firstStarted = Signal();
        var release = Signal();
        var plan = PlanWithExternals(PlanOf(Node("A")), External("Mail"), External("Ocr"));
        var invoker = new FakeInvoker(async (req, _, _) =>
        {
            if (req.ProjectId == TargetOf("Mail")) { firstStarted.TrySetResult(); await release.Task; }
            return Ok();
        });
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(), default);
        await firstStarted.Task.WaitAsync(Limit);
        Assert.True(h.Sut.TryRequestStop(StopKind.Graceful));
        release.TrySetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.DoesNotContain(invoker.Requests, r => r.ProjectId == TargetOf("Ocr"));
        Assert.Equal(RunOutcome.Stopped, h.Events.OfType<RunCompletedEvent>().Single().Outcome);
    }

    [Fact]
    public async Task A_cycles_run_ignores_externals_entirely()
    {
        // Cycles ana reponun SCC onarımıdır; harici projelerin orada işi yoktur.
        var plan = PlanWithExternals(CyclePlanOf(["A", "B"], Node("A", ["B"], inCycle: true), Node("B", ["A"], inCycle: true)),
            External("Mail"));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(RunMode.Cycles), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.DoesNotContain(invoker.Requests, r => r.ExternalTarget);
        Assert.DoesNotContain(h.Events.OfType<ProjectStartedEvent>(), e => ExternalNameOf(e.ProjectId) == "Mail");
    }

    [Fact]
    public async Task A_preparation_failure_ends_the_run_before_it_starts()
    {
        // Kir, ayrışma ya da eksik tf.exe planlama sırasında yakalanır: koşu HİÇ başlamaz ve kullanıcı
        // hatayı olduğu gibi görür (Build butonu geri açılır).
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(PlanOf(Node("A")), invoker,
            planner: (_, _) => throw ExternalPreparationException.Dirty("Mail", @"D:\ext\mail"));

        await h.Sut.StartAsync(Start(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var error = Assert.Single(h.Events.OfType<ErrorEvent>());
        Assert.Equal("planFailed", error.Code);
        Assert.Contains("'Mail'", error.Message);
        Assert.Empty(h.Events.OfType<RunStartedEvent>());
        Assert.Empty(invoker.Requests);
    }

    [Fact]
    public async Task A_run_without_externals_behaves_exactly_as_before()
    {
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(PlanOf(Node("A"), Node("B")), invoker);

        await h.Sut.StartAsync(Start(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(2, h.Events.OfType<RunStartedEvent>().Single().TotalProjects);
        Assert.DoesNotContain(invoker.Requests, r => r.ExternalTarget);
    }
}
