using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [pencere çerçevesi] Ana pencerenin DWM çerçevesi — koyu başlık, Windows 11 köşeleri, 1 px kenarlık — ve DWM özniteliklerinin TEK
/// yazıldığı yer (kaynak guard'ı: <c>WindowFrameTests</c>). Köşeler yalnız pencere tam ekran DEĞİLKEN yuvarlaktır.
///
/// <para><b>Neden — ÖLÇÜLDÜ</b> (solda birincil 4K %100, sağda laptop %150): tam ekran pencerenin dikdörtgeni çalışma alanından her
/// kenarda çerçeve kalınlığı kadar taşar (<c>(3829,168)-(6731,1918)</c>, alan <c>(3840,179)-(6720,1907)</c>: 11 px). İçeriği WPF'in
/// pencere bölgesi alana kırpar (bölge alanla birebir), ama yuvarlak köşe tercihi açıkken DWM Windows 11 çerçevesini — yuvarlak köşe,
/// kenarlık, gölge — bölgeye değil dikdörtgene çizer: taşan 11 px komşu monitöre düşer, pencerenin kenarı, köşesi ve gölgesi orada
/// görünür ve pencere her öne geldiğinde komşu monitördeki pencerenin üstüne çıkar. Soldaki monitöre konan yeşil bir pencerenin üstünde:
/// tercih yuvarlakken 13 577 px, tam ekranda kare köşeyle 0 px. Kenarlık rengini kapatmak (<c>DWMWA_COLOR_NONE</c>) yetmedi — gölge
/// kaldı; kare köşede kenarlık da çizilmez, renk bu yüzden durumdan bağımsız tek seferde yazılır.</para>
/// </summary>
internal static class WindowFrame
{
    /// <summary>Pencerenin kaynağı kurulunca — ilk karesinden önce — üç özniteliği yazar; köşe tercihini sonra her
    /// <see cref="Window.WindowState"/> değişiminde yeniden yazar. Kaynak yokken gelen değişim atlanır, kaynakta güncel durum yazılır.
    /// DP izleyicisi, <c>StateChanged</c> DEĞİL: doğuştan tam ekran pencere ve monitörüne açılan pencere (normal doğar, kaynağında tam
    /// ekran olur) için o olay gelmez — <see cref="MaximizeFix.Bind"/> ile aynı gerekçe.</summary>
    public static void Bind(Window window, int borderColor)
    {
        window.SourceInitialized += (_, _) =>
        {
            nint hwnd = Handle(window);
            Set(hwnd, Dwm.DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
            Set(hwnd, Dwm.DWMWA_BORDER_COLOR, borderColor);
            ApplyCorners(window);
        };
        DependencyPropertyDescriptor.FromProperty(Window.WindowStateProperty, typeof(Window))
            .AddValueChanged(window, (_, _) => ApplyCorners(window));
    }

    private static void ApplyCorners(Window window)
    {
        nint hwnd = Handle(window);
        if (hwnd == 0) return;
        Set(hwnd, Dwm.DWMWA_WINDOW_CORNER_PREFERENCE,
            window.WindowState == WindowState.Maximized ? Dwm.DWMWCP_DONOTROUND : Dwm.DWMWCP_ROUND);
    }

    private static nint Handle(Window window) => new WindowInteropHelper(window).Handle;

    private static void Set(nint hwnd, int attribute, int value) => Dwm.DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
}
