using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E5/T47] Ekran-okuyucu (AutomationProperties.Name) kapsamı + klavye odak yönetimi. İkon-yalnız kontroller
/// (sayaç chip'leri, branch/worktree/perf chip'leri, sync/stop) İngilizce UIA-adı taşır (tek kaynak
/// <see cref="AccessibilityNames"/>); proje kartı adını, ayraçlar işlevini duyurur; şerit faz metni bir
/// assertive live region'dır; popover açılınca odak içeri; filtre Ctrl+F ile odaklanır, içindeki Esc yalnız
/// temizler+blur eder.
/// </summary>
[Collection("Console UI (serial)")]
public class AccessibilityTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    // ------------------------------------------------------------------ UIA-adı kapsamı
    [StaFact]
    public void Action_bar_names_its_icon_only_controls_for_screen_readers()
    {
        var vm = NewVm();
        var host = DsResources.NewHost();
        var bar = new ActionBar { DataContext = vm };
        var window = DsResources.Realize(host, bar);

        Assert.Equal(AccessibilityNames.FilterAll, AutomationProperties.GetName(bar.SigmaChip));
        Assert.Equal(AccessibilityNames.FilterBuilding, AutomationProperties.GetName(bar.BuildingChip));
        Assert.Equal(AccessibilityNames.FilterSucceeded, AutomationProperties.GetName(bar.SucceededChip));
        Assert.Equal(AccessibilityNames.FilterFailed, AutomationProperties.GetName(bar.FailedChip));
        Assert.Equal(AccessibilityNames.FilterSkipped, AutomationProperties.GetName(bar.SkippedChip));
        Assert.Equal(AccessibilityNames.FilterDep, AutomationProperties.GetName(bar.DepChip));
        Assert.Equal(AccessibilityNames.BranchChip, AutomationProperties.GetName(bar.BranchChip));
        Assert.Equal(AccessibilityNames.WorktreeChip, AutomationProperties.GetName(bar.WorktreeChip));
        Assert.Equal(AccessibilityNames.PerfChip, AutomationProperties.GetName(bar.PerfChip));
        Assert.Equal(AccessibilityNames.SyncButton, AutomationProperties.GetName(bar.SyncButton));
        Assert.Equal(AccessibilityNames.StopButton, AutomationProperties.GetName(bar.StopButton));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_project_row_exposes_its_project_name_to_screen_readers()
    {
        var row = new ProjectRow { AnimationsEnabledProvider = () => false,
            DataContext = new ProjectRowViewModel(@"C:\p\Foo.csproj", "Foo", ProjectRowState.Pending) };
        Assert.Equal("Foo", AutomationProperties.GetName(row));
    }

    [StaFact]
    public void The_splitters_are_named_for_screen_readers()
    {
        var shell = new ShellRoot();
        Assert.Equal(AccessibilityNames.ColumnSplitter, AutomationProperties.GetName(shell.ColumnSplitter));
        Assert.Equal(AccessibilityNames.GraphListSplitter, AutomationProperties.GetName(shell.LeftSplitter));
        Assert.Equal(AccessibilityNames.ConsoleStreamSplitter, AutomationProperties.GetName(shell.RightSplitter));
    }

    // ------------------------------------------------------------------ live region
    [StaFact]
    public void The_ribbon_phase_text_is_an_assertive_live_region()
    {
        var ribbon = new StickyRibbon();
        Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(ribbon.PhaseText));
    }

    // ------------------------------------------------------------------ klavye odak / focus ring
    [StaFact]
    public void A_project_row_is_a_focusable_tab_stop_with_the_amber_focus_ring()
    {
        var host = DsResources.NewHost();
        var row = new ProjectRow { AnimationsEnabledProvider = () => false };
        var window = DsResources.Realize(host, row);
        Assert.True(row.Focusable);
        Assert.True(row.IsTabStop);
        Assert.NotNull(row.FocusVisualStyle); // Ds.FocusVisual (amber halka) çözülür
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_splitter_is_a_keyboard_focusable_tab_stop_with_a_focus_ring()
    {
        var host = DsResources.NewHost();
        var splitter = new DsSplitter();
        var window = DsResources.Realize(host, splitter);
        Assert.True(splitter.Focusable);
        Assert.True(splitter.IsTabStop);
        Assert.NotNull(splitter.FocusVisualStyle);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_project_list_lets_arrow_keys_move_between_rows()
    {
        // Ok tuşlarıyla satırlar arası gezinme: Flow DirectionalNavigation=Contained olmalı (odak liste içinde kalır).
        var list = new StickyLayerList();
        Assert.Equal(KeyboardNavigationMode.Contained, KeyboardNavigation.GetDirectionalNavigation(list.RowFlow));
    }

    [StaFact]
    public void The_branch_popover_moves_focus_into_the_search_box_on_open()
    {
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent([new BranchRef("main", "aaaaaaaaaaaa", true, false)]));
        var host = DsResources.NewHost();
        var popover = new BranchPopover { DataContext = vm };
        var window = DsResources.Realize(host, popover);

        popover.IsOpen = true; // OnIsOpenChanged → Dispatcher.BeginInvoke(Input) ile arama kutusuna odak
        DispatcherPump.PumpUntil(() => popover.SearchBox.IsKeyboardFocused, TimeSpan.FromSeconds(2));

        Assert.True(popover.SearchBox.IsKeyboardFocused);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Escape_inside_the_branch_popover_requests_close_without_leaking()
    {
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent([new BranchRef("main", "aaaaaaaaaaaa", true, false)]));
        var host = DsResources.NewHost();
        var popover = new BranchPopover { DataContext = vm };
        var window = DsResources.Realize(host, popover);
        popover.IsOpen = true;

        bool closeRequested = false;
        popover.CloseRequested += () => closeRequested = true;

        // Esc arama kutusundan bubble eder → popover kapanır (popover ayrı HWND; pencere Esc zinciri buraya gelmez).
        var esc = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(popover.SearchBox)!, 0, Key.Escape)
        { RoutedEvent = Keyboard.KeyDownEvent };
        popover.SearchBox.RaiseEvent(esc);

        Assert.True(closeRequested);
        Assert.True(esc.Handled);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Ctrl_f_focuses_the_project_filter_input()
    {
        var (shell, box, window) = RealizeFilterBox();

        bool focused = shell.FocusProjectFilter(); // Ctrl+F'in çağırdığı GERÇEK metot
        DispatcherPump.PumpUntil(() => box.IsKeyboardFocused, TimeSpan.FromSeconds(2));

        Assert.True(focused);
        Assert.True(box.IsKeyboardFocused);
        Assert.Equal(AccessibilityNames.ProjectFilter, AutomationProperties.GetName(box));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Escape_inside_the_filter_clears_the_query_and_blurs_without_leaking()
    {
        var (_, box, window) = RealizeFilterBox();
        box.Focus();
        box.Text = "core";
        DispatcherPump.PumpUntil(() => box.IsKeyboardFocused, TimeSpan.FromSeconds(2));

        var esc = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box)!, 0, Key.Escape)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        box.RaiseEvent(esc); // ShellRoot ctor'da bağlanan GERÇEK OnFilterKeyDown handler'ı (box'a asılı, reparent'te kalır)

        Assert.True(esc.Handled);            // stopPropagation — global Esc zincirine sızmaz
        Assert.Equal("", box.Text);          // sorgu temizlendi
        Assert.False(box.IsKeyboardFocused); // blur
        GC.KeepAlive(window);
    }

    /// <summary>ShellRoot'un TAMAMI headless realize edilemez (bir RowDefinition'ın <c>Size.ActionBarHeight</c>
    /// Double DynamicResource'u GridLength'e dönüşürken host'a ekleme anında patlar — üretimde App-seviyesi
    /// kaynaklarla sorun olmaz). GERÇEK filtre kutusu + ona ShellRoot ctor'da bağlanan Esc handler'ı korunur:
    /// kutu mevcut ebeveyninden ayrılıp hafif bir host'ta realize edilir; <c>FocusProjectFilter</c> aynı referansı
    /// odakladığından metot da gerçek yolla sınanır.</summary>
    private static (ShellRoot shell, TextBox box, Window window) RealizeFilterBox()
    {
        var shell = new ShellRoot();
        var box = shell.ProjectFilterBox;
        if (LogicalTreeHelper.GetParent(box) is ContentPresenter cp) cp.Content = null; // kutuyu PanelHeader'dan ayır
        var host = DsResources.NewHost();
        var window = DsResources.Realize(host, box);
        return (shell, box, window);
    }

    // ------------------------------------------------------------------ isim sözlüğü İngilizce
    [Fact]
    public void All_accessibility_names_are_non_empty_and_free_of_turkish_letters()
    {
        const string turkish = "çğıİşöüÇĞŞÖÜ";
        foreach (var f in typeof(AccessibilityNames).GetFields(BindingFlags.Public | BindingFlags.Static)
                     .Where(f => f is { IsLiteral: true, IsInitOnly: false }))
        {
            var value = (string)f.GetValue(null)!;
            Assert.False(string.IsNullOrWhiteSpace(value), $"{f.Name} boş");
            Assert.DoesNotContain(value, c => turkish.Contains(c));
        }
    }
}
