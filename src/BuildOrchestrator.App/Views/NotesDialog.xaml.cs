using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [design v1.13.0/v1.19.0 §2.11 · D4/T9] What's new — sürüm notlarının kendi diyalogu. About'un dördüncü
/// sekmesiydi (design v1.9.0); v1.13.0 bunu About'tan ÇIKARDI, kendi title bar butonu (title bar'daki notes
/// butonu) ve kendi kısayolu (Ctrl+F1) verdi. Kabuk (scrim, Ds.Dialog, odak tuzağı, Esc/scrim ile kapanma,
/// giriş) üç dialogun ORTAK kabuğudur: <see cref="ModalDialog"/>.
///
/// <para><b>[v1.19.0] Sürüm bloğu 2 kolonlu grid'dir</b> (84px + 26px aralık + kalan): solda sürüm kimliği
/// (numara · tarih · kuruluysa <c>INSTALLED</c> çipi) STICKY durur, sağda kategori blokları okunur ölçüde
/// (satır yüksekliği 1.62, maddeler arası 10px, en çok 500px) sarılır. Son 3 açık kuralı, katlama,
/// <see cref="ReleaseNotes"/> veri kaynağı ve kategori renk/sırası DEĞİŞMEDİ.</para>
/// </summary>
public partial class NotesDialog : ModalDialog
{
    /// <summary>[v1.19.0 §2.11] <c>INSTALLED</c> çipinin caps ölçüsü. DS ölçeğinde 9.5px adımı YOKTUR (§1.2) —
    /// tasarım kaynağı burada ham bir sayı verir, yani bu component-specific bir ölçüdür ve ARCHITECTURE §14.1
    /// gereği onu çizen kontrolde ADLANDIRILMIŞ tek bir sabit olarak durur.</summary>
    public const double InstalledChipCapsPx = 9.5;

    /// <summary>[v1.19.0 §2.11] Madde metninin satır yüksekliği ORANI (CSS <c>line-height: 1.62</c>). WPF mutlak
    /// DIP ister; oran çizim anında <c>FontSize.Sm</c> token'ının çözülmüş değeriyle çarpılır (13 × 1.62 = 21.06)
    /// — punto token'da değişirse satır yüksekliği onu izler.</summary>
    public const double NoteLineHeightRatio = 1.62;

    /// <summary>[v1.19.0 §2.11] Madde metninin ölçü sınırı (CSS <c>maxWidth: 500</c>).</summary>
    public const double NoteMaxWidth = 500;

    /// <summary>Sürüm bloğunun (ve katlı kısmın) grid kolonları: kimlik kolonu · kolon aralığı · kalan.</summary>
    private const double IdentityColumnPx = 84;
    private const double ColumnGapPx = 26;

    /// <summary>Sürümler arası: 22px boşluk + 1px ayraç + 22px.</summary>
    private const double VersionGapPx = 22;

    // Her sürüm bloğunun grid'i ve yapışan sol kolonu — ScrollChanged'de sticky ofseti yazmak için.
    private readonly List<(Grid Grid, FrameworkElement Column)> _stickyColumns = [];

    public NotesDialog()
    {
        InitializeComponent();
        AddBlockColumns(EarlierVersionsGrid);
    }

    /// <summary>[design v1.13.0 §2.11] Diyalog GÖRÜLDÜ — title bar'daki notes butonunun okunmadı noktası
    /// söner. Kablo MainWindow'da kurulur (kalıcı duruma yazma orada; diyalog yalnız olguyu bildirir):
    /// <see cref="Open"/> çağrıldığı anda ateşlenir (prototipte <c>onSeen</c>, <c>open</c> olduğu anda).</summary>
    public event Action? NotesSeen;

    /// <summary>[test yüzeyi] Çizilmiş sürüm blokları.</summary>
    internal IReadOnlyList<FrameworkElement> WhatsNewBlocks => [.. WhatsNewRows.Children.Cast<FrameworkElement>()];
    internal Button EarlierVersions => EarlierVersionsButton;
    /// <summary>[test yüzeyi] Katlı kısmın bloğu (ayraç + buton) — görünürlük butona değil BUNA yazılır.</summary>
    internal FrameworkElement EarlierVersionsFold => EarlierVersionsBlock;

