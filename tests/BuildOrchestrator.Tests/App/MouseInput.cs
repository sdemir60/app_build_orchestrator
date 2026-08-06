using System.Windows;
using System.Windows.Input;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T1] Testlerde GERÇEK bir fare basışı yükseltmenin TEK yeri.
///
/// <para><b>Neden doğrudan handler çağrılmıyor:</b> WPF'te <see cref="UIElement.MouseLeftButtonDown"/>
/// <b>Direct</b> yönlendirmelidir; "kabarma" görüntüsünü <see cref="Mouse.MouseDownEvent"/> (bubbling)
/// üzerindeki sınıf handler'ı üretir — kabarma yolundaki her öğede tek tek yeniden yükseltir ve
/// <c>Handled</c>'ı geri kopyalar. Bu yüzden testler <see cref="Mouse.MouseDownEvent"/> yükseltir: yol
/// üstündeki handler'lar üretimde hangi sırayla/koşulla koşuyorsa testte de öyle koşar.</para>
///
/// <para><b>Neden ortak:</b> aynı beş satır <c>GraphClickTests</c> (düğüm/zemin tıklaması) ve
/// <c>GraphPanZoomTests</c> (sinema modunda zemin basışının seçimi KALDIRMADIĞI) tarafından kullanılır —
/// kopya YASAK (CLAUDE.md).</para>
/// </summary>
internal static class MouseInput
{
    /// <summary>Sol tuş basışı. Dönen <see cref="MouseButtonEventArgs"/> ile <c>Handled</c> sınanabilir.</summary>
    public static MouseButtonEventArgs PressLeft(UIElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = Mouse.MouseDownEvent,
        };
        target.RaiseEvent(args);
        return args;
    }

    /// <summary>Sol tuş BIRAKMA. [quiet] Grafta "boş zemine tıklama" kararı artık bırakmaya aittir
    /// (basış bir sürüklemenin başı olabilir), dolayısıyla tam jesti sürmek için gerekir.</summary>
    public static MouseButtonEventArgs ReleaseLeft(UIElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = Mouse.MouseUpEvent,
        };
        target.RaiseEvent(args);
        return args;
    }

    /// <summary>Capture'ın DÜŞMESİ (Alt+Tab, popup, başka öğenin capture alması). Bu bir bırakma DEĞİL
    /// iptaldir — <c>GraphView</c> onu tuş bırakmadan ayrı bir yola bağlar.</summary>
    public static void LoseCapture(UIElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
        {
            RoutedEvent = Mouse.LostMouseCaptureEvent,
        });
    }
}
