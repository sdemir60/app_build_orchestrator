using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [kullanıcı kararı 2026-09-29] Bir sistem diyaloğunu (dosya seçici) sahibinin ÜZERİNDE ortalayan kapsam:
/// <c>using (CenteredDialog.Over(window)) dialog.ShowDialog(window);</c>. Settings diyaloğu da pencerede ortalı
/// olduğu için seçici onun tam üstüne gelir.
///
/// <para><b>Neden bu yol (ölçüldü — <c>FileDialogCenteringProbeTests</c>):</b> Windows dosya seçicisinin konumunu
/// dışarıya açmaz. Seçici önce küçük boyutuyla ve GİZLİ etkinleşir; yüzlerce milisaniye sonra, hâlâ gizliyken,
/// kaydedilmiş boyutuna geçer ve konumunu KENDİSİ yeniden yazar, ardından görünür olur. Etkinleşme anında yapılan
/// bir taşıma bu son yazımla ezilir. Kapsam bu yüzden UI thread'ine bir <c>WH_CBT</c> kancası kurar; sahibin sahip
/// olduğu pencere etkinleşince onun pencere yordamını geçici olarak devralır ve pencere GÖRÜNÜR OLANA KADAR her
/// <c>WM_WINDOWPOSCHANGING</c>'de konumu, gelen nihai boyuta göre merkeze yazar. Görünür olduğu anda devir bırakılır:
/// kullanıcı seçiciyi sonra serbestçe taşır ve ekranda sıçrama olmaz (konum çizimden ÖNCE yazılır).</para>
///
/// <para>Konum kararı SAF <see cref="DialogPlacement.CenterOver"/>'dadır; burada yalnız Win32 okuma/yazması vardır
/// (<see cref="Win32"/> deseni). Kanca ve devir, kapsam kapanınca (seçici hiç açılmadıysa bile) bırakılır.</para>
/// </summary>
internal sealed class CenteredDialog : IDisposable
{
    private readonly nint _owner;
    private readonly Win32.HookProc _hookProc;    // kanca yaşadıkça delege GC'den korunmalı
    private readonly Win32.WndProc _dialogProc;   // devir sürdükçe delege GC'den korunmalı
    private nint _hook;
    private nint _dialog;
    private nint _previousProc;

    private CenteredDialog(nint owner)
    {
        _owner = owner;
        _hookProc = OnCbt;
        _dialogProc = OnDialogMessage;
        if (owner != 0) _hook = Win32.SetWindowsHookEx(Win32.WH_CBT, _hookProc, 0, Win32.GetCurrentThreadId());
    }

    /// <summary>Sahibi <paramref name="owner"/> olan bir sonraki diyaloğu onun üzerinde ortalar. Pencerenin henüz bir
    /// HWND'si yoksa kapsam hiçbir şey yapmaz.</summary>
    public static IDisposable Over(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return new CenteredDialog(new WindowInteropHelper(owner).Handle);
    }

    private nint OnCbt(int code, nint wParam, nint lParam)
    {
        if (code == Win32.HCBT_ACTIVATE && _hook != 0 && _dialog == 0
            && Win32.GetWindow(wParam, Win32.GW_OWNER) == _owner && !Win32.IsWindowVisible(wParam))
        {
            _dialog = wParam;
            _previousProc = Win32.SetWindowProc(wParam, Marshal.GetFunctionPointerForDelegate(_dialogProc));
            Unhook(); // kapsam TEK diyalog içindir
        }
        return Win32.CallNextHookEx(0, code, wParam, lParam);
    }

    private nint OnDialogMessage(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        nint previous = _previousProc;
        if (msg == Win32.WM_WINDOWPOSCHANGING && !Win32.IsWindowVisible(hwnd)) Centre(hwnd, lParam);
        nint result = Win32.CallWindowProc(previous, hwnd, msg, wParam, lParam);
        if ((msg == Win32.WM_WINDOWPOSCHANGED && Win32.IsWindowVisible(hwnd)) || msg == Win32.WM_NCDESTROY) Release();
        return result;
    }

    /// <summary>Gelen yerleşimin konumunu, nihai boyuta (değişmiyorsa bugünküne) göre sahibin merkezine yazar.</summary>
    private void Centre(nint dialog, nint lParam)
    {
        var pos = Marshal.PtrToStructure<Win32.WINDOWPOS>(lParam);
        if (!Win32.GetWindowRect(_owner, out var owner) || !Win32.GetWindowRect(dialog, out var now)) return;
        var monitor = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(Win32.MonitorFromWindow(_owner, Win32.MONITOR_DEFAULTTONEAREST), ref monitor)) return;

        bool keepsSize = (pos.flags & Win32.SWP_NOSIZE) != 0;
        int width = keepsSize ? now.Right - now.Left : pos.cx;
        int height = keepsSize ? now.Bottom - now.Top : pos.cy;
        (pos.x, pos.y) = DialogPlacement.CenterOver(ToRect(owner), width, height, ToRect(monitor.rcWork));
        pos.flags &= ~Win32.SWP_NOMOVE;
        Marshal.StructureToPtr(pos, lParam, false);
    }

    private static Int32Rect ToRect(Win32.RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    private void Unhook()
    {
        if (_hook == 0) return;
        Win32.UnhookWindowsHookEx(_hook);
        _hook = 0;
    }

    /// <summary>Devri bırakır: diyaloğun kendi pencere yordamı geri konur.</summary>
    private void Release()
    {
        if (_dialog == 0) return;
        Win32.SetWindowProc(_dialog, _previousProc);
        _dialog = 0;
    }

    public void Dispose()
    {
        Unhook();
        Release();
    }
}
