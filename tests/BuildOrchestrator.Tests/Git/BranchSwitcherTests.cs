using System.IO;
using System.Threading.Tasks;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Tests.Externals;

namespace BuildOrchestrator.Tests.Git;

/// <summary>
/// [Faz 2/Task 2] <see cref="BranchSwitcher"/> — kod tabanındaki checkout/stash yüzeyi. <c>RepositoryWriter.cs</c>
/// içinde <see cref="FastForwardUpdater"/> ile aynı dosyayı paylaşır (§6.6 — tek mutasyon dosyası).
///
/// <para>Testler gerçek <c>git.exe</c> ile koşar (D8) — yalnız "git'e hiç dokunulmadı" iddiası
/// <see cref="FakeProcessRunner"/> ile sınanır (gerçek repoda "hiç komut çalışmadı" gözlemlenemez).</para>
/// </summary>
public class BranchSwitcherTests
{
    private static BranchSwitcher Switcher(string root) => new(new ProcessRunner(), root);

    private static string HeadOf(string root) => GitTestRepo.RunGitAt(root, "rev-parse", "HEAD").Trim();

    private static string BranchOf(string root) => GitTestRepo.RunGitAt(root, "symbolic-ref", "--short", "HEAD").Trim();

    [Fact]
    public async Task A_clean_tree_switches_to_a_local_branch()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "one");
        repo.CommitAll("first");
        repo.CreateBranch("feature");

        var result = await Switcher(repo.RootPath).SwitchAsync("feature", isRemote: false, stashIfDirty: false);

        Assert.Equal(CheckoutStatus.Switched, result.Status);
        Assert.Equal("feature", result.Branch);
        Assert.Equal("feature", BranchOf(repo.RootPath));
        Assert.Equal(HeadOf(repo.RootPath), result.Revision);
        Assert.Equal(0, result.DirtyCount);
        Assert.Null(result.StashMessage);
    }

    [Fact]
    public async Task A_dirty_tree_with_stop_is_refused_and_nothing_changes()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "one");
        repo.CommitAll("first");
        repo.CreateBranch("feature");
        string branchBefore = BranchOf(repo.RootPath);
        string headBefore = HeadOf(repo.RootPath);
        File.WriteAllText(Path.Combine(repo.RootPath, "a.txt"), "dirty edit");

        var result = await Switcher(repo.RootPath).SwitchAsync("feature", isRemote: false, stashIfDirty: false);

        Assert.Equal(CheckoutStatus.Dirty, result.Status);
        Assert.Equal(1, result.DirtyCount);
        Assert.Equal(branchBefore, BranchOf(repo.RootPath));
        Assert.Equal(headBefore, HeadOf(repo.RootPath));
        Assert.Equal("dirty edit", File.ReadAllText(Path.Combine(repo.RootPath, "a.txt")));
        Assert.Equal("", GitTestRepo.RunGitAt(repo.RootPath, "stash", "list").Trim());
    }

    [Fact]
    public async Task A_dirty_tree_with_stash_stashes_untracked_too_and_switches()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "one");
        repo.CommitAll("first");
        repo.CreateBranch("feature");
        string fromBranch = BranchOf(repo.RootPath);
        File.WriteAllText(Path.Combine(repo.RootPath, "a.txt"), "dirty edit");
        File.WriteAllText(Path.Combine(repo.RootPath, "scratch.cs"), "// notes");

        var result = await Switcher(repo.RootPath).SwitchAsync("feature", isRemote: false, stashIfDirty: true);

        Assert.Equal(CheckoutStatus.Switched, result.Status);
        Assert.Equal("feature", BranchOf(repo.RootPath));
        Assert.Equal("one", File.ReadAllText(Path.Combine(repo.RootPath, "a.txt")));
        Assert.False(File.Exists(Path.Combine(repo.RootPath, "scratch.cs")));
        Assert.Equal(BranchSwitcher.StashMessage(fromBranch, "feature"), result.StashMessage);
        Assert.Contains(result.StashMessage!, GitTestRepo.RunGitAt(repo.RootPath, "stash", "list"));
    }

    [Fact]
    public async Task A_remote_branch_without_a_local_one_is_checked_out_as_tracking()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.txt", "one");
        upstream.CommitAll("first");
        upstream.CreateBranch("feature");
        string clone = upstream.CloneFull();

        var result = await Switcher(clone).SwitchAsync("origin/feature", isRemote: true, stashIfDirty: false);

        Assert.Equal(CheckoutStatus.Switched, result.Status);
        Assert.Equal("feature", result.Branch);
        Assert.Equal("feature", BranchOf(clone));
    }

    [Fact]
    public async Task A_remote_branch_with_a_local_one_uses_the_local()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.txt", "one");
        upstream.CommitAll("first");
        upstream.CreateBranch("feature");
        string clone = upstream.CloneFull();
        // Yerel bir izleme branch'i önceden var ama checkout edilmemiş — "--track" DENENSEYDİ git burada
        // "a branch named 'feature' already exists" ile başarısız olurdu; bu yüzden test dolaylı olarak
        // uygulamanın gerçekten YEREL checkout'u tercih ettiğini kanıtlar.
        GitTestRepo.RunGitAt(clone, "branch", "feature", "origin/feature");

        var result = await Switcher(clone).SwitchAsync("origin/feature", isRemote: true, stashIfDirty: false);

        Assert.Equal(CheckoutStatus.Switched, result.Status);
        Assert.Equal("feature", result.Branch);
        Assert.Equal("feature", BranchOf(clone));
    }

    [Fact]
    public async Task The_active_branch_is_already_on_and_runs_no_git_write()
    {
        var runner = new FakeProcessRunner(FakeProcessRunner.Output("feature\n"));
        var switcher = new BranchSwitcher(runner, @"C:\fake-root");

        var result = await switcher.SwitchAsync("feature", isRemote: false, stashIfDirty: false);

        Assert.Equal(CheckoutStatus.AlreadyOn, result.Status);
        Assert.Equal("feature", result.Branch);
        Assert.DoesNotContain(runner.Calls, c => c.Arguments.Contains("checkout"));
        Assert.DoesNotContain(runner.Calls, c => c.Arguments.Contains("stash"));
    }

    [Fact]
    public async Task A_failed_checkout_after_a_stash_reports_the_stash()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "one");
        repo.CommitAll("first");
        string fromBranch = BranchOf(repo.RootPath);
        File.WriteAllText(Path.Combine(repo.RootPath, "a.txt"), "dirty edit");

        var result = await Switcher(repo.RootPath).SwitchAsync("does-not-exist", isRemote: false, stashIfDirty: true);

        Assert.Equal(CheckoutStatus.Failed, result.Status);
        Assert.Equal(BranchSwitcher.StashMessage(fromBranch, "does-not-exist"), result.StashMessage);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
        // Checkout başarısız olduğu için stash GERİ UYGULANMAZ — kullanıcının işi kaybolmaz, araç geri almaz.
        Assert.Contains(result.StashMessage!, GitTestRepo.RunGitAt(repo.RootPath, "stash", "list"));
    }
}
