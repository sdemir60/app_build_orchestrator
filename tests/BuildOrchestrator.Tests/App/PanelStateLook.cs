using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı kararı 2026-10-19] Panel durum bloklarının — keşif blokları ve boş görünümler — ortak çizim kimliği TEK
/// yerde doğrulanır (kopya YASAK): ikon 28px, opaklık 0.7, kontur 1.4 <c>text-faint</c>, yuvarlak uç; ikonun altındaki
/// sakin metin 12px <c>text-faint</c>, 10 altta, arayüz yazı tipiyle. Kullananlar: <c>DiscoveryBlockTests</c> (iki keşif
/// bloğu) ve <c>EmptyPanelViewTests</c> (listenin boş görünümü). Yerleşimin bloğa özgü kalanı (listenin keşif ikonunun
/// altındaki +2, zemin, padding) çağıranda doğrulanır.
/// </summary>
internal static class PanelStateLook
{
    /// <summary><paramref name="icon"/> panel durum ikonunun kimliğiyle ve <paramref name="iconKey"/> geometrisiyle mi
    /// çizilmiş.</summary>
    public static void AssertIcon(FrameworkElement host, Viewbox icon, string iconKey)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(icon);
        Assert.Equal((28.0, 28.0), (icon.Width, icon.Height));
        Assert.Equal(0.7, icon.Opacity, 3);
        var path = Assert.Single(DsResources.Descendants(icon).OfType<Path>());
        Assert.Same(host.FindResource(iconKey), path.Data);
        Assert.Equal(1.4, path.StrokeThickness, 3);
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextFaint"), DsResources.ColorOf(path.Stroke));
        Assert.Equal(PenLineCap.Round, path.StrokeStartLineCap);
    }

    /// <summary><paramref name="caption"/> ikonun altındaki sakin metnin kimliğini mi taşıyor.</summary>
    public static void AssertCaption(FrameworkElement host, TextBlock caption)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(caption);
        Assert.Equal(12.0, caption.FontSize);
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextFaint"), DsResources.ColorOf(caption.Foreground));
        Assert.Equal(new Thickness(0, 10, 0, 0), caption.Margin);
        Assert.Equal(AppFonts.Ui, caption.FontFamily);
    }
}
