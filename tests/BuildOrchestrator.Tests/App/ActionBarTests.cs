using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
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

    /// <summary>[A13/T3c · c4] design-v1 BuildApp.jsx:1544-1545: <c>background: var(--surface)</c>,
    /// <c>borderTop: 1px solid var(--border)</c>. Yükseklik zaten pinliydi (DesignTokenScaleTests); zemin/üst
    /// çizgi testsizdi — root Border başka bir fırçaya bağlansa süit yeşil kalırdı.</summary>
    [StaFact]
    public void The_action_bar_root_is_surface_with_a_border_top_line()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm);

        var root = Assert.IsType<Border>(bar.Content);
        Assert.Same(bar.FindResource("Brush.Surface"), root.Background);
        Assert.Same(bar.FindResource("Brush.Border"), root.BorderBrush);
        Assert.Equal(new Thickness(0, 1, 0, 0), root.BorderThickness);
        GC.KeepAlive(window);
    }

    /// <summary>[A13/T3c · c5] design-v1 BuildApp.jsx:1546-1614 sırası: Sync · ayraç · 6 sayaç chip'i (Σ/building/
    /// succeeded/failed/skipped/dep) · … · branch · worktree · Debug|Release · perf · ayraç · Stop/Build. Hiçbir
    /// test bu SIRAYI assert etmiyordu — chip'ler kod-tarafı kurulduğu için ("BuildCounterChips") sıra sessizce
    /// kayabilirdi.
    /// <para><b>[DEĞİŞEN KURAL — design v1.7.0 §2.7-2]</b> Sol grup Sync'ten HEMEN SONRA <b>bakım kutusunu</b>
    /// taşır (ayraçtan ÖNCE). Eski iddia "Sync'ten sonra etiketli <c>Cycles</c> düğmesi gelir" idi; o düğme
    /// kaldırıldı ve işi kutunun üçüncü ikonu (unlink) devraldı. Gerekçe: Clean/Optimize/Resolve üçü de
    /// derleme ÖNCESİ hazırlık işleridir ve tasarım bunları tek kutuda toplar; üç etiketli düğme ayrıca barı
    /// 1240px minimumda taşırıyordu. Yer seçimi hâlâ anlamlıdır: ayracın öbür yanı sayaçlarındır ve kutu
    /// Build'in yanına KONMADI — orası birincil aksiyonun yeridir.</para></summary>
    [StaFact]
    public void The_left_group_orders_sync_then_the_maintenance_box_then_a_separator_then_the_six_counter_chips()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm);

        var leftGroup = Assert.IsType<StackPanel>(bar.SyncButton.Parent);
        var leftChildren = leftGroup.Children.Cast<UIElement>().ToList();
        Assert.Equal(4, leftChildren.Count);
        Assert.Same(bar.SyncButton, leftChildren[0]);
        Assert.Same(bar.MaintenanceBoxControl, leftChildren[1]);
        var leftSeparator = Assert.IsType<Border>(leftChildren[2]);
        Assert.Same(bar.FindResource("Brush.BorderSubtle"), leftSeparator.Background);
        var counterStrip = Assert.IsType<StackPanel>(leftChildren[3]);

        var chipOrder = counterStrip.Children.Cast<UIElement>().ToList();
        Assert.Equal(
            new UIElement[] { bar.SigmaChip, bar.BuildingChip, bar.SucceededChip, bar.FailedChip, bar.SkippedChip, bar.DepChip },
            chipOrder);
        GC.KeepAlive(window);
    }

    /// <summary>[A13/T3c · c5] Sağ grubun sırası: branch · worktree · Debug|Release · perf · ayraç · Stop/Build
    /// grid'i (BuildApp.jsx:1570-1614).</summary>
    [StaFact]
    public void The_right_group_orders_branch_worktree_config_perf_a_separator_then_the_build_area()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm);

        var rightGroup = Assert.IsType<StackPanel>(bar.Segment.Parent);
        var rightChildren = rightGroup.Children.Cast<UIElement>().ToList();
        Assert.Equal(6, rightChildren.Count);
        Assert.Same(bar.BranchChip, ((Grid)rightChildren[0]).Children.Cast<UIElement>().First());
        Assert.Same(bar.WorktreeChip, ((Grid)rightChildren[1]).Children.Cast<UIElement>().First());
        Assert.Same(bar.Segment, rightChildren[2]);
        Assert.Same(bar.PerfChip, rightChildren[3]);
        var rightSeparator = Assert.IsType<Border>(rightChildren[4]);
        Assert.Same(bar.FindResource("Brush.BorderSubtle"), rightSeparator.Background);
        var buildArea = Assert.IsType<Grid>(rightChildren[5]);
        Assert.Contains(bar.StopButton, buildArea.Children.Cast<UIElement>());
        Assert.Contains(bar.Split, buildArea.Children.Cast<UIElement>());
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [A13/T4 · m6] popover 8px boşluk

    /// <summary>[A13/T4 · m6 · fix-1 · D5] design-v1 prototipi (<c>BuildApp.jsx:821</c> <c>bottom: 'calc(100% +
    /// 8px)'</c>) — branch/worktree popover'ları anchor'larının 8px ÜSTÜNDE açılır. WPF karşılığı
    /// <c>Placement="Top"</c> + <c>VerticalOffset="-8"</c> (yukarı = negatif) + <c>PlacementTarget</c>'ın GERÇEKTEN
    /// chip'e bağlı olması (aksi halde offset doğru olsa da popover yanlış öğenin üstünde açılır — <c>ActionBar.xaml
    /// :26-27,:39-40</c>'ın <c>PlacementTarget="{Binding ElementName=PART_BranchChip}"</c> bağının runtime karşılığı,
    /// fix-1'de eklendi). Bir XAML değişikliği (ör. -8 → -4 ya da binding kopması) burada KIRMIZI verir, saf metin
    /// taraması vermez.</summary>
    [StaFact]
    public void The_branch_and_worktree_popovers_open_eight_pixels_above_their_chip()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm);

        Assert.Equal(PlacementMode.Top, bar.BranchPopup.Placement);
        Assert.Equal(-8.0, bar.BranchPopup.VerticalOffset);
        Assert.Same(bar.BranchChip, bar.BranchPopup.PlacementTarget); // [fix-1 · D5] doğru chip'in ÜSTÜNDE açılır
        Assert.Equal(PlacementMode.Top, bar.WorktreePopup.Placement);
        Assert.Equal(-8.0, bar.WorktreePopup.VerticalOffset);
        Assert.Same(bar.WorktreeChip, bar.WorktreePopup.PlacementTarget);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [A13/T4 · n4] perf/Build tooltip YOK

    /// <summary>[A13/T4 · n4 · fix-1 · B2] Bilinçli KARARLAR listesi (design-v1 README §8: <i>"Toast/popup yok ·
    /// 'View failures' butonu yok · <b>perf/Build tooltip'i yok</b> · katman eşleşme sayacı yok."</i>) + §2.7 madde
    /// 8: <i>"perf: Balanced chip — tıkla döngü ... <b>Tooltip YOK (istenmedi)</b>."</i> — sayaç chip'lerinin
    /// AKSİNE (<see cref="ActionBar.AddCounterChip"/> her birine <c>ToolTip = label</c> atar), perf chip'i ve Build
    /// split-button'ı (otoritenin İKİNCİ yarısı — önceden testsizdi) BİLE BİLE tooltipsiz bırakılmıştır.
    ///
    /// <para><b>fix-1 · B2:</b> pozitif kontrol eklendi — <c>SigmaChip.ToolTip</c>'in dolu olduğu ÖNCE assert
    /// edilir (chip kurulum yolunun GERÇEKTEN koştuğunun kanıtı); bu olmadan <c>PerfChip</c>/<c>Split</c> hiç
    /// kurulmasa da iki <c>Null</c> assert'i vakumda yeşil kalırdı.</para></summary>
    [StaFact]
    public void The_perf_chip_and_build_button_carry_no_tooltip_by_design()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm);

        Assert.NotNull(bar.SigmaChip.ToolTip); // ön-koşul: chip kurulum yolu GERÇEKTEN koştu (vakum değil)
        Assert.Null(bar.PerfChip.ToolTip);
        Assert.Null(bar.Split.ToolTip); // [fix-1 · B2] otoritenin "Build" yarısı — önceden pinsizdi
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [A13/T3c · c6] chip glyph'leri

    /// <summary>Bir sayaç chip'inin İLK çocuğu (StackPanel[icon, value]) — chip glyph'ini okumanın TEK yolu.</summary>
    private static UIElement ChipIcon(ToggleButton chip) => ((StackPanel)chip.Content).Children[0];

    /// <summary>Bir sayaç chip'inin İKİNCİ çocuğu (StackPanel[icon, value]) — <see cref="ChipIcon"/>'ın simetriği,
    /// değer <see cref="TextBlock"/>'unu okumanın TEK yolu.</summary>
    private static TextBlock ChipValue(ToggleButton chip) => (TextBlock)((StackPanel)chip.Content).Children[1];

    /// <summary>[A13/T4 · n6 · fix-1 · B3/C3] design-v1 README:48 "DAİMA tabular rakam" — sayaç chip değeri
    /// (<c>ActionBar.xaml.cs:258 CounterValue</c>) mono taşıyan altı üretim yerinden biridir. Envanter/kapsam
    /// kararı XML doc'u: <see cref="ProjectRowTests.The_project_row_sha_and_duration_columns_are_tabular"/>.</summary>
    [StaFact]
    public void The_sigma_chip_value_is_tabular()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm);

        Assert.Equal(System.Windows.FontNumeralAlignment.Tabular,
            System.Windows.Documents.Typography.GetNumeralAlignment(ChipValue(bar.SigmaChip)));
        GC.KeepAlive(window);
    }

    /// <summary>[A13/T3c · c6.2] BuildApp.jsx:1550 Σ chip'i <c>I.sigma</c> ikonunu taşır — <c>ActionBar.BuildCounterChips</c>
    /// hangi ikonu bağladığını hiçbir test doğrulamıyordu (Icon.Sigma yerine başka bir anahtar verilse süit yeşil kalırdı).</summary>
    [StaFact]
    public void The_sigma_chip_paints_the_sigma_glyph()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm);

        var canvas = Assert.IsType<Canvas>(Assert.IsType<Viewbox>(ChipIcon(bar.SigmaChip)).Child);
        var path = Assert.IsType<System.Windows.Shapes.Path>(canvas.Children[0]);
        Assert.Same(bar.FindResource("Icon.Sigma"), path.Data);
        GC.KeepAlive(window);
    }

    /// <summary>[A13/T3c · c6.1+c6.2] BuildApp.jsx:1551-1565 chip KÜMESİ (bu SIRAYLA: building/succeeded/failed/
    /// skipped/dep) ve HER birinin glyph türü. Building = spinner+nokta çifti (BuildApp.jsx:1553); succeeded/
    /// failed/skipped = <see cref="StatusGlyph"/> (DS.StatusGlyph status=…); dep = ▲ üçgeni (Icon.AlertTri).</summary>
    [StaFact]
    public void Each_counter_chip_after_sigma_paints_its_own_designated_glyph()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm);

        var buildingIcon = Assert.IsType<Grid>(ChipIcon(bar.BuildingChip));
        Assert.IsType<Ellipse>(buildingIcon.Children[0]);
        Assert.IsType<BuildingSpinner>(buildingIcon.Children[1]);

        Assert.Equal(GraphStatus.Succeeded, Assert.IsType<StatusGlyph>(ChipIcon(bar.SucceededChip)).Status);
        Assert.Equal(GraphStatus.Failed, Assert.IsType<StatusGlyph>(ChipIcon(bar.FailedChip)).Status);
        Assert.Equal(GraphStatus.Skipped, Assert.IsType<StatusGlyph>(ChipIcon(bar.SkippedChip)).Status);

        var depCanvas = Assert.IsType<Canvas>(Assert.IsType<Viewbox>(ChipIcon(bar.DepChip)).Child);
        var depPath = Assert.IsType<System.Windows.Shapes.Path>(depCanvas.Children[0]);
        Assert.Same(bar.FindResource("Icon.AlertTri"), depPath.Data);
        GC.KeepAlive(window);
    }

    /// <summary>[A13/T3 fix-1 · B1] BuildApp.jsx:1552-1553 building chip'inin glyph'i KOŞULLUdur:
    /// <c>icon={c.building ? &lt;BuildingSpin size={12}/&gt; : &lt;span 8×8 daire background:'var(--neutral-600)'&gt;}</c>
    /// (README §2.7: <i>"spinner+4 (building; boşken gri nokta)"</i>). Üretimde kural
    /// <c>ActionBar.RefreshChips</c>'te yaşar; T3c yalnız İKİ ÇOCUĞUN VARLIĞINI okuyordu — iki satır tersine
    /// çevrilse (boşken spinner, koşarken nokta) süit YEŞİL kalırdı.
    ///
    /// <para>Takas <b>iki yönlü</b> ve ÜRETİM YOLUNDAN sürülür (brief kural 3): gerçek
    /// <see cref="RunStartedEvent"/>/<see cref="ProjectStartedEvent"/>/<see cref="ProjectSucceededEvent"/>
    /// zinciri <see cref="RunViewModel.Counters"/>'ı GERÇEKTEN değiştirir; <c>RefreshChips</c> doğrudan
    /// çağrılmaz. Nokta rengi de pinli: <c>--dot-clean = var(--neutral-600) = #3a3a42 = Brush.DotClean</c>
    /// (colors.css:39).</para></summary>
    [StaFact]
    public void The_building_chip_swaps_its_grey_dot_for_the_spinner_only_while_a_project_is_building()
    {
        var vm = NewVm();
        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", 0)], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0)); // → Idle
        var (bar, window) = Realize(vm);

        var icon = Assert.IsType<Grid>(ChipIcon(bar.BuildingChip));
        var dot = Assert.IsType<Ellipse>(icon.Children[0]);
        var spinner = Assert.IsType<BuildingSpinner>(icon.Children[1]);

        // Boşken: gri nokta görünür, spinner gizli.
        Assert.Equal(0, vm.Counters.Building); // ön-koşul: gerçekten kimse derlenmiyor
        Assert.Equal(Visibility.Visible, dot.Visibility);
        Assert.Equal(Visibility.Collapsed, spinner.Visibility);
        Assert.Same(bar.FindResource("Brush.DotClean"), dot.Fill);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));

        // Koşarken TAKAS: spinner görünür, nokta gizli.
        Assert.Equal(1, vm.Counters.Building); // ön-koşul: sayaç GERÇEKTEN arttı
        Assert.Equal(Visibility.Visible, spinner.Visibility);
        Assert.Equal(Visibility.Collapsed, dot.Visibility);

        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 100));

        // Bitince geri döner (tek yönlü bir latch DEĞİL).
        Assert.Equal(0, vm.Counters.Building);
        Assert.Equal(Visibility.Visible, dot.Visibility);
        Assert.Equal(Visibility.Collapsed, spinner.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>[A13/T3c · c6.3] BuildApp.jsx:1566 <c>color: di ? 'var(--status-fail-text)' : 'var(--text-faint)'</c>
    /// — ▲'nin kırmızıya dönmesi İKİ YÖNLÜDÜR (0'da faint, &gt;0'da kırmızı) ve ÜRETİM YOLUNDAN (gerçek bir proje
    /// succeeded + depIssue taşıyarak <see cref="RunViewModel.Counters"/>'ı GERÇEKTEN artırarak) tetiklenir —
    /// alanı doğrudan set etmek/metodu çağırmak brief kural 6'yı ihlal ederdi.</summary>
    [StaFact]
    public void The_dep_triangle_turns_red_only_once_a_project_succeeds_with_a_real_dep_issue()
    {
        var vm = NewVm();
        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", 0)], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0)); // → Idle
        var (bar, window) = Realize(vm);

        var depCanvas = Assert.IsType<Canvas>(Assert.IsType<Viewbox>(ChipIcon(bar.DepChip)).Child);
        var depPath = Assert.IsType<System.Windows.Shapes.Path>(depCanvas.Children[0]);

        // ön-koşul: henüz kimse succeeded değil → DepAffected == 0 → faint.
        Assert.Equal(0, vm.Counters.DepAffected);
        Assert.Same(bar.FindResource("Brush.TextFaint"), depPath.Stroke);

        // ÜRETİM YOLU: RunStarted → ProjectStarted → ProjectSucceeded(DepIssues: [...]) — RunCounters.From
        // yalnız succeeded+HasDepIssue satırları sayar (RunCounters.cs).
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 100, ["dependent B henüz derlenmedi"]));

        Assert.Equal(1, vm.Counters.DepAffected); // ön-koşul: sayaç GERÇEKTEN arttı
        Assert.Same(bar.FindResource("Brush.StatusFailText"), depPath.Stroke);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [A13/T3b · b1] popover kabukları

    /// <summary>[A13/T3b · b1] design-v1 README §2.8: <c>"Branch (272px)"</c> / <c>"Worktree (300px)"</c> —
    /// OTORİTE LİTERALLERİ. Ölçülen, ActionBar'ın KENDİ sarmalayıcı Border'ıdır (<c>Ds.Popover</c> stilli,
    /// <c>Width="272"</c>/<c>"300"</c>, ActionBar.xaml) — <see cref="BranchPopover"/>/<see cref="WorktreePopover"/>
    /// kontrollerinin kendi genişliği DEĞİL (bu ikisi FARKLI kavramlardır).
    ///
    /// <para>[fix-1 · B7] Test <c>PopoverTests</c>'ten BURAYA taşındı (kalem ActionBar'ındır) ve oradaki inline
    /// <see cref="Realize"/> kopyası silindi. [fix-1 · C8] Açılan her popup KAPATILIR: <c>StaysOpen="False"</c> +
    /// <c>AllowsTransparency="True"</c> bir Popup, kapatılmadan bırakılırsa STA thread'inde canlı bir HWND olarak
    /// asılı kalır. [fix-1 · C11] Ata yürüyüşü <see cref="DsResources.Ancestors"/>'a çıkarıldı.</para></summary>
    [StaFact]
    public void Action_bar_wraps_the_branch_and_worktree_popovers_in_design_v1s_272_and_300_pixel_shells()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm);

        var branchBorder = PopoverShellBorder(bar.BranchPopoverControl);
        var worktreeBorder = PopoverShellBorder(bar.WorktreePopoverControl);
        Assert.Equal(272.0, branchBorder.Width);
        Assert.Equal(300.0, worktreeBorder.Width);

        // Realize zorunlu (kural 5): Popup içeriği yalnız IsOpen=true iken ölçülüp yerleşir — gerçek açılış
        // olmadan ActualWidth hep 0 kalırdı (bu yüzden literal DP okumak TEK BAŞINA yetmezdi).
        bar.BranchChip.IsChecked = true;
        DispatcherPump.PumpUntil(() => branchBorder.ActualWidth > 0, TimeSpan.FromSeconds(2));
        Assert.Equal(272.0, branchBorder.ActualWidth);
        bar.BranchChip.IsChecked = false;

        bar.WorktreeChip.IsChecked = true;
        DispatcherPump.PumpUntil(() => worktreeBorder.ActualWidth > 0, TimeSpan.FromSeconds(2));
        Assert.Equal(300.0, worktreeBorder.ActualWidth);
        bar.WorktreeChip.IsChecked = false; // simetri: açılan popup kapatılır (fix-1 · C8)

        GC.KeepAlive(window);
    }

    private static Border PopoverShellBorder(FrameworkElement inner) =>
        DsResources.Ancestors(inner).OfType<Border>().FirstOrDefault()
        ?? throw new InvalidOperationException("popover'ı saran Ds.Popover Border'ı bulunamadı");

    // ---------------------------------------------------------------- [A13/T3c · c7] Stop takası

    /// <summary>[A13/T3c · c7] BuildApp.jsx:1584-1614: <c>running ? &lt;Stop/&gt; : &lt;split-button/&gt;</c>.
    /// AccessibilityTests yalnız Stop'un UIA adını pinliyordu — görünürlük TAKASININ KENDİSİ (Idle→running→
    /// tamamlandı) hiç sürülmemişti. Tetik ÜRETİM YOLU: <see cref="RunStartedEvent"/>/<see cref="RunCompletedEvent"/>
    /// (brief kural 6) — VM'in <see cref="RunViewModel.IsMidRunLocked"/>'ını doğrudan set etmek YOK.</summary>
    [StaFact]
    public void The_stop_button_and_the_build_split_button_swap_visibility_across_a_real_run()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm);

        // Idle: split-button görünür, Stop gizli.
        Assert.Equal(Visibility.Visible, bar.Split.Visibility);
        Assert.Equal(Visibility.Collapsed, bar.StopButton.Visibility);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        Assert.True(vm.IsRunning); // ön-koşul: gerçekten koşuyor

        Assert.Equal(Visibility.Visible, bar.StopButton.Visibility);
        Assert.Equal(Visibility.Collapsed, bar.Split.Visibility);

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 0));
        Assert.False(vm.IsRunning); // ön-koşul: gerçekten bitti

        Assert.Equal(Visibility.Visible, bar.Split.Visibility);
        Assert.Equal(Visibility.Collapsed, bar.StopButton.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>[Stopping] Graceful stop uçuştaki child'ların bitmesini bekler. O pencerede buton
    /// <b>görünür kalır</b> (split-button geri gelirse kullanıcı hâlâ koşan bir run'a Build/Continue
    /// sunulmuş olurdu), etiketi "Stopping…" olur ve <c>StopCommand</c> pasifleştiği için buton disable
    /// olur — ikinci bir tıklama ikinci bir stopRun üretmez. Faz doğrudan set edilir: buraya NASIL
    /// girildiği (StopCommand → gerçek Supervisor) kardeş süitte pinli, burada sürülen GÖRÜNÜM.</summary>
    [StaFact]
    public void The_stop_button_reads_stopping_and_goes_disabled_while_the_run_drains()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        Assert.Equal("Stop", StopLabel(bar));      // ön-koşul
        Assert.True(bar.StopButton.IsEnabled);

        vm.Phase = AppPhase.Stopping;

        Assert.Equal(Visibility.Visible, bar.StopButton.Visibility);
        Assert.Equal(Visibility.Collapsed, bar.Split.Visibility);
        Assert.Equal("Stopping…", StopLabel(bar));
        Assert.False(bar.StopButton.IsEnabled);
        GC.KeepAlive(window);
    }

    private static string StopLabel(ActionBar bar) =>
        ((StackPanel)bar.StopButton.Content).Children.OfType<TextBlock>().Single().Text;

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

    /// <summary>Eski iddia: "stopped fazında menüde <c>continue</c> maddesi BELİRİR". Continue yüzeyi
    /// kaldırıldı — Stop'tan sonra kullanıcı Build'e basar ve run baştan koşar (öldürülen projelerin kaydı
    /// geçersizleştiği için onlar da yeniden derlenir). Bu test artık <c>continue</c>'nun HİÇBİR fazda
    /// üretilmediğini pinler; Retry'ın koşullu davranışı aynen korunur.</summary>
    [StaFact]
    public void Build_menu_never_offers_continue_and_shows_retry_only_when_something_failed()
    {
        var vm = NewVm();
        var (menu, window) = RealizeMenu(vm);

        // Idle, hiç failure yok: Retry YOK; Build + Rebuild VAR.
        Assert.DoesNotContain(menu.Items, i => i.Kind == "continue");
        Assert.DoesNotContain(menu.Items, i => i.Kind == "retry");
        Assert.Contains(menu.Items, i => i.Kind == "build");
        Assert.Contains(menu.Items, i => i.Kind == "rebuild");

        // Bir failure → Retry görünür.
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));
        vm.OnEvent(new ProjectFailedEvent("r1", @"C:\p\a.csproj", 100, "exit 1"));
        Assert.Contains(menu.Items, i => i.Kind == "retry");

        // Stop → yine Continue YOK.
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 0, 0, 0, 0, 0));
        Assert.Equal(AppPhase.Stopped, vm.Phase);
        Assert.DoesNotContain(menu.Items, i => i.Kind == "continue");
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [A13/T3a · a5] kopya metinleri (BİREBİR)

    /// <summary>[A13/T3a · a5] BuildMenu.xaml.cs:82,84 açıklamaları — Kind+Kbd zaten pinliydi (bkz. yukarıdaki
    /// testler), kopya metni testsizdi: <c>Only changed projects</c> / stopped varyantı <c>Start over — only
    /// changed projects</c> / <c>All {n} projects — cache ignored</c> (design-v1 §2.7).</summary>
    [StaFact]
    public void Build_menu_desc_texts_are_verbatim_for_build_and_rebuild()
    {
        var vm = NewVm();
        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1), Node(@"C:\p\c.csproj", "C", 2)],
            [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 3, 0)); // → Idle
        var (menu, window) = RealizeMenu(vm);

        Assert.Equal("Only changed projects", menu.Items.Single(i => i.Kind == "build").Desc);
        Assert.Equal("All 3 projects — cache ignored", menu.Items.Single(i => i.Kind == "rebuild").Desc);

        // stopped → Build'in açıklaması "Start over" önekini alır (BuildMenu.ComposeItems).
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 3, "Debug", 0));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 0, 0, 0, 0, 0));
        Assert.Equal(AppPhase.Stopped, vm.Phase);
        Assert.Equal("Start over — only changed projects", menu.Items.Single(i => i.Kind == "build").Desc);

        GC.KeepAlive(window);
    }

    /// <summary>Eski iddia: "stopped fazında F5 rozeti Build'den Continue'ya TAŞINIR". Continue kalktığı için
    /// taşınacak yer de kalmadı — rozet Build'de KALIR ve F5 her fazda aynı şeyi yapar (koşuyorsa Stop, aksi
    /// halde Build). Kardeşi: <c>KeyboardShortcutTests.Plain_f5_builds_when_stopped_too</c>.</summary>
    [StaFact]
    public void The_F5_badge_stays_on_build_even_when_stopped()
    {
        var vm = NewVm();
        var (menu, window) = RealizeMenu(vm);
        Assert.Equal("F5", menu.Items.Single(i => i.Kind == "build").Kbd);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 0, 0, 0, 0, 0));
        Assert.Equal(AppPhase.Stopped, vm.Phase);

        Assert.Equal("F5", menu.Items.Single(i => i.Kind == "build").Kbd);
        GC.KeepAlive(window);
    }

    /// <summary>[topoloji kapısı] Kullanıcının GÖRDÜĞÜ yüzey: Sync yapılmadan (topoloji yokken) Build'in birincil
    /// yarısı PASİFtir; topoloji gelince açılır. VM kapısı ayrıca pinlidir (<c>RunViewModelStateTests</c>) — burada
    /// pinlenen, kapının şablondan GERÇEKTEN geçtiğidir: <c>PART_Primary</c>, <c>PrimaryCommand</c>'ın
    /// CanExecute'unu okuyan bir <see cref="Button"/> olmasaydı ekranda etkin görünmeye devam ederdi.</summary>
    [StaFact]
    public void The_build_primary_half_is_disabled_until_a_topology_arrives()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm);
        var primary = (Button)bar.Split.Template.FindName("PART_Primary", bar.Split);

        Assert.Equal(AppPhase.Boot, vm.Phase); // ön-koşul: repo var, Sync yapılmadı
        Assert.False(primary.IsEnabled);

        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A", 0)], [], [], []));

        Assert.True(primary.IsEnabled);
        GC.KeepAlive(window);
    }

    /// <summary>Split-button'ın birincil aksiyonu HER fazda Build'dir — stopped'ta Continue'ya dönüşmez.</summary>
    [StaFact]
    public void The_split_button_primary_action_stays_build_after_a_stop()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 0, 0, 0, 1, 10));
        Assert.Equal(AppPhase.Stopped, vm.Phase); // ön-koşul

        Assert.Equal(Visibility.Visible, bar.Split.Visibility);
        Assert.Same(vm.BuildCommand, bar.Split.PrimaryCommand);
        Assert.Equal("Build", ((StackPanel)bar.Split.PrimaryContent).Children.OfType<TextBlock>().Single().Text);
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

    // [Task 6 · TAŞINDI] Döngü sayılarının tooltip'e yansıması artık bakım kutusunun işidir; iddia
    // MaintenanceBoxTests'te YENİ metinle yaşıyor (etiketli Cycles düğmesi kaldırıldı, design v1.7.0 §2.7-2).

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
