using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Processes;
using Xunit;

namespace BuildOrchestrator.Tests.Git;

/// <summary>
/// [T29 · T14 · K3] WorktreeManager: 3-durum matrisi (in-place / committed-worktree / zorunlu-farklı-branch-worktree),
/// K3 niyet satırı (seçim anında git-no-op), Build-anı gerçek <c>git worktree add --detach</c>, ve havuz
/// (T14 — boyut/LRU prune/silme). Gerçek ephemeral temp git repo'lar üzerinde (D8 — mock/sleep-poll yok).
/// </summary>
public class WorktreeManagerTests
{
    private static readonly ProcessRunner Runner = new();

    private static string NewPoolRoot() => Path.Combine(Path.GetTempPath(), "bo-pool-" + Guid.NewGuid().ToString("N"));

    // ---- 3-durum matrisi ----

    [Fact]
    public void PlanWorktree_active_branch_with_toggle_off_is_in_place_and_creates_nothing()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        string sha = repo.CommitAll("c1");
        string branch = repo.CurrentBranchName();
        string poolRoot = NewPoolRoot();

        var mgr = new WorktreeManager(Runner, repo.RootPath, poolRoot);
        var plan = mgr.PlanWorktree(activeBranch: branch, selectedBranch: branch, useWorktreeToggle: false, selectedSha: sha);

