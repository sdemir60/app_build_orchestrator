using System.Windows;
using System.Windows.Media;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [T62 / feasibility §3.2] <see cref="SnapLayout"/> kararını gerçek pencere mesaj pompasına bağlayan İNCE hook.
///
/// <para><b>Neden gerekli:</b> WindowChrome, Win11 Snap Layouts uçbirimini vermez (dotnet/wpf#4825). Windows
/// uçbirimi yalnız <c>WM_NCHITTEST</c>'e <c>HTMAXBUTTON</c> yanıtı verildiğinde açar.</para>
///
/// <para><b>Bedeli:</b> o bölge artık NON-CLIENT'tır — WPF butonu normal fare olaylarını (IsMouseOver, Click)
/// ARTIK ALMAZ. Bu yüzden hover görseli elle sürülür (<c>setHover</c>) ve tıklama
/// <c>WM_NCLBUTTONDOWN</c>/<c>WM_NCLBUTTONUP</c> çiftinden üretilir: basma non-client'ta yutulur (yoksa
/// DefWindowProc sistem menüsü/başlık sürükleme davranışına girer), maximize/restore YALNIZ aynı bölgede biten
/// bırakma ile tetiklenir (basıp dışarı kaydırınca iptal — gerçek buton semantiği).</para>
/// </summary>
internal sealed class SnapLayoutHook(FrameworkElement maxButton, Action<bool> setHover, Action toggleMaximizeRestore)
{
    private bool _hover;
    private bool _pressed;

    /// <summary><c>HwndSource.AddHook</c> imzası. Yalnız ilgilendiği mesajlarda <paramref name="handled"/> set eder;
    /// diğer her şey WindowChrome/DefWindowProc'a dokunulmadan geçer.</summary>
    public nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case Win32.WM_NCHITTEST:
                int? hit = SnapLayout.HitTest(ButtonRectPx(), lParam);
                SetHover(hit is not null);
                if (hit is null) return 0; // handled=false → mesaj WindowChrome'a gider
                handled = true;
                return hit.Value;

            case Win32.WM_NCMOUSELEAVE: // fare pencereden/non-client alandan çıktı — hover kalıntısı bırakma
            // [M-4 fix wave] Hover/basılı kalıntısı YALNIZ WM_NCMOUSELEAVE'e ya da bir sonraki WM_NCHITTEST'e
            // bağlıysa, düğmeden pencere DIŞINA hızlı fare hareketi (ör. non-client'tan doğrudan ekran dışına)
            // ikisini de tetiklemeyebilir. WM_MOUSELEAVE (client-area) ve WM_CAPTURECHANGED (capture başka
            // pencereye/kontrole geçti — basılıyken Alt+Tab, sistem menüsü vb.) AYNI temizliği tetikler.
            case Win32.WM_MOUSELEAVE:
            case Win32.WM_CAPTURECHANGED:
                SetHover(false);
                _pressed = false;
                return 0;

            case Win32.WM_NCLBUTTONDOWN when (int)wParam == SnapLayout.HTMAXBUTTON:
                _pressed = true;
                handled = true; // DefWindowProc'a bırakılırsa başlık çubuğu davranışına girer
                return 0;

            case Win32.WM_NCLBUTTONUP when (int)wParam == SnapLayout.HTMAXBUTTON:
                handled = true;
                if (!_pressed) return 0;
                _pressed = false;
                toggleMaximizeRestore();
                return 0;

            // [M-4 fix wave] Basma maximize butonunda başladı ama bırakma BAŞKA bir non-client bölgede bitti
            // (ör. basılı sürükleyip başlık çubuğuna/kenara taşıdı) — bu olay bize ait DEĞİL (handled=false,
            // DefWindowProc/WindowChrome görsün) ama stale _pressed'i temizlemezsek sonraki bağımsız bir
            // maximize-butonu basımı olmadan beklenmeyen bir toggle üretebilirdi.
            case Win32.WM_NCLBUTTONUP:
                _pressed = false;
                return 0;

            default:
                return 0;
        }
    }

    private void SetHover(bool on)
    {
        if (_hover == on) return;
        _hover = on;
        setHover(on);
    }

    /// <summary>Butonun O ANKİ ekran rect'i (fiziksel px). Pencere gizliyken/ölçülmemişken
    /// <see cref="Visual.PointToScreen"/> geçersizdir → boş rect (hit-test pass-through).</summary>
    private Rect ButtonRectPx()
    {
        if (!maxButton.IsVisible || maxButton.ActualWidth <= 0 || maxButton.ActualHeight <= 0) return Rect.Empty;
        try
        {
            return SnapLayout.ButtonRectPx(
                maxButton.PointToScreen(new Point(0, 0)),
                new Size(maxButton.ActualWidth, maxButton.ActualHeight),
                VisualTreeHelper.GetDpi(maxButton).DpiScaleX);
        }
        catch (InvalidOperationException) { return Rect.Empty; } // görsel ağaçta/HwndSource'ta değil
    }
}
