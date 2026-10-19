using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [Exit] Exit'in durduracağı (ya da bekleyeceği) iş — <see cref="RunViewModel.ExitWork"/>. <see cref="ExitWork.None"/> ancak ve ancak
/// çıkışın uçuşta iş görmediği andır (<c>WorkspaceIdle</c>): kabuk o zaman kartsız kapanır, aksi hâlde kart bu işi anlatır. Koşu, kilit
/// sürdükçe kendi moduyla (Build/Rebuild/Resolve/Clean) okunur — komut henüz gitmeden, açılış koreografisinde de. İş değişince
/// <c>PropertyChanged(ExitWork)</c> duyurulur: açık kart "Nothing is running." hâline kendiliğinden geçer.
/// </summary>
public sealed class ExitWorkTests
{
    private const string Root = @"D:\repo";

    private static RunViewModel NewVm()
    {
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), MainWindowHost.NeverTickingBatcher(), () => "r1")
        {
            RootPath = Root,
        };
        MainWindowHost.AcceptSends(vm);
        return vm;
    }

    [Fact]
    public void Nothing_in_flight_is_no_work()
    {
        Assert.Equal(ExitWork.None, NewVm().ExitWork);
    }

    [Theory]
    [InlineData(RunMode.Build, ExitWork.Build)]
    [InlineData(RunMode.Rebuild, ExitWork.Rebuild)]
    [InlineData(RunMode.Cycles, ExitWork.Resolve)]
    [InlineData(RunMode.Clean, ExitWork.Clean)]
    public void A_run_is_read_with_its_own_mode(RunMode mode, ExitWork expected)
    {
        var vm = NewVm();
        vm.OnEvent(new RunStartedEvent("r1", mode, 2, 1, "Debug", 0));

        Assert.Equal(expected, vm.ExitWork);
    }

    /// <summary>Koşu, komut gitmeden (açılış koreografisi) de koşudur: kilit tıklamada kurulur ve çıkış onu geri alır.</summary>
    [Fact]
    public async Task A_run_still_in_its_opening_choreography_is_a_build()
    {
        var vm = NewVm();
        VmTopology.Seed(vm);
        var choreography = new TaskCompletionSource();
        vm.OperationChoreography = _ => choreography.Task;
        var run = vm.RebuildCommand.ExecuteAsync(null);

        Assert.Equal(ExitWork.Rebuild, vm.ExitWork);

        choreography.SetResult();
        await run;
    }

    [Fact]
    public async Task Workspace_jobs_are_read_by_their_own_flag()
    {
        var vm = NewVm();
        await vm.SyncCommand.ExecuteAsync(null);
        Assert.Equal(ExitWork.Sync, vm.ExitWork);
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));

        await vm.CleanCommand.ExecuteAsync(null);
        Assert.Equal(ExitWork.DeepClean, vm.ExitWork);
        vm.OnEvent(new CleanCompletedEvent(1, 2, 1024, 0, 1));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0)); // Clean'in zincirlediği Sync

        await vm.OptimizeCommand.ExecuteAsync(null);
        Assert.Equal(ExitWork.Optimize, vm.ExitWork);
    }

    [Fact]
    public async Task A_branch_switch_and_a_pull_are_git_writes()
    {
        var vm = NewVm();
        var featureX = new BranchRef("feature/x", "bbbbbbbbbbbb", IsActive: false, IsRemoteTracking: false);
        vm.OnEvent(new BranchListEvent([new BranchRef("main", "aaaaaaaaaaaa", IsActive: true, IsRemoteTracking: false), featureX]));
        await vm.SelectBranch(featureX);
        Assert.Equal(ExitWork.BranchSwitch, vm.ExitWork);
        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Switched, "main", "feature/x",
            "b7e91d4a0c1f2e3d4c5b6a7980716253443526a1", 0, null, null));
        vm.OnEvent(new SyncCompletedEvent("feature/x", "sha1234", false, 1, 0)); // checkout'un zincirlediği Sync

        vm.OnEvent(new SyncCompletedEvent("feature/x", "sha1234", false, 1, 0, Behind: 2));
        await vm.PullRepositoryCommand.ExecuteAsync(null);
        Assert.Equal(ExitWork.Pull, vm.ExitWork);
    }

    /// <summary>İş bitince duyurulur — açık kart kendini "Nothing is running." hâline günceller. Aynı değer yeniden duyurulmaz.</summary>
    [Fact]
    public void The_end_of_the_work_is_announced_once()
    {
        var vm = NewVm();
        MainWindowHost.StartBuild(vm);
        int announced = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RunViewModel.ExitWork)) announced++;
        };

        vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 0, 0, 0, 1, 400));

        Assert.Equal(ExitWork.None, vm.ExitWork);
        Assert.Equal(1, announced);
    }

    /// <summary>Kartın ve kapanış ekranının ilerlemesi koşu sürerken yazılır; komut gitmeden ya da koşu dışında yoktur — şeridin
    /// "0/0 uydurma olurdu" kuralı (<c>RibbonText</c>). Sayılar şeridin okuduğu iki sayaçtır.</summary>
    [Fact]
    public void There_is_no_progress_before_the_run_has_a_plan()
    {
        var vm = NewVm();
        Assert.Null(vm.ExitProgress);

        MainWindowHost.StartBuild(vm); // runStarted geldi, önizleme (will-build kümesi) henüz yok
        Assert.Null(vm.ExitProgress);
    }
}
