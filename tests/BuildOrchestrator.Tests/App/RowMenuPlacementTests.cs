using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Linq;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Satır menüsü <b>⋯ düğmesine değil, listenin SAĞ kenarına</b> çakılır: prototipte konum
/// <c>position:absolute; right: 8</c>'dir ve dikeyde satırın altına 3px binerek oturur
/// (<c>BuildApp.jsx:609</c> ve <c>:659</c>), ardından panelin görünür alanına kelepçelenir (<c>:661-664</c>).
///
/// <para><b>Ölçülen kusur.</b> Yerleşim <c>HorizontalOffset="-118"</c> sihirli sayısıyla ⋯ düğmesinin soluna
/// kaydırılıyordu. ⋯'den sonra iki ikon daha geldiği için (folder, VS) menünün sağ kenarı satırın sağ
/// kenarından ~44px içeride kalıyordu; kullanıcı bunu "hep altta ama baya solda" diye tarif etti. Sayı
/// ayrıca ikon sayısı/genişliği değişince sessizce bozulacak bir varsayımdı.</para>
///
/// <para><b>Neden mutlak ekran konumu ölçülmüyor:</b> süitin pencereleri EKRAN DIŞINDA gösterilir
/// (<c>AnimationHost.ShowOffscreen</c>) ve WPF bir <see cref="Popup"/>'ı hiçbir monitöre düşmediğinde
/// görünür alana GERİ KELEPÇELER — <c>PointToScreen</c> o yüzden yerleşim kararını değil kelepçeyi ölçerdi
/// (ÖLÇÜLDÜ). Bu yüzden testin sürdüğü şey kararın KENDİSİDİR: popup gerçekten açılır, sonra WPF'in
/// çağırdığı geri çağrı, WPF'in vereceği ölçülerle çağrılır.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class RowMenuPlacementTests
{
    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)),
            () => "r1") { RootPath = @"D:\repo" };

    [StaFact]
    public void The_row_menu_is_pinned_to_the_lists_right_edge_not_to_the_ellipsis_button()
    {
        var row = new ProjectRow { DataContext = new ProjectRowViewModel(@"C:\p\a.csproj", "A", ProjectRowState.Pending) };
        var shell = new Border { DataContext = NewVm(), Child = row };
        var window = DsResources.Realize(DsResources.NewHost(), shell, 900, 200);
        row.SimulateHover(true);
        row.UpdateLayout();

        var actions = row.Actions!;
        bool opened = false;
        actions.RowMenu.Opened += (_, _) => opened = true;
        actions.MoreButton.IsChecked = true;
        DispatcherPump.PumpUntil(() => opened, TimeSpan.FromSeconds(2));
        Assert.True(opened, "ön-koşul: satır menüsü açılmadı");
        actions.RowMenuContent.UpdateLayout();

        // Çapa SATIRIN KÖKÜDÜR, ⋯ düğmesi değil — sabit bir offset yerine WPF'in Custom yerleşimi kullanılır.
        Assert.Equal(PlacementMode.Custom, actions.RowMenu.Placement);
        Assert.Same(row.Root, actions.RowMenu.PlacementTarget);

        var menu = actions.RowMenuContent;
        var target = new Size(row.Root.ActualWidth, row.Root.ActualHeight);
        var popup = new Size(menu.ActualWidth, menu.ActualHeight);
        Assert.True(popup.Width > 0 && target.Width > popup.Width, "ön-koşul: ölçüler gerçek değil");

        var placement = actions.RowMenu.CustomPopupPlacementCallback(popup, target, default).Single();

        // Menünün SAĞ kenarı satırın sağ kenarından 8px içeride (BuildApp.jsx:609 `right: 8`).
        Assert.Equal(target.Width - RowMenuPlacement.EdgeInset, placement.Point.X + popup.Width, 6);
        // Dikeyde satırın altına 3px biner (BuildApp.jsx:659) — tek satırlık kapta kelepçe devreye girmez.
        Assert.Equal(target.Height - RowMenuPlacement.RowOverlap, placement.Point.Y, 6);

        actions.MoreButton.IsChecked = false;
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- saf aritmetik (BuildApp.jsx:659-664)

    [Fact]
    public void The_menu_hangs_three_pixels_over_the_rows_bottom_when_there_is_room()
    {
        // rowTop 10, rowHeight 36 → satır altı 46; menü 46-3 = 43'te başlar.
        Assert.Equal(43, RowMenuPlacement.TopInViewport(rowTop: 10, rowHeight: 36, menuHeight: 108, viewportHeight: 400));
    }

    [Fact]
    public void Near_the_bottom_of_the_list_the_menu_slides_up_to_stay_inside_the_viewport()
    {
        // Satır altı 393 → 390 istenir ama menü (108) 400'lük alana sığmaz: taban 400-108-4 = 288.
        Assert.Equal(288, RowMenuPlacement.TopInViewport(rowTop: 357, rowHeight: 36, menuHeight: 108, viewportHeight: 400));
    }

    /// <summary>
    /// Panel menüden KISA olduğunda kelepçenin iki ucu ters döner (üst sınır alt sınırdan büyük olur).
    /// Prototip bu durumda ÜST sınırı kazandırır (<c>Math.max(minTop, Math.min(top, Math.max(minTop, maxTop)))</c>,
    /// BuildApp.jsx:664) — menü aşağı taşar ama başlığı görünür kalır; ters kelepçe onu görünmez yapardı.
    /// </summary>
    [Fact]
    public void When_the_panel_is_shorter_than_the_menu_the_top_margin_wins()
    {
        Assert.Equal(RowMenuPlacement.ViewportMargin,
            RowMenuPlacement.TopInViewport(rowTop: 0, rowHeight: 36, menuHeight: 108, viewportHeight: 60));
    }
}
