using System.Windows.Input;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı kararı 2026-10-26] Pencereyi arka plandan öne getiren basışın tuşu <c>WM_MOUSEACTIVATE</c>'in <c>lParam</c>'ından okunur
/// (<see cref="ActivatingClick.ButtonOf"/>): düşük söz isabet kodu, yüksek söz basışın fare mesajı. Yalnız istemci alanına sol ya da sağ
/// basış bir tuş verir — sağ basış satırın menüsünü açar ve satırda dönüşün Sync'ini atlar (<c>WindowReturnClickTests</c>). Title bar
/// zemini (<see cref="Win32.HTCAPTION"/>) ve orta tuş tuş vermez: dönüş bugünkü gibi Sync'ler. Saf, WPF'siz.
/// </summary>
public sealed class ActivatingClickTests
{
    /// <summary>Orta tuş basıldı — üretimin tanımadığı tek örnek, yalnız burada gerekir.</summary>
    private const int WM_MBUTTONDOWN = 0x0207;

    /// <summary><c>WM_MOUSEACTIVATE</c>'in <c>lParam</c>'ı: yüksek söz fare mesajı, düşük söz isabet kodu.</summary>
    private static nint LParam(int hitTest, int mouseMessage) => (nint)((mouseMessage << 16) | hitTest);

    [Theory]
    [InlineData(Win32.HTCLIENT, Win32.WM_LBUTTONDOWN, MouseButton.Left)]
    [InlineData(Win32.HTCLIENT, Win32.WM_RBUTTONDOWN, MouseButton.Right)]
    [InlineData(Win32.HTCAPTION, Win32.WM_LBUTTONDOWN, null)]
    [InlineData(Win32.HTCLIENT, WM_MBUTTONDOWN, null)]
    public void Only_a_left_or_right_press_on_the_client_area_is_an_activating_press(int hitTest, int mouseMessage, MouseButton? expected) =>
        Assert.Equal(expected, ActivatingClick.ButtonOf(LParam(hitTest, mouseMessage)));
}
