using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [SCROLLBAR] Resources/Controls.xaml SCROLLBAR bölümü — design-v1 `.bo-scroll` sözleşmesini
/// (BuildApp.jsx:35-38) pinler: 10px ray, şeffaf track, ok butonu YOK, thumb = Brush.Neutral700 +
/// 3px içerlek + CornerRadius 2 hap. Implicit stil olduğu için ŞABLON SINIRLARINI geçmesi ayrıca
/// kanıtlanır (ScrollViewer ve AvalonEdit üzerinden) — bir Style'ın varlığını okumak, gerçek
/// ScrollViewer'ların onu giydiğini kanıtlamaz. Kontroller GERÇEKTEN kurulur (DsControlTemplateTests deseni).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ScrollBarStyleTests
{
    private static ScrollBar NewVerticalBar() => new()
    {
        Orientation = Orientation.Vertical,
        Height = 150,
        Minimum = 0,
        Maximum = 100,
        ViewportSize = 20,
        Value = 10,
    };

    [StaFact]
    public void Vertical_rail_is_10px_and_the_thumb_is_a_neutral700_pill_inset_by_3px()
    {
        var host = DsResources.NewHost();
        var bar = NewVerticalBar();
        var window = DsResources.Realize(host, bar);

        // BuildApp.jsx:36 — ::-webkit-scrollbar { width: 10px }.
        Assert.Equal(10.0, bar.ActualWidth);

        // BuildApp.jsx:38 — thumb: neutral-700 zemin, 3px şeffaf kenar (Margin), dış 5 − kenar 3 = 2 radius.
        var thumb = DsResources.Descendants(bar).OfType<Thumb>().Single();
        var pill = DsResources.Descendants(thumb).OfType<Border>().Single();
        Assert.Equal(new Thickness(3), pill.Margin);
        Assert.Equal(new CornerRadius(2), pill.CornerRadius);
        Assert.Equal(DsResources.TokenColor(host, "Brush.Neutral700"), DsResources.ColorOf(pill.Background));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Horizontal_rail_is_10px()
    {
        var host = DsResources.NewHost();
        var bar = new ScrollBar
        {
            Orientation = Orientation.Horizontal,
            Width = 200,
            Minimum = 0,
            Maximum = 100,
            ViewportSize = 20,
        };
        var window = DsResources.Realize(host, bar);

        Assert.Equal(10.0, bar.ActualHeight); // BuildApp.jsx:36 — height: 10px
        Assert.Single(DsResources.Descendants(bar).OfType<Thumb>());
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_rail_has_no_arrow_buttons_only_two_transparent_page_areas()
    {
        var host = DsResources.NewHost();
        var bar = NewVerticalBar();
        var window = DsResources.Realize(host, bar);

        // webkit scrollbar'ında buton çizilmez (BuildApp.jsx:36-38 yalnız track+thumb tanımlar): ok glyph'i
        // (Path) HİÇ yok; ray'ın boş alanı = 2 şeffaf sayfa-atlama RepeatButton'ı (davranış korunur).
        // (Şekil tipleri NİTELENDİRİLİR: `using System.Windows.Shapes` System.IO.Path'i belirsizleştirirdi.)
        Assert.Empty(DsResources.Descendants(bar).OfType<System.Windows.Shapes.Path>());
        var pageAreas = DsResources.Descendants(bar).OfType<RepeatButton>().ToList();
        Assert.Equal(2, pageAreas.Count);
        Assert.All(pageAreas, b => Assert.Equal(Colors.Transparent, DsResources.ColorOf(
            DsResources.Descendants(b).OfType<Border>().Single().Background)));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_scrollviewer_gets_the_ds_bar_through_its_default_template()
    {
        // Implicit stilin ŞABLON SINIRINI geçtiğinin kanıtı: ScrollBar'ı biz değil, ScrollViewer'ın
        // default şablonu kurar (üretimdeki StickyLayerList/EventStream yolu budur).
        var host = DsResources.NewHost();
        var viewer = new ScrollViewer
        {
            Width = 200,
            Height = 120,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Height = 1000 },
        };
        var window = DsResources.Realize(host, viewer);

        var bar = DsResources.Descendants(viewer).OfType<ScrollBar>()
            .Single(b => b.Orientation == Orientation.Vertical);
        Assert.Equal(Visibility.Visible, bar.Visibility);
        Assert.Equal(10.0, bar.ActualWidth);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_disabled_bar_collapses_its_track()
    {
        // Kaydıracak şey yokken (IsEnabled=false) boş koyu hap kalıntısı görünmez — restraint.
        var host = DsResources.NewHost();
        var bar = NewVerticalBar();
        var window = DsResources.Realize(host, bar);

        var track = DsResources.Descendants(bar).OfType<Track>().Single();
        Assert.Equal(Visibility.Visible, track.Visibility);

        bar.IsEnabled = false;
        bar.UpdateLayout();
        Assert.Equal(Visibility.Collapsed, track.Visibility);
        GC.KeepAlive(window);
    }
}
