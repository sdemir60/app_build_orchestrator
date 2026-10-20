using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using BuildOrchestrator.App;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Title bar'ın sağ kümesinin ölçüleri: yedi ikon düğmesinin tek ikon kutusu, bir gruptaki komşu düğmeler arası boşluk ve
/// ayraçların iki yanı. Sıra ve stiller kendi sınıflarında pinlidir (<see cref="UpdatePillTests"/>, <see cref="ExitButtonTests"/>,
/// <c>AboutWiringTests</c>); burada yalnız ölçü vardır.
/// <para><b>[2026-10-20 · kullanıcı: "burayı biraz daha ferah hale getirebilir miyiz … exit ikonu küçük kaldı"]</b> Ölçüm (kullanıcının
/// ekran görüntüsü, %100, 1 px = 1 dip): düğmeler bitişikti — tasarım prototipinin kümedeki <c>gap: 2</c>'si hiç uygulanmamıştı — ve
/// ayraçların iki yanı 5'ti; görünen çizimler arası ikonlar arasında 8, ayraç yanında 9 px'ti, ayraç grupları ayırmıyordu. Exit tasarımın
/// 13 px'ini taşıyordu, komşuları ise uygulamada 16 px'te çizilir: Exit'in görünen boyu 11×12, dişlininki 14×15.</para>
/// <para><b>Öğe arası boşluk 1 px, prototipin 2'si DEĞİL</b> — ölçüldü: 2 px'lik boşluk 8'lik ayraç kenarıyla birlikte en dar pencerede (1240)
/// güncelleme hapı görünürken kümeyi ortadaki kaynak şeridine 1 px yaklaştırıyordu (eskiden 39); 1 px'te pay 11'dir
/// (<c>ResourceStripRealizeTests.At_the_narrowest_width_with_the_update_pill_showing_the_strip_touches_neither_cluster</c>). Ayraçların açılması
/// kullanıcının isteğiydi, boşluk ondan sonra gelir.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class TitleBarClusterTests
{
    /// <summary>Sağ kümenin kendisi — gear'ın ebeveyni (UpdatePillTests ile aynı seçici).</summary>
    private static Panel RightCluster(MainWindow window) => (Panel)LogicalTreeHelper.GetParent(window.GearButton);

    /// <summary>Kümenin yedi ikon düğmesi. Güncelleme hapı bu aileden değildir: 13 px'lik ikonu metinli hapın içindedir.</summary>
    private static ButtonBase[] IconButtons(MainWindow window) =>
        [window.LayQuadButton, window.LayListButton, window.LayFocusButton, window.GearButton, window.NotesButton, window.InfoButton,
         window.ExitButton];

    /// <summary>Bir öğenin kümedeki yatay kapsamı (kenar boşluğu hariç, kendi kutusu).</summary>
    private static (double Left, double Right) Span(FrameworkElement element, Panel cluster)
    {
        double left = element.TranslatePoint(new Point(0, 0), cluster).X;
        return (left, left + element.ActualWidth);
    }

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 1e-6;

    /// <summary>Yedi ikon düğmesi ikonunu AYNI 16 px'lik kutuda çizer. Eskiden Exit tasarımın 13'ünde kalmıştı (sınıfın başındaki ölçüm).</summary>
    [StaFact]
    public void Every_icon_button_in_the_right_cluster_draws_its_icon_in_the_same_16_px_box()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.NewRealized(temp);

        var boxes = IconButtons(window)
            .Select(button => (button.Name, Box: DsResources.Descendants(button).OfType<Viewbox>().Single()))
            .ToList();
        Assert.All(boxes, b => Assert.True((b.Box.Width, b.Box.Height) == (16.0, 16.0),
            $"{b.Name}: ikon kutusu {b.Box.Width}×{b.Box.Height}, beklenen 16×16"));
        GC.KeepAlive(window);
    }

    /// <summary>Bir gruptaki iki komşu düğme arasında 1 px vardır (sınıfın başındaki gerekçe; prototipte 2). Eskiden 0'dı (bitişik).
    /// %100 ölçekte ölçülür: orada her konum tam pikseldir.</summary>
    [StaFact]
    public void Neighbouring_buttons_in_the_right_cluster_stand_one_pixel_apart()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.NewRealized(temp, dpi: new DpiScale(1, 1));
        var cluster = RightCluster(window);

        (FrameworkElement Left, FrameworkElement Right)[] neighbours =
        [
            (window.LayQuadButton, window.LayListButton), (window.LayListButton, window.LayFocusButton),
            (window.GearButton, window.NotesButton), (window.NotesButton, window.InfoButton),
        ];
        Assert.All(neighbours, n =>
        {
            double gap = Span(n.Right, cluster).Left - Span(n.Left, cluster).Right;
            Assert.True(Near(gap, 1), $"{n.Left.Name} → {n.Right.Name}: {gap} px, beklenen 1");
        });
        GC.KeepAlive(window);
    }

    /// <summary>Her ayraç iki yanındaki düğmeden 9 px uzakta durur: ayracın kendi 8'lik kenar boşluğu + kümenin 1'lik boşluğu. Eskiden 5'ti —
    /// görünen çizimler arası ayraç yanında 9, ikonlar arasında 8 px: gruplar ayrılmıyordu; şimdi 13'e karşı 9. Güncelleme hapının ayracı da
    /// aynı kuralı taşır (hap → ayraç → quad), hap görünürken ölçülür; %100 ölçekte.</summary>
    [StaFact]
    public void Every_separator_stands_nine_pixels_from_the_buttons_on_either_side()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.NewRealizedWithOffer(temp, new DpiScale(1, 1));
        Assert.Equal(Visibility.Visible, window.UpdatePillSlot.Visibility); // ön-koşul: hap görünür
        var cluster = RightCluster(window);
        var layoutSeparator = Assert.IsType<Rectangle>(cluster.Children[cluster.Children.IndexOf(window.LayFocusButton) + 1]);

        (FrameworkElement Before, Rectangle Separator, FrameworkElement After)[] separators =
        [
            (window.UpdatePill, window.UpdatePillSeparator, window.LayQuadButton),
            (window.LayFocusButton, layoutSeparator, window.GearButton),
            (window.InfoButton, window.ExitSeparator, window.ExitButton),
        ];
        Assert.All(separators, s =>
        {
            var line = Span(s.Separator, cluster);
            double left = line.Left - Span(s.Before, cluster).Right, right = Span(s.After, cluster).Left - line.Right;
            Assert.True(Near(left, 9) && Near(right, 9),
                $"{s.Before.Name} | {s.After.Name}: ayracın solu {left}, sağı {right} px — beklenen 9 / 9");
        });
        GC.KeepAlive(window);
    }
}
