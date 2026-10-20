using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// Çocuklarını tek sıraya dizen ve aralarına sabit bir boşluk (<see cref="Gap"/>) koyan panel — tasarım kaynağındaki CSS
/// <c>gap</c>'inin karşılığı; WPF'in <see cref="StackPanel"/>'inde boşluk özelliği yoktur. Boşluk yalnız iki GÖRÜNÜR çocuğun ARASINA
/// girer: kenarlarda boşluk yoktur, Collapsed çocuk ne yer kaplar ne boşluk doğurur (CSS'te çizilmeyen öğe gibi) — title bar kümesinde
/// güncelleme hapı yokken kümenin solunda fazladan boşluk kalmaz.
/// <para><b>Yerleşim yuvarlaması açıkken boşluk TAM cihaz pikselidir</b> (<see cref="DeviceGap"/>). Ölçüldü: 2 dip %125'te 2,5, %175'te 3,5
/// pikseldir; yarım pikseller toplanınca aralıklar 2 ile 3 piksel arasında gidip geliyor ve başa bir öğe girince (title bar'da güncelleme
/// hapı) sonraki öğeler 1 piksel kayıyordu.</para>
/// <para>Kaydırma (<c>IScrollInfo</c>) desteklemez: bir <see cref="ScrollViewer"/>'ın öğe paneli olmak için değil, kısa ve sabit
/// sıralar içindir. Çocuk dizilim ekseninde sınırsız ölçülür, çapraz eksende panelin boyunu alır — <see cref="StackPanel"/> gibi.</para>
/// </summary>
public sealed class GapStackPanel : Panel
{
    public static readonly DependencyProperty OrientationProperty = StackPanel.OrientationProperty.AddOwner(
        typeof(GapStackPanel), new FrameworkPropertyMetadata(Orientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(GapStackPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure),
        value => value is double gap && gap >= 0 && !double.IsPositiveInfinity(gap));

    /// <summary>Dizilim yönü; varsayılan yatay.</summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>İki görünür çocuk arasındaki boşluk (dip).</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        bool horizontal = Orientation == Orientation.Horizontal;
        var slot = horizontal
            ? new Size(double.PositiveInfinity, availableSize.Height)
            : new Size(availableSize.Width, double.PositiveInfinity);
        double along = 0, across = 0;
        int shown = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(slot);
            if (child.Visibility == Visibility.Collapsed) continue;
            var desired = child.DesiredSize;
            along += horizontal ? desired.Width : desired.Height;
            across = Math.Max(across, horizontal ? desired.Height : desired.Width);
            shown++;
        }
        along += DeviceGap(horizontal) * Math.Max(0, shown - 1);
        return horizontal ? new Size(along, across) : new Size(across, along);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        bool horizontal = Orientation == Orientation.Horizontal;
        double gap = DeviceGap(horizontal);
        double offset = 0;
        bool first = true;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            if (!first) offset += gap;
            first = false;
            var desired = child.DesiredSize;
            child.Arrange(horizontal
                ? new Rect(offset, 0, desired.Width, finalSize.Height)
                : new Rect(0, offset, finalSize.Width, desired.Height));
            offset += horizontal ? desired.Width : desired.Height;
        }
        return finalSize;
    }

    /// <summary>Bir aralığın dip cinsinden fiili boyu. Yerleşim yuvarlaması açıksa <see cref="Gap"/> tam cihaz pikseline oturtulur —
    /// WPF'in boy ve kenar boşluğu için uyguladığı kuralla aynı (<c>Math.Round(değer × ölçek) / ölçek</c>): çocukların boyları zaten tam
    /// pikseldir, aralık da öyle olunca her konum ızgarada kalır.</summary>
    private double DeviceGap(bool horizontal)
    {
        if (!UseLayoutRounding) return Gap;
        var dpi = VisualTreeHelper.GetDpi(this);
        double scale = horizontal ? dpi.DpiScaleX : dpi.DpiScaleY;
        return Math.Round(Gap * scale) / scale;
    }
}
