using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
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

    [StaFact]
    public void At_most_four_building_chips_are_shown_and_the_overflow_is_plain_text()
    {
        var vm = NewVm();
        var projects = Enumerable.Range(0, 6).Select(i => ($@"C:\p\proj{i}.csproj", $"Proj{i}")).ToArray();
        StartRun(vm, projects);
        foreach (var (id, name) in projects) vm.OnEvent(new ProjectStartedEvent("r1", id, name));

        var (ribbon, window) = Realize(vm);

        Assert.Equal(4, ribbon.BuildingChips.Count);        // ilk 4 chip
        Assert.NotNull(ribbon.BuildingOverflow);            // taşan +2 DÜZ metin
        Assert.Equal("+2", ribbon.BuildingOverflow!.Text);
        Assert.IsNotType<ToggleButton>(ribbon.BuildingOverflow); // tıklanamaz (chip DEĞİL)
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Failure_cluster_shows_three_chips_and_a_more_chip_that_applies_the_failed_filter()
    {
        var vm = NewVm();
        var projects = Enumerable.Range(0, 5).Select(i => ($@"C:\p\fail{i}.csproj", $"Fail{i}")).ToArray();
        StartRun(vm, projects);
        foreach (var (id, name) in projects)
        {
            vm.OnEvent(new ProjectStartedEvent("r1", id, name));
            vm.OnEvent(new ProjectFailedEvent("r1", id, 100, "exit 1"));
        }

        var (ribbon, window) = Realize(vm);

        Assert.Equal(3, ribbon.FailureChips.Count);   // ilk 3 hatalı chip
        Assert.NotNull(ribbon.FailureMoreChip);        // "+2 more"

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
