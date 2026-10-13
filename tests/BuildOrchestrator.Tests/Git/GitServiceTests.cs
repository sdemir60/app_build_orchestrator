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

    /// <summary>[tag çakışması] Checkout edilmiş branch'le AYNI adı taşıyan bir tag varken de ad düz branch adıdır.
    /// <para><b>Ölçülen kusur:</b> ad <c>git symbolic-ref --short</c>'tan okunuyordu; git bu durumda belirsizliği önlemek için
    /// <c>heads/feature/x</c> döner. O ad ne bir branch'tir ne de diskten okunan HEAD'le aynıdır: pencereye her dönüş sahte bir
    /// "branch değişti" bölümü açıyordu, branch chip'i <c>heads/…</c> gösteriyordu, pull
    /// <c>refs/remotes/origin/heads/…</c>'yi arıyordu.</para></summary>
    [Fact]
    public async Task GetCurrentBranchAsync_returns_the_plain_branch_name_when_a_tag_has_the_same_name()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "hello");
        repo.CommitAll("c1");
        repo.CheckoutBranchNamedLikeATag("feature/x");

        var result = await new GitService(Runner, repo.RootPath).GetCurrentBranchAsync();

        Assert.True(result.Success);
        Assert.Equal("feature/x", result.Value);
    }

    /// <summary>[reftable · imza] HEAD reflog'unun kayıtları, git'in log çıktılarına imza doğrulamasını açan bir ayar
    /// (<c>log.showSignature=true</c>) ve imzalı commit'lerle de yalnız kayıtlardır. Ölçülen kusur: git her imzalı kayıt için
    /// çıktıya fazladan bir doğrulama satırı basıyordu; reftable izleyicisi hareketleri yanlış sayıyor ve sınıflıyordu
    /// (build sürerken atılan tek commit build'i kesebiliyordu).</summary>
    [Fact]
    public async Task GetHeadReflogAsync_is_not_mixed_with_signature_lines()
    {
        using var repo = GitTestRepo.Reftable();
        repo.SignCommitsAndShowSignatures();
        repo.WriteFile("a.txt", "1");
        repo.CommitAll("c1");
        repo.WriteFile("a.txt", "2");
        repo.CommitAll("c2");

        var result = await new GitService(Runner, repo.RootPath).GetHeadReflogAsync(HeadWatcher.ReflogWindow);

        Assert.True(result.Success, result.Error);
        Assert.Equal(["commit: c2", "commit (initial): c1"], result.Value!.Select(r => r.Message));
    }

    /// <summary>[reftable] Henüz commit'i olmayan repoda reflog yoktur: git hata verse de sonuç boş listedir — izleyici bunu
    /// "hareket yok" okur. Kusurlu hâlde hata olarak dönüyordu ve ilk commit'ten önceki her tablo değişikliği (ör. bir
    /// branch adı değişimi) koşuyu kesebilecek bir "Other" hareketi üretiyordu.</summary>
    [Fact]
    public async Task GetHeadReflogAsync_is_empty_before_the_first_commit()
    {
        using var repo = GitTestRepo.Reftable();

        var result = await new GitService(Runner, repo.RootPath).GetHeadReflogAsync(HeadWatcher.ReflogWindow);

        Assert.True(result.Success, result.Error);
        Assert.Empty(result.Value!);
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

    /// <summary>
    /// [final review M5] Türkçe karakterli yollar DEŞİFRE edilir. git varsayılan <c>core.quotepath=true</c> ile
    /// ASCII dışı baytları tırnaklı sekizlik kaçışlarla yazar (<c>"src/\304\260l.cs"</c>); o metin diskteki
    /// hiçbir yolla eşleşmez ve dosyası değişen proje <c>local</c> işaretini hiç almazdı. Ayar repoda AÇIKÇA
    /// true yapılır — makinenin global git ayarı testi sessizce yeşile boyayamasın. Değiştirilmiş (takipli),
    /// takipsiz ve yeniden adlandırılmış (yeni ad) yolun üçü de düz metin döner.
    /// </summary>
    [Fact]
    public async Task GetDirtyPathsAsync_decodes_non_ascii_paths()
    {
        using var repo = new GitTestRepo();
        GitTestRepo.RunGitAt(repo.RootPath, "config", "core.quotepath", "true");
        repo.WriteFile("src/Işık.cs", "v1");
        repo.WriteFile("src/Eski.cs", "v1");
        repo.CommitAll("c1");
        repo.WriteFile("src/Işık.cs", "v2");                               // takipli, değişti
        repo.WriteFile("src/Çağrı Öğesi.cs", "yeni");                      // takipsiz, boşluklu
        GitTestRepo.RunGitAt(repo.RootPath, "mv", "src/Eski.cs", "src/Güncel.cs"); // staged rename

        var result = await new GitService(Runner, repo.RootPath).GetDirtyPathsAsync();

        Assert.True(result.Success);
        Assert.Equal(["src/Güncel.cs", "src/Işık.cs", "src/Çağrı Öğesi.cs"],
            result.Value!.Order(StringComparer.Ordinal));
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

    /// <summary>[tag çakışması] Aynı adlı bir tag varken de checkout edilmiş branch listede aktif işaretlenir — işaret
    /// <see cref="GitService.GetCurrentBranchAsync"/>'ın adıyla eşleştirilir. Kusurlu hâlde ad <c>heads/feature/x</c>'ti ve
    /// listedeki hiçbir adla eşleşmiyordu: branch listesinde aktif branch YOKTU.</summary>
    [Fact]
    public async Task ListBranchesAsync_flags_the_checked_out_branch_as_active_when_a_tag_has_the_same_name()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");
        repo.CheckoutBranchNamedLikeATag("feature/x");

        var result = await new GitService(Runner, repo.RootPath).ListBranchesAsync();

        Assert.True(result.Success);
        Assert.Contains(result.Value!, b => b.Name == "feature/x" && !b.IsRemote && b.IsActive);
    }

    // [A5/T69] BranchRef (branchList IPC event'i) her branch için sha TAŞIR — UI kart/chip'lerinde commit
    // gösterimi (N10) buna dayanır. Sha, branch listesiyle AYNI `for-each-ref` çağrısından gelir: branch
    // başına ayrı bir `rev-parse` process'i spawn etmek çok branch'li repolarda gereksiz pahalıdır.
    [Fact]
    public async Task ListBranchesAsync_carries_the_commit_sha_of_every_branch()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        string firstSha = repo.CommitAll("c1");
        string current = repo.CurrentBranchName();
        repo.CreateBranch("feature-x"); // aynı commit'ten dallanır

        var svc = new GitService(Runner, repo.RootPath);
        var result = await svc.ListBranchesAsync();

        Assert.True(result.Success);
        Assert.Equal(firstSha, Assert.Single(result.Value!, b => b.Name == current).Sha);
        Assert.Equal(firstSha, Assert.Single(result.Value!, b => b.Name == "feature-x").Sha);
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

    /// <summary>
    /// [v1.16.0] Uzak uçtan mesafe: alt bardaki <c>N behind</c> chip'i ve Sync'in mesafe satırı bu tek
    /// sayıdan beslenir. Ölçüm YEREL bir sorgudur — uzak uç Sync'in ref-only fetch'iyle çözülmüştür.
    /// </summary>
    [Fact]
    public async Task CountBehindAsync_counts_the_commits_the_local_head_is_missing()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.txt", "v1");
        upstream.CommitAll("c1");
        string clone = upstream.CloneFull();
        upstream.WriteFile("a.txt", "v2");
        upstream.CommitAll("c2");
        upstream.WriteFile("a.txt", "v3");
        string target = upstream.CommitAll("c3");

        // Üretimde bu sırayı Sync kurar: önce ref-only fetch (uzak uç yerel nesne veritabanına iner), sonra
        // mesafe ölçümü. Fetch olmadan hedef commit yerelde YOKTUR.
        var svc = new GitService(Runner, clone);
        await svc.FetchRefOnlyAsync(upstream.CurrentBranchName());

        var behind = await svc.CountBehindAsync(target);

        Assert.True(behind.Success);
        Assert.Equal(2, behind.Value);
    }

    /// <summary>
    /// Yerelde OLMAYAN bir hedef (fetch yapılmamış) sayı üretmez — tanımlı bir hata döner. Çağıran bunu
    /// "mesafe bilinmiyor" diye okur: chip çizilmez, konsola uydurma bir sayı yazılmaz.
    /// </summary>
    [Fact]
    public async Task CountBehindAsync_reports_a_defined_error_for_a_target_the_clone_has_never_seen()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.txt", "v1");
        upstream.CommitAll("c1");
        string clone = upstream.CloneFull();
        upstream.WriteFile("a.txt", "v2");
        string unseen = upstream.CommitAll("c2");

        var behind = await new GitService(Runner, clone).CountBehindAsync(unseen);

        Assert.False(behind.Success);
        Assert.NotNull(behind.Error);
    }

    [Fact]
    public async Task CountBehindAsync_is_zero_when_the_local_head_already_has_everything()
    {
        using var upstream = new GitTestRepo();
        upstream.WriteFile("a.txt", "v1");
        string head = upstream.CommitAll("c1");
        string clone = upstream.CloneFull();

        var behind = await new GitService(Runner, clone).CountBehindAsync(head);

        Assert.True(behind.Success);
        Assert.Equal(0, behind.Value);
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
