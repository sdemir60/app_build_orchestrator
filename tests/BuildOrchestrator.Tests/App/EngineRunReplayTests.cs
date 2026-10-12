using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Supervisor;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.Tests.Supervisor.RunCoordinatorTests;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// <b>Motorun gerçek koşu olayları görünüm modeline oynatılır.</b> Koordinatör sahte MSBuild'le koşar
/// (<see cref="RunCoordinatorTests.Harness"/>) ve yazdığı olaylar sırasıyla <see cref="RunViewModel.OnEvent"/>'e verilir;
/// satırlar önce Sync'in kararını alır (planın kendi kararı — Sync aynı soruyu sorar). Ekranın söylediği böylece elle
/// kurulmuş bir önizlemeye değil, motorun gerçekten gönderdiğine karşı ölçülür.
/// <para><b>Ölçülen kusur (2026-10-12, gerçek koşu: 10:44 Rebuild, 194 proje, hepsi güncel):</b> dalga bütün düğümleri
/// amber'a yaktı, koşu başlar başlamaz hepsi yeşile döndü. Motorun önizlemesi güncel projelere <c>WillBuild=false</c>
/// gönderiyordu (planlayıcının artımlı cevabı); kuyruk, sabit ilerleme paydası, "ortada iş var mı" sorusu ve açılış satırı
/// o bayraktan okunduğu için şerit koşu boyunca <c>Checking — scanning for changes…</c>, çubuk %0, akış <c>Build started —
/// 0 projects</c>, Stop sonrası <c>Stopped — 0/0 · 0 not built</c>, bitişte <c>Everything up to date — … nothing to
/// build</c> dedi. Tek kök neden, her belirti ayrı test.</para>
/// </summary>
public class EngineRunReplayTests
{
    private static readonly string[] Names = ["A", "B", "C"];

    /// <summary>Hepsi güncel bir plan: planlayıcının (ve Sync'in) cevabı <c>false</c> · <c>up to date</c>.</summary>
    private static RunPlan AllUpToDate(params string[] names) =>
        PlanOf([.. names.Select(n => Node(n, willBuild: false) with { WillBuildReason = WillBuildReason.UpToDate })]);

    /// <summary>Koordinatörü <paramref name="cmd"/> ile sonuna kadar koşturur ve yazdığı olayları döner.</summary>
    private static async Task<IReadOnlyList<IpcEvent>> RunEngineAsync(RunPlan plan, StartRunCommand cmd)
    {
        using var h = new Harness(plan, AllSucceed());
        await h.Sut.StartAsync(cmd, default);
        await h.Sut.RunCompletion.WaitAsync(Limit);
        return h.Events;
    }

