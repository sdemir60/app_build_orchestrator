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

    /// <summary>Global kısayol (Alt+B). Başarısızlık = çakışma → SESSİZ devre dışı (bkz.
    /// <see cref="HotkeyRegistration"/>).</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(nint hWnd, int id);

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
}
