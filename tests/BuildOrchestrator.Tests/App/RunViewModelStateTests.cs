using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.ProcessControl;
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
        IReadOnlyList<string>? deps = null, string? layerName = null, bool inCycle = false) =>
        new(id, name, id, ["Osys"], deps ?? [], buildOrder, layerName is null ? null : 0, layerName, inCycle, willBuild);

    // ---------------------------------------------------------------- [Fix wave 1, C2 review Finding 2] Parallelism, varsayılan PerfMode'dan tohumlanır

    [Fact]
    public async Task Fresh_view_model_seeds_parallelism_from_the_default_perf_mode()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        Assert.Equal("Balanced", vm.PerfMode);
        Assert.Equal(4, vm.Parallelism); // PerfProfile.For(Balanced).Parallelism == 4 — Environment.ProcessorCount DEĞİL
    }

    // [T20-b] Tek doğruluk kaynağı artık Core'un PerfProfile'ıdır (App'in kendi ParallelismFor tablosu KALDIRILDI).
    // İki tablonun ASİMETRİSİ bilerek korunur: PerfProfile.TryParse tanınmayan metinde null döner, App'in eski
    // tablosu ise 4'e düşerdi (`_ => 4`) — birleşmede ESKİ davranış kazanır, aksi halde bayat bir UiState değeri
    // paralelliği tanımsız bırakırdı. Bu dal yalnız savunma amaçlıdır (public yollar hep geçerli metin verir),
    // bu yüzden doğrudan pinlenir.
    [Fact]
    public void An_unrecognised_perf_mode_falls_back_to_the_balanced_row()
    {
        Assert.Equal(PerfProfile.For(PerfMode.Balanced), RunViewModel.ProfileFor("Turbo"));
        Assert.Equal(PerfProfile.For(PerfMode.Light), RunViewModel.ProfileFor("Light"));
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

    // ---------------------------------------------------------------- [planlama görünürlüğü] Starting fazı
    //
    // Build'e basmakla runStarted arasında motor planlamayı koşar (177 projelik OSYS'te saniyeler). O pencerede
    // App HİÇBİR ŞEY göstermiyordu: BeginRunAsync konsolu TEMİZLİYOR, Phase'e dokunmuyordu — şerit önceki
    // metinde ("▸ Stopped — …" / "▸ Ready — …") donuyor, konsol bomboş kalıyordu. Kullanıcının bildirdiği
    // "tekrar build dedim, ui'da bir şey olmadı" cümlesinin yarısı buydu (diğer yarısı motorun donmasıydı).
    //
    // Faz adı Planning DEĞİL Starting: RetryFailed planner'ı hiç çağırmaz (aynı plandan devam eder) ama o
    // pencerede de tıklamanın kaydedildiği görünmelidir — "starting" her modda DOĞRU, "planning" değil.

    [Fact]
    public async Task Pressing_build_enters_the_starting_phase_and_notes_the_request_in_the_console()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", 0)], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));
        Assert.Equal(AppPhase.Idle, vm.Phase); // ön-koşul

        // Faz GÖNDERİM ANINDA okunur: bu harness'ta engine başlatılmamıştır, gönderim senkron fırlar ve
        // aşağıdaki "gönderim başarısız → geri al" dalı fazı hemen dinlenmeye çeker.
        AppPhase phaseAtSend = AppPhase.Empty;
        vm.DebugOnCommandSent = _ => phaseAtSend = vm.Phase;

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.Equal(AppPhase.Starting, phaseAtSend);
        Assert.Contains("build requested", vm.GetRunDocumentText(), StringComparison.Ordinal);
    }

    /// <summary>Gönderim SENKRON fırlarsa (motor hiç doğmadı/öldü) hiçbir engine event'i gelmeyecektir: faz
    /// <see cref="AppPhase.Starting"/>'te asılı kalırsa şerit sonsuza dek "Starting" der. <c>StopAsync</c>'in
    /// "gönderim başarısız → fazı geri al" deseninin ikizi.
    /// <para>Geri dönülen faz <b>ÖNCEKİ</b> fazdır, <c>RestingPhase</c> değil: hiçbir şey OLMADI, dolayısıyla
    /// ekran tam olarak tıklamadan önceki hâline dönmelidir. İki test bunu iki farklı tabandan sürer —
    /// Sync'lenmiş (Idle) ve Sync'lenmemiş (Boot) — çünkü <c>RestingPhase</c> ikisini de üretebilirdi.</para></summary>
    [Theory]
    [InlineData(false, AppPhase.Boot)] // repo seçili ama hiç Sync yok
    [InlineData(true, AppPhase.Idle)]  // Sync bitmiş, durumlar biliniyor
    public async Task A_failed_send_takes_the_phase_back_to_where_it_was(bool synced, AppPhase expected)
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", 0)], [], [], []));
        if (synced) vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));
        Assert.Equal(expected, vm.Phase); // ön-koşul

        await vm.BuildCommand.ExecuteAsync(null); // engine başlatılmadı → SendAsync senkron fırlar

        Assert.Equal(expected, vm.Phase);
        Assert.False(vm.IsStarting);
    }

    [Fact] // normal akış: motor planlamayı bitirdi ve run'ı açtı
    public async Task RunStarted_takes_the_phase_out_of_starting()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.Phase = AppPhase.Starting;

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));

        Assert.Equal(AppPhase.Running, vm.Phase);
    }

    [Fact] // planlama düştü (planFailed/msbuildNotFound) — runStarted da runCompleted da GELMEYECEK
    public async Task A_run_ending_error_during_starting_leaves_the_phase_for_the_resting_phase()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", 0)], [], [], []));
        vm.Phase = AppPhase.Starting;

        vm.OnEvent(new ErrorEvent("planFailed", "workspace root not found"));

        Assert.Equal(AppPhase.Idle, vm.Phase);
    }

    /// <summary>Motor planlama penceresinde öldü. Faz <see cref="AppPhase.Stopped"/>'a DEĞİL dinlenme fazına
    /// düşer: hiçbir proje derlenmedi, "▸ Stopped — 0/0 · 0 not built" olmayan bir koşuyu anlatırdı. (Şerit
    /// zaten engine-died önceliğiyle kırmızı metni gösterir — bu, o metin temizlendikten sonraki dürüst
    /// taban durumdur.)</summary>
    [Fact]
    public async Task An_engine_death_during_starting_settles_the_phase_on_the_resting_phase()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", 0)], [], [], []));
        vm.Phase = AppPhase.Starting;

        vm.OnEngineExited(139);

        Assert.Equal(AppPhase.Idle, vm.Phase);
        Assert.False(vm.IsStarting);
    }

    /// <summary>Motorun planlama adımları konsola AKAR — pencere artık tek satırlık bir "requested" notundan
    /// ibaret değil, ilerlemeyi gösterir. Kanal <c>syncProgress</c>'ten AYRIdır ve Sync yüzeyine dokunmaz:
    /// <c>SyncInFlight</c> bu satırlarla açılmamalıdır (açılsaydı Rebuild/RetryFailed sessizce kilitlenirdi).</summary>
    [Fact]
    public async Task Plan_progress_lines_reach_the_console_without_touching_the_sync_surface()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.Phase = AppPhase.Starting;

        vm.OnEvent(new PlanProgressEvent("Scanning solutions (12)"));
        vm.OnEvent(new PlanProgressEvent("Build order resolved (177)"));

        string text = vm.GetRunDocumentText();
        Assert.Contains("Scanning solutions (12)", text, StringComparison.Ordinal);
        Assert.Contains("Build order resolved (177)", text, StringComparison.Ordinal);
        Assert.Equal(AppPhase.Starting, vm.Phase); // satırlar fazı DEĞİŞTİRMEZ
        Assert.False(vm.SyncInFlight);
    }

    // ---------------------------------------------------------------- [Stopping] fazdan ÇIKIŞ garantileri
    //
    // Bu dört test "Stopping'e nasıl girildiği"ni değil, "Stopping'ten HER YOLDAN çıkıldığı"nı sürer:
    // girişin üretim yolu (StopCommand → gerçek Supervisor'a gönderim) kardeş sınıf RunViewModelTests'te
    // pinlidir ve burada tekrarlanması testlere dört process başlatmaktan başka bir şey katmaz. Çıkışı
    // sürmek KRİTİKTİR: bir yol açık kalırsa buton sonsuza dek pasif, şerit sonsuza dek "Stopping" kalır —
    // yani "stop çalışmıyor" kusurunun daha kötü bir biçimi.

    [Fact] // normal akış: uçuştaki child'lar bitti → engine run'ı kapattı
    public async Task RunCompleted_takes_the_phase_out_of_stopping_and_offers_continue()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.Phase = AppPhase.Stopping;

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 0, 0, 0, 1, 10));

        Assert.Equal(AppPhase.Stopped, vm.Phase);
    }

    /// <summary>[B2] <c>runStopped</c> TEK DALLIDIR: faz <see cref="AppPhase.Stopped"/>, run state serbest —
    /// runStarted görülmüş olsun ya da olmasın.
    /// <para><b>Eski iddia (değişti):</b> runStarted görülmüşse <c>OnRunStopped</c> ERKEN DÖNERDİ ("runCompleted
    /// az sonra gelecek, faz orada yazılır"), görülmemişse dinlenme fazına düşerdi. Bu, fazın çözülmesini bir
    /// OLAY SIRALAMASI varsayımına bağlıyordu ve kullanıcı "Stop dedim, Stopping'te kaldı" durumunu bildirdi.
    /// <b>Değişme gerekçesi:</b> koordinatör <c>runStopped</c>'ı zaten TÜM in-flight sonuçlarını raporladıktan
    /// sonra yazar (<c>RunSegmentAsync</c>'in finally'si, <c>_finishing</c> kapısı) — yani bu olay görüldüğünde
    /// koşan bir şey KALMAMIŞTIR. Tek dallı kural, fazın asılı kalma ihtimalini varsayıma değil YAPIYA bağlar;
    /// arkadan gelen <c>runCompleted</c> aynı fazı yazdığı için ara bir görüntü de oluşmaz.</para></summary>
    [Fact]
    public async Task RunStopped_always_settles_the_phase_whether_or_not_the_run_had_started()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", 0)], [], [], []));

        // (a) planlama sırasında stop — runStarted HİÇ gelmedi
        vm.Phase = AppPhase.Stopping;
        vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));
        Assert.Equal(AppPhase.Stopped, vm.Phase);
        Assert.False(vm.IsStarting);

        // (b) koşan run durduruldu — runStarted GÖRÜLDÜ, runCompleted henüz gelmedi
        vm.OnEvent(new RunStartedEvent("r2", RunMode.Build, 1, 1, "Debug", 0));
        vm.Phase = AppPhase.Stopping;
        vm.OnEvent(new RunStoppedEvent("r2", WasHard: false));
        Assert.Equal(AppPhase.Stopped, vm.Phase);
        Assert.False(vm.IsRunning);
    }

    [Fact] // run-bitiren hata Stopping penceresinde geldi (ör. msbuildNotFound) — runCompleted gelmeyecek
    public async Task A_run_ending_error_during_stopping_leaves_the_phase_for_the_resting_phase()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.Phase = AppPhase.Stopping; // topoloji hiç gelmedi → dinlenme fazı Boot

        vm.OnEvent(new ErrorEvent("msbuildNotFound", "MSBuild.exe bulunamadı"));

        Assert.Equal(AppPhase.Boot, vm.Phase);
    }

    [Fact] // engine Stopping penceresinde öldü: hiçbir IPC event'i gelmeyecek
    public async Task An_engine_death_during_stopping_settles_the_phase_on_stopped()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.Phase = AppPhase.Stopping;

        vm.OnEngineExited(139);

        Assert.Equal(AppPhase.Stopped, vm.Phase);
        Assert.False(vm.IsRunning);
    }

    // ---------------------------------------------------------------- [runFailed] koşarken düşen run görünür olur
    //
    // runFailed, run'ın TAMAMINI saran dış catch'ten gelir (RunCoordinator.ExecuteRunAsync) — yani runStarted'dan
    // SONRA da gelebilir ve o yolda runCompleted ASLA yazılmaz. Kümedeki diğer üç kod (planFailed/msbuildNotFound
    // planlama penceresinde, noResumableRun bir Continue reddinde) faz Running iken gelemez.

    [Fact]
    public async Task A_run_that_fails_mid_flight_surfaces_the_reason_and_leaves_the_running_phase()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", 0)], [], [], []));
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        Assert.Equal(AppPhase.Running, vm.Phase); // ön-koşul

        vm.OnEvent(new ErrorEvent("runFailed", "access to the log file was denied"));

        Assert.Equal("access to the log file was denied", vm.RunErrorMessage); // şerit KIRMIZI gerekçeyi gösterir
        Assert.Equal(AppPhase.Idle, vm.Phase);   // donmuş "Building 3/10" faz-metni kalmaz
        Assert.False(vm.IsRunning);
    }

    // noResumableRun bir REDdir, bir başarısızlık değil (RunCoordinator onu `rejection` diye adlandırır):
    // Continue'ya basıldığında sürdürülecek run yoktur. Kırmızı kalıcı bir "Run failed" satırı burada hem
    // yanlış olurdu hem de "▸ Stopped — 3/10 · rest queued" gibi HÂLÂ doğru olan faz-metnini kalıcı ezerdi.
    [Fact]
    public async Task A_rejected_continue_does_not_paint_the_ribbon_red()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 0, 0, 0, 1, 10)); // → Stopped

        vm.OnEvent(new ErrorEvent("noResumableRun", "No resumable run for 'D:\\repo'."));

        Assert.Null(vm.RunErrorMessage);
        Assert.Equal(AppPhase.Stopped, vm.Phase); // faz-metni korunur
    }

    // Kalıcılık kuralı SyncErrorMessage'ın ikizi: metin, kullanıcı YENİ bir şey başlatana kadar durur.
    [Fact]
    public async Task The_run_failure_text_is_cleared_by_the_next_run_and_by_the_next_sync()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new ErrorEvent("runFailed", "boom"));
        Assert.Equal("boom", vm.RunErrorMessage);

        vm.OnEvent(new RunStartedEvent("r2", RunMode.Build, 1, 1, "Debug", 0)); // yeni run başladı
        Assert.Null(vm.RunErrorMessage);

        vm.OnEvent(new ErrorEvent("runFailed", "boom again"));
        Assert.Equal("boom again", vm.RunErrorMessage);

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main")); // Sync başladı — şerit Sync ilerlemesini göstermeli
        Assert.Null(vm.RunErrorMessage);
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
            UseWorktree = true,
            LayerPatterns = layers,
        };
        // [T2 fix-1 · C1/I4] Branch ARTIK doğrudan atanamaz: StartRunCommand.Branch bir NİYETtir ve yalnız
        // kullanıcının AÇIK seçimi oraya gider (bkz. RunViewModel.RunBranchIntent). Doğrudan atama bir
        // görüntüleme/seed değeridir ve komuta GİTMEZ — bu testin konusu komutun ALANLARININ doğru
        // taşındığı olduğundan, branch de üretimdeki gerçek yoldan (popover seçimi) kurulur.
        vm.OnEvent(new BranchListEvent([
            new BranchRef("main", "aaaaaaaaaaaa", true, false),
            new BranchRef("feature/x", "bbbbbbbccccc", false, false),
        ]));
        vm.SelectBranch(new BranchRef("feature/x", "bbbbbbbccccc", false, false));
        vm.WorktreeName = "wt-1"; // SelectBranch hedefi auto'ya (null) döndürür → seçimden SONRA verilir
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

    /// <summary>[cycles] <b>Cycles</b> düğmesi KENDİ modunu gönderir ve YALNIZ elde döngü varken etkindir.
    /// Üç iddia tek testte, çünkü üçü aynı kararın parçalarıdır:
    /// (a) topoloji hiç döngü taşımıyorken komut PASİF — o koşu döngüsüz bir workspace'te her projeyi kapsam
    ///     dışı sayıp atlar, yani hiçbir şey yapmaz; kullanıcı bunu tıklamadan ÖNCE görmelidir;
    /// (b) döngü GELİNCE etkinleşir — kapı canlıdır, ilk topolojide donmuş kalmaz;
    /// (c) gönderilen komut <see cref="RunMode.Cycles"/> taşır — Build'in modunu DEĞİL.
    /// <para>(a) non-vacuous'tur: aynı VM'de <see cref="RunViewModel.BuildCommand"/> o anda ETKİNdir, yani
    /// pasiflik ortak run kapısından (topoloji/motor/mid-run) değil, DÖNGÜ kapısından gelir.</para></summary>
    [Fact]
    public async Task The_cycles_command_needs_a_cycle_and_sends_RunMode_Cycles()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "run-1") { RootPath = @"D:\repo" };
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        vm.OnEvent(new WorkspaceTopologyEvent([Node(D, "D", 0)], [], [], []));
        Assert.True(vm.BuildCommand.CanExecute(null));         // ortak run kapısı AÇIK
        Assert.False(vm.BuildCyclesCommand.CanExecute(null));  // (a) ama döngü YOK

        vm.OnEvent(CycleTopology());
        Assert.True(vm.BuildCyclesCommand.CanExecute(null));   // (b)

        await vm.BuildCyclesCommand.ExecuteAsync(null);
        Assert.Equal(RunMode.Cycles, Assert.Single(sent.OfType<StartRunCommand>()).Mode); // (c)
    }

    [Fact]
    public async Task Retry_failed_command_sends_RunMode_RetryFailed_and_is_enabled_only_when_a_failure_exists()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "run-1") { RootPath = @"D:\repo" };
        // [topoloji kapısı] Konu "failure var mı" koşulu — kapının kendisi DEĞİL; ön-koşul satır event'lerinden
        // ÖNCE kurulur (topoloji, kendinde olmayan satırları budar).
        VmTopology.Seed(vm, @"C:\p\a.csproj", @"C:\p\b.csproj");

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

        await vm.CyclePerfAsync(); // perf CANLI kalır ve koşarken K11 notunu yazar
        Assert.Equal("Light", vm.PerfMode);
        Assert.Equal(2, vm.Parallelism);
        Assert.Contains("parallelism: 2 · cpu cap 40%", vm.GetRunDocumentText());
    }

    // ---------------------------------------------------------------- [T20-b/K11] canlı perf: cap + priority

    /// <summary>
    /// [T20-b/K11] Koşarken perf değişimi ARTIK koşan run'a ulaşır: <see cref="SetPerfModeCommand"/> gönderilir
    /// (eskiden yalnız konsola not yazılırdı, motora SIFIR etkisi vardı). Konsola yazılan TEK satır K11'in
    /// kendi kopyasıdır; "paralellik bir sonraki run'da geçerli olur" semantiği koda (XML-doc) ve README'ye
    /// yazılır, her chip tıklamasında konsolda TEKRARLANMAZ — bu yüzden test o ikinci satırın YOKLUĞUNU da
    /// pinler (aksi halde design-v1'in sakin konsol dili sessizce bozulur).
    /// </summary>
    [Fact]
    public async Task Perf_change_while_running_sends_setPerfMode_and_writes_the_k11_note()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { PerfMode = "Balanced" };
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0, CpuCapPercent: 70));
        Assert.True(vm.IsRunning);

        var sent = new List<SetPerfModeCommand>();
        vm.DebugOnCommandSent = c => { if (c is SetPerfModeCommand s) sent.Add(s); };

        await vm.CyclePerfAsync(); // Balanced → Light
        await vm.CyclePerfAsync(); // Light → Full (cap YOK)

        Assert.Equal(["Light", "Full"], sent.Select(s => s.PerfMode));
        string text = vm.GetRunDocumentText();
        Assert.Contains("parallelism: 2 · cpu cap 40%", text);
        Assert.Contains("parallelism: 6 · cpu cap off", text);
        // K11 kopyası TEK satırdır: prototipte her chip tıklamasında tekrarlanan açıklayıcı cümle YOKTUR
        // (canlı-değişim semantiği XML-doc + README'de anlatılır, konsolda değil).
        Assert.DoesNotContain("applies to the next run", text);
    }

    // ---------------------------------------------------------------- [A13/T3a · a10/a11] K11 notunun Balanced varyantı + damgası

    /// <summary>
    /// [A13/T3a · a10/a11] Yukarıdaki test yalnız Light/Full varyantlarını pinliyordu (<c>PerfNoteText.cs:35</c>
    /// — Balanced testsizdi). Aynı satırın <c>HH:mm:ss</c> önekini (<see cref="RunViewModel.WallClock"/> ile
    /// deterministik) de BİRLİKTE pinler — <c>ComposeNarrativeLine</c>'ın gerçekten K11 notuna da uygulandığının
    /// kanıtı (damga başka bir satırda ayrıca doğrulanıyordu, bu notta değil).
    /// </summary>
    [Fact]
    public async Task Perf_change_while_running_writes_the_balanced_variant_with_its_hh_mm_ss_stamp()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1")
        {
            PerfMode = "Balanced",
            WallClock = () => new DateTimeOffset(2026, 7, 23, 12, 4, 7, TimeSpan.Zero),
        };
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0, CpuCapPercent: 70));
        Assert.True(vm.IsRunning);

        await vm.CyclePerfAsync(); // Balanced → Light
        await vm.CyclePerfAsync(); // Light → Full
        await vm.CyclePerfAsync(); // Full → Balanced (tam döngü)

        Assert.Equal("Balanced", vm.PerfMode);
        Assert.Contains("12:04:07 parallelism: 4 · cpu cap 70%", vm.GetRunDocumentText());
    }

    // [Fix round 1 — KÖK 1] Planlama penceresi: Build'e basıldı, startRun gönderildi, ama runStarted HENÜZ
    // gelmedi (177 projede SANİYELER). Perf chip'i o pencerede canlıdır (IsMidRunLocked kilidi onu kapsamaz) —
    // değişim SESSİZCE KAYBOLMAMALI: komut gitmeli ve not yazılmalı. Kapı IsRunning DEĞİL IsMidRunLocked'dır.
    [Fact]
    public async Task Perf_change_during_the_planning_window_still_reaches_the_engine()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { PerfMode = "Balanced", IsStarting = true };
        Assert.False(vm.IsRunning);       // runStarted gelmedi
        Assert.True(vm.IsMidRunLocked);   // ama run UÇUŞTA

        var sent = new List<SetPerfModeCommand>();
        vm.DebugOnCommandSent = c => { if (c is SetPerfModeCommand s) sent.Add(s); };

        await vm.CyclePerfAsync();

        Assert.Equal("Light", Assert.Single(sent).PerfMode);
        Assert.Contains("parallelism: 2 · cpu cap 40%", vm.GetRunDocumentText());
    }

    // Koşmuyorken: tablo güncellenir ama NE komut gönderilir NE de konsola not yazılır — profil zaten bir
    // sonraki startRun'ın PerfMode alanıyla gidecektir (koşmayan bir job'a cap uygulamanın sahibi yoktur).
    [Fact]
    public async Task Perf_change_while_idle_updates_the_table_without_ipc_or_a_console_note()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { PerfMode = "Balanced" };
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.CyclePerfAsync();

        Assert.Equal("Light", vm.PerfMode);
        Assert.Equal(2, vm.Parallelism);
        Assert.Empty(sent);
        Assert.DoesNotContain("cpu cap", vm.GetRunDocumentText());
    }

    // Run başlatma komutu perf profilinin ADINI da taşır: cap/priority Supervisor'da BU alandan çözülür
    // (paralellik ayrı alandır — App ile Supervisor aynı tablodan aynı satırı okur).
    [Fact]
    public async Task A_started_run_carries_the_perf_mode_name_alongside_parallelism()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo", PerfMode = "Light" };
        vm.SetPerfMode("Light"); // seed yolu: PerfMode + Parallelism birlikte

        StartRunCommand? sent = null;
        vm.DebugOnCommandSent = c => { if (c is StartRunCommand s) sent = s; };
        await vm.BuildCommand.ExecuteAsync(null);

        Assert.Equal("Light", sent!.PerfMode);
        Assert.Equal(2, sent.Parallelism);
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
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
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
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        vm.OnEvent(new ProjectStartedEvent("r0", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectFailedEvent("r0", @"C:\p\a.csproj", 100, "exit 1"));
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(vm.RebuildCommand.CanExecute(null));
        Assert.False(vm.RetryFailedCommand.CanExecute(null));

        bool rebuildChanged = false, retryChanged = false;
        vm.RebuildCommand.CanExecuteChanged += (_, _) => rebuildChanged = true;
        vm.RetryFailedCommand.CanExecuteChanged += (_, _) => retryChanged = true;

        // [Not] Bu Sync'in İÇİNDE ayrıca bir WorkspaceTopologyEvent GÖNDERİLMEZ: IsRunning false iken satır
        // durumlarını Pending'e resetler (Sync = yeni taban) — bu testin konusu DEĞİL, Failed satırı burada
        // KORUNMALI ki RetryFailedCommand'ın yalnız _syncInFlight guard'ı test edilsin. Baştaki VmTopology.Seed
        // satır event'lerinden ÖNCE koştuğu için bu kısıtı bozmaz.
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
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
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
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
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

    // ---------------------------------------------------------------- [topoloji kapısı] Sync yapılmadan run başlatılamaz

    // Ölçülen kusur: uygulama kayıtlı bir repo ile açılıp (Boot) Sync'e hiç basılmadan Build'e basıldığında motor
    // GERÇEKTEN derlemeye başlıyordu, ama App'e hiç `workspaceTopology` gelmediği için (onu YALNIZ Sync yayınlar —
    // run yalnız `buildPreview` yayınlar) liste/graf/sayaçlar BOŞ kalıyordu: kullanıcı ne derlendiğini göremeden
    // koşan bir run'a bakıyordu. Kapı topolojinin VARLIĞIDIR (Sync'in tek gözlemlenebilir ürünü).
    [Fact]
    public async Task Build_is_disabled_until_a_topology_arrives()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        Assert.Equal(AppPhase.Boot, vm.Phase);
        Assert.False(vm.BuildCommand.CanExecute(null));   // Sync yapılmadı → kör run başlatılamaz
        Assert.False(vm.RebuildCommand.CanExecute(null));
        Assert.True(vm.SyncCommand.CanExecute(null));     // çıkış yolu AÇIK kalır

        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", 0)], [], [], []));

        Assert.True(vm.BuildCommand.CanExecute(null));
        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    // Sync KOŞTU ama klasörün altında hiç proje yok: liste boş kalır ve derlenecek bir şey yoktur — kapı kapalı.
    [Fact]
    public async Task Build_stays_disabled_when_the_topology_is_empty()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        vm.OnEvent(new WorkspaceTopologyEvent([], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 0, 0));

        Assert.Empty(vm.Projects);
        Assert.False(vm.BuildCommand.CanExecute(null));
    }

    // Kapı, "önceki koşuda failure var" koşulunu geçen RetryFailed'ı da kapsar: satırlar bir şekilde dolmuş olsa
    // bile (ör. eski bir koşunun event'leri) topoloji YOKSA yeni bir run başlatılamaz.
    [Fact]
    public async Task Retry_failed_is_disabled_without_a_topology_even_with_a_failed_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new ProjectStartedEvent("r0", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectFailedEvent("r0", @"C:\p\a.csproj", 100, "exit 1"));

        Assert.False(vm.RetryFailedCommand.CanExecute(null));
    }

    // Kapı bir CanExecute değişimidir: topoloji GELDİĞİNDE butonların yeniden sorgulanması gerekir — CommunityToolkit
    // RelayCommand CommandManager.RequerySuggested'a abone OLMADIĞI için bildirim elle tetiklenmezse gerçek pencerede
    // Build, Sync bittikten sonra da pasif GÖRÜNÜRDÜ.
    [Fact]
    public async Task Topology_arrival_raises_CanExecuteChanged_for_the_run_commands()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        bool buildChanged = false, rebuildChanged = false, retryChanged = false;
        vm.BuildCommand.CanExecuteChanged += (_, _) => buildChanged = true;
        vm.RebuildCommand.CanExecuteChanged += (_, _) => rebuildChanged = true;
        vm.RetryFailedCommand.CanExecuteChanged += (_, _) => retryChanged = true;

        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", 0)], [], [], []));

        Assert.True(buildChanged);
        Assert.True(rebuildChanged);
        Assert.True(retryChanged);
    }

    // ---------------------------------------------------------------- [cycle rounds/I2] koşan SCC'nin sayaç + ETA yüzeyi

    /// <summary>Bir SCC (A↔B↔C) ve grup DIŞINDA bir D — motorun gönderdiği topolojinin App'teki karşılığı.
    /// <c>Cycles</c> alanı DOLDURULUR: App üyelik haritasını (hangi Started üye hangi grubun sırasını bekliyor)
    /// yalnız oradan kurabilir.</summary>
    private static WorkspaceTopologyEvent CycleTopology() =>
        new([Node(D, "D", 0), Node(A, "A", 1, inCycle: true), Node(B, "B", 2, inCycle: true),
             Node(C, "C", 3, inCycle: true)], [[A, B, C]], [], []);

    private const string A = @"C:\p\a.csproj";
    private const string B = @"C:\p\b.csproj";
    private const string C = @"C:\p\c.csproj";
    private const string D = @"C:\p\d.csproj";

    /// <summary>Topoloji + runStarted; ardından SCC üyeleri motorun SIRALI invoke sırasıyla Started'a alınır
    /// (ara tur sonucu yayılmadığı için hiçbiri terminale dönmez — grup bitene kadar ÜÇÜ DE Started'tır).</summary>
    private static void StartCycleGroup(RunViewModel vm)
    {
        var topology = CycleTopology();
        vm.OnEvent(topology);
        foreach (var node in topology.Nodes) // cycle üyeliği topolojiden satıra taşınır
            Assert.Equal(node.InCycle, vm.Projects.Single(p => p.Id == node.Id).InCycle);
    }

    [Fact]
    public async Task A_running_cycle_group_never_reports_more_building_than_the_run_has_workers()
    {
        // [I2] Kusur: ProjectStarted HER turda ve HER üye için yayılır, ara tur sonucu ise HİÇ yayılmaz —
        // dolayısıyla grup bitene kadar bütün üyeler Started'ta birikir. 32 üyeli bir SCC 4 worker'lı bir
        // run'da "32 building" raporluyordu ve şerit "finishing 32 in flight" yazıyordu; bir SCC ise TEK bir
        // iş kalemidir, o an derlenen tek bir üyesi vardır.
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        StartCycleGroup(vm);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, TotalProjects: 4, Parallelism: 4, "Debug", 0));

        vm.OnEvent(new ProjectStartedEvent("r1", A, "A"));
        vm.OnEvent(new ProjectStartedEvent("r1", B, "B"));
        vm.OnEvent(new ProjectStartedEvent("r1", C, "C"));

        Assert.Equal(1, vm.Counters.Building);              // yalnız SON başlayan üye gerçekten derleniyor
        Assert.True(vm.Counters.Building <= vm.Parallelism); // hiçbir koşulda worker sayısını aşamaz
        Assert.Equal(3, vm.Counters.Queued);                // A + B (sıra bekliyor) + D (hiç başlamadı)
        Assert.Equal(4, vm.Counters.Total);

        // Grup bitince bayrak DÜŞER: bekleyen üye sonsuza dek "queued" görünmez.
        vm.OnEvent(new ProjectSucceededEvent("r1", A, 10, null, false));
        Assert.False(vm.Projects.Single(p => p.Id == A).CycleWaiting);
    }

    [Fact]
    public async Task The_eta_keeps_the_cycle_round_multiplier_while_the_group_is_running()
    {
        // [I2] Kusur: cycle katkısı (paralelliğe BÖLÜNMEYEN, BaselineRounds ile ÇARPILAN terim) yalnız Pending
        // üyeleri sayıyordu. Grup dispatch edilir edilmez üyeler Started'a geçtiği ve orada KALDIĞI için,
        // çarpan tam da işin yapıldığı pencerede kayboluyor; üyeler paralelliğe bölünen building kovasına
        // düşüyordu. Sabit saat: 4 proje, D 1000ms'te bitti ⇒ gözlenen ortalama 1000ms.
        long now = 5_000;
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1", () => now);
        StartCycleGroup(vm);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, TotalProjects: 4, Parallelism: 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", D, "D"));
        vm.OnEvent(new ProjectSucceededEvent("r1", D, 1000, null, false));

        // Üç queued cycle üyesi: 3 × 1000ms × BaselineRounds(2) = 6000ms, paralelliğe BÖLÜNMEDEN.
        Assert.Equal(6000, vm.EtaMs);

        vm.OnEvent(new ProjectStartedEvent("r1", A, "A"));
        vm.OnEvent(new ProjectStartedEvent("r1", B, "B"));
        vm.OnEvent(new ProjectStartedEvent("r1", C, "C"));
        vm.TickElapsed(); // canlı tick — ETA'yı yeniden hesaplar

        // Grup KOŞARKEN de aynı terim: tahmin 6000'de kalır. Kusurlu hâlde üçü building kovasına düşer ve
        // 4'e bölünürdü — ham tahmin 3000/4 + 400 = 1150, EMA ile 4788.
        Assert.Equal(6000, vm.EtaMs);
    }
}
