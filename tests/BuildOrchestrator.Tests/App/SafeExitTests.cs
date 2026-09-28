using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [P3 · Task 2] Güvenli tam çıkış (<see cref="RunViewModel.RequestExit"/>) arkada yarım iş BIRAKMAZ: uçuşta iş yoksa
/// çıkış HEMEN hazırdır; bir derleme koşuyorsa graceful durdurulur (yeni proje başlamaz, uçuştakiler post-build copy
/// dahil biter) ve çıkış drain'i bekler; Sync/Clean/Optimize/checkout/pull (ve bitişlerinde zincirlenen Sync) beklenir.
/// Bekleyiş sonsuz DEĞİLDİR: motor susarsa (sessizlik bekçisi) ya da ölürse çıkış serbest kalır. İkinci istek ikinci
/// bir durdurma ya da satır üretmez; bekleyişte kendiliğinden Sync başlamaz.
///
/// <para>Harness VM düzeyidir: motor başlatılmaz, gönderim <see cref="MainWindowHost.AcceptSends"/> ile kabul edilir,
/// giden komutlar <c>DebugOnCommandSent</c> ile izlenir ve motorun cevabı <c>vm.OnEvent(...)</c> ile verilir. D8:
/// saat enjekte edilir, gerçek bekleme yok.</para>
/// </summary>
public sealed class SafeExitTests
{
    private const string Root = @"D:\repo";

    /// <summary>VM + gönderilen komutlar + <see cref="RunViewModel.ExitReady"/> sayacı.</summary>
    private sealed class Harness
    {
        public RunViewModel Vm { get; }
        public List<IpcCommand> Sent { get; } = [];
        public int Ready { get; private set; }

