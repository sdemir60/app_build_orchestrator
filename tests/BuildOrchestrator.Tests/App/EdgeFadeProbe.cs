using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Kayan bir alanın kenar solmasını (<c>EdgeFade</c>) ÖLÇEN ortak düzenek — şeridin kendi testleri ve onu kullanan üç
/// dialogun (What's new, About, Settings) testleri bunu paylaşır (kopya YASAK). Alanın içeriğine opak beyaz bir SONDA
/// konur, alan altındaki zeminin üstünde çizilir; bir satırın görünürlüğü sondanın orada ne kadar beyaz göründüğüdür
/// (0 = zemin, 1 = tam beyaz).
///
/// <para>Kural (iki kenarda aynı): içeriğin kenar boşluğu bir solma şerididir. İçerik şeride girdiği anda — durağan
/// hâldeki çizgisinden itibaren — solmaya başlar, kenara (başlığın ya da footer'ın çizgisine) vardığında görünmez olur;
/// aradaki geçiş yumuşaktır. Şeridin dışı dokunulmaz; şerit boşken zeminden ayırt edilemez.</para>
/// </summary>
internal static class EdgeFadeProbe
{
    /// <summary>İçeriğe, alanı taşıracak boyda (viewport + 100) opak beyaz bir sonda koyar: yığın panelde başa; üst üste
    /// binen panelde (Settings sayfalarının <see cref="Grid"/>'i) en üste ve üste yaslı — örnek sütunda altındaki her
    /// şeyi örter.</summary>
    public static Border InsertProbe(ScrollViewer viewer)
    {
        var content = (Panel)viewer.Content;
        var probe = new Border { Height = viewer.ViewportHeight + 100, Background = Brushes.White, VerticalAlignment = VerticalAlignment.Top };
        if (content is StackPanel) content.Children.Insert(0, probe); else content.Children.Add(probe);
        viewer.UpdateLayout();
        return probe;
    }

    /// <summary>Üst şerit: durağanken sonda içeriğin üst çizgisindedir, üstündeki boş şerit zeminden ayırt edilemez.
    /// İçerik <paramref name="inset"/> kadar kayınca sondanın ilk satırları şeridi doldurur: kenarda (satır 0) neredeyse
    /// görünmez, çizgide (satır inset−1) neredeyse tam, ortada yarı yarıya ve aşağı doğru hiç azalmadan; çizginin altı
    /// dokunulmaz.</summary>
    public static void AssertTopBandFades(ScrollViewer viewer, Border probe, Color surface, int inset)
    {
        int x = ProbeColumn(viewer, probe);
        Scroll(viewer, 0);
        Assert.Equal(inset, TopIn(probe, viewer), precision: 3); // içeriğin durağan üst çizgisi
        var rest = Render(viewer, surface);
        for (int y = 0; y < inset; y++) // ±1: 8 bitlik karışımın yuvarlaması — gözle ayırt edilemez
            Assert.True(Math.Abs(rest.GreenAt(x, y) - surface.G) <= 1, $"durağanken boş üst şerit zeminden ayrışıyor: satır {y} = {rest.GreenAt(x, y)}, zemin {surface.G}");
        for (int y = inset; y < 2 * inset; y++)
            Assert.True(rest.GreenAt(x, y) == 255, $"durağanken satır {y} solmuş: {rest.GreenAt(x, y)}");

        Scroll(viewer, inset);
        Assert.Equal(0.0, TopIn(probe, viewer), precision: 3); // sondanın ilk satırları şeritte
        var scrolled = Render(viewer, surface);
        for (int y = inset; y < 2 * inset; y++)
            Assert.True(scrolled.GreenAt(x, y) == 255, $"üst çizginin altında satır {y} solmuş: {scrolled.GreenAt(x, y)}");
        Assert.True(scrolled.VisibilityAt(x, 0) <= 0.1, $"üst kenarda içerik hâlâ görünüyor: {scrolled.VisibilityAt(x, 0):0.000}");
        Assert.True(scrolled.VisibilityAt(x, inset - 1) >= 0.9, $"üst çizgide solma fazla: {scrolled.VisibilityAt(x, inset - 1):0.000}");
        Assert.InRange(scrolled.VisibilityAt(x, inset / 2), 0.3, 0.7); // yumuşak geçiş, sert kesik değil
        for (int y = 1; y < inset; y++)
            Assert.True(scrolled.GreenAt(x, y) >= scrolled.GreenAt(x, y - 1),
                $"üst solma aşağı doğru tekdüze artmıyor: satır {y - 1} = {scrolled.GreenAt(x, y - 1)} → satır {y} = {scrolled.GreenAt(x, y)}");
    }

