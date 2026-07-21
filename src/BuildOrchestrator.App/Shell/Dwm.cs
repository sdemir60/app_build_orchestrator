using System.Runtime.InteropServices;
using System.Windows.Media;

namespace BuildOrchestrator.App.Shell;

internal static class Dwm
{
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33; // 2 = DWMWCP_ROUND
    public const int DWMWA_BORDER_COLOR = 34;             // COLORREF 0x00BBGGRR
    public const int SM_CXSIZEFRAME = 32, SM_CYSIZEFRAME = 33, SM_CXPADDEDBORDER = 92;

    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")] public static extern int GetSystemMetricsForDpi(int index, uint dpi);

    /// <summary>
    /// [T49] Bir WPF <see cref="Color"/>'ı Win32 COLORREF'e (<c>0x00BBGGRR</c> — RGB'nin TERSİ bayt sırası)
    /// çevirir; alfa DÜŞÜRÜLÜR (DWM kenarlığı opaktır). Böylece <see cref="DWMWA_BORDER_COLOR"/> hardcoded bir
    /// sabit yerine token brush'ından (Brush.Border) beslenebilir.
    /// </summary>
    public static int ColorRefFrom(Color color) => color.R | (color.G << 8) | (color.B << 16);
}