        public Harness(string root = Root, Func<long>? nowMs = null)
        {
            Vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), MainWindowHost.NeverTickingBatcher(),
                () => "r1", nowMs) { RootPath = root };
            MainWindowHost.AcceptSends(Vm);
            Vm.DebugOnCommandSent = Sent.Add;
            Vm.ExitReady += (_, _) => Ready++;
        }

        /// <summary>Konsolda <see cref="RunViewModel.ExitPendingLine"/> kaç kez yazıldı.</summary>
        public int ExitLines => Vm.GetRunDocumentText().Split('\n').Count(l => l == RunViewModel.ExitPendingLine);

        /// <summary>Motor bir derlemeye başladı: koşu kilidi (<see cref="RunViewModel.IsMidRunLocked"/>) açık.</summary>
        public void StartBuild() => Vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 2, "Debug", 0));
    }

    // ---------------------------------------------------------------- uçuşta iş yok

    /// <summary>Uçuşta hiçbir iş yokken çıkış ANINDA hazırdır: bekleyiş açılmaz, konsola satır düşmez, motora komut
    /// gitmez. İkinci istek ikinci bir atım üretmez.</summary>
    [Fact]
    public void An_exit_with_nothing_in_flight_is_ready_at_once()
    {
        var h = new Harness();

        h.Vm.RequestExit();

        Assert.Equal(1, h.Ready);
        Assert.False(h.Vm.ExitPending);
        Assert.Empty(h.Sent);
        Assert.Equal(0, h.ExitLines);

        h.Vm.RequestExit();
        Assert.Equal(1, h.Ready); // TEK atım
    }

    // ---------------------------------------------------------------- derleme

    /// <summary>Koşan bir derleme graceful durdurulur (TEK <see cref="StopRunCommand"/>, <see cref="StopKind.Graceful"/>)
    /// ve çıkış drain'i bekler: uçuştaki proje bitene, motor <c>runStopped</c> yazana kadar <c>ExitReady</c> yoktur.
    /// Ardından gelen <c>runCompleted</c> ikinci bir atım üretmez.</summary>
    [Fact]
    public void An_exit_during_a_build_stops_it_gracefully_and_waits_for_the_drain()
    {
        var h = new Harness();
        h.StartBuild();
        h.Vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));

        h.Vm.RequestExit();

        Assert.Equal(new StopRunCommand("r1", StopKind.Graceful), Assert.Single(h.Sent));
        Assert.Equal(AppPhase.Stopping, h.Vm.Phase);
        Assert.True(h.Vm.ExitPending);
        Assert.Equal(1, h.ExitLines);
        Assert.Equal(0, h.Ready);

        h.Vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 1200)); // uçuştaki proje drain'de bitti
        Assert.Equal(0, h.Ready); // motor koşuyu henüz kapatmadı

        h.Vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));
        Assert.Equal(1, h.Ready);

        h.Vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 1, 0, 0, 1, 1500));
        Assert.Equal(1, h.Ready); // TEK atım
    }

    /// <summary>İkinci istek (ikinci ×, tepsi → Exit) tam no-op'tur: ikinci bir <c>stopRun</c> ya da ikinci bir konsol
    /// satırı üretmez.</summary>
    [Fact]
    public void A_second_exit_request_sends_no_second_stop_and_no_second_line()
    {
        var h = new Harness();
        h.StartBuild();

        h.Vm.RequestExit();
        h.Vm.RequestExit();

        Assert.Single(h.Sent.OfType<StopRunCommand>());
        Assert.Equal(1, h.ExitLines);
        Assert.Equal(0, h.Ready);
    }

    // ---------------------------------------------------------------- workspace işleri

    /// <summary>Çıkışın beklediği workspace işi.</summary>
    public enum WorkspaceJob { Sync, Clean, Optimize, Checkout, Pull }

    private static readonly BranchRef FeatureX = new("feature/x", "bbbbbbbbbbbb", IsActive: false, IsRemoteTracking: false);

    /// <summary>İşi üretimdeki girişinden başlatır ve (varsa) motorun başlangıç cevabını verir — iş uçuşta.</summary>
    private static async Task StartAsync(RunViewModel vm, WorkspaceJob job)
    {
        switch (job)
        {
            case WorkspaceJob.Sync:
                await vm.SyncCommand.ExecuteAsync(null);
                vm.OnEvent(new SyncStartedEvent(Root, "main"));
                break;
            case WorkspaceJob.Clean:
                await vm.CleanCommand.ExecuteAsync(null);
                vm.OnEvent(new CleanStartedEvent(Root));
                break;
            case WorkspaceJob.Optimize:
                await vm.OptimizeCommand.ExecuteAsync(null);
                vm.OnEvent(new OptimizeStartedEvent(Root));
                break;
            case WorkspaceJob.Checkout:
                vm.OnEvent(new BranchListEvent(
                    [new BranchRef("main", "aaaaaaaaaaaa", IsActive: true, IsRemoteTracking: false), FeatureX]));
                await vm.SelectBranch(FeatureX);
                break;
            case WorkspaceJob.Pull:
                vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0, Behind: 2)); // "N behind" chip'i görünür
                await vm.PullRepositoryCommand.ExecuteAsync(null);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(job), job, null);
        }
    }

    private static readonly SyncCompletedEvent SyncDone = new("main", "sha1234", false, 1, 0);

    /// <summary>Motorun işi bitirişi ve — bitişte bir Sync zincirlenen işlerde — o Sync'in cevabı, sırasıyla.</summary>
    private static IpcEvent[] EndWithItsChain(WorkspaceJob job)
    {
        IpcEvent[] chainedSync = [new SyncStartedEvent(Root, "main"), SyncDone];
        return job switch
        {
            WorkspaceJob.Sync => [SyncDone],
            WorkspaceJob.Clean => [new CleanCompletedEvent(1, 2, 1024, 0, 1), .. chainedSync],
            WorkspaceJob.Optimize => [new OptimizeCompletedEvent(ProjectCount: 1), .. chainedSync],
            WorkspaceJob.Checkout => [new CheckoutCompletedEvent(CheckoutStatus.Switched, "main", "feature/x",
                "b7e91d4a0c1f2e3d4c5b6a7980716253443526a1", 0, null, null), .. chainedSync],
            WorkspaceJob.Pull => [new PullCompletedEvent(Succeeded: true), .. chainedSync],
            _ => throw new ArgumentOutOfRangeException(nameof(job), job, null),
        };
    }

    /// <summary>Workspace işi durdurulmaz (iptali yoktur; yarım bırakılan bir checkout/pull/Clean ağacı bozar) — çıkış
    /// onu ve bitişinde zincirlenen Sync'i BEKLER: iş ve zinciri sürerken <c>ExitReady</c> yoktur, zincir bitince bir
    /// kez gelir. Motora durdurma gitmez; zincirin kendi Sync'i dışında Sync de gitmez.</summary>
    [Theory]
    [InlineData(WorkspaceJob.Sync)]
    [InlineData(WorkspaceJob.Clean)]
    [InlineData(WorkspaceJob.Optimize)]
    [InlineData(WorkspaceJob.Checkout)]
    [InlineData(WorkspaceJob.Pull)]
    public async Task An_exit_waits_for_a_workspace_job_to_finish(WorkspaceJob job)
    {
        var h = new Harness();
        await StartAsync(h.Vm, job);

        h.Vm.RequestExit();

        Assert.True(h.Vm.ExitPending);
        foreach (var e in EndWithItsChain(job))
        {
            Assert.Equal(0, h.Ready); // iş (ya da zinciri) hâlâ uçuşta
            h.Vm.OnEvent(e);
        }
        Assert.Equal(1, h.Ready);
        Assert.Empty(h.Sent.OfType<StopRunCommand>());
        Assert.Single(h.Sent.OfType<SyncWorkspaceCommand>()); // işin kendi Sync'i ya da zinciri — başkası yok
    }

    // ---------------------------------------------------------------- bekleyiş sonsuz değil

    /// <summary>Motor susarsa (sessizlik bekçisi — <see cref="RunViewModel.EngineSilenceThresholdMs"/>) çıkış drain'i
    /// sonsuza dek beklemez. Eşiğin altı meşru bir drain'dir ve beklenir. Kilit açılmaz: çıkışı serbest bırakan
    /// bekçinin uyarısıdır, motorun cevabı değil.</summary>
    [Fact]
    public void A_silent_engine_does_not_hold_the_exit()
    {
        long now = 1_000;
        var h = new Harness(nowMs: () => now);
        h.StartBuild();
        h.Vm.RequestExit();
        Assert.Equal(AppPhase.Stopping, h.Vm.Phase); // ön-koşul: drain bekleniyor — bekçinin penceresi

        now += RunViewModel.EngineSilenceThresholdMs - 1;
        h.Vm.TickElapsed();
        Assert.Equal(0, h.Ready);

        now += 1;
        h.Vm.TickElapsed();

        Assert.Equal(1, h.Ready);
        Assert.True(h.Vm.IsRunning);
    }

    /// <summary>Bekleyiş sırasında motor ölürse beklenecek bir cevap kalmaz: çıkış serbest kalır.</summary>
    [Fact]
    public void An_engine_that_dies_during_the_wait_releases_the_exit()
    {
        var h = new Harness();
        h.StartBuild();
        h.Vm.RequestExit();
        Assert.Equal(0, h.Ready); // ön-koşul: drain bekleniyor

        h.Vm.OnEngineExited(1);

        Assert.Equal(1, h.Ready);
    }

    // ---------------------------------------------------------------- açılış koreografisi

    /// <summary>[design v1.11.0 §3.1] Açılış koreografisi oynarken koşu komutu henüz GİTMEDİ: çıkış, Stop'un marking
    /// kuralıyla isteği geri alır — motora ne <c>startRun</c> ne <c>stopRun</c> gider ve çıkış hemen hazırdır.
    /// Koreografi sonradan bitse de koşu gönderilmez.</summary>
    [Fact]
    public async Task An_exit_during_the_marking_phase_cancels_the_pending_run()
    {
        var h = new Harness();
        VmTopology.Seed(h.Vm); // [topoloji kapısı] Build'in ön-koşulu
        var choreography = new TaskCompletionSource();
        h.Vm.OperationChoreography = _ => choreography.Task;
        var run = h.Vm.BuildCommand.ExecuteAsync(null);
        Assert.True(h.Vm.IsStarting); // ön-koşul: işlem başladı, komut henüz gitmedi

        h.Vm.RequestExit();

        Assert.Equal(1, h.Ready);
        Assert.Empty(h.Sent);

        choreography.SetResult();
        await run;

        Assert.Empty(h.Sent);
        Assert.Equal(1, h.Ready);
    }

    // ---------------------------------------------------------------- kendiliğinden Sync

    /// <summary>Çıkış beklerken kendiliğinden Sync BAŞLAMAZ: HEAD izleyicisi bırakılır, pencereye dönüş ve HEAD
    /// tetiği hiçbir şey göndermez; koşu bitince bekletilen bir tetik de koşmaz (koordinatör kapandı). Aksi hâlde
    /// drain'in hemen ardından yeni bir iş başlar ve çıkış onu da beklerdi.</summary>
    [Fact]
    public void No_automatic_sync_starts_while_the_exit_waits()
    {
        using var root = FakeHeadWatcher.GitRoot();
        var h = new Harness(root.Path);
        var watcher = new FakeHeadWatcher();
        h.Vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0,
            HeadSha: "1111111111111111111111111111111111111111", ActiveBranch: "main"));
        h.Vm.EnableAutoSync(post => post(), _ => new HeadState("main", "2222222222222222222222222222222222222222"),
            () => watcher);
        Assert.NotNull(watcher.OnSettled); // ön-koşul: izleyici bu köke bağlandı
        h.StartBuild();

        h.Vm.RequestExit();
        h.Vm.OnWindowActivated();
        watcher.OnSettled!(HeadMove.Other);
        h.Vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));
        h.Vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 0, 0, 0, 2, 500));

        Assert.Empty(h.Sent.OfType<SyncWorkspaceCommand>());
        Assert.True(watcher.Disposed);
    }
}
