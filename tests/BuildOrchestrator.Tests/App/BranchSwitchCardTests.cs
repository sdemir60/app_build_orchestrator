using System.IO;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Tests.Git;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [worktree] Branch karar kartı: branch chip'inden seçilen branch'e geçiş kirli ağaçta durursa (Settings → General "Stash and switch
/// branches" kapalı — varsayılan) kart sorar: stash'le ve geç, diğer kopyada aç ya da vazgeç. Ayar açıksa bugünkü gibi sessizce
/// stash'lenir ve kart hiç açılmaz (ayar "sorma, stash'le" demektir).
///
/// <para><b>Diğer kopya:</b> repo kopyasındayken worktree, worktree'deyken repo. Checkout DİĞER kökte gönderilir; geçiş olursa (ya da branch
/// orada zaten açıksa) uygulama o kopyaya geçer — kopya geçişi + branch satırıyla tek bölüm. Diğer kopya da kirliyse uygulama yine oraya
/// geçer ve kart orada yeniden açılır (kullanıcının tarifi: "worktree'ye geçtim, orada da değişiklik var — ikinci defa çıkar"). Worktree
/// tanımlı değilse "diğer kopya" kurulumu ister. Diğer kopyada açık bir branch'e geçiş denenmez: motor stash'lemeden reddeder ve konsola
/// uyarı düşer.</para>
///
/// <para>Harness <see cref="BranchCheckoutTests"/>'inkidir; gönderim başarılıdır (<c>DebugSendOverride</c>), cevabı test verir.</para>
/// </summary>
public class BranchSwitchCardTests
{
    private const string FullSha = "b7e91d4a0c1f2e3d4c5b6a7980716253443526a1";
    private static readonly BranchRef FeatureX = new("feature/x", "bbbbbbbbbbbb", false, false);

    private static RunViewModel NewVm(List<IpcCommand> sent, string root = @"D:\repo", string worktree = "")
    {
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), MainWindowHost.NeverTickingBatcher(), () => "r1")
        {
            RootPath = root,
            WorktreeFolder = worktree,
        };
        vm.OnEvent(new BranchListEvent([
            new BranchRef("main", "aaaaaaaaaaaa", IsActive: true, IsRemoteTracking: false),
            FeatureX,
        ]));
        vm.DebugOnCommandSent = sent.Add;
        vm.DebugSendOverride = _ => Task.CompletedTask;
        return vm;
    }

    private static (GitTestRepo Repo, string Worktree) RepoWithWorktree()
    {
        var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "one");
        repo.CommitAll("first");
        repo.CreateBranch("side");
        return (repo, repo.AddWorktree("side"));
    }

    private static string[] Lines(RunViewModel vm) =>
        vm.GetRunDocumentText().Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Kirli ağaç kartı açar: hedef, kirli kopya ve dosya sayısı karttadır; konsolun uyarısı yerinde kalır.</summary>
    [Fact]
    public async Task A_dirty_tree_opens_the_card_for_the_requested_branch()
    {
        var sent = new List<IpcCommand>();
        var vm = NewVm(sent);
        await vm.SelectBranch(FeatureX);

        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Dirty, "main", "main", null, 3, null, null));

        Assert.Equal(new PendingBranchSwitch(FeatureX, WorkingCopy.Repository, 3), vm.PendingBranchSwitch);
        Assert.Equal(PlanProgressLines.SwitchRefusedDirty(3), Lines(vm)[^1]);
        Assert.False(vm.CheckoutBusy);
    }

    [Fact]
    public async Task Cancel_closes_the_card_and_sends_nothing()
    {
        var sent = new List<IpcCommand>();
        var vm = NewVm(sent);
        await vm.SelectBranch(FeatureX);
        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Dirty, "main", "main", null, 3, null, null));
        sent.Clear();

        await vm.DecideBranchSwitchAsync(BranchDecision.Cancel);

        Assert.Null(vm.PendingBranchSwitch);
        Assert.Empty(sent);
    }

    /// <summary>Stash: aynı checkout bu kez stash'le gider — bugünkü ayar açıkken olanın aynısı, tek seferlik.</summary>
    [Fact]
    public async Task Stash_and_switch_sends_the_same_checkout_with_stash()
    {
        var sent = new List<IpcCommand>();
        var vm = NewVm(sent);
        await vm.SelectBranch(FeatureX);
        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Dirty, "main", "main", null, 3, null, null));
        sent.Clear();

        await vm.DecideBranchSwitchAsync(BranchDecision.Stash);

        Assert.Null(vm.PendingBranchSwitch);
        Assert.Equal(new CheckoutBranchCommand(@"D:\repo", "feature/x", IsRemote: false, StashIfDirty: true),
            Assert.Single(sent.OfType<CheckoutBranchCommand>()));
        Assert.True(vm.CheckoutBusy);
    }

    /// <summary>Diğer kopya: checkout worktree'de gider (stash ayarıyla); geçiş başarılıysa uygulama worktree'ye geçer — bölüm kopya
    /// satırıyla başlar, branch satırı ardından gelir, Sync worktree kökünde koşar.</summary>
    [Fact]
    public async Task Using_the_worktree_checks_the_branch_out_there_and_switches_to_it()
    {
        var (repo, worktree) = RepoWithWorktree();
        using var _ = repo;
        var sent = new List<IpcCommand>();
        var vm = NewVm(sent, repo.RootPath, worktree);
        await vm.SelectBranch(FeatureX);
        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Dirty, "main", "main", null, 3, null, null));
        sent.Clear();

        await vm.DecideBranchSwitchAsync(BranchDecision.OtherCopy);

        Assert.Equal(new CheckoutBranchCommand(worktree, "feature/x", IsRemote: false, StashIfDirty: false),
            Assert.Single(sent.OfType<CheckoutBranchCommand>()));
        Assert.Equal(WorkingCopy.Repository, vm.ActiveCopy); // geçiş cevabı bekler

        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Switched, "side", "feature/x", FullSha, 0, null, null));

        Assert.Equal(WorkingCopy.Worktree, vm.ActiveCopy);
        Assert.Equal(worktree, Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
        var lines = Lines(vm);
        Assert.Equal(WorkingCopyText.Switched(WorkingCopy.Worktree, worktree), lines[0]);
        Assert.Equal(PlanProgressLines.SwitchedBranch("side", "feature/x", "b7e91d4"), lines[1]);
        Assert.False(vm.CheckoutBusy);
    }

    /// <summary>Branch worktree'de zaten açıksa checkout bir şey değiştirmez ama kullanıcı oraya gitmek istedi: uygulama worktree'ye geçer.</summary>
    [Fact]
    public async Task A_branch_already_open_in_the_worktree_just_switches_there()
    {
        var (repo, worktree) = RepoWithWorktree();
        using var _ = repo;
        var sent = new List<IpcCommand>();
        var vm = NewVm(sent, repo.RootPath, worktree);
        await vm.SelectBranch(FeatureX);
        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Dirty, "main", "main", null, 3, null, null));
        await vm.DecideBranchSwitchAsync(BranchDecision.OtherCopy);

        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.AlreadyOn, "feature/x", "feature/x", null, 0, null, null));

        Assert.Equal(WorkingCopy.Worktree, vm.ActiveCopy);
        Assert.Equal(WorkingCopyText.Switched(WorkingCopy.Worktree, worktree), Lines(vm)[0]);
    }

    /// <summary>Worktree de kirliyse: uygulama worktree'ye geçer ve kart orada yeniden açılır — bu kez diğer kopya repo'dur.</summary>
    [Fact]
    public async Task A_dirty_worktree_switches_there_and_asks_again()
    {
        var (repo, worktree) = RepoWithWorktree();
        using var _ = repo;
        var sent = new List<IpcCommand>();
        var vm = NewVm(sent, repo.RootPath, worktree);
        await vm.SelectBranch(FeatureX);
        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Dirty, "main", "main", null, 3, null, null));
        await vm.DecideBranchSwitchAsync(BranchDecision.OtherCopy);

        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Dirty, "side", "side", null, 2, null, null));

        Assert.Equal(WorkingCopy.Worktree, vm.ActiveCopy);
        Assert.Equal(new PendingBranchSwitch(FeatureX, WorkingCopy.Worktree, 2), vm.PendingBranchSwitch);
        Assert.Contains(PlanProgressLines.SwitchRefusedDirty(2), Lines(vm));
        vm.OnEvent(new SyncStartedEvent(worktree, "side"));        // geçişin Sync'i biter — kartın kapısı açılır
        vm.OnEvent(new SyncCompletedEvent("side", "sha1234", false, 0, 0));
        sent.Clear();

        await vm.DecideBranchSwitchAsync(BranchDecision.OtherCopy); // bu kez repo

        Assert.Equal(repo.RootPath, Assert.Single(sent.OfType<CheckoutBranchCommand>()).RootPath);
    }

    [Fact]
    public async Task Without_a_worktree_the_other_copy_asks_for_setup()
    {
        var sent = new List<IpcCommand>();
        var vm = NewVm(sent);
        int setupRequests = 0;
        vm.WorktreeSetupRequested += (_, _) => setupRequests++;
        await vm.SelectBranch(FeatureX);
        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Dirty, "main", "main", null, 3, null, null));
        sent.Clear();

        await vm.DecideBranchSwitchAsync(BranchDecision.OtherCopy);

        Assert.Equal(1, setupRequests);
        Assert.Null(vm.PendingBranchSwitch);
        Assert.Empty(sent);
    }

    /// <summary>Hedef başka bir kopyada açık: motor hiçbir şey yapmadı (stash da) — konsol hangi kopyada açık olduğunu söyler, Sync gitmez,
    /// kart açılmaz.</summary>
    [Fact]
    public async Task A_branch_open_in_another_copy_writes_a_warning_and_changes_nothing()
    {
        var sent = new List<IpcCommand>();
        var vm = NewVm(sent);
        await vm.SelectBranch(FeatureX);
        sent.Clear();

        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.CheckedOutElsewhere, "main", "main", null, 0, null, @"D:\repo-worktree"));

        Assert.Equal(PlanProgressLines.SwitchRefusedElsewhere("feature/x", @"D:\repo-worktree"), Lines(vm)[^1]);
        Assert.Null(vm.PendingBranchSwitch);
        Assert.Empty(sent);
        Assert.False(vm.CheckoutBusy);
        Assert.Null(vm.CurrentOperation);
    }

    [Fact]
    public async Task Picking_another_branch_closes_an_open_card()
    {
        var sent = new List<IpcCommand>();
        var vm = NewVm(sent);
        await vm.SelectBranch(FeatureX);
        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Dirty, "main", "main", null, 3, null, null));

        await vm.SelectBranch(new BranchRef("other", "cccccccccccc", false, false));

        Assert.Null(vm.PendingBranchSwitch);
    }

    /// <summary>Kartın konusu bir kopyanın kirli ağacıdır: kopya değişince (alt bardaki geçiş) kart kapanır — açık kalsaydı kararı yeni
    /// kopyaya uygulanırdı.</summary>
    [Fact]
    public async Task Switching_the_copy_closes_an_open_card()
    {
        var (repo, worktree) = RepoWithWorktree();
        using var _ = repo;
        var sent = new List<IpcCommand>();
        var vm = NewVm(sent, repo.RootPath, worktree);
        await vm.SelectBranch(FeatureX);
        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Dirty, "main", "main", null, 3, null, null));
        Assert.NotNull(vm.PendingBranchSwitch);

        await vm.SwitchCopyAsync(WorkingCopy.Worktree);

        Assert.Equal(WorkingCopy.Worktree, vm.ActiveCopy);
        Assert.Null(vm.PendingBranchSwitch);
    }

    /// <summary>Repo kökü değişince ya da workspace kapanınca da kart kapanır: konusu eski kökün ağacıydı.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_new_repository_root_or_a_closed_workspace_closes_an_open_card(bool close)
    {
        using var other = new GitTestRepo();
        var sent = new List<IpcCommand>();
        var vm = NewVm(sent);
        await vm.SelectBranch(FeatureX);
        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Dirty, "main", "main", null, 3, null, null));
        Assert.NotNull(vm.PendingBranchSwitch);

        await vm.ApplySettingsAsync([], close ? "" : other.RootPath, []);

        Assert.Null(vm.PendingBranchSwitch);
    }

    /// <summary>"Diğer kopya" kararı o kopyanın git dizinine de bakar: worktree'de yarıda bir rebase varken checkout gönderilmez ve konsol
    /// nedenini söyler. Chip'in git kapısı (<see cref="RunViewModel.CanSwitchBranch"/>) yalnız aktif kopyayı yoklar — worktree'nin rebase'i
    /// kendi git dizinindedir (<c>.git/worktrees/&lt;ad&gt;/rebase-merge</c>).</summary>
    [Fact]
    public async Task The_other_copy_mid_rebase_refuses_the_checkout_with_a_warning()
    {
        var (repo, worktree) = RepoWithWorktree();
        using var _ = repo;
        var sent = new List<IpcCommand>();
        var vm = NewVm(sent, repo.RootPath, worktree);
        await vm.SelectBranch(FeatureX);
        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Dirty, "main", "main", null, 3, null, null));
        Directory.CreateDirectory(Path.Combine(GitDirectory.Resolve(worktree)!, "rebase-merge"));
        sent.Clear();

        await vm.DecideBranchSwitchAsync(BranchDecision.OtherCopy);

        Assert.Empty(sent.OfType<CheckoutBranchCommand>());
        Assert.Equal(BranchSwitchText.OtherCopyBusy("feature/x", WorkingCopy.Worktree, GitOperation.Rebase), Lines(vm)[^1]);
        Assert.False(vm.CheckoutBusy);
        Assert.Equal(WorkingCopy.Repository, vm.ActiveCopy);
    }

    /// <summary>Motorun reddettiği checkout'un isteği unutulur: ardından gelen sahipsiz bir cevap (sırası kaymış bir olay) eski isteğin
    /// kopyasına geçirmez — istek diğer kopyaya gitmişti ve cevap "geçti" diyor.</summary>
    [Fact]
    public async Task A_stray_answer_after_a_rejected_checkout_does_not_switch_the_copy()
    {
        var (repo, worktree) = RepoWithWorktree();
        using var _ = repo;
        var sent = new List<IpcCommand>();
        var vm = NewVm(sent, repo.RootPath, worktree);
        await vm.SelectBranch(FeatureX);
        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Dirty, "main", "main", null, 3, null, null));
        await vm.DecideBranchSwitchAsync(BranchDecision.OtherCopy);
        vm.OnEvent(new ErrorEvent("checkoutRejected", "A run is in flight — stop it before switching branches."));

        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Switched, "side", "feature/x", FullSha, 0, null, null));

        Assert.Equal(WorkingCopy.Repository, vm.ActiveCopy);
    }

    /// <summary>Motor checkout'un ortasında ölünce de istek unutulur: yeniden başlayan motordan gelen sahipsiz bir cevap kart açmaz.</summary>
    [Fact]
    public async Task A_stray_answer_after_losing_the_engine_opens_no_card()
    {
        var sent = new List<IpcCommand>();
        var vm = NewVm(sent);
        await vm.SelectBranch(FeatureX);
        vm.OnEngineExited(1);

        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Dirty, "main", "main", null, 3, null, null));

        Assert.Null(vm.PendingBranchSwitch);
    }

    /// <summary>Kartın düğme kapısı branch chip'ininkidir: geçişin Sync'i uçuşta açılan kart (diğer kopya da kirliydi) iş bitene dek
    /// beklemeye alınır — basılan karar sessizce düşmez.</summary>
    [Fact]
    public async Task The_card_waits_for_the_branch_gate()
    {
        var sent = new List<IpcCommand>();
        var vm = NewVm(sent);
        await vm.SelectBranch(FeatureX);
        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Dirty, "main", "main", null, 3, null, null));
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(vm.CanSwitchBranch);
        sent.Clear();

        await vm.DecideBranchSwitchAsync(BranchDecision.Stash);

        Assert.Empty(sent);
        Assert.NotNull(vm.PendingBranchSwitch); // karar düşmedi: kart açık kalır
    }

    // ================================================================ kart (realize)

    /// <summary>Yeni XAML kökünün realize testi: kart konuyu yazar (kaç dosya, hangi kopya, iki seçeneğin anlamı), diğer kopya düğmesi
    /// worktree tanımsızken kurulum der, Cancel kararı iletir.</summary>
    [StaFact]
    public async Task The_card_realizes_with_the_count_the_copies_and_three_actions()
    {
        var sent = new List<IpcCommand>();
        var vm = NewVm(sent);
        await vm.SelectBranch(FeatureX);
        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Dirty, "main", "main", null, 3, null, null));
        var host = DsResources.NewHost();
        var card = new BuildOrchestrator.App.Views.BranchSwitchCard { DataContext = vm };
        var window = DsResources.Realize(host, card);
        card.IsOpen = true;
        card.UpdateLayout();

        Assert.Equal(BranchSwitchText.Sentence(3, WorkingCopy.Repository), card.Sentence.Text);
        Assert.Equal(BranchSwitchText.Outcome("feature/x", WorkingCopy.Repository), card.Outcome.Text);
        Assert.Equal(BranchSwitchText.OtherCopy(WorkingCopy.Repository, worktreeReady: false), card.OtherCopyButton.Content);
        Assert.True(card.StashButton.IsEnabled);
        Assert.True(card.CancelButton.ActualWidth > 0, "kart yerleşmedi");

        CommandPress.Click(card.CancelButton);

        Assert.Null(vm.PendingBranchSwitch);
        GC.KeepAlive(window);
    }

    /// <summary>Alt bar kartı VM'in konusuyla açar; dışarı tıklama (Popup kapanır) vazgeçmektir.</summary>
    [StaFact]
    public async Task The_bar_opens_the_card_for_a_pending_switch_and_dismissing_it_cancels()
    {
        var sent = new List<IpcCommand>();
        var vm = NewVm(sent);
        var host = DsResources.NewHost();
        var bar = new BuildOrchestrator.App.Views.ActionBar { DataContext = vm };
        var window = DsResources.Realize(host, bar);
        Assert.False(bar.BranchSwitchPopup.IsOpen);

        await vm.SelectBranch(FeatureX);
        vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Dirty, "main", "main", null, 3, null, null));
        Assert.True(bar.BranchSwitchPopup.IsOpen);

        bar.BranchSwitchPopup.IsOpen = false; // light dismiss

        Assert.Null(vm.PendingBranchSwitch);
        Assert.Empty(sent.OfType<CheckoutBranchCommand>().Skip(1)); // vazgeçmek bir şey göndermez
        GC.KeepAlive(window);
    }

    // ================================================================ metinler

    [Fact]
    public void Card_texts_count_files_and_name_the_copies()
    {
        Assert.Equal("1 file has uncommitted changes in the repository.", BranchSwitchText.Sentence(1, WorkingCopy.Repository));
        Assert.Equal("3 files have uncommitted changes in the worktree.", BranchSwitchText.Sentence(3, WorkingCopy.Worktree));
        Assert.Equal("Use worktree", BranchSwitchText.OtherCopy(WorkingCopy.Repository, worktreeReady: true));
        Assert.Equal("Set up worktree…", BranchSwitchText.OtherCopy(WorkingCopy.Repository, worktreeReady: false));
        Assert.Equal("Use repository", BranchSwitchText.OtherCopy(WorkingCopy.Worktree, worktreeReady: true));
        Assert.Equal("Stash and switch", BranchSwitchText.Stash);
        Assert.Equal("warning: feature/x is already checked out in D:\\repo-worktree — a branch can be open in one working copy at a time",
            PlanProgressLines.SwitchRefusedElsewhere("feature/x", @"D:\repo-worktree"));
        Assert.Equal("warning: feature/x not opened in the worktree · Rebase in progress — finish or abort it in git",
            BranchSwitchText.OtherCopyBusy("feature/x", WorkingCopy.Worktree, GitOperation.Rebase));
    }
}
