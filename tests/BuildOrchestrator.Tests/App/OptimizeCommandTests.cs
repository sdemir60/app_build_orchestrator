using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [optimize] <see cref="RunViewModel"/>'in Optimize yüzeyi: komut gönderimi, kapı matrisi (Sync guard'ın
/// birebir simetriği), konsol/stream akışı ve yüzeyin serbest bırakıldığı dört yol (tamamlanma, gönderim
/// düşmesi, tanımlı hata, motor ölümü).
/// <para>Harness <see cref="RunViewModelStateTests"/> ile aynı: başlatılmamış <see cref="EngineHost"/> —
/// <c>OnEvent</c> engine'e dokunmaz, komut gönderimi engine hazır değilken SENKRON düşer ve VM içinde
/// yutulur. D8: sleep/poll yok.</para>
/// </summary>
public class OptimizeCommandTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static ProjectNode Node(string id, string name, int buildOrder, bool inCycle = false) =>
        new(id, name, id, ["Osys"], [], buildOrder, null, null, inCycle, null);

    private static OptimizeCompletedEvent Done(int restored = 1, int unresolved = 2, int prunedState = 3,
        int prunedCache = 4, int prunedSourceHash = 5) =>
        new(ProjectCount: 10, RestoredProjects: restored, FailedRestores: 0, UnresolvedReferences: unresolved,
            StaleObjCleaned: 1, PrunedStateEntries: prunedState, PrunedCacheEntries: prunedCache,
            PrunedSourceHashEntries: prunedSourceHash, RemovedTempFiles: 0, LockedFileCount: 0,
            BytesReclaimed: 2048);

    // ---------------------------------------------------------------- gönderim + konsol

    [Fact]
    public async Task Optimize_sends_optimizeWorkspace_with_the_workspace_root()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        IpcCommand? sent = null;
        vm.DebugOnCommandSent = c => sent = c;

        await vm.OptimizeCommand.ExecuteAsync(null);

        var cmd = Assert.IsType<OptimizeWorkspaceCommand>(sent);
        Assert.Equal(@"D:\repo", cmd.RootPath);
    }

    [Fact] // Optimize kendi transkriptiyle başlar: eski run/Sync tortusu konsolda kalmaz.
    public async Task Optimize_clears_the_console_and_writes_the_requested_line()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new SyncProgressEvent("eski bir transkript satırı", "info"));
        Assert.Contains("eski bir transkript", vm.GetRunDocumentText(), StringComparison.Ordinal);

        await vm.OptimizeCommand.ExecuteAsync(null);

        string text = vm.GetRunDocumentText();
        Assert.DoesNotContain("eski bir transkript", text, StringComparison.Ordinal);
        Assert.Contains(RunViewModel.OptimizeRequestedLine, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Optimize_progress_lines_flow_into_the_run_document()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        vm.OnEvent(new OptimizeStartedEvent(@"D:\repo"));
        vm.OnEvent(new OptimizeProgressEvent("checking NuGet packages — 10 projects, 2 need restore", "info"));

        Assert.Contains("checking NuGet packages", vm.GetRunDocumentText(), StringComparison.Ordinal);
    }

    [Fact] // Stream'e TEK bir bitiş satırı düşer (adım satırları konsolun işidir, stream'in değil).
    public async Task Optimize_completion_pushes_one_stream_summary_line()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        vm.OnEvent(new OptimizeStartedEvent(@"D:\repo"));
        vm.OnEvent(Done(restored: 2, unresolved: 5, prunedState: 3, prunedCache: 4, prunedSourceHash: 5));

        var line = Assert.Single(vm.StreamEvents, s => s.Text.Contains("Optimize", StringComparison.Ordinal));
        Assert.Contains("2 restored", line.Text, StringComparison.Ordinal);
        Assert.Contains("5 unresolved refs", line.Text, StringComparison.Ordinal);
        // [DEĞİŞEN KURAL] Eski iddia "7 entries pruned" idi: toplam İKİ defterden geliyordu. Optimize artık
        // üçüncü deftere de (source-hash-cache) dokunuyor, toplam 3 + 4 + 5'tir. Toplama hâlâ TEK yerde
        // (AppendStreamFor) yapılır — değişen, kaç terim toplandığıdır.
        Assert.Contains("12 entries pruned", line.Text, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- kapı matrisi

    [Fact]
    public async Task Optimize_is_disabled_without_a_workspace()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        Assert.False(vm.OptimizeCommand.CanExecute(null));

        vm.RootPath = @"D:\repo";
        Assert.True(vm.OptimizeCommand.CanExecute(null));
    }

    [Fact] // Optimize diski değiştirir: koşan (ya da başlamakta olan) bir run varken ASLA açılmaz.
    public async Task Optimize_is_disabled_while_a_run_is_starting_or_running()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        Assert.False(vm.OptimizeCommand.CanExecute(null));

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500));
        Assert.True(vm.OptimizeCommand.CanExecute(null));
    }

    [Fact] // Sync uçuştayken Optimize beklemelidir: ikisi de aynı workspace'i okur/yazar.
    public async Task Optimize_is_disabled_while_a_sync_is_in_flight()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(vm.OptimizeCommand.CanExecute(null));

        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", 0)], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));
        Assert.True(vm.OptimizeCommand.CanExecute(null));
    }

    /// <summary>
    /// Motor ERİŞİLEMEZ (yeniden başlatılamaz — eksik/bozuk kurulum) ise komut kapanır. Ölü ama YENİDEN
    /// BAŞLATILABİLİR bir motorda kapı AÇIK kalır: <c>CanSync</c> ile aynı kural — kullanıcı "Restart engine"
    /// dedikten sonra düğmeye basabilmelidir.
    /// </summary>
    [Fact]
    public async Task Optimize_is_disabled_only_when_the_engine_cannot_be_restarted()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        vm.OnEngineExited(1);
        Assert.True(vm.OptimizeCommand.CanExecute(null)); // restartable → kapı açık (Sync'in kuralı)

        vm.OnEngineUnavailable(@"C:\app\supervisor\BuildOrchestrator.Supervisor.exe");
        Assert.False(vm.OptimizeCommand.CanExecute(null));
    }

    /// <summary>
    /// Optimize uçuştayken TÜM run komutları ve Sync kapanır — istek penceresi (gönderimden önce kurulan
    /// bayrak) DAHİL. Tamamlanma hepsini tek yerden geri açar.
    /// </summary>
    [Fact]
    public async Task Sync_build_rebuild_and_cycles_are_disabled_while_an_optimize_is_in_flight_and_reopen_on_completion()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(@"C:\p\a.csproj", "A", 0, inCycle: true), Node(@"C:\p\b.csproj", "B", 1, inCycle: true)],
            [[@"C:\p\a.csproj", @"C:\p\b.csproj"]], [], []));
        Assert.True(vm.BuildCyclesCommand.CanExecute(null));

        // İSTEK penceresi: motor henüz optimizeStarted ile cevap vermedi ama kapılar çoktan kapalı.
        vm.DebugOnCommandSent = _ =>
        {
            Assert.True(vm.OptimizeRequested);
            Assert.False(vm.SyncCommand.CanExecute(null));
            Assert.False(vm.BuildCommand.CanExecute(null));
            Assert.False(vm.RebuildCommand.CanExecute(null));
            Assert.False(vm.BuildCyclesCommand.CanExecute(null));
        };
        await vm.OptimizeCommand.ExecuteAsync(null);
        vm.DebugOnCommandSent = null;

        // Uçuş penceresi
        vm.OnEvent(new OptimizeStartedEvent(@"D:\repo"));
        Assert.False(vm.OptimizeRequested); // nöbeti devretti
        Assert.False(vm.SyncCommand.CanExecute(null));
        Assert.False(vm.RebuildCommand.CanExecute(null));
        Assert.False(vm.OptimizeCommand.CanExecute(null)); // ikinci Optimize da anlamsız

        vm.OnEvent(Done());

        Assert.True(vm.SyncCommand.CanExecute(null));
        Assert.True(vm.BuildCommand.CanExecute(null));
        Assert.True(vm.RebuildCommand.CanExecute(null));
        Assert.True(vm.BuildCyclesCommand.CanExecute(null));
        Assert.True(vm.OptimizeCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- yüzeyin bırakıldığı yollar

    [Fact] // Gönderim SENKRON düştüyse hiçbir optimizeStarted gelmeyecek — kapı burada açılmazsa kalıcı pasif kalırdı.
    public async Task A_failed_send_reopens_the_optimize_gate()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // hiç başlatılmadı → gönderim düşer
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        bool? requestedAtSendTime = null;
        vm.DebugOnCommandSent = c => { if (c is OptimizeWorkspaceCommand) requestedAtSendTime = vm.OptimizeRequested; };

        await vm.OptimizeCommand.ExecuteAsync(null);

        Assert.True(requestedAtSendTime);  // kapı gönderimden ÖNCE kapandı
        Assert.False(vm.OptimizeRequested);
        Assert.True(vm.OptimizeCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("optimizeFailed")]
    [InlineData("optimizeRejected")]
    public async Task An_optimizeFailed_or_optimizeRejected_error_releases_the_optimize_surface(string code)
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new OptimizeStartedEvent(@"D:\repo"));
        Assert.False(vm.OptimizeCommand.CanExecute(null));

        vm.OnEvent(new ErrorEvent(code, "something went wrong"));

        Assert.True(vm.OptimizeCommand.CanExecute(null));
    }

    /// <summary>
    /// Yarış: kullanıcı Optimize'a bir run BAŞLARKEN basar. Motor <c>optimizeRejected</c> döner — bu, koşan
    /// run'ı YIKMAMALIDIR (aksi halde Stop erişilemez olur ve sonraki run event'leri yıkılmış bir state'e düşer).
    /// </summary>
    [Fact]
    public async Task An_optimizeRejected_during_a_live_run_leaves_the_run_state_untouched()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        Assert.True(vm.IsRunning);

        vm.OnEvent(new ErrorEvent("optimizeRejected", "a run is in flight — stop it before optimizing"));

        Assert.True(vm.IsRunning);
        Assert.Equal(AppPhase.Running, vm.Phase);
    }

    [Fact] // Motor mid-optimize ölürse yüzey sızmaz (Sync'in Engine_death_mid_sync ikizi).
    public async Task Engine_death_mid_optimize_releases_the_optimize_surface()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new OptimizeStartedEvent(@"D:\repo"));
        Assert.False(vm.OptimizeCommand.CanExecute(null));

        vm.OnEngineExited(1); // ne optimizeCompleted ne tanımlı bir hata gelecek — yüzey burada bırakılmalı

        Assert.False(vm.OptimizeInFlight);
        Assert.True(vm.OptimizeCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- v1.13.2 §9 · pill · çift kapı

    [Fact] // [design v1.13.2 §9] Konsol gibi STREAM de her işlemde temizlenir — Clean ikizinin pini.
    public async Task Optimize_clears_the_stream_left_over_from_the_previous_operation()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new SyncCompletedEvent("main", "sha", false, ProjectCount: 10, CycleCount: 0,
            ToBuildCount: 3, UpToDateCount: 7));
        Assert.NotEmpty(vm.StreamEvents);

        await vm.OptimizeCommand.ExecuteAsync(null);

        Assert.Empty(vm.StreamEvents);
    }

    [Fact] // [design v1.11.0 §2.2] İşlem pill'i TIKLAMA anında yazılır, motorun cevabı beklenmez.
    public async Task Optimize_writes_the_operation_pill_at_click_time()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        await vm.OptimizeCommand.ExecuteAsync(null);

        Assert.Equal(OperationLabel.Optimize, vm.CurrentOperation);
    }

    /// <summary>
    /// Karşılıklı dışlamanın CLEAN ayağı. Optimize'ın kapısı Clean'i gördüğü gibi Clean'in kapısı da
    /// Optimize'ı görmelidir — yoksa iki bakım işi aynı workspace'te aynı anda koşabilirdi (Clean <c>obj</c>'i
    /// silerken Optimize aynı <c>obj</c>'e restore yazardı). Pencere İSTEK anında başlar, motorun cevabında değil.
    /// </summary>
    [Fact]
    public async Task Clean_is_disabled_while_an_optimize_is_in_flight_and_reopens_on_completion()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        Assert.True(vm.CleanCommand.CanExecute(null));

        // İSTEK penceresi: motor henüz optimizeStarted ile cevap vermedi ama Clean çoktan kapalı.
        bool observed = false;
        vm.DebugOnCommandSent = _ =>
        {
            observed = true;
            Assert.True(vm.OptimizeRequested);
            Assert.False(vm.CleanCommand.CanExecute(null));
        };
        await vm.OptimizeCommand.ExecuteAsync(null);
        Assert.True(observed);

        vm.OnEvent(new OptimizeStartedEvent(@"D:\repo"));
        Assert.False(vm.CleanCommand.CanExecute(null));

        vm.OnEvent(Done());
        Assert.True(vm.CleanCommand.CanExecute(null));
    }

    [Fact] // Simetrinin öteki ayağı: uçuştaki bir Clean Optimize'ı da kapatır.
    public async Task Optimize_is_disabled_while_a_clean_is_in_flight_and_reopens_on_completion()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        Assert.True(vm.OptimizeCommand.CanExecute(null));

        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));
        Assert.False(vm.OptimizeCommand.CanExecute(null));

        vm.OnEvent(new CleanCompletedEvent(1, 2, 3, 0, 4));
        Assert.True(vm.OptimizeCommand.CanExecute(null));
    }

    [Fact] // Uçuştaki bir Optimize alt bardaki N behind chip'ini de kilitler (bakım kilidi tek kapıdır).
    public async Task The_pull_chip_is_disabled_while_an_optimize_is_in_flight()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo", Behind = 3 };
        Assert.True(vm.PullRepositoryCommand.CanExecute(null));

        vm.OnEvent(new OptimizeStartedEvent(@"D:\repo"));
        Assert.False(vm.PullRepositoryCommand.CanExecute(null));

        vm.OnEvent(Done());
        Assert.True(vm.PullRepositoryCommand.CanExecute(null));
    }
}
