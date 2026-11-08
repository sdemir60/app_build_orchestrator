using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using BuildOrchestrator.App;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Git;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [worktree] Alt bardaki Repo ↔ Worktree anahtarı: workspace adının sağında, branch chip'inin solunda, bardaki öteki chip'lerle aynı dilde
/// (<c>Ds.Bar.Chip</c>, işaretliyken amber). İşaretli = worktree açık. Tıklamak VM'in geçişini ister (<see cref="RunViewModel.SwitchCopyAsync"/>);
/// geçilecek worktree yoksa VM kurulum ister ve kabuk Settings'i açar. Kapısı Debug|Release segment'ininkidir (geçiş bir Sync başlatır).
///
/// <para><b>Genişlik:</b> anahtar etiketsiz kare bir chip'tir (Restart engine'in ölçüsü) — bar en dar pencerede sığmaya devam eder
/// (<see cref="With_the_worktree_toggle_the_bar_still_fits_the_narrowest_window"/>). Restart engine görünürken anahtar ona yer açar
/// (<see cref="The_toggle_makes_room_for_restart_engine"/>): ölçüldü — ikisi birlikte en dolu barı 3,8 px taşırıyordu; motor ölüyken geçiş
/// zaten çalışamaz (yeni kökte Sync ister).</para>
/// </summary>
public partial class ActionBarTests
{
    private static RunViewModel NewWorktreeVm(string root = @"D:\repo", string worktree = "")
    {
        var vm = NewVm();
        vm.RootPath = root;
        vm.WorktreeFolder = worktree;
        return vm;
    }

