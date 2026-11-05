using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Git;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [worktree] Repo ↔ Worktree geçişi bir KÖK DEĞİŞİMİDİR: Settings'teki repo kökü (<see cref="RunViewModel.RootPath"/>) yerinde kalır,
/// motora giden her komut ve git okuması aktif kökü (<see cref="RunViewModel.ActiveRoot"/>) taşır. Geçiş eski kopyanın kararlarını ve
/// son Sync'ini unutur, yeni bir konsol bölümü açar ve yeni kökte Sync'ler.
///
/// <para>Harness <see cref="BranchCheckoutTests"/>'inkidir (başlatılmamış motor: gönderim gönderim anında gözlenir, sonra senkron
/// düşer). Worktree gerçek git ile kurulur — geçişin kapısı klasörün bu reponun linked worktree'si olmasıdır.</para>
/// </summary>
public class WorktreeSwitchTests
{
    private static (GitTestRepo Repo, string Worktree) RepoWithWorktree()
    {
        var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "one");
        repo.CommitAll("first");
        repo.CreateBranch("side");
        return (repo, repo.AddWorktree("side"));
    }

    private static RunViewModel NewVm(string root, string worktree = "") =>
        new(new EngineHost(TestPaths.SupervisorExe), MainWindowHost.NeverTickingBatcher(), () => "r1")
        {
            RootPath = root,
            WorktreeFolder = worktree,
        };

    private static string[] Lines(RunViewModel vm) =>
        vm.GetRunDocumentText().Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public async Task Switching_to_the_worktree_syncs_the_worktree_and_leaves_the_repository_root_alone()
    {
        var (repo, worktree) = RepoWithWorktree();
        using var _ = repo;
        var vm = NewVm(repo.RootPath, worktree);
        vm.OnEvent(new SyncProgressEvent("previous operation line", "info"));
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.SwitchCopyAsync(WorkingCopy.Worktree);

        Assert.Equal(WorkingCopy.Worktree, vm.ActiveCopy);
        Assert.Equal(worktree, vm.ActiveRoot);
        Assert.Equal(repo.RootPath, vm.RootPath); // Settings'in kökü ve kalıcı durumun RepositoryRoot'u
        Assert.Equal(worktree, Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
        Assert.Equal(worktree, Assert.Single(sent.OfType<ListBranchesCommand>()).RootPath);
        // Yeni bölüm: önceki işlemin satırı gider, bölümün ilk satırı geçişi söyler.
        var lines = Lines(vm);
        Assert.DoesNotContain("previous operation line", lines);
        Assert.Equal(WorkingCopyText.Switched(WorkingCopy.Worktree, worktree), lines[0]);
    }

    [Fact]
    public async Task Switching_back_to_the_repository_syncs_the_repository_root()
    {
        var (repo, worktree) = RepoWithWorktree();
        using var _ = repo;
        var vm = NewVm(repo.RootPath, worktree);
        await vm.SwitchCopyAsync(WorkingCopy.Worktree);
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.SwitchCopyAsync(WorkingCopy.Repository);

        Assert.Equal(WorkingCopy.Repository, vm.ActiveCopy);
        Assert.Equal(repo.RootPath, vm.ActiveRoot);
        Assert.Equal(repo.RootPath, Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
        Assert.Equal(WorkingCopyText.Switched(WorkingCopy.Repository, repo.RootPath), Lines(vm)[0]);
    }

    /// <summary>Geçişten sonra kullanıcının her işi aktif kökte koşar — Sync düğmesi, Clean, Optimize, pull.</summary>
    [Fact]
    public async Task After_the_switch_every_workspace_command_carries_the_worktree_root()
    {
        var (repo, worktree) = RepoWithWorktree();
        using var _ = repo;
        var vm = NewVm(repo.RootPath, worktree);
        await vm.SwitchCopyAsync(WorkingCopy.Worktree);
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.SyncCommand.ExecuteAsync(null);
        await vm.CleanCommand.ExecuteAsync(null);
        await vm.OptimizeCommand.ExecuteAsync(null);

        Assert.Equal(worktree, Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
        Assert.Equal(worktree, Assert.Single(sent.OfType<CleanWorkspaceCommand>()).RootPath);
        Assert.Equal(worktree, Assert.Single(sent.OfType<OptimizeWorkspaceCommand>()).RootPath);
    }

    /// <summary>Geçişin kararları unutması: eski kopyanın satırları ve topolojisi gider (proje kimlikleri yola bağlıdır — worktree'nin
    /// projeleri başka satırlardır); son Sync'in HEAD'i de unutulur, yoksa ilk HEAD tetiği sahte bir "Switched to" bölümü açardı.</summary>
    [Fact]
    public async Task The_switch_drops_the_previous_copy_s_plan_and_last_sync()
    {
        var (repo, worktree) = RepoWithWorktree();
        using var _ = repo;
        var vm = NewVm(repo.RootPath, worktree);
        VmTopology.Seed(vm, System.IO.Path.Combine(repo.RootPath, "a.csproj"));
        vm.OnEvent(new SyncStartedEvent(repo.RootPath, "main"));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));
        Assert.NotEmpty(vm.Projects);
        Assert.NotNull(vm.LastSyncHead);

        await vm.SwitchCopyAsync(WorkingCopy.Worktree);

        Assert.Empty(vm.Projects);
        Assert.Null(vm.LastSyncHead);
    }

    [Fact]
    public async Task An_unconfigured_worktree_asks_for_setup_and_sends_nothing()
    {
        using var repo = new GitTestRepo();
        var vm = NewVm(repo.RootPath);
        int setupRequests = 0;
        vm.WorktreeSetupRequested += (_, _) => setupRequests++;
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.SwitchCopyAsync(WorkingCopy.Worktree);

        Assert.Equal(1, setupRequests);
        Assert.Equal(WorkingCopy.Repository, vm.ActiveCopy);
        Assert.Empty(sent);
    }

    /// <summary>Ayardaki klasör diskte yoksa (silindi) ya da bu reponun worktree'si değilse de kurulum istenir — geçilecek bir şey yok.</summary>
    [Fact]
    public async Task A_worktree_folder_that_is_gone_asks_for_setup()
    {
        using var repo = new GitTestRepo();
        using var parent = new TempDir();
        var vm = NewVm(repo.RootPath, System.IO.Path.Combine(parent.Path, "gone"));
        int setupRequests = 0;
        vm.WorktreeSetupRequested += (_, _) => setupRequests++;
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.SwitchCopyAsync(WorkingCopy.Worktree);

        Assert.Equal(1, setupRequests);
        Assert.Equal(WorkingCopy.Repository, vm.ActiveCopy);
        Assert.Empty(sent);
    }

    /// <summary>Kapı Sync düğmesininkidir: koşu ya da bir workspace işi uçuştayken geçiş hiçbir şey yapmaz — derlenen ya da okunan
    /// kökü altından değiştirmek olurdu.</summary>
    [Fact]
    public async Task Switching_does_nothing_while_the_workspace_is_busy()
    {
        var (repo, worktree) = RepoWithWorktree();
        using var _ = repo;
        var vm = NewVm(repo.RootPath, worktree);
        vm.OnEvent(new SyncStartedEvent(repo.RootPath, "main"));
        Assert.False(vm.CanSwitchCopy);
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.SwitchCopyAsync(WorkingCopy.Worktree);

        Assert.Equal(WorkingCopy.Repository, vm.ActiveCopy);
        Assert.Empty(sent);
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));
        Assert.True(vm.CanSwitchCopy);
    }

    [Fact]
    public async Task Switching_to_the_active_copy_does_nothing()
    {
        var (repo, worktree) = RepoWithWorktree();
        using var _ = repo;
        var vm = NewVm(repo.RootPath, worktree);
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.SwitchCopyAsync(WorkingCopy.Repository);

        Assert.Empty(sent);
    }

    /// <summary>Settings'te repo kökü değişince worktree eski reponundur: kopya Repo'ya döner ve Save'in Sync'i yeni kökü taşır.</summary>
    [Fact]
    public async Task A_new_repository_root_returns_to_the_repository_copy()
    {
        var (repo, worktree) = RepoWithWorktree();
        using var _ = repo;
        using var other = new GitTestRepo();
        var vm = NewVm(repo.RootPath, worktree);
        await vm.SwitchCopyAsync(WorkingCopy.Worktree);
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.ApplySettingsAsync([], other.RootPath, []);

        Assert.Equal(WorkingCopy.Repository, vm.ActiveCopy);
        Assert.Equal(other.RootPath, vm.ActiveRoot);
        Assert.Equal(other.RootPath, Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
    }

    [Fact]
    public async Task Closing_the_workspace_returns_to_the_repository_copy()
    {
        var (repo, worktree) = RepoWithWorktree();
        using var _ = repo;
        var vm = NewVm(repo.RootPath, worktree);
        await vm.SwitchCopyAsync(WorkingCopy.Worktree);

        await vm.ApplySettingsAsync([], "", []);

        Assert.Equal(WorkingCopy.Repository, vm.ActiveCopy);
        Assert.Equal("", vm.ActiveRoot);
    }

    /// <summary>Branch chip'i aktif kopyada checkout eder.</summary>
    [Fact]
    public async Task A_branch_picked_in_the_worktree_is_checked_out_there()
    {
        var (repo, worktree) = RepoWithWorktree();
        using var _ = repo;
        var vm = NewVm(repo.RootPath, worktree);
        await vm.SwitchCopyAsync(WorkingCopy.Worktree);
        vm.OnEvent(new BranchListEvent([
            new BranchRef("side", "aaaaaaaaaaaa", IsActive: true, IsRemoteTracking: false),
            new BranchRef("feature/x", "bbbbbbbbbbbb", IsActive: false, IsRemoteTracking: false),
        ]));
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.SelectBranch(new BranchRef("feature/x", "bbbbbbbbbbbb", false, false));

        Assert.Equal(worktree, Assert.Single(sent.OfType<CheckoutBranchCommand>()).RootPath);
    }
}
