using System.Windows.Media;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T49] <see cref="Dwm.ColorRefFrom"/>: WPF <c>Color</c> → Win32 COLORREF (<c>0x00BBGGRR</c>). MainWindow'un
/// <c>DWMWA_BORDER_COLOR</c> çağrısı artık hardcoded sabit yerine <c>Brush.Border</c> token'ından beslendiği
/// için bayt sırasının (RGB'nin TERSİ) doğruluğu burada pinlenir — ters çevrilirse kenarlık YANLIŞ renkte
/// çizilir ve bunu hiçbir derleme hatası yakalamaz.
/// </summary>
public sealed class DwmColorRefTests
{
    [Fact]
    public void Border_token_colour_becomes_the_expected_bgr_colorref()
    {
        // Brush.Border = #2a2a30 → R=0x2a, G=0x2a, B=0x30 → 0x00BBGGRR = 0x00302A2A
        Assert.Equal(0x0030_2A2A, Dwm.ColorRefFrom(Color.FromRgb(0x2a, 0x2a, 0x30)));
    }

    [Fact]
    public void Channels_are_reversed_not_copied_and_alpha_is_dropped()
    {
        // Asimetrik renk: düz RGB kopyası (0x00FF8000) ile karışamaz.
        Assert.Equal(0x0000_80FF, Dwm.ColorRefFrom(Color.FromArgb(0x7F, 0xFF, 0x80, 0x00)));
    }
}
