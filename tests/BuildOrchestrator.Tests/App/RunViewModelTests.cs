using System.Collections.Specialized;
using System.IO;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [Task 12] RunViewModel: event → satır/proje durumu, elapsed, log yükleme dikişi, Stop/Continue komut
/// gönderimi. <see cref="RunViewModel.OnEvent"/> HERHANGİ bir thread'den (test thread'i dahil) doğrudan
/// çağrılabilir — VM'in kendisi Dispatcher/AvalonEdit türü TAŞIMAZ (UI-thread-agnostic çekirdek).
/// Determinizm [D8]: sleep/poll yok — Stop/Continue testleri gerçek Supervisor process'i üzerinden
/// TaskCompletionSource ile event bekler (EngineHostTests/RunCoordinatorTests ile aynı desen).
/// </summary>
public class RunViewModelTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    // [B1/F1 · fix-1] Bu dosyada GERÇEK Supervisor process'i BAŞLATAN (StartAsync çağıran) sekiz test var ve
    // hepsi aynı köke bağlı: üretim varsayılanı (5s, EngineHost.StartupTimeout) yük altında yetmiyor. fix-1
    // öncesi bunlardan yalnız ÜÇÜ (ilk ölçülenler) yamalıydı; yük altındaki koşumda yamasız kalanlardan ikisi
    // (Continue_sends_StartRunCommand_with_ContinueMode + OnEngineExited_while_IsStarting_...) yine
    // EngineHost.StartAsync'te TimeoutException ile düştü — bkz. task-B1-report.md İŞ 4. Artık SEKİZİ DE
    // enjekte ediyor; StartAsync ÇAĞIRMAYAN diğer engine'ler (çoğunluk) bu süreyi hiç kullanmadığı için
    // dokunulmadı. Sabitin tek sahibi TestPaths.WideStartupTimeout; üretim varsayılanı DEĞİŞMEDİ.
    private static readonly TimeSpan WideStartupTimeout = TestPaths.WideStartupTimeout;

    // ---------------------------------------------------------------- 1) satır ekleme/güncelleme (saf OnEvent)

    [Fact]
    public async Task ProjectStarted_adds_a_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // hiç başlatılmadı — OnEvent engine'e dokunmaz
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(@"C:\p\a.csproj", row.Id);
        Assert.Equal("A", row.Name);
        Assert.Equal(ProjectRowState.Started, row.State);
    }

    [Fact]
    public async Task Selecting_a_project_flows_IsSelected_to_the_matching_row_and_toggles_off_on_repeat()
    {
        // [D1 · C1 debt] Seçim RunViewModel.SelectedProjectId'de yaşar; kart görsel seçili durumunu satır
        // VM'inin IsSelected'ından okur (kanonik same-click deselect korunur).
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\b.csproj", "B"));
        var a = vm.Projects.Single(p => p.Id == @"C:\p\a.csproj");
        var b = vm.Projects.Single(p => p.Id == @"C:\p\b.csproj");

        vm.SelectProject(a.Id);
        Assert.True(a.IsSelected);
        Assert.False(b.IsSelected);

        vm.SelectProject(b.Id); // seçim taşınır — eski satır bırakılır
        Assert.False(a.IsSelected);
        Assert.True(b.IsSelected);

        vm.SelectProject(b.Id); // aynı satıra tekrar → deselect
        Assert.False(b.IsSelected);
        Assert.Null(vm.SelectedProjectId);
    }

    [Fact]
    public async Task Topology_carries_the_solution_name_onto_each_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        var node = new ProjectNode(@"C:\p\a.csproj", "A", @"C:\p\a.csproj",
            SolutionNames: ["Osys.sln"], Dependencies: [], BuildOrder: 0,
            LayerIndex: null, LayerName: null, InCycle: false, WillBuild: null);
        vm.OnEvent(new WorkspaceTopologyEvent([node], [], [], []));

        Assert.Equal("Osys.sln", Assert.Single(vm.Projects).SolutionName);
    }

    /// <summary>[Fix wave 1, Finding 1] cycle verisi IPC'de VAR: <c>ProjectNode.InCycle</c> topolojiden satıra
    /// taşınır ve <c>Status</c> onu — <b>satır hakkında bu koşuda henüz bir şey söylenmemişken</b> — cycle
    /// görsel statüsüne çevirir.
    /// <para><b>[DEĞİŞEN KURAL]</b> İkinci iddia tersine döndü. Eskiden pre-skip edilen bir üyede görsel
    /// "cycle KALIR (skipped değil)" diye pinliydi; şimdi motor konuştuğunda glyph MOTORUN cevabını gösterir
    /// (<c>Skipped</c>) ve döngü üyeliği dep-slotundaki rozete taşınır. Gerekçe ölçüldü: eski kuralla bir
    /// Build'den sonra döngüdeki her satır Sync'ten hemen sonraki hâliyle BİREBİR aynı görünüyordu — "bu koşu
    /// onları atladı" ile "bunlar bir döngüde" ayırt edilemiyordu; ve döngüleri gerçekten derleyen koşu
    /// (<c>RunMode.Cycles</c>) geldiğinde aynı kural sonucu da gizlerdi. Rozetin kendisi
    /// <c>ProjectRowTests</c>'te pinlidir.</para></summary>
    [Fact]
    public async Task A_cycle_member_row_keeps_its_real_status_and_carries_membership_separately()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        var node = new ProjectNode(@"C:\p\a.csproj", "A", @"C:\p\a.csproj",
            SolutionNames: ["Osys.sln"], Dependencies: [], BuildOrder: 0,
            LayerIndex: null, LayerName: null, InCycle: true, WillBuild: null);
        vm.OnEvent(new WorkspaceTopologyEvent([node], [], [], []));

        var row = Assert.Single(vm.Projects);
        Assert.True(row.InCycle);
        // [DEĞİŞEN KURAL — design v1.7.0 §5] Üyelik STATÜ DEĞİLDİR: Sync sonrası satır hâlâ Discovered'dır,
        // üyelik kendi kanalında (nokta + uyarı üçgeni + graf çekirdeği) yaşar ve statüyü asla gizlemez.
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Discovered, row.Status);

        // Motor konuştu: glyph ONUN cevabıdır; üyelik satırda (InCycle) DURUR.
        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\a.csproj", SkipReasons.InDependencyCycle));
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Skipped, row.Status);
        Assert.True(row.InCycle);
    }

    // [Fix wave 1, Finding 1] queued verisi TÜRETİLİR: willBuild=true + Pending + run uçuşta → Queued;
    // run bitince (IsRunning düşer) yine Discovered.
    [Fact]
    public async Task A_planned_pending_row_is_queued_during_a_run_and_discovered_once_it_ends()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0)); // IsRunning=true
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(@"C:\p\a.csproj", "A", true)]));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(ProjectRowState.Pending, row.State);
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Queued, row.Status); // planlanmış, henüz başlamadı

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500)); // IsRunning=false
        Assert.Equal(BuildOrchestrator.App.Controls.GraphStatus.Discovered, row.Status); // run bitti → dinlenme
    }

    // [Fix wave 1, Minor 6] TickElapsed building satırların CANLI süresini ilerletir; building OLMAYAN satırlara
    // dokunmaz. Deterministik saat enjekte edilir (D8: sleep/poll yok).
    [Fact]
    public async Task TickElapsed_advances_only_the_building_rows_live_duration()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        long clock = 0;
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1", () => clock);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 2, 2, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A")); // building (started at 0)
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\b.csproj", "B"));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\b.csproj", 1000)); // terminal, DurationMs=1000

        clock = 5000;
        vm.TickElapsed();

        var a = vm.Projects.Single(p => p.Id == @"C:\p\a.csproj");
        var b = vm.Projects.Single(p => p.Id == @"C:\p\b.csproj");
        Assert.Equal(5000, a.DurationMs); // building → canlı ilerledi
        Assert.Equal(1000, b.DurationMs); // succeeded → dokunulmadı
    }

    [Fact]
    public async Task ProjectSucceeded_updates_state_and_duration()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));

        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 2400));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(ProjectRowState.Succeeded, row.State);
        Assert.Equal(2400, row.DurationMs);
    }

    [Fact]
    public async Task ProjectFailed_updates_state_and_duration()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\b.csproj", "B"));

        vm.OnEvent(new ProjectFailedEvent("r1", @"C:\p\b.csproj", 900, "exit 1"));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(ProjectRowState.Failed, row.State);
        Assert.Equal(900, row.DurationMs);
    }

    [Fact] // cycle üyeleri Started OLMADAN doğrudan Skipped gelir (RunCoordinator PreSkipped) — satır yine de eklenmeli
    public async Task ProjectSkipped_without_a_prior_started_still_adds_a_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\p\x.csproj", "bağımlılık döngüsünde"));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(ProjectRowState.Skipped, row.State);
    }

    // ---------------------------------------------------------------- 2) elapsed

    [Fact]
    public async Task RunStarted_sets_ElapsedMs_from_ElapsedMsAtStart()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 177, 6, "Debug", ElapsedMsAtStart: 4200));

        Assert.Equal(4200, vm.ElapsedMs);
        Assert.True(vm.IsRunning);
    }

    [Fact]
    public async Task RunCompleted_stops_the_clock_at_the_engine_reported_duration()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, DurationMs: 9999));

        Assert.Equal(9999, vm.ElapsedMs);
        Assert.False(vm.IsRunning);
    }

    // ---------------------------------------------------------------- 3) log yükleme dikişi

    [Fact]
    public async Task LoadProjectLogAsync_stitches_only_buffered_lines_after_ThroughLineNumber_no_duplicates()
    {
        // Engine hiç başlatılmadı: LoadProjectLogAsync'in SendAsync'i senkron fırlar ve VM içinde yutulur —
        // dikiş TAMAMEN yerel state'ten (buffered projectLog + chunk) üretilir, gerçek IPC gerekmez.
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\a.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));
        for (int i = 1; i <= 4; i++)
            vm.OnEvent(new ProjectLogEvent("r1", projectId, i, $"line{i}"));

        // [D4 review §2] Üretimde kart seçimi (SelectedProjectId) LoadProjectLogAsync'ten ÖNCE kurulur
        // (OnSelectedProjectChangedAsync onu SelectedProjectId değişimiyle tetikler); proje modu ancak seçim hâlâ
        // o projedeyse kurulur. Testler bu koşulu birebir modellemeli.
        vm.SelectProject(projectId);
        var load = vm.LoadProjectLogAsync(projectId); // pending state SENKRON kurulur (ilk await'e kadar)
        vm.OnEvent(new ProjectLogChunkEvent(projectId, Sequence: 0, "line1\nline2\n", IsLast: true, ThroughLineNumber: 2));
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("line1\nline2\nline3\nline4\n", vm.GetProjectDocumentText(projectId));
        Assert.Equal(projectId, vm.ActiveProjectId);
    }

    [Fact] // [Fix wave 1, Finding 2] dikiş kilidi kapanması ile ActiveProjectId ataması AYNI kilit altında olmalı
    public async Task LoadProjectLogAsync_does_not_drop_a_live_line_racing_the_stitch_finalize()
    {
        // ActiveProjectId ataması eskiden kilit DIŞINDAYDI: kilit kapanıp _projectText yazıldıktan SONRA,
        // ActiveProjectId GÜNCELLENMEDEN ÖNCEKİ dar aralıkta gelen bir canlı ProjectLog satırı, _liveLines'a
        // eklenir (kilitli) ama ActiveProjectId hâlâ eski değeri taşıdığından _projectText'e YAZILMAZ — snapshot
        // da o satırı zaten kapatmış olur, satır kalıcı olarak kaybolur. DebugAfterStitchLockExited kancası,
        // TAM O aralığın (artık fix ile var OLMAYAN) sınırında senkron (tek thread, sleep/poll YOK — D8) bir
        // canlı satır enjekte ederek bunu kanıtlar: fix'ten ÖNCE bu satır kaybolur, fix'ten SONRA (atama artık
        // kilit içinde olduğundan kanca zaten güncellenmiş ActiveProjectId'yi görür) satır projeye düşer.
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\a.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));
        vm.OnEvent(new ProjectLogEvent("r1", projectId, 1, "line1"));
        vm.OnEvent(new ProjectLogEvent("r1", projectId, 2, "line2"));

        vm.DebugAfterStitchLockExited = () => vm.OnEvent(new ProjectLogEvent("r1", projectId, 3, "race-line3"));

        // [D4 review §2] Seçim üretimde load'dan önce kurulur; proje modu (ActiveProjectId) ancak seçim hâlâ
        // o projedeyken kurulur — bu test tam da o modun kurulduğunu (race-line3'ün projeye düşmesi) doğrular.
        vm.SelectProject(projectId);
        // ThroughLineNumber=0: disk chunk boş, dikiş TÜMÜYLE tamponlanmış canlı satırlardan (line1, line2 —
        // kilit çalıştığı ANDA _liveLines'ta zaten var) üretilir; race-line3 kanca ile kilit KAPANDIKTAN SONRA
        // enjekte edilir — snapshot'ın parçası DEĞİLDİR, yalnız (fix ile) güncel ActiveProjectId sayesinde canlı eklenir.
        var load = vm.LoadProjectLogAsync(projectId);
        vm.OnEvent(new ProjectLogChunkEvent(projectId, Sequence: 0, "", IsLast: true, ThroughLineNumber: 0));
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("line1\nline2\nrace-line3\n", vm.GetProjectDocumentText(projectId));
    }

    [Fact] // [Fix wave 1(It-3), Finding 1] ikinci Rebuild'de dosya sıfırdan yazılır (satır no'ları 1'den başlar) —
           // eski run'ın _liveLines/_projectText'te kalan tortusu yeni dikişe SIZMAMALI.
    public async Task Second_Rebuild_clears_stitch_buffers_so_old_run_lines_do_not_leak_into_new_stitch()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // hiç başlatılmadı — startRun senkron atılır, VM içinde yutulur
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\a.csproj";

        // --- ilk run: P için 1..4 satır, run tamamlanır ---
        await vm.RebuildCommand.ExecuteAsync(null);
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));
        for (int i = 1; i <= 4; i++)
            vm.OnEvent(new ProjectLogEvent("r1", projectId, i, $"old-line{i}"));
        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 100));

        // --- ikinci Rebuild: proje log dosyası sıfırdan yazılıyor, satır no'ları yeniden 1'den başlıyor ---
        await vm.RebuildCommand.ExecuteAsync(null); // buffer'lar burada temizlenmeli (fix ÖNCESİ: temizlenmez)
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));
        for (int i = 1; i <= 2; i++)
            vm.OnEvent(new ProjectLogEvent("r1", projectId, i, $"new-line{i}"));

        // Karta tıklama: disk snapshot ikinci run'ın 1. satırını kapatıyor (ThroughLineNumber=1)
        var load = vm.LoadProjectLogAsync(projectId);
        vm.OnEvent(new ProjectLogChunkEvent(projectId, 0, "new-line1\n", IsLast: true, ThroughLineNumber: 1));
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        var text = vm.GetProjectDocumentText(projectId);
        Assert.Equal("new-line1\nnew-line2\n", text);
        Assert.DoesNotContain("old-line", text);
    }

    [Fact] // [Fix wave 1(It-3), Finding 2] Skipped proje (cycle üyesi) hiç log dosyası taşımaz — Supervisor
           // error(logNotFound) döner; pending Completion tamamlanmazsa await SONSUZA DEK asılı kalır.
    public async Task LoadProjectLogAsync_completes_on_logNotFound_instead_of_hanging_forever()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\skipped.csproj";
        vm.OnEvent(new ProjectSkippedEvent("r1", projectId, "bağımlılık döngüsünde"));

        var load = vm.LoadProjectLogAsync(projectId);
        vm.OnEvent(new ErrorEvent("logNotFound", projectId));

        // Sınırlı bekleme [D8]: fix ÖNCESİ hiçbir şey Completion'ı tamamlamaz → WaitAsync timeout ile FAIL.
        await load.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(vm.ActiveProjectId); // dikiş hiç kurulmadı — proje moduna geçilmedi, VM tutarlı kaldı
    }

    [Fact]
    public async Task LoadProjectLogAsync_multi_chunk_history_is_assembled_in_arrival_order()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\a.csproj";

        var load = vm.LoadProjectLogAsync(projectId);
        vm.OnEvent(new ProjectLogChunkEvent(projectId, 0, "line1\n", IsLast: false, ThroughLineNumber: 2));
        vm.OnEvent(new ProjectLogChunkEvent(projectId, 1, "line2\n", IsLast: true, ThroughLineNumber: 2));
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("line1\nline2\n", vm.GetProjectDocumentText(projectId));
    }

    // ---------------------------------------------------------------- 4) run dokümanı proje modunda bile birikir

    [Fact]
    public async Task ProjectLog_always_accumulates_into_the_run_document_even_in_project_mode()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string a = @"C:\p\a.csproj";
        const string b = @"C:\p\b.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", a, "A"));
        vm.OnEvent(new ProjectStartedEvent("r1", b, "B"));

        var load = vm.LoadProjectLogAsync(a);
        vm.OnEvent(new ProjectLogChunkEvent(a, 0, "", IsLast: true, ThroughLineNumber: 0));
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        // Artık proje modundayız (ActiveProjectId == a); B için gelen canlı satır YİNE run dokümanına düşmeli.
        vm.OnEvent(new ProjectLogEvent("r1", b, 1, "b-line"));

        Assert.Contains("b-line", vm.GetRunDocumentText());
        Assert.DoesNotContain("b-line", vm.GetProjectDocumentText(a)); // farklı projenin satırı A'nın dokümanına sızmadı
    }

    [Fact]
    public async Task ShowRun_returns_to_run_mode()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string a = @"C:\p\a.csproj";
        vm.SelectProject(a); // [D4 review §2] proje modu ancak seçim o projedeyken kurulur
        var load = vm.LoadProjectLogAsync(a);
        vm.OnEvent(new ProjectLogChunkEvent(a, 0, "", IsLast: true, ThroughLineNumber: 0));
        await load.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(a, vm.ActiveProjectId);

        vm.ShowRun();

        Assert.Null(vm.ActiveProjectId);
    }

    [Fact] // [D4 review §2] Hızlı select→deselect: gecikmiş chunk ActiveProjectId'yi TAKMAMALI → konsol donmamalı
    public async Task Deselecting_before_the_project_log_chunk_arrives_does_not_freeze_the_run_console()
    {
        // Kanala düşen anlatı satırlarını gözlemlemek için: tüm VM işlemleri ÖNCE (kanala Post eder), sonra pump'ı
        // TEK completing-tick ile boşalt (ConsoleBatcherTests deseni — gerçek 50ms/sleep YOK, D8). Fix ÖNCESİ
        // OnProjectLogChunk ActiveProjectId'yi KOŞULSUZ "a"ya set eder → deselect'ten sonra "a"da TAKILI kalır →
        // AppendRunLine (ActiveProjectId null gate'i) sonraki anlatı satırlarını post EDEMEZ → konsol sessizce DONAR.
        ConsoleBatcher? batcher = null;
        Task Tick(CancellationToken ct) { batcher!.Complete(); return Task.CompletedTask; }
        batcher = new ConsoleBatcher(Tick);
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // hiç başlatılmadı → SendAsync senkron fırlar
        var vm = new RunViewModel(engine, batcher, () => "r1")
        {
            WallClock = () => new DateTimeOffset(2026, 7, 23, 9, 0, 0, TimeSpan.Zero),
        };
        const string a = @"C:\p\a.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", a, "A"));

        // (1) kart A seç → yükleme uçuşta (engine ölü → SendAsync senkron fırlar; pending yine de kurulur ve
        //     SendAsync-throws yolu onu BİLEREK null'LAMAZ — gecikmiş bir chunk hâlâ eşleşip dikişi tamamlayabilsin).
        vm.SelectProject(a);
        var load = vm.LoadProjectLogAsync(a);
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        // (2) IPC dönmeden kullanıcı A'yı bırakır.
        vm.SelectProject(null);
        Assert.Null(vm.SelectedProjectId);

        // (3) A'nın gecikmiş IsLast chunk'ı gelir — dikiş HER ZAMAN yapılır AMA seçim artık A değil → mod KURULMAZ.
        vm.OnEvent(new ProjectLogChunkEvent(a, 0, "disk\n", IsLast: true, ThroughLineNumber: 0));
        Assert.Null(vm.ActiveProjectId);                            // TAKILI DEĞİL (fix'siz kod burada "a" bırakır)
        Assert.Equal("disk\n", vm.GetProjectDocumentText(a));       // dikiş yine de hazır (re-select için)

        // (4) sonraki bir anlatı satırı YİNE konsol kanalına post edilir (donma yok).
        vm.OnEvent(new SyncProgressEvent("Sync complete — all good", "info"));

        var flushes = new List<string>();
        await batcher.PumpAsync((text, _) => flushes.Add(text), CancellationToken.None);
        Assert.Contains("Sync complete — all good", string.Concat(flushes)); // anlatı kanala ulaştı → DONMADI
    }

    // ---------------------------------------------------------------- 5) hata-yalnız outcome'lar runCompleted BEKLEMEZ

    [Fact]
    public async Task Error_only_outcome_reenables_rebuild_without_a_runCompleted()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0)); // koşu başladı: IsRunning=true

        vm.OnEvent(new ErrorEvent("runFailed", "koşu beklenmedik bir istisnayla düştü"));

        Assert.False(vm.IsRunning);
        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    [Fact] // stop-during-planning: runStarted HİÇ gelmedi, runStopped var ama runCompleted YOK
    public async Task RunStopped_without_a_prior_RunStarted_reenables_rebuild()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        // Rebuild tıklanır tıklanmaz IsRunning=true OLMAZ (yalnız runStarted ile olur) — App bu senaryoda
        // buton durumu için TAMAMEN engine event'lerine güvenir; burada doğrudan senaryoyu event'lerle kurarız.
        vm.OnEvent(new ErrorEvent("planFailed", "disk okunamadı"));

        Assert.False(vm.IsRunning);
        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    [Fact] // runInProgress KOŞAN run'ı ETKİLEMEMELİ — IsRunning olduğu gibi kalır
    public async Task RunInProgress_error_does_not_disturb_an_active_run()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));

        vm.OnEvent(new ErrorEvent("runInProgress", "zaten koşuyor"));

        Assert.True(vm.IsRunning);
    }

    /// <summary>[B1] Motor, run slotunu (<c>_runActive</c>) TÜM event'ler yazıldıktan SONRA bırakır
    /// (<c>ExecuteRunAsync</c>'in finally'si) — yani <c>runCompleted</c> App'e ulaştıktan sonra kısa bir pencere
    /// boyunca slot HÂLÂ doludur. Butonlar o anda açıldığı için hızlı bir tıklama <c>runInProgress</c> alır.
    /// <para><c>IsStarting</c> REDDEDİLEN isteğin kendi bayrağıdır: temizlenmezse UI kilit penceresinde
    /// (<see cref="RunViewModel.IsMidRunLocked"/>) SONSUZA DEK donar — Build/Rebuild disabled kalır, Stop
    /// görünür ama arkada durdurulacak bir şey yoktur. Koşan run'a dokunulmaz; kilit gerçekten koşuyorsa
    /// <see cref="RunViewModel.IsRunning"/> üzerinden zaten sürer (kardeş test).</para></summary>
    [Fact]
    public async Task A_rejected_start_releases_its_own_starting_flag()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r2") { RootPath = @"D:\repo" };
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        // Kilit penceresi doğrudan kurulur: bu harness'ta engine YOK, gönderim senkron düşer ve BeginRunAsync
        // bayrağı kendi hata dalında zaten geri açar — yani üretim yolu bu ön-koşulu üretemez. Test zaten
        // "bayrak nasıl kondu"yu değil, REDDİ GÖREN OnError'ın onu bırakıp bırakmadığını sürüyor.
        vm.IsStarting = true;
        Assert.True(vm.IsMidRunLocked); // ön-koşul: UI kilit penceresinde

        vm.OnEvent(new ErrorEvent("runInProgress", "A run is already in progress — 'r2' was rejected."));

        Assert.False(vm.IsStarting);
        Assert.False(vm.IsMidRunLocked);              // UI kilitte donmadı
        Assert.True(vm.BuildCommand.CanExecute(null)); // kullanıcı tekrar deneyebilir
    }

    // ---------------------------------------------------------------- 6) Stop/Continue komut gönderimi (gerçek Supervisor)

    /// <summary>Stop <c>StopKind.Graceful</c> gönderir: yeni proje dispatch edilmez, uçuştaki child'lar
    /// post-build copy dahil BİTİRİLİR.
    /// <para><b>Continue kalktıktan sonra da graceful:</b> tek toparlanma yolu Build olduğu için seçim artık
    /// "kaç projelik iş çöpe gidiyor" sorusudur. Drain'de biten projeler <c>PersistBuildStateOnSuccess</c> ile
    /// bankaya girer ve bir sonraki Build onları ATLAR; hard kill ise o projeleri
    /// <c>failed("stopped")</c> yapıp stored state'lerini geçersizleştirir, yani paralellik kadar yarım derleme
    /// çöpe gider ve Build'de baştan derlenir. Stop'un bedeli graceful'de sıfırdır. Bekleme görünürlüğü ayrı
    /// çözüldü (<c>Stopping</c> fazı + pasif buton).</para></summary>
    [Fact]
    public async Task Stop_sends_a_graceful_stop_and_the_engine_acks_it_as_not_hard()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe, WideStartupTimeout); // [B1/F1] gerçek engine BAŞLATILIYOR — bkz. sınıf başındaki sabit
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var stopped = new TaskCompletionSource<RunStoppedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.EventReceived += e => { if (e is RunStoppedEvent s) stopped.TrySetResult(s); };
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0)); // App'in "run aktif" inancı

        await vm.StopCommand.ExecuteAsync(null);

        // Gerçek Supervisor'da eşleşen bir run YOK (yalnız VM'in inancı kuruldu) → TryRequestStop false →
        // host yolu hemen runStopped döner. WasHard, gönderilen StopKind'ın gözlenebilir kanıtıdır — Hard bu
        // bayrağı her zaman true yapardı.
        var result = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("r1", result.RunId);
        Assert.False(result.WasHard);
    }

    /// <summary>[Stopping] Graceful stop uçuştaki child'ların bitmesini bekler; o pencerede uygulamanın
    /// TIKLAMAYI ALDIĞINI göstermesi gerekir. Faz <see cref="AppPhase.Stopping"/>'e geçer ve
    /// <c>StopCommand</c> pasifleşir (aynı Stop'a ikinci kez basmak yeni bir stopRun ÜRETMEZ) — ama kilit
    /// (<see cref="RunViewModel.IsMidRunLocked"/>) SÜRER: motor hâlâ koşuyor, branch/worktree/configuration
    /// açılmamalı ve split-button geri gelmemeli.</summary>
    [Fact]
    public async Task Stop_moves_the_phase_to_stopping_and_disables_the_stop_command_while_the_lock_holds()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe, WideStartupTimeout);
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        Assert.Equal(AppPhase.Running, vm.Phase);   // ön-koşul
        Assert.True(vm.StopCommand.CanExecute(null));

        await vm.StopCommand.ExecuteAsync(null);

        Assert.Equal(AppPhase.Stopping, vm.Phase);
        Assert.False(vm.StopCommand.CanExecute(null));
        Assert.True(vm.IsMidRunLocked);
    }

    /// <summary>[Stopping] Faz TIKLAMA ANINDA yazılır — gönderimin dönmesi BEKLENMEZ (gecikmeli bir engine'de
    /// buton saniyelerce "Stop" kalırdı). <c>DebugOnCommandSent</c> gönderimden hemen ÖNCE senkron tetiklenir,
    /// yani burada gözlenen değer "komut yola çıkarken UI'nın hâli"dir.</summary>
    [Fact]
    public async Task The_phase_flips_to_stopping_before_the_command_is_even_sent()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        AppPhase? phaseAtSend = null;
        vm.DebugOnCommandSent = _ => phaseAtSend = vm.Phase;

        await vm.StopCommand.ExecuteAsync(null);

        Assert.Equal(AppPhase.Stopping, phaseAtSend);
    }

    /// <summary>[Stopping] Engine hazır değilken gönderim SENKRON fırlar: hiçbir runStopped/runCompleted
    /// gelmeyeceği için faz <see cref="AppPhase.Stopping"/>'te ASILI kalırdı — buton sonsuza dek pasif,
    /// şerit sonsuza dek "Stopping". Gönderim başarısızsa faz geri alınır (<c>BeginRunAsync</c>'in
    /// "gönderim başarısız → IsStarting geri açılır" deseninin ikizi).</summary>
    [Fact]
    public async Task A_stop_that_cannot_be_sent_puts_the_phase_back()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // BAŞLATILMAZ → SendAsync fırlar
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));

        await vm.StopCommand.ExecuteAsync(null);

        Assert.Equal(AppPhase.Running, vm.Phase);
        Assert.True(vm.StopCommand.CanExecute(null)); // kullanıcı tekrar deneyebilir
    }

    [Fact]
    public async Task Rebuild_is_disabled_while_running_and_reenabled_after_completion()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        Assert.True(vm.RebuildCommand.CanExecute(null));

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        Assert.False(vm.RebuildCommand.CanExecute(null));

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500));
        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- 6b) [Fix wave 1, Finding 1] CanExecuteChanged canlı UI'a ULAŞMALI
    // CommunityToolkit RelayCommand CommandManager.RequerySuggested'a ABONE OLMAZ — [NotifyCanExecuteChangedFor]
    // olmadan gerçek pencerede Stop butonu hiç yeniden sorgulanmaz (hep disabled kalır). Bu testler
    // CanExecute'in DOĞRU DEĞERİ değil, event'in GERÇEKTEN ATEŞLENDİĞİNİ kanıtlar.

    [Fact]
    public async Task RunStarted_raises_CanExecuteChanged_for_Rebuild_and_Stop()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        bool rebuildChanged = false, stopChanged = false;
        vm.RebuildCommand.CanExecuteChanged += (_, _) => rebuildChanged = true;
        vm.StopCommand.CanExecuteChanged += (_, _) => stopChanged = true;

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0)); // IsRunning false→true

        Assert.True(rebuildChanged); // CanRebuild = !IsRunning
        Assert.True(stopChanged);    // CanStop = IsRunning — Kısıt 3: kullanıcı Stop'a hiç basamaz olmasın diye
        Assert.True(vm.StopCommand.CanExecute(null));
        Assert.False(vm.RebuildCommand.CanExecute(null));
    }

    [Fact]
    public async Task RunCompleted_after_a_running_state_raises_CanExecuteChanged_for_Stop_via_IsRunning()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        bool stopChanged = false;
        vm.StopCommand.CanExecuteChanged += (_, _) => stopChanged = true;

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500)); // IsRunning true→false

        Assert.True(stopChanged);
        Assert.False(vm.StopCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- 6b-2) [Fix wave 1(It-3), Finding 3] planlama sırasında Stop erişilebilir olmalı
    // Supervisor runStarted'dan ÖNCE planlama yapar (scan/graph/topo) ve stop-during-planning'i destekler
    // (ack-debt yolu) — App bunu yalnız IsRunning'e bakarak engelliyordu; IsStarting bu boşluğu kapatır.

    [Fact]
    public async Task RebuildCommand_enables_Stop_and_disables_Rebuild_before_runStarted_arrives()
    {
        // [Fix wave 2, Finding 1] Gerçek (başlatılmış) engine kullanılır: gönderim GERÇEKTEN başarılı olmalı
        // ki "planlama sürüyor, runStarted henüz gelmedi" penceresi doğru simüle edilsin — engine hiç
        // başlatılmamış olsaydı SendAsync senkron fırlardı ve (Finding 1 fix'i ile) IsStarting hemen geri
        // açılırdı; bu artık "send başarısız" senaryosu olur, "planlama sürüyor" değil. Event pump vm.OnEvent'e
        // bağlanmadığından Supervisor'ın gerçek yanıtı (varsa) bu testi etkilemez — yalnız elle enjekte edilen
        // RunStartedEvent state'i değiştirir.
        await using var engine = new EngineHost(TestPaths.SupervisorExe, WideStartupTimeout); // [B1/F1] bkz. sınıf başındaki sabit — aynı üçlünün ilki
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        await vm.RebuildCommand.ExecuteAsync(null); // gönderim başarılı — runStarted HENÜZ gelmedi — yalnız IsStarting=true

        Assert.True(vm.StopCommand.CanExecute(null));
        Assert.False(vm.RebuildCommand.CanExecute(null));

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));

        Assert.True(vm.StopCommand.CanExecute(null)); // runStarted sonrası da Stop erişilebilir kalmalı
        Assert.False(vm.RebuildCommand.CanExecute(null));
    }

    [Fact] // stop-during-planning ack: runStarted hiç gelmedi, runStopped geldi → Rebuild tekrar aktif olmalı
    public async Task RunStopped_without_runStarted_after_Rebuild_reenables_Rebuild_and_disables_Stop()
    {
        // [Fix wave 2, Finding 1] bkz. yukarıdaki test — gerçek engine gerekir ki RunStoppedEvent geldiğinde
        // IsStarting GERÇEKTEN true olsun (aksi halde unstarted-engine senaryosunda gönderim zaten başarısız
        // olup IsStarting'i erkenden false yapar — test sonucu tesadüfen aynı kalır ama artık "stop-during-
        // planning" senaryosunu DOĞRULAMAZ).
        await using var engine = new EngineHost(TestPaths.SupervisorExe, WideStartupTimeout); // [B1/F1] bkz. yukarıdaki sabit
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        await vm.RebuildCommand.ExecuteAsync(null); // gönderim başarılı — IsStarting=true, runStarted HENÜZ gelmedi

        vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));

        Assert.True(vm.RebuildCommand.CanExecute(null));
        Assert.False(vm.StopCommand.CanExecute(null));
    }

    [Fact] // [Fix wave 3] RunCoordinator.ExecuteRunAsync'in dış catch'i planlama SIRASINDA (runStarted'dan ÖNCE)
           // beklenmedik bir istisnada "runFailed" ErrorEvent'i yayınlar — bu kod eskiden RunEndingErrorCodes'ta
           // yoktu, bu yüzden IsStarting kalıcı true kalır, Rebuild/Continue sonsuza dek kilitli kalırdı.
    public async Task RunFailed_error_during_planning_reenables_Rebuild_and_disables_Stop()
    {
        // bkz. yukarıdaki iki test — gerçek (başlatılmış) engine gerekir ki runFailed geldiğinde IsStarting
        // GERÇEKTEN true olsun (planlama-sırasında-beklenmedik-hata senaryosu).
        await using var engine = new EngineHost(TestPaths.SupervisorExe, WideStartupTimeout); // [B1/F1] bkz. sınıf başındaki sabit — aynı üçlünün üçüncüsü
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        await vm.RebuildCommand.ExecuteAsync(null); // gönderim başarılı — IsStarting=true, runStarted HENÜZ gelmedi
        Assert.True(vm.IsStarting);
        Assert.False(vm.RebuildCommand.CanExecute(null));

        vm.OnEvent(new ErrorEvent("runFailed", "planlama sırasında beklenmedik hata"));

        Assert.False(vm.IsStarting);
        Assert.False(vm.IsRunning);
        Assert.True(vm.RebuildCommand.CanExecute(null));
        Assert.False(vm.StopCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- 6d) [Fix wave 2, Finding 1] gönderim senkron BAŞARISIZ olursa IsStarting geri açılmalı

    [Fact] // engine hiç başlamadı/öldü → SendAsync senkron fırlar → IsStarting KALICI takılmamalı (hiç event gelmeden)
    public async Task RebuildCommand_recovers_IsStarting_when_the_initial_send_fails_synchronously()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // hiç başlatılmadı — writer null, SendAsync senkron fırlar
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        await vm.RebuildCommand.ExecuteAsync(null); // gönderim başarısız — hiçbir engine event'i asla gelmeyecek

        Assert.False(vm.IsStarting);
        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- 6e) [Fix wave 2, Finding 2] engine ölüyken kart tıklaması ASILI KALMAMALI

    [Fact] // SendAsync senkron fırlar, hiçbir event Completion'ı tamamlamaz eskiden — LoadProjectLogAsync sonsuza dek asılı kalırdı
    public async Task LoadProjectLogAsync_completes_when_engine_is_dead_without_any_event()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe); // hiç başlatılmadı — SendAsync senkron fırlar
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\dead.csproj";

        var load = vm.LoadProjectLogAsync(projectId);

        // Sınırlı bekleme [D8]: fix ÖNCESİ hiçbir şey Completion'ı tamamlamaz → WaitAsync timeout ile FAIL (hang kanıtı).
        await load.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(vm.ActiveProjectId); // dikiş hiç kurulmadı — boş/loglu-olmayan doküman, proje moduna geçilmedi
    }

    // ---------------------------------------------------------------- 6c) [Fix wave 1] TickElapsed enjekte edilen saatle deterministik

    [Fact]
    public async Task TickElapsed_uses_the_injected_clock_deterministically()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        long fakeNow = 1_000_000;
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1", () => fakeNow);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", ElapsedMsAtStart: 500));
        Assert.Equal(500, vm.ElapsedMs);

        fakeNow += 250;
        vm.TickElapsed();

        Assert.Equal(750, vm.ElapsedMs);
    }

    [Fact]
    public async Task TickElapsed_does_nothing_once_stopped()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        long fakeNow = 1_000_000;
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1", () => fakeNow);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, DurationMs: 4242));

        fakeNow += 10_000;
        vm.TickElapsed();

        Assert.Equal(4242, vm.ElapsedMs); // IsRunning=false → TickElapsed no-op, engine'in kesin süresi korunur
    }

    // ---------------------------------------------------------------- 6f) [Task 16 — It-2 devir §8] EngineExited → run-state reset (wedge fix)
    // EngineHost.EngineExited sinyali eskiden VM'e hiç bağlı DEĞİLDİ (yalnız MainWindow'daki banner'ı
    // güncelliyordu) — engine startRun sonrası runStarted'dan ÖNCE ya da run ORTASINDA ölürse hiçbir IPC
    // event'i asla gelmeyeceğinden IsStarting/IsRunning SONSUZA DEK kilitli kalırdı, "Restart
    // Engine" bile açmıyordu. OnEngineExited bu kamayı kapatır.

    [Fact] // startRun gönderildi, runStarted HENÜZ gelmedi (IsStarting=true) — engine bu pencerede ölürse butonlar açılmalı
    public async Task OnEngineExited_while_IsStarting_resets_run_state_and_reenables_Rebuild()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe, WideStartupTimeout); // [B1/F1] yük altında ÖLÇÜLEN kırmızı — bkz. sınıf başındaki sabit
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        await vm.RebuildCommand.ExecuteAsync(null); // gönderim başarılı — IsStarting=true, runStarted HENÜZ gelmedi
        Assert.True(vm.IsStarting);

        vm.OnEngineExited(1);

        Assert.False(vm.IsStarting);
        Assert.False(vm.IsRunning);
        Assert.True(vm.RebuildCommand.CanExecute(null)); // butonlar artık un-wedged
        Assert.False(vm.StopCommand.CanExecute(null));
    }

    [Fact] // run ORTASINDA (IsRunning=true, runStarted zaten geldi) engine ölürse yine sıfırlanmalı
    public async Task OnEngineExited_while_IsRunning_resets_run_state_and_reenables_Rebuild()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        Assert.True(vm.IsRunning);

        vm.OnEngineExited(139);

        Assert.False(vm.IsRunning);
        Assert.False(vm.IsStarting);
        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    [Fact] // hiçbir run aktif değilken (idle, zaten temiz) engine ölürse no-op — state bozulmamalı, fırlamamalı
    public async Task OnEngineExited_with_nothing_running_is_a_noop()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        Assert.False(vm.IsRunning);
        Assert.False(vm.IsStarting);
        Assert.True(vm.RebuildCommand.CanExecute(null));

        vm.OnEngineExited(null); // framing-hatası senaryosu (exit code yok) — fırlamamalı

        Assert.False(vm.IsRunning);
        Assert.False(vm.IsStarting);
        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    [Fact] // normal runCompleted akışı ZATEN sıfırlamıştı — ardından gelen engine-death bu temiz durumu bozmamalı (idempotent)
    public async Task OnEngineExited_after_a_normal_runCompleted_does_not_corrupt_already_reset_state()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu bu değil
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500));
        Assert.False(vm.IsRunning);
        Assert.True(vm.RebuildCommand.CanExecute(null));

        vm.OnEngineExited(0); // supervisor bu run'dan SONRA, sıradan bir sebeple kapanmış olabilir

        Assert.False(vm.IsRunning);
        Assert.False(vm.IsStarting);
        Assert.True(vm.RebuildCommand.CanExecute(null)); // hâlâ un-wedged
    }

    [Fact] // engine-died VM-observable bir durum/hata metnine yansımalı (pixel It-4 — burada yalnız VM-state)
    public async Task OnEngineExited_sets_an_observable_engine_died_message()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEngineExited(139);

        Assert.False(string.IsNullOrWhiteSpace(vm.EngineDiedMessage));
        // [E2/FIX3] Gevşek Contains("139") tek başına Türkçe bir regresyonu (ör. "Motor beklenmedik…") yine
        // GEÇİRİRDİ (İngilizce-sweep folder'ını boşa çıkarır). Üretim literaline (OnEngineExited) TAM pinle.
        Assert.StartsWith("Engine stopped unexpectedly", vm.EngineDiedMessage, StringComparison.Ordinal);
        Assert.Contains("139", vm.EngineDiedMessage); // exit kodu korunur
    }

    [Fact] // [Review fix, Task 16] EngineDiedMessage engine ölümünden sonra KALICI kalmamalı — sonraki run gerçekten
    // başladığında (runStarted, IPC round-trip kanıtı) VM'in artık CANLI/güncel bir engine'e bağlı olduğu kesinleşir.
    public async Task OnEngineExited_then_next_runStarted_clears_EngineDiedMessage()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEngineExited(139);
        Assert.False(string.IsNullOrWhiteSpace(vm.EngineDiedMessage)); // önce kama-sonrası mesaj var

        vm.OnEvent(new RunStartedEvent("r2", RunMode.Rebuild, 1, 1, "Debug", 0)); // sonraki run gerçekten başladı

        Assert.Null(vm.EngineDiedMessage); // eski ölüm mesajı artık geçerli engine durumunu YANLIŞ yansıtmamalı
    }

    [Fact] // [E2/F3 fold] Engine run ORTASINDA ölürse: Phase Running'de asılı kalmamalı → tutarlı bir terminale (Stopped)
    // çekilir VE EngineDiedMessage kurulur (İngilizce, exit kodu KORUNUR). İkisi de tek atımda pinlenir.
    public async Task OnEngineExited_mid_run_pulls_phase_to_a_terminal_stopped_and_sets_the_message()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        Assert.Equal(AppPhase.Running, vm.Phase); // gerçekten mid-run

        vm.OnEngineExited(139);

        Assert.Equal(AppPhase.Stopped, vm.Phase);                 // [F3] terminal Phase (Running'de asılı kalmaz)
        Assert.False(string.IsNullOrWhiteSpace(vm.EngineDiedMessage));
        Assert.StartsWith("Engine stopped unexpectedly", vm.EngineDiedMessage, StringComparison.Ordinal); // [E2/FIX3] İngilizce pinlenir
        Assert.Contains("139", vm.EngineDiedMessage);             // exit kodu korunur
        Assert.False(vm.IsRunning);
        Assert.False(vm.IsStarting);
    }

    [Fact] // [E2/F3] Engine RESTING bir fazda (Idle) ölürse Phase'i Stopped'a çekmek YANILTICI olurdu — dokunulmaz.
    public async Task OnEngineExited_while_idle_leaves_the_resting_phase_untouched()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new SyncCompletedEvent("main", "abc1234", FetchDegraded: false, 3, 0)); // Phase → Idle
        Assert.Equal(AppPhase.Idle, vm.Phase);

        vm.OnEngineExited(1);

        Assert.Equal(AppPhase.Idle, vm.Phase); // resting faz korunur
        Assert.False(string.IsNullOrWhiteSpace(vm.EngineDiedMessage));
    }

    [Fact] // [Fix wave 1, Finding 1 deseniyle tutarlı] CanExecuteChanged GERÇEKTEN ateşlenmeli, yoksa gerçek pencerede buton hiç yeniden sorgulanmaz
    public async Task OnEngineExited_raises_CanExecuteChanged_for_Rebuild_Stop_and_Continue()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe, WideStartupTimeout); // [B1/F1] gerçek engine BAŞLATILIYOR — bkz. sınıf başındaki sabit
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        await vm.RebuildCommand.ExecuteAsync(null); // IsStarting=true
        bool rebuildChanged = false, stopChanged = false;
        vm.RebuildCommand.CanExecuteChanged += (_, _) => rebuildChanged = true;
        vm.StopCommand.CanExecuteChanged += (_, _) => stopChanged = true;

        vm.OnEngineExited(1);

        Assert.True(rebuildChanged);
        Assert.True(stopChanged);
    }

    // ---------------------------------------------------------------- 7) gerçek uçtan uca (Rebuild → satırlar + IsRunning)

    [SkippableFact] // vswhere/VS kurulu değilse msbuildNotFound gelir — RunCoordinatorTests ile aynı desen
    public async Task Rebuild_wires_through_the_real_engine_and_populates_rows()
    {
        string root = Directory.CreateTempSubdirectory("bo-vm-rebuild-").FullName;
        // X ↔ Y cycle fixture (RunCoordinatorTests ile aynı desen): iki üyeli bir SCC — [cycle rounds] artık
        // pre-skip edilmez, turlarla derlenir.
        foreach (var (self, other) in new[] { ("X", "Y"), ("Y", "X") })
        {
            Directory.CreateDirectory(Path.Combine(root, self));
            await File.WriteAllTextAsync(Path.Combine(root, self, self + ".csproj"),
                $"""
                <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup><AssemblyName>OSYS.{self}</AssemblyName></PropertyGroup>
                  <ItemGroup><Reference Include="OSYS.{other}"><HintPath>..\{other}\bin\OSYS.{other}.dll</HintPath></Reference></ItemGroup>
                </Project>
                """);
        }

        await using var engine = new EngineHost(TestPaths.SupervisorExe, WideStartupTimeout); // [B1/F1] gerçek engine BAŞLATILIYOR — bkz. sınıf başındaki sabit
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = root };
        var final = new TaskCompletionSource<IpcEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.EventReceived += e =>
        {
            vm.OnEvent(e);
            if (e is RunCompletedEvent or ErrorEvent { Code: "msbuildNotFound" }) final.TrySetResult(e);
        };

        await vm.RebuildCommand.ExecuteAsync(null);
        // [cycle rounds] Hang-guard; 15 sn idi. Fixture artık GERÇEKTEN derliyor (2 tur × 2 üye) — gerekçe ve
        // ölçüm sabitin tek sahibinde: TestPaths.WideRunTimeout. İddiaların hiçbiri süreye bakmaz.
        var outcome = await final.Task.WaitAsync(TestPaths.WideRunTimeout);
        if (outcome is ErrorEvent { Code: "msbuildNotFound" } err) Skip.If(true, err.Message);

        var done = Assert.IsType<RunCompletedEvent>(outcome);
        // [DEĞİŞEN KURAL — iki kez] Bu iddia önce "X↔Y pre-skip edilir" (Skipped=2) idi; turlar Build/Rebuild'in
        // içine katlanınca "gerçekten derlenir" (Skipped=0) oldu; turlar KENDİ moduna (RunMode.Cycles, Sync'in
        // yanındaki düğme) taşınınca yeniden pre-skip'e döndü. Sebep ölçümdür: katlanmış hâlde iki dakikalık
        // bir Build on beş dakikaya çıkıyordu. Rebuild bir SCC'ye artık HİÇ dokunmaz.
        // Testin ASIL iddiası (Rebuild gerçek motora kablolu, satırlar doluyor, IsRunning düşüyor) her üç
        // sürümde de aynı kaldı.
        Assert.Equal(2, done.Skipped);
        Assert.Equal(2, vm.Projects.Count);
        Assert.All(vm.Projects, p => Assert.Equal(ProjectRowState.Skipped, p.State));
        Assert.False(vm.IsRunning);
    }

    // ---------------------------------------------------------------- 8) [Task 17] depIssue VM state

    [Fact]
    public async Task ProjectSucceeded_with_depIssues_sets_HasDepIssue_and_DepIssues_roots_on_the_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\a.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));

        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100, DepIssues: ["B", "C"]));

        var row = Assert.Single(vm.Projects);
        Assert.True(row.HasDepIssue);
        Assert.Equal(["B", "C"], row.DepIssues);
    }

    [Fact]
    public async Task ProjectFailed_with_depIssues_sets_HasDepIssue_and_DepIssues_roots_on_the_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\b.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "B"));

        vm.OnEvent(new ProjectFailedEvent("r1", projectId, 100, "exit 1", DepIssues: ["X"]));

        var row = Assert.Single(vm.Projects);
        Assert.True(row.HasDepIssue);
        Assert.Equal(["X"], row.DepIssues);
    }

    [Fact]
    public async Task ProjectSucceeded_without_depIssues_leaves_HasDepIssue_false()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\clean.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "Clean"));

        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100)); // DepIssues null

        var row = Assert.Single(vm.Projects);
        Assert.False(row.HasDepIssue);
        Assert.Null(row.DepIssues);
    }

    // ---------------------------------------------------------------- 8b) [cycle rounds/Task 8] round + unsettled/unconverged VM state

    [Fact]
    public async Task ProjectSucceeded_carries_CycleUnsettled_onto_the_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\a.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));

        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100, DepIssues: null, CycleUnsettled: true));

        var row = Assert.Single(vm.Projects);
        Assert.True(row.CycleUnsettled);
    }

    [Fact]
    public async Task ProjectSucceeded_without_cycleUnsettled_leaves_the_row_flag_false()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\a.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));

        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100)); // CycleUnsettled default false

        var row = Assert.Single(vm.Projects);
        Assert.False(row.CycleUnsettled);
    }

    [Fact]
    public async Task ProjectSkipped_carries_CycleUnconverged_onto_the_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\a.csproj";

        vm.OnEvent(new ProjectSkippedEvent("r1", projectId, SkipReasons.CycleNonConvergent, CycleUnconverged: true));

        var row = Assert.Single(vm.Projects);
        Assert.True(row.CycleUnconverged);
    }

    /// <summary>
    /// [cycles] "Kalıcı kırık döngü" bayrağının ASIL kaynağı: koşunun kendi yakınsamama kararı.
    ///
    /// <para>Bayrak eskiden yalnız motorun pre-skip'inden gelirdi ("önceki koşuda yakınsamamıştı, hiç
    /// denemiyorum"). O pre-skip kalktı — açık bir Resolve basışı artık her zaman taze bir deneme yapar — ve
    /// bayrağın tek üreticisi de onunla birlikte kalkmıştı. Yeni kaynak hem daha dürüst hem daha erken:
    /// hatırlanan bir geçmiş değil ŞU koşunun kanıtı, ve kullanıcı bunu ikinci bir basışı beklemeden, tam da
    /// denemenin bittiği koşuda görür.</para>
    /// </summary>
    [Fact]
    public async Task A_cycle_that_ends_without_progress_marks_all_its_members_as_unconverged()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string a = @"C:\p\a.csproj", b = @"C:\p\b.csproj";
        vm.OnEvent(new WorkspaceTopologyEvent(
            [new ProjectNode(a, "A", a, [], [], 0, null, null, true, null),
             new ProjectNode(b, "B", b, [], [], 0, null, null, true, null)],
            [[a, b]], [], []));

        // Grup turlarını harcadı: A yeşil bitti, B patladı — ve grup YAKINSAMADI.
        vm.OnEvent(new ProjectStartedEvent("r1", a, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", a, 100));
        vm.OnEvent(new ProjectStartedEvent("r1", b, "B"));
        vm.OnEvent(new ProjectFailedEvent("r1", b, 100, "boom"));
        vm.OnEvent(new CycleCompletedEvent("r1", a, CycleOutcome.NoProgress, 2, 2, 1, 400));

        // İkisi de işaretli: sıkışan GRUPTUR, tek tek üyeler değil — yeşil biten üyenin çıktısı da bayat.
        Assert.All(vm.Projects, p => Assert.True(p.CycleUnconverged));
        // Sayaç statüden bağımsız okur: üyeler Failed/Succeeded, Skipped DEĞİL.
        Assert.Equal(2, vm.Counters.StuckCycles);
    }

    /// <summary>Yakınsayan grup hiçbir üyesini işaretlemez — kontrol grubu.</summary>
    [Fact]
    public async Task A_cycle_that_converges_marks_nothing()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string a = @"C:\p\a.csproj";
        vm.OnEvent(new ProjectStartedEvent("r1", a, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", a, 100));

        vm.OnEvent(new CycleCompletedEvent("r1", a, CycleOutcome.Converged, 1, 2, 0, 400));

        Assert.False(Assert.Single(vm.Projects).CycleUnconverged);
        Assert.Equal(0, vm.Counters.StuckCycles);
    }

    [Fact]
    public async Task ProjectSkipped_without_cycleUnconverged_leaves_the_row_flag_false()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\b.csproj";

        vm.OnEvent(new ProjectSkippedEvent("r1", projectId, SkipReasons.UpToDate)); // CycleUnconverged default false

        var row = Assert.Single(vm.Projects);
        Assert.False(row.CycleUnconverged);
    }

    /// <summary>[cycle rounds/Task 9 review fix 1] Kök neden: <c>CycleUnconverged</c>'i yazan TEK yer
    /// <see cref="RunViewModel"/>'in <c>OnProjectSkipped</c>'idir; satır nesneleri segmentler arası HAYATTA
    /// KALIR (<c>Projects.Clear()</c> yalnız <see cref="RunMode.Rebuild"/>'de) — kaynak düzeltilip proje
    /// GERÇEKTEN derlenirse bayat bayrak temizlenmezse "az önce düzelen proje" kalıcı-kırık gibi render edilir
    /// (Task 9'un önlemeye çalıştığı yanlış bilginin TERSİ). <c>OnProjectDone</c> artık her terminal derleme
    /// sonucunda (Succeeded/Failed — ikisi de proje GERÇEKTEN invoke edildi demektir) bayrağı temizler.</summary>
    [Fact]
    public async Task ProjectSucceeded_after_a_prior_CycleUnconverged_skip_clears_the_flag()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\a.csproj";

        vm.OnEvent(new ProjectSkippedEvent("r1", projectId, SkipReasons.CycleNonConvergent, CycleUnconverged: true));
        var row = Assert.Single(vm.Projects);
        Assert.True(row.CycleUnconverged); // ön-koşul: bayrak gerçekten set edildi

        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100));

        Assert.False(row.CycleUnconverged);
    }

    /// <summary>[cycle rounds/Task 9 review fix 1] Aynı temizlik <c>Failed</c> için de geçerli — proje bu run'da
    /// GERÇEKTEN invoke edildiyse (başarılı ya da başarısız fark etmez) artık "hiç invoke edilmeden pre-skip"
    /// hikayesi doğru DEĞİLDİR.</summary>
    [Fact]
    public async Task ProjectFailed_after_a_prior_CycleUnconverged_skip_clears_the_flag()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\a.csproj";

        vm.OnEvent(new ProjectSkippedEvent("r1", projectId, SkipReasons.CycleNonConvergent, CycleUnconverged: true));
        var row = Assert.Single(vm.Projects);
        Assert.True(row.CycleUnconverged); // ön-koşul

        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "A"));
        vm.OnEvent(new ProjectFailedEvent("r1", projectId, 100, "boom"));

        Assert.False(row.CycleUnconverged);
    }

    [Fact]
    public async Task RunCompleted_sets_DepIssueCount_from_the_event()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 3, 1, "Debug", 0));

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 2, 1, 0, 0, 500, DepIssueCount: 2));

        Assert.Equal(2, vm.DepIssueCount);
    }

    // ---------------------------------------------------------------- 9) [Task 17] will-build + succeeded→clean live transition

    [Fact]
    public async Task BuildPreviewEvent_pre_populates_rows_with_WillBuild_dirty_clean_and_hollow()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new BuildPreviewEvent(
        [
            new BuildPreviewItem(@"C:\p\dirty.csproj", "Dirty", true),
            new BuildPreviewItem(@"C:\p\clean.csproj", "Clean", false),
            new BuildPreviewItem(@"C:\p\hollow.csproj", "Hollow", null),
        ]));

        Assert.Equal(3, vm.Projects.Count);
        Assert.True(vm.Projects.Single(p => p.Name == "Dirty").WillBuild);
        Assert.False(vm.Projects.Single(p => p.Name == "Clean").WillBuild);
        Assert.Null(vm.Projects.Single(p => p.Name == "Hollow").WillBuild);
    }

    // ---------------------------------------------------------------- [W1] per-proje sha (BuiltCommit) wire

    [Fact]
    public async Task BuildPreviewEvent_fills_each_rows_current_sha_from_the_built_commit()
    {
        // [W1] Sha çiftinin sol yarısı artık GERÇEKTEN akar (It-4b'de üretimde HEP null'dı). Değer HAM taşınır —
        // 7 haneye kısaltma kartın işidir (bkz. ProjectRowTests), VM veriyi kırpmaz.
        const string built = "a3f81c29b4d5e6f708192a3b4c5d6e7f80910a2b";
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new BuildPreviewEvent(
        [
            new BuildPreviewItem(@"C:\p\built.csproj", "Built", true, built),
            new BuildPreviewItem(@"C:\p\never.csproj", "Never", true), // hiç derlenmemiş
        ]));

        Assert.Equal(built, vm.Projects.Single(p => p.Name == "Built").CurrentSha);
        Assert.Null(vm.Projects.Single(p => p.Name == "Never").CurrentSha); // uydurulmaz
    }

    [Fact]
    public async Task A_later_segments_preview_refreshes_the_current_sha_of_an_already_terminal_row()
    {
        // [W1] OnBuildPreview'daki terminal-satır `continue` guard'ı YALNIZ WillBuild'i korur (segment 1'in canlı
        // succeeded→clean geçişi ezilmesin). CurrentSha o guard'dan ÖNCE atanır: segment 2'nin okuduğu build-state
        // segment 1'in persist'ini içerir, yani derlenmiş satırın sol yarısı ancak burada tazelenebilir.
        const string oldSha = "1111111aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string newSha = "2222222bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string projectId = @"C:\p\dirty.csproj";
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(projectId, "Dirty", true, oldSha)]));
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "Dirty"));
        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100)); // satır artık terminal + clean

        // Continue segmenti: preview BAYAT willBuild=true taşır ama build-state TAZE commit'i taşır.
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(projectId, "Dirty", true, newSha)]));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(newSha, row.CurrentSha); // sha TAZELENDİ
        Assert.False(row.WillBuild);          // ama canlı succeeded→clean geçişi KORUNDU
    }

    [Fact]
    public async Task Sync_completed_pushes_the_target_sha_onto_rows_that_already_exist_and_onto_later_ones()
    {
        // [W1] Olay sırası SABİTTİR: buildPreview, syncCompleted'dan ÖNCE gelir. Kart hedef sha'yı render anında
        // ata ağaçtan ÇEKSEYDİ (eski davranış) satır onu daha null'ken okur ve bir daha tazelenmezdi — ilk Sync'ten
        // sonra slot BOŞ kalırdı. Değer artık satıra İTİLİR, yani sıradan bağımsızdır.
        const string target = "b7e91d4c0affee1122334455667788990aabbcc";
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(@"C:\p\early.csproj", "Early", true, "aaaaaaa")]));
        Assert.Null(Assert.Single(vm.Projects).TargetSha); // syncCompleted henüz gelmedi

        vm.OnEvent(new SyncCompletedEvent("main", target, FetchDegraded: false, 1, 0));
        Assert.Equal(target, Assert.Single(vm.Projects).TargetSha); // ÖNCE doğmuş satır tazelendi

        // Sonradan doğan satır da (run ortasında ilk kez görülen proje) hedefi yeni bir Sync beklemeden alır.
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\late.csproj", "Late"));
        Assert.Equal(target, vm.Projects.Single(p => p.Name == "Late").TargetSha);

        // Topolojinin YERİNDE uzlaştırma yolu da (yeni bir proje eklendiğinde) aynı değeri taşır.
        vm.OnEvent(new WorkspaceTopologyEvent(
            Nodes: [Node(@"C:\p\early.csproj", "Early", 0), Node(@"C:\p\added.csproj", "Added", 1)],
            Cycles: [], Solutions: [], LayerWarnings: []));
        Assert.Equal(target, vm.Projects.Single(p => p.Name == "Added").TargetSha);
    }

    [Fact] // buildPreview arrives BEFORE the per-project events; ProjectStarted on an already-previewed row must still flip it to Started
    public async Task ProjectStarted_after_a_buildPreview_row_still_transitions_the_row_to_Started()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\dirty.csproj";
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(projectId, "Dirty", true)]));

        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "Dirty"));

        var row = Assert.Single(vm.Projects);
        Assert.Equal(ProjectRowState.Started, row.State);
        Assert.True(row.WillBuild); // preview'ın dirty=true'su korunur — henüz succeeded değil
    }

    [Fact] // v7Δ8: proje succeeded olduğu ANDA (bu run içinde canlı) willBuild=false (clean/güncel) olur
    public async Task ProjectSucceeded_flips_WillBuild_to_clean_immediately()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\dirty.csproj";
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(projectId, "Dirty", true)]));
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "Dirty"));

        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100));

        var row = Assert.Single(vm.Projects);
        Assert.False(row.WillBuild);
    }

    [Fact] // hollow (WillBuild=null — imza yok/pre-Sync) preview'dan SONRA da (henüz succeeded olmadıkça) null kalmalı
    public async Task Hollow_WillBuild_is_preserved_until_a_project_succeeds()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\hollow.csproj";
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(projectId, "Hollow", null)]));
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "Hollow"));

        Assert.Null(Assert.Single(vm.Projects).WillBuild); // henüz succeeded değil — hollow korunur

        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100));

        Assert.False(Assert.Single(vm.Projects).WillBuild); // succeeded → artık clean, hollow değil
    }

    [Fact] // preview hiç gelmeden (buildPreview YOK) doğrudan ProjectStarted gelirse satır yine hollow (null) başlar
    public async Task ProjectStarted_without_a_prior_buildPreview_defaults_WillBuild_to_hollow_null()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));

        Assert.Null(Assert.Single(vm.Projects).WillBuild);
    }

    [Fact] // [Review fix, Task 17] RunCoordinator, Continue segmentinde AYNI (dondurulmuş) plan'dan türetilmiş
           // buildPreview'ı YENİDEN yayınlar (Projects Continue'da temizlenmez) — bu yeniden-yayın, segment 1'de
           // gerçekleşen succeeded→clean canlı geçişini EZMEMELİ
    public async Task BuildPreviewEvent_on_a_Continue_segment_does_not_clobber_an_already_clean_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string projectId = @"C:\p\dirty.csproj";

        // segment 1: preview (dirty) → started → succeeded (canlı clean geçişi)
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(projectId, "Dirty", true)]));
        vm.OnEvent(new ProjectStartedEvent("r1", projectId, "Dirty"));
        vm.OnEvent(new ProjectSucceededEvent("r1", projectId, 100));
        Assert.False(Assert.Single(vm.Projects).WillBuild); // clean

        // Sonraki koşu aynı (bayat) planı yeniden preview eder — WillBuild=true (dirty)
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", ElapsedMsAtStart: 0));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(projectId, "Dirty", true)]));

        Assert.False(Assert.Single(vm.Projects).WillBuild); // succeeded→clean geçişi HÂLÂ ayakta — ezilmedi
    }

    // ---------------------------------------------------------------- 10) [Task 17] ETA text (Core.Incremental.EtaCalculator)

    [Fact] // run başlar başlamaz (hiç completion yok) — ETA NUMARASI YOK, yalnız X/N · elapsed (EtaCalculator'ın "ilk koşu" fallback'i)
    public async Task EtaText_shows_XofN_fallback_before_any_completion_no_bogus_number()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 3, 1, "Debug", ElapsedMsAtStart: 0));

        Assert.Equal("0/3 · 0s", vm.EtaText);
    }

    [Fact] // bir proje 10s'de tamamlandı, 2 proje daha kuyrukta (henüz başlamadı) — kalan tahmini bu run'ın
           // GÖZLEMLENEN süresinden (EtaCalculator'ın BuildState.LastDurationMs YERİNE bu run'ın ortalaması) üretilir
    public async Task EtaText_reflects_the_calculator_estimate_after_a_completion_using_observed_durations()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 3, 1, "Debug", ElapsedMsAtStart: 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));

        // A 10s sürdü; B/C henüz hiç başlamadı (queued=2) → ortalama=10s, (10s+10s)/1 parallelism = 20s, +0 (building yok)
        // → ilk tick smoothing yok, raw AYNEN → 20000ms → yuvarlanmış "~20s left"
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", DurationMs: 10_000));

        Assert.Equal("~20s left", vm.EtaText);
    }

    [Fact] // ham tahmin AlmostDoneThresholdMs (4000ms) altına düşünce numerik değer YOK, "· almost done"
    public async Task EtaText_shows_almost_done_when_the_raw_estimate_is_small()
    {
        long fakeNow = 0;
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1", () => fakeNow);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 2, 1, "Debug", ElapsedMsAtStart: 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\b.csproj", "B"));

        fakeNow = 1000; // A 1000ms sürdü, B de aynı anda (t=0) başlamıştı → B şu an 1000ms building
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", DurationMs: 1000));
        // completed=1 (A, avg=1000ms), building=[B: elapsed=1000, est=1000] → remaining=max(0,1000-1000)=0
        // queued=max(0, 2-1-1)=0 → raw=(0+0)/1 + 400(building var) = 400ms < 4000ms eşiği

        Assert.Equal("· almost done", vm.EtaText);
    }

    // ---------------------------------------------------------------- [A5/T69] sync / branch / worktree / topoloji

    private static ProjectNode Node(string id, string name, int buildOrder, bool? willBuild = null,
        IReadOnlyList<string>? deps = null, string? layerName = null) =>
        new(id, name, id, ["Osys"], deps ?? [], buildOrder, layerName is null ? null : 0, layerName, false, willBuild);

    [Fact]
    public async Task Sync_events_update_phase_target_sha_and_topology()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        Assert.Equal(AppPhase.Empty, vm.Phase);

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.Equal(AppPhase.Syncing, vm.Phase);

        vm.OnEvent(new WorkspaceTopologyEvent(
            Nodes: [Node(@"C:\p\a.csproj", "A", 0, willBuild: true), Node(@"C:\p\b.csproj", "B", 1, willBuild: false)],
            Cycles: [],
            Solutions: [new SolutionRef("Osys", @"C:\p\Osys.sln")],
            LayerWarnings: []));
        vm.OnEvent(new SyncCompletedEvent("main", "b7e91d4c0affee", FetchDegraded: false, 2, 0,
            ChangedCount: 1, ToBuildCount: 1, UpToDateCount: 1));

        Assert.Equal(AppPhase.Idle, vm.Phase);          // syncing → idle
        Assert.Equal("b7e91d4c0affee", vm.TargetSha);
        Assert.False(vm.FetchDegraded);
        Assert.Equal(["A", "B"], vm.Topology.Select(n => n.Name));
        Assert.Equal("Osys", Assert.Single(vm.Solutions).Name);
    }

    [Fact]
    public async Task Sync_completed_records_a_degraded_fetch()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new SyncCompletedEvent("main", "abc1234", FetchDegraded: true, 1, 0));

        Assert.True(vm.FetchDegraded);
        Assert.Equal(AppPhase.Idle, vm.Phase);
    }

    // [A5/T69] Topoloji, proje listesini BUILD-ORDER'da yeniden kurar; D1 (katman gruplaması) ve D5 (graf)
    // TopologyChanged'i dinler. [A13.2] Koleksiyon RESET'İ YASAK — liste yerinde uzlaştırılır (Add/Remove/Move).
    [Fact]
    public async Task Workspace_topology_rebuilds_the_project_list_in_build_order_and_raises_TopologyChanged()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        // Önceki bir run'ın kalıntısı: sıra TERS ve topolojide olmayan bir proje var
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\b.csproj", "B"));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\gone.csproj", "Gone"));

        var resets = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)vm.Projects).CollectionChanged += (_, e) => resets.Add(e.Action);
        int topologyChanged = 0;
        vm.TopologyChanged += (_, _) => topologyChanged++;

        vm.OnEvent(new WorkspaceTopologyEvent(
            Nodes: [Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1, deps: [@"C:\p\a.csproj"])],
            Cycles: [], Solutions: [], LayerWarnings: []));

        Assert.Equal([@"C:\p\a.csproj", @"C:\p\b.csproj"], vm.Projects.Select(p => p.Id)); // build-order
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, resets);                // [A13.2]
        Assert.Equal(1, topologyChanged);
        Assert.All(vm.Projects, p => Assert.Equal(ProjectRowState.Pending, p.State));      // Sync = yeni taban
    }

    // [E2/§5-b verify-then-fix] AYNI graf yapısının yeniden yayınlanması (mid-run Sync gibi) TopologyChanged'i
    // YENİDEN ateşlememeli (SetGraph = tam inşa + reveal stagger + kamera re-home; koşan grafı bozar). YALNIZ
    // düğüm/kenar YAPISI değişince ateşlenir; statü (InCycle/WillBuild) değişimi ateşlemez (o UpdateStatuses'tan akar).
    [Fact]
    public async Task Identical_workspace_topology_does_not_re_raise_TopologyChanged_but_a_structural_change_does()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        int topologyChanged = 0;
        vm.TopologyChanged += (_, _) => topologyChanged++;

        WorkspaceTopologyEvent Topo(params ProjectNode[] nodes) =>
            new(Nodes: nodes, Cycles: [], Solutions: [], LayerWarnings: []);

        vm.OnEvent(Topo(Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1, deps: [@"C:\p\a.csproj"])));
        Assert.Equal(1, topologyChanged); // ilk topoloji: graf ilk kez kurulur

        // AYNI yapı yeniden gelir (yeni ProjectNode örnekleri, aynı Id/Ad/kenar) — re-reveal YOK.
        vm.OnEvent(Topo(Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1, deps: [@"C:\p\a.csproj"])));
        Assert.Equal(1, topologyChanged);

        // Yapı GERÇEKTEN değişir (yeni düğüm eklenir) — yeniden kurulmalı.
        vm.OnEvent(Topo(Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1, deps: [@"C:\p\a.csproj"]),
            Node(@"C:\p\c.csproj", "C", 2)));
        Assert.Equal(2, topologyChanged);
    }

    // [E2/FIX2] §5b imzası grafın GEOMETRİ sürücüsünü (LayerIndex) İÇERMELİDİR. LayerName, LayerIndex'in vekili
    // DEĞİLDİR: kullanıcı D7 katman pattern'lerini düzenleyip (ör. ortaya boş katman ekleyip / Other kovasını
    // iten eşleşmeyen bir pattern ekleyip) bir grup düğümün LayerIndex'ini kaydırdığında Id/Ad/LayerName/
    // Dependencies VE OrderBy(LayerIndex) yayın sırası BYTE-AYNI kalabilir. LayerIndex imzada yoksa
    // TopologyChanged ateşlenmez → SetGraph koşmaz → düğümler bayat satırlarda (bir tam satır kayması +
    // eksik/fazla boş katman bandı) kalır. SADECE LayerIndex değişince de yeniden kurulmalı.
    [Fact]
    public async Task A_layer_index_only_shift_re_raises_TopologyChanged()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        int topologyChanged = 0;
        vm.TopologyChanged += (_, _) => topologyChanged++;

        // Aynı Id/Ad/LayerName/Dependencies + AYNI yayın sırası; SADECE LayerIndex farklı.
        ProjectNode NodeAt(int layerIndex) => new(
            @"C:\p\a.csproj", "A", @"C:\p\a.csproj",
            SolutionNames: ["Osys"], Dependencies: [], BuildOrder: 0,
            LayerIndex: layerIndex, LayerName: "Edge", InCycle: false, WillBuild: null);
        WorkspaceTopologyEvent Topo(ProjectNode n) => new([n], [], [], []);

        vm.OnEvent(Topo(NodeAt(0)));
        Assert.Equal(1, topologyChanged); // ilk topoloji: graf ilk kez kurulur

        vm.OnEvent(Topo(NodeAt(0))); // hiçbir şey değişmedi — ateşlenmez
        Assert.Equal(1, topologyChanged);

        vm.OnEvent(Topo(NodeAt(1))); // SADECE LayerIndex kaydı → graf satırı yer değiştirir → yeniden kurulmalı
        Assert.Equal(2, topologyChanged);
    }

    // Topolojinin hemen ardından gelen buildPreview, satırların will-dot'unu kurar (mevcut handler — İKİNCİ
    // bir will-build yolu açılmaz). Sync sonrası hiçbir düğüm hollow KALMAZ.
    [Fact]
    public async Task Build_preview_after_topology_fills_the_will_build_dots()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new WorkspaceTopologyEvent(
            Nodes: [Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1)],
            Cycles: [], Solutions: [], LayerWarnings: []));
        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(@"C:\p\a.csproj", "A", true),
            new BuildPreviewItem(@"C:\p\b.csproj", "B", false),
        ]));

        Assert.True(vm.Projects[0].WillBuild);
        Assert.False(vm.Projects[1].WillBuild);
    }

    // Koşan bir run'ın CANLI satır durumu, araya giren bir Sync tarafından SİLİNMEZ (mid-run Sync koruması).
    [Fact]
    public async Task Workspace_topology_does_not_reset_row_state_while_a_run_is_in_flight()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 2400));

        vm.OnEvent(new WorkspaceTopologyEvent(
            Nodes: [Node(@"C:\p\a.csproj", "A", 0)], Cycles: [], Solutions: [], LayerWarnings: []));

        Assert.Equal(ProjectRowState.Succeeded, Assert.Single(vm.Projects).State);
    }

    // Başarısız bir Sync (ör. kök bir git repo'su değil) syncCompleted YAYINLAMAZ — yalnız planFailed gelir.
    // Faz Syncing'de ASILI KALMAMALIDIR, aksi halde şerit sonsuza dek "▸ Sync — git fetch origin…" gösterirdi.
    [Fact]
    public async Task A_failed_sync_releases_the_syncing_phase_instead_of_hanging_there()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.Equal(AppPhase.Syncing, vm.Phase);

        vm.OnEvent(new ErrorEvent("planFailed", "'D:\\repo' is not a usable git repository: ..."));

        Assert.Equal(AppPhase.Boot, vm.Phase); // topoloji hiç gelmedi → "repo var, Sync yapılmadı"
    }

    [Fact]
    public async Task A_failed_resync_falls_back_to_idle_when_a_topology_is_already_known()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", 0)], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "abc1234", false, 1, 0));

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        vm.OnEvent(new ErrorEvent("planFailed", "boom"));

        Assert.Equal(AppPhase.Idle, vm.Phase); // önceki topoloji hâlâ geçerli
    }

    // [Fix wave 1 — Finding 2] Sync SALT-OKURDUR ve KOŞAN bir run sırasında da tetiklenebilir (bkz.
    // OnWorkspaceTopology'nin mid-run koruması). A5, `planFailed`'ı Sync'in de hata kodu yaptı; başarısız bir
    // Sync CANLI run state'ini yıkarsa motor derlemeye devam ederken UI run'ı bitmiş gösterir (Stop erişilemez
    // olur) ve sonraki projectSucceeded/runCompleted YIKILMIŞ bir state'e düşer.
    [Fact]
    public async Task A_failed_sync_during_a_live_run_releases_the_phase_without_tearing_down_the_run()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        // [topoloji kapısı] Koşan bir run ancak bir Sync'ten SONRA var olabilir — senaryo bu yüzden topolojiyle kurulur.
        VmTopology.Seed(vm);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        Assert.True(vm.IsRunning);

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        vm.OnEvent(new ErrorEvent("planFailed", "'D:\\repo' is not a usable git repository: ..."));

        Assert.True(vm.IsRunning);                      // run YAŞIYOR — motor hâlâ derliyor
        Assert.True(vm.StopCommand.CanExecute(null));   // Stop erişilebilir kaldı [Kısıt 3]
        Assert.Equal(AppPhase.Idle, vm.Phase);          // faz Syncing'de ASILI kalmadı (elde topoloji var → Idle)

        // Motor derlemeye devam etti: sonraki event'ler hâlâ AYAKTA bir state'e düşer
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 1200));
        Assert.Equal(ProjectRowState.Succeeded, Assert.Single(vm.Projects).State);
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 1500));
        Assert.False(vm.IsRunning);
        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    // Ayrım ÇİFT YÖNLÜ olmalı: Sync in-flight iken gelen bir planFailed, run PLANLAMA penceresindeyse
    // (IsStarting — `planFailed`'ın run tarafındaki TEK üretim penceresi) yine run'ı bitirir; aksi halde
    // hiçbir engine event'i gelmeyeceğinden IsStarting kalıcı true kalır ve butonlar sonsuza dek kilitlenir.
    [Fact]
    public async Task A_plan_failure_while_a_run_is_still_starting_still_ends_the_run_even_if_a_sync_is_in_flight()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.IsStarting = true; // Rebuild gönderildi, runStarted HENÜZ gelmedi = planlama penceresi
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));

        vm.OnEvent(new ErrorEvent("planFailed", "planlama patladı"));

        Assert.False(vm.IsStarting);
        // [Fix wave 1, C2 review Finding 1] _syncInFlight BİLEREK true kalır (çakışan pencere — yukarıdaki
        // TryConsumeSyncFailure yorumu), yani VM'e göre bir Sync HÂLÂ uçuşta olabilir; RebuildCommand artık
        // buna da bakıyor (CanRebuildOrRetry) — mid-Sync clearBuffers'ın canlı transkripti bozma riskiyle
        // TUTARLI biçimde burada da engelli kalır.
        Assert.False(vm.RebuildCommand.CanExecute(null));
        Assert.Equal(AppPhase.Boot, vm.Phase); // faz yine de bırakılır
    }

    // Ayrım KODA da bakar: `runFailed` Sync'in ASLA yayınlamadığı (yalnız RunCoordinator'ın dış catch'inden
    // gelen, run ORTASINDA da gelebilen) bir koddur — uçuşta bir Sync olması onu Sync'e atfettirmemelidir,
    // yoksa gerçek bir run çöküşünde IsRunning kalıcı true kalır (buton kaması).
    [Fact]
    public async Task A_mid_run_runFailed_still_ends_the_run_even_while_a_sync_is_in_flight()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));

        vm.OnEvent(new ErrorEvent("runFailed", "beklenmeyen hata"));

        Assert.False(vm.IsRunning);
        // [Fix wave 1, C2 review Finding 1] Sync HÂLÂ GERÇEKTEN uçuşta (Phase == Syncing, aşağıda doğrulanır) —
        // RebuildCommand artık _syncInFlight'a da baktığından (CanRebuildOrRetry) burada BİLEREK engelli kalır:
        // run bitmiş olsa da canlı Sync transkripti hâlâ SyncProgressEvent ile büyüyor olabilir.
        Assert.False(vm.RebuildCommand.CanExecute(null));
        Assert.Equal(AppPhase.Syncing, vm.Phase); // Sync HÂLÂ uçuşta — fazı bu hata bırakmaz
    }

    [Fact]
    public async Task Branch_list_event_fills_branches()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new BranchListEvent([
            new BranchRef("main", "abc1234", true, false),
            new BranchRef("origin/main", "abc1234", false, true),
        ]));

        Assert.Equal(["main", "origin/main"], vm.Branches.Select(b => b.Name));
        Assert.True(vm.Branches[0].IsActive);

        // İkinci liste ÖNCEKİNİ değiştirir, üstüne eklemez
        vm.OnEvent(new BranchListEvent([new BranchRef("feature-x", "def5678", false, false)]));
        Assert.Equal(["feature-x"], vm.Branches.Select(b => b.Name));
    }

    [Fact]
    public async Task Worktree_list_event_fills_worktrees()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        vm.OnEvent(new WorktreeListEvent([new Worktree("main-1", "main", @"C:\pool\main-1", true, 4096)]));

        Assert.Equal("main-1", Assert.Single(vm.Worktrees).Name);
    }

    // [D8] Gerçek 50ms beklenmez — tick tamamen kontrol edilir (ConsoleBatcherTests deseni).
    [Fact]
    public async Task Sync_progress_lines_reach_the_console_batcher()
    {
        int ticks = 0;
        ConsoleBatcher? batcher = null;
        Task Tick(CancellationToken ct)
        {
            ticks++;
            if (ticks == 2) batcher!.Complete(); // 1. tick birikenleri flush eder, 2. tick pump'ı kapatır
            return Task.CompletedTask;
        }
        batcher = new ConsoleBatcher(Tick);
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, batcher, () => "r1")
        {
            // [D4/T56-UI] Anlatı satırı "HH:MM:SS " damgasıyla bileşilir (design-v1 §2.5); saat deterministik enjekte.
            WallClock = () => new DateTimeOffset(2026, 7, 23, 12, 4, 7, TimeSpan.Zero),
        };

        vm.OnEvent(new SyncProgressEvent("git fetch origin main", "cmd"));
        vm.OnEvent(new SyncProgressEvent("Sync complete — 7 changed projects, 14 to build", "info"));

        var flushes = new List<string>();
        await batcher.PumpAsync((text, _) => flushes.Add(text), CancellationToken.None);

        // Batcher'a düşen satırlar artık "HH:MM:SS " önekli (ham ▸ satırı ONUN İÇİNDE — colorizer damga+▸'yi çözer).
        Assert.Equal(
            ["git fetch origin main\nSync complete — 7 changed projects, 14 to build\n"],
            flushes);
        // Konsol dokümanına da düşer (run dokümanı aktifken)
        Assert.Contains("git fetch origin main", vm.GetRunDocumentText(), StringComparison.Ordinal);
    }
}