        Assert.Equal(WorktreeMode.InPlace, plan.Mode);
        Assert.Null(plan.Path);
        Assert.Equal(string.Empty, plan.IntentLine);
        Assert.False(Directory.Exists(poolRoot)); // planlama hiçbir şey oluşturmaz
    }

    [Fact]
    public void PlanWorktree_active_branch_with_toggle_on_is_worktree_mode_and_creates_nothing()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        string sha = repo.CommitAll("c1");
        string branch = repo.CurrentBranchName();
        string poolRoot = NewPoolRoot();

        var mgr = new WorktreeManager(Runner, repo.RootPath, poolRoot);
        var plan = mgr.PlanWorktree(activeBranch: branch, selectedBranch: branch, useWorktreeToggle: true, selectedSha: sha);

        Assert.Equal(WorktreeMode.Worktree, plan.Mode);
        Assert.NotNull(plan.Path);
        // aynı branch -> branch DEĞİŞMEDİ, "Branch changed" satırı OLMAMALI, tek satır
        Assert.Equal($"branch target: {branch} ({sha}) — worktree will be used at Build", plan.IntentLine);
        Assert.False(Directory.Exists(plan.Path));
        Assert.False(Directory.Exists(poolRoot));
    }

    [Fact]
    public void PlanWorktree_different_branch_forces_worktree_mode_even_when_toggle_off_and_creates_nothing()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");
        string activeBranch = repo.CurrentBranchName();

        repo.CreateBranch("feature-x");
        repo.Checkout("feature-x");
        repo.WriteFile("a.txt", "v2-feature");
        string featureSha = repo.CommitAll("c2");
        repo.Checkout(activeBranch); // ana repo yeniden aktif branch'te (K1 — gerçek kullanıcı durumu)

        string poolRoot = NewPoolRoot();
        var mgr = new WorktreeManager(Runner, repo.RootPath, poolRoot);

        var plan = mgr.PlanWorktree(activeBranch: activeBranch, selectedBranch: "feature-x", useWorktreeToggle: false, selectedSha: featureSha);

        Assert.Equal(WorktreeMode.Worktree, plan.Mode); // farklı branch -> ZORUNLU worktree, toggle OFF olsa bile
        Assert.NotNull(plan.Path);
        Assert.False(Directory.Exists(plan.Path));
        Assert.False(Directory.Exists(poolRoot));
        Assert.Equal(activeBranch, repo.CurrentBranchName()); // planlama ana repoyu HİÇ etkilemedi
    }

    // ---- K3 niyet satırı: birebir string ----

    [Fact]
    public void PlanWorktree_K3_intent_line_matches_exact_format_for_different_branch_selection()
    {
        // Saf karar mantığı — gerçek repo/sha gerekmez (PlanWorktree git komutu ÇALIŞTIRMAZ).
        var mgr = new WorktreeManager(Runner, repoRoot: Path.GetTempPath(), poolRoot: NewPoolRoot());
        const string sha = "abc1234abc1234abc1234abc1234abc1234abcd";

        var plan = mgr.PlanWorktree(activeBranch: "main", selectedBranch: "release/2026.06", useWorktreeToggle: false, selectedSha: sha);

        string expected =
            $"branch target: release/2026.06 ({sha}) — worktree will be used at Build" + "\n" +
            "Branch changed: release/2026.06 — Sync required";
        Assert.Equal(expected, plan.IntentLine);
    }

    [Fact]
    public void PlanWorktree_never_invokes_any_git_command()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        string sha = repo.CommitAll("c1");
        string branch = repo.CurrentBranchName();

        var recorder = new RecordingProcessRunner();
        var mgr = new WorktreeManager(recorder, repo.RootPath, NewPoolRoot());

        mgr.PlanWorktree(branch, branch, useWorktreeToggle: true, selectedSha: sha);
        mgr.PlanWorktree(branch, "some-other-branch", useWorktreeToggle: false, selectedSha: sha);

        Assert.Empty(recorder.Calls); // seçim anında git-no-op (K3)
    }

    // ---- Build anı: gerçek worktree add, no switch/checkout, aktif branch değişmez ----

    [Fact]
    public async Task PrepareWorktreeAsync_creates_real_worktree_only_at_build_time_with_no_switch_or_checkout_and_leaves_active_branch_unchanged()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");
        string activeBranch = repo.CurrentBranchName();

        repo.CreateBranch("feature-x");
        repo.Checkout("feature-x");
        repo.WriteFile("a.txt", "v2-feature");
        string featureSha = repo.CommitAll("c2");
        repo.Checkout(activeBranch);

        string poolRoot = NewPoolRoot();
        var recorder = new RecordingProcessRunner();
        var mgr = new WorktreeManager(recorder, repo.RootPath, poolRoot);

        var plan = mgr.PlanWorktree(activeBranch, "feature-x", useWorktreeToggle: false, selectedSha: featureSha);
        Assert.Equal(WorktreeMode.Worktree, plan.Mode);
        // [Review fix — Task 9] plan.Sha, PlanWorktree'ye verilen selectedSha ile AYNI kaynaktan gelir ve
        // K3 IntentLine'da GÖSTERİLEN sha budur — PrepareWorktreeAsync'in build ettiği sha bununla GARANTİ
        // olarak eşleşir (ayrı bir sha parametresi YOK, sapma yapısal olarak imkansız).
        Assert.Equal(featureSha, plan.Sha);
        Assert.Contains(featureSha, plan.IntentLine);
        Assert.Empty(recorder.Calls); // plan aşamasında hâlâ hiçbir git çağrısı yok

        var result = await mgr.PrepareWorktreeAsync(plan);

        Assert.True(result.Success);
        Assert.Equal(plan.Path, result.Value);
        Assert.True(Directory.Exists(plan.Path));
        Assert.Equal("v2-feature", File.ReadAllText(Path.Combine(plan.Path!, "a.txt"))); // hedef sha'daki dosyalar mevcut

        // git worktree list Windows'ta path'i '/' ile normalize eder (backslash değil) — karşılaştırma öncesi normalize edilir.
        string listOutput = GitTestRepo.RunGitAt(repo.RootPath, "worktree", "list");
        Assert.Contains(plan.Path!.Replace('\\', '/'), listOutput.Replace('\\', '/')); // git worktree list ARTIK gösteriyor

        var addCalls = recorder.Calls.Where(c => c.Count > 1 && c[0] == "worktree" && c[1] == "add").ToList();
        var singleAdd = Assert.Single(addCalls);
        // [Review fix — Task 9] build edilen sha == plan.Sha (== K3'te gösterilen sha) — birebir aynı değer.
        Assert.Equal(["worktree", "add", "--detach", plan.Path!, plan.Sha], singleAdd);

        foreach (var call in recorder.Calls)
        {
            Assert.DoesNotContain("switch", call);
            Assert.DoesNotContain("checkout", call);
        }

        Assert.Equal(activeBranch, repo.CurrentBranchName()); // aktif branch ASLA checkout edilmedi (K1)
    }

    [Fact]
    public async Task PrepareWorktreeAsync_returns_defined_error_for_in_place_plan_without_throwing()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        string sha = repo.CommitAll("c1");
        string branch = repo.CurrentBranchName();

        var mgr = new WorktreeManager(Runner, repo.RootPath, NewPoolRoot());
        var plan = mgr.PlanWorktree(branch, branch, useWorktreeToggle: false, selectedSha: sha);

        var result = await mgr.PrepareWorktreeAsync(plan);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    // ---- Havuz (T14): boyut, LRU prune, aktif asla prune edilmez, isimle silme ----

    [Fact]
    public async Task Pool_lists_sizes_prunes_least_recently_used_but_never_the_active_worktree_and_deletes_by_name()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");
        string activeBranch = repo.CurrentBranchName();
        string activeSha = GitTestRepo.RunGitAt(repo.RootPath, "rev-parse", activeBranch).Trim();

        (string Branch, string Sha) CreateBranchWithCommit(string name, string payload)
        {
            repo.CreateBranch(name);
            repo.Checkout(name);
            repo.WriteFile("payload.bin", payload);
            string sha = repo.CommitAll("commit on " + name);
            repo.Checkout(activeBranch);
            return (name, sha);
        }

        var f1 = CreateBranchWithCommit("feature-1", new string('a', 2000));
        var f2 = CreateBranchWithCommit("feature-2", new string('b', 2000));
        var f3 = CreateBranchWithCommit("feature-3", new string('c', 2000));

        string poolRoot = NewPoolRoot();
        var mgr = new WorktreeManager(Runner, repo.RootPath, poolRoot);

        // "aktif branch, worktree toggle AÇIK" senaryosu -> bu worktree'nin branch'i AKTİF branch'e eşit olacak
        var planActive = mgr.PlanWorktree(activeBranch, activeBranch, useWorktreeToggle: true, selectedSha: activeSha);
        var planF1 = mgr.PlanWorktree(activeBranch, f1.Branch, useWorktreeToggle: false, selectedSha: f1.Sha);
        var planF2 = mgr.PlanWorktree(activeBranch, f2.Branch, useWorktreeToggle: false, selectedSha: f2.Sha);
        var planF3 = mgr.PlanWorktree(activeBranch, f3.Branch, useWorktreeToggle: false, selectedSha: f3.Sha);

        // Gerçek oluşturma sırası (sequential, "şimdi" mtime'ları): active, f1, f2, f3 — yani f1 GERÇEKTE en
        // eski, f3 GERÇEKTE en yeni oluşturulan worktree.
        Assert.True((await mgr.PrepareWorktreeAsync(planActive)).Success);
        Assert.True((await mgr.PrepareWorktreeAsync(planF1)).Success);
        Assert.True((await mgr.PrepareWorktreeAsync(planF2)).Success);
        Assert.True((await mgr.PrepareWorktreeAsync(planF3)).Success);

        // [Review fix — Task 9] D8: sleep-poll YOK — LRU sırasını DETERMİNİSTİK kılmak için HER worktree'nin
        // TÜM dosyalarının (recursive, bkz. DirectoryTimestampHelper) mtime'ı backdate edilir.
        // WorktreeManager.ComputeDirStats, LastUsedUtc'yi worktree içindeki TÜM dosyaların (recursive) MAX
        // LastWriteTimeUtc'si olarak hesaplar — YALNIZ sidecar dosyasını (.bo-worktree-branch.txt) backdate
        // etmek YETERSİZDİR: git worktree add'in yazdığı diğer tüm dosyalar hâlâ "şimdi" zamanına sahip olur
        // ve max() bunları ezer (önceki sürümdeki bug tam olarak buydu — sidecar-only backdate hiçbir etkiye
        // sahip DEĞİLDİ, test yalnız sequential oluşturma sırasının tesadüfen intended sırayla eşleşmesi
        // sayesinde geçiyordu).
        //
        // Bu testin gerçekten mtime'lara (backdate'e) bağımlı olduğunu, tesadüfi sıralamaya DEĞİL, ispatlamak
        // için intended LRU sırası BİLEREK gerçek oluşturma sırasının TERSİNE atanır: f1 gerçekte İLK
        // oluşturuldu ama burada intended-EN YENİ; f3 gerçekte SON oluşturuldu ama burada intended-EN ESKİ
        // (non-active). Backdating'in gerçek bir etkisi OLMASAYDI, ComputeDirStats "şimdi"ye (gerçek
        // oluşturma sırasına) düşer ve LRU f1'i en eski sanırdı — aşağıdaki prune assert'i (f3 silinmeli, f1
        // DEĞİL) o durumda BAŞARISIZ olurdu.
        var baseTime = DateTime.UtcNow.AddMinutes(-10);
        DirectoryTimestampHelper.SetAllTimestampsUtc(planActive.Path!, baseTime); // active: en eski ama IsActive -> asla silinmez
        DirectoryTimestampHelper.SetAllTimestampsUtc(planF3.Path!, baseTime.AddMinutes(1)); // intended EN ESKİ non-active -> ilk silinecek aday (gerçekte SON oluşturulan)
        DirectoryTimestampHelper.SetAllTimestampsUtc(planF2.Path!, baseTime.AddMinutes(2));
        DirectoryTimestampHelper.SetAllTimestampsUtc(planF1.Path!, baseTime.AddMinutes(3)); // intended EN YENİ non-active -> son silinecek aday (gerçekte İLK oluşturulan)

        var listResult = await mgr.ListWorktreesAsync();
        Assert.True(listResult.Success);
        Assert.Equal(4, listResult.Value!.Count);
        Assert.All(listResult.Value!, w => Assert.True(w.SizeBytes > 0));

        var activeInfo = Assert.Single(listResult.Value!, w => w.IsActive);
        Assert.Equal(Path.GetFileName(planActive.Path!), activeInfo.Name);
        Assert.Equal(activeBranch, activeInfo.Branch);

        // LastUsedUtc gerçekten intended (gerçek oluşturma sırasının TERSİ) sırayı mı yansıtıyor — yani
        // recursive-max backdate GERÇEKTEN etkili mi (yoksa "şimdi"ye mi düşüyor)?
        var f1Info = listResult.Value!.Single(w => w.Name == Path.GetFileName(planF1.Path!));
        var f3Info = listResult.Value!.Single(w => w.Name == Path.GetFileName(planF3.Path!));
        Assert.True(f3Info.LastUsedUtc < f1Info.LastUsedUtc); // intended: f3 EN ESKİ, f1 EN YENİ

        string f3Name = Path.GetFileName(planF3.Path!);
        long f3Size = listResult.Value!.Single(w => w.Name == f3Name).SizeBytes;
        long totalSize = listResult.Value!.Sum(w => w.SizeBytes);

        // cap: yalnız f3'ün silinmesiyle tam olarak cap altına inecek şekilde seçildi (determinist tek-adım prune).
        long cap = totalSize - f3Size;

        var pruneResult = await mgr.PruneToCapAsync(cap);
        Assert.True(pruneResult.Success);
        Assert.Equal([f3Name], pruneResult.Value); // intended EN ESKİ non-active (f3) silindi — gerçek oluşturma sırası (f1 ilk) DEĞİL
        Assert.DoesNotContain(Path.GetFileName(planActive.Path!), pruneResult.Value!); // aktif ASLA silinmedi

        var afterList = await mgr.ListWorktreesAsync();
        Assert.True(afterList.Success);
        Assert.DoesNotContain(afterList.Value!, w => w.Name == f3Name);
        Assert.Contains(afterList.Value!, w => w.IsActive); // aktif hâlâ orada

        // git worktree list Windows'ta '/' kullanır — normalize edilmeden karşılaştırmak sessizce hep-doğru (vacuous) olurdu.
        string listOutputAfterPrune = GitTestRepo.RunGitAt(repo.RootPath, "worktree", "list");
        Assert.DoesNotContain(planF3.Path!.Replace('\\', '/'), listOutputAfterPrune.Replace('\\', '/'));

        // isimle silme (T14 — per-worktree Sil)
        string f2Name = Path.GetFileName(planF2.Path!);
        var deleteResult = await mgr.DeleteAsync(f2Name);
        Assert.True(deleteResult.Success);

        string listOutputAfterDelete = GitTestRepo.RunGitAt(repo.RootPath, "worktree", "list");
        Assert.DoesNotContain(planF2.Path!.Replace('\\', '/'), listOutputAfterDelete.Replace('\\', '/'));

        var finalList = await mgr.ListWorktreesAsync();
        Assert.DoesNotContain(finalList.Value!, w => w.Name == f2Name);
        Assert.Contains(finalList.Value!, w => w.Name == Path.GetFileName(planF1.Path!)); // dokunulmayan (intended en yeni) hâlâ orada
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        private readonly ProcessRunner _inner = new();

        public List<IReadOnlyList<string>> Calls { get; } = [];

        public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
        {
            Calls.Add(spec.Arguments);
            return await _inner.RunAsync(spec, ct);
        }
    }
}
