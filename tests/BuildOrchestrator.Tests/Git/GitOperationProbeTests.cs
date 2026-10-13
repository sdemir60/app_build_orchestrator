using System.IO;
using BuildOrchestrator.Core.Git;
using Xunit;

namespace BuildOrchestrator.Tests.Git;

/// <summary>[Faz 2/T1] <see cref="GitOperationProbe.Inspect"/>: her işaret dosyası/klasörü, hiçbiri, öncelik.</summary>
public class GitOperationProbeTests
{
    [Fact]
    public void Inspect_returns_none_when_no_markers_present()
    {
        using var temp = new TempDir();

        Assert.Equal(GitOperation.None, GitOperationProbe.Inspect(temp.Path));
    }

    [Fact]
    public void Inspect_returns_command_running_on_index_lock()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "index.lock"), "");

        Assert.Equal(GitOperation.CommandRunning, GitOperationProbe.Inspect(temp.Path));
    }

    [Fact]
    public void Inspect_returns_merge_on_merge_head()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "MERGE_HEAD"), "deadbeef\n");

        Assert.Equal(GitOperation.Merge, GitOperationProbe.Inspect(temp.Path));
    }

    [Theory]
    [InlineData("rebase-merge")]
    [InlineData("rebase-apply")]
    public void Inspect_returns_rebase_on_rebase_directories(string dirName)
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, dirName));

        Assert.Equal(GitOperation.Rebase, GitOperationProbe.Inspect(temp.Path));
    }

    [Fact]
    public void Inspect_returns_cherry_pick_on_cherry_pick_head()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "CHERRY_PICK_HEAD"), "deadbeef\n");

        Assert.Equal(GitOperation.CherryPick, GitOperationProbe.Inspect(temp.Path));
    }

    [Fact]
    public void Inspect_returns_revert_on_revert_head()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "REVERT_HEAD"), "deadbeef\n");

        Assert.Equal(GitOperation.Revert, GitOperationProbe.Inspect(temp.Path));
    }

    [Fact]
    public void Inspect_prioritizes_merge_over_index_lock()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, "MERGE_HEAD"), "deadbeef\n");
        File.WriteAllText(Path.Combine(temp.Path, "index.lock"), "");

        Assert.Equal(GitOperation.Merge, GitOperationProbe.Inspect(temp.Path));
    }

    [Fact]
    public void Inspect_prioritizes_rebase_over_index_lock()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "rebase-merge"));
        File.WriteAllText(Path.Combine(temp.Path, "index.lock"), "");

        Assert.Equal(GitOperation.Rebase, GitOperationProbe.Inspect(temp.Path));
    }

    // ---------------------------------------------------------------- [reftable] CHERRY_PICK_HEAD/REVERT_HEAD dosya değil

    /// <summary>[reftable] Tek commit'li reftable repo: <c>a.txt</c> = base.</summary>
    private static GitTestRepo ReftableWithBase()
    {
        var repo = GitTestRepo.Reftable();
        repo.WriteFile("a.txt", "base");
        repo.CommitAll("base");
        return repo;
    }

    /// <summary>[reftable] Çakışmayla yarıda kalan cherry-pick reftable repoda da görülür. Orada <c>CHERRY_PICK_HEAD</c>
    /// git'in tablosundadır, diskte dosya olarak durmaz. Kusurlu (eksik destekli) hâlde işlem görülmüyordu: checkout ve pull
    /// kilitlenmiyor, kendiliğinden Sync beklemiyordu.</summary>
    [Fact]
    public void A_conflicted_cherry_pick_in_a_reftable_repository_is_seen()
    {
        using var repo = ReftableWithBase();
        repo.CreateBranch("side");
        repo.Checkout("side");
        repo.WriteFile("a.txt", "side");
        repo.CommitAll("side");
        repo.Checkout("-");
        repo.WriteFile("a.txt", "main");
        repo.CommitAll("main");
        Assert.NotEqual(0, GitTestRepo.RunGitAllowingFailure(repo.RootPath, "cherry-pick", "side")); // ön-koşul: çakıştı

        Assert.Equal(GitOperation.CherryPick, GitOperationProbe.Inspect(GitDirectory.Resolve(repo.RootPath)!));
    }

    /// <summary>[reftable] Çakışmayla yarıda kalan revert de görülür ve cherry-pick'ten ayrılır (<c>REVERT_HEAD</c> de
    /// tablodadır).</summary>
    [Fact]
    public void A_conflicted_revert_in_a_reftable_repository_is_seen()
    {
        using var repo = ReftableWithBase();
        repo.WriteFile("a.txt", "x");
        string reverted = repo.CommitAll("x");
        repo.WriteFile("a.txt", "y");
        repo.CommitAll("y");
        Assert.NotEqual(0, GitTestRepo.RunGitAllowingFailure(repo.RootPath, "revert", "--no-edit", reverted)); // ön-koşul: çakıştı

        Assert.Equal(GitOperation.Revert, GitOperationProbe.Inspect(GitDirectory.Resolve(repo.RootPath)!));
    }

    /// <summary>Normal (files) repoda yarıdaki cherry-pick'in ve revert'in işareti kendi dosyasıdır: tek başına bir
    /// <c>MERGE_MSG</c> işlem SAYILMAZ (ör. <c>cherry-pick -n</c> sonrası). reftable'ın kuralı buraya sızmaz.</summary>
    [Fact]
    public void In_a_files_repository_a_lone_merge_message_is_not_an_operation()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "base");
        repo.CommitAll("base");
        string gitDir = GitDirectory.Resolve(repo.RootPath)!;
        File.WriteAllText(Path.Combine(gitDir, "MERGE_MSG"), "picked without committing\n");

        Assert.Equal(GitOperation.None, GitOperationProbe.Inspect(gitDir));
    }
}