    /// <summary>Alt şerit: durağanken sonda alt kenarı geçer — şeridin üst çizgisinde neredeyse tam, alt kenarda neredeyse
    /// görünmez, ortada yarı yarıya ve aşağı doğru hiç artmadan; şeridin üstü dokunulmaz. Sona kaydırınca şeritte
    /// içeriğin alt boşluğu durur: şerit boştur ve zeminden ayırt edilemez (son satır solmaz).</summary>
    public static void AssertBottomBandFades(ScrollViewer viewer, Border probe, Color surface, int inset)
    {
        int x = ProbeColumn(viewer, probe);
        int h = (int)Math.Floor(viewer.ViewportHeight);
        int line = h - inset; // şeridin üst çizgisi
        Scroll(viewer, 0);
        Assert.True(TopIn(probe, viewer) + probe.ActualHeight > h, "sonda alt kenarı geçmiyor — senaryo kurulmadı");
        var rest = Render(viewer, surface);
        for (int y = line - inset; y < line; y++)
            Assert.True(rest.GreenAt(x, y) == 255, $"alt şeridin üstünde satır {y} solmuş: {rest.GreenAt(x, y)}");
        Assert.True(rest.VisibilityAt(x, h - 1) <= 0.1, $"alt kenarda içerik hâlâ görünüyor: {rest.VisibilityAt(x, h - 1):0.000}");
        Assert.True(rest.VisibilityAt(x, line) >= 0.9, $"alt şeridin üst çizgisinde solma fazla: {rest.VisibilityAt(x, line):0.000}");
        Assert.InRange(rest.VisibilityAt(x, line + inset / 2), 0.3, 0.7); // yumuşak geçiş, sert kesik değil
        for (int y = line + 1; y < h; y++)
            Assert.True(rest.GreenAt(x, y) <= rest.GreenAt(x, y - 1),
                $"alt solma aşağı doğru tekdüze azalmıyor: satır {y - 1} = {rest.GreenAt(x, y - 1)} → satır {y} = {rest.GreenAt(x, y)}");

        Scroll(viewer, viewer.ScrollableHeight);
        var end = Render(viewer, surface);
        for (int y = line; y < h; y++)
            Assert.True(Math.Abs(end.GreenAt(x, y) - surface.G) <= 1, $"sondayken boş alt şerit zeminden ayrışıyor: satır {y} = {end.GreenAt(x, y)}, zemin {surface.G}");
    }

    /// <summary>Alanın görülen hâli: önce zemin, üstüne alan — ekrandaki gibi tek geçişte, 96 DPI'da 1:1; satır ve
    /// sütunlar alanın kendi koordinatıdır. <see cref="VisualBrush"/> ataların opaklığını ve dönüşümünü (dialogun giriş
    /// animasyonu) ölçüye katmaz, ama kökün ebeveynine göre KENDİ ofsetini uygular (ölçüldü: kutusunda ortalanan
    /// sentetik bir alan 43px aşağıda çiziliyordu) — görüntü penceresi bu yüzden o ofset kadar kaydırılır.</summary>
    public static Seen Render(FrameworkElement area, Color surface)
    {
        var bounds = new Rect(0, 0, area.ActualWidth, area.ActualHeight);
        var offset = VisualTreeHelper.GetOffset(area);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(surface), null, bounds);
            dc.DrawRectangle(new VisualBrush(area)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(offset.X, offset.Y, bounds.Width, bounds.Height),
            }, null, bounds);
        }
        int width = (int)Math.Ceiling(bounds.Width), height = (int)Math.Ceiling(bounds.Height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return new Seen(pixels, width, surface);
    }

    /// <param name="Surface">Alanın altındaki zemin — alan bunun üstünde görülür.</param>
    public sealed record Seen(byte[] Pixels, int Width, Color Surface)
    {
        /// <summary>Bir pikselin görülen yeşil kanalı: alanın boş kaldığı yerde zemin, opak beyaz sondada 255.</summary>
        public int GreenAt(int x, int y) => Pixels[(y * Width + x) * 4 + 1];

        /// <summary>Opak beyaz bir sondanın o pikseldeki görünürlüğü: 0 = zemin, 1 = tam beyaz.</summary>
        public double VisibilityAt(int x, int y) => (GreenAt(x, y) - Surface.G) / (255.0 - Surface.G);
    }

    public static int ProbeColumn(ScrollViewer viewer, Border probe) =>
        (int)(DsResources.BoundsIn(probe, viewer).Left + probe.ActualWidth / 2);

    public static double TopIn(FrameworkElement element, FrameworkElement root) =>
        element.TranslatePoint(new Point(0, 0), root).Y;

    public static void Scroll(ScrollViewer viewer, double offset)
    {
        viewer.ScrollToVerticalOffset(offset);
        viewer.UpdateLayout();
    }
}
