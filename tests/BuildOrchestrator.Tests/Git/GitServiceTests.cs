using System;
using System.Linq;
using System.Threading.Tasks;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Processes;
using Xunit;

namespace BuildOrchestrator.Tests.Git;

/// <summary>
/// [T11] GitService: gerçek ephemeral temp git repo'lar üzerinde (D8 — mock yok, sleep-poll yok)
/// HEAD commit/branch/dirty-paths/branch-list sorguları + patolojik repo edge'leri (no-commits,
/// detached HEAD, shallow clone, non-repo dir, git.exe yok).
/// </summary>
public class GitServiceTests
{
    private static readonly ProcessRunner Runner = new();

    // ---- normal repo ----

    [Fact]
    public async Task GetHeadCommitAsync_returns_40_hex_sha_on_normal_repo()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "hello");
        string expectedSha = repo.CommitAll("c1");

        var svc = new GitService(Runner, repo.RootPath);
        var result = await svc.GetHeadCommitAsync();

        Assert.True(result.Success);
        Assert.Equal(expectedSha, result.Value);
        Assert.Equal(40, result.Value!.Length);
        Assert.True(result.Value.All(Uri.IsHexDigit));
    }

    [Fact]
    public async Task GetCurrentBranchAsync_returns_branch_name_on_normal_repo()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "hello");
        repo.CommitAll("c1");
        string expectedBranch = repo.CurrentBranchName();

        var svc = new GitService(Runner, repo.RootPath);
        var result = await svc.GetCurrentBranchAsync();

        Assert.True(result.Success);
        Assert.Equal(expectedBranch, result.Value);
    }

    // ---- no-commits (fresh init, no commit yet) ----

    [Fact]
    public async Task GetHeadCommitAsync_returns_null_on_repo_with_no_commits()
    {
        using var repo = new GitTestRepo(); // init only — hiç commit yok

        var svc = new GitService(Runner, repo.RootPath);
        var result = await svc.GetHeadCommitAsync();

        Assert.True(result.Success); // no-commits bir HATA değil, tanımlı bir edge
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task GetRepoStateAsync_no_commits_repo_sets_HasNoCommits_and_TreatAsDirty_without_throwing()
    {
        using var repo = new GitTestRepo();

        var svc = new GitService(Runner, repo.RootPath);
        var state = await svc.GetRepoStateAsync();

        Assert.True(state.HasNoCommits);
        Assert.Null(state.HeadCommit);
        Assert.True(state.TreatAsDirty);
        Assert.False(state.HasError);
        Assert.Contains(state.Warnings, w => w.Contains("commit", StringComparison.OrdinalIgnoreCase));
    }

    // ---- detached HEAD ----

    [Fact]
    public async Task GetCurrentBranchAsync_returns_null_on_detached_HEAD()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        string firstSha = repo.CommitAll("c1");
        repo.WriteFile("a.txt", "v2");
        repo.CommitAll("c2");
        repo.Checkout(firstSha); // detached HEAD

        var svc = new GitService(Runner, repo.RootPath);
        var result = await svc.GetCurrentBranchAsync();

        Assert.True(result.Success); // detached bir HATA değil, tanımlı bir edge
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task GetRepoStateAsync_detached_HEAD_sets_IsDetached_and_warns()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        string firstSha = repo.CommitAll("c1");
        repo.WriteFile("a.txt", "v2");
        repo.CommitAll("c2");
        repo.Checkout(firstSha);

        var svc = new GitService(Runner, repo.RootPath);
        var state = await svc.GetRepoStateAsync();

        Assert.True(state.IsDetached);
        Assert.Null(state.Branch);
        Assert.Equal(firstSha, state.HeadCommit); // HEAD commit detached'de de çözülebilir olmalı
        Assert.Contains(state.Warnings, w => w.Contains("detached", StringComparison.OrdinalIgnoreCase));
    }

    // ---- dirty working tree ----

    [Fact]
    public async Task GetDirtyPathsAsync_reports_modified_tracked_file()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");
        repo.WriteFile("a.txt", "v2 — modified");

        var svc = new GitService(Runner, repo.RootPath);
        var result = await svc.GetDirtyPathsAsync();

        Assert.True(result.Success);
        Assert.Contains(result.Value!, p => p.Contains("a.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetDirtyPathsAsync_returns_empty_on_clean_repo()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");

        var svc = new GitService(Runner, repo.RootPath);
        var result = await svc.GetDirtyPathsAsync();

        Assert.True(result.Success);
        Assert.Empty(result.Value!);
    }

    // ---- shallow repo ----

    [Fact]
    public async Task IsShallowRepositoryAsync_true_on_depth_1_clone()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");
        repo.WriteFile("a.txt", "v2");
        repo.CommitAll("c2");
        string shallowRoot = repo.CloneShallow();

        var svc = new GitService(Runner, shallowRoot);
        var result = await svc.IsShallowRepositoryAsync();

        Assert.True(result.Success);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task GetRepoStateAsync_shallow_repo_sets_IsShallow_and_TreatAsDirty_and_warns()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");
        string shallowRoot = repo.CloneShallow();

        var svc = new GitService(Runner, shallowRoot);
        var state = await svc.GetRepoStateAsync();

        Assert.True(state.IsShallow);
        Assert.True(state.TreatAsDirty);
        Assert.Contains(state.Warnings, w => w.Contains("shallow", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IsShallowRepositoryAsync_false_on_normal_repo()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");

        var svc = new GitService(Runner, repo.RootPath);
        var result = await svc.IsShallowRepositoryAsync();

        Assert.True(result.Success);
        Assert.False(result.Value);
    }

    // ---- ListBranches ----

    [Fact]
    public async Task ListBranchesAsync_flags_the_checked_out_local_branch_as_active()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");
        string current = repo.CurrentBranchName();
        repo.CreateBranch("feature-x"); // oluşturulur ama checkout edilmez — current hâlâ ilk branch

        var svc = new GitService(Runner, repo.RootPath);
        var result = await svc.ListBranchesAsync();

        Assert.True(result.Success);
        var locals = result.Value!.Where(b => !b.IsRemote).ToList();
        Assert.Contains(locals, b => b.Name == current && b.IsActive);
        Assert.Contains(locals, b => b.Name == "feature-x" && !b.IsActive);
    }

    [Fact]
    public async Task ListBranchesAsync_reports_remote_tracking_branches_from_a_clone()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");
        repo.CreateBranch("feature-x");
        string cloneRoot = repo.CloneFull();

        var svc = new GitService(Runner, cloneRoot);
        var result = await svc.ListBranchesAsync();

        Assert.True(result.Success);
        var remotes = result.Value!.Where(b => b.IsRemote).ToList();
        Assert.NotEmpty(remotes);
        Assert.All(remotes, b => Assert.False(b.IsActive)); // remote-tracking asla "active" değil
        Assert.Contains(remotes, b => b.Name.EndsWith("feature-x", StringComparison.Ordinal));

        var locals = result.Value!.Where(b => !b.IsRemote).ToList();
        Assert.Single(locals); // klonda yalnız default branch yerel olarak checkout edilir
        Assert.True(locals[0].IsActive);
    }

    // ---- tracked blob hashes [A6 refinement — Task 7b: per-project committed fingerprint kaynağı] ----

    [Fact]
    public async Task GetTrackedBlobHashesAsync_returns_path_to_blob_sha_map_for_committed_files()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "hello");
        repo.WriteFile("sub/b.txt", "world");
        repo.CommitAll("c1");

        var svc = new GitService(Runner, repo.RootPath);
        var result = await svc.GetTrackedBlobHashesAsync();

        Assert.True(result.Success);
        Assert.True(result.Value!.ContainsKey("a.txt"));
        Assert.True(result.Value!.ContainsKey("sub/b.txt"));
        Assert.Equal(40, result.Value!["a.txt"].Length);
        Assert.True(result.Value!["a.txt"].All(Uri.IsHexDigit));
        Assert.Equal(40, result.Value!["sub/b.txt"].Length);
        Assert.True(result.Value!["sub/b.txt"].All(Uri.IsHexDigit));
    }

    [Fact]
    public async Task GetTrackedBlobHashesAsync_modifying_and_committing_a_file_changes_only_its_own_blob_sha()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.WriteFile("b.txt", "unchanged");
        repo.CommitAll("c1");

        var svc = new GitService(Runner, repo.RootPath);
        var before = await svc.GetTrackedBlobHashesAsync();

        repo.WriteFile("a.txt", "v2 — modified");
        repo.CommitAll("c2");

        var after = await svc.GetTrackedBlobHashesAsync();

        Assert.True(before.Success);
        Assert.True(after.Success);
        Assert.NotEqual(before.Value!["a.txt"], after.Value!["a.txt"]);
        Assert.Equal(before.Value!["b.txt"], after.Value!["b.txt"]); // dokunulmayan dosya aynı blob SHA'sını korur
    }

    [Fact]
    public async Task GetTrackedBlobHashesAsync_returns_empty_map_on_repo_with_no_commits()
    {
        using var repo = new GitTestRepo(); // init only — hiç commit yok

        var svc = new GitService(Runner, repo.RootPath);
        var result = await svc.GetTrackedBlobHashesAsync();

        Assert.True(result.Success); // no-commits bir HATA değil, tanımlı bir edge
        Assert.Empty(result.Value!);
    }

    // ---- error signals: no unhandled exception ----

    [Fact]
    public async Task GetHeadCommitAsync_on_non_repo_dir_returns_defined_error_without_throwing()
    {
        string plainDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gitsvc-nonrepo-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(plainDir);
        try
        {
            var svc = new GitService(Runner, plainDir);
            var result = await svc.GetHeadCommitAsync();

            Assert.False(result.Success);
            Assert.NotNull(result.Error);
        }
        finally { System.IO.Directory.Delete(plainDir, recursive: true); }
    }

    [Fact]
    public async Task GetRepoStateAsync_on_non_repo_dir_treats_as_dirty_with_error_flagged()
    {
        string plainDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gitsvc-nonrepo-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(plainDir);
        try
        {
            var svc = new GitService(Runner, plainDir);
            var state = await svc.GetRepoStateAsync();

            Assert.True(state.HasError);
            Assert.True(state.TreatAsDirty);
            Assert.NotNull(state.Error);
        }
        finally { System.IO.Directory.Delete(plainDir, recursive: true); }
    }

    // ---- corrupted repo: a real (non-"not a git repository") 128-class git error must surface as
    // Fail, NOT be silently swallowed as "no commits"/"detached" (review fix — Task 4) ----

    [Fact]
    public async Task GetHeadCommitAsync_on_corrupted_repo_returns_Fail_not_silent_no_commits()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");
        repo.CorruptGitConfig(); // exit=128, stderr "fatal: bad config line ..." — "not a git repository" İÇERMEZ

        var svc = new GitService(Runner, repo.RootPath);
        var result = await svc.GetHeadCommitAsync();

        Assert.False(result.Success); // gerçek hata — no-commits (Ok(null)) OLMAMALI
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task GetCurrentBranchAsync_on_corrupted_repo_returns_Fail_not_silent_detached()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");
        repo.CorruptGitConfig();

        var svc = new GitService(Runner, repo.RootPath);
        var result = await svc.GetCurrentBranchAsync();

        Assert.False(result.Success); // gerçek hata — detached (Ok(null)) OLMAMALI
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task GetHeadCommitAsync_with_missing_git_executable_returns_defined_error_without_throwing()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");

        var svc = new GitService(Runner, repo.RootPath, gitExecutable: "git-does-not-exist-xyz-12345");
        var result = await svc.GetHeadCommitAsync();

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }
}
