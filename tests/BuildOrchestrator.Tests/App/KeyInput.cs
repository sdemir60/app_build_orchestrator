using System.Windows;
using System.Windows.Input;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [B2 fix 3 · madde 7] Testlerde GERÇEK bir klavye olayı yükseltmenin TEK yeri — <see cref="MouseInput"/>'un klavye eşi. Olay hedefin sunum
/// kaynağıyla (<see cref="PresentationSource"/>) ve birincil klavye aygıtıyla kurulur: hedef gösterilen bir pencerede olmalıdır — gösterilmeyen
/// bir pencerede (kabuk testleri) kaynak dışarıdan verilir. Tünelleyen önizleme (<c>PreviewKeyDown</c>) kaydırma sinyalinin
/// (<c>UserScrollSignal</c>) dinlediği ham girdidir; kabarcıklanan <c>KeyDown</c> editörün bağlamalarını <c>CommandManager</c> üstünden
/// eşleştirir. Dönen argümanla <c>Handled</c> sınanabilir.
/// <para>[son temizlik · madde 6 · O1] Tuş olayını elle kuran sekiz test yeri (Esc, ok, Enter/Space basışları; kabuğun iki fazlı basışı)
/// buraya toplandı — <c>new KeyEventArgs(…)</c> testlerde yalnız burada yazılır.</para>
/// </summary>
internal static class KeyInput
{
    /// <summary><paramref name="key"/>'in <paramref name="routedEvent"/> olayı (<see cref="Keyboard.PreviewKeyDownEvent"/> ya da
    /// <see cref="Keyboard.KeyDownEvent"/>) <paramref name="target"/>'ten yükseltilir.</summary>
    /// <param name="source">Olayın sunum kaynağı — verilmezse hedefinki (hedef gösterilen bir pencerede olmalıdır).</param>
    public static KeyEventArgs Raise(UIElement target, Key key, RoutedEvent routedEvent, PresentationSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source ?? PresentationSource.FromVisual(target)!, 0, key) { RoutedEvent = routedEvent };
        target.RaiseEvent(args);
        return args;
    }

    /// <summary>Tuşun tünelleyen yarısı — kaydırma sinyalinin gördüğü ham girdi.</summary>
    public static KeyEventArgs PreviewKeyDown(UIElement target, Key key) => Raise(target, key, Keyboard.PreviewKeyDownEvent);

    /// <summary>Bir tuşa WPF'in girdi yöneticisi gibi basar: önce tünelleyen <c>PreviewKeyDown</c>, sonra AYNI argümanla kabarcıklanan
    /// <c>KeyDown</c> — hedefin <see cref="KeyBinding"/>'leri ve tuş işleyicileri ikincisinde, yalnız olay handled değilse çalışır.</summary>
    /// <param name="source">Olayın sunum kaynağı — verilmezse hedefinki.</param>
    public static KeyEventArgs Press(UIElement target, Key key, PresentationSource? source = null)
    {
        var args = Raise(target, key, Keyboard.PreviewKeyDownEvent, source);
        args.RoutedEvent = Keyboard.KeyDownEvent;
        target.RaiseEvent(args);
        return args;
    }
}