    [StaFact]
    public void The_worktree_toggle_sits_between_the_workspace_name_and_the_branch_chip()
    {
        var (bar, window) = Realize(NewWorktreeVm());

        var group = (Panel)bar.WorktreeToggle.Parent;
        int label = group.Children.IndexOf(bar.WorkspaceLabel);
        int toggle = group.Children.IndexOf(bar.WorktreeToggle);
        int branch = group.Children.IndexOf((UIElement)bar.BranchChip.Parent);

        Assert.True(label >= 0 && toggle == label + 1 && branch == toggle + 1, $"sıra: label {label}, toggle {toggle}, branch {branch}");
        Assert.Equal(Visibility.Visible, bar.WorktreeToggle.Visibility);
        Assert.Equal(AccessibilityNames.WorktreeToggle, AutomationProperties.GetName(bar.WorktreeToggle));
        Assert.Same(bar.FindResource("Ds.Bar.Chip"), bar.WorktreeToggle.Style);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_worktree_toggle_is_hidden_without_a_workspace()
    {
        var (bar, window) = Realize(NewWorktreeVm(root: ""));

        Assert.Equal(Visibility.Collapsed, bar.WorktreeToggle.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>İşaret VM'in kopyasını izler — tıklamanın kendi işareti değil: geçiş reddedilirse (ya da kurulum istenirse) anahtar
    /// yerinde kalır.</summary>
    [StaFact]
    public void Without_a_worktree_a_click_asks_for_setup_and_the_toggle_stays_off()
    {
        var vm = NewWorktreeVm();
        var (bar, window) = Realize(vm);
        int setupRequests = 0;
        vm.WorktreeSetupRequested += (_, _) => setupRequests++;

        CommandPress.Click(bar.WorktreeToggle);
        DispatcherPump.DrainToIdle();

        Assert.Equal(1, setupRequests);
        Assert.False(bar.WorktreeToggle.IsChecked);
        Assert.Equal(WorkingCopy.Repository, vm.ActiveCopy);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_click_switches_to_the_worktree_and_back_and_the_toggle_follows()
    {
        using var repo = new GitTestRepo();
        repo.WriteFile("a.txt", "one");
        repo.CommitAll("first");
        repo.CreateBranch("side");
        string worktree = repo.AddWorktree("side");
        var vm = NewWorktreeVm(repo.RootPath, worktree);
        var (bar, window) = Realize(vm);
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        CommandPress.Click(bar.WorktreeToggle);
        DispatcherPump.DrainToIdle();

        Assert.Equal(WorkingCopy.Worktree, vm.ActiveCopy);
        Assert.True(bar.WorktreeToggle.IsChecked);
        Assert.Equal(worktree, Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
        Assert.Equal(WorktreeToggleText.Tooltip(WorkingCopy.Worktree, worktree, repo.RootPath), bar.WorktreeToggle.ToolTip); // bir sonraki tık
        Assert.Equal(worktree, bar.WorkspaceLabel.ToolTip); // etiketin tooltip'i derlenen köktür

        CommandPress.Click(bar.WorktreeToggle);
        DispatcherPump.DrainToIdle();

        Assert.Equal(WorkingCopy.Repository, vm.ActiveCopy);
        Assert.False(bar.WorktreeToggle.IsChecked);
        GC.KeepAlive(window);
    }

    /// <summary>Kapı Sync'inkidir: Sync (ya da bir koşu, bir workspace işi) uçuştayken anahtar pasiftir; tooltip pasifken de görünür.</summary>
    [StaFact]
    public void The_worktree_toggle_follows_the_switch_gate()
    {
        var vm = NewWorktreeVm();
        var (bar, window) = Realize(vm);
        Assert.True(bar.WorktreeToggle.IsEnabled);

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(bar.WorktreeToggle.IsEnabled);
        Assert.True(ToolTipService.GetShowOnDisabled(bar.WorktreeToggle));

        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));
        Assert.True(bar.WorktreeToggle.IsEnabled);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_worktree_toggle_tooltip_says_what_a_click_does()
    {
        var vm = NewWorktreeVm();
        var (bar, window) = Realize(vm);

        Assert.Equal(WorktreeToggleText.Tooltip(WorkingCopy.Repository, ""), bar.WorktreeToggle.ToolTip);

        vm.WorktreeFolder = @"D:\repo-worktree";
        Assert.Equal(WorktreeToggleText.Tooltip(WorkingCopy.Repository, @"D:\repo-worktree"), bar.WorktreeToggle.ToolTip);
        GC.KeepAlive(window);
    }

    /// <summary>Metinler: tanımsızken kurulum davet eder, repo kopyasında worktree'ye, worktree'de repo'ya geçişi yolu ile söyler.</summary>
    [Fact]
    public void Worktree_toggle_texts_name_the_target_and_its_path()
    {
        Assert.Equal("Set up a worktree — a second working copy for another branch", WorktreeToggleText.Tooltip(WorkingCopy.Repository, ""));
        Assert.Equal(@"Switch to the worktree — D:\src\acme-worktree", WorktreeToggleText.Tooltip(WorkingCopy.Repository, @"D:\src\acme-worktree"));
        Assert.Equal(@"Back to the repository — D:\src\acme", WorktreeToggleText.Tooltip(WorkingCopy.Worktree, @"D:\src\acme-worktree", @"D:\src\acme"));
    }

    /// <summary>Restart engine görünürken anahtar yer açar: ikisi birlikte en dolu barı en dar pencerede taşırıyordu (ölçüldü: 3,8 px) ve
    /// motor ölüyken geçişin anlamı yoktur — yeni kökte Sync ister. Görünürlük <see cref="RunViewModel.OffersEngineRestart"/>'ın bildirimiyle
    /// iki yönde de tazelenir (motor dönünce anahtar geri gelir).</summary>
    [StaFact]
    public void The_toggle_makes_room_for_restart_engine()
    {
        var vm = NewWorktreeVm();
        var (bar, window) = Realize(vm);
        Assert.Equal(Visibility.Visible, bar.WorktreeToggle.Visibility);

        vm.OnEngineExited(2);

        Assert.True(vm.OffersEngineRestart); // ön-koşul: Restart engine çizildi
        Assert.Equal(Visibility.Collapsed, bar.WorktreeToggle.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>Anahtar görünürken bar en dar pencerede sığar — Restart engine testiyle aynı dolu durumlar (üç basamaklı sayaçlar, uyarı
    /// chip'i, bir iş branch'i ya da <c>develop</c> + <c>N behind</c>), motor sağlıklı.</summary>
    [StaTheory]
    [InlineData("fix/triage-tray-engine-notice", 0)]
    [InlineData("develop", 12)]
    public void With_the_worktree_toggle_the_bar_still_fits_the_narrowest_window(string branch, int behind)
    {
        var vm = NewWorktreeVm(worktree: @"D:\repo-worktree");
        VmTopology.SeedCycle(vm, [.. Enumerable.Range(0, 150).Select(i => $@"C:\p\project{i}.csproj")]);
        vm.OnEvent(new BranchListEvent([new BranchRef(branch, "aaa", IsActive: true, IsRemoteTracking: false)]));
        vm.OnEvent(new SyncCompletedEvent(branch, "b7e91d4", FetchDegraded: false, 150, 0, Behind: behind));
        var (bar, window) = Realize(vm, atMinimumWindowWidth: true);
        bar.Width = (double)bar.FindResource("Size.WindowMinWidth"); // bkz. Restart engine testinin gerekçesi
        bar.UpdateLayout();

        Assert.Equal(Visibility.Visible, bar.WorktreeToggle.Visibility); // ön-koşul: anahtar yer kaplıyor
        Assert.Equal(bar.Width, bar.ActualWidth);
        Assert.Equal(Visibility.Visible, bar.WarnChip.Visibility);
        var rightGroup = (FrameworkElement)((FrameworkElement)bar.Split.Parent).Parent;
        var leftGroup = (Panel)((FrameworkElement)bar.SyncButton.Parent).Parent;
        var counters = (Panel)leftGroup.Children[^1];
        var lastChip = counters.Children.Cast<FrameworkElement>().Last(c => c.Visibility == Visibility.Visible);
        double leftEnd = lastChip.TransformToAncestor(bar).Transform(new Point(lastChip.ActualWidth, 0)).X;
        double rightStart = rightGroup.TransformToAncestor(bar).Transform(new Point(0, 0)).X;
        Assert.True(leftEnd <= rightStart, $"the counter chips end at {leftEnd:0.#} but the right group starts at {rightStart:0.#}");
        GC.KeepAlive(window);
    }
}
