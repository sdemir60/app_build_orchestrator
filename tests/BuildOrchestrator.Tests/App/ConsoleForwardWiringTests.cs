using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [D4 review §3] Kart → proje-log ileri kablajının (OnSelectedProjectChangedAsync / AppendConsoleBatch routing)
/// otomatik kapsaması. <c>MainWindow</c> DI olmadan kurulamadığından (RestoreGlyphTests.cs:106) karar/guard mantığı
/// test edilebilir seam'lere çıkarıldı: pump flush yönlendirmesi <see cref="ConsoleBatchRouter"/>'da (SAF), seçim
/// kararı + guard dizisi <see cref="RunViewModel.NextConsoleSelection"/> / <see cref="RunViewModel.ShouldShowLoadedProject"/>'te.
/// Bu testler o seam'leri (§1 generation-guard stale-drop + §2 freeze-race guard'ı dahil) MainWindow olmadan sürer.
/// Not: seam'ler SAF olduğundan WPF/STA gerekmez ([Fact] yeterli — brief'in [StaFact] önerisi seam'siz varyant içindi).
/// </summary>
public class ConsoleForwardWiringTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(System.Threading.Timeout.Infinite));

    // ---------------------------------------------------------------- [§1] AppendConsoleBatch routing (SAF karar)

    [Theory]
    [InlineData(0, 0, null, ConsoleBatchRouter.Route.Narrative)]  // güncel nesil + anlatı modu
    [InlineData(0, 0, "A", ConsoleBatchRouter.Route.Raw)]         // güncel nesil + proje-log modu
    [InlineData(0, 1, null, ConsoleBatchRouter.Route.Drop)]       // aradan reseed geçti → bayat, at (mod önemsiz)
    [InlineData(0, 1, "A", ConsoleBatchRouter.Route.Drop)]        // aradan reseed geçti → bayat, at
    [InlineData(3, 3, null, ConsoleBatchRouter.Route.Narrative)]  // eşit nesil bayat DEĞİL
    public void Router_drops_stale_generations_and_otherwise_routes_by_active_project(
        long batchGen, long currentReseedGen, string? activeProjectId, ConsoleBatchRouter.Route expected)
        => Assert.Equal(expected, ConsoleBatchRouter.Decide(batchGen, currentReseedGen, activeProjectId));

    [Fact] // [§1] Solution B senkron doc-set'in ARDINDAN koşan bayat flush, generation guard'la ATILIR (dup penceresi kapanır)
    public async Task A_flush_read_before_a_reseed_is_dropped_after_the_synchronous_doc_set()
    {
        // Deterministik (gerçek thread YOK): gen N altında bir batch drenajla+damgala → PostReseedDrop (senkron
        // reseed, gen N+1) → bayat flush'ın (batchGen=N) yönlendirmesini sorgula. Aradan reseed geçtiğinden router
        // DÜŞÜRÜR — anlatı modu (ActiveProjectId null) olmasına RAĞMEN. Guard'sız (nesli yok sayan) kod bu satırı
        // Narrative'e yönlendirip TAZE dokümana EKLERDİ (T3b regresyonu / dup).
        long staleGen = -1;
        ConsoleBatcher? batcher = null;
        Task Tick(CancellationToken ct) { batcher!.Complete(); return Task.CompletedTask; }
        batcher = new ConsoleBatcher(Tick);
        batcher.Post("stale-line");
        await batcher.PumpAsync((_, gen) => staleGen = gen, CancellationToken.None); // batch gen N=0 damgalı yakalandı
        Assert.Equal(0, staleGen);

        batcher.PostReseedDrop(); // senkron reseed → gen N+1

        Assert.Equal(
            ConsoleBatchRouter.Route.Drop,
            ConsoleBatchRouter.Decide(staleGen, batcher.CurrentReseedGen, activeProjectId: null));
    }

    // ---------------------------------------------------------------- [§3] seçim kararı + [§2] freeze-race guard

    [Fact]
    public async Task NextConsoleSelection_returns_ShowRun_when_nothing_is_selected()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");

        Assert.Equal(ConsoleSelection.ShowRun, vm.NextConsoleSelection(out var id));
        Assert.Null(id);
    }

    [Fact]
    public async Task NextConsoleSelection_returns_LoadProjectLog_with_the_selected_id()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string a = @"C:\p\a.csproj";
        vm.SelectProject(a);

        Assert.Equal(ConsoleSelection.LoadProjectLog, vm.NextConsoleSelection(out var id));
        Assert.Equal(a, id);
    }

    [Fact] // guard1 (log vardı, mod kuruldu) + guard2 (seçim hâlâ o projede) — ikisi de sağlanınca proje gösterilir
    public async Task ShouldShowLoadedProject_is_true_when_load_set_project_mode_and_selection_still_matches()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string a = @"C:\p\a.csproj";
        vm.SelectProject(a);
        var load = vm.LoadProjectLogAsync(a);
        vm.OnEvent(new ProjectLogChunkEvent(a, 0, "disk\n", IsLast: true, ThroughLineNumber: 0));
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(a, vm.ActiveProjectId);
        Assert.True(vm.ShouldShowLoadedProject(a));
    }

    [Fact] // [§2] guard2: yükleme bitince seçim BAŞKA yere kaydıysa (deselect) proje GÖSTERİLMEZ → run modunda kal
    public async Task ShouldShowLoadedProject_is_false_when_selection_changed_after_the_load()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string a = @"C:\p\a.csproj";
        vm.SelectProject(a);
        var load = vm.LoadProjectLogAsync(a);
        vm.OnEvent(new ProjectLogChunkEvent(a, 0, "disk\n", IsLast: true, ThroughLineNumber: 0));
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        vm.SelectProject(null); // yükleme bittikten SONRA seçim kalktı

        Assert.False(vm.ShouldShowLoadedProject(a));
    }

    [Fact] // guard1: yükleme log BULAMAZSA (logNotFound) mod kurulmaz → ShouldShowLoadedProject false (run modunda kal)
    public async Task ShouldShowLoadedProject_is_false_when_the_load_found_no_log()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string a = @"C:\p\skipped.csproj";
        vm.OnEvent(new ProjectSkippedEvent("r1", a, "cycle"));
        vm.SelectProject(a);
        var load = vm.LoadProjectLogAsync(a);
        vm.OnEvent(new ErrorEvent("logNotFound", a));
        await load.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(vm.ActiveProjectId);              // mod kurulmadı
        Assert.Equal(a, vm.SelectedProjectId);        // ama seçim hâlâ a (guard2 geçer, guard1 keser)
        Assert.False(vm.ShouldShowLoadedProject(a));
    }
}
