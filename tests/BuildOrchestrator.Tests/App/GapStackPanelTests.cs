using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// <see cref="GapStackPanel"/> — tasarım kaynağının CSS <c>gap</c>'inin karşılığı: boşluk yalnız iki GÖRÜNÜR çocuğun ARASINA girer,
/// kenarlarda boşluk yoktur; Collapsed çocuk ne yer kaplar ne boşluk doğurur (title bar'da güncelleme hapı yokken kümenin solunda
/// fazladan boşluk kalmaz).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class GapStackPanelTests
{
    private static Border Box(double width, double height, Visibility visibility = Visibility.Visible) =>
        new() { Width = width, Height = height, Visibility = visibility };

    private static void Lay(Panel panel)
    {
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        panel.Arrange(new Rect(panel.DesiredSize));
    }

    [StaFact]
    public void The_gap_goes_only_between_shown_children()
    {
        var first = Box(10, 5);
        var hidden = Box(7, 5, Visibility.Collapsed);
        var last = Box(20, 8);
        var panel = new GapStackPanel { Gap = 2, Children = { first, hidden, last } };
        Lay(panel);

        Assert.Equal(new Size(32, 8), panel.DesiredSize); // 10 + 2 + 20: gizli çocuk yer kaplamaz, boşluk doğurmaz; kenarda boşluk yok
        Assert.Equal(0, first.TranslatePoint(new Point(), panel).X);
        Assert.Equal(12, last.TranslatePoint(new Point(), panel).X);
    }

    [StaFact]
    public void A_vertical_panel_puts_the_gap_between_rows()
    {
        var top = Box(10, 5);
        var bottom = Box(6, 9);
        var panel = new GapStackPanel { Orientation = Orientation.Vertical, Gap = 3, Children = { top, bottom } };
        Lay(panel);

        Assert.Equal(new Size(10, 17), panel.DesiredSize); // 5 + 3 + 9
        Assert.Equal(0, top.TranslatePoint(new Point(), panel).Y);
        Assert.Equal(8, bottom.TranslatePoint(new Point(), panel).Y);
    }

    /// <summary>Yerleşim yuvarlaması açıkken her aralık AYNI ve TAM sayıda cihaz pikselidir. 2 dip %125'te 2,5, %175'te 3,5 pikseldir;
    /// yarım pikseller toplanınca aralıklar 2 ile 3 piksel arasında gidip gelir ve başa bir öğe girince (güncelleme hapı) sonrakiler 1 px
    /// kayar — ölçüldü: <c>UpdatePillTests.The_existing_title_bar_buttons_do_not_move_when_the_pill_appears</c> bu iki ölçekte kırmızıydı
    /// (dört düğme 1'er piksel).</summary>
    [StaTheory]
    [InlineData(1.25)]
    [InlineData(1.75)]
    public void Under_layout_rounding_every_gap_is_the_same_whole_number_of_device_pixels(double scale)
    {
        var boxes = Enumerable.Range(0, 4).Select(_ => Box(22, 22)).ToArray();
        var panel = new GapStackPanel { Gap = 2, UseLayoutRounding = true };
        foreach (var box in boxes) panel.Children.Add(box);
        VisualTreeHelper.SetRootDpi(panel, new DpiScale(scale, scale)); // çocuklar ağaçtayken: ölçek yalnız o anki görsellere iner
        Lay(panel);

        var gaps = boxes.Zip(boxes.Skip(1), (left, right) =>
            (right.TranslatePoint(new Point(), panel).X - (left.TranslatePoint(new Point(), panel).X + left.ActualWidth)) * scale).ToList();
        Assert.All(gaps, gap => Assert.True(Math.Abs(gap - Math.Round(gap)) < 1e-6, $"aralık {gap} cihaz pikseli — tam değil"));
        Assert.True(gaps.Select(gap => Math.Round(gap)).Distinct().Count() == 1, $"aralıklar eşit değil: {string.Join(", ", gaps)} cihaz pikseli");
    }
}
