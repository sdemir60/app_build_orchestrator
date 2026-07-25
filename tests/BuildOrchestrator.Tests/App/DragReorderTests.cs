using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [D7/T66] <see cref="DragReorderBehavior"/> — Mouse.Capture tabanlı katman kartı sürükle-sırala.
/// (a) SAF swap kararı ([Fact]): ±21px eşik + 42px adım + çoklu-adım (BuildApp.jsx portu, WPF'siz).
/// (b) [StaFact]: gerçek davranış grip'ten mouse'u YAKALAR ve OLE <c>DragDrop.DoDragDrop</c>'u ASLA çağırmaz
/// (yakalama sürükleme boyunca elde tutulur — OLE modal döngüsüne DEVREDİLMEZ).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class DragReorderTests
{
    [Fact]
    public void Drag_swaps_with_the_neighbour_at_twentyone_pixels_and_supports_multi_step_drags()
    {
        // Ölçüler prototipten pinlenir (BuildApp.jsx: ROWH=42, ROWH/2=21).
        Assert.Equal(42, DragReorderBehavior.RowStep);
        Assert.Equal(21, DragReorderBehavior.SwapThreshold);

        // Eşiğin ALTINDA (tam 21px, kesin '>' — prototiple birebir): swap YOK.
        var atThreshold = DragReorderBehavior.Resolve(index: 1, count: 6, deltaY: 21);
        Assert.Empty(atThreshold.Swaps);
        Assert.Equal(1, atThreshold.NewIndex);

        // Eşiğin hemen ÜSTÜNDE (22px): komşuyla TEK swap (aşağı).
        var oneDown = DragReorderBehavior.Resolve(index: 1, count: 6, deltaY: 22);
        Assert.Equal([(1, 2)], oneDown.Swaps);
        Assert.Equal(2, oneDown.NewIndex);

        // Çoklu-adım aşağı (64px = 21'i + bir tam 42'lik adımı geçer): İKİ swap.
        var twoDown = DragReorderBehavior.Resolve(index: 1, count: 6, deltaY: 64);
        Assert.Equal([(1, 2), (2, 3)], twoDown.Swaps);
        Assert.Equal(3, twoDown.NewIndex);

        // Yukarı yön (-22px): komşuyla tek swap (yukarı).
        var oneUp = DragReorderBehavior.Resolve(index: 2, count: 6, deltaY: -22);
        Assert.Equal([(2, 1)], oneUp.Swaps);
        Assert.Equal(1, oneUp.NewIndex);

        // Üst sınırda kelepçelenir: index 0'dan yukarı sürükleme swap üretmez.
        var clampTop = DragReorderBehavior.Resolve(index: 0, count: 6, deltaY: -100);
        Assert.Empty(clampTop.Swaps);
        Assert.Equal(0, clampTop.NewIndex);

        // Alt sınırda kelepçelenir: son index'ten (5) aşağı sürükleme swap üretmez.
        var clampBottom = DragReorderBehavior.Resolve(index: 5, count: 6, deltaY: 100);
        Assert.Empty(clampBottom.Swaps);
        Assert.Equal(5, clampBottom.NewIndex);
    }

    [StaFact]
    public void Reorder_uses_mouse_capture_and_never_calls_the_ole_drag_drop_api()
    {
        // 4 katman satırı, her biri bir grip (IsDragHandle) taşıyan bir ItemsControl'de gerçeklenir.
        var rows = new ObservableCollection<LayerRowViewModel>
        {
            new("A", ""), new("B", ""), new("C", ""), new("D", ""),
        };
        var gripFactory = new FrameworkElementFactory(typeof(Border));
        gripFactory.SetValue(DragReorderBehavior.IsDragHandleProperty, true);
        gripFactory.SetValue(FrameworkElement.WidthProperty, 20.0);
        gripFactory.SetValue(FrameworkElement.HeightProperty, DragReorderBehavior.CardHeight);
        gripFactory.SetValue(Control.BackgroundProperty, Brushes.Transparent);
        var itemsControl = new ItemsControl
        {
            ItemsSource = rows,
            ItemTemplate = new DataTemplate { VisualTree = gripFactory },
        };
        var window = AnimationHost.ShowOffscreen(itemsControl, width: 300, height: 300);
        itemsControl.UpdateLayout();
        DispatcherPump.PumpUntil(() => itemsControl.Items.Count == 4 && Grip(itemsControl, rows[0]) is not null,
            TimeSpan.FromSeconds(2));

        var grip0 = Grip(itemsControl, rows[0])!;

        // Grip'e basış → davranış MOUSE'U YAKALAR (OLE DragDrop DEĞİL).
        grip0.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
        });

        Assert.True(grip0.IsMouseCaptured);                     // "uses mouse capture"
        Assert.Same(grip0, Mouse.Captured);
        var session = DragReorderBehavior.ActiveSession;
        Assert.NotNull(session);
        Assert.True(rows[0].IsDragging);

        // Yakalama boyunca (pozisyon deterministik enjekte edilir — gerçek imleç WPF'te enjekte edilemez):
        // 50px aşağı → komşuyla TEK swap. Bu, OLE modal döngüsü OLMADAN, yalnız yakalama+move ile olur.
        session!.DragTo(session.StartY + 50);

        Assert.Equal("B", rows[0].Name);                       // A, B ile yer değiştirdi
        Assert.Equal("A", rows[1].Name);
        Assert.True(rows[1].IsDragging);                       // sürüklenen öğe (A) hâlâ kalkık
        // OLE DragDrop.DoDragDrop çağrılsaydı kendi modal döngüsüne geçip yakalamayı DEVRALIRDI; yakalama
        // sürükleme boyunca HÂLÂ grip'te → OLE yolu HİÇ girilmedi.
        Assert.Same(grip0, Mouse.Captured);

        // Bırakış → yakalama serbest, sürükleme durumu temizlenir.
        grip0.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent,
        });

        Assert.Null(Mouse.Captured);
        Assert.Null(DragReorderBehavior.ActiveSession);
        Assert.All(rows, r => Assert.False(r.IsDragging));

        GC.KeepAlive(window);
    }

    /// <summary>Verilen satırın grip Border'ını (IsDragHandle=true, DataContext=row) görsel ağaçta bulur.</summary>
    private static Border? Grip(DependencyObject root, LayerRowViewModel row) =>
        DsResources.Descendants(root).OfType<Border>()
            .FirstOrDefault(b => DragReorderBehavior.GetIsDragHandle(b) && ReferenceEquals(b.DataContext, row));
}
