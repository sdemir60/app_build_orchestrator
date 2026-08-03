using System.Runtime.InteropServices;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [T62] Pencere kabuğunun user32 P/Invoke yüzeyi — <see cref="Dwm"/> ile aynı üslupta İNCE tutulur: burada karar
/// mantığı YOKTUR, kararlar saf yardımcılardadır (<see cref="HotkeyBinding"/>, <see cref="SingleInstanceProtocol"/>).
/// </summary>
internal static class Win32
{
    // --- pencere mesajları (WndProc hook'unun tanıdıkları)
    public const int WM_HOTKEY = 0x0312;

    /// <summary>
    /// [feasibility §4.3] Tepside bekleyen ilk instance BACKGROUND'dur; kendi <c>Activate()</c>'ı çoğu durumda
    /// yalnız taskbar'ı yakıp söndürür. İKİNCİ instance, sinyali göndermeden ÖNCE bunu çağırarak öne gelme
    /// hakkını ilk instance'a devreder.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(int dwProcessId);

    /// <summary>Global kısayol (Alt+B). Başarısızlık = çakışma → SESSİZ devre dışı (bkz.
    /// <see cref="HotkeyRegistration"/>).</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(nint hWnd, int id);
}
