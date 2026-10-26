using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [kullanıcı kararı 2026-10-19 · 2026-10-26] Pencereyi arka plandan öne getiren basışın iki saf sorusu: hangi tuşla basıldı
/// (<see cref="ButtonOf"/>) ve neye indi (<see cref="HitAt"/>). Karar burada DEĞİLDİR — basışın indiği kontrolün sahibine sorulur
/// (<c>MainWindow.StartsOwnRefreshAt</c>).
/// </summary>
internal static class ActivatingClick
{
    /// <summary><c>WM_MOUSEACTIVATE</c>'in <c>lParam</c>'ı: düşük söz isabet kodu, yüksek söz basışın fare mesajı. Yalnız istemci alanına
    /// (<see cref="Win32.HTCLIENT"/>) basış bir tuş verir: <see cref="Win32.WM_LBUTTONDOWN"/> → sol, <see cref="Win32.WM_RBUTTONDOWN"/> →
    /// sağ (satırın menüsü). Başka her şey — title bar zemini, orta tuş — <c>null</c>: dönüş bugünkü gibi Sync'ler.</summary>
    public static MouseButton? ButtonOf(nint lParam)
    {
        if ((lParam & 0xFFFF) != Win32.HTCLIENT) return null;
        return (int)((lParam >> 16) & 0xFFFF) switch
        {
            Win32.WM_LBUTTONDOWN => MouseButton.Left,
            Win32.WM_RBUTTONDOWN => MouseButton.Right,
            _ => null,
        };
    }

    /// <summary>Kökün <paramref name="point"/>'teki isabeti. <c>InputHitTest</c> kapalı öğeyi atlar: isabet onu taşıyan açık ataya düşer —
    /// kapalı bir düğmeye inen tık hiçbir şey başlatmaz ve sayılmaz. İsabet bir <see cref="ContentElement"/> ise (ör. bir
    /// <c>TextBlock</c>'un içindeki <c>Run</c>) mantıksal ebeveynlerinden ilk <see cref="Visual"/> döner; yoksa <c>null</c>.</summary>
    /// <param name="point">Köke göre konum.</param>
    public static Visual? HitAt(UIElement root, Point point)
    {
        var hit = root.InputHitTest(point) as DependencyObject;
        while (hit is ContentElement content) hit = LogicalTreeHelper.GetParent(content);
        return hit as Visual;
    }
}
