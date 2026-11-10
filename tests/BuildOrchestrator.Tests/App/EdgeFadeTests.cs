using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Kayan bir alanın kenar solması (<see cref="EdgeFade"/>): <c>EdgeFade.Insets</c> açık bir <see cref="ScrollViewer"/>,
/// içeriğini üst ve alt kenar boşluklarında altındaki zemine karıştırarak soldurur. Burada şeridin KENDİ davranışı
/// sentetik bir alanda ölçülür; üç dialogun onu gerçekten kullandığı kendi testlerindedir (NotesDialogTests,
/// AboutDialogTests, SettingsDialogTests) ve her kayan gövdenin açık olduğu kabuk kuralıdır (DialogShellTests).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class EdgeFadeTests
{
    private const int TopInset = 22;
    private const int BottomInset = 24;

    /// <summary>Sentetik kayan alan: zemin kutusu şerit rengini KALITIMLA verir (kabuğun dialoglara yaptığı), içinde
    /// <c>Insets</c> açık bir kaydırıcı ve 22/24 iç boşluklu bir liste; listenin başında alanı taşıran beyaz sonda.</summary>
    private static (ScrollViewer Viewer, Border Probe, Color Surface, Window Window) Realize()
    {
        var host = DsResources.NewHost();
        var surface = (SolidColorBrush)host.FindResource("Brush.SurfaceRaised");
        var viewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 300 };
        EdgeFade.SetInsets(viewer, true);
        viewer.Content = new StackPanel { Margin = new Thickness(18, TopInset, 18, BottomInset) };
        var box = new Border { Background = surface, Child = viewer };
        EdgeFade.SetFill(box, surface);
        var window = DsResources.Realize(host, box, width: 500, height: 400);
        return (viewer, EdgeFadeProbe.InsertProbe(viewer), surface.Color, window);
    }

    [StaFact]
    public void Content_scrolled_into_the_top_inset_fades_out_toward_the_top_edge()
    {
        var (viewer, probe, surface, window) = Realize();
        EdgeFadeProbe.AssertTopBandFades(viewer, probe, surface, TopInset);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Content_running_into_the_bottom_inset_fades_out_toward_the_bottom_edge()
    {
        var (viewer, probe, surface, window) = Realize();
        EdgeFadeProbe.AssertBottomBandFades(viewer, probe, surface, BottomInset);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Solma yalnız kayan İÇERİĞE uygulanır: scrollbar'ın hapı şeritlerden geçerken solmaz — alan baştayken hap rayın
    /// en üstünde üst şeridin, sondayken en altında alt şeridin içinden geçer. Şerit kaydırıcının tam genişliğini
    /// örtseydi hap kenara doğru zemine karışırdı.
    ///
    /// <para><b>[TAŞINDI]</b> NotesDialogTests'ten (<c>The_scrollbar_thumb_is_not_faded_by_the_top_band</c>): şerit
    /// artık üç dialogun ortak parçasıdır; kural aynı, iki kenara genişledi.</para>
    /// </summary>
    [StaFact]
    public void The_scrollbar_thumb_is_not_faded_at_either_edge()
    {
        var (viewer, _, surface, window) = Realize();
        var bar = DsResources.Descendants(viewer).OfType<ScrollBar>().Single(b => b.Orientation == Orientation.Vertical);
        var thumb = DsResources.Descendants(bar).OfType<Thumb>().Single();

        EdgeFadeProbe.Scroll(viewer, 0);
        var top = DsResources.BoundsIn(thumb, viewer);
        Assert.True(top.Top < 1 && top.Height > 60, $"hap üst şeridi ölçecek yerde değil: {top}");
        AssertThumbRowsUnfaded(EdgeFadeProbe.Render(viewer, surface), top, from: (int)top.Top + 6, to: TopInset);

        EdgeFadeProbe.Scroll(viewer, viewer.ScrollableHeight);
        var bottom = DsResources.BoundsIn(thumb, viewer);
        int h = (int)Math.Floor(viewer.ViewportHeight);
        Assert.True(bottom.Bottom > h - 1 && bottom.Height > 60, $"hap alt şeridi ölçecek yerde değil: {bottom}");
        AssertThumbRowsUnfaded(EdgeFadeProbe.Render(viewer, surface), bottom, from: h - BottomInset, to: (int)bottom.Bottom - 6);
        GC.KeepAlive(window);
    }

    private static void AssertThumbRowsUnfaded(EdgeFadeProbe.Seen seen, Rect thumb, int from, int to)
    {
        int x = (int)(thumb.Left + thumb.Width / 2);
        int inside = seen.GreenAt(x, (int)(thumb.Top + thumb.Height / 2)); // şeritlerin çok uzağı
        Assert.True(inside != seen.Surface.G, "hap çizilmemiş");
        for (int y = from; y < to; y++)
            Assert.True(seen.GreenAt(x, y) == inside, $"hap satır {y}'de solmuş: {seen.GreenAt(x, y)}, hapın içi {inside}");
    }

    /// <summary>
    /// Şeritler işaretçiyi almaz: altlarından geçen içeriğe yapılan tıklama ve tekerlek o içeriğe ulaşır. Şerit isabet
    /// alsaydı olay içeriğin değil şeridin atalarından yükselirdi — alan kenar boşluklarında tekerlekle kaymaz, oraya
    /// kaymış bir düğme tıklanamazdı. Ölçü fareyi yönlendiren isabet testidir (<c>Mouse.DirectlyOver</c>'ın kaynağı):
    /// <c>IsHitTestVisible</c>'ı yalnız o uygular; <c>VisualTreeHelper.HitTest</c> bayrağa bakmaz (ölçüldü).
    ///
    /// <para><b>[TAŞINDI]</b> NotesDialogTests'ten (<c>The_top_band_lets_the_pointer_through_to_the_list</c>), iki kenara
    /// genişledi.</para>
    /// </summary>
    [StaFact]
    public void Both_bands_let_the_pointer_through_to_the_content()
    {
        var (viewer, probe, _, window) = Realize();
        int x = EdgeFadeProbe.ProbeColumn(viewer, probe);

        EdgeFadeProbe.Scroll(viewer, 0); // sonda alt şeridin altından geçer
        AssertHitsProbe(viewer, probe, new Point(x, Math.Floor(viewer.ViewportHeight) - BottomInset / 2));

        EdgeFadeProbe.Scroll(viewer, TopInset); // sonda üst şeridin altına kaydı
        AssertHitsProbe(viewer, probe, new Point(x, TopInset / 2));
        GC.KeepAlive(window);
    }

    private static void AssertHitsProbe(ScrollViewer viewer, Border probe, Point inBand)
    {
        var hit = viewer.InputHitTest(inBand) as DependencyObject;
        Assert.NotNull(hit);
        Assert.True(DsResources.IsSelfOrDescendantOf(hit!, probe), $"şerit isabeti aldı: {hit!.GetType().Name}");
    }
}
