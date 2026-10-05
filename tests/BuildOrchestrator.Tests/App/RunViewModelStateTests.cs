using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T12/T43/C2] <see cref="RunViewModel"/>'in C2 omurgası: faz yürüyüşü, seçim/deselect, Sync ile Build/Rebuild'in
/// seçim-filtre kuralı, Build'in workspace argümanlı gönderimi, koşarken kilit (branch/
/// configuration) + canlı perf, T43 configuration geçişinin Sync'i ve kilidi, ve A5-review fold'u (engine ölümü
/// Sync fazını bırakır).
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

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug")); // → Running
        Assert.Equal(AppPhase.Running, vm.Phase);

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500)); // → Done
        Assert.Equal(AppPhase.Done, vm.Phase);
    }

    [Fact] // [E6/D7 M3] Açılış seed'i = DOĞRUDAN RootPath set (Empty→Boot) — Settings Save'in kök değişimi DEĞİL: kayıtlı
    // repo seed edilir, repo BİLİNİR ama seed'in kendisi hiçbir komut GÖNDERMEZ. Açılışın Sync'i motor hazır olunca gider
    // (RunViewModel.OnEngineReady — RunViewModelTests.The_first_engine_ready_syncs_with_the_transcript); Save'in kök
    // değişimi ise TEK Sync gönderir (SettingsDialogTests.Applying_settings_with_a_new_root_resets_rows_and_syncs_at_the_new_root).
    public async Task Seeding_the_root_path_directly_lands_in_boot_without_starting_a_sync()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        IpcCommand? sent = null;
        vm.DebugOnCommandSent = c => sent = c;

        vm.RootPath = @"D:\repo"; // MainWindow'un açılış seed'i (doğrudan set — Settings Save DEĞİL)

        Assert.Equal(AppPhase.Boot, vm.Phase); // repo bilinir → Boot
        Assert.Null(sent);                     // hiçbir komut/Sync gönderilmedi (seed komut göndermez)
        Assert.False(vm.SyncInFlight);         // uçuşta Sync yok
    }

    // ---------------------------------------------------------------- [planlama görünürlüğü] Starting fazı
    //
    // Build'e basmakla runStarted arasında motor planlamayı koşar (177 projelik OSYS'te saniyeler). O pencerede
    // App HİÇBİR ŞEY göstermiyordu: BeginRunAsync konsolu TEMİZLİYOR, Phase'e dokunmuyordu — şerit önceki
    // metinde ("▸ Stopped — …" / "▸ Ready — …") donuyor, konsol bomboş kalıyordu. Kullanıcının bildirdiği
    // "tekrar build dedim, ui'da bir şey olmadı" cümlesinin yarısı buydu (diğer yarısı motorun donmasıydı).
    //
    // Faz adı Planning DEĞİL Starting: pencerenin işi tıklamanın kaydedildiğini göstermektir, planlamayı
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

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug"));

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
    /// <c>SyncInFlight</c> bu satırlarla açılmamalıdır (açılsaydı Rebuild sessizce kilitlenirdi).</summary>
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
    public async Task RunCompleted_takes_the_phase_out_of_stopping()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug"));
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
    /// sonra yazar (<c>PlanAndRunAsync</c>'in finally'si, <c>_finishing</c> kapısı) — yani bu olay görüldüğünde
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
        vm.OnEvent(new RunStartedEvent("r2", RunMode.Build, 1, 1, "Debug"));
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
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug"));
        vm.Phase = AppPhase.Stopping;

        vm.OnEngineExited(139);

        Assert.Equal(AppPhase.Stopped, vm.Phase);
        Assert.False(vm.IsRunning);
    }

    // ---------------------------------------------------------------- [runFailed] koşarken düşen run görünür olur
    //
    // runFailed, run'ın TAMAMINI saran dış catch'ten gelir (RunCoordinator.ExecuteRunAsync) — yani runStarted'dan
    // SONRA da gelebilir ve o yolda runCompleted ASLA yazılmaz. Kümedeki diğer iki kod (planFailed/
    // msbuildNotFound) planlama penceresinde üretilir, faz Running iken gelemez.

    [Fact]
    public async Task A_run_that_fails_mid_flight_surfaces_the_reason_and_leaves_the_running_phase()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", 0)], [], [], []));
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug"));
        Assert.Equal(AppPhase.Running, vm.Phase); // ön-koşul

        vm.OnEvent(new ErrorEvent("runFailed", "access to the log file was denied"));

        Assert.Equal("access to the log file was denied", vm.RunErrorMessage); // şerit KIRMIZI gerekçeyi gösterir
        Assert.Equal(AppPhase.Idle, vm.Phase);   // donmuş "Building 3/10" faz-metni kalmaz
        Assert.False(vm.IsRunning);
    }

    // [KALDIRILDI — design v1.7.0 §3.1] Eski test: "reddedilen bir Continue şeridi kırmızıya boyamaz".
    // Continue modu ve onun reddi (noResumableRun) kaldırıldı — böyle bir olay artık hiç üretilmiyor.


    // Kalıcılık kuralı SyncErrorMessage'ın ikizi: metin, kullanıcı YENİ bir şey başlatana kadar durur.
    [Fact]
    public async Task The_run_failure_text_is_cleared_by_the_next_run_and_by_the_next_sync()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug"));
        vm.OnEvent(new ErrorEvent("runFailed", "boom"));
        Assert.Equal("boom", vm.RunErrorMessage);

        vm.OnEvent(new RunStartedEvent("r2", RunMode.Build, 1, 1, "Debug")); // yeni run başladı
        Assert.Null(vm.RunErrorMessage);

        vm.OnEvent(new ErrorEvent("runFailed", "boom again"));
        Assert.Equal("boom again", vm.RunErrorMessage);

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main")); // Sync başladı — şerit Sync ilerlemesini göstermeli
        Assert.Null(vm.RunErrorMessage);
    }

    // ---------------------------------------------------------------- seçim / filtre kuralı

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

    /// <summary>
    /// [spec 2026-09-18 §1-13 · §6.2 · task 3] <b>Sync düğmesi ekranı baştan başlatır ama VM'in plan verisine
    /// DOKUNMAZ:</b> tıklamada satırlar, topoloji ve plan (will-build) durur, faz Boot'a düşmez, graf yeniden
    /// kurulmaz (<see cref="RunViewModel.TopologyChanged"/> yok) — yalnız <see cref="RunViewModel.PlanSurfaceRestarting"/>
    /// kabuğa "liste ve grafı boş göster" der. AYNI yapıdaki topoloji gelince yüzey TEK KEZ yeniden kurulur ve
    /// bayrak düşer. Ekran tarafı <c>ProjectListFilterTests.A_restarting_sync_*</c>'tedir.
    ///
    /// <para><b>[DEĞİŞEN KURAL — spec 2026-09-18 §1-13]</b> Eski ad/iddia:
    /// <c>Sync_empties_the_project_list_and_the_graph_at_click_like_clean_does</c> — kullanıcı kararı 2026-09-12:
    /// Sync de Clean gibi tıklamada liste + grafı boşaltır, faz Boot'a döner, topoloji hepsini geri getirir.
    /// Değişme gerekçesi: Sync artık kendiliğinden de koşar (commit, pencereye dönüş) ve her Sync'te listenin
    /// boşalıp dolması yapı aynıyken hiçbir bilgi taşımadan ekranı sarsıyordu. Boşaltma Clean/Optimize
    /// tıklamasında ve gerçek bir kök değişiminde kaldı.</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-19 · task 3]</b> Bir önceki ad/iddia:
    /// <c>A_sync_click_leaves_the_plan_surface_alone</c> — Sync düğmesi yüzeye hiç dokunmaz, yapı aynı topoloji
    /// de grafı yeniden kurmaz. Değişme gerekçesi: kullanıcı Sync düğmesini (ve branch değişimini) "ekranı baştan
    /// başlat" olarak okuyor; yerinde kalan ekran Sync'in bir şey yapıp yapmadığını belirsiz bırakıyordu. Bu kez
    /// Clean'in yolu (VM'i boşaltmak, faz Boot) DEĞİL, yalnız ekran baştan başlar — veri ve faz iddiaları aynen
    /// korunur. Gönderim başarılı kurulur: düşen gönderim yüzeyi hemen geri getirir (ayrı pin).</para>
    /// </summary>
    [Fact]
    public async Task A_sync_click_restarts_the_screen_but_keeps_the_plan_data()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        var node = new ProjectNode(@"C:\p\a.csproj", "A", @"C:\p\a.csproj", ["Osys"], [], 0, null, null, false, null);
        vm.OnEvent(new WorkspaceTopologyEvent([node], [], [], []));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(@"C:\p\a.csproj", "A", true)]));
        Assert.Single(vm.Projects);   // ön-koşul: ekranda bir proje ve bir plan var
        Assert.True(vm.HasTopology);
        Assert.Equal(1, vm.WillBuildCount);
        int topologyChanges = 0;
        vm.TopologyChanged += (_, _) => topologyChanges++;
        MainWindowHost.AcceptSends(vm);

        var phaseBefore = vm.Phase;

        await vm.SyncCommand.ExecuteAsync(null);

        Assert.Single(vm.Projects);
        Assert.True(vm.HasTopology);
        Assert.Equal(0, topologyChanges);   // graf yeniden kurulmadı — ekran yalnız boş gösterilir
        Assert.Equal(1, vm.WillBuildCount); // plan durur — motorun önizlemesi gelince tazelenir
        Assert.Equal(phaseBefore, vm.Phase);
        Assert.True(vm.PlanSurfaceRestarting);

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        vm.OnEvent(new WorkspaceTopologyEvent([node], [], [], [])); // AYNI yapı

        Assert.Equal(1, topologyChanges);   // yapı aynı olsa da yüzey yeniden kuruldu
        Assert.False(vm.PlanSurfaceRestarting);
    }

    [Fact]
    public async Task Sync_clears_the_selection_but_keeps_the_filter()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.SelectProject(@"C:\p\a.csproj");
        vm.ToggleFilter(ProjectFilter.Failed);

        await vm.SyncCommand.ExecuteAsync(null);

        Assert.Null(vm.SelectedProjectId);
        Assert.Equal([ProjectFilter.Failed], vm.ActiveFilters.Order()); // filtre KORUNUR
    }

    /// <summary>[D3/T5 · design v1.13.2 §9] "Konsol + event stream her işlemde temizlenir, ardından yalnız o
    /// işlemin satırları yazılır" — Build/Rebuild/Cycles bunu <c>BeginRunAsync</c> ile zaten (koşulsuz)
    /// yapıyordu. Sync ise TIKLAMA ANINDA (motorun cevabı beklenmeden, pill'in kendisiyle AYNI an) konsolu VE
    /// event stream'i temizlemiyordu — bir önceki işlemin tortusu, Sync'in kendi <c>syncProgress</c> satırlarının
    /// ÜZERİNE yazılıyordu (bkz. <c>RunViewModel.cs:784-793</c>'teki mid-Sync run guard'ının gerekçesi: "…ama
    /// SyncProgressEvent hâlâ _runText'e satır ekliyor olabilir").</summary>
    [Fact]
    public async Task Sync_clears_the_console_and_stream_left_over_from_the_previous_operation()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        // Önceki bir Build konsola VE event stream'e satır bırakır.
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug"));
        vm.OnEvent(new ProjectLogEvent("r1", @"C:\p\a.csproj", 1, "Build succeeded"));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 100));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 100));

        Assert.NotEqual("", vm.GetRunDocumentText()); // ön-koşul: konsolda ÖNCEKİ işlemden iz var — vakum değil
        Assert.True(vm.StreamEventCount > 0, "ön-koşul: event stream'de ÖNCEKİ işlemden iz yok — vakum");

        await vm.SyncCommand.ExecuteAsync(null);

        // Bu harness'te engine hiç başlatılmaz (sınıf özeti) — gönderim SENKRON düşer ve TrySendAsync kendi
        // "[error] failed to send sync: …" satırını YENİ konsola yazar (meşru, BU Sync denemesinin satırı).
        // Asıl iddia stale içeriğin GİTMİŞ olması: önceki işlemin "Build succeeded" satırı bir daha görünmez.
        Assert.DoesNotContain("Build succeeded", vm.GetRunDocumentText());
        Assert.Equal(0, vm.StreamEventCount); // TrySendAsync hatası yalnız konsola yazar, stream'e dokunmaz
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-19]</b> Eski ad/iddia:
    /// <c>Build_and_retry_clear_both_selection_and_filter</c> — Build/Rebuild tıklaması seçimi VE statü
    /// chip'lerini düşürüyordu (prototip <c>doBuild</c>, BuildApp.jsx:1199-1200). Değişme gerekçesi (kullanıcı
    /// testi): filtreyle çalışırken Build'e basmak listeyi her seferinde filtresiz hâle döndürüyordu. Yeni kural:
    /// seçim düşer (graf fit görünüme döner), filtre — chip'ler ve arama metni — korunur; liste koşu boyunca
    /// filtreli kalır. Grafın koşu boyunca filtreyi YOK SAYMASI ayrı bir kuraldır (GraphFilterRunSuspendTests).
    /// </summary>
    [Fact]
    public async Task Build_and_rebuild_clear_the_selection_but_keep_the_filter_and_the_search()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        vm.SelectProject(@"C:\p\a.csproj");
        vm.ToggleFilter(ProjectFilter.Building);
        vm.ProjectQuery = "osys";
        await vm.BuildCommand.ExecuteAsync(null);
        Assert.Null(vm.SelectedProjectId);
        Assert.Equal([ProjectFilter.Building], vm.ActiveFilters.Order());
        Assert.Equal("osys", vm.ProjectQuery);

        vm.SelectProject(@"C:\p\b.csproj");
        await vm.RebuildCommand.ExecuteAsync(null);
        Assert.Null(vm.SelectedProjectId);
        Assert.Equal([ProjectFilter.Building], vm.ActiveFilters.Order());
        Assert.Equal("osys", vm.ProjectQuery);
    }

    // ---------------------------------------------------------------- komut gönderimi (workspace argümanları)

    /// <summary>
    /// <para><b>[DEĞİŞEN KURAL — spec 2026-09-18 §1-1]</b> Eski iddia "Build komutu seçili branch'i,
    /// <c>UseWorktree</c>'yi ve worktree adını da taşır" idi (ad: <c>..._with_branch_worktree_and_layer_patterns</c>).
    /// Motor artık yalnız çalışma ağacında derler; <c>StartRunCommand</c> bu alanları taşımaz
    /// (<c>NoWorktreeSurfaceTests</c>). Kalan iddia: mod, kök, configuration ve katman pattern'leri.</para>
    /// </summary>
    [Fact]
    public async Task Build_command_sends_RunMode_Build_with_workspace_arguments_and_layer_patterns()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var layers = new List<LayerPattern> { new(0, "^Core", "Core") };
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "run-1")
        {
            RootPath = @"D:\repo",
            Configuration = "Release",
            LayerPatterns = layers,
        };
        StartRunCommand? sent = null;
        vm.DebugOnCommandSent = c => { if (c is StartRunCommand s) sent = s; };

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.NotNull(sent);
        Assert.Equal(RunMode.Build, sent!.Mode);
        Assert.Equal(@"D:\repo", sent.RootPath);
        Assert.Equal("Release", sent.Configuration);
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

    // [KALDIRILDI — design v1.7.0 §2.7-11] Eski test: "Retry failed komutu RunMode.RetryFailed gönderir ve
    // yalnız bir failure varken etkindir". Komut kaldırıldı; hata sonrası kümenin hâlâ derlendiği
    // Incremental/BuildAfterFailureTests'te pinlenir.

    // ---------------------------------------------------------------- T12 kilit / T43 configuration

    /// <summary>[T12] Koşarken branch ve configuration kilitli, perf canlı.
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-29]</b> Kilidin kanıtı eskiden konsolda
    /// "all projects will rebuild" satırının OLMAMASIYDI; o satır artık hiç yazılmıyor (geçiş kendi Sync'ini başlatır),
    /// yani iddia vakumda kalırdı. Kanıt artık davranıştır: kilitliyken geçiş Sync başlatmaz.</para></summary>
    [Fact]
    public async Task Branch_and_configuration_are_locked_while_running_but_perf_stays_live()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1")
        {
            RootPath = @"D:\repo",
            Configuration = "Debug",
            PerfMode = "Balanced",
        };
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug"));
        Assert.True(vm.IsRunning);
        Assert.True(vm.IsMidRunLocked); // branch/configuration kontrolleri KİLİTLİ
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        vm.SetConfiguration("Release"); // koşarken kilitli → no-op
        Assert.Equal("Debug", vm.Configuration);
        Assert.Empty(sent.OfType<SyncWorkspaceCommand>());

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
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", CpuCapPercent: 70));
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

    // ---------------------------------------------------------------- [RESOLVE Faz 4 · fix 1A] Resolve notu ve koşu bağlamı

    /// <summary>[RESOLVE Faz 4 · fix 1A] Döngülü topolojili bir VM (Balanced). Koşular üretimdeki gibi KOMUT yolundan
    /// başlar — koşunun perf bağlamı (mod + Resolve anahtarı) yalnız orada yakalanır. Motor başlatılmamıştır: gönderim
    /// düşer, motorun cevabını (<see cref="RunStartedEvent"/>) test verir.</summary>
    private static RunViewModel PerfContextVm(EngineHost engine, bool fullPriority)
    {
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1")
        {
            RootPath = @"D:\repo",
            PerfMode = "Balanced",
            ResolveAtFullPriority = fullPriority,
        };
        vm.OnEvent(CycleTopology());
        return vm;
    }

    /// <summary>Koşuyu komut yolundan başlatır: <see cref="RunMode.Cycles"/> → Resolve cycles, diğeri → Build.</summary>
    private static Task StartViaCommandAsync(RunViewModel vm, RunMode mode) =>
        (mode == RunMode.Cycles ? vm.BuildCyclesCommand : vm.BuildCommand).ExecuteAsync(null);

    /// <summary>
    /// [RESOLVE Faz 4 · fix 1A — I1] Resolve cycles tam öncelikte başlarken kullanıcının konsoluna TEK satır düşer; App
    /// <c>runStarted</c>'ta yazar. Sayı motorun fiilî paralelliğidir (profilin dördü değil, cevaptaki üç), anahtar koşu
    /// başlatılırken yakalanan değerdir. Kusur: satır yalnız Supervisor'ın stderr'ine gidiyordu ve App stderr'i atar —
    /// kullanıcı onu hiç görmüyordu. Anahtar kapalıyken ve Build'de satır yoktur.
    /// </summary>
    [Theory]
    [InlineData(RunMode.Cycles, true, true)]
    [InlineData(RunMode.Cycles, false, false)]
    [InlineData(RunMode.Build, true, false)]
    public async Task A_resolve_run_at_full_priority_says_so_once_when_it_starts(RunMode mode, bool fullPriority, bool noted)
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = PerfContextVm(engine, fullPriority);

        await StartViaCommandAsync(vm, mode);
        vm.OnEvent(new RunStartedEvent("r1", mode, 4, 3, "Debug"));

        string text = vm.GetRunDocumentText();
        Assert.Equal(noted ? 1 : 0, text.Split("parallelism: 3 · cpu cap off · priority normal (Resolve cycles)").Length - 1);
        Assert.Equal(noted, text.Contains("priority normal", StringComparison.Ordinal));
    }

    /// <summary>
    /// [RESOLVE Faz 4 · fix 1A — I3] Koşu içi chip notu motorun O koşuya uyguladığını söyler: Resolve cycles tam öncelikteyse
    /// cap'siz + Normal (<see cref="PerfNoteText.ResolveNote"/>), anahtar kapalıysa ya da koşu Build ise profilin kendi notu.
    /// Mod ve anahtar koşunun yakalanan bağlamından gelir (komut yolundan başlamış koşu).
    /// </summary>
    [Theory]
    [InlineData(RunMode.Cycles, true, ResolveNote)]
    [InlineData(RunMode.Cycles, false, "parallelism: 2 · cpu cap 40%")]
    [InlineData(RunMode.Build, true, "parallelism: 2 · cpu cap 40%")]
    public async Task A_perf_change_during_a_run_describes_what_the_engine_applies_to_it(
        RunMode mode, bool fullPriority, string expected)
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = PerfContextVm(engine, fullPriority);
        await StartViaCommandAsync(vm, mode);
        vm.OnEvent(new RunStartedEvent("r1", mode, 4, 3, "Debug"));

        await vm.CyclePerfAsync(); // Balanced → Light

        string text = vm.GetRunDocumentText();
        Assert.Contains(expected, text);
        Assert.Equal(expected.Contains("priority normal", StringComparison.Ordinal),
            text.Contains("priority normal", StringComparison.Ordinal));
    }

    /// <summary>
    /// [RESOLVE Faz 4 · fix 1A — I2] Açılış koreografisi boyunca chip canlıdır (<see cref="RunViewModel.IsMidRunLocked"/>) ve
    /// notu AÇILAN koşuyu anlatmalı. Kusur: bağlam komutla birlikte, koreografiden SONRA yazılıyordu; oturumun ilk Resolve'u
    /// o pencerede <c>cpu cap 40%</c> der, motor ise cap'siz ve Normal koşardı.
    /// </summary>
    [Fact]
    public async Task A_perf_change_while_a_resolve_run_is_opening_describes_that_run()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = PerfContextVm(engine, fullPriority: true);
        var choreography = new TaskCompletionSource();
        vm.OperationChoreography = _ => choreography.Task;

        var start = StartViaCommandAsync(vm, RunMode.Cycles);
        Assert.True(vm.IsStarting); // komut henüz gitmedi
        await vm.CyclePerfAsync(); // Balanced → Light

        Assert.Contains(ResolveNote, vm.GetRunDocumentText());
        choreography.SetResult();
        await start;
    }

    /// <summary>
    /// [RESOLVE Faz 4 · fix 1A — I2] Bir önceki koşunun bağlamı sonrakinin koreografisine sızmaz: Resolve başlatıldıktan
    /// (komutu gittikten) sonra Build açılırken chip düz notu yazar — motor Light'ın cap'ini uygular. Kusur: not son
    /// gönderilen (Resolve) komutla konuşup <c>cpu cap off · priority normal</c> derdi.
    /// </summary>
    [Fact]
    public async Task A_perf_change_while_a_build_opens_after_a_resolve_start_writes_the_plain_note()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = PerfContextVm(engine, fullPriority: true);
        await StartViaCommandAsync(vm, RunMode.Cycles); // komut gider; motor yok, VM boşa döner
        Assert.False(vm.IsMidRunLocked);
        var choreography = new TaskCompletionSource();
        vm.OperationChoreography = _ => choreography.Task;

        var start = StartViaCommandAsync(vm, RunMode.Build);
        Assert.True(vm.IsStarting);
        await vm.CyclePerfAsync(); // Balanced → Light

        string text = vm.GetRunDocumentText();
        Assert.Contains("parallelism: 2 · cpu cap 40%", text);
        Assert.DoesNotContain("priority normal", text);
        choreography.SetResult();
        await start;
    }

    /// <summary>
    /// [RESOLVE Faz 4 · re-review N1] Koşu açılırken anahtar değişse de (Save koreografi sürerken) O koşu başlatıldığı
    /// andaki değerle kalır: komutun bayrağı, açılıştaki chip notu ve <c>runStarted</c> notu AYNI yakalanan değeri okur.
    /// Pinsizdi: üç okumadan biri canlı özelliğe dönse süit yeşil kalırdı — not cap'siz derken motor cap uygulardı ya da
    /// tersi. Üç gerçek tek demette karşılaştırılır ki bir kırmızı hangisinin koptuğunu göstersin.
    /// </summary>
    [Fact]
    public async Task A_setting_changed_while_a_resolve_run_opens_does_not_reach_that_run()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = PerfContextVm(engine, fullPriority: true);
        var sent = new List<StartRunCommand>();
        vm.DebugOnCommandSent = c => { if (c is StartRunCommand s) sent.Add(s); };
        var choreography = new TaskCompletionSource();
        vm.OperationChoreography = _ => choreography.Task;

        var start = StartViaCommandAsync(vm, RunMode.Cycles);
        Assert.True(vm.IsStarting);
        vm.ResolveAtFullPriority = false; // Save koreografi sürerken anahtarı kapatır
        await vm.CyclePerfAsync();        // Balanced → Light: not AÇILAN koşuyu anlatmalı
        choreography.SetResult();
        await start;
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Cycles, 4, 3, "Debug"));

        string text = vm.GetRunDocumentText();
        Assert.Equal((Flag: true, ChipNote: true, StartNote: true), (
            Flag: Assert.Single(sent).ResolveAtFullPriority,
            ChipNote: text.Contains(ResolveNote, StringComparison.Ordinal),
            StartNote: text.Contains("parallelism: 3 · cpu cap off · priority normal (Resolve cycles)", StringComparison.Ordinal)));
    }

    // ---------------------------------------------------------------- [PERF Faz D / karar 10] İşçi kırpma notu görünür

    /// <summary>
    /// [PERF Faz D / karar 10 · kırpma notu görünür] Motor profilin istediğinden az işçiyle koşarsa (<c>runStarted</c>
    /// gerekçe taşır) kullanıcı bunu İKİ yerde görür: konsolda TAM satır bir kez, event stream'de koşunun başlangıç
    /// satırının HEMEN ardından aynı metin (Info — başlangıç satırının anlatı tonu). Kusur: satır yalnız decision.log'a ve
    /// Supervisor'ın stderr'ine gidiyordu; App stderr'i atar — kullanıcı onu hiç görmüyordu. Gerekçe yoksa iki yerde de
    /// satır yoktur.
    /// </summary>
    [Theory]
    [InlineData("1 logical processor")]
    [InlineData(null)]
    public async Task A_run_with_fewer_workers_than_asked_says_so_in_the_console_and_after_the_stream_start_line(string? reason)
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = PerfContextVm(engine, fullPriority: true);
        await StartViaCommandAsync(vm, RunMode.Build);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 4, 2, "Debug", WorkersReducedReason: reason));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(@"C:\p\a.csproj", "A", true)]));

        const string note = ReductionNote;
        bool reduced = reason is not null;
        // [review M2] Satır bazlı: run dokümanı satır satır yazılır (AppendRunLine). Alt-dize sayımı süslenmiş bir satırı
        // ("warning: workers reduced …") ve gerekçesiz koşuda başka metinle yazılmış bir kırpma satırını kaçırırdı.
        var lines = vm.GetRunDocumentText().Split('\n');
        Assert.Equal(reduced ? 1 : 0, lines.Count(l => l == note));
        Assert.Equal(reduced ? 1 : 0, lines.Count(l => l.Contains("workers reduced", StringComparison.Ordinal)));
        var stream = vm.StreamEvents.ToList();
        int start = stream.FindIndex(s => s.Text.StartsWith("Build started", StringComparison.Ordinal));
        Assert.True(start >= 0, "the run's start line is missing from the stream");
        Assert.Equal(reduced ? 1 : 0, stream.Count(s => s.Text.Contains("workers reduced", StringComparison.Ordinal)));
        if (reduced)
        {
            Assert.Equal(note, stream[start + 1].Text);
            Assert.Equal(StreamKind.Info, stream[start + 1].Kind);
        }
    }

    /// <summary>
    /// [kırpma notu görünür] Kırpılmış bir Resolve cycles koşusu tam öncelikte başlarsa iki not da konsola düşer; sıra
    /// decision.log'unkiyle AYNI: önce kırpma (sayının nereden geldiğini söyler), sonra o sayıyı kullanan Resolve notu.
    /// </summary>
    [Fact]
    public async Task A_clamped_resolve_run_names_the_reduction_before_its_full_priority_note()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = PerfContextVm(engine, fullPriority: true);
        await StartViaCommandAsync(vm, RunMode.Cycles);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Cycles, 4, 2, "Debug", WorkersReducedReason: "1 logical processor"));

        // [review M2] Sıra da satır bazlı: IndexOf alt-dizeyi bulurdu, süslenmiş bir satır sırayı sahte doğrulardı.
        var lines = vm.GetRunDocumentText().Split('\n');
        int reduction = Array.IndexOf(lines, ReductionNote);
        int resolve = Array.IndexOf(lines, ResolveNote);
        Assert.True(reduction >= 0 && resolve > reduction, string.Join('\n', lines));
    }

    /// <summary>
    /// [kırpma notu görünür] Akış satırı başlangıç satırıyla birlikte <see cref="BuildPreviewEvent"/>'e ertelenir; bir
    /// sonraki <c>runStarted</c> bekleyen satırı EZER: kırpmasız yeni koşu, önizlemesi gelmemiş kırpılmış koşunun satırını
    /// taşımaz. Bu pin düzeltmeden önce de yeşildir (o zaman hiç satır yoktu): bekleyen satırın her <c>runStarted</c>'da
    /// yeniden yazıldığını korur. Koşunun BİTİŞ yollarını (motor kaybı) kapsamaz — onlar bir sonraki teorinin konusu.
    /// </summary>
    [Fact]
    public async Task A_new_run_does_not_carry_the_previous_runs_pending_reduction_line()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = PerfContextVm(engine, fullPriority: true);
        vm.OnEvent(new RunStartedEvent("r0", RunMode.Build, 4, 2, "Debug", WorkersReducedReason: "1 logical processor"));
        // r0'ın önizlemesi hiç gelmedi: bekleyen satırı yayılmadı, sıradaki runStarted onu ezer
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 4, 4, "Debug"));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(@"C:\p\a.csproj", "A", true)]));

        Assert.Contains(vm.StreamEvents, s => s.Text.StartsWith("Build started", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.StreamEvents, s => s.Text.Contains("workers reduced", StringComparison.Ordinal));
    }

    /// <summary>
    /// [kırpma notu görünür · review M1] Koşu <c>runStarted</c>'tan sonra, önizlemesinden ÖNCE biterse (motor kaybı —
    /// <c>OnEngineExited</c>) bekleyen satırlar bırakılır: Restart sonrası Appended Sync'in önizlemesi
    /// (<see cref="BuildPreviewEvent"/>'in tek diğer üreticisi) ölü koşunun "Build started", "workers reduced" ve uyarı
    /// satırlarını yeni akışa basmaz. Kusur: bekleyen durum yalnız bir sonraki <c>runStarted</c>'la ya da önizlemeyle
    /// sıfırlanıyordu; koşu-sonu hunisi (<c>MarkRunEnded</c>) ona dokunmuyordu. Üç satır tek kök nedenden gelir (başlangıç
    /// kipi bırakılmazsa önizleme üçünü de yayar); her biri ayrı bir satır olarak pinlidir.
    /// </summary>
    [Theory]
    [InlineData(null, null, "Build started")]
    [InlineData("1 logical processor", null, "workers reduced")]
    // [koşu başı uyarıları görünür] Ölü koşunun uyarı satırı da sonraki önizlemeye basılmaz — aynı kökün üçüncü belirtisi:
    // uyarılar başlangıç kapısının (_pendingRunStartMode) içinde yayılır, kapı bırakılmazsa üçü birlikte kırmızı verir.
    // Bekleyen uyarı listesinin kendi null'lanması hiçbir yüzeyden gözlenmez (hijyen); bu satır onu pinlemez.
    [InlineData(null, StaleObjWarning, StaleObjWarningText)]
    public async Task A_run_lost_before_its_preview_leaves_no_stale_stream_line_for_the_next_preview(string? reason,
        string? warning, string staleLine)
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = PerfContextVm(engine, fullPriority: true);
        vm.OnEvent(new RunStartedEvent("r0", RunMode.Build, 4, 2, "Debug", WorkersReducedReason: reason,
            Warnings: warning is null ? null : [warning]));
        vm.OnEngineExited(1); // motor önizlemeden önce öldü: r0'ın önizlemesi hiç gelmeyecek
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(@"C:\p\a.csproj", "A", true)])); // Restart sonrası Sync'in önizlemesi

        Assert.DoesNotContain(vm.StreamEvents, s => s.Text.Contains(staleLine, StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- [koşu başı uyarıları görünür] bayat obj + ters katman

    // Koşu başı uyarılarının örnek satırları — Supervisor'ın decision.log'a yazıp runStarted.Warnings'le taşıdığı biçimde
    // ("warning: " önekli; önce bayat obj, sonra ters katman). App metni ayrıştırmaz; akışta yalnız öneki düşer. Öneksiz
    // metinler akış satırının beklenen değeridir (kâhin StreamText'ten bağımsız); önekli satır Supervisor'ın
    // kompozisyonuyla ("warning: " + metin) kurulur. StaleObjWarning/ReverseLayerWarning internal: tel testi
    // (IpcMessagesTests) aynı örnekleri kullanır, ikinci bir kopya yok. Konsol/akış not metinleri (PerfNoteText çıktısı)
    // bu sınıfın birçok testinde AYNEN pinlenir; tek tanım burada.
    private const string StaleObjWarningText = "A: obj holds a restore for .NETStandard,Version=v2.0";
    private const string ReverseLayerWarningText =
        "reverse layer dependency: 'A' (layer 0 'Data') depends on producer 'B.csproj' (layer 1 'Ui')";
    internal const string StaleObjWarning = "warning: " + StaleObjWarningText;
    internal const string ReverseLayerWarning = "warning: " + ReverseLayerWarningText;
    private const string ReductionNote = "workers reduced to 2 (1 logical processor)";
    private const string ResolveNote = "parallelism: 2 · cpu cap off · priority normal (Resolve cycles)";

    /// <summary>
    /// [koşu başı uyarıları görünür] Motorun koşu başı uyarıları (<c>runStarted.Warnings</c> — bayat obj, ters katman)
    /// kullanıcıya İKİ yerde görünür: konsolda her satır AYNEN tam bir kez ("warning: " öneki satırı amber boyar), event
    /// stream'de başlangıç satırının ve kırpma satırının ardından, sırası korunarak, Warn türünde ve öneksiz (akışın Warn
    /// satırları önek taşımaz). Kusur: iki uyarı ailesi yalnız Supervisor'ın stderr'ine (App onu atar) ve kısmen
    /// decision.log'a gidiyordu — kullanıcı hiçbirini görmüyordu. Tek projelik koşuda da yazılır (kırpma notunun istisnası
    /// burada geçerli değil: o projenin bayat obj'si o koşuyu bozabilir); orada kırpma satırı olmadığı için uyarılar
    /// başlangıç satırını doğrudan izler. Uyarı yoksa iki yerde de satır yoktur.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Run_start_warnings_reach_the_console_and_follow_the_start_lines_in_the_stream(bool warned,
        bool singleProject)
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = PerfContextVm(engine, fullPriority: true);
        await StartViaCommandAsync(vm, RunMode.Build);
        if (singleProject) vm.RunTargetId = "A"; // satırdan basıldı (BeginRunAsync bunu tıklama anında yazar)

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 4, 2, "Debug", WorkersReducedReason: "1 logical processor",
            Warnings: warned ? [StaleObjWarning, ReverseLayerWarning] : null));
        int streamBefore = vm.StreamEvents.Count;
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(@"C:\p\a.csproj", "A", true)]));

        // [review M2 deseni] Satır bazlı: süslenmiş ya da öneki düşmüş bir konsol satırı sayılmaz.
        var lines = vm.GetRunDocumentText().Split('\n');
        Assert.Equal(warned ? 1 : 0, lines.Count(l => l == StaleObjWarning));
        Assert.Equal(warned ? 1 : 0, lines.Count(l => l == ReverseLayerWarning));
        // Önizlemenin akışa eklediği satırlar: başlangıç satırı, (tek projelik koşu değilse) kırpma satırı, uyarılar.
        var expected = new List<(StreamKind, string)>();
        if (!singleProject) expected.Add((StreamKind.Info, ReductionNote));
        if (warned)
        {
            expected.Add((StreamKind.Warn, StaleObjWarningText));
            expected.Add((StreamKind.Warn, ReverseLayerWarningText));
        }
        Assert.True(vm.StreamEvents.Count > streamBefore, "the preview did not add the run's start line");
        Assert.Equal<(StreamKind, string)>(expected, vm.StreamEvents.Skip(streamBefore + 1).Select(s => (s.Kind, s.Text)));
    }

    /// <summary>
    /// [koşu başı uyarıları görünür] Konsol sırası: önce kırpma notu, sonra Resolve notu, sonra koşu başı uyarıları
    /// (motorun sırasıyla) — event stream'deki sırayla AYNI (başlangıç → kırpma → uyarılar). İki not koşunun nasıl koştuğunu
    /// söyler ve yan yana kalır (Resolve notunun sayısı kırpmadan gelir); uyarılar onların ardından gelir.
    /// </summary>
    [Fact]
    public async Task Run_start_warnings_follow_the_reduction_and_resolve_notes_in_the_console()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = PerfContextVm(engine, fullPriority: true);
        await StartViaCommandAsync(vm, RunMode.Cycles);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Cycles, 4, 2, "Debug", WorkersReducedReason: "1 logical processor",
            Warnings: [StaleObjWarning, ReverseLayerWarning]));

        var lines = vm.GetRunDocumentText().Split('\n');
        int[] order =
        [
            Array.IndexOf(lines, ReductionNote),
            Array.IndexOf(lines, ResolveNote),
            Array.IndexOf(lines, StaleObjWarning),
            Array.IndexOf(lines, ReverseLayerWarning),
        ];
        Assert.True(order[0] >= 0 && order.Zip(order.Skip(1)).All(p => p.First < p.Second), string.Join('\n', lines));
    }

    /// <summary>
    /// [koşu başı uyarıları · akış seli] Event stream'de koşu başı uyarıları tavana kadar (üç) her biri ayrı bir Warn satırıdır,
    /// öneksiz; tavanı aşınca akışa TEK Warn özet satırı düşer (<c>{n} run-start warnings — see the console</c>, n uyarıların
    /// gerçek sayısı). Konsol tavandan bağımsız HER satırı AYNEN tam bir kez yazar ve özet satırını yazmaz: özet satırı
    /// konsola yönlendirir. Kusur: her uyarı satırı akışa ayrı yazılıyordu; çok projeli bir çalışma alanında
    /// (ARCHITECTURE §4.3) bayat obj satırları akışın sınırlı tamponunu doldurup diğer olayları gömerdi.
    /// </summary>
    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(25, true)]
    public async Task Run_start_warnings_beyond_the_stream_limit_collapse_into_one_summary_line(int count, bool collapsed)
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = PerfContextVm(engine, fullPriority: true);
        await StartViaCommandAsync(vm, RunMode.Build);
        string[] texts = [.. Enumerable.Range(1, count).Select(i => $"P{i}: obj holds a restore for .NETStandard,Version=v2.0")];
        string[] warnings = [.. texts.Select(t => "warning: " + t)]; // Supervisor'ın kompozisyonu: "warning: " + metin

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 4, 2, "Debug", Warnings: warnings));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(@"C:\p\a.csproj", "A", true)]));

        // Konsol: tavandan bağımsız her satır AYNEN, tam bir kez (satır bazlı); özet satırı konsola yazılmaz.
        var consoleLines = vm.GetRunDocumentText().Split('\n');
        Assert.All(warnings, w => Assert.Equal(1, consoleLines.Count(l => l == w)));
        Assert.DoesNotContain(consoleLines, l => l.Contains("run-start warnings", StringComparison.Ordinal));
        // Akış: bu koşunun akışa yazdığı Warn satırlarının TAMAMI (uyarılar dışında bu akışa Warn düşüren bir şey yok).
        string[] expected = collapsed ? [$"{count} run-start warnings — see the console"] : texts;
        Assert.Equal(expected, vm.StreamEvents.Where(s => s.Kind == StreamKind.Warn).Select(s => s.Text));
    }

    /// <summary>
    /// [kırpma notu görünür · review M5] Tek projelik koşuda (satır menüsünden Build/Rebuild/Clean — <c>RunTargetId</c>
    /// dolu) kırpma satırı ne konsola ne akışa yazılır: bir proje derlenirken işçi sayısı koşuyu tarif etmez, akışın tek
    /// proje başlangıç satırı da bu yüzden paralellik söylemez. decision.log satırı Supervisor'da kalır (tanı). Önizleme
    /// akışa yalnız açılış satırını ekler. Satır menüsünün üç kipi de (Build/Rebuild/Clean) aynı kapıdan geçer.
    /// </summary>
    [Theory]
    [InlineData(RunMode.Build)]
    [InlineData(RunMode.Rebuild)]
    [InlineData(RunMode.Clean)]
    public async Task A_single_project_run_writes_the_reduction_line_to_neither_the_console_nor_the_stream(RunMode mode)
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = PerfContextVm(engine, fullPriority: true);
        await StartViaCommandAsync(vm, RunMode.Build);
        vm.RunTargetId = "A"; // satırdan basıldı (BeginRunAsync bunu tıklama anında yazar)

        vm.OnEvent(new RunStartedEvent("r1", mode, 1, 2, "Debug", WorkersReducedReason: "1 logical processor"));
        int streamBefore = vm.StreamEvents.Count;
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(@"C:\p\a.csproj", "A", true)]));

        Assert.DoesNotContain(vm.GetRunDocumentText().Split('\n'), l => l.Contains("workers reduced", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.StreamEvents, s => s.Text.Contains("workers reduced", StringComparison.Ordinal));
        Assert.Equal(streamBefore + 1, vm.StreamEvents.Count); // önizleme yalnız tek proje açılış satırını ekledi
    }

    // ---------------------------------------------------------------- [A13/T3a · a10/a11] K11 notunun Balanced varyantı + damgası

    /// <summary>
    /// [A13/T3a · a10/a11] Yukarıdaki test yalnız Light/Full varyantlarını pinliyordu (<c>PerfNoteText.cs:35</c>
    /// — Balanced testsizdi).
    /// <para><b>[DEĞİŞEN KURAL — design v1.7.0 §2.5]</b> Eski iddia satırın <c>HH:mm:ss</c> önekini de
    /// pinliyordu; konsolda duvar saati kaldırıldı, satır yalnız metindir.</para>
    /// </summary>
    [Fact]
    public async Task Perf_change_while_running_writes_the_balanced_variant()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1")
        {
            PerfMode = "Balanced",
            WallClock = () => new DateTimeOffset(2026, 7, 23, 12, 4, 7, TimeSpan.Zero),
        };
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", CpuCapPercent: 70));
        Assert.True(vm.IsRunning);

        await vm.CyclePerfAsync(); // Balanced → Light
        await vm.CyclePerfAsync(); // Light → Full
        await vm.CyclePerfAsync(); // Full → Balanced (tam döngü)

        Assert.Equal("Balanced", vm.PerfMode);
        Assert.Contains("parallelism: 4 · cpu cap 70%", vm.GetRunDocumentText());
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

    /// <summary>[kullanıcı kararı 2026-09-29] Debug|Release geçişi Sync düğmesinin sürecini işletir: konsol ve akış
    /// temizlenir, ekran (liste + graf) baştan başlar, bölümün ilk satırı yeni configuration'ı adlandırır ve Sync yeni
    /// configuration'ı taşır. Uzakta değişen bir şey olmadığı için ağa çıkılmaz (branch değişiminin Sync'i gibi).
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski ad/iddia: <c>Switching_configuration_marks_everything_dirty_and_writes_the_warn_line</c>
    /// — geçiş Sync GÖNDERMEZ, her satırı <c>WillBuild=true</c> işaretler ve konsola
    /// "Configuration → Release — all projects will rebuild" yazar. Değişme gerekçesi (ölçüm): defter proje başına TEK
    /// imza tutar, o da projenin en son derlendiği configuration'ınkidir — Debug → Release → Debug dönüşünde motor her
    /// satırı güncel bulurken tahmin hepsini "derlenecek" diyordu; OSYS'te 184 projenin hiçbirinde <c>bin\Release</c>
    /// çıktısı yokken motor "never built", tahmin "affected" diyordu. Doğru cevabı yalnız yeni configuration'ın Sync'i
    /// verir.</para></summary>
    [Fact]
    public async Task Switching_configuration_runs_the_sync_button_process_with_it_without_fetching()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1)], [], [], []));
        vm.OnEvent(new SyncProgressEvent("previous operation line", "info"));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 2, 0));
        MainWindowHost.AcceptSends(vm);
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        vm.SetConfiguration("Release");

        Assert.Equal("Release", vm.Configuration);
        var sync = Assert.Single(sent.OfType<SyncWorkspaceCommand>());
        Assert.Equal("Release", sync.Configuration);
        Assert.False(sync.Fetch);
        var lines = vm.GetRunDocumentText().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.DoesNotContain("previous operation line", lines); // yeni bölüm: önceki işlemin satırı gitti
        Assert.Equal("Configuration → Release", lines[0]);         // bölümün ilk satırı (BİREBİR)
        Assert.True(vm.PlanSurfaceRestarting);                      // liste + graf ekranda baştan başlar
    }

    /// <summary>[kullanıcı kararı 2026-09-29] Uçuştaki bir Sync segment'i kilitler (branch chip'iyle AYNI kapı): Sync
    /// başladığı configuration'ı taşır; o sırada kabul edilen bir geçişte eski configuration'ın cevabı yeni
    /// configuration'ın satırlarını boyardı (ölçülen kusur: segment "Release" derken satırlar Debug'ın durumunu
    /// gösteriyordu).</summary>
    [Fact]
    public void A_switch_is_refused_while_a_sync_is_in_flight()
    {
        var vm = T5Vm();
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));

        Assert.False(vm.CanSwitchConfiguration);
        vm.SetConfiguration("Release");
        Assert.Equal("Debug", vm.Configuration);
        Assert.Empty(sent);

        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 0, 0));
        Assert.True(vm.CanSwitchConfiguration);
    }

    /// <summary>[kullanıcı kararı 2026-09-29] Pencereye dönüşün sessiz Sync'i de segment'i İSTEK anından itibaren
    /// kilitler. Gerçek sıra budur: arkadaki pencerede segment'e tıklamak önce pencereyi etkinleştirir (sessiz Sync
    /// eski configuration'la yola çıkar), tıklama ondan SONRA işlenir. Kilit olmasa geçiş kabul edilir ve sessiz Sync'in
    /// eski configuration'lı cevabı satırları boyardı; kilitle o tıklama yutulur, ikincisi çalışır.</summary>
    [Fact]
    public async Task A_switch_is_refused_while_a_silent_sync_is_being_requested()
    {
        var vm = T5Vm();
        MainWindowHost.AcceptSends(vm);
        bool? openAtRequest = null;
        vm.DebugOnCommandSent = c =>
        {
            if (c is not SyncWorkspaceCommand) return;
            openAtRequest = vm.CanSwitchConfiguration;
            vm.SetConfiguration("Release"); // aynı tıklamanın segment'e düşen yarısı
        };

        Assert.True(await vm.SyncSilentlyAsync(SilentSyncReason.Refresh));

        Assert.False(openAtRequest);
        Assert.Equal("Debug", vm.Configuration);
    }

    /// <summary>[kullanıcı kararı 2026-09-29] Workspace'e dokunan bir iş (Clean, Optimize, checkout, pull) sürerken de
    /// geçiş reddedilir — build'in kilidiyle aynı kural: geçiş bir Sync başlatır, Sync'in başlayamadığı her an geçiş de
    /// yoktur. Kapı işin gönderim ANINDA (istek penceresi) ölçülür; tıklama da o anda denenir.</summary>
    [Theory]
    [InlineData("clean")]
    [InlineData("optimize")]
    [InlineData("checkout")]
    [InlineData("pull")]
    public async Task A_switch_is_refused_while_a_workspace_job_is_in_flight(string job)
    {
        var vm = T5Vm();
        vm.InspectGitOperation = _ => Core.Git.GitOperation.None; // checkout ve pull git'e yazabilsin
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 0, 0, Behind: 2)); // pull'un chip'i görünür
        bool? openDuringJob = null;
        vm.DebugOnCommandSent = c =>
        {
            if (c is not (CleanWorkspaceCommand or OptimizeWorkspaceCommand or CheckoutBranchCommand
                or PullRepositoryCommand)) return;
            openDuringJob = vm.CanSwitchConfiguration;
            vm.SetConfiguration("Release"); // iş sürerken segment'e tıklanmış gibi
        };

        switch (job)
        {
            case "clean": await vm.CleanCommand.ExecuteAsync(null); break;
            case "optimize": await vm.OptimizeCommand.ExecuteAsync(null); break;
            case "checkout":
                await vm.SelectBranch(new BranchRef("feature/x", "bbbbbbbbbbbb", IsActive: false, IsRemoteTracking: false));
                break;
            case "pull": await vm.PullRepositoryCommand.ExecuteAsync(null); break;
        }

        Assert.False(openDuringJob);
        Assert.Equal("Debug", vm.Configuration);
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

    /// <summary>
    /// [Task 4 — carried item 1] Şeridin sabit paydası (<c>WillBuildCount</c>, dolayısıyla ilerleme yüzdesi)
    /// yalnız KESİN derlenecekleri sayar — koşullu (<c>Conditional</c>, <see
    /// cref="WillBuildReason.WaitingForDependency"/>) bir proje kökü hâlâ hatalıysa atlanabilir, dolayısıyla
    /// paydaya GİRMEZ. Dalga/kuyrukla (<see cref="RunViewModel.ScopeFor"/>/<c>InRunQueueFor</c>) AYNI kaynak.
    /// </summary>
    [Fact]
    public void WillBuildCount_excludes_a_conditional_project_from_its_fixed_denominator()
    {
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\d.csproj", "D", 1)], [], [], []));
        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(@"C:\p\a.csproj", "A", true),
            new BuildPreviewItem(@"C:\p\d.csproj", "D", true, Reason: WillBuildReason.WaitingForDependency,
                Conditional: true, DependencyRoots: ["Up"]),
        ]));

        Assert.Equal(1, vm.WillBuildCount); // yalnız A — D koşullu, kesin değil
        Assert.False(vm.AllClean);
    }

    /// <summary>
    /// [final review — C1] Önizlemesi YALNIZ koşullu bir proje taşıyan koşu "her şey güncel" diye
    /// RAPORLANAMAZ. Koşullu proje <c>WillBuild=true</c> kalır ve motor sırası geldiğinde onu GERÇEKTEN
    /// dispatch eder (<c>ConditionalRebuild.Decide</c> → <c>Build</c>: kök bu koşuda düzeldi, defterde zaten
    /// başarılı ya da projeden düştü) — yani MSBuild derlerken şerit "▸ Checking — scanning for changes…"
    /// diyordu, bitişte yeşil "Everything up to date — … nothing to build" yazıyordu ve konsol koşuyu
    /// "Build started — 0 projects" diye açıyordu.
    ///
    /// <para><b>[DEĞİŞEN KURAL — Task 4'ün yan etkisi]</b> Task 4 <c>_willBuildIds</c>'i "KESİN derlenecekler"e
    /// daralttı (koşullu hariç, bkz. üstteki test) ama <c>AllClean</c> onu hâlâ "hiçbir şey kirli değil" diye
    /// okuyordu. İki soru ayrıldı: payda/kuyruk/dalga KESİN kümedir (değişmedi), <c>AllClean</c> ise
    /// ÖNİZLEMENİN gördüğü kirliliğin (<c>WillBuild==true</c>, koşullu DAHİL) yokluğudur.</para>
    /// </summary>
    [Fact]
    public void A_run_whose_preview_is_entirely_conditional_is_never_reported_as_all_clean()
    {
        const string d = @"C:\p\d.csproj";
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1")
        { RootPath = @"D:\repo" };
        vm.OnEvent(new WorkspaceTopologyEvent([Node(d, "D", 0)], [], [], []));
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug"));
        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(d, "D", true, Reason: WillBuildReason.WaitingForDependency,
                Conditional: true, DependencyRoots: ["Up"]),
        ]));

        Assert.False(vm.AllClean);          // önizleme KİRLİ bir proje gördü
        Assert.Equal(0, vm.WillBuildCount); // ...ama sabit payda hâlâ yalnız KESİN kümedir (Task 4, değişmedi)
        // Konsolun açılış satırı koşunun PLANINI söyler — "0 projects" derken bir proje derleniyordu.
        Assert.Equal("Build started — 1 projects, parallelism 4",
            vm.StreamEvents.Single(s => s.Text.StartsWith("Build started", StringComparison.Ordinal)).Text);

        // Motor koşullu projeyi GERÇEKTEN dispatch etti (kök artık sağlıklı): şerit "Checking…" DEMEZ.
        vm.OnEvent(new ProjectStartedEvent("r1", d, "D"));
        Assert.StartsWith("▸ Building ", vm.RibbonLine.Text, StringComparison.Ordinal);

        vm.OnEvent(new ProjectSucceededEvent("r1", d, 1200));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, Succeeded: 1, Failed: 0, Skipped: 0,
            Queued: 0, DurationMs: 1200));

        Assert.Equal(AppPhase.Done, vm.Phase);
        // Bitiş satırı koşunun GERÇEKTEN yaptığını sayar — "Everything up to date … nothing to build" DEĞİL.
        Assert.Equal("Completed — 1 succeeded · 0 skipped · 1s", vm.RibbonLine.Text);
        // İlerleme de all-clean dalını (Done ⇒ %100 "hiçbir şey yapılmadı") bırakır: çubuğun ölçtüğü şey
        // KESİN kümedir ve o küme boştur (payda dalı, wb==0 ⇒ 0).
        Assert.Equal(0.0, RibbonText.Progress(vm.Phase, vm.AllClean, vm.Counters,
            vm.WillBuildCount, vm.FinishedOfWillBuild, vm.Counters.Total));
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

    // ---------------------------------------------------------------- [Sync guard] Sync'in kendisi de çift tetiklenemez
    //
    // Ölçülen kusur: Sync düğmesi, Sync sürerken basılabilir kalıyordu. İkinci basış motora ikinci bir TAM
    // analiz kuyruklatır (tarama + graf + topo + iki incremental geçiş) ve her basış İKİ komut gönderir
    // (sync + listBranches) — konsolda aynı transkript iki kez akıyor, şerit
    // Syncing → Idle → Syncing yapıyordu. Kapı iki pencereyi de kapsar: tıklama→syncStarted arası
    // (_syncRequested) ve syncStarted→syncCompleted arası (_syncInFlight).

    [Fact]
    public async Task Sync_cannot_be_triggered_again_while_one_is_in_flight()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        Assert.True(vm.SyncCommand.CanExecute(null));

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(vm.SyncCommand.CanExecute(null)); // Sync uçuşta — düğme pasif

        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));
        Assert.True(vm.SyncCommand.CanExecute(null));  // bitti — kapı geri açık
    }

    [Fact] // Bayrak GÖNDERİMDEN ÖNCE kurulur (BeginRunAsync'in IsStarting simetriği) ve gönderim düşerse geri açılır.
    public async Task The_sync_gate_closes_before_the_command_is_sent_and_reopens_if_the_send_fails()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // hiç başlatılmadı → gönderim SENKRON düşer
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        bool? requestedAtSendTime = null;
        vm.DebugOnCommandSent = c => { if (c is SyncWorkspaceCommand) requestedAtSendTime = vm.SyncRequested; };

        await vm.SyncCommand.ExecuteAsync(null);

        Assert.True(requestedAtSendTime);               // kapı gönderimden ÖNCE kapandı
        Assert.False(vm.SyncRequested);                 // gönderim düştü → hiçbir syncStarted gelmeyecek, kilit bırakılmaz
        Assert.True(vm.SyncCommand.CanExecute(null));
    }

    [Fact] // syncStarted geldiğinde nöbet _syncInFlight'a geçer — istek bayrağı ASILI kalmaz (çift kilit olmaz).
    public async Task The_request_flag_hands_over_to_the_in_flight_flag_when_the_engine_answers()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));

        Assert.False(vm.SyncRequested); // nöbeti devretti
        Assert.True(vm.SyncInFlight);
    }

    [Fact] // Motor Sync ORTASINDA ölürse kapı açılmalı — aksi halde Sync düğmesi KALICI pasif kalırdı.
    public async Task Engine_death_mid_sync_reopens_the_sync_gate()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(vm.SyncCommand.CanExecute(null));

        vm.OnEngineExited(1);

        Assert.True(vm.SyncCommand.CanExecute(null));
    }

    [Fact] // Başarısız bir Sync (planFailed) de kapıyı açar: retry YALNIZ Sync ile mümkündür (Sync salt-okur).
    public async Task A_failed_sync_reopens_the_sync_gate()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(vm.SyncCommand.CanExecute(null));

        vm.OnEvent(new ErrorEvent("planFailed", "git fetch origin failed"));

        Assert.True(vm.SyncCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- [Fix wave 1, C2 review Finding 1] Sync sırasında hiçbir run BAŞLAMAZ

    // [kullanıcı kararı 2026-10-02] Bu bölümün başındaki test (`A_run_pressed_while_a_sync_is_in_flight_waits_for_it_instead_of_starting`,
    // eski adı `No_run_can_start_while_a_sync_is_in_flight`) silindi — kuyruk yok. "Sync sürerken hiçbir run komutu çalıştırılamaz"
    // iddiası artık `RunRequestDuringWorkTests`'te pinlidir: her iş türü × her run komutu
    // (`Every_run_command_is_not_executable_while_a_sync_clean_optimize_checkout_or_pull_is_in_flight`); kuyruğun kaldırılma
    // gerekçesi o dosyanın doc'undadır. Aşağıdaki dört test kapının Sync BİTİNCE açıldığını pinler. Tek projeli liste ortak
    // `VmTopology.Seed` ile kurulur (kapıyı yalnız süren Sync kapatsın, listesizlik değil).

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-02]</b> Önceki ad ve iddia (<c>A_sync_ending_without_a_project_list_closes_build_and_says_so</c>,
    /// kullanıcı bildirimi 2026-09-29): Sync Build'i kapatmaz (basış bekler); bitişin bildirimi listesiz biten Sync'te
    /// kapıyı kapatır. Kuyruk kaldırıldı; asıl iddia geri geldi: Sync'in kapattığı Build, Sync bitince TEK yerden yeniden
    /// açılır ve bildirimi de o yoldan gelir.
    /// </summary>
    [Fact]
    public async Task Sync_completing_reenables_build_as_well_as_rebuild()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        VmTopology.Seed(vm);
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(vm.BuildCommand.CanExecute(null)); // Sync sürerken kapalı

        bool buildChanged = false;
        vm.BuildCommand.CanExecuteChanged += (_, _) => buildChanged = true;

        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));

        Assert.True(vm.BuildCommand.CanExecute(null));
        Assert.True(buildChanged);
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-02]</b> Önceki ad ve iddia (<c>A_sync_ending_without_a_project_list_closes_rebuild_and_raises_CanExecuteChanged</c>,
    /// kullanıcı bildirimi 2026-09-29): Rebuild'in Sync'le ilişkisi Build'inkiyle aynı kurala bağlıydı. Kuyruk kaldırıldı;
    /// asıl iddia geri geldi: Sync bitince Rebuild yeniden açılır ve <c>CanExecuteChanged</c> atılır.
    /// <para>[Not] Bu Sync'in İÇİNDE bir <c>WorkspaceTopologyEvent</c> GÖNDERİLMEZ: topolojinin gelişi run komutlarını
    /// KENDİSİ yeniden sordurur (<c>OnWorkspaceTopology</c>) — gönderilseydi bildirimin bitişten geldiği ayırt edilemezdi.</para>
    /// </summary>
    [Fact]
    public async Task Sync_completing_reenables_rebuild_and_raises_CanExecuteChanged()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        VmTopology.Seed(vm);
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(vm.RebuildCommand.CanExecute(null));

        bool rebuildChanged = false;
        vm.RebuildCommand.CanExecuteChanged += (_, _) => rebuildChanged = true;

        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));

        Assert.True(vm.RebuildCommand.CanExecute(null));
        Assert.True(rebuildChanged);
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-02]</b> Önceki ad ve iddia (<c>Engine_death_mid_sync_closes_rebuild_via_release_sync_phase_when_no_project_list_came</c>,
    /// kullanıcı bildirimi 2026-09-29): liste yokken Rebuild'i açık tutan süren işti; motor ölünce kapı kapanır. Kuyruk
    /// kaldırıldı; asıl iddia geri geldi: motor Sync ortasında ölünce Rebuild <c>ReleaseSyncPhase</c> üzerinden yeniden
    /// AÇILIR. (Sync düğmesinin aynı yoldan açılması <see cref="Engine_death_mid_sync_reopens_the_sync_gate"/>'te pinlidir.)
    /// </summary>
    [Fact]
    public async Task Engine_death_mid_sync_reenables_rebuild_via_release_sync_phase()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        VmTopology.Seed(vm);
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(vm.RebuildCommand.CanExecute(null));

        bool rebuildChanged = false;
        vm.RebuildCommand.CanExecuteChanged += (_, _) => rebuildChanged = true;

        vm.OnEngineExited(1); // engine Sync ortasında öldü → ReleaseSyncPhase

        Assert.True(vm.RebuildCommand.CanExecute(null));
        Assert.True(rebuildChanged);
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-02]</b> Önceki ad ve iddia (<c>Sync_attributed_planFailed_closes_rebuild_and_raises_CanExecuteChanged_when_no_project_list_came</c>,
    /// kullanıcı bildirimi 2026-09-29): aynı geçiş Rebuild'i liste yokken kapatırdı. Kuyruk kaldırıldı; asıl iddia geri
    /// geldi: Sync'e atfedilen <c>planFailed</c> Rebuild'i AÇAR ve <c>CanExecuteChanged</c> atar ([re-review C2, Finding 4]:
    /// bu, Sync yüzeyini bırakan 4. geçiştir ve bildirimi unutulmuştu). (Sync düğmesinin açılması
    /// <see cref="A_failed_sync_reopens_the_sync_gate"/>'te pinlidir.)
    /// </summary>
    [Fact]
    public async Task Sync_attributed_planFailed_reenables_rebuild_and_raises_CanExecuteChanged()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        VmTopology.Seed(vm);
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(vm.RebuildCommand.CanExecute(null));

        bool rebuildChanged = false;
        vm.RebuildCommand.CanExecuteChanged += (_, _) => rebuildChanged = true;

        // Hiç run başlamadı → TryConsumeSyncFailure normal yola girer: _syncInFlight=false olur ve bildirim BURADA da
        // ateşlenmeli.
        vm.OnEvent(new ErrorEvent("planFailed", "git fetch origin failed"));

        Assert.True(vm.RebuildCommand.CanExecute(null));
        Assert.True(rebuildChanged);
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

    /// <summary>
    /// Kapı her run komutunu kapsar: satırlar bir şekilde dolmuş olsa bile (ör. eski bir koşunun event'leri)
    /// topoloji YOKSA yeni bir run başlatılamaz — ne Build ne Rebuild. Kapı satırlara değil topolojinin
    /// varlığına bakar (<see cref="RunViewModel.HasTopology"/>).
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski ad/iddia:
    /// <c>Retry_failed_is_disabled_without_a_topology_even_with_a_failed_row</c> — başarısız bir satır varken bile
    /// topolojisiz <c>RetryFailed</c> devre dışıdır. Değişme gerekçesi: RetryFailed <c>a2ff12e</c>'de koddan kalktı;
    /// tek assert'i komutla birlikte silindi ve test assert'siz kaldı — hiçbir şey pinlemiyordu. Aynı kural bugünkü
    /// run komutlarına pinlenir.</para>
    /// </summary>
    [Fact]
    public async Task Run_commands_stay_disabled_without_a_topology_even_when_rows_exist()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new ProjectStartedEvent("r0", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectFailedEvent("r0", @"C:\p\a.csproj", 100, "exit 1"));
        Assert.Single(vm.Projects);      // ön-koşul: satır VAR…
        Assert.False(vm.HasTopology);    // …ama topoloji yok

        Assert.False(vm.BuildCommand.CanExecute(null));
        Assert.False(vm.RebuildCommand.CanExecute(null));
    }

    // Kapı bir CanExecute değişimidir: topoloji GELDİĞİNDE butonların yeniden sorgulanması gerekir — CommunityToolkit
    // RelayCommand CommandManager.RequerySuggested'a abone OLMADIĞI için bildirim elle tetiklenmezse gerçek pencerede
    // Build, Sync bittikten sonra da pasif GÖRÜNÜRDÜ.
    [Fact]
    public async Task Topology_arrival_raises_CanExecuteChanged_for_the_run_commands()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        bool buildChanged = false, rebuildChanged = false;
        vm.BuildCommand.CanExecuteChanged += (_, _) => buildChanged = true;
        vm.RebuildCommand.CanExecuteChanged += (_, _) => rebuildChanged = true;

        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", 0)], [], [], []));

        Assert.True(buildChanged);
        Assert.True(rebuildChanged);
    }

    // ---------------------------------------------------------------- [cycles] koşan SCC'nin SATIR görseli

    /// <summary>
    /// [cycles] <b>Grubunu bekleyen üye derleniyor GÖRÜNMEZ.</b> Ara tur sonuçları yayılmadığı için bir SCC'nin
    /// üyeleri grup bitene kadar motor durumunda <c>Started</c> kalır; hangisinin ŞU AN derlendiğini motorun iki
    /// ilanı söyler — <c>ProjectStartedEvent</c> satırı "derleniyor"a alır, <c>CycleMemberHeldEvent</c> ("turdaki
    /// derlemesi bitti, grubunu bekliyor") çıkarır. Bekleyen üye kuyrukta gösterilir.
    ///
    /// <para><b>[DEĞİŞEN KURAL — dalgalı turlar]</b> Eski iddia: "üyeler sıralı invoke edilir, o an derlenen TEK
    /// üye vardır — en son başlayan"; App bir kardeş başladığında öncekileri bekliyor sayardı. Turlar artık
    /// bariyerli dalgalarla koşar ve aynı dalgadaki üyeler BİRLİKTE derlenir: eski kural onları sarı saatle
    /// gösteriyor, işi bitmiş son başlayanı ise dönmeye devam ettiriyordu. Paralellik sınırı artık motorda
    /// korunur — üye ancak slot tutarken "başladı" ilan edilir (CycleRoundsTests §17).</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL — önceki]</b> <c>CycleWaiting</c> eskiden bilerek yalnız sayacı etkiliyordu
    /// ("GÖRSEL durumu DEĞİŞTİRMEZ"). Ölçülen sonuç: 15 üyeli bir grupta listede 15, grafta 15 dönen spinner —
    /// sayaç chip'i "1 building" derken. Satır ile sayaç artık AYNI soruyu aynı şekilde cevaplar.</para>
    /// </summary>
    [Fact]
    public async Task Members_of_a_running_group_compile_together_until_the_engine_holds_each_one()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        StartCycleGroup(vm);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Cycles, TotalProjects: 4, Parallelism: 4, "Debug"));
        vm.OnEvent(new ProjectStartedEvent("r1", A, "A"));
        vm.OnEvent(new ProjectStartedEvent("r1", B, "B"));
        vm.OnEvent(new ProjectStartedEvent("r1", C, "C"));   // tek dalga: üçü birlikte derleniyor

        var a = vm.Projects.Single(p => p.Id == A);
        var b = vm.Projects.Single(p => p.Id == B);
        var c = vm.Projects.Single(p => p.Id == C);
        Assert.All([a, b, c], row => Assert.Equal(GraphStatus.Building, row.Status));
        Assert.Equal(3, vm.Counters.Building);

        vm.OnEvent(new CycleMemberHeldEvent("r1", A));       // A'nın derlemesi bitti — sonucu grubun kararını bekler

        Assert.True(a.CycleWaiting);
        Assert.Equal(GraphStatus.Queued, a.Status);          // bekleyen
        Assert.Equal(GraphStatus.Building, b.Status);        // dalga arkadaşları hâlâ derleniyor
        Assert.Equal(GraphStatus.Building, c.Status);
        // Satır ile sayaç aynı şeyi söyler — bu testin ASIL iddiası; ikisi ayrışamaz.
        Assert.Equal(2, vm.Counters.Building);
        Assert.Equal(2, vm.Projects.Count(p => p.Status == GraphStatus.Building));
    }

    /// <summary>[cycles] Koşu uçuştayken PLANLANMIŞ bir döngü üyesi de kuyrukta görünür — sıradan bir proje
    /// gibi. Döngü glyph'i "derlenmeyecek" çağrışımı taşır ve tam da derlenmek üzere olan bir satırda
    /// yanıltıcıdır. Boştaki (Sync sonrası) hâli DEĞİŞMEZ: orada glyph hâlâ döngüdür.</summary>
    [Fact]
    public async Task A_planned_cycle_member_shows_queued_while_a_run_is_in_flight()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(CycleTopology());

        var a = vm.Projects.Single(p => p.Id == A);
        // [DEĞİŞEN KURAL — design v1.7.0 §5] Üyelik statü değildir: boşta satır Discovered'dır.
        Assert.Equal(GraphStatus.Discovered, a.Status);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Cycles, TotalProjects: 4, Parallelism: 4, "Debug"));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(A, "A", true)]));

        Assert.Equal(GraphStatus.Queued, a.Status);
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
        // run'da "32 building" raporluyordu ve şerit "finishing 32 in flight" yazıyordu.
        // [DEĞİŞEN KURAL — dalgalı turlar] Eskiden App "yalnız SON başlayan üye derleniyor" diye tahmin ederdi
        // (bu test üç başlama olayını arka arkaya verip 1 building bekliyordu). Motor artık her derlemenin bitişini
        // de ilan eder (CycleMemberHeldEvent) ve "başladı"yı yalnız slot tutan üyeye yazar; sayaç ilanları sayar,
        // sınırı motor korur (CycleRoundsTests §17). Test motorun gerçekte gönderdiği sırayı verir.
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        StartCycleGroup(vm);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, TotalProjects: 4, Parallelism: 4, "Debug"));

        vm.OnEvent(new ProjectStartedEvent("r1", A, "A"));
        vm.OnEvent(new CycleMemberHeldEvent("r1", A));
        vm.OnEvent(new ProjectStartedEvent("r1", B, "B"));
        vm.OnEvent(new CycleMemberHeldEvent("r1", B));
        vm.OnEvent(new ProjectStartedEvent("r1", C, "C"));

        Assert.Equal(1, vm.Counters.Building);              // derlenmesi süren tek üye C
        Assert.True(vm.Counters.Building <= vm.Parallelism); // hiçbir koşulda worker sayısını aşamaz
        Assert.Equal(3, vm.Counters.Queued);                // A + B (grubunu bekliyor) + D (hiç başlamadı)
        Assert.Equal(4, vm.Counters.Total);

        // Grup bitince bayrak DÜŞER: bekleyen üye sonsuza dek "queued" görünmez.
        vm.OnEvent(new ProjectSucceededEvent("r1", A, 10, null, false));
        Assert.False(vm.Projects.Single(p => p.Id == A).CycleWaiting);
    }

    /// <summary>
    /// [I2] Kusur: cycle katkısı (paralelliğe BÖLÜNMEYEN, BaselineRounds ile ÇARPILAN terim) yalnız Pending
    /// üyeleri sayıyordu. Grup dispatch edilir edilmez üyeler Started'a geçtiği ve orada KALDIĞI için,
    /// çarpan tam da işin yapıldığı pencerede kayboluyor; üyeler paralelliğe bölünen building kovasına
    /// düşüyordu. Sabit saat: 4 proje, D 1000ms'te bitti ⇒ gözlenen ortalama 1000ms.
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski kurulum koşuyu <c>RunMode.Build</c> ile açıyordu: döngü kovası moddan
    /// bağımsızdı, dolayısıyla mod önemsizdi. Artık kova yalnız turların GERÇEKTEN koştuğu <c>Cycles</c>
    /// koşusuna aittir — gerekçe: Build menüsünün Clean'i döngü üyelerini tur koşmadan, sıradan paralel iş
    /// olarak temizler (<see cref="A_full_clean_estimates_cycle_members_as_ordinary_parallel_work"/>). Bu test
    /// kovanın kendi kuralını, onu kullanan tek koşuda pinler.</para>
    /// </summary>
    [Fact]
    public async Task The_eta_keeps_the_cycle_round_multiplier_while_the_group_is_running()
    {
        long now = 5_000;
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1", () => now);
        StartCycleGroup(vm);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Cycles, TotalProjects: 4, Parallelism: 4, "Debug"));
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

    /// <summary>[Clean] Build menüsünün Clean'i döngü üyelerini de temizler ama TUR KOŞMAZ: motor Clean'de döngü
    /// anlamını düşürür (<c>Core/Planning/CleanRunScope</c>) ve her üye bir kez, sıradan bir proje gibi paralel
    /// temizlenir. Tahmin de onları sıradan kuyruk sayar — tur çarpanı ve bölünmezlik yalnız Cycles koşusunundur
    /// (<see cref="The_eta_keeps_the_cycle_round_multiplier_while_the_group_is_running"/>). Kusurlu hâlde aynı üç üye
    /// 3 × 1000ms × BaselineRounds(2) = 6000ms okunurdu; gerçek iş 3 × 1000ms / 4 worker'dır.</summary>
    [Fact]
    public async Task A_full_clean_estimates_cycle_members_as_ordinary_parallel_work()
    {
        long now = 5_000;
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1", () => now);
        StartCycleGroup(vm);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Clean, TotalProjects: 4, Parallelism: 4, "Debug"));
        vm.OnEvent(new ProjectStartedEvent("r1", D, "D"));
        vm.OnEvent(new ProjectSucceededEvent("r1", D, 1000, null, false));

        Assert.Equal(750, vm.EtaMs);
    }

    // ---------------------------------------------------------------- [Task 5] kümülatif renk · defter üçgeni · nötrleme

    private static string P(string name) => $@"C:\p\{name}.csproj";

    private static BuildPreviewItem Item(string name, bool? willBuild, WillBuildReason? reason,
        bool conditional = false, IReadOnlyList<string>? roots = null, DateTimeOffset? failedAt = null,
        bool localEdits = false) =>
        new(P(name), name, willBuild, Reason: reason, Conditional: conditional, DependencyRoots: roots,
            FailedAt: failedAt, LocalEdits: localEdits);

    /// <summary>Sync'in olay sırası: başladı → topoloji → önizleme → tamamlandı. Koşu yok (<c>IsRunning=false</c>).</summary>
    private static void SyncWith(RunViewModel vm, params BuildPreviewItem[] items)
    {
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        AnswerSync(vm, items);
    }

    /// <summary>Başlamış bir Sync'in cevabı: topoloji → önizleme → tamamlandı.</summary>
    private static void AnswerSync(RunViewModel vm, params BuildPreviewItem[] items)
    {
        vm.OnEvent(new WorkspaceTopologyEvent(
            [.. items.Select((it, i) => Node(it.ProjectId, it.Name, i))], [], [], []));
        vm.OnEvent(new BuildPreviewEvent(items));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, items.Length, 0));
    }

    private static RunViewModel T5Vm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    private static ProjectRowViewModel RowOf(RunViewModel vm, string name) => vm.Projects.Single(r => r.Name == name);

    /// <summary>[design v1.20.0 §2.3 · spec 2026-09-18 §1-2] Sync renk verir: her satır önizlemenin
    /// gerekçesinden kendi çıktı durumuna iner — güncel yeşil, değişmiş gri, kanıtlı hata kırmızı; kararı
    /// olmayan satır başlangıç modunda kalır.</summary>
    [Fact]
    public void Sync_paints_every_row_from_its_reason()
    {
        var vm = T5Vm();
        SyncWith(vm,
            Item("Up", false, WillBuildReason.UpToDate),
            Item("Mod", true, WillBuildReason.SignatureChanged),
            Item("Bad", true, WillBuildReason.LastFailed),
            Item("Unk", null, null));

        Assert.Equal(VisualStatus.Current, RowOf(vm, "Up").VisualStatus);
        Assert.Equal(VisualStatus.Stale, RowOf(vm, "Mod").VisualStatus);
        Assert.Equal(VisualStatus.Failed, RowOf(vm, "Bad").VisualStatus);
        Assert.Equal(VisualStatus.Unknown, RowOf(vm, "Unk").VisualStatus);
    }

    /// <summary>[spec 2026-09-18 §1-2 "kümülatif"] Satırdan tek proje derlemek diğer satırların rengine
    /// DOKUNMAZ: tıklama anındaki nötrleme yalnız koşu alanlarını siler, çıktı durumunu değil.</summary>
    [Fact]
    public async Task A_row_build_leaves_every_other_rows_colour_untouched()
    {
        var vm = T5Vm();
        var items = Enumerable.Range(0, 50).Select(i => Item($"G{i}", false, WillBuildReason.UpToDate))
            .Append(Item("T", true, WillBuildReason.SignatureChanged)).ToArray();
        SyncWith(vm, items);

        await vm.BuildProjectCommand.ExecuteAsync(P("T"));

        Assert.All(vm.Projects.Where(r => r.Name != "T"), r => Assert.Equal(VisualStatus.Current, r.VisualStatus));

        // Koşu yalnız hedefi taşır ve biter — 50 yeşil yine yeşil.
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug"));
        vm.OnEvent(new BuildPreviewEvent([Item("T", true, WillBuildReason.SignatureChanged)]));
        vm.OnEvent(new ProjectStartedEvent("r1", P("T"), "T"));
        vm.OnEvent(new ProjectSucceededEvent("r1", P("T"), 900));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 900));

        Assert.All(vm.Projects.Where(r => r.Name != "T"), r => Assert.Equal(VisualStatus.Current, r.VisualStatus));
        Assert.Equal(VisualStatus.Succeeded, RowOf(vm, "T").VisualStatus);
    }

    /// <summary>[spec 2026-09-18 §1-2] İkinci Build, birincinin yeşillerini ve kanıtlı kırmızılarını
    /// korur: sonuç bir sonraki koşuya kadar durumda yazılı kalır, nötrleme onu silmez.</summary>
    [Fact]
    public async Task A_second_build_keeps_the_greens_of_the_first()
    {
        var vm = T5Vm();
        SyncWith(vm, Item("A", true, WillBuildReason.SignatureChanged), Item("B", true, WillBuildReason.SignatureChanged));
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 4, "Debug"));
        vm.OnEvent(new BuildPreviewEvent([Item("A", true, WillBuildReason.SignatureChanged),
            Item("B", true, WillBuildReason.SignatureChanged)]));
        vm.OnEvent(new ProjectStartedEvent("r1", P("A"), "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", P("A"), 900));
        vm.OnEvent(new ProjectStartedEvent("r1", P("B"), "B"));
        vm.OnEvent(new ProjectFailedEvent("r1", P("B"), 900, "exit 1", Evidence: true));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 1, 0, 0, 1800));

        await vm.BuildCommand.ExecuteAsync(null); // ikinci işlemin tıklama anı — nötrleme

        Assert.Equal(VisualStatus.Current, RowOf(vm, "A").VisualStatus);
        Assert.Equal(VisualStatus.Failed, RowOf(vm, "B").VisualStatus);
    }

    /// <summary>[R-M2 · design v1.20.0 §5] Koşunun atladığı güncel satır koşu bittikten sonra da
    /// <c>Skipped</c> durumunda KALIR (koşu hikâyesi: şerit "N skipped", konsolun "Up to date — nothing to
    /// compile in this run." cümlesi) — ama rengi ve glyph'i çıktı durumundandır: yeşil ✓.</summary>
    [Fact]
    public void A_skipped_up_to_date_row_reads_current_with_a_tick_after_the_run()
    {
        var vm = T5Vm();
        SyncWith(vm, Item("A", true, WillBuildReason.SignatureChanged), Item("U", false, WillBuildReason.UpToDate));
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 4, "Debug"));
        vm.OnEvent(new BuildPreviewEvent([Item("A", true, WillBuildReason.SignatureChanged),
            Item("U", false, WillBuildReason.UpToDate)]));
        vm.OnEvent(new ProjectStartedEvent("r1", P("A"), "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", P("A"), 900));
        vm.OnEvent(new ProjectSkippedEvent("r1", P("U"), SkipReasons.UpToDate));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 1, 0, 900));

        var u = RowOf(vm, "U");
        Assert.Equal(ProjectRowState.Skipped, u.State);                 // koşu hikâyesi durur
        Assert.Equal(1, vm.Counters.Skipped);
        Assert.Contains("1 skipped", vm.RibbonLine.Text, StringComparison.Ordinal);
        Assert.Contains("Up to date — nothing to compile in this run.", ConsoleEmptyState.ForEmptyLog(u));
        Assert.Equal(VisualStatus.Current, u.VisualStatus);             // renk çıktı durumundan
        Assert.Equal("Icon.StatusCheck", StatusGlyph.InnerIconKeyFor(u.VisualStatus));
    }

    /// <summary>[spec 2026-09-18 §1-15 · design v1.20.0 §2.4-6] Üçgen kümülatiftir: defterdeki bağımlılık
    /// notu (<see cref="WillBuildReason.WaitingForDependency"/> + kökleri) Sync'ten gelir ve satır koşu
    /// görmeden de üçgeni taşır; tooltip koşu listesiyle AYNI dili konuşur.</summary>
    [Fact]
    public void A_waiting_row_carries_the_triangle_after_sync()
    {
        var vm = T5Vm();
        SyncWith(vm, Item("Up", true, WillBuildReason.SignatureChanged),
            Item("W", true, WillBuildReason.WaitingForDependency, conditional: true, roots: ["Up"]));

        var w = RowOf(vm, "W");
        Assert.True(w.HasDepIssue);
        Assert.Equal(["Up"], w.WarningRoots);
        Assert.Equal("Dependency issue: Up", RowWarning.For(false, false, false, w.WarningRoots, w.NamePrefix));
        Assert.Equal(VisualStatus.Current, w.VisualStatus); // kendi çıktısı sağlam — bekleyiş üçgende
        Assert.Equal(1, vm.Counters.Warn);
    }

    /// <summary>[spec 2026-09-18 §1-15] Bir sonraki işlemin nötrlemesi defter üçgenini SİLMEZ — yalnız
    /// koşu alanlarını (<c>DepIssues</c>) siler; not düşene kadar üçgen durur.</summary>
    [Fact]
    public async Task The_next_operation_does_not_clear_a_ledger_triangle()
    {
        var vm = T5Vm();
        SyncWith(vm, Item("Up", true, WillBuildReason.SignatureChanged),
            Item("W", true, WillBuildReason.WaitingForDependency, conditional: true, roots: ["Up"]));

        await vm.BuildCommand.ExecuteAsync(null);

        var w = RowOf(vm, "W");
        Assert.Null(w.DepIssues);   // koşu alanı silindi
        Assert.True(w.HasDepIssue); // defter notu durur
        Assert.Equal(["Up"], w.WarningRoots);
    }

    /// <summary>[design v1.20.0 §5] Başlangıç modu yalnız KARARIN yokluğudur: hiç Sync yokken ya da karar
    /// düşürülmüşken; kararı olan her satır (hangi durumda olursa olsun) kendi renginde.</summary>
    [Fact]
    public void Only_a_row_without_a_decision_is_in_start_mode()
    {
        var vm = T5Vm();
        vm.OnEvent(new WorkspaceTopologyEvent([Node(P("A"), "A", 0), Node(P("B"), "B", 1)], [], [], []));
        Assert.All(vm.Projects, r => Assert.True(VisualStatuses.IsStartMode(r.VisualStatus))); // Sync yok

        vm.OnEvent(new BuildPreviewEvent([Item("A", false, WillBuildReason.UpToDate), Item("B", null, null)]));

        Assert.False(VisualStatuses.IsStartMode(RowOf(vm, "A").VisualStatus));
        Assert.True(VisualStatuses.IsStartMode(RowOf(vm, "B").VisualStatus));
    }

    /// <summary>[kullanıcı kararı 2026-09-29] Configuration değişince elde duran kararlar ESKİ configuration'ındır:
    /// geçişin Sync'i başlarken her satır kararını bırakır ve başlangıç moduna (renksiz) iner — güncel, kanıtlı
    /// kırmızı, bağımlılık bekleyen ve hiç derlenmemiş satır ayrımsız; koşullu söz ve uyarı üçgeninin kökleri de
    /// kararla gider. Renk yalnız yeni configuration'ın önizlemesinden gelir.
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski ad/iddia: <c>Switching_configuration_drops_every_decided_row_to_stale</c> —
    /// geçiş anında her kararlı satır bayat griye iner ve gerekçesi "motorun bir sonraki önizlemesiyle AYNI" diye
    /// tahmin edilir (<c>NextPreview.AfterConfigurationChange</c>: başarı izi varsa <c>SignatureChanged</c>, yoksa
    /// <c>NeverBuilt</c>). Değişme gerekçesi (ölçüm): tahmin motorla ayrışıyordu — defter tek imza tutar, Debug'a
    /// dönüşte motor <c>UpToDate</c> der; OSYS'te <c>bin\Release</c> çıktısı olmadığı için motor
    /// <c>OutputMissing</c> ("never built") derken tahmin <c>SignatureChanged</c> ("affected — a dependency
    /// changed") diyordu. Eşleme ve <c>NextPreviewTests</c>'teki testleri kaldırıldı.</para></summary>
    [Fact]
    public void Switching_configuration_drops_every_decision_until_its_sync_answers()
    {
        var vm = T5Vm();
        SyncWith(vm,
            Item("Up", false, WillBuildReason.UpToDate),
            new BuildPreviewItem(P("Bad"), "Bad", true, BuiltCommit: "abc1234", Reason: WillBuildReason.LastFailed),
            Item("W", true, WillBuildReason.WaitingForDependency, conditional: true, roots: ["Up"]),
            Item("New", true, WillBuildReason.NeverBuilt));
        MainWindowHost.AcceptSends(vm);

        vm.SetConfiguration("Release");
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main")); // geçişin Sync'i başladı

        Assert.All(vm.Projects, r => Assert.True(VisualStatuses.IsStartMode(r.VisualStatus), r.Name));
        Assert.All(vm.Projects, r => Assert.Null(r.WillBuildReason));
        Assert.False(RowOf(vm, "W").Conditional);   // koşullu söz de kararla gider
        Assert.False(RowOf(vm, "W").HasDepIssue);   // bilinmeyen bir satır bağımlılık bekleyemez

        AnswerSync(vm,
            Item("Up", true, WillBuildReason.OutputMissing),
            Item("Bad", true, WillBuildReason.OutputMissing),
            Item("W", true, WillBuildReason.OutputMissing),
            Item("New", true, WillBuildReason.NeverBuilt));
        Assert.All(vm.Projects, r => Assert.Equal(VisualStatus.Stale, r.VisualStatus));
    }

    /// <summary>[kullanıcı kararı 2026-09-29 · kullanıcının gördüğü anormallik] Defter proje başına TEK imza tutar: en son
    /// derlendiği configuration'ınkini. Debug'da güncel bir çalışma alanında Release'e geçip — Release'de derlemeden —
    /// Debug'a dönmek hiçbir şeyi derletmez; motor her satırı güncel bulur. Geçiş bunu önceden bilemez, bu yüzden hiçbir
    /// şey vaat etmez: konsol yeniden derleme demez, satırlar Sync'in cevabına kadar renksizdir, cevap gelince yeşildir.
    /// <para>Ölçülen kusur: dönüşte her satır gri ve konsolda "all projects will rebuild"; Build'e basınca açılış dalgası
    /// bütün satırları yakıyor, motor hepsini "up to date" diye atlıyordu.</para></summary>
    [Fact]
    public void Switching_back_to_the_configuration_the_ledger_holds_claims_no_rebuild()
    {
        var vm = T5Vm();
        MainWindowHost.AcceptSends(vm);
        SyncWith(vm, Item("A", false, WillBuildReason.UpToDate), Item("B", false, WillBuildReason.UpToDate));
        vm.SetConfiguration("Release");
        SyncWith(vm, Item("A", true, WillBuildReason.OutputMissing), Item("B", true, WillBuildReason.OutputMissing));

        vm.SetConfiguration("Debug");

        Assert.DoesNotContain("will rebuild", vm.GetRunDocumentText());
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.All(vm.Projects, r => Assert.True(VisualStatuses.IsStartMode(r.VisualStatus), r.Name));
        AnswerSync(vm, Item("A", false, WillBuildReason.UpToDate), Item("B", false, WillBuildReason.UpToDate));
        Assert.All(vm.Projects, r => Assert.Equal(VisualStatus.Current, r.VisualStatus));
        Assert.Equal("▸ Ready — everything looks up to date", vm.RibbonLine.Text);
    }

    /// <summary>[kullanıcı kararı 2026-09-29] Geçiş kimseyi "derlenecek" saymaz: ne döngü üyesini (düz Build onu hiç
    /// derlemez) ne kararı olmayan satırı. Ölçülen kusur: tahmin her satıra <c>WillBuild=true</c> yazıyordu — OSYS'in
    /// 33 döngü üyesi de şeridin "N to build"una giriyor, Build'in açılış dalgası onları da yakıyordu.</summary>
    [Fact]
    public void Switching_configuration_puts_no_cycle_member_or_undecided_row_in_the_next_build()
    {
        var vm = T5Vm();
        MainWindowHost.AcceptSends(vm);
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(P("A"), "A", 0), Node(P("C"), "C", 1, inCycle: true), Node(P("U"), "U", 2)], [], [], []));
        vm.OnEvent(new BuildPreviewEvent([
            Item("A", false, WillBuildReason.UpToDate),
            Item("C", false, WillBuildReason.SignatureChanged), // kapsam dışı döngü üyesi: bayat ama Build derlemez
            Item("U", null, null)]));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 3, 0));
        Assert.Equal(0, vm.WillBuildCount); // ön-koşul: Build'in yapacağı iş yok

        vm.SetConfiguration("Release");

        Assert.DoesNotContain(vm.ScopeFor(RunMode.Build), r => r.Name is "C" or "U");
        Assert.Equal(0, vm.WillBuildCount);
    }

    /// <summary>[kullanıcı kararı 2026-09-29] Bitmiş bir koşudan sonra geçiş, koşunun hikâyesini kendi Sync'ine bırakır:
    /// tıklama anında faz yerinde durur (Sync düğmesiyle AYNI), Sync başlayınca koşu bindirmesi ve kararlar düşer — az
    /// önce başarıyla biten satır koşunun yeşilinde kalmaz, renksizdir — ve Sync bitince şerit yeni configuration'ın
    /// planını okur.
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski ad/iddia: <c>Switching_configuration_after_a_run_drops_the_run_overlay_too</c> —
    /// Sync'siz geçiş tıklama anında koşu alanlarını siler, satırları tahminle bayat griye indirir, fazı Idle'a alır ve
    /// şerit tahmini planı okur. Değişme gerekçesi: tahmin motorla ayrışıyordu (bkz.
    /// <see cref="Switching_configuration_drops_every_decision_until_its_sync_answers"/>); plan artık yalnız yeni
    /// configuration'ın Sync'inden gelir.</para></summary>
    [Fact]
    public void Switching_configuration_after_a_run_hands_its_story_to_the_sync()
    {
        var vm = T5Vm();
        SyncWith(vm, Item("A", true, WillBuildReason.SignatureChanged), Item("U", false, WillBuildReason.UpToDate),
            Item("Unk", null, null));
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug"));
        vm.OnEvent(new BuildPreviewEvent([Item("A", true, WillBuildReason.SignatureChanged),
            Item("U", false, WillBuildReason.UpToDate)]));
        vm.OnEvent(new ProjectStartedEvent("r1", P("A"), "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", P("A"), 900));
        vm.OnEvent(new ProjectSkippedEvent("r1", P("U"), SkipReasons.UpToDate));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 1, 0, 900));
        Assert.Equal(VisualStatus.Succeeded, RowOf(vm, "A").VisualStatus); // ön-koşul
        Assert.Equal(AppPhase.Done, vm.Phase);
        MainWindowHost.AcceptSends(vm);

        vm.SetConfiguration("Release");
        Assert.Equal(AppPhase.Done, vm.Phase); // tıklama kendi başına bir plan anlatmaz — Sync anlatır

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.Equal(AppPhase.Syncing, vm.Phase);
        Assert.All(vm.Projects, r => Assert.Equal(ProjectRowState.Pending, r.State));
        Assert.All(vm.Projects, r => Assert.True(VisualStatuses.IsStartMode(r.VisualStatus), r.Name));
        Assert.Equal(0, vm.Counters.Succeeded); // koşu alanları silindi

        AnswerSync(vm, Item("A", true, WillBuildReason.OutputMissing), Item("U", true, WillBuildReason.OutputMissing),
            Item("Unk", true, WillBuildReason.NeverBuilt));
        Assert.Equal(AppPhase.Idle, vm.Phase);
        Assert.Equal("▸ Ready — 3 to build · 0 up to date", vm.RibbonLine.Text);
    }

    /// <summary>[kullanıcı kararı 2026-09-29] Geçiş DURDURULMUŞ bir koşunun hikâyesini de kendi Sync'iyle kapatır:
    /// durdurulan koşunun planı eski configuration'a aittir, yeni configuration altında onu sürdürmenin anlamı yoktur.
    /// Tıklama anında faz <c>Stopped</c>'da durur; Sync <c>Syncing</c>'den <c>Idle</c>'a taşır ve şerit artık
    /// "▸ Stopped — …" değil yeni configuration'ın planını okur.
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski ad/iddia: <c>Switching_configuration_after_a_stopped_run_closes_its_story</c> —
    /// Sync'siz geçiş fazı tıklama anında <c>Idle</c>'a alır ve şerit tahmini planı okur. Değişme gerekçesi:
    /// <see cref="Switching_configuration_after_a_run_hands_its_story_to_the_sync"/> ile aynı.</para></summary>
    [Fact]
    public void Switching_configuration_after_a_stopped_run_closes_its_story_through_the_sync()
    {
        var vm = T5Vm();
        SyncWith(vm, Item("A", true, WillBuildReason.SignatureChanged), Item("B", true, WillBuildReason.SignatureChanged));
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 4, "Debug"));
        vm.OnEvent(new BuildPreviewEvent([Item("A", true, WillBuildReason.SignatureChanged),
            Item("B", true, WillBuildReason.SignatureChanged)]));
        vm.OnEvent(new ProjectStartedEvent("r1", P("A"), "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", P("A"), 900));
        vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));
        Assert.Equal(AppPhase.Stopped, vm.Phase); // ön-koşul
        Assert.StartsWith("▸ Stopped", vm.RibbonLine.Text, StringComparison.Ordinal);
        MainWindowHost.AcceptSends(vm);

        vm.SetConfiguration("Release");
        Assert.Equal(AppPhase.Stopped, vm.Phase); // hikâyeyi tıklama değil Sync kapatır

        SyncWith(vm, Item("A", true, WillBuildReason.OutputMissing), Item("B", true, WillBuildReason.OutputMissing));
        Assert.Equal(AppPhase.Idle, vm.Phase);
        Assert.Equal("▸ Ready — 2 to build · 0 up to date", vm.RibbonLine.Text);
    }

    /// <summary>[kullanıcı kararı 2026-09-29] Kanıtlı kırmızı, ESKİ configuration'ın kaynağında alınmış bir hatadır:
    /// geçişin Sync'i başlarken satır kırmızısını bırakır ve başlangıç moduna iner — hiç başarısı olmayan ya da bir
    /// başarının ardından patlayan ayrımı yapılmaz, çünkü satır artık hiçbir şey tahmin etmez. Yeni configuration'da ne
    /// olduğunu motorun cevabı söyler.
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski ad/iddia: <c>A_failed_row_drops_to_never_built_only_when_it_never_succeeded</c>
    /// — geçiş anında kırmızı satır, başarı izi varsa (<c>BuiltCommit</c>) <c>SignatureChanged</c>, yoksa
    /// <c>NeverBuilt</c> okunur (<c>NextPreview.AfterConfigurationChange</c>). Değişme gerekçesi:
    /// <see cref="Switching_configuration_drops_every_decision_until_its_sync_answers"/> ile aynı — tahmin kaldırıldı.</para></summary>
    [Fact]
    public void A_failed_row_leaves_its_red_until_the_new_configurations_sync_answers()
    {
        var vm = T5Vm();
        SyncWith(vm,
            new BuildPreviewItem(P("Once"), "Once", true, BuiltCommit: "abc1234", Reason: WillBuildReason.LastFailed),
            new BuildPreviewItem(P("Never"), "Never", true, Reason: WillBuildReason.LastFailed));
        Assert.All(vm.Projects, r => Assert.Equal(VisualStatus.Failed, r.VisualStatus)); // ön-koşul: kırmızı
        MainWindowHost.AcceptSends(vm);

        vm.SetConfiguration("Release");
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));

        Assert.All(vm.Projects, r => Assert.True(VisualStatuses.IsStartMode(r.VisualStatus), r.Name));
        AnswerSync(vm, Item("Once", true, WillBuildReason.OutputMissing), Item("Never", true, WillBuildReason.NeverBuilt));
        Assert.All(vm.Projects, r => Assert.Equal(VisualStatus.Stale, r.VisualStatus));
    }

    /// <summary>[R-M3 · spec 2026-09-18 §1-18] <c>LocalEdits</c> yalnız koşu DIŞINDAKİ bir önizlemeden
    /// (Sync ve bakım sonrası zincirlenen Sync) yazılır; koşu önizlemesi alanı hep <c>false</c> gönderdiği için
    /// onu DEĞİŞTİRMEZ. Koşu bittikten sonraki yeni Sync ise iki yönde de günceller.</summary>
    [Fact]
    public void Local_edits_come_only_from_a_preview_outside_a_run()
    {
        var vm = T5Vm();
        SyncWith(vm, Item("A", true, WillBuildReason.SignatureChanged, localEdits: true),
            Item("B", false, WillBuildReason.UpToDate));
        Assert.True(RowOf(vm, "A").LocalEdits);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug"));
        vm.OnEvent(new BuildPreviewEvent([Item("A", true, WillBuildReason.SignatureChanged),
            Item("B", false, WillBuildReason.UpToDate)])); // koşu önizlemesi: LocalEdits=false
        Assert.True(RowOf(vm, "A").LocalEdits);             // korunur
        vm.OnEvent(new ProjectStartedEvent("r1", P("A"), "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", P("A"), 900));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 900));
        Assert.True(RowOf(vm, "A").LocalEdits);

        SyncWith(vm, Item("A", false, WillBuildReason.UpToDate),
            Item("B", true, WillBuildReason.SignatureChanged, localEdits: true));
        Assert.False(RowOf(vm, "A").LocalEdits); // true → false
        Assert.True(RowOf(vm, "B").LocalEdits);  // false → true
    }

    /// <summary>[R-M4b · spec 2026-09-18 §1-14 · design v1.20.0 §5] Koşudaki hata, MOTORUN kanıt kararıyla
    /// boyanır (<see cref="ProjectFailedEvent.Evidence"/> — defter yazımıyla aynı kapı): kanıt kırmızıdır
    /// (<c>failed</c>); kanıt olmayan hata hemen gridir (<c>never built</c>) — timeout, stop, invoke
    /// hatası VE yakınsamayan bir SCC'nin <c>exit N</c> ile biten üyesi (metin kanıt gibi görünür, defter kanıt
    /// saymaz).
    /// <para><b>[DEĞİŞEN KURAL — R-M4b]</b> İlk hâl App'te <c>Reason</c> metnini sınıflandırıyordu; SCC üyesinde
    /// satır ile bir sonraki Sync ayrışıyordu (review I1).</para>
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-20]</b> Test ayrıca hata ZAMANININ satırda yaşadığını
    /// pinliyordu: Sync'in getirdiği <c>FailedAt</c> satıra taşınır, kanıtlı hata onu ŞİMDİ'ye, başarı ve
    /// kanıtsız hata <c>null</c>'a çeker. O alanın tek okuyucusu <c>failed · 2h</c> kuyruğuydu; yaş kalkınca
    /// alan satırdan da kalktı. Kanıt kuralının KENDİSİ (gerekçe + renk) değişmedi ve burada pinlenmeye devam
    /// ediyor.</para></summary>
    [Fact]
    public void A_run_failure_is_painted_by_the_same_evidence_rule_the_ledger_uses()
    {
        var vm = T5Vm();
        var earlier = new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);
        SyncWith(vm,
            Item("Exit", true, WillBuildReason.SignatureChanged),
            Item("Slow", true, WillBuildReason.SignatureChanged),
            Item("Stop", true, WillBuildReason.SignatureChanged),
            Item("Invoke", true, WillBuildReason.SignatureChanged),
            Item("Cyc", true, WillBuildReason.SignatureChanged),
            Item("Fixed", true, WillBuildReason.LastFailed, failedAt: earlier));
        Assert.Equal(WillBuildReason.LastFailed, RowOf(vm, "Fixed").WillBuildReason); // ön koşul: kırmızı

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 4, 4, "Debug"));
        foreach (var n in new[] { "Exit", "Slow", "Stop", "Invoke", "Cyc", "Fixed" })
            vm.OnEvent(new ProjectStartedEvent("r1", P(n), n));
        vm.OnEvent(new ProjectFailedEvent("r1", P("Exit"), 900, "exit 1", Evidence: true));
        vm.OnEvent(new ProjectFailedEvent("r1", P("Slow"), 900, "timeout"));
        vm.OnEvent(new ProjectFailedEvent("r1", P("Stop"), 900, "stopped"));
        vm.OnEvent(new ProjectFailedEvent("r1", P("Invoke"), 900, "invoke error: file not found"));
        vm.OnEvent(new ProjectFailedEvent("r1", P("Cyc"), 900, "exit 1", Evidence: false)); // yakınsamayan SCC
        vm.OnEvent(new ProjectSucceededEvent("r1", P("Fixed"), 900));

        var exit = RowOf(vm, "Exit");
        Assert.Equal(WillBuildReason.LastFailed, exit.WillBuildReason);
        Assert.Equal(VisualStatus.Failed, exit.VisualStatus);
        foreach (var n in new[] { "Slow", "Stop", "Invoke", "Cyc" })
        {
            var row = RowOf(vm, n);
            Assert.Equal(ProjectRowState.Failed, row.State);            // koşu hikâyesi: bu koşuda patladı
            Assert.Equal(WillBuildReason.NeverBuilt, row.WillBuildReason);
            Assert.Equal(VisualStatus.Stale, row.VisualStatus);         // ama kanıt değil: gri
        }
        Assert.Equal(WillBuildReason.UpToDate, RowOf(vm, "Fixed").WillBuildReason); // başarı kırmızıyı düşürdü
        Assert.Equal(5, vm.Counters.Failed);                             // şerit/konsol koşu hikâyesi değişmez
    }

    /// <summary>[R-D144] İki soru ayrıdır: DURUM yüzeyleri (⚠ chip'i, <c>warn</c> filtresi) defter üçgenini
    /// de sayar; şeridin koşu özeti "(N dependency-affected)" ise yalnız BU koşunun <c>DepIssues</c>'ını.</summary>
    [Fact]
    public void The_ribbons_dependency_affected_counts_only_this_run_while_the_warn_chip_is_cumulative()
    {
        var vm = T5Vm();
        SyncWith(vm, Item("Up", true, WillBuildReason.SignatureChanged),
            Item("Down", true, WillBuildReason.SignatureChanged),
            Item("W", true, WillBuildReason.WaitingForDependency, conditional: true, roots: ["Old"]));
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 4, "Debug"));
        vm.OnEvent(new ProjectStartedEvent("r1", P("Up"), "Up"));
        vm.OnEvent(new ProjectFailedEvent("r1", P("Up"), 900, "exit 1"));
        vm.OnEvent(new ProjectStartedEvent("r1", P("Down"), "Down"));
        vm.OnEvent(new ProjectSucceededEvent("r1", P("Down"), 900, ["Up"]));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 1, 0, 0, 1800));

        Assert.Equal(1, vm.Counters.DepAffected);  // yalnız Down (bu koşu)
        Assert.Contains("(1 dependency-affected)", vm.RibbonLine.Text, StringComparison.Ordinal);
        Assert.Equal(2, vm.Counters.Warn);         // Down (koşu) ∪ W (defter)
        Assert.True(ProjectFilter.Matches(RowOf(vm, "W"), null, new HashSet<string> { ProjectFilter.Warn }));
    }

    /// <summary>[design v1.20.0 §5] Karar düşünce (repo değişimi) defter notu da düşer: bilinmiyor
    /// modundaki satır üçgen taşımaz — not, düşürülen kararın parçasıdır.
    /// <para>[spec 2026-09-18 §1-1/8] Tetik eskiden aktif olmayan bir branch'in seçimiydi; o seçim artık
    /// kararları düşürmez (checkout gelene dek hiçbir şey yapmaz). Kararları satırları KORUYARAK düşüren tek yol
    /// Settings'ten repo değişimidir ve ardından gelen Sync'in listeyi boşaltmadığı durum motorun erişilemez
    /// olduğu durumdur — iddia aynı, tetik o.</para></summary>
    [Fact]
    public async Task Dropping_the_decisions_drops_the_ledger_triangle_too()
    {
        var vm = T5Vm();
        SyncWith(vm, Item("W", true, WillBuildReason.WaitingForDependency, conditional: true, roots: ["Up"]));
        Assert.True(RowOf(vm, "W").HasDepIssue); // ön-koşul
        vm.OnEngineUnavailable(@"D:\missing\BuildOrchestrator.Supervisor.exe"); // Sync gitmez, liste kalır

        await vm.ApplySettingsAsync([], @"D:\other-repo", []);

        Assert.Equal(VisualStatus.Unknown, RowOf(vm, "W").VisualStatus);
        Assert.False(RowOf(vm, "W").HasDepIssue);
        Assert.Equal(0, vm.Counters.Warn);
    }

    /// <summary>[T15 PİN] "dependency still failing" skip'i SONRASI satır: motor durumu <c>Skipped</c>'tır ama
    /// <see cref="VisualStatus"/> kendi ÇIKTI durumundan okunur (<see cref="VisualStatuses.For"/>'un "atlanmak
    /// bir renk değildir" kuralı) — <c>WaitingForDependency</c> yeşildir, "bekliyor" olgusu üçgende söylenir.
    /// Üçgenin (<see cref="RowWarning.For"/>) VE etiketin (<see cref="DecisionLabel.For"/>) AYNI defter
    /// alanlarından (<see cref="ProjectRowViewModel.WarningRoots"/>/<see cref="ProjectRowViewModel.WillBuildReason"/>)
    /// bu skip'ten SONRA da GERÇEKTEN besleniyor mu hiç kanıtlanmamıştı.</summary>
    [Fact]
    public void A_dependency_still_failing_skip_leaves_the_row_current_with_the_warning_and_the_up_to_date_label()
    {
        var vm = T5Vm();
        SyncWith(vm, Item("A", true, WillBuildReason.SignatureChanged),
            Item("Down", true, WillBuildReason.WaitingForDependency, conditional: true, roots: ["A"]));

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 4, "Debug"));
        vm.OnEvent(new BuildPreviewEvent([Item("A", true, WillBuildReason.SignatureChanged),
            Item("Down", true, WillBuildReason.WaitingForDependency, conditional: true, roots: ["A"])]));
        vm.OnEvent(new ProjectStartedEvent("r1", P("A"), "A"));
        vm.OnEvent(new ProjectFailedEvent("r1", P("A"), 900, "exit 1", Evidence: true)); // kök yine hatalı
        vm.OnEvent(new ProjectSkippedEvent("r1", P("Down"), SkipReasons.DependencyStillFailing));

        var down = RowOf(vm, "Down");
        Assert.Equal(ProjectRowState.Skipped, down.State);      // motor: bu koşuda atlandı
        Assert.Equal(VisualStatus.Current, down.VisualStatus);  // ama renk çıktı durumundan — yeşil kalır
        Assert.True(down.HasDepIssue);                          // ⚠ taşır
        Assert.Equal(["A"], down.WarningRoots);
        Assert.Equal("Dependency issue: A", RowWarning.For(false, false, false, down.WarningRoots, down.NamePrefix));
        Assert.Equal("up to date",
            DecisionLabel.For(down.WillBuild, down.WillBuildReason, down.OwnFilesChanged, down.LocalEdits).Word);
    }
}
