using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [Clean] Build menüsünün Clean'i (<see cref="RunViewModel.CleanAllCommand"/>) Build ve Rebuild ile AYNI kapıdan
/// geçer: topoloji yoksa, bir koşu uçuşta ya da planlanırken, workspace işi (Sync, Clean, Optimize, checkout,
/// pull) sürerken ve motor erişilemezken kapalıdır.
/// </summary>
public class CleanAllCommandTests
{
    /// <summary>
    /// Kapı Build'inkiyle her durumda AYNI cevabı verir — ve bildirimi de AYNI yerlerden gelir. CommunityToolkit'in
    /// <c>RelayCommand</c>'ı <c>CommandManager.RequerySuggested</c>'a abone OLMAZ: <c>CanExecuteChanged</c> yalnız
    /// elle (ya da <c>NotifyCanExecuteChangedFor</c> ile) ateşlenir. Değer doğru, bildirim eksik olsaydı madde
    /// gerçek pencerede bayat bir kapıyla kalırdı; sayaçların eşitliği iki komutun aynı bildirim noktalarından
    /// geçtiğini pinler.
    /// </summary>
    [Fact]
    public async Task Clean_all_opens_and_closes_exactly_when_build_does()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, MainWindowHost.NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };
        int buildChanged = 0, cleanChanged = 0;
        vm.BuildCommand.CanExecuteChanged += (_, _) => buildChanged++;
        vm.CleanAllCommand.CanExecuteChanged += (_, _) => cleanChanged++;
        var seen = new List<(string Step, bool Build, bool Clean)>();
        void Note(string step) => seen.Add((step, vm.BuildCommand.CanExecute(null), vm.CleanAllCommand.CanExecute(null)));

        Note("no topology");
        VmTopology.Seed(vm);
        Note("topology");
        vm.IsStarting = true;
        Note("planning");
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        Note("running");
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 10));
        Note("idle");
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Note("sync in flight");
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));
        Note("synced");
        vm.OnEngineUnavailable(@"C:\missing\BuildOrchestrator.Supervisor.exe");
        Note("engine unavailable");

        Assert.All(seen, s => Assert.True(s.Build == s.Clean, $"{s.Step}: build={s.Build} clean={s.Clean}"));
        Assert.Contains(seen, s => s.Clean);       // kapı gerçekten açıldı…
        Assert.Contains(seen, s => !s.Clean);      // …ve kapandı
        Assert.Equal(buildChanged, cleanChanged);  // bildirim de Build'inkiyle aynı noktalardan
    }
}
