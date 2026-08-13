using System.Windows;
using System.Windows.Controls;
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

    private static ProjectNode Node(string id, string name, int buildOrder) =>
        new(id, name, id, ["Osys"], [], buildOrder, null, null, false, null);

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

    /// <summary>[karar 2026-08-13] Clean ve Optimize'ın ARKA UCU henüz yok. Düğmeler tasarımdaki yerlerinde
    /// durur ama kalıcı olarak pasiftir ve tooltip bunu açıkça söyler — basılıp hiçbir şey olmaması, yokluğu
    /// sessizce gizlemekten daha kötü olurdu. Tooltip'in gövdesi tasarım metnidir (§2.7-2), sonuna durum eki
    /// gelir. Pasif kontrolde tooltip WPF'te varsayılan olarak GÖSTERİLMEZ; bu yüzden ShowOnDisabled da
    /// pinlenir — aksi halde metin var ama kullanıcı hiç göremez.</summary>
    [StaFact]
    public void Clean_and_optimize_are_disabled_and_say_so_in_a_tooltip_that_shows_while_disabled()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        Assert.False(box.CleanButton.IsEnabled);
        Assert.False(box.OptimizeButton.IsEnabled);
        Assert.Equal("Clean — /t:Clean on every solution, then remove bin/, obj/, artifacts/ — not available yet",
                     box.CleanButton.ToolTip);
        Assert.Equal("Optimize — restore packages, prune the cache, rebuild the dependency index — not available yet",
                     box.OptimizeButton.ToolTip);
        Assert.True(ToolTipService.GetShowOnDisabled(box.CleanButton));
        Assert.True(ToolTipService.GetShowOnDisabled(box.OptimizeButton));
        GC.KeepAlive(window);
    }

    /// <summary>[design v1.7.0 §2.7-2] Resolve'un tooltip'i döngü VARKEN ne yapacağını üye sayısıyla anlatır,
    /// yokken neden pasif olduğunu söyler.
    /// <para><b>Tasarımdan bilinçli SAPMA (karar 2026-08-13):</b> prototip metni "in two passes" der; gerçek
    /// motor tur sayısını kendi belirler (<c>CycleRoundPolicy</c>: en az iki ardışık yeşil tur, tavan üç).
    /// Tooltip bu yüzden sabit bir sayı VAAT ETMEZ — "repeated passes" der, gerisi aynıdır.</para></summary>
    [StaFact]
    public void Resolve_tooltip_explains_the_run_when_cycles_exist_and_the_absence_otherwise()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        Assert.Equal("Resolve cycles — no dependency cycles detected", box.ResolveButton.ToolTip);

        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1), Node(@"C:\p\c.csproj", "C", 2)],
            [[@"C:\p\a.csproj", @"C:\p\b.csproj", @"C:\p\c.csproj"]], [], []));

        Assert.Equal("Resolve cycles — build the 3 cycle projects in repeated passes: stale references first, "
                     + "then rebuild until they converge", box.ResolveButton.ToolTip);
        GC.KeepAlive(window);
    }

    /// <summary>[Task 6 · korunan geliştirme] Kod tarafında sonradan eklenen grup sayısı bilgisi KORUNUR:
    /// tasarımın tek-sayılı cümlesi birden çok ayrı döngü olduğunda eksik kalıyordu ("5 proje" beş projelik
    /// TEK bir döngü sanılabilir). Ek yalnız gerçekten birden çok grup varken çıkar — tek gruplu (yaygın)
    /// durumda cümle tasarımdaki hâliyle kalır.</summary>
    [StaFact]
    public void Resolve_tooltip_names_the_group_count_only_when_there_is_more_than_one_cycle()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1),
             Node(@"C:\p\c.csproj", "C", 2), Node(@"C:\p\d.csproj", "D", 3)],
            [[@"C:\p\a.csproj", @"C:\p\b.csproj"], [@"C:\p\c.csproj", @"C:\p\d.csproj"]],
            [], []));

        Assert.Equal("Resolve cycles — build the 4 cycle projects in repeated passes: stale references first, "
                     + "then rebuild until they converge (2 separate cycles)", box.ResolveButton.ToolTip);
        GC.KeepAlive(window);
    }
}
