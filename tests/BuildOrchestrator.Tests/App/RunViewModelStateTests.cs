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
/// [T12/T43/C2] <see cref="RunViewModel"/>'in C2 omurgası: faz yürüyüşü, seçim/deselect, Sync vs Build/Retry
/// seçim-filtre asimetrisi, Build'in workspace argümanlı gönderimi, koşarken kilit (branch/worktree/
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
    // SONRA da gelebilir ve o yolda runCompleted ASLA yazılmaz. Kümedeki diğer iki kod (planFailed/
    // msbuildNotFound) planlama penceresinde üretilir, faz Running iken gelemez.

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

    // [KALDIRILDI — design v1.7.0 §3.1] Eski test: "reddedilen bir Continue şeridi kırmızıya boyamaz".
    // Continue modu ve onun reddi (noResumableRun) kaldırıldı — böyle bir olay artık hiç üretilmiyor.


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

    /// <summary>
    /// [kullanıcı kararı 2026-09-12] <b>Sync de plan yüzeyini TIKLAMA ANINDA boşaltır</b> — Clean'in birebir
    /// simetriği (<c>CleanCommandTests.Clean_empties_the_project_list_and_the_graph_at_click</c>).
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> Sync eskiden yalnız konsolu ve event stream'i tıklamada temizliyordu; liste
    /// ve graf ekranda ESKİ topolojiyle duruyor, ancak motorun cevabı gelince yenileniyordu. Kullanıcının
    /// gördüğü şey tek bir işlemin iki ayrı sarsıntısıydı: konsol anında boşalıyor, liste bir süre bayat
    /// kalıyor, sonra yerine yenisi geliyordu. Clean'in kuralı buraya da taşındı — aynı karede her şey boşalır,
    /// Sync'in yayınladığı topoloji hepsini birden geri getirir.</para>
    ///
    /// <para>Bedeli Clean'inkiyle AYNI ve bilerek kabul edildi: gönderim düşerse liste boş kalır (burada motor
    /// hiç başlatılmamıştır, yani gönderim SENKRON düşer) — geri getiren şey bir sonraki Sync'tir. Panel yanlış
    /// konuşmaz: faz <see cref="AppPhase.Boot"/>'a döner ve davet hiçbir şey söylemez.</para>
    /// </summary>
    [Fact]
    public async Task Sync_empties_the_project_list_and_the_graph_at_click_like_clean_does()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new WorkspaceTopologyEvent(
            [new ProjectNode(@"C:\p\a.csproj", "A", @"C:\p\a.csproj", ["Osys"], [], 0, null, null, false, null)],
            [], [], []));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(@"C:\p\a.csproj", "A", true)]));
        Assert.Single(vm.Projects);   // ön-koşul: ekranda bir proje ve bir plan var
        Assert.True(vm.HasTopology);
        Assert.Equal(1, vm.WillBuildCount);
        int topologyChanges = 0;
        vm.TopologyChanged += (_, _) => topologyChanges++;

        await vm.SyncCommand.ExecuteAsync(null);

        Assert.Empty(vm.Projects);
        Assert.False(vm.HasTopology);   // graf da boşalır — kabuk TopologyChanged ile yeniden kurar
        Assert.Equal(1, topologyChanges);
        Assert.Equal(0, vm.WillBuildCount);
        Assert.Equal(AppPhase.Boot, vm.Phase);
        Assert.Equal(ListInviteState.None,
            ListInvite.Resolve(vm.HasWorkspace, vm.Phase, vm.Projects.Count, vm.VisibleProjects.Count));
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
    /// işlemin satırları yazılır" — Build/Rebuild/Cycles bunu <c>BeginRunAsync(clearBuffers:true)</c> ile zaten
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
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
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

    [Fact]
    public async Task Build_and_retry_clear_both_selection_and_filter()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

        vm.SelectProject(@"C:\p\a.csproj");
        vm.ToggleFilter(ProjectFilter.Building);
        await vm.BuildCommand.ExecuteAsync(null);
        Assert.Null(vm.SelectedProjectId);
        Assert.Empty(vm.ActiveFilters);

        vm.SelectProject(@"C:\p\b.csproj");
        vm.ToggleFilter(ProjectFilter.Failed);
        await vm.RebuildCommand.ExecuteAsync(null);
        Assert.Null(vm.SelectedProjectId);
        Assert.Empty(vm.ActiveFilters);
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

    // [KALDIRILDI — design v1.7.0 §2.7-11] Eski test: "Retry failed komutu RunMode.RetryFailed gönderir ve
    // yalnız bir failure varken etkindir". Komut kaldırıldı; hata sonrası kümenin hâlâ derlendiği
    // Incremental/BuildAfterFailureTests'te pinlenir.

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
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0, CpuCapPercent: 70));
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
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0));
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
    // analiz kuyruklatır (tarama + graf + topo + iki incremental geçiş) ve her basış ÜÇ komut gönderir
    // (sync + listBranches + listWorktrees) — konsolda aynı transkript iki kez akıyor, şerit
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

    // ---------------------------------------------------------------- [Fix wave 1, C2 review Finding 1] Sync sırasında hiçbir run başlatılamaz

    /// <summary>
    /// <b>[DEĞİŞEN KURAL]</b> Eskiden bu test "Build sync sırasında da ETKİN" diye pinliyordu: prototipin
    /// (<c>BuildApp.jsx:1194</c> <c>doBuild</c>) kasıtlı asimetrisi taşınmıştı — <c>doRebuild</c>/<c>doRetry</c>
    /// erken döner, <c>doBuild</c> dönmezdi.
    ///
    /// <para><b>Neden ayrıldık (ölçüldü):</b> Supervisor Sync boyunca komut döngüsünü BLOKLAR
    /// (<c>SupervisorHost.SyncWorkspaceAsync</c>). Mid-Sync basılan Build o yüzden yalnız kuyruğa girmiyor,
    /// başkasının transkriptinin ORTASINA düşüyordu: <c>BeginRunAsync</c> konsol tamponlarını ANINDA temizleyip
    /// "build requested" yazıyor, ama motor hâlâ Sync'in içinde olduğu için kalan <c>syncProgress</c> satırları
    /// aynı run dokümanına akıyor ve okuyucuda iki hikâye iç içe geçiyordu. Rebuild'in bu yüzden bloklandığı
    /// zaten yazılıydı; Build'in serbest kalması aynı bedeli ödüyordu.</para>
    ///
    /// <para>Kapı artık TEK predicate'tir (<c>CanRebuildOrRetry</c> → <c>SyncBusy</c>) ve üç run komutunun
    /// üçünü de kapsar. Sync saniyeler sürdüğü için pratikte görünmez: Sync biter bitmez üçü de geri açılır
    /// (aşağıdaki <c>Sync_completing_reenables…</c> testi).</para>
    /// </summary>
    [Fact]
    public async Task No_run_can_start_while_a_sync_is_in_flight()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        vm.OnEvent(new ProjectStartedEvent("r0", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectFailedEvent("r0", @"C:\p\a.csproj", 100, "exit 1"));
        Assert.True(vm.BuildCommand.CanExecute(null)); // ön-koşul: Sync'ten ÖNCE açık

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));

        Assert.False(vm.RebuildCommand.CanExecute(null));
        Assert.False(vm.BuildCommand.CanExecute(null)); // [DEĞİŞEN KURAL] Build de bekler — gerekçe doc'ta
    }

    [Fact] // Kapı Sync bitince TEK yerden açılır — Build'in bildirimi de o yoldan gelir (RelayCommand requery etmez).
    public async Task Sync_completing_reenables_build_as_well_as_rebuild()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        VmTopology.Seed(vm);
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(vm.BuildCommand.CanExecute(null));

        bool buildChanged = false;
        vm.BuildCommand.CanExecuteChanged += (_, _) => buildChanged = true;

        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));

        Assert.True(vm.BuildCommand.CanExecute(null));
        Assert.True(buildChanged);
    }

    [Fact]
    public async Task Sync_completing_reenables_rebuild_and_raises_CanExecuteChanged()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        vm.OnEvent(new ProjectStartedEvent("r0", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectFailedEvent("r0", @"C:\p\a.csproj", 100, "exit 1"));
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(vm.RebuildCommand.CanExecute(null));

        bool rebuildChanged = false;
        vm.RebuildCommand.CanExecuteChanged += (_, _) => rebuildChanged = true;

        // [Not] Bu Sync'in İÇİNDE ayrıca bir WorkspaceTopologyEvent GÖNDERİLMEZ: IsRunning false iken satır
        // durumlarını Pending'e resetler (Sync = yeni taban) — bu testin konusu DEĞİL. Baştaki
        // VmTopology.Seed satır event'lerinden ÖNCE koştuğu için bu kısıtı bozmaz.
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));

        Assert.True(vm.RebuildCommand.CanExecute(null));
        Assert.True(rebuildChanged);
    }

    [Fact]
    public async Task Engine_death_mid_sync_reenables_rebuild_via_release_sync_phase()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        vm.OnEvent(new ProjectStartedEvent("r0", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectFailedEvent("r0", @"C:\p\a.csproj", 100, "exit 1"));
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(vm.RebuildCommand.CanExecute(null));

        vm.OnEngineExited(1); // engine Sync ortasında öldü → ReleaseSyncPhase

        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    [Fact] // [re-review C2, Finding 4] Sync'e atfedilen planFailed (normal başarısız-sync yolu) da CanExecuteChanged tetiklemeli
    public async Task Sync_attributed_planFailed_reenables_rebuild_and_raises_CanExecuteChanged()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        vm.OnEvent(new ProjectStartedEvent("r0", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectFailedEvent("r0", @"C:\p\a.csproj", 100, "exit 1"));
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(vm.RebuildCommand.CanExecute(null));

        bool rebuildChanged = false;
        vm.RebuildCommand.CanExecuteChanged += (_, _) => rebuildChanged = true;

        // IsStarting false (hiç run başlamadı) → TryConsumeSyncFailure normal yola girer: _syncInFlight=false
        // olur ama Fix wave 1'in kaçırdığı 4. geçiş burasıdır — notify BURADA da ateşlenmeli.
        vm.OnEvent(new ErrorEvent("planFailed", "git fetch origin failed"));

        Assert.True(rebuildChanged);
        Assert.True(vm.RebuildCommand.CanExecute(null));
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

    // Kapı her run komutunu kapsar: satırlar bir şekilde dolmuş olsa
    // bile (ör. eski bir koşunun event'leri) topoloji YOKSA yeni bir run başlatılamaz.
    [Fact]
    public async Task Retry_failed_is_disabled_without_a_topology_even_with_a_failed_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        vm.OnEvent(new ProjectStartedEvent("r0", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectFailedEvent("r0", @"C:\p\a.csproj", 100, "exit 1"));

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

        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", 0)], [], [], []));

        Assert.True(buildChanged);
        Assert.True(rebuildChanged);
    }

    // ---------------------------------------------------------------- [cycles] koşan SCC'nin SATIR görseli

    /// <summary>
    /// [cycles] <b>Sırasını bekleyen üye derleniyor GÖRÜNMEZ.</b> Bir SCC'nin üyeleri sıralı invoke edilir ve
    /// ara tur sonuçları yayılmadığı için grup bitene kadar hepsi motor durumunda <c>Started</c> kalır — ama
    /// o an gerçekten derlenen TEK üye vardır. Bekleyen üyeler kuyrukta gösterilir.
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> <c>CycleWaiting</c> eskiden bilerek yalnız sayacı etkiliyordu ("GÖRSEL
    /// durumu DEĞİŞTİRMEZ"). Ölçülen sonuç: 15 üyeli bir grupta listede 15, grafta 15 dönen spinner —
    /// sayaç chip'i "1 building" derken. Ekran, aracın aynı anda on beş iş yaptığını söylüyordu; bir iş
    /// yapıyordu. Satır ile sayaç artık AYNI soruyu aynı şekilde cevaplar.</para>
    /// </summary>
    [Fact]
    public async Task A_member_waiting_its_turn_inside_a_running_group_renders_queued_not_building()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        StartCycleGroup(vm);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Cycles, TotalProjects: 4, Parallelism: 4, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", A, "A"));
        vm.OnEvent(new ProjectStartedEvent("r1", B, "B"));
        vm.OnEvent(new ProjectStartedEvent("r1", C, "C"));   // sıra C'de

        var a = vm.Projects.Single(p => p.Id == A);
        var c = vm.Projects.Single(p => p.Id == C);

        Assert.True(a.CycleWaiting);
        Assert.Equal(GraphStatus.Queued, a.Status);     // bekleyen
        Assert.Equal(GraphStatus.Building, c.Status);   // sırası ONDA
        // Satır ile sayaç aynı şeyi söyler — bu testin ASIL iddiası; ikisi ayrışamaz.
        Assert.Equal(1, vm.Counters.Building);
        Assert.Equal(1, vm.Projects.Count(p => p.Status == GraphStatus.Building));
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

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Cycles, TotalProjects: 4, Parallelism: 4, "Debug", 0));
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

    // ---------------------------------------------------------------- [Task 5] kümülatif renk · defter üçgeni · nötrleme

    private static string P(string name) => $@"C:\p\{name}.csproj";

    private static BuildPreviewItem Item(string name, bool? willBuild, WillBuildReason? reason,
        bool conditional = false, IReadOnlyList<string>? roots = null, DateTimeOffset? failedAt = null,
        bool localEdits = false) =>
        new(P(name), name, willBuild, Reason: reason, Conditional: conditional, DependencyRoots: roots,
            FailedAt: failedAt, LocalEdits: localEdits);

    /// <summary>Sync'in olay sırası: topoloji → önizleme → tamamlandı. Koşu yok (<c>IsRunning=false</c>).</summary>
    private static void SyncWith(RunViewModel vm, params BuildPreviewItem[] items)
    {
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
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
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0));
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
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 4, "Debug", 0));
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
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 4, "Debug", 0));
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

    /// <summary>[T4 review ledger (a)] Configuration değişince konsol "all projects will rebuild" der — satır
    /// da aynı şeyi söylemelidir: güncel ya da kanıtlı kırmızı satır bayat griye düşer. Gerekçe motorun bir
    /// sonraki önizlemesiyle AYNIDIR: configuration imzaya girer (<c>BuildSignature</c>), yani kaydı olan
    /// her proje <see cref="WillBuildReason.SignatureChanged"/>'dir; kaydı hiç olmayan
    /// <see cref="WillBuildReason.NeverBuilt"/> kalır.</summary>
    [Fact]
    public void Switching_configuration_drops_every_decided_row_to_stale()
    {
        var vm = T5Vm();
        SyncWith(vm,
            Item("Up", false, WillBuildReason.UpToDate),
            // Kaydında bir başarı olan kanıtlı hata (BuiltCommit dolu) — hiç başarısı olmayanı alttaki test sınar.
            new BuildPreviewItem(P("Bad"), "Bad", true, BuiltCommit: "abc1234", Reason: WillBuildReason.LastFailed),
            Item("W", true, WillBuildReason.WaitingForDependency, conditional: true, roots: ["Up"]),
            Item("New", true, WillBuildReason.NeverBuilt));

        vm.SetConfiguration("Release");

        Assert.All(vm.Projects, r => Assert.Equal(VisualStatus.Stale, r.VisualStatus));
        Assert.Equal(WillBuildReason.SignatureChanged, RowOf(vm, "Up").WillBuildReason);
        Assert.Equal(WillBuildReason.SignatureChanged, RowOf(vm, "Bad").WillBuildReason);
        Assert.Equal(WillBuildReason.SignatureChanged, RowOf(vm, "W").WillBuildReason);
        Assert.False(RowOf(vm, "W").Conditional);   // imza değişti: artık kesin derlenir
        Assert.False(RowOf(vm, "W").HasDepIssue);   // not imza değişince karar terimi değil
        Assert.Equal(WillBuildReason.NeverBuilt, RowOf(vm, "New").WillBuildReason);
    }

    /// <summary>[R-Config] Configuration değişimi koşu alanlarını da siler (nötrleme — aynı metot): az önce
    /// başarıyla biten satır koşunun yeşilinde KALMAZ, herkes gibi yeni bayat durumuna iner; kararı olmayan
    /// satır kararsız (bilinmiyor) kalır. Koşu hikâyesi de biter: şerit bitmiş koşunun özetini (artık sıfır
    /// sayaçlarla) okumaz, "Ready" satırına döner.</summary>
    [Fact]
    public void Switching_configuration_after_a_run_drops_the_run_overlay_too()
    {
        var vm = T5Vm();
        SyncWith(vm, Item("A", true, WillBuildReason.SignatureChanged), Item("U", false, WillBuildReason.UpToDate),
            Item("Unk", null, null));
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([Item("A", true, WillBuildReason.SignatureChanged),
            Item("U", false, WillBuildReason.UpToDate)]));
        vm.OnEvent(new ProjectStartedEvent("r1", P("A"), "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", P("A"), 900));
        vm.OnEvent(new ProjectSkippedEvent("r1", P("U"), SkipReasons.UpToDate));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 1, 0, 900));
        Assert.Equal(VisualStatus.Succeeded, RowOf(vm, "A").VisualStatus); // ön-koşul

        vm.SetConfiguration("Release");

        Assert.All(vm.Projects, r => Assert.Equal(ProjectRowState.Pending, r.State));
        Assert.Equal(VisualStatus.Stale, RowOf(vm, "A").VisualStatus);
        Assert.Equal(WillBuildReason.SignatureChanged, RowOf(vm, "A").WillBuildReason);
        Assert.Equal(VisualStatus.Stale, RowOf(vm, "U").VisualStatus);
        Assert.Equal(VisualStatus.Unknown, RowOf(vm, "Unk").VisualStatus); // karar yok → bilinmiyor
        Assert.Null(RowOf(vm, "Unk").WillBuildReason);
        Assert.Equal(0, vm.Counters.Succeeded);                            // koşu alanları silindi
        Assert.Equal(AppPhase.Idle, vm.Phase);                             // bitmiş koşunun özeti kalkar
        Assert.Equal("▸ Ready — 3 to build · 0 up to date", vm.RibbonLine.Text);
    }

    /// <summary>[R-Config · fix round 2] Configuration değişimi DURDURULMUŞ bir koşunun hikâyesini de kapatır:
    /// durdurulan koşunun planı eski configuration'a aittir, yeni configuration altında onu sürdürmenin anlamı
    /// yoktur. Faz <c>Stopped</c>'dan <c>Idle</c>'a döner (Done ile AYNI yol) ve şerit artık
    /// "▸ Stopped — 0/N · N not built" değil yeni planı okur.</summary>
    [Fact]
    public void Switching_configuration_after_a_stopped_run_closes_its_story()
    {
        var vm = T5Vm();
        SyncWith(vm, Item("A", true, WillBuildReason.SignatureChanged), Item("B", true, WillBuildReason.SignatureChanged));
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 4, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([Item("A", true, WillBuildReason.SignatureChanged),
            Item("B", true, WillBuildReason.SignatureChanged)]));
        vm.OnEvent(new ProjectStartedEvent("r1", P("A"), "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", P("A"), 900));
        vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));
        Assert.Equal(AppPhase.Stopped, vm.Phase); // ön-koşul
        Assert.StartsWith("▸ Stopped", vm.RibbonLine.Text, StringComparison.Ordinal);

        vm.SetConfiguration("Release");

        Assert.Equal(AppPhase.Idle, vm.Phase);
        Assert.Equal("▸ Ready — 2 to build · 0 up to date", vm.RibbonLine.Text);
    }

    /// <summary>[R-Config · M6] <see cref="WillBuildReason.LastFailed"/> bir satır configuration değişince motorun
    /// bir sonraki önizlemesinin diyeceğini der: kaydında bir BAŞARI varsa (<c>BuiltSignature</c> dolu)
    /// <c>SignatureChanged</c>, hiç başarı yoksa <c>NeverBuilt</c> (<c>WillBuildEvaluator</c>). App
    /// <c>BuiltSignature</c>'ı görmez; başarı izi olarak önizlemenin <c>BuiltCommit</c>'i (satırda
    /// <c>CurrentSha</c>) okunur — defterde onu yalnız başarı yazar. <c>LastBuiltAt</c> ayırıcı OLAMAZ: son koşu
    /// başarısızsa her LastFailed satırında null'dır (<c>BuildStateStore.LastBuiltAtOf</c>).</summary>
    [Fact]
    public void A_failed_row_drops_to_never_built_only_when_it_never_succeeded()
    {
        var vm = T5Vm();
        SyncWith(vm,
            new BuildPreviewItem(P("Once"), "Once", true, BuiltCommit: "abc1234", Reason: WillBuildReason.LastFailed),
            new BuildPreviewItem(P("Never"), "Never", true, Reason: WillBuildReason.LastFailed));

        vm.SetConfiguration("Release");

        Assert.Equal(WillBuildReason.SignatureChanged, RowOf(vm, "Once").WillBuildReason);
        Assert.Equal(WillBuildReason.NeverBuilt, RowOf(vm, "Never").WillBuildReason);
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

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug", 0));
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
    /// (<c>failed · just now</c>); kanıt olmayan hata hemen gridir (<c>never built</c>) — timeout, stop, invoke
    /// hatası VE yakınsamayan bir SCC'nin <c>exit N</c> ile biten üyesi (metin kanıt gibi görünür, defter kanıt
    /// saymaz). Başarı hata zamanını düşürür; Sync'in getirdiği hata zamanı satıra taşınır.
    /// <para><b>[DEĞİŞEN KURAL — R-M4b]</b> İlk hâl App'te <c>Reason</c> metnini sınıflandırıyordu; SCC üyesinde
    /// satır ile bir sonraki Sync ayrışıyordu (review I1).</para></summary>
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
        Assert.Equal(earlier, RowOf(vm, "Fixed").FailedAt); // Sync'in hata zamanı satırda

        var before = DateTimeOffset.Now;
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 4, 4, "Debug", 0));
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
        Assert.NotNull(exit.FailedAt);
        Assert.True(exit.FailedAt >= before);
        Assert.Equal(VisualStatus.Failed, exit.VisualStatus);
        foreach (var n in new[] { "Slow", "Stop", "Invoke", "Cyc" })
        {
            var row = RowOf(vm, n);
            Assert.Equal(ProjectRowState.Failed, row.State);            // koşu hikâyesi: bu koşuda patladı
            Assert.Equal(WillBuildReason.NeverBuilt, row.WillBuildReason);
            Assert.Null(row.FailedAt);
            Assert.Equal(VisualStatus.Stale, row.VisualStatus);         // ama kanıt değil: gri
        }
        Assert.Null(RowOf(vm, "Fixed").FailedAt);
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
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 4, "Debug", 0));
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

    /// <summary>[design v1.20.0 §5] Karar düşünce (branch değişimi) defter notu da düşer: bilinmiyor
    /// modundaki satır üçgen taşımaz — not, düşürülen kararın parçasıdır.</summary>
    [Fact]
    public void Dropping_the_decisions_drops_the_ledger_triangle_too()
    {
        var vm = T5Vm();
        SyncWith(vm, Item("W", true, WillBuildReason.WaitingForDependency, conditional: true, roots: ["Up"]));
        Assert.True(RowOf(vm, "W").HasDepIssue); // ön-koşul

        vm.SelectBranch(new BranchRef("feature/x", "bbbbbbbccccc", false, false));

        Assert.Equal(VisualStatus.Unknown, RowOf(vm, "W").VisualStatus);
        Assert.False(RowOf(vm, "W").HasDepIssue);
        Assert.Equal(0, vm.Counters.Warn);
    }
}
