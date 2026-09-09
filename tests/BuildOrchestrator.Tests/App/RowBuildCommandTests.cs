using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [tek proje · design v1.11.0 §3.8] Satırdan tetiklenen koşunun VM tarafı: play/⋯ → <see
/// cref="RunViewModel.BuildProjectCommand"/>/<see cref="RunViewModel.RebuildProjectCommand"/>. Satırdan
/// tetiklemek satıra tıklamak DEĞİLDİR: seçim + filtre düşer (graf odaktan fit görünüme, konsol ana loga
/// döner), koreografi yalnız hedef satırla oynar, komut kapsamı taşır. Koşu boyunca hedef satır işaretlidir
/// (play → Stop) ve her satır kilidi bilir (busy tooltip'i).
/// </summary>
public class RowBuildCommandTests
{
    private const string A = @"C:\p\a.csproj";
    private const string B = @"C:\p\b.csproj";

    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm(EngineHost engine)
    {
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        VmTopology.Seed(vm, A, B);
        return vm;
    }

    private static ProjectRowViewModel Row(RunViewModel vm, string id) => vm.Projects.Single(r => r.Id == id);

    [Fact]
    public async Task Build_project_sends_a_scoped_build_and_clears_selection_and_filter()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        vm.SelectProject(B);                       // grafta başka bir projeye odaklanılmış
        vm.ToggleFilter(ProjectFilter.Failed);
        StartRunCommand? sent = null;
        vm.DebugOnCommandSent = c => { if (c is StartRunCommand s) sent = s; };

        await vm.BuildProjectCommand.ExecuteAsync(A);

        Assert.NotNull(sent);
        Assert.Equal(RunMode.Build, sent!.Mode);
        Assert.Equal(A, sent.ScopeProjectId);
        Assert.Null(vm.SelectedProjectId);         // odak düşer → graf fit görünüme döner
        Assert.Empty(vm.ActiveFilters);
        Assert.Contains("build requested — a (single project)", vm.GetRunDocumentText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rebuild_project_sends_RunMode_Rebuild_with_the_same_scope()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        StartRunCommand? sent = null;
        vm.DebugOnCommandSent = c => { if (c is StartRunCommand s) sent = s; };

        await vm.RebuildProjectCommand.ExecuteAsync(B);

        Assert.Equal((RunMode.Rebuild, B), (sent!.Mode, sent.ScopeProjectId));
        Assert.Equal(OperationLabel.Rebuild, vm.CurrentOperation); // pill hedef adı TAŞIMAZ (v1.13.2)
    }

    /// <summary>Koreografi yalnız hedef satırla oynar — kapsam bir tahmin değil, motorun derleyeceği kümedir.</summary>
    [Fact]
    public async Task The_opening_choreography_marks_only_the_target_row()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        IReadOnlyList<ProjectRowViewModel>? scope = null;
        vm.OperationChoreography = s => { scope = s; return Task.CompletedTask; };

        await vm.BuildProjectCommand.ExecuteAsync(A);

