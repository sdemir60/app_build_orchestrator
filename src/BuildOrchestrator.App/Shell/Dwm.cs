using System.Runtime.InteropServices;

namespace BuildOrchestrator.App.Shell;

internal static class Dwm
{
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33; // 2 = DWMWCP_ROUND
    public const int DWMWA_BORDER_COLOR = 34;             // COLORREF 0x00BBGGRR
    public const int SM_CXSIZEFRAME = 32, SM_CYSIZEFRAME = 33, SM_CXPADDEDBORDER = 92;

    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")] public static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
