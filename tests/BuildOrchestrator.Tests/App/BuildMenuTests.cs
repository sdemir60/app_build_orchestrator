using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.11.0 §2.7-11 · §9-7] Build split-button menüsü ÜÇ maddedir: <b>Build · Rebuild · Clean</b>.
/// İkon ailesi tek grid/stroke'ta okunur — play · rotate-cw · brush — ve AYNI aile satır menüsünde de
/// kullanılır.
///
/// <para><b>[DEĞİŞEN KURAL]</b> v1.7.0'da menü KOŞULSUZ İKİ maddeydi (Build + Rebuild) ve Rebuild'in ikonu
/// <c>Icon.Rot</c>'tu (Sync/Redo ailesinden). v1.11.0 üçüncü maddeyi (VS <i>Clean Solution</i>) ekledi ve
/// Rebuild'i kendi ailesinin rotate-cw'sine (<c>Icon.Rebuild</c>) taşıdı — eski iddiayı pinleyen testler bu
/// dosyada YENİ kurala göre yeniden yazıldı.</para>
///
/// <para><b>Clean'in arka ucu henüz yazılmadı</b> (bakım kutusundaki Clean/Optimize ile aynı karar): madde
/// tasarımdaki yerinde durur, pasiftir ve tooltip nedenini söyler.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class BuildMenuTests
{
    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)),
            () => "r1") { RootPath = @"D:\repo" };

    private static (BuildMenu menu, Window window) Realize(RunViewModel vm)
    {
        var host = DsResources.NewHost();
        var menu = new BuildMenu { DataContext = vm };
        return (menu, DsResources.Realize(host, menu));
    }

    [Fact]
    public void The_menu_is_build_rebuild_then_clean()
    {
        var items = BuildMenu.ComposeItems(total: 36);

        Assert.Equal(["build", "rebuild", "clean"], items.Select(i => i.Kind));
        Assert.Equal("Clean", items[2].Title);
        Assert.Equal("Remove build outputs — next build is full", items[2].Desc);
        Assert.Null(items[2].Kbd); // Clean'in kısayolu YOK (F5/Ctrl+F5 Build ve Rebuild'indir)
    }

    /// <summary>[§9-7] "İkon ailesi tek grid/stroke'ta: play · rotate-cw · brush." Rebuild ARTIK Sync'in
    /// <c>Icon.Rot</c>'unu kullanmaz.</summary>
    [Fact]
    public void The_three_items_share_one_icon_family_play_rotate_cw_brush()
    {
        Assert.Equal("Icon.Play", BuildMenu.IconKeyFor("build"));
        Assert.Equal("Icon.Rebuild", BuildMenu.IconKeyFor("rebuild"));
        Assert.Equal("Icon.Brush", BuildMenu.IconKeyFor("clean"));
    }

    /// <summary>Arka ucu olmayan madde pasif çizilir ve nedeni tooltip'te durur — bakım kutusunun
    /// Clean/Optimize düğmeleriyle AYNI karar (basılıp hiçbir şey olmaması yokluğu sessizce gizlerdi).</summary>
    [StaFact]
    public void Clean_is_drawn_disabled_with_a_reason_in_its_tooltip()
    {
        var vm = NewVm();
        var (menu, window) = Realize(vm);

        var rows = menu.Rows.ToList();
        Assert.Equal(3, rows.Count);
        Assert.True(rows[0].IsEnabled);
        Assert.True(rows[1].IsEnabled);
        Assert.False(rows[2].IsEnabled);
        Assert.Equal(BuildOrchestrator.App.AccessibilityNames.CleanSolutionTooltip, rows[2].ToolTip);
        Assert.True(ToolTipService.GetShowOnDisabled(rows[2])); // pasif kontrolde WPF tooltip'i saklardı
        GC.KeepAlive(window);
    }
}
