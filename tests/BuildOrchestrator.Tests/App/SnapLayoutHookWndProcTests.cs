using System.Windows.Controls;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T62/M-5 fix wave] <see cref="SnapLayoutHitTestTests"/> yalnız SAF hit-test aritmetiğini
/// (<see cref="SnapLayout.HitTest"/>) kapsar. Pencere sürüklemesini/yeniden-boyutlandırmasını FİİLEN
/// kırabilecek olan şey <c>handled</c> bayrağıdır — ve bu, hiçbir testte doğrulanmıyordu. <see cref="SnapLayoutHook"/>
/// <c>internal</c>'dır; <c>InternalsVisibleTo</c> (BuildOrchestrator.App.csproj) BuildOrchestrator.Tests'e zaten
/// açık — <c>WndProc</c> burada DOĞRUDAN sürülür (P/Invoke'suz, gerçek HWND'siz).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class SnapLayoutHookWndProcTests
{
    private static nint LParam(int x, int y) => (nint)(((y & 0xFFFF) << 16) | (x & 0xFFFF));

    [StaFact]
    public void Point_outside_the_maximize_button_passes_through_unhandled()
    {
        // Ölçülmemiş/görsel ağaca eklenmemiş buton → SnapLayoutHook.ButtonRectPx() HER ZAMAN Rect.Empty döner,
        // yani HER nokta "dışarıda" — pass-through garanti. handled=true kalsaydı WindowChrome'un caption-drag
        // ve resize-border yanıtı bu mesaj için hiç ÇALIŞMAZDI.
        var button = new Button();
        var hook = new SnapLayoutHook(button, _ => { }, () => { });
        bool handled = false;

        nint result = hook.WndProc(hwnd: 0, Win32.WM_NCHITTEST, wParam: 0, LParam(500, 20), ref handled);

        Assert.False(handled);
        Assert.Equal(0, result);
    }

    [StaFact]
    public void Nclbuttonup_without_a_preceding_nclbuttondown_does_not_toggle()
    {
        var button = new Button();
        int toggleCalls = 0;
        var hook = new SnapLayoutHook(button, _ => { }, () => toggleCalls++);
        bool handled = false;

        // _pressed hiç true olmadı (WM_NCLBUTTONDOWN gelmedi) — tek başına bir WM_NCLBUTTONUP (ör. senkronize
        // olmayan/kaybolmuş bir DOWN'dan sonra) sahte bir maximize/restore toggle'ı ÜRETMEMELİ.
        hook.WndProc(hwnd: 0, Win32.WM_NCLBUTTONUP, (nint)SnapLayout.HTMAXBUTTON, lParam: 0, ref handled);

        Assert.Equal(0, toggleCalls);
    }

    [StaFact]
    public void Matching_down_then_up_on_the_maximize_button_toggles_exactly_once()
    {
        var button = new Button();
        int toggleCalls = 0;
        var hook = new SnapLayoutHook(button, _ => { }, () => toggleCalls++);
        bool handledDown = false, handledUp = false;

        hook.WndProc(hwnd: 0, Win32.WM_NCLBUTTONDOWN, (nint)SnapLayout.HTMAXBUTTON, lParam: 0, ref handledDown);
        hook.WndProc(hwnd: 0, Win32.WM_NCLBUTTONUP, (nint)SnapLayout.HTMAXBUTTON, lParam: 0, ref handledUp);

        Assert.True(handledDown);
        Assert.True(handledUp);
        Assert.Equal(1, toggleCalls);
    }

    [StaFact] // [M-4 fix wave] regresyon: capture kaybı basılı bayrağını temizlemeli
    public void Capture_lost_while_pressed_clears_the_stale_press_so_a_later_up_does_not_toggle()
    {
        var button = new Button();
        int toggleCalls = 0;
        var hook = new SnapLayoutHook(button, _ => { }, () => toggleCalls++);
        bool handled = false;

        hook.WndProc(hwnd: 0, Win32.WM_NCLBUTTONDOWN, (nint)SnapLayout.HTMAXBUTTON, lParam: 0, ref handled); // basıldı
        hook.WndProc(hwnd: 0, Win32.WM_CAPTURECHANGED, wParam: 0, lParam: 0, ref handled); // capture başka yere gitti
        hook.WndProc(hwnd: 0, Win32.WM_NCLBUTTONUP, (nint)SnapLayout.HTMAXBUTTON, lParam: 0, ref handled); // gecikmiş bırakma

        Assert.Equal(0, toggleCalls); // WM_CAPTURECHANGED basılı bayrağını temizlemiş olmalı — sahte toggle YOK
    }

    [StaFact] // [M-4 fix wave] regresyon: başka bir NC bölgede biten bırakma stale basılı bayrağı temizlemeli
    public void Release_on_a_different_nonclient_region_clears_the_stale_press_without_toggling()
    {
        var button = new Button();
        int toggleCalls = 0;
        var hook = new SnapLayoutHook(button, _ => { }, () => toggleCalls++);
        bool handled = false;
        const int HTCAPTION = 2;

        hook.WndProc(hwnd: 0, Win32.WM_NCLBUTTONDOWN, (nint)SnapLayout.HTMAXBUTTON, lParam: 0, ref handled); // basıldı
        hook.WndProc(hwnd: 0, Win32.WM_NCLBUTTONUP, (nint)HTCAPTION, lParam: 0, ref handled); // başlık çubuğunda bırakıldı — _pressed BURADA temizlenmeli
        // Temizlenmeseydi, bununla İLGİSİZ, sonraki (kendi başına DOWN'suz) bir maximize-butonu UP'ı yanlışlıkla toggle ederdi:
        hook.WndProc(hwnd: 0, Win32.WM_NCLBUTTONUP, (nint)SnapLayout.HTMAXBUTTON, lParam: 0, ref handled);

        Assert.Equal(0, toggleCalls);
    }
}
