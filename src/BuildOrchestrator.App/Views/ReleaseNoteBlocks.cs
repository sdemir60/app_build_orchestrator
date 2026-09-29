using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// Bir kategori bloğu yüzeyinin ölçüleri — blok dilinin kendisi (kare, caps etiket, girinti) ortaktır, ölçüler
/// yüzeye göre değişir: What's new okuma ölçüsündedir, güncelleme kartı kompakttır.
/// </summary>
/// <param name="BlockGap">Kategori blokları arası boşluk.</param>
/// <param name="HeadGap">Başlık satırı ile ilk madde arası.</param>
/// <param name="ItemGap">Aynı kategorideki maddeler arası.</param>
/// <param name="FontSizeKey">Madde metninin punto token'ı.</param>
/// <param name="LineHeightKey">Madde metninin satır yüksekliği token'ı.</param>
/// <param name="MaxWidth">Madde metninin ölçü sınırı; sınır yoksa <see cref="double.PositiveInfinity"/>.</param>
internal readonly record struct ReleaseNoteBlockMetrics(
    double BlockGap, double HeadGap, double ItemGap, string FontSizeKey, string LineHeightKey, double MaxWidth);

/// <summary>
/// [design v1.19.0 §2.11 · v1.23.0 §2.12] Sürüm notu maddelerinin kategori bloklarını çizen TEK yer: What's new
/// diyaloğu (<see cref="NotesDialog"/>) ve güncelleme kartı (<c>UpdateCard</c>) aynı blok dilini konuşur —
/// 6px renkli kare (köşe 1) + 7px + caps <c>text-dim</c> etiket, altında 13px içeriden işaretsiz maddeler
/// (<c>text-secondary</c>). Kategoriler <see cref="ReleaseNotes.KindOrder"/> sırasıyla çizilir, boş kategori hiç
/// çizilmez; renk ve etiket <see cref="ReleaseNotes"/>'ten gelir (ikinci bir kategori tablosu yoktur). Yüzeye özgü
/// ölçüler <see cref="ReleaseNoteBlockMetrics"/> ile verilir.
/// </summary>
internal static class ReleaseNoteBlocks
{
    /// <summary>Kategori karesinin kenarı (CSS <c>width/height: 6</c>).</summary>
    public const double SwatchSize = 6;

    /// <summary>Kategori karesinin köşe yarıçapı (CSS <c>borderRadius: 1</c>).</summary>
    public const double SwatchRadius = 1;

    /// <summary>Kare ile caps etiket arası (CSS <c>gap: 7</c>).</summary>
    public const double LabelGap = 7;

    /// <summary>Maddelerin başlığa göre girintisi (CSS <c>paddingLeft: 13</c>).</summary>
    public const double Indent = 13;

    /// <summary><paramref name="host"/>'u kategori bloklarıyla doldurur (önce temizler).</summary>
    public static void Fill(Panel host, IEnumerable<ReleaseNote> notes, ReleaseNoteBlockMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(notes);
        host.Children.Clear();
        var all = notes as IReadOnlyCollection<ReleaseNote> ?? [.. notes];
        foreach (var kind in ReleaseNotes.KindOrder)
        {
            var items = all.Where(n => n.Kind == kind).ToList();
            if (items.Count == 0) continue;
            host.Children.Add(Category(kind, items, first: host.Children.Count == 0, metrics));
        }
    }

    private static FrameworkElement Category(
        NoteKind kind, IReadOnlyList<ReleaseNote> items, bool first, ReleaseNoteBlockMetrics metrics)
    {
        var group = new StackPanel { Margin = new Thickness(0, first ? 0 : metrics.BlockGap, 0, 0) };

        var heading = new StackPanel { Orientation = Orientation.Horizontal };
        var swatch = new System.Windows.Shapes.Rectangle
        {
            Width = SwatchSize,
            Height = SwatchSize,
            RadiusX = SwatchRadius,
            RadiusY = SwatchRadius,
            VerticalAlignment = VerticalAlignment.Center,
        };
        swatch.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, ReleaseNotes.SwatchBrushKey(kind));
        heading.Children.Add(swatch);

        var label = new TrackedTextBlock
        {
            Text = ReleaseNotes.Label(kind),
            Margin = new Thickness(LabelGap, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Hedef DP'ler AÇIKÇA nitelenir: TrackedTextBlock kendi Foreground/FontSize DP'lerini kaydeder,
        // TextBlock/Control ailesindekiler burada ETKİSİZDİR.
        label.SetResourceReference(TrackedTextBlock.FontSizeProperty, "FontSize.2xs");
        label.SetResourceReference(TrackedTextBlock.ForegroundProperty, "Brush.TextDim");
        heading.Children.Add(label);
        group.Children.Add(heading);

        var list = new StackPanel { Margin = new Thickness(Indent, metrics.HeadGap, 0, 0) };
        foreach (var note in items)
        {
            var text = new TextBlock
            {
                Text = note.Text,
                Margin = new Thickness(0, list.Children.Count == 0 ? 0 : metrics.ItemGap, 0, 0),
                MaxWidth = metrics.MaxWidth,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextWrapping = TextWrapping.Wrap,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };
            text.SetResourceReference(TextBlock.FontSizeProperty, metrics.FontSizeKey);
            text.SetResourceReference(TextBlock.LineHeightProperty, metrics.LineHeightKey);
            text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
            list.Children.Add(text);
        }
        group.Children.Add(list);
        return group;
    }
}
