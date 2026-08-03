using System.Diagnostics;
using System.IO;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E1/T67] <see cref="OsActions"/>: satır hover ikonlarının OS eylemleri. İki enjekte edilen seam vardır —
/// <see cref="IProcessLauncher"/> (fire-and-forget başlatma; testler GERÇEK process başlatmadan FileName +
/// Arguments'ı, özellikle <c>explorer.exe /select,"&lt;yol&gt;"</c> tırnak kaçışını doğrular) ve
/// <see cref="IProcessRunner"/> (vswhere sorgusu; MsBuildResolver deseni). 0/1/N solution dalları saf mantıktır
/// ve gerçek bir VS kurulumuna bağlı DEĞİLDİR; yalnız gerçek-devenv çözümü <see cref="SkippableFact"/>'tir.
/// Konsol notları (<c>{name}.csproj revealed in Explorer</c> / <c>{name} opened in Visual Studio</c>) satırı
/// çözen <see cref="RunViewModel"/>'in sorumluluğudur — birebir metin orada pinlenir.
/// </summary>
public class OsActionsTests
{
    // ------------------------------------------------------------ seam'ler (gerçek process/dialog YOK)
    private sealed class CaptureLauncher : IProcessLauncher
    {
        public ProcessStartInfo? Last;
        public int Count;
        public void Launch(ProcessStartInfo startInfo) { Last = startInfo; Count++; }
    }

    private sealed class StubRunner(ProcessResult result) : IProcessRunner
    {
        public ProcessSpec? LastSpec;
        public int Calls;
        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
        { LastSpec = spec; Calls++; return Task.FromResult(result); }
    }

    private static ProcessResult Ok(string stdout = "") => new(0, stdout, "", TimeSpan.Zero, false);

    // ============================================================ RevealInExplorer — argüman kaçışı
    [Fact]
    public void Reveal_in_explorer_emits_the_select_switch_with_the_quoted_full_path()
    {
        var launcher = new CaptureLauncher();
        var os = new OsActions(launcher, new StubRunner(Ok()));

        // Boşluk içeren yol: tırnak OLMADAN explorer yolu iki argümana bölerdi (güvenlik-ilgili).
        os.RevealInExplorer(@"C:\Program Files\Repo\My Proj\App.csproj");

        Assert.Equal(1, launcher.Count);
        Assert.Equal("explorer.exe", launcher.Last!.FileName);
        Assert.Equal("/select,\"C:\\Program Files\\Repo\\My Proj\\App.csproj\"", launcher.Last.Arguments);
    }

    // ============================================================ OpenInVisualStudio — 0/1/N dalları
    [Fact]
    public async Task Open_in_visual_studio_with_no_candidates_returns_none_and_launches_nothing()
    {
        var launcher = new CaptureLauncher();
        var runner = new StubRunner(Ok());
        var os = new OsActions(launcher, runner);

        var result = await os.OpenInVisualStudioAsync([]);

        Assert.Equal(OpenInVsOutcome.NoSolution, result.Outcome);
        Assert.Equal(0, launcher.Count);
        Assert.Equal(0, runner.Calls); // aday yok → vswhere'e hiç dokunulmaz
    }

    [Fact]
    public async Task Open_in_visual_studio_with_one_candidate_launches_devenv_on_that_solution()
    {
        // File.Exists geçen sahte devenv + sahte vswhere (gerçek VS'e bağımlılık YOK).
        string fakeDevenv = Environment.ProcessPath!;
        string fakeVswhere = Environment.ProcessPath!;
        var launcher = new CaptureLauncher();
        var runner = new StubRunner(Ok(fakeDevenv + "\r\n"));
        var os = new OsActions(launcher, runner, vswherePath: fakeVswhere);
        var sln = new SolutionRef("Osys", @"C:\src\My Sln\Osys.sln");

        var result = await os.OpenInVisualStudioAsync([sln]);

        Assert.Equal(OpenInVsOutcome.Opened, result.Outcome);
        Assert.Equal(fakeDevenv, launcher.Last!.FileName);
        Assert.Equal("\"C:\\src\\My Sln\\Osys.sln\"", launcher.Last.Arguments); // devenv.exe "<sln yolu>" (tırnaklı)
        // vswhere sorgusu MsBuildResolver desenini izler (-latest -property productPath).
        Assert.Equal(fakeVswhere, runner.LastSpec!.FileName);
        Assert.Contains("productPath", runner.LastSpec.Arguments);
    }

