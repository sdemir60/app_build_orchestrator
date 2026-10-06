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

    /// <summary>Yatay tekerlek / precision touchpad'in iki parmakla yatay kaydırması. WPF bu mesajı HİÇ
    /// dağıtmaz (yalnız <c>WM_MOUSEWHEEL</c> bir routed event'e çevrilir), bu yüzden yatay kaydırma
    /// uygulamanın kendi hook'undan geçer — bkz. <see cref="Controls.HorizontalWheelScroll"/>.</summary>
    public const int WM_MOUSEHWHEEL = 0x020E;

    /// <summary>
    /// [feasibility §4.3] Tepside bekleyen ilk instance BACKGROUND'dur; kendi <c>Activate()</c>'ı çoğu durumda
    /// yalnız taskbar'ı yakıp söndürür. İKİNCİ instance, sinyali göndermeden ÖNCE bunu çağırarak öne gelme
    /// hakkını ilk instance'a devreder.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(int dwProcessId);

    /// <summary>Global kısayollar (<see cref="GlobalHotkeys"/>). Başarısızlık = çakışma → SESSİZ devre dışı (bkz.
    /// <see cref="HotkeyRegistration"/>).</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(nint hWnd, int id);

    // --- imleç zaman aşımı (SystemParametersInfo)

    /// <summary><c>SPI_GETCARETTIMEOUT</c>: Windows'un imlecin kırpmayı bıraktığı girdisizlik süresi (ms).</summary>
    private const uint SPI_GETCARETTIMEOUT = 0x2022;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out uint pvParam, uint fWinIni);

    /// <summary>İşletim sisteminin imleç zaman aşımı; okunamazsa (ya da sıfırsa) Windows'un varsayılanı
    /// (<see cref="Controls.CaretIdleGate.DefaultTimeoutMs"/>). Kuruluşta bir kez okunur.</summary>
    public static TimeSpan CaretBlinkTimeout()
    {
        try
        {
            if (SystemParametersInfo(SPI_GETCARETTIMEOUT, 0, out uint ms, 0) && ms > 0) return TimeSpan.FromMilliseconds(ms);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        return TimeSpan.FromMilliseconds(Controls.CaretIdleGate.DefaultTimeoutMs);
    }

    // --- genişletilmiş pencere stili (tepsi build overlay'i)

    public const int GWL_EXSTYLE = -20;

    /// <summary>Pencere Alt-Tab ve görev çubuğu listesine girmez — bir araç yüzeyidir, bir uygulama değil.</summary>
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    /// <summary>Tıklandığında pencere KENDİSİ aktive olmaz. Fare olaylarını engellemez; yalnız odağın
    /// overlay'e kaçmasını önler — asıl aktivasyonu ana pencerenin kendi geri getirme yolu yapar.</summary>
    public const int WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>
    /// Pencereyi TAMAMEN tıklama-geçirgen yapar. Tepsi overlay'inde <b>BİLEREK KULLANILMAZ</b> ve yalnız
    /// "eklenmediği" test edilebilsin diye burada tanımlıdır.
    ///
    /// <para>Geçirgenlik zaten katmanlı pencereden gelir: <c>AllowsTransparency</c> açıkken işletim sistemi
    /// piksel piksel alfaya bakar ve alfası sıfır olan yerlerde tıklamayı alttaki pencereye geçirir. Bu bayrak
    /// eklenirse ayrım kalkar — logonun çizili piksellerine basmak da geçer ve "tıkla-aç" ölür.</para></summary>
    public const int WS_EX_TRANSPARENT = 0x00000020;

    /// <summary>Tepsi build overlay'inin ex-style kümesi — TEK yer (çağrı yerinde bit'ler yeniden yazılmaz,
    /// ve test kurulan kümeyi doğrudan okuyabilir).</summary>
    public const int OverlayExStyle = WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    public static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    public static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    // --- sistem diyaloğunu sahibinin üzerinde ortalama (CenteredDialog)

    public const int WH_CBT = 5;
    public const int HCBT_ACTIVATE = 5;
    public const uint GW_OWNER = 4;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int GWLP_WNDPROC = -4;
    public const uint WM_WINDOWPOSCHANGING = 0x0046;
    public const uint WM_WINDOWPOSCHANGED = 0x0047;
    public const uint WM_NCDESTROY = 0x0082;

    public delegate nint HookProc(int code, nint wParam, nint lParam);

    public delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public nint hwnd;
        public nint hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    /// <summary>Pencere yordamını değiştirir; 32 bit süreçte <c>SetWindowLongPtrW</c> dışa aktarılmaz.</summary>
    public static nint SetWindowProc(nint hWnd, nint proc) =>
        nint.Size == 8 ? SetWindowLongPtr64(hWnd, GWLP_WNDPROC, proc) : SetWindowLong(hWnd, GWLP_WNDPROC, (int)proc);

    /// <summary>Pencerenin o anki yordamı — <see cref="SetWindowProc"/>'un okuma eşi.</summary>
    public static nint GetWindowProc(nint hWnd) =>
        nint.Size == 8 ? GetWindowLongPtr64(hWnd, GWLP_WNDPROC) : GetWindowLong(hWnd, GWLP_WNDPROC);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    public static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(nint hWnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    public static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern nint GetWindow(nint hWnd, uint uCmd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);
}
