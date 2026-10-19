using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [Exit] Title bar'ın Exit butonu: sağ ikon kümesinin SONU (About'tan sonra, pencere kontrollerinden önce), önünde kümenin diğer
/// ayraçlarıyla aynı ayraç; DS IconButton sm (22×22, radius 4) içinde Lucide power, 13px, kalınlık 1.7 (ⓘ ile aynı); varsayılan
/// <c>text-secondary</c>, hover <c>surface-raised</c> + <c>text-primary</c>; kart açıkken <c>surface-overlay</c> + <c>text-primary</c> —
/// IconButton'ın amber <c>active</c> hâli DEĞİL (title bar'da logo dışında amber yok) — ve tooltip gösterilmez. Tooltip "Exit — quit
/// and end every process (Ctrl+Q)", altta açılır; cümle ve jest kısayol kataloğundan gelir. Uçuşta iş yokken buton da Ctrl+Q da
/// onay sormadan kapatır.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class ExitButtonTests
{
    private static Panel RightCluster(MainWindow window) => (Panel)LogicalTreeHelper.GetParent(window.GearButton);

    [StaFact]
    public void The_exit_button_closes_the_right_cluster_behind_its_own_separator()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.NewRealized(temp);

        var children = RightCluster(window).Children.Cast<UIElement>().ToList();
        int info = children.IndexOf(window.InfoButton);
        Assert.Same(window.ExitSeparator, children[info + 1]);
        Assert.True(DsResources.IsSelfOrDescendantOf(window.ExitButton, children[info + 2], includeLogical: true),
            "Exit butonu ayraçtan sonraki öğe değil");
        Assert.Equal(info + 3, children.Count); // kümenin SONU — arkasında yalnız pencere kontrolleri
        Assert.Same(window.FindResource("TitleBarSeparator"), window.ExitSeparator.Style);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_exit_button_is_a_small_icon_button_drawing_the_power_icon()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.NewRealized(temp);
        var button = window.ExitButton;

        Assert.Equal((22.0, 22.0), (button.ActualWidth, button.ActualHeight));
        Assert.Equal(window.FindResource("Radius.Sm"), DsChrome.GetCornerRadius(button));
        Assert.Equal(new CornerRadius(4), DsChrome.GetCornerRadius(button));
        var icon = DsResources.Descendants(button).OfType<Viewbox>().Single();
        Assert.Equal((13.0, 13.0), (icon.Width, icon.Height));
        var path = DsResources.Descendants(icon).OfType<Path>().Single();
        Assert.Same(window.FindResource("Icon.Power"), path.Data);
        Assert.Equal(1.7, path.StrokeThickness);
        Assert.Equal(DsResources.TokenColor(window, "Brush.TextSecondary"), DsResources.ColorOf(button.Foreground));
        Assert.Equal(DsResources.TokenColor(window, "Brush.TextSecondary"), DsResources.ColorOf(path.Stroke));
        Assert.Empty(DsResources.DynamicResourceTypeMismatches(button));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_exit_button_reads_its_tooltip_from_the_shortcut_catalog()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.NewRealized(temp);

        var tooltip = (ToolTip)window.ExitButton.ToolTip;
        var exit = ShortcutCatalog.Get(ShortcutId.Exit);
        Assert.Equal($"{exit.Description} ({exit.Gestures[0]})", tooltip.Content);
        Assert.Equal("Exit — quit and end every process (Ctrl+Q)", tooltip.Content); // tasarımın metni birebir
        Assert.Equal(AppTooltip.GetSide((ToolTip)window.GearButton.ToolTip), AppTooltip.GetSide(tooltip)); // altta
        Assert.Equal(AccessibilityNames.Exit, AutomationProperties.GetName(window.ExitButton));
        Assert.Equal("Exit", AccessibilityNames.Exit);
        GC.KeepAlive(window);
    }

    /// <summary>Kart açıkken buton <c>surface-overlay</c> + <c>text-primary</c>'dedir (amber değil) ve tooltip'i kapalıdır; hover
    /// IconButton'ınkidir (<c>surface-raised</c> + <c>text-primary</c>). Açık hâlin sinyali <see cref="DsChrome.IsActiveProperty"/>'dir —
    /// <c>Button</c>'ın <c>IsChecked</c> karşılığı; kabuk onu kartın <c>IsOpen</c>'ına bağlar.</summary>
    [StaFact]
    public void With_the_card_open_the_button_sits_on_the_overlay_surface_without_amber_or_tooltip()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.NewRealized(temp);
        var button = window.ExitButton;
        Assert.True(ToolTipService.GetIsEnabled(button)); // ön-koşul: kapalıyken tooltip açık

        DsChrome.SetIsActive(button, true);
        button.UpdateLayout();

        Assert.Equal(DsResources.TokenColor(window, "Brush.SurfaceOverlay"), DsResources.ColorOf(button.Background));
        Assert.Equal(DsResources.TokenColor(window, "Brush.TextPrimary"), DsResources.ColorOf(button.Foreground));
        Assert.False(ToolTipService.GetIsEnabled(button));

        var styles = new List<Style>();
        for (var s = button.Style; s is not null; s = s.BasedOn) styles.Add(s);
        var hover = styles.SelectMany(s => s.Triggers.OfType<Trigger>()).Single(t => t.Property == UIElement.IsMouseOverProperty)
            .Setters.OfType<Setter>().ToDictionary(s => s.Property, s => ((DynamicResourceExtension)s.Value).ResourceKey);
        Assert.Equal("Brush.SurfaceRaised", hover[DsTransition.AnimatedBackgroundProperty]);
        Assert.Equal("Brush.TextPrimary", hover[DsTransition.AnimatedForegroundProperty]);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Clicking_exit_with_nothing_in_flight_quits_at_once()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.NewRealized(temp);
        int shutdowns = 0;
        window.ShutdownApplication = () => shutdowns++;
        var sent = new List<IpcCommand>();
        MainWindowHost.AcceptSends(vm);
        vm.DebugOnCommandSent = sent.Add;

        CommandPress.Click(window.ExitButton);
        DispatcherPump.PumpUntil(() => shutdowns > 0, TimeSpan.FromSeconds(2));

        Assert.Equal(1, shutdowns);
        Assert.False(vm.ExitPending);
        Assert.Empty(sent);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Ctrl_q_with_nothing_in_flight_quits_at_once()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.NewRealized(temp);
        int shutdowns = 0;
        window.ShutdownApplication = () => shutdowns++;

        MainWindowHost.PressKeyBinding(window, Key.Q, ModifierKeys.Control);
        DispatcherPump.PumpUntil(() => shutdowns > 0, TimeSpan.FromSeconds(2));

        Assert.Equal(1, shutdowns);
        Assert.False(vm.ExitPending);
        GC.KeepAlive(window);
    }
}
