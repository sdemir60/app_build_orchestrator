using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [D6] Bir Icons.xaml geometrisini kod-tarafı bir <c>Viewbox → Canvas(viewBox) → Path</c> görseline sarar
/// (ConsoleHeader/GraphView'ın XAML'de tekrarladığı deyim; boya + kalınlık <see cref="IconPaint"/>'ten). Alt bar
/// chip'leri, popover satırları ve Build menüsü aynı deyime ihtiyaç duyduğundan tek yerde toplanır (kopya YASAK).
/// Viewbox 24 (ya da verilen) birimlik geometriyi <paramref name="size"/>px'e ölçekler — stroke da birlikte küçülür.
/// </summary>
internal static class IconVisual
{
    public static Viewbox Make(FrameworkElement resourceHost, string iconKey, string brushKey, double size, double viewBox = 24)
    {
        var path = new Path { StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        IconPaint.Apply(path, resourceHost, iconKey, brushKey);
        var canvas = new Canvas { Width = viewBox, Height = viewBox };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = canvas };
    }
}
