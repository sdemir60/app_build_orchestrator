using System.Windows;
using System.Windows.Input;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T1] Testlerde GERÇEK bir fare girdisi — basış ve tekerlek — yükseltmenin TEK yeri.
///
/// <para><b>Neden doğrudan handler çağrılmıyor:</b> WPF'te <see cref="UIElement.MouseLeftButtonDown"/>
/// <b>Direct</b> yönlendirmelidir; "kabarma" görüntüsünü <see cref="Mouse.MouseDownEvent"/> (bubbling)
/// üzerindeki sınıf handler'ı üretir — kabarma yolundaki her öğede tek tek yeniden yükseltir ve
/// <c>Handled</c>'ı geri kopyalar. Bu yüzden testler <see cref="Mouse.MouseDownEvent"/> yükseltir: yol
/// üstündeki handler'lar üretimde hangi sırayla/koşulla koşuyorsa testte de öyle koşar.</para>
///
/// <para><b>Neden ortak:</b> aynı beş satır <c>GraphClickTests</c> (düğüm/zemin tıklaması) ve
/// <c>GraphPanZoomTests</c> (sinema modunda zemin basışının seçimi KALDIRMADIĞI) tarafından kullanılır —
/// kopya YASAK (CLAUDE.md). Tekerleğin iki fazlı sentezi de öyle: <c>AboutDialogTests</c> (Environment hücreleri) ve
/// <c>RealizedNarrative</c> (konsol) aynı <see cref="Wheel"/>'i kullanır.</para>
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

    /// <summary>
    /// Sol tuş basışının TÜNELLEYEN yarısı. <see cref="UIElement.PreviewMouseLeftButtonDown"/> de
    /// <b>Direct</b>'tir ve tünelleme görüntüsünü <see cref="Mouse.PreviewMouseDownEvent"/> üzerindeki sınıf
    /// handler'ı üretir — <see cref="PressLeft"/> ile AYNI gerekçe, yalnız yön ters.
    ///
    /// <para>Ayrı bir metot olmasının nedeni: bir <c>ButtonBase</c>'in tıkı YUTULABİLİR olması yalnız önizleme
    /// yolunda karara bağlanır (handled bir önizleme basışında ButtonBase mouse'u hiç yakalamaz, dolayısıyla
    /// bırakmada <c>Click</c> de doğmaz). Kabarma yarısını yükselten test bunu göremez.</para>
    /// </summary>
    public static MouseButtonEventArgs PreviewPressLeft(UIElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent,
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

    /// <summary>
    /// Tekerleğin bir hareketi (<paramref name="delta"/>: pozitif yukarı). HWND/SendMessage gerekmez — WPF düz dikey tekerleği zaten
    /// routed event olarak dağıtır (yatay tekerlek ayrıdır, bkz. <c>HorizontalWheelScroll</c>).
    ///
    /// <para><b>İKİ faz raise edilir, çünkü WPF'in <c>InputManager</c>'ı da öyle yapar:</b> önce tünel (<c>PreviewMouseWheel</c>),
    /// önizleme YUTMADIYSA baloncuk (<c>MouseWheel</c>). Baloncuk fazı ŞARTTIR: <c>ScrollViewer</c>'ın olayı işleyip yutan class
    /// handler'ı (<c>OnMouseWheel</c>) YALNIZ orada koşar — tek başına önizleme raise etmek kaydırmayı hiç sürmez. Dönen önizleme
    /// argümanıyla <c>Handled</c> sınanabilir.</para>
    /// </summary>
    public static MouseWheelEventArgs Wheel(UIElement target, int delta)
    {
        ArgumentNullException.ThrowIfNull(target);
        var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent,
        };
        target.RaiseEvent(preview);
        if (!preview.Handled)
        {
            target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
            });
        }
        return preview;
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
