using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.7.0 §2.7-2] Action bar'ın BAKIM KUTUSU: chip ağırlığında TEK kutu (24px, surface-raised zemin,
/// 1px border, radius-xs, overflow hidden) ve içinde üç 28×22 ikon buton — Clean · Optimize · Resolve cycles —
/// aralarında 1px×14 ayraçla. Düğmelerde ETİKET YOKTUR: üç etiketli düğme barı 1240px minimumda taşırıyor
/// ve Build split-button'ı eziyordu (§2.7-2 gerekçesi); anlam tooltip'tedir.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class MaintenanceBoxTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    private static (MaintenanceBox box, Window window) Realize(RunViewModel vm)
    {
        var host = DsResources.NewHost();
        var box = new MaintenanceBox { DataContext = vm };
        return (box, DsResources.Realize(host, box));
    }

    [StaFact]
    public void The_box_is_a_raised_bordered_strip_that_clips_its_children()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        var root = Assert.IsType<Border>(box.Content);
        Assert.Same(box.FindResource("Brush.SurfaceRaised"), root.Background);
        Assert.Same(box.FindResource("Brush.Border"), root.BorderBrush);
        Assert.Equal(new Thickness(1), root.BorderThickness);
        Assert.Equal(box.FindResource("Radius.Xs"), root.CornerRadius);
        Assert.Equal(24d, root.Height);
        // overflow:hidden — kutunun köşe yarıçapı içerideki düğmeleri KESMELİ (BuildApp.jsx:1931).
        Assert.True(root.ClipToBounds);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_box_orders_clean_then_optimize_then_resolve_with_hairline_separators_between_them()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        var strip = Assert.IsType<StackPanel>(Assert.IsType<Border>(box.Content).Child);
        var children = strip.Children.Cast<UIElement>().ToList();
        Assert.Equal(5, children.Count);
        Assert.Same(box.CleanButton, children[0]);
        Assert.Same(box.OptimizeButton, children[2]);
        Assert.Same(box.ResolveButton, children[4]);

        foreach (int i in new[] { 1, 3 })
        {
            var separator = Assert.IsType<Border>(children[i]);
            Assert.Equal(1d, separator.Width);
            Assert.Equal(14d, separator.Height);
            Assert.Same(box.FindResource("Brush.Border"), separator.Background);
        }

        foreach (var button in new[] { box.CleanButton, box.OptimizeButton, box.ResolveButton })
        {
            Assert.Equal(28d, button.Width);
            Assert.Equal(22d, button.Height);
        }
        GC.KeepAlive(window);
    }
}
