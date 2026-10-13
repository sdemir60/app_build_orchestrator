using System.Threading.Tasks;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Tests.Externals;

namespace BuildOrchestrator.Tests.Git;

/// <summary>
/// [reftable] <see cref="HeadSource.ReadAsync"/>: kendiliğinden Sync'in HEAD okuması — normal repoda yalnız dosya (git
/// çalıştırılmaz), reftable repoda git'e sorarak. Gerçek <c>git.exe</c> ile.
/// </summary>
public class HeadSourceTests
{
    /// <summary>Normal (files) repoda HEAD dosyadan okunur ve hiçbir git process'i açılmaz — kendiliğinden Sync her tetikte
    /// buradan geçer, normal repodaki maliyeti bir dosya okuması olarak kalmalı.</summary>
    [Fact]
    public async Task A_files_repository_is_read_from_the_file_without_starting_git()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "hello");
        string sha = repo.CommitAll("c1");
        var runner = new FakeProcessRunner();

        var head = await HeadSource.ReadAsync(GitDirectory.Resolve(repo.RootPath)!, runner);

        Assert.Equal(new HeadState(repo.CurrentBranchName(), sha), head);
        Assert.Empty(runner.Calls);
    }

    /// <summary>reftable repoda HEAD dosyası yer tutucudur: branch ve commit git'e sorularak okunur — git dizininin içinden.</summary>
    [Fact]
    public async Task A_reftable_repository_is_read_through_git()
    {
        using var repo = GitTestRepo.Reftable();
        repo.WriteFile("a.txt", "hello");
        repo.CommitAll("c1");
        GitTestRepo.RunGitAt(repo.RootPath, "checkout", "-q", "-b", "feature/x");
        repo.WriteFile("a.txt", "changed on feature/x");
        string sha = repo.CommitAll("c2");
        string gitDir = GitDirectory.Resolve(repo.RootPath)!;
        Assert.Null(HeadReader.Read(gitDir)); // ön-koşul: dosyadan okunamıyor (yer tutucu)

        var head = await HeadSource.ReadAsync(gitDir, new ProcessRunner());

        Assert.Equal(new HeadState("feature/x", sha), head);
    }

    /// <summary>[reftable] Git'e soran yol process'leri çağıranın thread'inde BAŞLATMAZ: çağıran kendiliğinden Sync'in UI
    /// thread'idir ve bir process'in başlatılması bile onu bekletir. Kusurlu hâlde ilk git process'i çağıranın thread'inde
    /// başlıyordu (ölçülen: başlatma başına birkaç ms, okuma başına iki process).</summary>
    [Fact]
    public void A_reftable_read_starts_git_off_the_calling_thread()
    {
        using var repo = GitTestRepo.Reftable();
        repo.WriteFile("a.txt", "hello");
        repo.CommitAll("c1");
        var runner = new PoolRecordingRunner();
        Task<HeadState?>? read = null;

        var caller = new Thread(() => read = HeadSource.ReadAsync(GitDirectory.Resolve(repo.RootPath)!, runner)); // havuz dışı thread
        caller.Start();
        caller.Join();

        Assert.NotNull(read!.GetAwaiter().GetResult());
        Assert.NotEmpty(runner.StartedOnPool);
        Assert.All(runner.StartedOnPool, Assert.True);
    }

    /// <summary>Gerçek <see cref="ProcessRunner"/>'a devreder; her process'in hangi thread türünde başlatıldığını kaydeder.</summary>
    private sealed class PoolRecordingRunner : IProcessRunner
    {
        private readonly ProcessRunner _real = new();
        public List<bool> StartedOnPool { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
        {
            lock (StartedOnPool) StartedOnPool.Add(Thread.CurrentThread.IsThreadPoolThread);
            return _real.RunAsync(spec, ct);
        }
    }

    /// <summary>[reftable] Linked worktree'nin HEAD'i, worktree'nin kendi git dizininden sorulur (ana repoyunkinden değil).</summary>
    [Fact]
    public async Task A_reftable_worktree_is_read_from_its_own_git_directory()
    {
        using var repo = GitTestRepo.Reftable();
        repo.WriteFile("a.txt", "hello");
        string sha = repo.CommitAll("c1");
        repo.CreateBranch("wt");
        string worktree = repo.AddWorktree("wt");

        var head = await HeadSource.ReadAsync(GitDirectory.Resolve(worktree)!, new ProcessRunner());

        Assert.Equal(new HeadState("wt", sha), head);
    }

    /// <summary>reftable repoda detached HEAD: branch yok, commit var — dosyadan okunan detached HEAD'le aynı biçim.</summary>
    [Fact]
    public async Task A_detached_head_in_a_reftable_repository_has_no_branch()
    {
        using var repo = GitTestRepo.Reftable();
        repo.WriteFile("a.txt", "hello");
        string sha = repo.CommitAll("c1");
        repo.Checkout(sha);

        var head = await HeadSource.ReadAsync(GitDirectory.Resolve(repo.RootPath)!, new ProcessRunner());

        Assert.Equal(new HeadState(null, sha), head);
    }
}
