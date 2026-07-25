using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T12/T43/C2] <see cref="RunViewModel"/>'in C2 omurgası: faz yürüyüşü, seçim/deselect, Sync vs Build/Retry
/// seçim-filtre asimetrisi, Build/RetryFailed'ın workspace argümanlı gönderimi, koşarken kilit (branch/worktree/
/// configuration) + canlı perf, T43 configuration uyarısı, ve A5-review fold'u (engine ölümü Sync fazını bırakır).
/// Kardeş sınıf <see cref="RunViewModelTests"/> ile aynı harness (başlatılmamış EngineHost — <c>OnEvent</c> engine'e
/// dokunmaz; komut gönderimi engine hazır değilken SENKRON fırlar ve VM içinde yutulur). D8: sleep/poll yok.
/// </summary>
public class RunViewModelStateTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static ProjectNode Node(string id, string name, int buildOrder, bool? willBuild = null,
        IReadOnlyList<string>? deps = null, string? layerName = null) =>
        new(id, name, id, ["Osys"], deps ?? [], buildOrder, layerName is null ? null : 0, layerName, false, willBuild);

    // ---------------------------------------------------------------- [Fix wave 1, C2 review Finding 2] Parallelism, varsayılan PerfMode'dan tohumlanır

    [Fact]
    public async Task Fresh_view_model_seeds_parallelism_from_the_default_perf_mode()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        Assert.Equal("Balanced", vm.PerfMode);
        Assert.Equal(4, vm.Parallelism); // ParallelismFor("Balanced") == 4 — Environment.ProcessorCount DEĞİL
    }

    // ---------------------------------------------------------------- faz

    [Fact]
    public async Task Phase_walks_empty_boot_syncing_idle_running_done_from_engine_events()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        Assert.Equal(AppPhase.Empty, vm.Phase);

        vm.RootPath = @"D:\repo";                            // repo seçildi → Boot
        Assert.Equal(AppPhase.Boot, vm.Phase);

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main")); // → Syncing
        Assert.Equal(AppPhase.Syncing, vm.Phase);

        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", 0)], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0)); // → Idle
        Assert.Equal(AppPhase.Idle, vm.Phase);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0)); // → Running
        Assert.Equal(AppPhase.Running, vm.Phase);

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500)); // → Done
        Assert.Equal(AppPhase.Done, vm.Phase);
    }

    [Fact] // [E6/D7 M3] Açılış seed'i = DOĞRUDAN RootPath set (Empty→Boot) — ChangeRepositoryAsync DEĞİL: kayıtlı repo
    // seed edilir, repo BİLİNİR ama hiçbir Sync GÖNDERİLMEZ (seed-but-idle; kullanıcı hazır olunca Sync/Build'e basar).
    // Bu "seed ≠ ChangeRepositoryAsync" pini: ChangeRepositoryAsync bir SyncWorkspaceCommand GÖNDERİRDİ (bkz.
    // SettingsDialogTests.Changing_the_repository_...); doğrudan set HİÇBİR komut göndermez.
    public async Task Seeding_the_root_path_directly_lands_in_boot_without_starting_a_sync()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        IpcCommand? sent = null;
        vm.DebugOnCommandSent = c => sent = c;

        vm.RootPath = @"D:\repo"; // MainWindow'un açılış seed'i (doğrudan set — ChangeRepositoryAsync DEĞİL)

        Assert.Equal(AppPhase.Boot, vm.Phase); // repo bilinir → Boot
        Assert.Null(sent);                     // hiçbir komut/Sync gönderilmedi (seed-but-idle)
        Assert.False(vm.SyncInFlight);         // uçuşta Sync yok
    }

    // ---------------------------------------------------------------- seçim / filtre asimetrisi

    [Fact]
    public async Task Selecting_the_same_project_twice_clears_the_selection()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.SelectProject(@"C:\p\a.csproj");
        Assert.Equal(@"C:\p\a.csproj", vm.SelectedProjectId);

        vm.SelectProject(@"C:\p\a.csproj"); // aynı projeye tekrar → deselect
        Assert.Null(vm.SelectedProjectId);
    }

    [Fact]
    public async Task Sync_clears_the_selection_but_keeps_the_filter()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.SelectProject(@"C:\p\a.csproj");
        vm.ActiveFilter = ProjectFilter.Failed;

        await vm.SyncCommand.ExecuteAsync(null);

        Assert.Null(vm.SelectedProjectId);
        Assert.Equal(ProjectFilter.Failed, vm.ActiveFilter); // filtre KORUNUR
    }

    [Fact]
    public async Task Build_and_retry_clear_both_selection_and_filter()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        vm.SelectProject(@"C:\p\a.csproj");
        vm.ActiveFilter = ProjectFilter.Building;
        await vm.BuildCommand.ExecuteAsync(null);
        Assert.Null(vm.SelectedProjectId);
        Assert.Null(vm.ActiveFilter);

        vm.SelectProject(@"C:\p\b.csproj");
        vm.ActiveFilter = ProjectFilter.Failed;
        await vm.RetryFailedCommand.ExecuteAsync(null);
        Assert.Null(vm.SelectedProjectId);
        Assert.Null(vm.ActiveFilter);
    }

    [Fact]
    public async Task Continue_clears_neither_selection_nor_filter()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.SelectProject(@"C:\p\a.csproj");
        vm.ActiveFilter = ProjectFilter.Failed;
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 0, 0, 0, 1, 10)); // CanContinue=true

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\p\a.csproj", vm.SelectedProjectId);
        Assert.Equal(ProjectFilter.Failed, vm.ActiveFilter);
    }

    // ---------------------------------------------------------------- komut gönderimi (workspace argümanları)

    [Fact]
    public async Task Build_command_sends_RunMode_Build_with_branch_worktree_and_layer_patterns()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var layers = new List<LayerPattern> { new(0, "^Core", "Core") };
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "run-1")
        {
            RootPath = @"D:\repo",
            Configuration = "Release",
            Branch = "feature/x",
            UseWorktree = true,
            WorktreeName = "wt-1",
            LayerPatterns = layers,
        };
        StartRunCommand? sent = null;
        vm.DebugOnCommandSent = c => { if (c is StartRunCommand s) sent = s; };

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.NotNull(sent);
        Assert.Equal(RunMode.Build, sent!.Mode);
        Assert.Equal(@"D:\repo", sent.RootPath);
        Assert.Equal("Release", sent.Configuration);
        Assert.Equal("feature/x", sent.Branch);
        Assert.True(sent.UseWorktree);
        Assert.Equal("wt-1", sent.WorktreeName);
        Assert.Same(layers, sent.LayerPatterns);
    }

    [Fact]
    public async Task Retry_failed_command_sends_RunMode_RetryFailed_and_is_enabled_only_when_a_failure_exists()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "run-1") { RootPath = @"D:\repo" };

        // Henüz failure yok → devre dışı
        vm.OnEvent(new ProjectStartedEvent("r0", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectSucceededEvent("r0", @"C:\p\a.csproj", 100));
        Assert.False(vm.RetryFailedCommand.CanExecute(null));

        // Bir failure ortaya çıkınca etkin
        vm.OnEvent(new ProjectStartedEvent("r0", @"C:\p\b.csproj", "B"));
        vm.OnEvent(new ProjectFailedEvent("r0", @"C:\p\b.csproj", 200, "exit 1"));
        Assert.True(vm.RetryFailedCommand.CanExecute(null));

        StartRunCommand? sent = null;
        vm.DebugOnCommandSent = c => { if (c is StartRunCommand s) sent = s; };
        await vm.RetryFailedCommand.ExecuteAsync(null);
        Assert.Equal(RunMode.RetryFailed, sent!.Mode);
    }

    // ---------------------------------------------------------------- T12 kilit / T43 configuration

    [Fact]
    public async Task Branch_worktree_and_configuration_are_locked_while_running_but_perf_stays_live()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1")
        {
            RootPath = @"D:\repo",
            Configuration = "Debug",
            PerfMode = "Balanced",
        };
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        Assert.True(vm.IsRunning);
        Assert.True(vm.IsMidRunLocked); // branch/worktree/configuration kontrolleri KİLİTLİ

        vm.SetConfiguration("Release"); // koşarken kilitli → no-op
        Assert.Equal("Debug", vm.Configuration);
        Assert.DoesNotContain("all projects will rebuild", vm.GetRunDocumentText());

        vm.CyclePerf(); // perf CANLI kalır ve koşarken not yazar
        Assert.Equal("Light", vm.PerfMode);
        Assert.Equal(2, vm.Parallelism);
        Assert.Contains("parallelism: 2 (Light)", vm.GetRunDocumentText());
    }

    [Fact]
    public async Task Switching_configuration_marks_everything_dirty_and_writes_the_warn_line()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1)], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 2, 0));
        Assert.Equal(AppPhase.Idle, vm.Phase);
        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(@"C:\p\a.csproj", "A", false),
            new BuildPreviewItem(@"C:\p\b.csproj", "B", false),
        ]));
        Assert.All(vm.Projects, p => Assert.False(p.WillBuild)); // başta hepsi clean

        vm.SetConfiguration("Release");

        Assert.Equal("Release", vm.Configuration);
        Assert.All(vm.Projects, p => Assert.True(p.WillBuild)); // her şey dirty
        Assert.Contains("Configuration → Release — all projects will rebuild", vm.GetRunDocumentText());
    }

    [Fact] // [D2 fix wave, Finding 1] OnSyncStarted _willBuildIds'i temizlemeli — aksi halde ikinci Sync bayat "N to build" gösterir.
    public async Task Second_sync_clears_the_stale_will_build_set_from_the_first_sync()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        // Sync 1: A dirty, B clean → wb=1
        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1)], [], [], []));
        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(@"C:\p\a.csproj", "A", true),
            new BuildPreviewItem(@"C:\p\b.csproj", "B", false),
        ]));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1", false, 2, 0));
        Assert.Equal(1, vm.WillBuildCount);
        Assert.False(vm.AllClean);

        // Sync 2: her şey artık clean — bayat "1 to build" YANSIMAMALI
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1)], [], [], []));
        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(@"C:\p\a.csproj", "A", false),
            new BuildPreviewItem(@"C:\p\b.csproj", "B", false),
        ]));
        vm.OnEvent(new SyncCompletedEvent("main", "sha2", false, 2, 0));

        Assert.Equal(0, vm.WillBuildCount); // bayat küme temizlenmiş olmalı
        Assert.True(vm.AllClean);
    }

    [Fact] // [A5-review fold] Engine Sync ORTASINDA ölürse faz Syncing'de asılı kalamaz + _syncInFlight serbest.
    public async Task Engine_death_mid_sync_leaves_the_syncing_phase_and_releases_the_sync_flag()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.Equal(AppPhase.Syncing, vm.Phase);
        Assert.True(vm.SyncInFlight);

        vm.OnEngineExited(1); // engine Sync ortasında öldü

        Assert.NotEqual(AppPhase.Syncing, vm.Phase); // faz Syncing'de ASILI kalmadı
        Assert.Equal(AppPhase.Boot, vm.Phase);       // topoloji hiç gelmedi → Boot
        Assert.False(vm.SyncInFlight);               // uçuştaki Sync serbest bırakıldı
    }

    // ---------------------------------------------------------------- [Fix wave 1, C2 review Finding 1] Sync sırasında Rebuild/RetryFailed engellenir, Build DEĞİL

    [Fact]
    public async Task Rebuild_and_retry_are_blocked_during_sync_but_build_stays_enabled()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        // Failed satır: RetryFailed'ın "failure var" koşulu Sync'ten BAĞIMSIZ sağlansın, yalnız sync guard'ı test edilsin.
        vm.OnEvent(new ProjectStartedEvent("r0", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectFailedEvent("r0", @"C:\p\a.csproj", 100, "exit 1"));
        Assert.True(vm.RetryFailedCommand.CanExecute(null)); // sync öncesi: etkin

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));

        Assert.False(vm.RebuildCommand.CanExecute(null));
        Assert.False(vm.RetryFailedCommand.CanExecute(null));
        Assert.True(vm.BuildCommand.CanExecute(null)); // [design doBuild — kasıtlı asimetri] Build sync sırasında da etkin
    }

    [Fact]
    public async Task Sync_completing_reenables_rebuild_and_retry_and_raises_CanExecuteChanged()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new ProjectStartedEvent("r0", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectFailedEvent("r0", @"C:\p\a.csproj", 100, "exit 1"));
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(vm.RebuildCommand.CanExecute(null));
        Assert.False(vm.RetryFailedCommand.CanExecute(null));

        bool rebuildChanged = false, retryChanged = false;
        vm.RebuildCommand.CanExecuteChanged += (_, _) => rebuildChanged = true;
        vm.RetryFailedCommand.CanExecuteChanged += (_, _) => retryChanged = true;

        // [Not] WorkspaceTopologyEvent kasıtlı olarak GÖNDERİLMEZ: IsRunning false iken satır durumlarını
        // Pending'e resetler (Sync = yeni taban) — bu testin konusu DEĞİL, Failed satırı burada KORUNMALI ki
        // RetryFailedCommand'ın yalnız _syncInFlight guard'ı test edilsin.
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));

        Assert.True(vm.RebuildCommand.CanExecute(null));
        Assert.True(vm.RetryFailedCommand.CanExecute(null));
        Assert.True(rebuildChanged);
        Assert.True(retryChanged);
    }

    [Fact]
    public async Task Engine_death_mid_sync_reenables_rebuild_and_retry_via_release_sync_phase()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new ProjectStartedEvent("r0", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectFailedEvent("r0", @"C:\p\a.csproj", 100, "exit 1"));
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(vm.RebuildCommand.CanExecute(null));
        Assert.False(vm.RetryFailedCommand.CanExecute(null));

        vm.OnEngineExited(1); // engine Sync ortasında öldü → ReleaseSyncPhase

        Assert.True(vm.RebuildCommand.CanExecute(null));
        Assert.True(vm.RetryFailedCommand.CanExecute(null));
    }

    [Fact] // [re-review C2, Finding 4] Sync'e atfedilen planFailed (normal başarısız-sync yolu) da CanExecuteChanged tetiklemeli
    public async Task Sync_attributed_planFailed_reenables_rebuild_and_retry_and_raises_CanExecuteChanged()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new ProjectStartedEvent("r0", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectFailedEvent("r0", @"C:\p\a.csproj", 100, "exit 1"));
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(vm.RebuildCommand.CanExecute(null));
        Assert.False(vm.RetryFailedCommand.CanExecute(null));

        bool rebuildChanged = false, retryChanged = false;
        vm.RebuildCommand.CanExecuteChanged += (_, _) => rebuildChanged = true;
        vm.RetryFailedCommand.CanExecuteChanged += (_, _) => retryChanged = true;

        // IsStarting false (hiç run başlamadı) → TryConsumeSyncFailure normal yola girer: _syncInFlight=false
        // olur ama Fix wave 1'in kaçırdığı 4. geçiş burasıdır — notify BURADA da ateşlenmeli.
        vm.OnEvent(new ErrorEvent("planFailed", "git fetch origin failed"));

        Assert.True(rebuildChanged);
        Assert.True(retryChanged);
        Assert.True(vm.RebuildCommand.CanExecute(null));
        Assert.True(vm.RetryFailedCommand.CanExecute(null));
    }
}
