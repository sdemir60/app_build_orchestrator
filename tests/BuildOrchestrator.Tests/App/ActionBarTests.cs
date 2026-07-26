using System.Windows;
using System.Windows.Controls.Primitives;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [D6/T40+T12+T43-UI] Alt aksiyon barı: sayaç chip'leri (toggle/Σ-clear), Build split-button menüsü
/// (koşullu maddeler + F5 rozetinin yeri), mid-run kilidi (branch/worktree/config sönük, perf canlı) ve
/// K3 branch seçimi (worktree zorlama + niyet satırı, git switch DEĞİL) + worktree auto-ad üretimi.
/// Saf VM mantığı (SelectBranch/AutoWorktreeName) WPF'siz <see cref="FactAttribute"/> ile; görünüm kablajı
/// <see cref="StaFactAttribute"/> ile GERÇEK ActionBar kurulup sürülerek pinlenir.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public partial class ActionBarTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    private static ProjectNode Node(string id, string name, int buildOrder) =>
        new(id, name, id, ["Osys"], [], buildOrder, null, null, false, null);

    // ---------------------------------------------------------------- [K3] branch seçimi (saf VM)

    [Fact]
    public void Selecting_a_non_active_branch_forces_worktree_on_and_writes_the_intent_line_not_git_switch()
    {
        var vm = NewVm();
        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", 0)], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0)); // Idle
        vm.OnEvent(new BranchListEvent([
            new BranchRef("main", "aaaaaaaaaaaa", true, false),
            new BranchRef("feature/x", "bbbbbbbccccc", false, true),
        ]));
        vm.Branch = "main";

        // [D6 fix-wave] Satırı ÖNCE terminal (Succeeded) bir duruma sürükle — SelectBranch'in reset döngüsünün
        // GERÇEKTEN çalıştığını kanıtlamak için (aksi halde satır zaten Pending'den geldiği için Assert.All
        // hiçbir şey ispatlamaz). SelectBranch RunId/IsRunning kontrolü yapmaz, bu yüzden run BAŞLATMAYA gerek yok.
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 100));
        Assert.Equal(ProjectRowState.Succeeded, vm.Projects.Single().State); // ön-koşul: satır gerçekten terminal

        vm.SelectBranch(new BranchRef("feature/x", "bbbbbbbccccc", false, true));

        Assert.Equal("feature/x", vm.Branch);
        Assert.True(vm.UseWorktree);      // aktif-olmayan branch → worktree ZORUNLU ON
        Assert.True(vm.IsWorktreeForced);
        Assert.Equal(AppPhase.Boot, vm.Phase);
        Assert.Equal(ProjectRowState.Pending, vm.Projects.Single().State); // reset döngüsü GERÇEKTEN çalıştı (Succeeded→Pending)

        string doc = vm.GetRunDocumentText();
        Assert.Contains("branch target: feature/x (bbbbbbb) — worktree will be used at Build", doc);
        Assert.Contains("Branch changed: feature/x — Sync required", doc);
        Assert.DoesNotContain("git switch", doc);   // [K3] prototipten SAPMA: git switch/--detach YAZILMAZ
        Assert.DoesNotContain("--detach", doc);
    }

    [Fact]
    public void Selecting_the_active_branch_only_sets_branch_without_forcing_worktree_or_reset()
    {
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent([new BranchRef("main", "aaaaaaaaaaaa", true, false)]));
        vm.Branch = "feature/x"; // önceden aktif-olmayan seçilmiş gibi
        vm.UseWorktree = false;

        vm.SelectBranch(new BranchRef("main", "aaaaaaaaaaaa", true, false));

        Assert.Equal("main", vm.Branch);
        Assert.False(vm.UseWorktree);        // aktif branch → worktree zorlaması YOK
        Assert.False(vm.IsWorktreeForced);
        Assert.DoesNotContain("Sync required", vm.GetRunDocumentText()); // niyet satırı yazılmaz
    }

    // ---------------------------------------------------------------- worktree auto-ad (saf VM)

    [Fact]
    public void Auto_worktree_name_slugs_slashes_and_increments_the_suffix()
    {
        var worktrees = new List<Worktree>
        {
            new("feature-foo-1", "feature/foo", @"D:\wt\feature-foo-1", false, null),
            new("main-1", "main", @"D:\wt\main-1", false, null),
        };

        Assert.Equal("feature-foo-2", RunViewModel.AutoWorktreeName("feature/foo", worktrees)); // slug + (mevcut 1)+1
        Assert.Equal("main-2", RunViewModel.AutoWorktreeName("main", worktrees));
        Assert.Equal("release-hotfix-1", RunViewModel.AutoWorktreeName("release/hotfix", worktrees)); // eşleşen yok → 1
        // [D6 fix-wave] çok-slash'lı branch: Replace('/','-') TÜM slash'ları değiştirmeli (yalnız ilkini DEĞİL).
        Assert.Equal("feature-foo-bar-1", RunViewModel.AutoWorktreeName("feature/foo/bar", worktrees));
    }

    // ---------------------------------------------------------------- görünüm kablajı (GERÇEK ActionBar/BuildMenu)

    private static (ActionBar bar, Window window) Realize(RunViewModel vm)
    {
        var host = DsResources.NewHost();
        var bar = new ActionBar { DataContext = vm };
        return (bar, DsResources.Realize(host, bar));
    }

    private static (BuildMenu menu, Window window) RealizeMenu(RunViewModel vm)
    {
        var host = DsResources.NewHost();
        var menu = new BuildMenu { DataContext = vm };
        return (menu, DsResources.Realize(host, menu));
    }

    private static void Click(ButtonBase button) =>
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    [StaFact]
    public void Counter_chips_toggle_the_filter_and_sigma_always_clears_it()
    {
        var vm = NewVm(); // RootPath dolu → HasWorkspace → chip'ler etkin
        var (bar, window) = Realize(vm);

        Assert.Null(vm.ActiveFilter);
        Click(bar.FailedChip);
        Assert.Equal(ProjectFilter.Failed, vm.ActiveFilter);
        Click(bar.FailedChip);                                  // aynı chip'e ikinci tık → temizle
        Assert.Null(vm.ActiveFilter);

        Click(bar.SucceededChip);
        Assert.Equal(ProjectFilter.Succeeded, vm.ActiveFilter);
        Click(bar.BuildingChip);                                // farklı chip → devral
        Assert.Equal(ProjectFilter.Building, vm.ActiveFilter);

        Click(bar.SigmaChip);                                   // Σ HER ZAMAN temizler
        Assert.Null(vm.ActiveFilter);
        Assert.False(bar.SigmaChip.IsChecked);                  // Σ hiç aktif olmaz
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Sigma_chip_click_with_no_active_filter_still_ends_up_unchecked()
    {
        // [D6 fix-wave] ActiveFilter zaten null iken Σ'ya tık: ToggleFilter(null) no-op'tur (null→null), bu
        // yüzden PropertyChanged YAYINLAMAZ (CommunityToolkit eşitlik kontrolü) → RefreshChips ÇALIŞMAZ.
        // WPF'in native ToggleButton.OnClick'i (OnToggle → IsChecked=true, SONRA Click event) burada
        // RaiseEvent ile bypass edildiğinden, native davranışı simüle etmek için tık ÖNCESİ IsChecked
        // elle true'ya çekilir — gerçek bir tıkta olacağı gibi.
        var vm = NewVm();
        var (bar, window) = Realize(vm);

        Assert.Null(vm.ActiveFilter);       // hiçbir filtre aktif değilken başla
        bar.SigmaChip.IsChecked = true;     // native toggle-on-click'i simüle et
        Click(bar.SigmaChip);

        Assert.Null(vm.ActiveFilter);       // ToggleFilter(null) no-op kalır
        Assert.False(bar.SigmaChip.IsChecked); // Σ HER ZAMAN unchecked bitmeli — amber'da takılı kalmamalı
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Build_menu_shows_continue_only_when_stopped_and_retry_only_when_something_failed()
    {
        var vm = NewVm();
        var (menu, window) = RealizeMenu(vm);

        // Idle, hiç failure yok: Continue YOK, Retry YOK; Build + Rebuild VAR.
        Assert.DoesNotContain(menu.Items, i => i.Kind == "continue");
        Assert.DoesNotContain(menu.Items, i => i.Kind == "retry");
        Assert.Contains(menu.Items, i => i.Kind == "build");
        Assert.Contains(menu.Items, i => i.Kind == "rebuild");

        // Bir failure → Retry görünür.
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectFailedEvent("r1", @"C:\p\a.csproj", 100, "exit 1"));
        Assert.Contains(menu.Items, i => i.Kind == "retry");

        // Stop → Continue görünür.
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 0, 0, 0, 0, 0));
        Assert.Equal(AppPhase.Stopped, vm.Phase);
        Assert.Contains(menu.Items, i => i.Kind == "continue");
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Stopped_state_moves_the_F5_badge_from_build_to_continue()
    {
        var vm = NewVm();
        var (menu, window) = RealizeMenu(vm);

        // NOT-stopped: F5 rozeti Build'de; Continue maddesi yok.
        Assert.DoesNotContain(menu.Items, i => i.Kind == "continue");
        Assert.Equal("F5", menu.Items.Single(i => i.Kind == "build").Kbd);

        // Stopped: F5 Continue'ya taşınır, Build'in rozeti KALDIRILIR.
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 0, 0, 0, 0, 0));
        Assert.Equal(AppPhase.Stopped, vm.Phase);

        Assert.Equal("F5", menu.Items.Single(i => i.Kind == "continue").Kbd);
        Assert.Null(menu.Items.Single(i => i.Kind == "build").Kbd);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Branch_worktree_and_configuration_are_disabled_while_running_but_perf_is_not()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm);

        // Idle: hepsi etkin.
        Assert.True(bar.BranchChip.IsEnabled);
        Assert.True(bar.WorktreeChip.IsEnabled);
        Assert.True(bar.Segment.IsEnabled);
        Assert.True(bar.PerfChip.IsEnabled);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        Assert.True(vm.IsRunning);

        Assert.False(bar.BranchChip.IsEnabled);   // T12: branch/worktree/config KİLİTLİ
        Assert.False(bar.WorktreeChip.IsEnabled);
        Assert.False(bar.Segment.IsEnabled);
        Assert.True(bar.PerfChip.IsEnabled);       // perf CANLI kalır
        GC.KeepAlive(window);
    }

    // [Fix round 1 — KÖK 5] K11'in TEK kullanıcı giriş noktası: perf chip'i. Kablaj bu iterasyonda değişti
    // (`_vm?.CyclePerf()` → `_ = _vm?.CyclePerfAsync()`, fire-and-forget) ve görsel pas en sona ertelendiği
    // için (It-4b dersi c6e9a21) bu testten başka güvenlik ağı YOK. Tıklamanın SENKRON kısmı (PerfMode +
    // Parallelism güncellemesi, chip'in momentary kalması) burada pinlenir — gönderim/IPC yarısı VM
    // testlerindedir (RunViewModelStateTests), bu yüzden burada pump/bekleme GEREKMEZ.
    //
    // [Fix round 2 — YENİ 2] `Click` yardımcısı ClickEvent'i DOĞRUDAN raise eder ve WPF'in native
    // ToggleButton.OnClick→OnToggle yolunu BYPASS eder; bu yüzden IsChecked tıktan önce de sonra da false
    // olurdu ve "momentary" iddiası VACUOUS kalırdı (handler'daki `IsChecked = false` silinse test yeşil
    // kalırdı). Native toggle bu yüzden ELLE simüle edilir — SigmaChip testinin (:148-155) deseni.
    [StaFact]
    public void Perf_chip_click_cycles_the_profile_and_stays_momentary()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm);

        Assert.Equal("Balanced", vm.PerfMode);
        Assert.Equal(PerfProfile.For(PerfMode.Balanced).Parallelism, vm.Parallelism);

        bar.PerfChip.IsChecked = true; // native toggle (ClickEvent'i elle raise etmek bunu yapmaz)
        Click(bar.PerfChip);
        Assert.Equal("Light", vm.PerfMode);
        Assert.Equal(PerfProfile.For(PerfMode.Light).Parallelism, vm.Parallelism);
        Assert.False(bar.PerfChip.IsChecked); // momentary: chip basılı KALMAZ

        bar.PerfChip.IsChecked = true;
        Click(bar.PerfChip);
        Assert.Equal("Full", vm.PerfMode);
        Assert.Equal(PerfProfile.For(PerfMode.Full).Parallelism, vm.Parallelism);
        Assert.False(bar.PerfChip.IsChecked);
        GC.KeepAlive(window);
    }
}