    [Fact]
    public async Task Open_in_visual_studio_with_multiple_candidates_returns_a_chooser_and_launches_nothing()
    {
        var launcher = new CaptureLauncher();
        var runner = new StubRunner(Ok());
        var os = new OsActions(launcher, runner);
        var a = new SolutionRef("A", @"C:\a\A.sln");
        var b = new SolutionRef("B", @"C:\b\B.sln");

        var result = await os.OpenInVisualStudioAsync([a, b]);

        Assert.Equal(OpenInVsOutcome.NeedsChoice, result.Outcome);
        Assert.Equal([a, b], result.Candidates); // seçtirilecek adaylar taşınır (T32)
        Assert.Equal(0, launcher.Count);         // kullanıcı seçene dek launch YOK
        Assert.Equal(0, runner.Calls);           // aday seçimi vswhere'e dokunmaz
    }

    // ============================================================ vswhere yok → net başarısızlık
    [Fact]
    public async Task Open_in_visual_studio_reports_not_found_when_vswhere_is_absent()
    {
        var launcher = new CaptureLauncher();
        var runner = new StubRunner(Ok());
        string missingVswhere = Path.Combine(Path.GetTempPath(), "e1-no-such-vswhere.exe");
        var os = new OsActions(launcher, runner, vswherePath: missingVswhere);

        var result = await os.OpenInVisualStudioAsync([new SolutionRef("A", @"C:\a\A.sln")]);

        Assert.Equal(OpenInVsOutcome.VisualStudioNotFound, result.Outcome);
        Assert.Equal(0, launcher.Count);
        Assert.Equal(0, runner.Calls); // vswhere.exe yok → sorgu hiç koşmaz
    }

    [SkippableFact] // gerçek VS/Build Tools kurulu makinede koşar (MsBuildResolverTests deseni)
    public async Task Real_machine_resolves_devenv_without_launching_visual_studio_for_real()
    {
        Skip.IfNot(File.Exists(MsBuildResolver.DefaultVswherePath), "vswhere yok");
        var launcher = new CaptureLauncher(); // sahte → gerçek VS ASLA açılmaz
        var os = new OsActions(launcher, new ProcessRunner());

        var result = await os.OpenInVisualStudioAsync([new SolutionRef("Osys", @"C:\src\Osys.sln")]);

        // Makinede devenv varsa Opened + devenv.exe; yalnız Build Tools varsa VisualStudioNotFound. İki durumda da
        // launcher sahte olduğundan gerçek bir VS penceresi açılmaz.
        if (result.Outcome == OpenInVsOutcome.Opened)
        {
            Assert.EndsWith("devenv.exe", launcher.Last!.FileName, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("\"C:\\src\\Osys.sln\"", launcher.Last.Arguments);
        }
        else
        {
            Assert.Equal(OpenInVsOutcome.VisualStudioNotFound, result.Outcome);
            Assert.Equal(0, launcher.Count);
        }
    }

    // ============================================================ RunViewModel: aday çözümü + birebir notlar
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private sealed class FakeOsActions : IOsActions
    {
        public string? RevealedPath;
        public IReadOnlyList<SolutionRef>? OpenedCandidates;
        public OpenInVsResult NextOpenResult = OpenInVsResult.NoSolution;
        public void RevealInExplorer(string path) => RevealedPath = path;
        public Task<OpenInVsResult> OpenInVisualStudioAsync(IReadOnlyList<SolutionRef> candidates)
        { OpenedCandidates = candidates; return Task.FromResult(NextOpenResult); }
        public string? PickFolder(string? initial) => null;
    }

    private static ProjectNode Node(string id, string name, params string[] solutionNames) =>
        new(id, name, id, SolutionNames: solutionNames, Dependencies: [], BuildOrder: 0,
            LayerIndex: null, LayerName: null, InCycle: false, WillBuild: null);

    [Fact]
    public async Task Solution_candidates_are_the_solutions_whose_names_match_the_projects_full_solution_list()
    {
        // [T32] Kart yalnız İLK sln adını (SolutionName) saklar; Open-in-VS ise projenin TÜM SolutionNames'ini
        // topolojiden çözüp eşleşen Solutions girdilerini döndürür.
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        var solutions = new[]
        {
            new SolutionRef("Osys.sln", @"C:\src\Osys.sln"),
            new SolutionRef("Sales.sln", @"C:\src\Sales.sln"),
            new SolutionRef("Other.sln", @"C:\src\Other.sln"),
        };
        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", "Osys.sln", "Sales.sln")], [], solutions, []));

        var candidates = vm.SolutionCandidatesFor(@"C:\p\a.csproj");

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, s => s.Name == "Osys.sln" && s.Path == @"C:\src\Osys.sln");
        Assert.Contains(candidates, s => s.Name == "Sales.sln");
        Assert.DoesNotContain(candidates, s => s.Name == "Other.sln");
    }