    /// <summary>Koşudan önceki ekran: topoloji + Sync'in önizlemesi (planın kararı).</summary>
    private static RunViewModel Synced(RunPlan plan)
    {
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe),
            new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new WorkspaceTopologyEvent(plan.Plan.Nodes, plan.Plan.Cycles, [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, plan.Plan.Nodes.Count, 0));
        vm.OnEvent(new BuildPreviewEvent([.. plan.Plan.Nodes.Select(n =>
            new BuildPreviewItem(n.Id, n.Name, n.WillBuild, Reason: n.WillBuildReason))]));
        return vm;
    }

    /// <summary>Olayları motorun önizlemesine kadar (dahil) oynatır: koşu başladı, hiçbir proje henüz başlamadı.
    /// Kalan olaylar döner.</summary>
    private static IReadOnlyList<IpcEvent> ReplayThroughPreview(RunViewModel vm, IReadOnlyList<IpcEvent> events)
    {
        int preview = events.ToList().FindIndex(e => e is BuildPreviewEvent);
        Assert.True(preview >= 0, "the engine sent no preview");
        foreach (var e in events.Take(preview + 1)) vm.OnEvent(e);
        return [.. events.Skip(preview + 1)];
    }

    /// <summary>Olayları ilk başarıya kadar (dahil) oynatır — bir proje bitti, kalanlar sırada. Kalan olaylar döner.</summary>
    private static IReadOnlyList<IpcEvent> ReplayThroughFirstSuccess(RunViewModel vm, IReadOnlyList<IpcEvent> events)
    {
        int first = events.ToList().FindIndex(e => e is ProjectSucceededEvent);
        Assert.True(first >= 0, "no project succeeded");
        foreach (var e in events.Take(first + 1)) vm.OnEvent(e);
        return [.. events.Skip(first + 1)];
    }

    /// <summary>Koşu bir proje bittikten sonra durduruldu: motorun Stop kapanışı (<c>runStopped</c> + <c>runCompleted</c>).</summary>
    private static void StopAfterFirstSuccess(RunViewModel vm, IReadOnlyList<IpcEvent> events, int total)
    {
        ReplayThroughFirstSuccess(vm, events);
        vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, Succeeded: 1, Failed: 0, Skipped: 0,
            Queued: total - 1, DurationMs: 100));
    }

    private static ProjectRowViewModel Row(RunViewModel vm, string name) => vm.Projects.Single(r => r.Name == name);

    /// <summary>Kullanıcının bildirdiği belirti: dalganın yaktığı amber koşu başlayınca SÖNMEZ (ARCHITECTURE §14.3) —
    /// Rebuild hepsini derler, hepsi kuyruktadır.</summary>
    [Fact]
    public async Task A_rebuild_keeps_every_up_to_date_row_queued_once_the_engines_preview_arrives()
    {
        var plan = AllUpToDate(Names);
        var events = await RunEngineAsync(plan, Start(RunMode.Rebuild));
        var vm = Synced(plan);
        Assert.All(vm.Projects, r => Assert.Equal(VisualStatus.Current, r.VisualStatus)); // ön-koşul: hepsi güncel, yeşil

        ReplayThroughPreview(vm, events);

        Assert.All(vm.Projects, r => Assert.Equal(VisualStatus.Queued, r.VisualStatus));
    }

    /// <summary>Aynısı güncel bir döngü grubunun üyelerinde: Rebuild grubu tur 1'de baştan derler (§8.8).</summary>
    [Fact]
    public async Task A_rebuild_keeps_an_up_to_date_cycle_groups_members_queued()
    {
        var plan = CyclePlanOf(["M1", "M2"],
            Node("M1", deps: ["M2"], inCycle: true, willBuild: false) with { WillBuildReason = WillBuildReason.UpToDate },
            Node("M2", deps: ["M1"], inCycle: true, willBuild: false) with { WillBuildReason = WillBuildReason.UpToDate });
        var events = await RunEngineAsync(plan, Start(RunMode.Rebuild));
        var vm = Synced(plan);

        ReplayThroughPreview(vm, events);

        Assert.All(vm.Projects, r => Assert.Equal(VisualStatus.Queued, r.VisualStatus));
    }

    /// <summary>Olay akışının açılış satırı koşunun derleyeceği proje sayısını söyler.</summary>
    [Fact]
    public async Task A_rebuilds_opening_line_counts_every_project()
    {
        var plan = AllUpToDate(Names);
        var events = await RunEngineAsync(plan, Start(RunMode.Rebuild));
        var vm = Synced(plan);

        ReplayThroughPreview(vm, events);

        Assert.Equal("Build started — 3 projects, parallelism 1",
            vm.StreamEvents.Single(s => s.Text.StartsWith("Build started", StringComparison.Ordinal)).Text);
    }

    /// <summary>Şerit koşu boyunca derleme ilerlemesini her projeye karşı sayar; çubuk ilerler.</summary>
    [Fact]
    public async Task A_running_rebuild_counts_its_progress_against_every_project()
    {
        var plan = AllUpToDate(Names);
        var events = await RunEngineAsync(plan, Start(RunMode.Rebuild));
        var vm = Synced(plan);

        var rest = ReplayThroughPreview(vm, events);
        Assert.StartsWith("▸ Building 0/3", vm.RibbonLine.Text, StringComparison.Ordinal);
        ReplayThroughFirstSuccess(vm, rest);

        Assert.StartsWith("▸ Building 1/3", vm.RibbonLine.Text, StringComparison.Ordinal);
        Assert.Equal(100.0 / 3, RibbonText.Progress(vm.Phase, vm.AllClean, vm.Counters, vm.WillBuildCount,
            vm.FinishedOfWillBuild, vm.Counters.Total), precision: 3);
    }

    /// <summary>Biten Rebuild ne derlediğini söyler — "her şey güncel, derlenecek bir şey yok" DEĞİL.</summary>
    [Fact]
    public async Task A_completed_rebuild_reports_what_it_built()
    {
        var plan = AllUpToDate(Names);
        var events = await RunEngineAsync(plan, Start(RunMode.Rebuild));
        var vm = Synced(plan);

        foreach (var e in events) vm.OnEvent(e);

        Assert.StartsWith("Completed — 3 succeeded · 0 skipped", vm.RibbonLine.Text, StringComparison.Ordinal);
    }

    /// <summary>Durdurulan Rebuild derlemediklerini sayar.</summary>
    [Fact]
    public async Task A_stopped_rebuild_counts_the_projects_it_did_not_build()
    {
        var plan = AllUpToDate(Names);
        var events = await RunEngineAsync(plan, Start(RunMode.Rebuild));
        var vm = Synced(plan);

        StopAfterFirstSuccess(vm, events, Names.Length);

        Assert.Equal("▸ Stopped — 1/3 · 2 not built", vm.RibbonLine.Text);
    }
}
