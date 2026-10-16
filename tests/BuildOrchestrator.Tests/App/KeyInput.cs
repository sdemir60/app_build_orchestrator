using System.Windows;
using System.Windows.Input;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [B2 fix 3 · madde 7] Testlerde GERÇEK bir klavye olayı yükseltmenin TEK yeri — <see cref="MouseInput"/>'un klavye eşi. Olay hedefin sunum
/// kaynağıyla (<see cref="PresentationSource"/>) ve birincil klavye aygıtıyla kurulur: hedef gösterilen bir pencerede olmalıdır. Tünelleyen
/// önizleme (<c>PreviewKeyDown</c>) kaydırma sinyalinin (<c>UserScrollSignal</c>) dinlediği ham girdidir; kabarcıklanan <c>KeyDown</c>
/// editörün bağlamalarını <c>CommandManager</c> üstünden eşleştirir. Dönen argümanla <c>Handled</c> sınanabilir.
/// </summary>
internal static class KeyInput
{
    /// <summary><paramref name="key"/>'in <paramref name="routedEvent"/> olayı (<see cref="Keyboard.PreviewKeyDownEvent"/> ya da
    /// <see cref="Keyboard.KeyDownEvent"/>) <paramref name="target"/>'ten yükseltilir.</summary>
    public static KeyEventArgs Raise(UIElement target, Key key, RoutedEvent routedEvent)
    {
        ArgumentNullException.ThrowIfNull(target);
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key) { RoutedEvent = routedEvent };
        target.RaiseEvent(args);
        return args;
    }

    /// <summary>Tuşun tünelleyen yarısı — kaydırma sinyalinin gördüğü ham girdi.</summary>
    public static KeyEventArgs PreviewKeyDown(UIElement target, Key key) => Raise(target, key, Keyboard.PreviewKeyDownEvent);
}
