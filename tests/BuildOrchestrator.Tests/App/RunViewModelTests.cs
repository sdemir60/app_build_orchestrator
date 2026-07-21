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

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Continue, 177, 6, "Debug", ElapsedMsAtStart: 4200));

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
        var load = vm.LoadProjectLogAsync(a);
        vm.OnEvent(new ProjectLogChunkEvent(a, 0, "", IsLast: true, ThroughLineNumber: 0));
        await load.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(a, vm.ActiveProjectId);

        vm.ShowRun();

        Assert.Null(vm.ActiveProjectId);
    }

    // ---------------------------------------------------------------- 5) hata-yalnız outcome'lar runCompleted BEKLEMEZ

    [Fact]
    public async Task Error_only_outcome_reenables_rebuild_without_a_runCompleted()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Continue, 1, 1, "Debug", 0)); // Continue akışı: IsRunning=true oldu

        vm.OnEvent(new ErrorEvent("noResumableRun", "sürdürülebilir run yok"));

        Assert.False(vm.IsRunning);
        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    [Fact] // stop-during-planning: runStarted HİÇ gelmedi, runStopped var ama runCompleted YOK
    public async Task RunStopped_without_a_prior_RunStarted_reenables_rebuild()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

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

    // ---------------------------------------------------------------- 6) Stop/Continue komut gönderimi (gerçek Supervisor)

    [Fact]
    public async Task Stop_sends_StopRunCommand_graceful_and_engine_acks_it()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var stopped = new TaskCompletionSource<RunStoppedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.EventReceived += e => { if (e is RunStoppedEvent s) stopped.TrySetResult(s); };
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0)); // App'in "run aktif" inancı

        await vm.StopCommand.ExecuteAsync(null);

        // Gerçek Supervisor'da eşleşen bir run YOK (yalnız VM'in inancı kuruldu) → TryRequestStop false →
        // T4-base yol: hemen runStopped(WasHard=false) döner. Bu, StopRunCommand(r1, Graceful)'ın DOĞRU
        // gönderildiğinin gözlenebilir kanıtıdır.
        var result = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("r1", result.RunId);
        Assert.False(result.WasHard);
    }

    [Fact]
    public async Task Continue_sends_StartRunCommand_with_ContinueMode()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        await engine.StartAsync();
        string root = Directory.CreateTempSubdirectory("bo-vm-continue-").FullName;
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r2") { RootPath = root };
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 0, 0, 0, 1, 10)); // CanContinue=true durumu kurar
        var error = new TaskCompletionSource<ErrorEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.EventReceived += e => { if (e is ErrorEvent er) error.TrySetResult(er); };

        await vm.ContinueCommand.ExecuteAsync(null);

        // Gerçek Supervisor'da sürdürülebilir bir run yok → noResumableRun. Bu, StartRunCommand'ın
        // Mode=Continue ile DOĞRU gönderildiğinin kanıtıdır (Rebuild bu reddi asla almaz).
        var result = await error.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("noResumableRun", result.Code);
    }

    [Fact]
    public async Task Rebuild_is_disabled_while_running_and_reenabled_after_completion()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        Assert.True(vm.RebuildCommand.CanExecute(null));

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        Assert.False(vm.RebuildCommand.CanExecute(null));

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500));
        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- 6b) [Fix wave 1, Finding 1] CanExecuteChanged canlı UI'a ULAŞMALI
    // CommunityToolkit RelayCommand CommandManager.RequerySuggested'a ABONE OLMAZ — [NotifyCanExecuteChangedFor]
    // olmadan gerçek pencerede Stop/Continue butonları hiç yeniden sorgulanmaz (Stop hep disabled, Continue hep
    // ölü kalır). Bu testler CanExecute'in DOĞRU DEĞERİ değil, event'in GERÇEKTEN ATEŞLENDİĞİNİ kanıtlar.

    [Fact]
    public async Task RunStarted_raises_CanExecuteChanged_for_Rebuild_Stop_and_Continue()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        bool rebuildChanged = false, stopChanged = false, continueChanged = false;
        vm.RebuildCommand.CanExecuteChanged += (_, _) => rebuildChanged = true;
        vm.StopCommand.CanExecuteChanged += (_, _) => stopChanged = true;
        vm.ContinueCommand.CanExecuteChanged += (_, _) => continueChanged = true;

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0)); // IsRunning false→true

        Assert.True(rebuildChanged); // CanRebuild = !IsRunning
        Assert.True(stopChanged);    // CanStop = IsRunning — Kısıt 3: kullanıcı Stop'a hiç basamaz olmasın diye
        Assert.True(continueChanged); // CanContinueRun = !IsRunning && CanContinue
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

    [Fact] // CanContinue'nun KENDİ NotifyCanExecuteChangedFor'unu, IsRunning'in DEĞİŞMEDİĞİ bir senaryoda izole eder
    public async Task CanContinue_becoming_true_raises_ContinueCommand_CanExecuteChanged_without_an_IsRunning_transition()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        Assert.False(vm.IsRunning); // hiç run başlamadı — IsRunning zaten false
        bool continueChanged = false;
        vm.ContinueCommand.CanExecuteChanged += (_, _) => continueChanged = true;

        // OnRunCompleted IsRunning'i false yapar (false→false, DEĞİŞİM YOK, [ObservableProperty] no-op) ama
        // CanContinue'yu false→true çevirir (outcome Stopped) — yalnız _canContinue'nun kendi annotation'ı
        // tetiklenirse ContinueCommand.CanExecuteChanged ateşlenir.
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 0, 0, 0, 1, 10));

        Assert.True(continueChanged);
        Assert.True(vm.ContinueCommand.CanExecute(null));
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
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
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
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
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
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
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

        await vm.RebuildCommand.ExecuteAsync(null); // gönderim başarısız — hiçbir engine event'i asla gelmeyecek

        Assert.False(vm.IsStarting);
        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    [Fact] // Continue için aynı senaryo — ContinueCommand da kalıcı kilitlenmemeli
    public async Task ContinueCommand_recovers_IsStarting_when_the_initial_send_fails_synchronously()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 0, 0, 0, 1, 10)); // CanContinue=true durumu kurar

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.False(vm.IsStarting);
        Assert.True(vm.ContinueCommand.CanExecute(null));
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
    // event'i asla gelmeyeceğinden IsStarting/IsRunning/CanContinue SONSUZA DEK kilitli kalırdı, "Restart
    // Engine" bile açmıyordu. OnEngineExited bu kamayı kapatır.

    [Fact] // startRun gönderildi, runStarted HENÜZ gelmedi (IsStarting=true) — engine bu pencerede ölürse butonlar açılmalı
    public async Task OnEngineExited_while_IsStarting_resets_run_state_and_reenables_Rebuild()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        await vm.RebuildCommand.ExecuteAsync(null); // gönderim başarılı — IsStarting=true, runStarted HENÜZ gelmedi
        Assert.True(vm.IsStarting);

        vm.OnEngineExited(1);

        Assert.False(vm.IsStarting);
        Assert.False(vm.IsRunning);
        Assert.False(vm.CanContinue);
        Assert.True(vm.RebuildCommand.CanExecute(null)); // butonlar artık un-wedged
        Assert.False(vm.StopCommand.CanExecute(null));
    }

    [Fact] // run ORTASINDA (IsRunning=true, runStarted zaten geldi) engine ölürse yine sıfırlanmalı
    public async Task OnEngineExited_while_IsRunning_resets_run_state_and_reenables_Rebuild()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 1, "Debug", 0));
        Assert.True(vm.IsRunning);

        vm.OnEngineExited(139);

        Assert.False(vm.IsRunning);
        Assert.False(vm.IsStarting);
        Assert.False(vm.CanContinue);
        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    [Fact] // hiçbir run aktif değilken (idle, zaten temiz) engine ölürse no-op — state bozulmamalı, fırlamamalı
    public async Task OnEngineExited_with_nothing_running_is_a_noop()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        Assert.False(vm.IsRunning);
        Assert.False(vm.IsStarting);
        Assert.True(vm.RebuildCommand.CanExecute(null));

        vm.OnEngineExited(null); // framing-hatası senaryosu (exit code yok) — fırlamamalı

        Assert.False(vm.IsRunning);
        Assert.False(vm.IsStarting);
        Assert.False(vm.CanContinue);
        Assert.True(vm.RebuildCommand.CanExecute(null));
    }

    [Fact] // normal runCompleted akışı ZATEN sıfırlamıştı — ardından gelen engine-death bu temiz durumu bozmamalı (idempotent)
    public async Task OnEngineExited_after_a_normal_runCompleted_does_not_corrupt_already_reset_state()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
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
        Assert.Contains("139", vm.EngineDiedMessage);
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

    [Fact] // [Fix wave 1, Finding 1 deseniyle tutarlı] CanExecuteChanged GERÇEKTEN ateşlenmeli, yoksa gerçek pencerede buton hiç yeniden sorgulanmaz
    public async Task OnEngineExited_raises_CanExecuteChanged_for_Rebuild_Stop_and_Continue()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
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
        // X ↔ Y cycle fixture (RunCoordinatorTests ile aynı desen): gerçek MSBuild child'ı DOĞMADAN
        // deterministik pre-skip üretir.
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

        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        await engine.StartAsync();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = root };
        var final = new TaskCompletionSource<IpcEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.EventReceived += e =>
        {
            vm.OnEvent(e);
            if (e is RunCompletedEvent or ErrorEvent { Code: "msbuildNotFound" }) final.TrySetResult(e);
        };

        await vm.RebuildCommand.ExecuteAsync(null);
        var outcome = await final.Task.WaitAsync(TimeSpan.FromSeconds(15));
        if (outcome is ErrorEvent { Code: "msbuildNotFound" } err) Skip.If(true, err.Message);

        var done = Assert.IsType<RunCompletedEvent>(outcome);
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

        // segment 2 (Continue): RunCoordinator aynı (bayat) planı yeniden preview eder — WillBuild=true (dirty)
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Continue, 1, 1, "Debug", ElapsedMsAtStart: 0));
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
        var vm = new RunViewModel(engine, batcher, () => "r1");

        vm.OnEvent(new SyncProgressEvent("▸ git fetch origin main", "cmd"));
        vm.OnEvent(new SyncProgressEvent("Sync complete — 7 changed projects, 14 to build", "info"));

        var flushes = new List<string>();
        await batcher.PumpAsync(text => flushes.Add(text), CancellationToken.None);

        Assert.Equal(["▸ git fetch origin main\nSync complete — 7 changed projects, 14 to build\n"], flushes);
        // Konsol dokümanına da düşer (run dokümanı aktifken)
        Assert.Contains("▸ git fetch origin main", vm.GetRunDocumentText(), StringComparison.Ordinal);
    }
}
