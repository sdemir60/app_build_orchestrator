using System.Windows;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı kararı 2026-09-29] Sistem dosya seçicisi ana pencerenin ÜZERİNDE ortalanır — Settings diyaloğu da
/// pencerede ortalı olduğu için seçici onun tam üstüne gelir. Konum hesabı SAF bir yardımcıdadır
/// (<see cref="DialogPlacement.CenterOver"/>); Win32 kancası yalnız sonucu uygular. Koordinatlar fiziksel piksel,
/// çalışma alanı (görev çubuğu hariç) seçicinin monitörüdür.
/// </summary>
public class DialogPlacementTests
{
    private static readonly Int32Rect Screen = new(0, 0, 1920, 1040);

    [Fact]
    public void The_dialog_is_centred_over_its_owner()
    {
        var owner = new Int32Rect(100, 100, 1000, 800);

        Assert.Equal((300, 300), DialogPlacement.CenterOver(owner, 600, 400, Screen));
    }

    [Fact]
    public void A_dialog_that_would_cross_the_right_edge_is_pulled_back_inside_the_work_area()
    {
        var owner = new Int32Rect(1500, 100, 800, 600); // pencerenin sağı ekranın dışında

        Assert.Equal((1320, 200), DialogPlacement.CenterOver(owner, 600, 400, Screen));
    }

    [Fact]
    public void A_dialog_that_would_cross_the_top_left_corner_starts_at_the_work_area_origin()
    {
        var owner = new Int32Rect(-300, -200, 400, 300);

        Assert.Equal((0, 0), DialogPlacement.CenterOver(owner, 600, 400, Screen));
    }

    [Fact]
    public void On_a_second_monitor_the_clamp_uses_that_monitors_work_area()
    {
        var secondScreen = new Int32Rect(1920, 0, 1920, 1040);
        var owner = new Int32Rect(1900, 50, 400, 300); // pencere iki monitörün sınırında

        Assert.Equal((1920, 0), DialogPlacement.CenterOver(owner, 600, 400, secondScreen));
    }

    [Fact]
    public void A_dialog_larger_than_the_work_area_starts_at_its_origin()
    {
        var small = new Int32Rect(0, 0, 800, 600);

        Assert.Equal((0, 0), DialogPlacement.CenterOver(new Int32Rect(0, 0, 800, 600), 1000, 700, small));
    }
}
