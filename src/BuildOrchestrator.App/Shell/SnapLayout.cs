using System.Windows;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [T62 / feasibility §3.2] Win11 <b>Snap Layouts</b> uçbiriminin SAF karar aritmetiği. <c>WindowChrome</c> bunu
/// kendiliğinden VERMEZ (dotnet/wpf#4825): Windows, snap uçbirimini yalnız pencere <c>WM_NCHITTEST</c>'te
/// <see cref="HTMAXBUTTON"/> döndürdüğünde açar. Karar = "lParam'daki ekran noktası, maximize butonunun
/// DPI-ölçekli ekran rect'inin içinde mi".
///
/// <para>Buradaki her şey P/Invoke'suzdur (test edilebilir); mesaj pompası ve hover görselini
/// <see cref="SnapLayoutHook"/> sürer.</para>
/// </summary>
public static class SnapLayout
{
    /// <summary>Non-client hit-test sonucu "maximize/restore butonu" — Snap Layouts'un TEK tetikleyicisi.</summary>
    public const int HTMAXBUTTON = 9;

    /// <summary>
    /// <c>WM_NCHITTEST</c>/<c>WM_NCLBUTTON*</c> lParam'ı → ekran noktası (FİZİKSEL piksel). Düşük 16 bit X,
    /// yüksek 16 bit Y ve <b>ikisi de işaretlidir</b>: birincil monitörün solundaki/üstündeki bir monitörde
    /// koordinatlar negatiftir, işaretsiz okuma 65486 gibi değerler üretip hit-test'i bozardı.
    /// </summary>
    public static Point ScreenPointFromLParam(nint lParam)
    {
        long value = lParam;
        return new Point(unchecked((short)(value & 0xFFFF)), unchecked((short)((value >> 16) & 0xFFFF)));
    }

    /// <summary>
    /// Butonun ekran rect'i (fiziksel piksel). <paramref name="topLeftPx"/> <c>Visual.PointToScreen</c>'den gelir
    /// ve ZATEN fizikseldir; yalnız WPF'in DIP cinsinden bildiği boyut ölçeklenir (PerMonitorV2'de dpiScale
    /// pencere başına değişir).
    /// </summary>
    public static Rect ButtonRectPx(Point topLeftPx, Size sizeDip, double dpiScale)
        => new(topLeftPx, new Size(sizeDip.Width * dpiScale, sizeDip.Height * dpiScale));

    /// <summary>
    /// Sol/üst kenar DAHİL, sağ/alt kenar HARİÇ. <b>Bilerek <c>Rect.Contains</c> KULLANILMAZ:</b> WPF'in
    /// <c>Contains</c>'i sağ/alt kenarı da DAHİL sayar; bu, bitişik close butonunun ilk piksel sütununu da
    /// maximize bölgesi yapar (snap uçbirimi yanlış butonda açılır). Boş/ölçülmemiş rect hiçbir noktayı içermez.
    /// </summary>
    public static bool IsOverMaximizeButton(Rect buttonRectPx, Point screenPointPx)
        => buttonRectPx.Width > 0 && buttonRectPx.Height > 0
           && screenPointPx.X >= buttonRectPx.X && screenPointPx.X < buttonRectPx.X + buttonRectPx.Width
           && screenPointPx.Y >= buttonRectPx.Y && screenPointPx.Y < buttonRectPx.Y + buttonRectPx.Height;

    /// <summary>Tek karar kapısı: <see cref="HTMAXBUTTON"/> (snap uçbirimi) ya da <c>null</c> = mesajı olduğu gibi
    /// WindowChrome/DefWindowProc'a bırak.</summary>
    public static int? HitTest(Rect buttonRectPx, nint lParam)
        => IsOverMaximizeButton(buttonRectPx, ScreenPointFromLParam(lParam)) ? HTMAXBUTTON : null;
}
