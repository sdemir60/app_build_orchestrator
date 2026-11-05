using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Tests.Git;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [worktree] Kalıcı durum: worktree klasörü ve hangi kopyanın açık olduğu hatırlanır; repo kökü (<c>RepositoryRoot</c>) HİÇBİR
/// zaman worktree yoluna kaymaz — Settings onu gösterir ve worktree ondan eklenir. Açılışta worktree yalnız diskte hâlâ bu reponun
/// linked worktree'siyse açılır; değilse uygulama repo kopyasında açılır.
///
/// <para>Yol ÜRETİMDEKİ yoldur (<see cref="StartupPathTests"/> deseni): kalıcı duruma gerçek store ile yazılır, pencere gerçek
/// ctor'undan geçer, gösterilmez.</para>
/// </summary>
public class WorktreePersistenceTests
{
    private static (GitTestRepo Repo, string Worktree) RepoWithWorktree()
    {
        var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "one");
        repo.CommitAll("first");
        repo.CreateBranch("side");
        return (repo, repo.AddWorktree("side"));
    }

    [StaFact]
    public void A_remembered_active_worktree_reopens_in_the_worktree()
    {
        var (repo, worktree) = RepoWithWorktree();
        using var _ = repo;
        using var temp = new TempDir();
        var store = MainWindowHost.UiStateStore(temp);
        var saved = store.Load();
        saved.RepositoryRoot = repo.RootPath;
        saved.WorktreeFolder = worktree;
        saved.WorktreeActive = true;
        store.Save(saved);

        var (window, vm) = MainWindowHost.New(temp);

        Assert.Equal(worktree, vm.WorktreeFolder);
        Assert.Equal(WorkingCopy.Worktree, vm.ActiveCopy);
        Assert.Equal(worktree, vm.ActiveRoot);
        Assert.Equal(repo.RootPath, vm.RootPath);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_remembered_worktree_that_is_gone_reopens_in_the_repository()
    {
        using var repo = new GitTestRepo();
        using var parent = new TempDir();
        using var temp = new TempDir();
        var store = MainWindowHost.UiStateStore(temp);
        var saved = store.Load();
        saved.RepositoryRoot = repo.RootPath;
        saved.WorktreeFolder = System.IO.Path.Combine(parent.Path, "gone");
        saved.WorktreeActive = true;
        store.Save(saved);

        var (window, vm) = MainWindowHost.New(temp);

        Assert.Equal(WorkingCopy.Repository, vm.ActiveCopy);
        Assert.Equal(repo.RootPath, vm.ActiveRoot);
        GC.KeepAlive(window);
    }

    [StaFact]
    public async Task Switching_copies_is_remembered_but_the_repository_root_stays()
    {
        var (repo, worktree) = RepoWithWorktree();
        using var _ = repo;
        using var temp = new TempDir();
        var store = MainWindowHost.UiStateStore(temp);
        var saved = store.Load();
        saved.RepositoryRoot = repo.RootPath;
        saved.WorktreeFolder = worktree;
        store.Save(saved);
        var (window, vm) = MainWindowHost.New(temp);

        await vm.SwitchCopyAsync(WorkingCopy.Worktree);

        var after = store.Load();
        Assert.Equal(repo.RootPath, after.RepositoryRoot);
        Assert.Equal(worktree, after.WorktreeFolder);
        Assert.True(after.WorktreeActive);

        await vm.SwitchCopyAsync(WorkingCopy.Repository);

        Assert.False(store.Load().WorktreeActive);
        Assert.Equal(repo.RootPath, store.Load().RepositoryRoot);
        GC.KeepAlive(window);
    }
}
