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

    /// <summary>
    /// [T2 fix-1 · I-B/m4] Bir CHIP'in içindeki ikon: konturu chip'in <b>animasyonlu</b>
    /// <see cref="Control.Foreground"/>'unu İZLER — yani chip aktifken (<c>IsChecked</c>) amber'a, pasifken
    /// text-secondary'ye birlikte geçer. Sabit bir token'a bağlanan ikon, chip'in aktif durumunu YALANLARDI.
    ///
    /// <para><b>Neden <see cref="Make"/>'ten ayrı:</b> orada boya SABİT bir token anahtarıdır; burada boya bir
    /// BAĞLAMADIR. Desenin kaynağı <c>ActionBar.BoundChipIcon</c>'du; ikinci çağıran (<c>ShellRoot</c>'un
    /// filtre chip'i) çıkınca buraya taşındı (kopya YASAK).</para>
    ///
    /// <para><b>Kalınlık ERTELENMİŞ bağlanır</b> (<c>SetResourceReference</c>), <c>IconPaint.Apply</c>'ın
    /// anında-çözme yolundan farklı olarak: bu görseller çoğu zaman bir kontrolün ctor'unda, öğe daha hiçbir
    /// ağaçta değilken kurulur. Orada <c>IconPaint</c> kalınlığı çözemeyip ERKEN DÖNER ve konturu hiç
    /// bağlamaz — üretimde <c>Application.Resources</c> sayesinde görünmeyen, headless realize'da ikonu
    /// tamamen kaybettiren bir sapma (T49 sınıfı). Ertelenmiş bağ iki ortamda da çözülür.</para>
    ///
    /// <para><b>Kapsam:</b> KONTURLU ikonlar (chip ikonlarının tümü: branch/tree/chevron/chip-remove). Dolu
    /// ikonlar (<c>StrokeThickness</c> 0) için <see cref="Make"/> kullanılmalıdır.</para>
    /// </summary>
    public static Viewbox BoundToForeground(Control chip, string iconKey, double size, double viewBox = 24)
    {
        ArgumentNullException.ThrowIfNull(chip);
        var path = new Path
        {
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        path.SetResourceReference(Path.DataProperty, iconKey);
        path.SetResourceReference(Shape.StrokeThicknessProperty, iconKey + ".StrokeThickness");
        path.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding(nameof(Control.Foreground)) { Source = chip });
        var canvas = new Canvas { Width = viewBox, Height = viewBox };
        canvas.Children.Add(path);
        return new Viewbox
        {
            Width = size, Height = size, Stretch = Stretch.Uniform, Child = canvas,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }
}
