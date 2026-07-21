using System.IO;
using System.Windows;
using System.Windows.Markup;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T35] Görünüm modu / split yüzdesi saf çekirdeği (<see cref="LayoutState"/>) — STA gerekmez, WPF'e dokunmaz.
/// Kaynak: design-v1 BuildApp.jsx:1143 (varsayılan), :1410-1417 (preset'ler), :1394/:1399/:1404 (clamp'ler).
/// </summary>
public class LayoutStateTests
{
    [Fact]
    public void Quad_preset_resets_all_three_splits_to_fifty()
        => Assert.Equal(new LayoutState(LayoutMode.Quad, 50, 50, 50),
                        new LayoutState(LayoutMode.Focus, 60, 74, 76).WithMode(LayoutMode.Quad));

    [Fact]
    public void List_preset_only_sets_right_to_fifty_and_keeps_col_and_left()
    {
        var s = new LayoutState(LayoutMode.Quad, 60, 74, 76).WithMode(LayoutMode.List);
        Assert.Equal(LayoutMode.List, s.Mode);
        Assert.Equal(60, s.ColPct); Assert.Equal(74, s.LeftPct); Assert.Equal(50, s.RightPct);
    }

    [Fact]
    public void Focus_preset_only_sets_right_to_seventysix()
        => Assert.Equal(76, new LayoutState(LayoutMode.Quad, 60, 74, 50).WithMode(LayoutMode.Focus).RightPct);

    [Theory]
    [InlineData(5, 28)] [InlineData(95, 72)] [InlineData(50, 50)]
    public void Column_split_is_clamped_between_twentyeight_and_seventytwo(double input, double expected)
        => Assert.Equal(expected, LayoutState.Default.WithCol(input).ColPct);

    [Theory]
    [InlineData(5, 18)] [InlineData(95, 82)]
    public void Row_splits_are_clamped_between_eighteen_and_eightytwo(double input, double expected)
    {
        Assert.Equal(expected, LayoutState.Default.WithLeft(input).LeftPct);
        Assert.Equal(expected, LayoutState.Default.WithRight(input).RightPct);
    }
}

/// <summary>
/// [T35] Pencere gövdesi <see cref="ShellRoot"/> — 2×2 panel yerleşimi, görünüm modları (graf gizleme) ve
/// <see cref="DsSplitter"/> kavrama bandı. WPF kaynak çekişmesi → serial collection; STA zorunlu.
/// </summary>
[Collection("Console UI (serial)")]
public class ShellLayoutTests
{
    [StaFact]
    public void Shell_has_four_panels_in_quad_mode_and_hides_the_graph_in_list_and_focus()
    {
        var shell = new ShellRoot();                 // MainWindow'un test edilebilir icerik kontrolu
        shell.ApplyLayout(new LayoutState(LayoutMode.Quad, 50, 50, 50));
        Assert.Equal(Visibility.Visible, shell.GraphHost.Visibility);
        shell.ApplyLayout(LayoutState.Default.WithMode(LayoutMode.List));
        Assert.Equal(Visibility.Collapsed, shell.GraphHost.Visibility);
        Assert.Equal(Visibility.Collapsed, shell.LeftSplitter.Visibility);   // graf gizliyken yatay splitter da gider
        shell.ApplyLayout(LayoutState.Default.WithMode(LayoutMode.Focus));
        Assert.Equal(Visibility.Collapsed, shell.GraphHost.Visibility);
    }

    [StaFact]
    public void Splitter_grab_band_is_seven_pixels_with_a_one_pixel_visible_line()
    {
        var col = new DsSplitter { LineOrientation = SplitterLine.Vertical };
        Assert.Equal(DsSplitter.GrabBand, col.Width);          // 7px kavrama alanı (dikey ayraç)
        Assert.Equal(DsSplitter.LineThickness, col.Line.Width); // ortada 1px görünür çizgi

        var row = new DsSplitter { LineOrientation = SplitterLine.Horizontal };
        Assert.Equal(DsSplitter.GrabBand, row.Height);
        Assert.Equal(DsSplitter.LineThickness, row.Line.Height);
    }

    [StaFact]
    public void Applying_a_layout_records_it_and_leaves_the_graph_visible_in_quad()
    {
        var shell = new ShellRoot();
        var state = new LayoutState(LayoutMode.Quad, 61, 33, 76);
        shell.ApplyLayout(state);
        Assert.Equal(state, shell.Layout);
        Assert.Equal(Visibility.Visible, shell.GraphHost.Visibility);
        Assert.Equal(Visibility.Visible, shell.LeftSplitter.Visibility);
    }
}

/// <summary>
/// [T35 fold #1] Title-bar yüksekliğinin (40) TEK kaynağı Size.TitleBarHeight token'ıdır: MainWindow.xaml'de
/// artık ne WindowChrome CaptionHeight literali ne de title-bar satırının Height="40" literali VARDIR — ikisi de
/// kod-tarafı (MainWindow ctor) o token'dan sürülür. Bu, B1'de tespit edilen "unpinned third definition site"ı
/// kapatır (token değeri DesignTokenScaleTests'te 40'a pinlidir).
/// </summary>
public sealed class TitleBarHeightSingleSourceTests
{
    private static string ReadAppSource(string relative)
        => File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, relative));

    [Fact]
    public void MainWindow_xaml_carries_no_forty_pixel_title_bar_literal()
    {
        string xaml = ReadAppSource("MainWindow.xaml");
        Assert.DoesNotContain("CaptionHeight=", xaml);   // WindowChrome yüksekliği XAML'de attribute literali DEĞİL
        Assert.DoesNotContain("Height=\"40\"", xaml);    // title-bar satırı da literal DEĞİL
    }

    [Fact]
    public void MainWindow_derives_the_title_bar_height_from_the_size_token()
    {
        string cs = ReadAppSource("MainWindow.xaml.cs");
        Assert.Contains("Size.TitleBarHeight", cs);   // tek kaynak token
        Assert.Contains("TitleRow.Height", cs);       // satır ondan sürülür
        Assert.Contains("CaptionHeight = titleBarHeight", cs); // WindowChrome ondan sürülür
    }

    [Fact]
    public void The_title_bar_height_token_is_forty()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestAssets", "Resources", "Tokens.xaml");
        using var stream = File.OpenRead(path);
        var tokens = (ResourceDictionary)XamlReader.Load(stream);
        Assert.Equal(40.0, (double)tokens["Size.TitleBarHeight"]);
    }
}
