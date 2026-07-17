using System.IO;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
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
}
