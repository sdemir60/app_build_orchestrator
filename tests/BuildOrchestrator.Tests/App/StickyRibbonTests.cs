using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T39] design-v1 sticky şerit görünümü (<see cref="StickyRibbon"/>, BuildApp.jsx:778-812). Şerit GERÇEKTEN
/// kurulur (ekran dışı pencere + merge zinciri) — 32px içerik / 2px progress geometrisi, building chip taşması,
/// hata kümesi (3 chip + "+N more" → Failed filtresi) ve Syncing'de belirsiz mod pinlenir.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class StickyRibbonTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    private static (StickyRibbon ribbon, Window window) Realize(RunViewModel vm, bool forceAnimations = false)
    {
        var host = DsResources.NewHost();
        var ribbon = new StickyRibbon { DataContext = vm };
        if (forceAnimations) ribbon.AnimationsEnabledProvider = () => true;
        var window = DsResources.Realize(host, ribbon);
        return (ribbon, window);
    }

    private static void StartRun(RunViewModel vm, params (string id, string name)[] projects)
    {
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, projects.Length, 4, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([.. projects.Select(p => new BuildPreviewItem(p.id, p.name, true))]));
    }

    /// <summary>[D5] Topolojiyi kurar → kısa-ad öneki (NamePrefix) satırlara itilir; chip'ler onu okur.</summary>
    private static void SetTopology(RunViewModel vm, params (string id, string name)[] projects) =>
        vm.OnEvent(new WorkspaceTopologyEvent(
            [.. projects.Select(p => new ProjectNode(p.id, p.name, p.id, [], [], 0, null, null, false, null))],
            [], [], []));

    /// <summary>[D2 review fix, Finding 4] Bir chip'in görünür etiketi — Content her zaman [ikon, TextBlock] StackPanel'i.</summary>
    private static string ChipLabel(ToggleButton chip) =>
        ((TextBlock)((StackPanel)chip.Content).Children[1]).Text;

    [StaFact]
    public void Ribbon_is_thirtytwo_pixels_over_a_two_pixel_progress_bar_with_zero_radius()
    {
        var vm = NewVm();
        var (ribbon, window) = Realize(vm);

        Assert.Equal(32.0, ribbon.ContentRow.Height);
        Assert.Equal(2.0, ribbon.ProgressTrack.Height);
        Assert.Equal(new CornerRadius(0), ribbon.ProgressTrack.CornerRadius);
        GC.KeepAlive(window);
    }

    /// <summary>[A13/T3c · c9] README §2.2: "Kalıcı durum satırı; surface-base, altta border-subtle." Yükseklik
    /// (32/2px) zaten pinliydi (yukarıdaki test); şeridin KENDİ zemini/alt çizgisi testsizdi — root Border
    /// başka bir fırçaya (ör. Brush.Surface) bağlansa süit yeşil kalırdı.</summary>
    [StaFact]
    public void The_ribbon_root_is_surface_base_with_a_border_subtle_bottom_line()
    {
        var vm = NewVm();
        var (ribbon, window) = Realize(vm);

        var root = Assert.IsType<Border>(ribbon.Content);
        Assert.Same(ribbon.FindResource("Brush.SurfaceBase"), root.Background);
        Assert.Same(ribbon.FindResource("Brush.BorderSubtle"), root.BorderBrush);
        Assert.Equal(new Thickness(0, 0, 0, 1), root.BorderThickness);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void At_most_four_building_chips_are_shown_and_the_overflow_is_plain_text()
    {
        var vm = NewVm();
        var projects = Enumerable.Range(0, 6).Select(i => ($@"C:\p\proj{i}.csproj", $"Proj{i}")).ToArray();
        StartRun(vm, projects);
        foreach (var (id, name) in projects) vm.OnEvent(new ProjectStartedEvent("r1", id, name));

        var (ribbon, window) = Realize(vm);

        Assert.Equal(4, ribbon.BuildingChips.Count);        // ilk 4 chip
        Assert.NotNull(ribbon.BuildingOverflow);            // taşan +2 DÜZ metin (ToggleButton DEĞİL — statik tip TextBlock?, ayrıca tıklanamaz)
        Assert.Equal("+2", ribbon.BuildingOverflow!.Text);

        // [D2 review fix, Finding 3] chip'ler arası 4px gap (BuildApp.jsx:783 flex gap:4) — ilk chip HARİÇ.
        Assert.Equal(0.0, ribbon.BuildingChips[0].Margin.Left);
        Assert.Equal(4.0, ribbon.BuildingChips[1].Margin.Left);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Failure_cluster_shows_three_chips_and_a_more_chip_that_applies_the_failed_filter()
    {
        var vm = NewVm();
        var projects = Enumerable.Range(0, 5).Select(i => ($@"C:\p\fail{i}.csproj", $"Fail{i}")).ToArray();
        const string depProjectId = @"C:\p\dep.csproj"; // [6b fold] "N dependency-affected" segmentini de tetikler (succeeded + dep-issue)
        StartRun(vm, [.. projects, (depProjectId, "Dep")]);
        foreach (var (id, name) in projects)
        {
            vm.OnEvent(new ProjectStartedEvent("r1", id, name));
            vm.OnEvent(new ProjectFailedEvent("r1", id, 100, "exit 1"));
        }
        vm.OnEvent(new ProjectStartedEvent("r1", depProjectId, "Dep"));
        vm.OnEvent(new ProjectSucceededEvent("r1", depProjectId, 100, ["dependent X henüz derlenmedi"]));

        var (ribbon, window) = Realize(vm);

        Assert.Equal(3, ribbon.FailureChips.Count);   // ilk 3 hatalı chip
        Assert.NotNull(ribbon.FailureMoreChip);        // "+2 more"

        // [D2 review fix, Finding 3] chip'ler arası 4px gap (BuildApp.jsx:801 flex gap:4) — ilk chip HARİÇ; "more" chip de dahil.
        Assert.Equal(0.0, ribbon.FailureChips[0].Margin.Left);
        Assert.Equal(4.0, ribbon.FailureChips[1].Margin.Left);
        Assert.Equal(4.0, ribbon.FailureMoreChip!.Margin.Left);

        // [6b fold] Failure-cluster metnini pinle: "N failed" + "· N dependency-affected" (view kodunda kuruluyor,
        // RibbonText.Compose'ta DEĞİL — bunlar chip-sayımının kapsamadığı segmentler).
        var texts = ribbon.FailureCluster.Children.OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("5 failed", texts);
        Assert.Contains("· 1 dependency-affected", texts);

        Assert.Null(vm.ActiveFilter);
        ribbon.FailureMoreChip!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(ProjectFilter.Failed, vm.ActiveFilter); // "+N more" → Failed filtresi
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_building_chip_click_selects_that_project()
    {
        var vm = NewVm();
        var projects = new[] { ($@"C:\p\a.csproj", "A"), ($@"C:\p\b.csproj", "B") };
        StartRun(vm, projects);
        foreach (var (id, name) in projects) vm.OnEvent(new ProjectStartedEvent("r1", id, name));

        var (ribbon, window) = Realize(vm);

        ribbon.BuildingChips[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(@"C:\p\a.csproj", vm.SelectedProjectId);
        Assert.False(ribbon.BuildingChips[0].IsChecked); // momentary — aktif amber yapışmaz
        GC.KeepAlive(window);
    }

    [StaFact] // [D2 review fix, Finding 4 · D5] design-v1 label={BO.shortName(n)} — veri-türevli ortak öneki atar.
    public void Building_and_failure_chip_labels_use_the_short_project_name()
    {
        // [D5] Önek artık hardcode değil: topoloji (OSYS.Foo) → NamePrefix "OSYS." satıra itilir → chip kırpar.
        var vmBuilding = NewVm();
        SetTopology(vmBuilding, (@"C:\p\OSYS.Foo.csproj", "OSYS.Foo"));
        StartRun(vmBuilding, (@"C:\p\OSYS.Foo.csproj", "OSYS.Foo"));
        vmBuilding.OnEvent(new ProjectStartedEvent("r1", @"C:\p\OSYS.Foo.csproj", "OSYS.Foo"));
        var (buildingRibbon, buildingWindow) = Realize(vmBuilding);
        Assert.Equal("Foo", ChipLabel(buildingRibbon.BuildingChips[0]));
        GC.KeepAlive(buildingWindow);

        var vmFailed = NewVm();
        SetTopology(vmFailed, (@"C:\p\OSYS.Bar.csproj", "OSYS.Bar"));
        StartRun(vmFailed, (@"C:\p\OSYS.Bar.csproj", "OSYS.Bar"));
        vmFailed.OnEvent(new ProjectStartedEvent("r1", @"C:\p\OSYS.Bar.csproj", "OSYS.Bar"));
        vmFailed.OnEvent(new ProjectFailedEvent("r1", @"C:\p\OSYS.Bar.csproj", 100, "exit 1"));
        var (failedRibbon, failedWindow) = Realize(vmFailed);
        Assert.Equal("Bar", ChipLabel(failedRibbon.FailureChips[0]));
        GC.KeepAlive(failedWindow);
    }

    [StaFact] // [D2 review fix, Finding 5] glyph collapsed → leading gap yok; glyph görünür → glyph→metin gap:10.
    public void Phase_text_margin_follows_glyph_visibility()
    {
        var vm = NewVm();
        var (ribbon, window) = Realize(vm);

        Assert.Equal(0.0, ribbon.PhaseText.Margin.Left); // Boot: glyph yok

        vm.OnEvent(new WorkspaceTopologyEvent([], [], [], []));
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 0, 0, "Debug", 0));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 0, 0, 0, 0, 100));

        Assert.Equal(AppPhase.Done, vm.Phase);
        Assert.True(vm.AllClean); // hiç willBuild yok → done+success glyph görünür
        Assert.Equal(10.0, ribbon.PhaseText.Margin.Left);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Sync_phase_puts_the_progress_bar_into_indeterminate_mode()
    {
        var vm = NewVm();
        var (ribbon, window) = Realize(vm, forceAnimations: true);

        // Başlangıç (Boot): belirsiz DEĞİL.
        Assert.False(ribbon.IsIndeterminate);

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main")); // → Syncing
        Assert.True(ribbon.IsIndeterminate);

        // Gerçek bir sweep saati (compositor tick) — HWND'li ekran dışı pencerede indikatör TranslateX animate olur.
        DispatcherPump.PumpUntil(
            () => DependencyPropertyHelper.GetValueSource(ribbon.IndicatorTranslate, TranslateTransform.XProperty).IsAnimated,
            TimeSpan.FromSeconds(2));
        Assert.True(DependencyPropertyHelper.GetValueSource(ribbon.IndicatorTranslate, TranslateTransform.XProperty).IsAnimated);

        // Sync bitince (Idle) belirsiz mod bırakılır ve sweep durur.
        vm.OnEvent(new WorkspaceTopologyEvent([], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 0, 0)); // → Idle
        Assert.False(ribbon.IsIndeterminate);
        Assert.False(DependencyPropertyHelper.GetValueSource(ribbon.IndicatorTranslate, TranslateTransform.XProperty).IsAnimated);
        GC.KeepAlive(window);
    }
}