    /// <summary>[test seam] Çizilecek sürüm listesi — üretimde HER ZAMAN <see cref="ReleaseNotes.All"/>. Testler
    /// sürümler arası ayracı, katlı kısmı ve sticky kaymayı ölçmek için çok sürümlü sentetik bir liste verir
    /// (gerçek listede tek sürüm olabilir; o durumda bu yüzeylerin hiçbiri çizilmez).</summary>
    internal IReadOnlyList<ReleaseEntry> Releases { get; set; } = ReleaseNotes.All;

    /// <summary>Diyaloğu açar: listeyi <c>showAll:false</c> ile kurar (katlama HER açılışta 3'e döner — geri
    /// katlama düğmesi YOKTUR), gövdeyi başa sarar, kabuğun girişiyle gösterir ve AÇILDIĞI ANDA görüldü
    /// işaretlenir.</summary>
    public void Open()
    {
        BuildWhatsNew(showAll: false);
        Body.ScrollToVerticalOffset(0);
        ShowDialog();
        NotesSeen?.Invoke();
    }

    private void OnClose(object sender, RoutedEventArgs e) => CloseDialog();

    // ---------------------------------------------------------------- liste

    private void OnShowEarlierVersions(object sender, RoutedEventArgs e) => BuildWhatsNew(showAll: true);

    /// <summary>
    /// [§2.11] Sürüm listesini kurar: en yeni üstte, <b>son 3 sürüm açık</b>, gerisi ghost bir düğmenin
    /// altında katlı. Katlama diyalog her açılışında 3'e döner (geri katlama düğmesi YOKTUR — açtıysan
    /// okuyorsundur).
    /// </summary>
    private void BuildWhatsNew(bool showAll)
    {
        WhatsNewRows.Children.Clear(); // minik, non-virtualized liste (BuildMenu deseni)
        _stickyColumns.Clear();
        var all = Releases;
        int shown = showAll ? all.Count : Math.Min(ReleaseNotes.OpenByDefault, all.Count);
        for (int i = 0; i < shown; i++) WhatsNewRows.Children.Add(BuildVersionBlock(all[i], first: i == 0));

        int hidden = all.Count - shown;
        EarlierVersionsLabel.Text = ReleaseNotes.EarlierVersionsLabel(hidden);
        // Ayraç + buton tek blok olarak katlanır: buton gizliyken üstündeki hairline de çizilmez.
        EarlierVersionsBlock.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Sürüm bloğu ile katlı kısmın PAYLAŞTIĞI kolon düzeni (84 · 26 · kalan) — tek tanım yeri; katlı
    /// kısmın XAML grid'i de kolonlarını buradan alır (kurucu).</summary>
    private static Grid AddBlockColumns(Grid grid)
    {
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IdentityColumnPx) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColumnGapPx) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    /// <summary>
    /// [v1.19.0 §2.11] Bir sürüm bloğu: 2 kolonlu grid. Sürümler arasında 22px + 1px <c>border-subtle</c> + 22px
    /// (ilk sürümün üstünde ayraç yok).
    ///
    /// <para><b>[DEĞİŞEN KURAL — design v1.19.0 §2.11]</b> ESKİ DÜZEN (v1.13.1): başlık satırı bir DockPanel'di
    /// (solda numara + çip, SAĞA YASLI tarih), kategoriler altında tam genişlikte; sürümler arası 14 + 1 + 14.
    /// Tarihler scrollbar'ın dibinde dağınık duruyordu — artık her sürümde aynı sol kolondadır.</para>
    /// </summary>
    private FrameworkElement BuildVersionBlock(ReleaseEntry entry, bool first)
    {
        var block = new Border
        {
            Margin = new Thickness(0, first ? 0 : VersionGapPx, 0, 0),
            BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0),
            Padding = new Thickness(0, first ? 0 : VersionGapPx, 0, 0),
        };
        block.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSubtle");