    [Fact]
    public async Task Solution_candidates_are_empty_for_an_unknown_project()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        Assert.Empty(vm.SolutionCandidatesFor(@"C:\nope\x.csproj"));
    }

    [Fact]
    public async Task Revealing_a_project_calls_os_actions_and_writes_the_verbatim_console_note()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var os = new FakeOsActions();
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1", osActions: os);
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "Foo"));

        vm.RevealProjectInExplorer(@"C:\p\a.csproj");

        Assert.Equal(@"C:\p\a.csproj", os.RevealedPath); // satırın Id'si (csproj yolu) reveal edilir
        Assert.Contains("Foo.csproj revealed in Explorer", vm.GetRunDocumentText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Opening_a_project_with_one_solution_writes_the_verbatim_opened_note_and_needs_no_chooser()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var sln = new SolutionRef("Osys.sln", @"C:\src\Osys.sln");
        var os = new FakeOsActions { NextOpenResult = OpenInVsResult.Opened(sln) };
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1", osActions: os);
        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "Foo", "Osys.sln")], [], [sln], []));

        var chooser = await vm.OpenProjectInVisualStudioAsync(@"C:\p\a.csproj");

        Assert.Null(chooser);                        // seçtirme gerekmez
        Assert.Single(os.OpenedCandidates!);         // tam 1 aday delege edildi
        Assert.Contains("Foo opened in Visual Studio", vm.GetRunDocumentText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Opening_a_project_with_multiple_solutions_returns_the_chooser_and_writes_no_opened_note_yet()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var a = new SolutionRef("A.sln", @"C:\src\A.sln");
        var b = new SolutionRef("B.sln", @"C:\src\B.sln");
        var os = new FakeOsActions { NextOpenResult = OpenInVsResult.Choose([a, b]) };
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1", osActions: os);
        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "Foo", "A.sln", "B.sln")], [], [a, b], []));

        var chooser = await vm.OpenProjectInVisualStudioAsync(@"C:\p\a.csproj");

        Assert.NotNull(chooser);
        Assert.Equal(2, chooser!.Count);
        Assert.Equal(2, os.OpenedCandidates!.Count); // VM, topolojiden çözdüğü İKİ adayı da delege etti (chooser'ı os üretti)
        Assert.DoesNotContain("opened in Visual Studio", vm.GetRunDocumentText()); // seçilene dek başarı notu YOK
    }

    [Fact]
    public async Task Picking_a_solution_from_the_chooser_opens_it_and_writes_the_verbatim_note()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var chosen = new SolutionRef("A.sln", @"C:\src\A.sln");
        var os = new FakeOsActions { NextOpenResult = OpenInVsResult.Opened(chosen) };
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1", osActions: os);
        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(@"C:\p\a.csproj", "Foo", "A.sln", "B.sln")], [],
            [chosen, new SolutionRef("B.sln", @"C:\src\B.sln")], []));

        await vm.OpenSolutionInVisualStudioAsync(@"C:\p\a.csproj", chosen);

        Assert.Equal(chosen, Assert.Single(os.OpenedCandidates!)); // tam o aday delege edildi
        Assert.Contains("Foo opened in Visual Studio", vm.GetRunDocumentText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Opening_a_project_with_no_solution_launches_nothing_and_writes_no_opened_note()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var os = new FakeOsActions { NextOpenResult = OpenInVsResult.NoSolution };
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1", osActions: os);
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "Foo"));

        var chooser = await vm.OpenProjectInVisualStudioAsync(@"C:\p\a.csproj");

        Assert.Null(chooser);
        Assert.DoesNotContain("opened in Visual Studio", vm.GetRunDocumentText()); // başarı notu YOK
    }
}
