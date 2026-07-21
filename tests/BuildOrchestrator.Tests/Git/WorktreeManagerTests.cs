using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Supervisor;
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

        // [Fix wave 1 — Finding 1] Tahliye muafiyeti ARTIK "branch'i aktif branch'e eşit" (IsActive) DEĞİL,
        // "O ANDA kullanılan worktree" (inUseName). Bu testin iddiası (aktif branch'in worktree'si, en eski
        // olmasına rağmen silinmemeli) DEĞİŞMEDİ — yalnız korumanın kaynağı, gerçekten koruma anlamı taşıyan
        // parametreye taşındı. Eski muafiyet, en yaygın yolda (aktif branch için worktree açmak) havuzun TAMAMINI
        // muaf kılıyor ve cap'i tümüyle işlevsiz bırakıyordu; bkz.
        // Pool_cap_evicts_the_least_recently_used_worktree_even_when_every_worktree_belongs_to_the_active_branch.
        var pruneResult = await mgr.PruneToCapAsync(cap, inUseName: Path.GetFileName(planActive.Path!));
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

    // ---- [A4] Build-anı workspace çözümü (Supervisor kompozisyon kökü: Program.PrepareAsync) ----

    [Fact]
    public async Task PrepareAsync_falls_back_to_in_place_and_reports_in_place_when_the_worktree_cannot_be_created()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        string sha = repo.CommitAll("c1");
        string branch = repo.CurrentBranchName();
        string poolRoot = NewPoolRoot();

        // `git worktree add`'i DETERMİNİSTİK olarak patlatmak için hedef path'e önceden bir DOSYA konur:
        // PlanWorktree adayı yalnız mevcut DİZİNLERE bakarak seçtiği (Directory.EnumerateDirectories) için
        // PrepareAsync'in kendi planı da AYNI adı seçer, git ise "already exists" ile exit!=0 döner.
        Directory.CreateDirectory(poolRoot);
        string targetPath = new WorktreeManager(Runner, repo.RootPath, poolRoot)
            .PlanWorktree(branch, branch, useWorktreeToggle: true, selectedSha: sha).Path!;
        File.WriteAllText(targetPath, "bu bir dosya, dizin degil");

        var warnings = new List<string>();
        var cmd = new StartRunCommand("r1", RunMode.Rebuild, repo.RootPath, "Debug", 1, Branch: branch, UseWorktree: true);

        var prepared = await Program.PrepareAsync(cmd, Runner, poolRoot, warnings.Add);

        // GÜVENLİ TARAF: run ÖLMEZ (in-place'e düşülür) — ama InPlace GERÇEĞİ söyler, aksi halde imza
        // local-diff terimini atlar ve "temiz commit'ten derlendi" diyen bir imza dirty working tree için
        // persist edilirdi (A4'ün kapattığı ters-çevrilme penceresi).
        Assert.True(prepared.InPlace);
        Assert.Equal(repo.RootPath, prepared.ScanRoot);
        Assert.Null(prepared.WorktreeObjRoot);
        // Uyarı GERÇEKTEN `git worktree add`'in kendi hatasını taşımalı (git, hedef path'i mesajında yankılar) —
        // aksi halde test, daha erken bir guard'a (branch/sha çözülemedi) düşerek sessizce boşa geçerdi. git
        // Windows'ta path'i '/' ile normalize eder (bkz. yukarıdaki worktree-list karşılaştırmaları).
        Assert.Contains(warnings, w => w.Replace('\\', '/')
            .Contains(targetPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
        Assert.Equal(branch, repo.CurrentBranchName()); // aktif branch ASLA checkout/switch edilmedi (K1)
    }

    [Fact]
    public async Task PrepareAsync_returns_the_worktree_path_as_both_scan_root_and_obj_root_on_success()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");
        string branch = repo.CurrentBranchName();
        string poolRoot = NewPoolRoot();

        var warnings = new List<string>();
        var cmd = new StartRunCommand("r1", RunMode.Rebuild, repo.RootPath, "Debug", 1, Branch: branch, UseWorktree: true);

        var prepared = await Program.PrepareAsync(cmd, Runner, poolRoot, warnings.Add);

        Assert.False(prepared.InPlace); // ÇÖZÜLMÜŞ worktree → imza committed moda (local-diff'siz) HAKLI olarak geçer
        Assert.NotNull(prepared.WorktreeObjRoot);
        Assert.Equal(prepared.ScanRoot, prepared.WorktreeObjRoot); // tarama ve obj izolasyonu AYNI kök
        Assert.NotEqual(repo.RootPath, prepared.ScanRoot);
        Assert.StartsWith(poolRoot, prepared.ScanRoot, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(prepared.ScanRoot, "a.txt"))); // committed HEAD gerçekten orada
        Assert.Empty(warnings);
        Assert.Equal(branch, repo.CurrentBranchName()); // aktif branch ASLA checkout/switch edilmedi (K1)
    }

    // ---- [Fix wave 1 — Finding 1] Havuz cap'i AKTİF branch'e ait worktree'leri de tahliye edebilmeli ----

    [Fact]
    public async Task Pool_cap_evicts_the_least_recently_used_worktree_even_when_every_worktree_belongs_to_the_active_branch()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");
        string activeBranch = repo.CurrentBranchName();
        string activeSha = GitTestRepo.RunGitAt(repo.RootPath, "rev-parse", activeBranch).Trim();

        string poolRoot = NewPoolRoot();
        var mgr = new WorktreeManager(Runner, repo.RootPath, poolRoot);

        // YAYGIN YOL: "aktif branch için worktree aç" (App bugün Branch="" + UseWorktree=true yolluyor) —
        // üçünün de sidecar branch'i AKTİF branch'e eşit, yani ÜÇÜ DE IsActive. Eski kod aday listesini
        // `!IsActive` ile kurduğu için bu havuzda aday HİÇ oluşmuyordu: cap değeri ne olursa olsun tahliye
        // imkânsızdı (her worktree Build'i diske kalıcı bir kaynak ağacı + obj/bin ekliyordu).
        var plans = new List<WorktreePlan>();
        for (int i = 0; i < 3; i++)
        {
            var p = mgr.PlanWorktree(activeBranch, activeBranch, useWorktreeToggle: true, selectedSha: activeSha);
            Assert.True((await mgr.PrepareWorktreeAsync(p)).Success);
            plans.Add(p);
        }

        // D8: sleep-poll YOK — LRU sırası recursive backdate ile deterministik kılınır (mevcut havuz testinin deseni).
        var baseTime = DateTime.UtcNow.AddMinutes(-10);
        for (int i = 0; i < plans.Count; i++)
            DirectoryTimestampHelper.SetAllTimestampsUtc(plans[i].Path!, baseTime.AddMinutes(i));

        var list = await mgr.ListWorktreesAsync();
        Assert.True(list.Success);
        Assert.Equal(3, list.Value!.Count);
        Assert.All(list.Value!, w => Assert.True(w.IsActive)); // hepsi "aktif branch" — eski kodda hiçbiri aday DEĞİLDİ

        string oldestName = Path.GetFileName(plans[0].Path!);
        long total = list.Value!.Sum(w => w.SizeBytes);
        long oldestSize = list.Value!.Single(w => w.Name == oldestName).SizeBytes;

        var pruned = await mgr.PruneToCapAsync(total - oldestSize);

        Assert.True(pruned.Success);
        Assert.Equal([oldestName], pruned.Value); // GERÇEKTEN tahliye etti (ve yalnız EN ESKİ olanı)
        Assert.False(Directory.Exists(plans[0].Path!));
        Assert.Equal(activeBranch, repo.CurrentBranchName()); // K1: ana repo dokunulmadı
    }

    // ---- [Fix wave 1 — Finding 2] Havuz worktree'si YENİDEN KULLANILIR (obj sıcak kalır) ----

    [Fact]
    public async Task PrepareAsync_reuses_the_existing_pool_worktree_for_the_same_branch_and_updates_it_to_the_new_commit()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.WriteFile("b.txt", "will-be-deleted"); // [Fix wave 2 — Fix 5] 2. commit'te SİLİNECEK dosya
        repo.CommitAll("c1");
        string branch = repo.CurrentBranchName();
        string poolRoot = NewPoolRoot();

        var warnings = new List<string>();
        var cmd = new StartRunCommand("r1", RunMode.Rebuild, repo.RootPath, "Debug", 1, Branch: branch, UseWorktree: true);

        var first = await Program.PrepareAsync(cmd, Runner, poolRoot, warnings.Add);
        Assert.False(first.InPlace);
        Assert.True(File.Exists(Path.Combine(first.ScanRoot, "b.txt"))); // 1. commit'in içeriği ilk hazırlıkta orada

        // Kullanıcı çalışmaya devam etti: aktif branch'e YENİ bir commit geldi (b.txt SİLİNDİ).
        repo.WriteFile("a.txt", "v2");
        File.Delete(Path.Combine(repo.RootPath, "b.txt"));
        repo.CommitAll("c2");
        string headBefore = GitTestRepo.RunGitAt(repo.RootPath, "rev-parse", "HEAD").Trim();

        var second = await Program.PrepareAsync(cmd, Runner, poolRoot, warnings.Add);

        Assert.False(second.InPlace);
        // AYNI dizin: obj (BaseIntermediateOutputPath projectId=tam csproj yolu ile türetildiği için) SICAK kalır.
        Assert.Equal(first.ScanRoot, second.ScanRoot);
        Assert.Single(Directory.EnumerateDirectories(poolRoot)); // havuz BÜYÜMEDİ
        Assert.Equal("v2", File.ReadAllText(Path.Combine(second.ScanRoot, "a.txt"))); // hedef commit'e GÜNCELLENDİ
        // [Fix wave 2 — Fix 5] `reset --hard`'ın iddia ettiği şey TAM EŞLEŞMEDİR, iki durumun BİRLEŞİMİ DEĞİL:
        // 1. commit'te var olup 2. commit'te silinen dosya, yeniden kullanılan worktree'de de GİTMİŞ olmalı.
        Assert.False(File.Exists(Path.Combine(second.ScanRoot, "b.txt")));
        Assert.Equal(headBefore, GitTestRepo.RunGitAt(second.ScanRoot, "rev-parse", "HEAD").Trim());
        // K1: ana repo HEAD'i ve branch'i DEĞİŞMEDİ (güncelleme yalnız havuz worktree'sinin İÇİNDE oldu).
        Assert.Equal(headBefore, GitTestRepo.RunGitAt(repo.RootPath, "rev-parse", "HEAD").Trim());
        Assert.Equal(branch, repo.CurrentBranchName());
        Assert.Empty(warnings);
    }

    // ---- [Fix wave 1 — Finding 4] Yalnız YERELDE var olan branch de çözülür ----

    [Fact]
    public async Task PrepareAsync_resolves_a_local_only_branch_that_has_no_remote_tracking_ref()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");
        string activeBranch = repo.CurrentBranchName();

        repo.CreateBranch("feature-local");
        repo.Checkout("feature-local");
        repo.WriteFile("a.txt", "v2-local");
        repo.CommitAll("c2");
        repo.Checkout(activeBranch); // ana repo yeniden aktif branch'te (K1 — gerçek kullanıcı durumu)

        string poolRoot = NewPoolRoot();
        var warnings = new List<string>();
        // Repo'nun HİÇ remote'u yok → refs/remotes/origin/feature-local MEVCUT DEĞİL. UI branch listesi
        // refs/heads/* enumerasyonundan geldiği için kullanıcıya tam olarak BU branch gösterilir.
        var cmd = new StartRunCommand("r1", RunMode.Rebuild, repo.RootPath, "Debug", 1,
            Branch: "feature-local", UseWorktree: true);

        var prepared = await Program.PrepareAsync(cmd, Runner, poolRoot, warnings.Add);

        Assert.False(prepared.InPlace);
        Assert.Equal("v2-local", File.ReadAllText(Path.Combine(prepared.ScanRoot, "a.txt"))); // YEREL ref çözüldü
        Assert.Empty(warnings);
        Assert.Equal(activeBranch, repo.CurrentBranchName()); // K1
    }

    [Fact]
    public async Task PruneToCapAsync_protects_only_the_worktree_that_is_in_use_and_skips_the_disk_scan_when_nothing_can_be_evicted()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");
        string activeBranch = repo.CurrentBranchName();
        string activeSha = GitTestRepo.RunGitAt(repo.RootPath, "rev-parse", activeBranch).Trim();

        string poolRoot = NewPoolRoot();
        var recorder = new RecordingProcessRunner();
        var mgr = new WorktreeManager(recorder, repo.RootPath, poolRoot);

        // (a) BOŞ havuz: [Finding 6] ucuz ön-kontrol → tahliye adayı yok ⇒ ne git çağrısı ne rekürsif disk taraması.
        var emptyPrune = await mgr.PruneToCapAsync(maxBytes: 1);
        Assert.True(emptyPrune.Success);
        Assert.Empty(emptyPrune.Value!);
        Assert.Empty(recorder.Calls);

        var p1 = mgr.PlanWorktree(activeBranch, activeBranch, useWorktreeToggle: true, selectedSha: activeSha);
        Assert.True((await mgr.PrepareWorktreeAsync(p1)).Success);
        var p2 = mgr.PlanWorktree(activeBranch, activeBranch, useWorktreeToggle: true, selectedSha: activeSha);
        Assert.True((await mgr.PrepareWorktreeAsync(p2)).Success);
        string n1 = Path.GetFileName(p1.Path!);
        string n2 = Path.GetFileName(p2.Path!);

        var baseTime = DateTime.UtcNow.AddMinutes(-10);
        DirectoryTimestampHelper.SetAllTimestampsUtc(p1.Path!, baseTime);              // EN ESKİ
        DirectoryTimestampHelper.SetAllTimestampsUtc(p2.Path!, baseTime.AddMinutes(1));

        // (b) KULLANIMDAKİ worktree en eski olsa bile tahliye edilmez; sıradaki aday (n2) gider.
        var pruned = await mgr.PruneToCapAsync(maxBytes: 0, inUseName: n1);
        Assert.True(pruned.Success);
        Assert.Equal([n2], pruned.Value);
        Assert.True(Directory.Exists(p1.Path!));

        // (c) Havuzda YALNIZ kullanımdaki worktree kaldı → yine ucuz ön-kontrol: pahalı yol hiç çalışmaz.
        recorder.Calls.Clear();
        var lastPrune = await mgr.PruneToCapAsync(maxBytes: 0, inUseName: n1);
        Assert.True(lastPrune.Success);
        Assert.Empty(lastPrune.Value!);
        Assert.Empty(recorder.Calls);

        Assert.Equal(activeBranch, repo.CurrentBranchName()); // K1
    }

    [Fact]
    public async Task PrepareAsync_reuse_runs_git_only_inside_the_pool_worktree_and_preserves_its_untracked_obj_cache()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");
        string branch = repo.CurrentBranchName();
        string poolRoot = NewPoolRoot();
        string headBefore = GitTestRepo.RunGitAt(repo.RootPath, "rev-parse", "HEAD").Trim();

        var warnings = new List<string>();
        var cmd = new StartRunCommand("r1", RunMode.Rebuild, repo.RootPath, "Debug", 1, Branch: branch, UseWorktree: true);

        var first = await Program.PrepareAsync(cmd, Runner, poolRoot, warnings.Add);
        // Worktree'nin izole obj'si: yeniden kullanımın TÜM VARLIK SEBEBİ bunun SAĞ KALMASIDIR (sıcak cache).
        Directory.CreateDirectory(Path.Combine(first.ScanRoot, "obj", "X"));
        File.WriteAllText(Path.Combine(first.ScanRoot, "obj", "X", "project.assets.json"), "warm");

        repo.WriteFile("a.txt", "v2");
        repo.CommitAll("c2");
        string headAfterCommit = GitTestRepo.RunGitAt(repo.RootPath, "rev-parse", "HEAD").Trim();

        var recorder = new RecordingProcessRunner();
        var second = await Program.PrepareAsync(cmd, recorder, poolRoot, warnings.Add);

        Assert.Equal(first.ScanRoot, second.ScanRoot);
        Assert.Equal("warm", File.ReadAllText(Path.Combine(second.ScanRoot, "obj", "X", "project.assets.json"))); // obj SICAK kaldı
        Assert.Equal("v2", File.ReadAllText(Path.Combine(second.ScanRoot, "a.txt")));                              // kaynaklar GÜNCEL

        // Güncelleme YALNIZ havuz worktree'sinin İÇİNDE: reset komutunun çalışma dizini worktree'dir...
        var resets = recorder.Specs.Where(s => s.Arguments.Contains("reset")).ToList();
        var reset = Assert.Single(resets);
        Assert.Equal(["reset", "--hard", headAfterCommit], reset.Arguments);
        Assert.Equal(second.ScanRoot, Path.GetFullPath(reset.WorkingDirectory!));
        // ...ve ANA REPO'da hiçbir mutasyon komutu çalıştırılmadı (yalnız salt-okur sorgular + worktree list).
        foreach (var spec in recorder.Specs.Where(s =>
            string.Equals(Path.GetFullPath(s.WorkingDirectory!), Path.GetFullPath(repo.RootPath), StringComparison.OrdinalIgnoreCase)))
        {
            Assert.DoesNotContain("reset", spec.Arguments);
            Assert.DoesNotContain("checkout", spec.Arguments);
            Assert.DoesNotContain("switch", spec.Arguments);
            Assert.DoesNotContain("pull", spec.Arguments);
            Assert.DoesNotContain("merge", spec.Arguments);
        }
        Assert.Equal(headAfterCommit, GitTestRepo.RunGitAt(repo.RootPath, "rev-parse", "HEAD").Trim()); // K1
        Assert.Equal(branch, repo.CurrentBranchName());
        Assert.NotEqual(headBefore, headAfterCommit); // (fixture doğrulaması: gerçekten yeni bir commit vardı)
        Assert.Empty(warnings);
    }

    // ---- [Fix wave 1 — Finding 3] FARKLI branch + hazırlık başarısız = run ÖLÜR (sessizce yanlış branch DERLENMEZ) ----

    [Fact]
    public async Task PrepareAsync_fails_the_run_instead_of_falling_back_to_in_place_when_a_different_branch_cannot_be_prepared()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");
        string activeBranch = repo.CurrentBranchName();

        repo.CreateBranch("feature-x");
        repo.Checkout("feature-x");
        repo.WriteFile("a.txt", "v2-feature");
        repo.CommitAll("c2");
        repo.Checkout(activeBranch);

        string poolRoot = NewPoolRoot();
        Directory.CreateDirectory(poolRoot);
        // `git worktree add`'i deterministik olarak patlat: hedef path'e önceden bir DOSYA konur (aday seçimi
        // yalnız DİZİNLERE baktığı için PrepareAsync da AYNI adı seçer) — mevcut fallback testinin deseni.
        string targetPath = new WorktreeManager(Runner, repo.RootPath, poolRoot)
            .PlanWorktree(activeBranch, "feature-x", useWorktreeToggle: true, selectedSha: "0".PadLeft(40, '0')).Path!;
        File.WriteAllText(targetPath, "bu bir dosya, dizin degil");

        var warnings = new List<string>();
        var cmd = new StartRunCommand("r1", RunMode.Rebuild, repo.RootPath, "Debug", 1,
            Branch: "feature-x", UseWorktree: true);

        var ex = await Assert.ThrowsAsync<WorktreePreparationException>(
            () => Program.PrepareAsync(cmd, Runner, poolRoot, warnings.Add));

        // Kullanıcıya gösterilen metin İNGİLİZCE, branch adını VE asıl sebebi (git'in kendi hatası) taşır.
        Assert.Contains("feature-x", ex.Message, StringComparison.Ordinal);
        Assert.Contains("isolated worktree", ex.Message, StringComparison.Ordinal);
        Assert.Contains(targetPath.Replace('\\', '/'), ex.Message.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(warnings, w => w.Contains("falling back to in-place", StringComparison.Ordinal));
        Assert.Equal(activeBranch, repo.CurrentBranchName()); // K1 — ana repo dokunulmadı
    }

    // ---- [Fix wave 2 — Fix 2] toggle KAPALI + FARKLI branch da worktree'yi ZORUNLU kılmalı ----

    [Fact]
    public async Task PrepareAsync_still_requires_a_worktree_for_a_different_branch_even_when_the_toggle_is_off()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");
        string activeBranch = repo.CurrentBranchName();

        repo.CreateBranch("feature-x");
        repo.Checkout("feature-x");
        repo.WriteFile("a.txt", "v2-feature");
        repo.CommitAll("c2");
        repo.Checkout(activeBranch);

        string poolRoot = NewPoolRoot();
        Directory.CreateDirectory(poolRoot);
        // Aynı deterministik patlatma deseni (bkz. yukarıdaki Finding 3 testi) — burada tek fark UseWorktree=false.
        string targetPath = new WorktreeManager(Runner, repo.RootPath, poolRoot)
            .PlanWorktree(activeBranch, "feature-x", useWorktreeToggle: true, selectedSha: "0".PadLeft(40, '0')).Path!;
        File.WriteAllText(targetPath, "bu bir dosya, dizin degil");

        var warnings = new List<string>();
        // Toggle KAPALI ama Branch DOLU (aktif branch'ten farklı): erken çıkışın ikinci yarısı
        // (`string.IsNullOrWhiteSpace(cmd.Branch)`) yalnız Branch BOŞ iken devreye girer — burada girmemeli,
        // aksi halde aktif branch'in kirli çalışma ağacı sessizce "feature-x" adına derlenirdi (in-place free-ride).
        var cmd = new StartRunCommand("r1", RunMode.Rebuild, repo.RootPath, "Debug", 1,
            Branch: "feature-x", UseWorktree: false);

        var ex = await Assert.ThrowsAsync<WorktreePreparationException>(
            () => Program.PrepareAsync(cmd, Runner, poolRoot, warnings.Add));

        Assert.Contains("feature-x", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(warnings, w => w.Contains("falling back to in-place", StringComparison.Ordinal));
        Assert.Equal(activeBranch, repo.CurrentBranchName()); // K1 — ana repo dokunulmadı
    }

    // ---- [Fix wave 2 — Fix 1] aktif branch okunurken THROW (result-failure değil) da zorunlu-worktree yolunu tetiklemeli ----

    [Fact]
    public async Task PrepareAsync_fails_the_run_when_reading_the_active_branch_throws_instead_of_returning_a_failure_result()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "v1");
        repo.CommitAll("c1");
        string activeBranch = repo.CurrentBranchName();
        string poolRoot = NewPoolRoot();

        var warnings = new List<string>();
        // "feature-x" gerçekten var olmak ZORUNDA DEĞİL: aktif branch okuma adımı throw ettiği için akış
        // hiçbir zaman branch'in var olup olmadığını sorgulayacak noktaya ulaşmıyor — tek gereken, isteğin
        // AÇIKÇA bir branch adlandırmış olması (mandatory henüz atanmadan önceki tek güvenilir sinyal).
        var cmd = new StartRunCommand("r1", RunMode.Rebuild, repo.RootPath, "Debug", 1,
            Branch: "feature-x", UseWorktree: true);

        var ex = await Assert.ThrowsAsync<WorktreePreparationException>(
            () => Program.PrepareAsync(cmd, new ThrowingOnCurrentBranchReadProcessRunner(), poolRoot, warnings.Add));

        Assert.Contains("feature-x", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(warnings, w => w.Contains("falling back to in-place", StringComparison.Ordinal));
        Assert.Equal(activeBranch, repo.CurrentBranchName()); // K1 — ana repo dokunulmadı
    }

    /// <summary>
    /// [Fix wave 2 — Fix 1] <c>GitService.GetCurrentBranchAsync</c>'in kullandığı <c>symbolic-ref</c> komutunda
    /// GERÇEK bir exception (Win32Exception/InvalidOperationException DEĞİL — <see cref="GitCommandExecutor"/>
    /// bu ikisini zaten <c>Fail</c>'e çevirir; burada hung-repo/timeout/iptal sınıfını temsilen <see
    /// cref="IOException"/> kullanılır) fırlatır. Diğer TÜM komutlar gerçek <see cref="ProcessRunner"/>'a
    /// delege edilir — bu test yalnız "aktif branch okuma THROW ederse" seam'ini izole eder.
    /// </summary>
    private sealed class ThrowingOnCurrentBranchReadProcessRunner : IProcessRunner
    {
        private readonly ProcessRunner _inner = new();

        public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
        {
            if (spec.Arguments.Contains("symbolic-ref"))
                throw new IOException("simulated hung-repo stream failure while reading the active branch");
            return _inner.RunAsync(spec, ct);
        }
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        private readonly ProcessRunner _inner = new();

        public List<IReadOnlyList<string>> Calls { get; } = [];

        /// <summary>[Fix wave 1] Çağrının ARGÜMANLARI + ÇALIŞMA DİZİNİ — "reset ana repoda değil, yalnız havuz worktree'sinde koştu" iddiasının kanıtı için.</summary>
        public List<ProcessSpec> Specs { get; } = [];

        public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
        {
            Calls.Add(spec.Arguments);
            Specs.Add(spec);
            return await _inner.RunAsync(spec, ct);
        }
    }
}
