using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Supervisor;
using static BuildOrchestrator.Tests.Supervisor.RunCoordinatorTests;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// Koşullu yeniden derlemenin koordinatör tarafı: önceki koşuda hatalı bir bağımlılığa rağmen başarıyla
/// derlenmiş (dep-issue notlu) ve imzası değişmemiş proje (<see cref="WillBuildReason.WaitingForDependency"/>)
/// sırası geldiğinde ANCAK kayıtlı köklerinden biri artık başarılıysa derlenir; hepsi hâlâ hatalıysa
/// <c>dependency still failing</c> ile atlanır ve defter kaydına dokunulmaz. Karar Core'dadır
/// (<c>ConditionalRebuild</c>), koordinatör uygular.
///
/// <para>Fixture: <see cref="RunCoordinatorTests"/>'in harness'ı AYNEN (<c>using static</c>) — kopya YASAK.
/// Planlayıcı sahte olduğu için düğümün gerekçesi elle verilir; gerekçenin defterden türediği
/// <c>WillBuildTests</c>'te sınanır.</para>
/// </summary>
public class ConditionalRebuildRunTests : IDisposable
{
    private readonly string _cacheRoot = NewCacheRoot();

    public void Dispose() { if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, recursive: true); }

    private static readonly DateTimeOffset RecordedAt = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Önceki koşunun defteri: Up patladı; Down ona rağmen başarıyla derlendi ve Up'ı kök olarak not etti.</summary>
    private BuildStateStore SeededStore(BuildResult upLastResult = BuildResult.Failed, bool downRootsKnown = true)
    {
        var store = new BuildStateStore(_cacheRoot);
        store.Upsert(new BuildState(Id("Up"), "up-sig", LastResult: upLastResult, LastRunAt: RecordedAt));
        store.Upsert(new BuildState(Id("Down"), "down-sig", "oldsha", BuildResult.Succeeded, RecordedAt,
            DepIssue: true, DepIssueRoots: downRootsKnown ? [Id("Up")] : null));
        return store;
    }

    /// <summary>Up → Down → Leaf. Down koşullu; Leaf kendi değişikliğiyle kesin derlenecek.</summary>
    private static RunPlan ChainPlan(bool upWillBuild = true, WillBuildReason downReason = WillBuildReason.WaitingForDependency) =>
        new(new BuildPlan(
            [Node("Up", willBuild: upWillBuild) with
                { BuildOrder = 0, WillBuildReason = upWillBuild ? WillBuildReason.LastFailed : WillBuildReason.UpToDate },
             Node("Down", deps: ["Up"], willBuild: true) with { BuildOrder = 1, WillBuildReason = downReason },
             Node("Leaf", deps: ["Down"], willBuild: true) with { BuildOrder = 2, WillBuildReason = WillBuildReason.SignatureChanged }],
            Cycles: [], Configuration: "Debug"),
            EmptyRefs(), Incremental: RunCoordinatorTests.Incremental("Up", "Down", "Leaf"));

    private static FakeInvoker UpFails() =>
        new((req, _, _) => Task.FromResult(NameOf(req.ProjectId) == "Up" ? Exit(1) : Ok()));

    private static FakeInvoker AllSucceed() => new((_, _, _) => Task.FromResult(Ok()));

    private static async Task RunAsync(Harness h, StartRunCommand cmd)
    {
        await h.Sut.StartAsync(cmd, default);
        await h.Sut.RunCompletion.WaitAsync(Limit);
    }

    // ---------------------------------------------------------------- 1) kök hâlâ hatalı

    /// <summary>
    /// Senaryo 1: kök bu koşuda yine patlıyor ⇒ Down dispatch EDİLMEZ, <c>dependency still failing</c> ile
    /// atlanır; decision.log gerekçeyi kök adıyla tek satırda yazar; defter kaydı (not, kökler, imza, commit,
    /// zaman) olduğu gibi kalır. Down'a bağlı Leaf derlenir ve Up'ı Down üzerinden MİRAS alır — Down'un çıktısı
    /// hâlâ bayat Up'a link'lidir, not zincirde kaybolmamalıdır.
    /// </summary>
    [Fact]
    public async Task A_waiting_project_whose_root_fails_again_is_skipped_and_its_ledger_record_is_kept()
    {
        var store = SeededStore();
        var invoker = UpFails();
        using var h = new Harness(ChainPlan(), invoker, stateStore: store);

        await RunAsync(h, Start(RunMode.Build));

        Assert.Equal([Id("Up"), Id("Leaf")], invoker.Requests.Select(r => r.ProjectId));
        var skip = Assert.Single(h.Events.OfType<ProjectSkippedEvent>());
        Assert.Equal((Id("Down"), SkipReasons.DependencyStillFailing), (skip.ProjectId, skip.Reason));
        Assert.DoesNotContain(h.Events.OfType<ProjectStartedEvent>(), e => e.ProjectId == Id("Down"));
        Assert.Contains("Down: skipped — dependency still failing (Up)", h.DecisionLog);

        var ledger = store.Load();
        Assert.Equal(new BuildState(Id("Down"), "down-sig", "oldsha", BuildResult.Succeeded, RecordedAt,
            DepIssue: true, DepIssueRoots: [Id("Up")]), ledger[Id("Down")]);

        var leaf = Assert.Single(h.Events.OfType<ProjectSucceededEvent>(), e => e.ProjectId == Id("Leaf"));
        Assert.Equal(["Up"], leaf.DepIssues);
        Assert.Equal([Id("Up")], ledger[Id("Leaf")].DepIssueRoots);

        // Atlanan proje bu koşunun dep-issue sayacına girmez: o sayı bu koşuda DERLENEN etkilenmişleri anlatır.
        Assert.Equal(1, Assert.Single(h.Events.OfType<RunCompletedEvent>()).DepIssueCount);
    }

    // ---------------------------------------------------------------- 2) kök bu koşuda başarılı

    /// <summary>Senaryo 2: kök bu koşuda başarıyla derlendi ⇒ Down onun ARKASINDAN derlenir ve notu temizlenir.</summary>
    [Fact]
    public async Task A_waiting_project_is_built_after_its_root_succeeds_in_this_run_and_its_note_is_cleared()
    {
        var store = SeededStore();
        var invoker = AllSucceed();
        using var h = new Harness(ChainPlan(), invoker, stateStore: store);

        await RunAsync(h, Start(RunMode.Build));

        Assert.Equal([Id("Up"), Id("Down"), Id("Leaf")], invoker.Requests.Select(r => r.ProjectId));
        Assert.Empty(h.Events.OfType<ProjectSkippedEvent>());
        var down = store.Load()[Id("Down")];
        Assert.False(down.DepIssue);
        Assert.Null(down.DepIssueRoots);
        Assert.Equal("sig", down.BuiltSignature);
    }

    // ---------------------------------------------------------------- 3) kök kaynak değişmeden düzeldi

    /// <summary>
    /// Senaryo 3 (§8.3 güvenlik): Up kaynak değişmeden düzelmiş — defterde son sonucu başarı, bu koşuda
    /// <c>up to date</c> atlanıyor. Down yine de derlenir: imzası değişmediği için onu yeniden derlemeye
    /// götürecek başka bir sinyal yoktur.
    /// </summary>
    [Fact]
    public async Task A_waiting_project_is_built_when_its_root_recovered_without_a_source_change()
    {
        var store = SeededStore(upLastResult: BuildResult.Succeeded);
        var invoker = AllSucceed();
        using var h = new Harness(ChainPlan(upWillBuild: false), invoker, stateStore: store);

        await RunAsync(h, Start(RunMode.Build));

        Assert.Equal([Id("Down"), Id("Leaf")], invoker.Requests.Select(r => r.ProjectId));
        var skip = Assert.Single(h.Events.OfType<ProjectSkippedEvent>());
        Assert.Equal((Id("Up"), SkipReasons.UpToDate), (skip.ProjectId, skip.Reason));
        Assert.False(store.Load()[Id("Down")].DepIssue);
    }

    // ---------------------------------------------------------------- 4) eski kayıt

    /// <summary>
    /// Senaryo 4: kök listesi olmayan eski kayıt ⇒ planlayıcı <see cref="WillBuildReason.DepIssue"/> der ve
    /// koşu projeyi bugünkü gibi KOŞULSUZ derler. Bu derleme defteri yeni biçime taşır: not artık kökleriyle
    /// yazılır, bir sonraki Build koşullu karar verebilir.
    /// </summary>
    [Fact]
    public async Task A_legacy_dep_issue_record_is_built_unconditionally_and_rewritten_with_its_roots()
    {
        var store = SeededStore(downRootsKnown: false);
        var invoker = UpFails();
        using var h = new Harness(ChainPlan(downReason: WillBuildReason.DepIssue), invoker, stateStore: store);

        await RunAsync(h, Start(RunMode.Build));

        Assert.Contains(Id("Down"), invoker.Requests.Select(r => r.ProjectId));
        Assert.Empty(h.Events.OfType<ProjectSkippedEvent>());
        var down = store.Load()[Id("Down")];
        Assert.True(down.DepIssue);
        Assert.Equal([Id("Up")], down.DepIssueRoots);
    }

    // ---------------------------------------------------------------- 6) önizleme

    /// <summary>
    /// Senaryo 6: önizleme koşullu projeyi ayırt edilebilir taşır — gerekçe <c>WaitingForDependency</c>,
    /// <c>Conditional=true</c> (kuyruk değil) ve etiket için kök ADLARI. Kesin derlenecek projeler koşullu değildir.
    /// </summary>
    [Fact]
    public async Task The_preview_marks_a_waiting_project_as_conditional_and_carries_its_root_names()
    {
        var store = SeededStore();
        using var h = new Harness(ChainPlan(), UpFails(), stateStore: store);

        await RunAsync(h, Start(RunMode.Build));

        var items = Assert.Single(h.Events.OfType<BuildPreviewEvent>()).Items.ToDictionary(i => NameOf(i.ProjectId));
        Assert.Equal(WillBuildReason.WaitingForDependency, items["Down"].Reason);
        Assert.True(items["Down"].Conditional);
        Assert.Equal(["Up"], items["Down"].DependencyRoots);
        Assert.False(items["Up"].Conditional);
        Assert.Null(items["Up"].DependencyRoots);
        Assert.False(items["Leaf"].Conditional);
    }

    // ---------------------------------------------------------------- 7) Rebuild ve tek proje koşusu

    /// <summary>Senaryo 7a: Rebuild her şeyi derler — kök patlasa da Down derlenir, önizleme koşullu demez.</summary>
    [Fact]
    public async Task Rebuild_builds_a_waiting_project_unconditionally()
    {
        var store = SeededStore();
        var invoker = UpFails();
        using var h = new Harness(ChainPlan(), invoker, stateStore: store);

        await RunAsync(h, Start(RunMode.Rebuild));

        Assert.Equal([Id("Up"), Id("Down"), Id("Leaf")], invoker.Requests.Select(r => r.ProjectId));
        Assert.Empty(h.Events.OfType<ProjectSkippedEvent>());
        Assert.False(Assert.Single(h.Events.OfType<BuildPreviewEvent>()).Items
            .Single(i => i.ProjectId == Id("Down")).Conditional);
    }

    /// <summary>Senaryo 7b: satırdan tetiklenen hedef koşulsuz derlenir — kökü defterde hâlâ hatalı olsa da.</summary>
    [Fact]
    public async Task A_single_project_run_builds_a_waiting_target_unconditionally()
    {
        var store = SeededStore();
        var invoker = AllSucceed();
        using var h = new Harness(ChainPlan(), invoker, stateStore: store);

        await RunAsync(h, Start(RunMode.Build, parallelism: 2) with { ScopeProjectId = Id("Down") });

        Assert.Equal([Id("Down")], invoker.Requests.Select(r => r.ProjectId));
        Assert.Empty(h.Events.OfType<ProjectSkippedEvent>());
        Assert.False(Assert.Single(Assert.Single(h.Events.OfType<BuildPreviewEvent>()).Items).Conditional);
    }

    // ---------------------------------------------------------------- 1b) kök defterden hâlâ hatalı (bu koşuda hiç denenmedi)

    /// <summary>
    /// [Task 4 — carried item 3] Up bu koşuda hiç DENENMEDİ — kendi başına önemsiz bir döngünün (Loop ile)
    /// üyesi ve Build modunda "in dependency cycle" ile pre-skip edilir (SCC'ler yalnız Cycles'ta derlenir).
    /// "Hâlâ hatalı" iddiası yalnız koşu BAŞINDAKİ defterden geliyor (SeededStore: Up'ın son sonucu Failed) —
    /// decision.log satırı bunu AYIRT ETMELİDİR: "Up failed in this run" YALANI basılmaz, son bilinen sonuç
    /// olduğu söylenir. Senaryo 1'deki ("…(Up)") test — kök GERÇEKTEN bu koşuda patladığında — DEĞİŞMEZ.
    /// </summary>
    [Fact]
    public async Task A_still_failing_root_that_never_ran_this_run_is_labelled_from_its_last_known_result()
    {
        var store = SeededStore(); // Up: defterdeki son sonucu Failed
        var invoker = AllSucceed(); // Up/Loop zaten dispatch edilmez (SCC, Build modu); Down/Leaf normal derlenir
        var plan = new RunPlan(new BuildPlan(
            [Node("Up", deps: ["Loop"], inCycle: true, willBuild: true) with
                { BuildOrder = 0, WillBuildReason = WillBuildReason.LastFailed },
             Node("Loop", deps: ["Up"], inCycle: true, willBuild: true) with
                { BuildOrder = 1, WillBuildReason = WillBuildReason.NeverBuilt },
             Node("Down", deps: ["Up"], willBuild: true) with { BuildOrder = 2, WillBuildReason = WillBuildReason.WaitingForDependency },
             Node("Leaf", deps: ["Down"], willBuild: true) with { BuildOrder = 3, WillBuildReason = WillBuildReason.SignatureChanged }],
            Cycles: [[Id("Up"), Id("Loop")]], Configuration: "Debug"),
            EmptyRefs(), Incremental: RunCoordinatorTests.Incremental("Up", "Loop", "Down", "Leaf"));
        using var h = new Harness(plan, invoker, stateStore: store);

        await RunAsync(h, Start(RunMode.Build));

        Assert.DoesNotContain(Id("Up"), invoker.Requests.Select(r => r.ProjectId)); // hiç dispatch edilmedi
        var skip = Assert.Single(h.Events.OfType<ProjectSkippedEvent>(), e => e.ProjectId == Id("Down"));
        Assert.Equal(SkipReasons.DependencyStillFailing, skip.Reason);
        Assert.Contains("Down: skipped — dependency still failing (Up (last known failure))", h.DecisionLog);
    }

    // ---------------------------------------------------------------- Cycles modu

    /// <summary>Cycles modunda kapsam içi (bir döngünün upstream'i olan) koşullu proje aynı kurala tabidir.</summary>
    [Fact]
    public async Task A_cycles_run_applies_the_same_rule_to_an_in_scope_waiting_project()
    {
        var store = SeededStore();
        var invoker = UpFails();
        var plan = new RunPlan(new BuildPlan(
            [Node("Up", willBuild: true) with { BuildOrder = 0, WillBuildReason = WillBuildReason.LastFailed },
             Node("Down", deps: ["Up"], willBuild: true) with { BuildOrder = 1, WillBuildReason = WillBuildReason.WaitingForDependency },
             Node("A", deps: ["B", "Down"], inCycle: true, willBuild: true) with { BuildOrder = 2, WillBuildReason = WillBuildReason.NeverBuilt },
             Node("B", deps: ["A"], inCycle: true, willBuild: true) with { BuildOrder = 3, WillBuildReason = WillBuildReason.NeverBuilt }],
            Cycles: [[Id("A"), Id("B")]], Configuration: "Debug"),
            EmptyRefs(), Incremental: RunCoordinatorTests.Incremental("Up", "Down", "A", "B"));
        using var h = new Harness(plan, invoker, stateStore: store);

        await RunAsync(h, Start(RunMode.Cycles));

        Assert.DoesNotContain(Id("Down"), invoker.Requests.Select(r => r.ProjectId));
        var skip = Assert.Single(h.Events.OfType<ProjectSkippedEvent>(), e => e.ProjectId == Id("Down"));
        Assert.Equal(SkipReasons.DependencyStillFailing, skip.Reason);
        Assert.Contains(Id("A"), invoker.Requests.Select(r => r.ProjectId)); // grup yine turlarla derlenir
    }
}