        var grid = AddBlockColumns(new Grid());
        var identity = BuildIdentityColumn(entry);
        grid.Children.Add(identity);

        var notes = new StackPanel();
        Grid.SetColumn(notes, 2);
        // [§2.11] Kategori BLOK başlığıdır (satır başına ikon/sigil YOK); boş kategori hiç çizilmez.
        foreach (var kind in ReleaseNotes.KindOrder)
        {
            var items = entry.Notes.Where(n => n.Kind == kind).ToList();
            if (items.Count == 0) continue;
            notes.Children.Add(BuildCategory(kind, items, first: notes.Children.Count == 0));
        }
        grid.Children.Add(notes);

        block.Child = grid;
        _stickyColumns.Add((grid, identity));
        return block;
    }

    /// <summary>[v1.19.0 §2.11] Sol (sticky) kolon: mono 13px/500 sürüm (line-height 1) · 6px · mono 11px
    /// <c>text-faint</c> tarih (line-height 1) · kurulu sürümde 6+2px sonra <c>INSTALLED</c> çipi. Kolonun
    /// kendi üst boşluğu 1px (prototipte <c>paddingTop: 1</c>).</summary>
    private FrameworkElement BuildIdentityColumn(ReleaseEntry entry)
    {
        var column = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Top, // CSS alignSelf: start — sticky kayması kolonun KENDİ boyuyla ölçülür
            RenderTransform = new TranslateTransform(),
        };
        Grid.SetColumn(column, 0);

        var version = MonoLine(entry.Version, "FontSize.Sm", "Brush.TextPrimary");
        version.Margin = new Thickness(0, 1, 0, 0);
        version.SetResourceReference(FontWeightProperty, "FontWeight.Emphasis");
        column.Children.Add(version);

        var date = MonoLine(entry.Date, "FontSize.2xs", "Brush.TextFaint");
        date.Margin = new Thickness(0, 6, 0, 0);
        column.Children.Add(date);

        if (string.Equals(entry.Version, AppIdentity.Version, StringComparison.Ordinal))
            column.Children.Add(BuildInstalledChip());
        return column;
    }

    /// <summary>Mono, <c>line-height: 1</c> bir satır — satır kutusu tam punto kadar (token'dan çözülür).</summary>
    private TextBlock MonoLine(string text, string fontSizeKey, string brushKey)
    {
        var line = new TextBlock
        {
            Text = text,
            FontFamily = AppFonts.Mono,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        line.SetResourceReference(FontSizeProperty, fontSizeKey);
        line.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        line.LineHeight = (double)FindResource(fontSizeKey);
        return line;
    }

    /// <summary>[v1.19.0 §2.11] Nötr <c>INSTALLED</c> çipi, sol kolonda tarihin altında: 16px yüksek, yatay padding
    /// 5px, <c>surface</c> zemin + 1px <c>border-strong</c>, <c>radius-xs</c>, 9.5px caps <c>text-dim</c> metin,
    /// sola yaslı (içeriğe sıkı). <see cref="TrackedTextBlock"/>'un KENDİ DP'leri doğrudan sürülür —
    /// TextBlock'un Foreground/FontSize'ı burada ETKİSİZDİR (ayrı bir DependencyProperty ailesi).
    ///
    /// <para><b>[DEĞİŞEN KURAL — design v1.19.0]</b> ESKİ ÇİP (v1.13.1): sürüm numarasının sağında, 17px yüksek,
    /// padding 6px, <c>surface-raised</c> zemin, 10px caps (başlıktaki <c>INSTALLED VERSION</c> ile paylaşılan
    /// <c>CapsLabelPx</c>). Başlık etiketi kalktığı için paylaşılan sabit de kalktı.</para></summary>
    private static FrameworkElement BuildInstalledChip()
    {
        var chip = new Border
        {
            Height = 16,
            Padding = new Thickness(5, 0, 5, 0),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 8, 0, 0), // [prototip gap 6 + marginTop 2] tarihin altında
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        chip.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        chip.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderStrong");
        chip.SetResourceReference(Border.CornerRadiusProperty, "Radius.Xs");

        var label = new TrackedTextBlock
        {
            Text = "INSTALLED",
            FontSize = InstalledChipCapsPx,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        label.SetResourceReference(TrackedTextBlock.ForegroundProperty, "Brush.TextDim");
        label.SetResourceReference(TrackedTextBlock.FontWeightProperty, "FontWeight.Emphasis");
        chip.Child = label;
        return chip;
    }

    /// <summary>[v1.19.0 §2.11] Kategori bloğu (sağ kolon): 6px kare + 7px + caps <c>text-dim</c> başlık; başlık
    /// altında 7px; maddeler 13px içeriden, aralarında 10px, 13px <c>text-secondary</c>, satır yüksekliği 1.62,
    /// en çok 500px genişlikte sarılır. Kategori blokları arası 15px.</summary>
    private FrameworkElement BuildCategory(NoteKind kind, IReadOnlyList<ReleaseNote> items, bool first)
    {
        var group = new StackPanel { Margin = new Thickness(0, first ? 0 : 15, 0, 0) };

        var heading = new StackPanel { Orientation = Orientation.Horizontal };
        var swatch = new System.Windows.Shapes.Rectangle
        {
            Width = 6,
            Height = 6,
            RadiusX = 1,
            RadiusY = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };
        swatch.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, ReleaseNotes.SwatchBrushKey(kind));
        heading.Children.Add(swatch);

        var label = new TrackedTextBlock
        {
            Text = ReleaseNotes.Label(kind),
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Hedef DP'ler AÇIKÇA nitelenir (BuildInstalledChip ile AYNI kural): TrackedTextBlock kendi
        // Foreground/FontSize DP'lerini kaydeder, TextBlock/Control ailesindekiler burada ETKİSİZDİR.
        label.SetResourceReference(TrackedTextBlock.FontSizeProperty, "FontSize.2xs");
        label.SetResourceReference(TrackedTextBlock.ForegroundProperty, "Brush.TextDim");
        heading.Children.Add(label);
        group.Children.Add(heading);

        var list = new StackPanel { Margin = new Thickness(13, 7, 0, 0) };
        double lineHeight = (double)FindResource("FontSize.Sm") * NoteLineHeightRatio;
        foreach (var note in items)
        {
            var text = new TextBlock
            {
                Text = note.Text,
                Margin = new Thickness(0, list.Children.Count == 0 ? 0 : 10, 0, 0),
                MaxWidth = NoteMaxWidth,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextWrapping = TextWrapping.Wrap,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                LineHeight = lineHeight,
            };
            text.SetResourceReference(FontSizeProperty, "FontSize.Sm");
            text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
            list.Children.Add(text);
        }
        group.Children.Add(list);
        return group;
    }

    // ---------------------------------------------------------------- sticky sol kolon

    /// <summary>[v1.19.0 §2.11] CSS <c>position: sticky; top: 0</c>: gövde her kaydığında (ve liste yeniden
    /// yerleştiğinde — <c>ExtentHeight</c> değişimi de <c>ScrollChanged</c> doğurur) her bloğun sol kolonu
    /// <see cref="StickyColumn.Offset"/> kadar aşağı itilir. Blok konumu kayan listenin KENDİ koordinatında
    /// okunur (liste kenar boşluğu hariç) — kolon böylece gövdenin 22px iç üst boşluğunun altına yapışır,
    /// CSS'in scroll kutusu padding'ine saygı duyan sticky kutusuyla aynı.</summary>
    private void OnBodyScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        foreach (var (grid, column) in _stickyColumns)
        {
            if (!grid.IsVisible || column.RenderTransform is not TranslateTransform shift) continue;
            double blockTop = grid.TranslatePoint(new Point(0, 0), BodyList).Y;
            shift.Y = StickyColumn.Offset(Body.VerticalOffset, blockTop, grid.ActualHeight, column.ActualHeight);
        }
    }
}
