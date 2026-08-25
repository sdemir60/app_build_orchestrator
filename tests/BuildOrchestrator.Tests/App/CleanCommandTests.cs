using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [clean] Bakım kutusundaki Clean düğmesinin VM yüzeyi: komut gönderimi, konsol sıfırlama, karşılıklı
/// dışlama kapıları ve serbest bırakma yolları.
/// <para>Harness <see cref="RunViewModelStateTests"/> ile aynıdır: başlatılmamış <see cref="EngineHost"/> —
/// gönderim SENKRON düşer ve VM içinde yutulur, yani "gönderim başarısız" yolu VARSAYILANDIR. Uçuş durumu
/// gerçek motor olmadan <c>vm.OnEvent(...)</c> ile kurulur. D8: sleep/poll yok.</para>
/// </summary>
public class CleanCommandTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    private static ProjectNode Node(string id, string name) => new(id, name, id, ["Osys"], [], 0, null, null, false, null);

    /// <summary>Run komutlarının kapısı topolojidir; Clean kapılarını ölçen testler önce onu kurar.</summary>
    private static void SeedTopology(RunViewModel vm) =>
        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A")], [], [], []));

    private static CleanCompletedEvent Completed() => new(2, 4, 1024, 0, 2);

    // ---------------------------------------------------------------- gönderim

    [Fact]
    public async Task Clean_sends_cleanWorkspace_with_the_workspace_root()
    {
        var vm = NewVm();
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.CleanCommand.ExecuteAsync(null);

        Assert.Equal(@"D:\repo", Assert.Single(sent.OfType<CleanWorkspaceCommand>()).RootPath);
    }

    // Clean bir "sıfırdan başla" anıdır: konsol önceki koşunun anlatısıyla karışmamalı. Sıfırlama bloğu
    // BeginRunAsync ile ORTAKTIR (kopya YASAK) — bu test o ortak yolun Clean'den de geçtiğini pinler.
    [Fact]
    public async Task Clean_clears_the_console_and_writes_the_requested_line()
    {
        var vm = NewVm();
        vm.OnEvent(new SyncProgressEvent("git fetch origin main", "cmd"));
        Assert.Contains("git fetch", vm.GetRunDocumentText(), StringComparison.Ordinal);

        await vm.CleanCommand.ExecuteAsync(null);

        string text = vm.GetRunDocumentText();
        Assert.DoesNotContain("git fetch", text, StringComparison.Ordinal);
        Assert.Contains("clean requested", text, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- kapı matrisi

    [Fact]
    public void Clean_is_disabled_without_a_workspace()
    {
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1");
        Assert.False(vm.CleanCommand.CanExecute(null));

        vm.RootPath = @"D:\repo";
        Assert.True(vm.CleanCommand.CanExecute(null));
    }

    [Fact]
    public void Clean_is_disabled_while_a_run_is_starting_or_running()
    {
        var vm = NewVm();
        SeedTopology(vm);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        Assert.False(vm.CleanCommand.CanExecute(null));

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500));
        Assert.True(vm.CleanCommand.CanExecute(null));
    }

    [Fact]
    public void Clean_is_disabled_while_a_sync_is_in_flight()
    {
        var vm = NewVm();

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(vm.CleanCommand.CanExecute(null));

        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));
        Assert.True(vm.CleanCommand.CanExecute(null));
    }

    [Fact]
    public void Clean_is_disabled_while_the_engine_is_unavailable()
    {
        var vm = NewVm();
        vm.OnEngineUnavailable(@"D:\repo\supervisor\BuildOrchestrator.Supervisor.exe");

        Assert.True(vm.IsEngineUnavailable);
        Assert.False(vm.CleanCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- karşılıklı dışlama

    // Clean silerken bir build başlarsa MSBuild, altından çekilen bir obj/bin ile yarışır. Kapı hem UÇUŞ
    // (cleanStarted geldi) hem İSTEK penceresini (komut yolda, motor henüz cevap vermedi) kapsar.
    [Fact]
    public void Sync_build_rebuild_and_cycles_are_disabled_while_a_clean_is_in_flight_and_reopen_on_completion()
    {
        var vm = NewVm();
        SeedTopology(vm);
        Assert.True(vm.SyncCommand.CanExecute(null));
        Assert.True(vm.BuildCommand.CanExecute(null));
        Assert.True(vm.RebuildCommand.CanExecute(null));

        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));

        Assert.False(vm.SyncCommand.CanExecute(null));
        Assert.False(vm.BuildCommand.CanExecute(null));
        Assert.False(vm.RebuildCommand.CanExecute(null));
        Assert.False(vm.BuildCyclesCommand.CanExecute(null));
        Assert.False(vm.CleanCommand.CanExecute(null)); // ikinci bir Clean de anlamsızdır

        vm.OnEvent(Completed());

        Assert.True(vm.SyncCommand.CanExecute(null));
        Assert.True(vm.BuildCommand.CanExecute(null));
        Assert.True(vm.RebuildCommand.CanExecute(null));
        Assert.True(vm.CleanCommand.CanExecute(null));
    }

    // İstek penceresi: komut gönderildikten hemen SONRA, motor cevap vermeden önce de kapı KAPALIDIR —
    // aksi halde ikinci bir basış motora ikinci bir silme kuyruklatırdı.
    [Fact]
    public async Task The_gate_is_already_closed_in_the_request_window_before_the_engine_answers()
    {
        var vm = NewVm();
        SeedTopology(vm);
        bool closedAtSendTime = true;
        vm.DebugOnCommandSent = cmd =>
        {
            if (cmd is CleanWorkspaceCommand) closedAtSendTime = !vm.CleanCommand.CanExecute(null);
        };

        await vm.CleanCommand.ExecuteAsync(null);

        Assert.True(closedAtSendTime, "kapı GÖNDERİMDEN ÖNCE kapanmalı");
    }

    // ---------------------------------------------------------------- serbest bırakma

    // Motor hazır değilse gönderim SENKRON düşer ve hiçbir cleanStarted GELMEZ — kapı burada açılmazsa
    // düğme kalıcı pasif kalırdı.
    [Fact]
    public async Task A_failed_send_reopens_the_clean_gate()
    {
        var vm = NewVm();
        SeedTopology(vm);

        await vm.CleanCommand.ExecuteAsync(null); // başlatılmamış engine → gönderim düşer

        Assert.True(vm.CleanCommand.CanExecute(null));
        Assert.True(vm.SyncCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("cleanFailed")]
    [InlineData("cleanRejected")]
    public void A_clean_error_releases_the_clean_surface(string code)
    {
        var vm = NewVm();
        SeedTopology(vm);
        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));
        Assert.False(vm.SyncCommand.CanExecute(null));

        vm.OnEvent(new ErrorEvent(code, "something went wrong"));

        Assert.True(vm.CleanCommand.CanExecute(null));
        Assert.True(vm.SyncCommand.CanExecute(null));
        Assert.True(vm.BuildCommand.CanExecute(null));
    }

    // Motor Clean ORTASINDA ölürse hiçbir cleanCompleted/clean-hatası gelmez: bayrak sızarsa yeniden
    // başlatılan motorda da düğmeler kilitli kalırdı.
    [Fact]
    public void Engine_death_mid_clean_releases_the_clean_surface()
    {
        var vm = NewVm();
        SeedTopology(vm);
        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));

        vm.OnEngineExited(1);

        // Ölüm YENİDEN BAŞLATILABİLİRdir (IsEngineUnavailable false kalır) — kapıyı kapatan tek şey
        // sızmış bir clean bayrağı olurdu.
        Assert.True(vm.EngineRestartable);
        Assert.True(vm.CleanCommand.CanExecute(null));
        Assert.True(vm.SyncCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- konsol + stream

    [Fact]
    public void Clean_progress_lines_flow_into_the_run_document()
    {
        var vm = NewVm();
        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));

        vm.OnEvent(new CleanProgressEvent("A — bin + obj removed (12 MB)", "dim"));

        Assert.Contains("bin + obj removed", vm.GetRunDocumentText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Clean_completion_pushes_one_stream_summary_line()
    {
        var vm = NewVm();
        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));
        int before = vm.StreamEventCount;

        vm.OnEvent(new CleanCompletedEvent(36, 71, 4_294_967_296, 2, 36));

        Assert.Equal(before + 1, vm.StreamEventCount);
        string line = vm.StreamEvents[^1].Text;
        Assert.Contains("Clean", line, StringComparison.Ordinal);
        Assert.Contains("36", line, StringComparison.Ordinal);
    }

    /// <summary>[design v1.13.2 §9] "Konsol + event stream HER işlemde temizlenir" — Clean de bir işlemdir ve
    /// <c>SyncCoreAsync(clearBuffers:true)</c> ile AYNI iki metodu tıklama anında çağırır
    /// (<see cref="RunViewModelStateTests.Sync_clears_the_console_and_stream_left_over_from_the_previous_operation"/>'ın
    /// Clean ikizi). Planın ilk hâli stream'i "mevcut sözleşme" diye koruyordu; o sözleşme v1.13.2 ile değişti.</summary>
    [Fact]
    public async Task Clean_clears_the_stream_left_over_from_the_previous_operation()
    {
        var vm = NewVm();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 100));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 100));
        Assert.True(vm.StreamEventCount > 0, "ön-koşul: event stream'de ÖNCEKİ işlemden iz yok — vakum");

        await vm.CleanCommand.ExecuteAsync(null);

        Assert.Equal(0, vm.StreamEventCount); // gönderim hatası yalnız konsola yazar, stream'e dokunmaz
    }

    /// <summary>[design v1.11.0 §2.2] Kalıcı işlem pill'i TIKLAMA ANINDA yazılır. Sözcük <c>DEEP CLEAN</c>:
    /// menüdeki tek-proje Clean'i (<c>/t:Clean</c>, <c>CLEAN</c>) ile karıştırılmaz.</summary>
    [Fact]
    public async Task Clean_writes_the_deep_clean_operation_pill_at_click_time()
    {
        var vm = NewVm();
        Assert.NotEqual(OperationLabel.DeepClean, vm.CurrentOperation);

        await vm.CleanCommand.ExecuteAsync(null);

        Assert.Equal(OperationLabel.DeepClean, vm.CurrentOperation);
    }

    /// <summary>[v1.16.0 · clean] Alt bardaki <c>N behind</c> chip'i de bakım kilidine tabidir: başarılı bir pull
    /// otomatik Sync koşar ve o Sync, tam o sırada silinen bin/obj'i okurdu.</summary>
    [Fact]
    public void The_pull_chip_is_disabled_while_a_clean_is_in_flight_and_reopens_on_completion()
    {
        var vm = NewVm();
        vm.OnEvent(new SyncCompletedEvent("main", "b7e91d4", FetchDegraded: false, 1, 0, Behind: 3));
        Assert.True(vm.PullRepositoryCommand.CanExecute(null));

        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));
        Assert.False(vm.PullRepositoryCommand.CanExecute(null));

        vm.OnEvent(Completed());
        Assert.True(vm.PullRepositoryCommand.CanExecute(null));
    }
}