        Assert.Equal([Row(vm, A)], scope);
    }

    /// <summary>
    /// <b>Satırdaki play ile ⋯ menüsünün Build maddesi AYNI yoldan geçer</b> — ikisi de aynı komuta aynı
    /// hedefi verir, dolayısıyla seçim düşmesi (graf odaktan fit görünüme dönmesi), filtre sıfırlaması,
    /// koreografi kapsamı ve gönderilen komut BİREBİR aynıdır.
    /// <para>Bu test kullanıcı bildirimi üzerine yazıldı ("play çalışıyor, menüden Build'de fit olmuyor"):
    /// iki yolun ayrışabileceği tek yer komutun kendisidir ve o da paylaşılır. Menü, komutu satırın kendi
    /// kimliğiyle çalıştırır (<c>ProjectRow.OnRowMenuItem</c>); burada o çağrının VM tarafındaki sonucu,
    /// play'in sonucuyla KARŞILAŞTIRILARAK pinlenir.</para>
    /// </summary>
    [Fact]
    public async Task The_menus_build_item_produces_exactly_what_the_play_button_produces()
    {
        static async Task<(string? Selection, IReadOnlySet<string> Filters, IReadOnlyList<string> Scope, StartRunCommand Sent)>
            RunAsync(EngineHost engine, Func<RunViewModel, Task> invoke)
        {
            var vm = NewVm(engine);
            vm.SelectProject(B);                       // grafta BAŞKA bir projeye odaklanılmış
            vm.ToggleFilter(ProjectFilter.Failed);
            IReadOnlyList<ProjectRowViewModel> scope = [];
            vm.OperationChoreography = s => { scope = s; return Task.CompletedTask; };
            StartRunCommand? sent = null;
            vm.DebugOnCommandSent = c => { if (c is StartRunCommand s) sent = s; };
            await invoke(vm);
            return (vm.SelectedProjectId, vm.ActiveFilters, [.. scope.Select(r => r.Id)], sent!);
        }

        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var viaPlay = await RunAsync(engine, vm => vm.BuildProjectCommand.ExecuteAsync(A));
        // Menü maddesi komutu satır üzerinden ICommand olarak çalıştırır — ProjectRow.OnRowMenuItem'ın yaptığı.
        var viaMenu = await RunAsync(engine, vm =>
        {
            System.Windows.Input.ICommand command = vm.BuildProjectCommand;
            Assert.True(command.CanExecute(A));
            command.Execute(A);
            return Task.CompletedTask;
        });

        Assert.Null(viaMenu.Selection);                       // seçim düştü → graf fit görünüme döner
        Assert.Equal(viaPlay.Selection, viaMenu.Selection);
        Assert.Empty(viaMenu.Filters);
        Assert.Equal([A], viaMenu.Scope);                     // koreografi yalnız hedefi işaretler
        Assert.Equal(viaPlay.Scope, viaMenu.Scope);
        Assert.Equal(viaPlay.Sent with { RunId = "" }, viaMenu.Sent with { RunId = "" }); // runId dışında AYNI komut
    }

    /// <summary>
    /// [Clean] Menünün <b>Clean</b> maddesi kapsamlı bir <c>RunMode.Clean</c> koşusu gönderir ve konsol/stream
    /// hedefi adıyla anar. Pill sözcüğü <c>CLEAN</c>'dir — hedef adı pill'e YAZILMAZ (v1.13.2).
    /// </summary>
    [Fact]
    public async Task Clean_project_sends_a_scoped_clean_and_names_the_target()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        StartRunCommand? sent = null;
        vm.DebugOnCommandSent = c => { if (c is StartRunCommand s) sent = s; };

        await vm.CleanProjectCommand.ExecuteAsync(A);

        Assert.Equal((RunMode.Clean, A), (sent!.Mode, sent.ScopeProjectId));
        Assert.Equal(OperationLabel.Clean, vm.CurrentOperation);
        Assert.Contains("clean requested — a (single project)", vm.GetRunDocumentText(), StringComparison.Ordinal);

        // Gönderim motorsuz harness'ta senkron düşer ve kilit inerken hedef bırakılır; stream satırı bu yüzden
        // hedefi AÇIKÇA kurulmuş bir koşuda sınanır (kardeş test The_stream_opens_a_scoped_run_… ile aynı desen).
        vm.RunTargetId = A;
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Clean, 1, 1, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(A, "a", true)]));
        Assert.Contains(vm.StreamEvents, l => l.Text == "Clean started — a (single project)");
    }

    /// <summary>
    /// <b>Temizlenen proje "güncel" sayılmaz.</b> Sıradan bir koşuda başarı, satırın will-build noktasını
    /// söndürür ("succeeded-to-clean" canlı geçişi); bir Clean koşusunda başarı ise çıktının SİLİNDİĞİ anlamına
    /// gelir — proje tam tersine derlenmesi gereken hâle gelmiştir. Nokta amber KALIR ve motor da aynı anda
    /// defter kaydını siler, yani iki taraf aynı şeyi söyler.
    /// </summary>
    [Fact]
    public async Task A_cleaned_project_stays_dirty_while_an_ordinary_build_turns_it_clean()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);

        var cleaned = NewVm(engine);
        cleaned.OnEvent(new RunStartedEvent("r1", RunMode.Clean, 1, 1, "Debug", 0));
        cleaned.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(A, "a", true)]));
        cleaned.OnEvent(new ProjectStartedEvent("r1", A, "a"));
        cleaned.OnEvent(new ProjectSucceededEvent("r1", A, 120));

        Assert.Equal(ProjectRowState.Succeeded, Row(cleaned, A).State);
        Assert.True(Row(cleaned, A).WillBuild);   // temizlendi -> yine derlenecek

        var built = NewVm(engine);                // kontrol grubu: sıradan Build AYNI olay dizisiyle
        built.OnEvent(new RunStartedEvent("r2", RunMode.Build, 1, 1, "Debug", 0));
        built.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(A, "a", true)]));
        built.OnEvent(new ProjectStartedEvent("r2", A, "a"));
        built.OnEvent(new ProjectSucceededEvent("r2", A, 120));

        Assert.False(Row(built, A).WillBuild);
    }

    /// <summary>Hedef satır TIKLAMA ANINDA işaretlenir (gönderim penceresi dahil) ve koşu bittiğinde bırakılır;
    /// gönderim senkron düşerse hemen bırakılır — hiçbir yol satırı sonsuza dek "Stop" hâlinde bırakamaz.</summary>
    [Fact]
    public async Task The_target_row_is_flagged_from_the_click_until_the_run_ends()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        bool targetAtSend = false, otherAtSend = true;
        vm.DebugOnCommandSent = _ => { targetAtSend = Row(vm, A).IsRunTarget; otherAtSend = Row(vm, B).IsRunTarget; };

        await vm.BuildProjectCommand.ExecuteAsync(A); // motor yok → gönderim senkron düşer

        Assert.True(targetAtSend);
        Assert.False(otherAtSend);
        Assert.Null(vm.RunTargetId);                 // gönderim düştü → bırakıldı
        Assert.False(Row(vm, A).IsRunTarget);

        // Gerçek koşu: hedef koşu boyunca işaretli kalır, bitince bırakılır.
        vm.RunTargetId = A;
        vm.IsStarting = true;
        Assert.True(Row(vm, A).IsRunTarget);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        Assert.True(Row(vm, A).IsRunTarget);
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 10));
        Assert.Null(vm.RunTargetId);
        Assert.False(Row(vm, A).IsRunTarget);
    }

    /// <summary>Her satır kilidi bilir (<c>Build in progress — wait or stop it first</c> tooltip'inin
    /// kaynağı): planlama penceresi DAHİL — o pencerede de ikinci bir koşu başlatılamaz.</summary>
    [Fact]
    public async Task Rows_carry_the_mid_run_lock_including_the_planning_window()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        Assert.All(vm.Projects, r => Assert.False(r.IsRunLocked));

        vm.IsStarting = true;
        Assert.All(vm.Projects, r => Assert.True(r.IsRunLocked));
        Assert.False(vm.BuildProjectCommand.CanExecute(A));

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0)); // IsStarting düşer, IsRunning kalkar
        Assert.All(vm.Projects, r => Assert.True(r.IsRunLocked));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\late.csproj", "Late")); // koşu ortasında doğan satır
        Assert.True(Row(vm, @"C:\p\late.csproj").IsRunLocked);

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 10));
        Assert.All(vm.Projects, r => Assert.False(r.IsRunLocked));
        Assert.True(vm.BuildProjectCommand.CanExecute(A));
    }

    [Fact]
    public async Task The_row_commands_share_the_run_gate_and_need_a_project()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var bare = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        Assert.False(bare.BuildProjectCommand.CanExecute(A));   // topoloji yok → tam koşu gibi kapalı

        var vm = NewVm(engine);
        Assert.True(vm.BuildProjectCommand.CanExecute(A));
        Assert.True(vm.RebuildProjectCommand.CanExecute(A));
        Assert.False(vm.BuildProjectCommand.CanExecute(null));  // hedefsiz kapsam yok
    }

    /// <summary>Stream'in açılış satırı hedefi söyler — "Build started — 1 projects" bir tek-proje koşusu
    /// için hem gramer hem anlam olarak yanlıştı.</summary>
    [Fact]
    public async Task The_stream_opens_a_scoped_run_with_the_target_name()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = NewVm(engine);
        vm.RunTargetId = A;

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Rebuild, 1, 4, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(A, "a", true)]));

        Assert.Contains(vm.StreamEvents, l => l.Text == "Rebuild started — a (single project)");
    }
}
