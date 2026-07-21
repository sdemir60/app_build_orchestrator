using System.Windows;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T62] Win11 Snap Layouts — WindowChrome bunu KENDİLİĞİNDEN vermez (dotnet/wpf#4825): pencere
/// <c>WM_NCHITTEST</c>'te maximize butonunun üstündeyken <c>HTMAXBUTTON</c> döndürmek ZORUNDADIR, aksi halde
/// Windows snap uçbirimini hiç açmaz. Karar tamamen aritmetiktir (buton rect'i × DPI × lParam noktası) —
/// P/Invoke'suz burada test edilir; hook'un kendisi (<c>SnapLayoutHook</c>) yalnız bu kararı sürer.
/// </summary>
public class SnapLayoutHitTestTests
{
    // WM_NCHITTEST lParam kodlaması: düşük 16 bit = ekran X, yüksek 16 bit = ekran Y (ikisi de İŞARETLİ).
    private static nint LParam(int x, int y) => (nint)(((y & 0xFFFF) << 16) | (x & 0xFFFF));

    [Fact]
    public void Screen_point_is_decoded_from_the_low_and_high_words_of_lparam()
        => Assert.Equal(new Point(1268, 69), SnapLayout.ScreenPointFromLParam(LParam(1268, 69)));

    [Fact]
    public void Negative_screen_coordinates_stay_negative() // soldaki/üstteki ikinci monitör — işaretsiz okuma 65486 verirdi
        => Assert.Equal(new Point(-50, -20), SnapLayout.ScreenPointFromLParam(LParam(-50, -20)));

    [Fact]
    public void Button_rect_scales_the_dip_size_to_physical_pixels()
    {
        // Sol-üst zaten fiziksel px'tir (Visual.PointToScreen); YALNIZ boyut DIP→px ölçeklenir.
        var rect = SnapLayout.ButtonRectPx(new Point(1200, 10), new Size(46, 40), dpiScale: 1.5);

        Assert.Equal(new Rect(1200, 10, 69, 60), rect);
    }

    [Theory]
    [InlineData(1200, 10, true)]    // sol-üst köşe dahil
    [InlineData(1268, 69, true)]    // sağ-alt sınırın 1px içi
    [InlineData(1269, 40, false)]   // sağ kenarın DIŞI (rect right = 1269, dışlayıcı)
    [InlineData(1240, 70, false)]   // alt kenarın DIŞI
    [InlineData(1199, 40, false)]   // solu — min butonu bölgesi
    public void Hit_test_returns_htmaxbutton_only_inside_the_scaled_button_rect(int x, int y, bool expectedMax)
    {
        var rect = SnapLayout.ButtonRectPx(new Point(1200, 10), new Size(46, 40), dpiScale: 1.5);

        int? result = SnapLayout.HitTest(rect, LParam(x, y));

        Assert.Equal(expectedMax ? SnapLayout.HTMAXBUTTON : null, result);
    }

    [Fact]
    public void Hit_test_passes_through_when_the_button_has_no_measured_size() // pencere gizli/ölçülmemiş
        => Assert.Null(SnapLayout.HitTest(SnapLayout.ButtonRectPx(new Point(0, 0), new Size(0, 0), 1.0), LParam(0, 0)));
}
