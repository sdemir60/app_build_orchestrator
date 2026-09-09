using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [design v1.13.0/v1.13.1 §2.11 · D4/T9] What's new — sürüm notlarının kendi diyalogu. About'un dördüncü
/// sekmesiydi (design v1.9.0); v1.13.0 bunu About'tan ÇIKARDI, kendi title bar butonu (sparkle) ve kendi
/// kısayolu (Ctrl+F1) verdi. Kabuk <see cref="AboutDialog"/>/<see cref="SettingsDialog"/> ile AYNIdır (scrim,
/// Ds.Dialog, odak tuzağı, Esc/scrim ile kapanma) — farkı kimlik bloğu ve sekme TAŞIMAMASI: dialogun tek işi
/// var.
///
/// <para><b>Liste kurma kodu About'tan TAŞINDI, KOPYALANMADI</b> (kopya YASAK, CLAUDE.md): aşağıdaki
/// <see cref="BuildWhatsNew"/>/<see cref="BuildVersionBlock"/>/<see cref="BuildCategory"/> eskiden
/// <c>AboutDialog.xaml.cs</c>'te yaşıyordu. Liste KURALLARI v1.9.0'dan DEĞİŞMEDİ; yalnız iki şey değişti
/// (v1.13.1): <c>CURRENT</c> metni nötr <c>INSTALLED</c> çipine döndü (bkz. <see cref="BuildInstalledChip"/>),
/// <c>Earlier versions</c> butonu içerik koluna hizalandı (XAML'de negatif sol margin — kod tarafında bir şey
/// değişmedi).</para>
/// </summary>
public partial class NotesDialog : UserControl
{
    public NotesDialog() => InitializeComponent();

    /// <summary>[design v1.13.0 §2.11] Diyalog GÖRÜLDÜ — title bar'daki sparkle butonunun okunmadı noktası
    /// söner. Kablo MainWindow'da kurulur (kalıcı duruma yazma orada; diyalog yalnız olguyu bildirir) —
    /// About'un eski <c>NotesSeen</c> deseniyle AYNI, yalnız artık tetikleyici bir SEKME değil DİYALOĞUN
    /// KENDİSİ: <see cref="Open"/> çağrıldığı anda ateşlenir (prototipte <c>onSeen</c>, <c>open</c> olduğu
    /// anda — BuildApp.jsx:1557).</summary>
    public event Action? NotesSeen;

    /// <summary>[test yüzeyi] Çizilmiş sürüm blokları.</summary>
    internal IReadOnlyList<FrameworkElement> WhatsNewBlocks => [.. WhatsNewRows.Children.Cast<FrameworkElement>()];
    internal Button EarlierVersions => EarlierVersionsButton;

    /// <summary>Diyaloğu açar: listeyi <c>showAll:false</c> ile kurar (katlama HER açılışta 3'e döner — geri
    /// katlama düğmesi YOKTUR, About'un eski davranışıyla AYNI), 180ms fade + 6px yukarı ile gösterir ve
    /// AÇILDIĞI ANDA görüldü işaretlenir.</summary>
    public void Open()
    {
        BuildWhatsNew(showAll: false);
        Visibility = Visibility.Visible;
        // [design-v1.2.1/v1.13.0 §2.10/§2.11] 180ms fade + 6px yukarı — About'la AYNI giriş (Controls.PopIn
        // paylaşılır, kopya YASAK).
        Controls.PopIn.PlayDialog(DialogShell);
        Focus(); // Esc HER durumda yakalanabilsin (MoveFocus altta bir şey bulamazsa bile odak burada kalır)
        Scrim.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        NotesSeen?.Invoke();
    }

    private void Close() => Visibility = Visibility.Collapsed;

    /// <summary>Esc zincirinin dialog katmanı için dışarıdan kapatma (MainWindow güvenlik ağı — odak dialog
    /// dışındayken). Dialog odaklıyken Esc'i <see cref="OnKeyDown"/> yakalar (handled) — About'unkiyle
    /// AYNI desen.</summary>
    public void CloseDialog() => Close();

    // ---------------------------------------------------------------- [design v1.9.0 §2.10, taşındı] liste

    private void OnShowEarlierVersions(object sender, RoutedEventArgs e) => BuildWhatsNew(showAll: true);

    /// <summary>
    /// [§2.11] Sürüm listesini kurar: en yeni üstte, <b>son 3 sürüm açık</b>, gerisi ghost bir düğmenin
    /// altında katlı. Katlama diyalog her açılışında 3'e döner (geri katlama düğmesi YOKTUR — açtıysan
    /// okuyorsundur).
    /// </summary>
    private void BuildWhatsNew(bool showAll)
    {
        WhatsNewRows.Children.Clear(); // minik, non-virtualized liste (BuildMenu deseni)
        var all = ReleaseNotes.All;
        int shown = showAll ? all.Count : Math.Min(ReleaseNotes.OpenByDefault, all.Count);
        for (int i = 0; i < shown; i++) WhatsNewRows.Children.Add(BuildVersionBlock(all[i], first: i == 0));

        int hidden = all.Count - shown;
        EarlierVersionsButton.Content = ReleaseNotes.EarlierVersionsLabel(hidden);
        EarlierVersionsButton.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>[§2.11] Bir sürüm bloğu: mono numara + (kuruluysa) nötr <c>INSTALLED</c> çipi + sağa yaslı
    /// tarih; altında kategori BLOKLARI. Sürümler arasında 14px boşluk + 1px ayraç.
    ///
    /// <para><b>[DEĞİŞEN KURAL — design v1.13.1 §2.11]</b> ESKİ İDDİA (design v1.1.0/v1.9.0, About'un
    /// dördüncü sekmesindeyken): güncel sürümde sessiz bir <c>CURRENT</c> metni dururdu (zemin/çerçeve yok,
    /// yalnız text-faint). Amber rozet VE çerçevesiz caps metin ikisi de denendi; ölçüm sonrası nötr bir ÇİP
    /// seçildi (bkz. <see cref="BuildInstalledChip"/>) ve etiket <c>CURRENT</c>'tan <c>INSTALLED</c>'a döndü —
    /// başlık satırındaki "INSTALLED VERSION" bloğuyla AYNI sözcüğü kullanır.</para></summary>
    private FrameworkElement BuildVersionBlock(ReleaseEntry entry, bool first)
    {
        var block = new StackPanel { Margin = new Thickness(0, first ? 0 : 14, 0, 0) };
        if (!first)
        {
            var divider = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 14) };
            divider.SetResourceReference(Border.BackgroundProperty, "Brush.BorderSubtle");
            block.Children.Insert(0, divider);
        }

        var header = new DockPanel();
        var date = new TextBlock { Text = entry.Date, VerticalAlignment = VerticalAlignment.Center, FontFamily = Controls.AppFonts.Mono };
        date.SetResourceReference(FontSizeProperty, "FontSize.2xs");
        date.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextFaint");
        DockPanel.SetDock(date, Dock.Right);
        header.Children.Add(date);

        var version = new TextBlock { Text = entry.Version, VerticalAlignment = VerticalAlignment.Center, FontFamily = Controls.AppFonts.Mono };
        version.SetResourceReference(FontSizeProperty, "FontSize.Sm");
        version.SetResourceReference(FontWeightProperty, "FontWeight.Emphasis");
        version.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
        header.Children.Add(version);

        if (string.Equals(entry.Version, AppIdentity.Version, StringComparison.Ordinal))
            header.Children.Add(BuildInstalledChip());
        block.Children.Add(header);

        // [§2.11] Kategori BLOK başlığıdır (satır başına ikon/sigil YOK); boş kategori hiç çizilmez.
        foreach (var kind in ReleaseNotes.KindOrder)
        {
            var items = entry.Notes.Where(n => n.Kind == kind).ToList();
            if (items.Count == 0) continue;
            block.Children.Add(BuildCategory(kind, items));
        }
        return block;
    }

    /// <summary>[§2.11 · v1.13.1] Nötr <c>INSTALLED</c> çipi — <see cref="BuildVersionBlock"/>'un CURRENT
    /// metninin yerini alan tek yeni parça: 17px yüksek, yatay padding 6px, <c>surface-raised</c> zemin + 1px
    /// <c>border-strong</c>, <c>radius-xs</c>, 10px caps <c>text-dim</c>. 10px ölçek TOKENİNDE karşılığı
    /// yoktur — tasarım kaynağı da ham <c>fontSize: 10</c> kullanır; component-specific ölçü olarak burada
    /// literal kalır (ARCHITECTURE §14.1'in izin verdiği istisna). <see cref="Controls.TrackedTextBlock"/>'un
    /// KENDİ DP'leri doğrudan (SetResourceReference'ın hedef DP'si AÇIKÇA nitelenerek) sürülür — TextBlock'un
    /// Foreground/FontSize'ı burada ETKİSİZDİR (ayrı bir DependencyProperty ailesi).</summary>
    private static FrameworkElement BuildInstalledChip()
    {
        var chip = new Border
        {
            Height = 17,
            Padding = new Thickness(6, 0, 6, 0),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(8, 0, 0, 0), // [prototip gap:8] sürüm numarasıyla arasındaki boşluk
            VerticalAlignment = VerticalAlignment.Center,
        };
        chip.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceRaised");
        chip.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderStrong");
        chip.SetResourceReference(Border.CornerRadiusProperty, "Radius.Xs");

        var label = new Controls.TrackedTextBlock
        {
            Text = "INSTALLED",
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        label.SetResourceReference(Controls.TrackedTextBlock.ForegroundProperty, "Brush.TextDim");
        chip.Child = label;
        return chip;
    }

    private FrameworkElement BuildCategory(NoteKind kind, IReadOnlyList<ReleaseNote> items)
    {
        var group = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };

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

        var label = new Controls.TrackedTextBlock
        {
            Text = ReleaseNotes.Label(kind),
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Hedef DP'ler AÇIKÇA nitelenir (BuildInstalledChip ile AYNI kural): TrackedTextBlock kendi
        // Foreground/FontSize DP'lerini kaydeder, TextBlock/Control ailesindekiler burada ETKİSİZDİR ve
        // etiket sessizce ctor varsayılanına (Brush.TextFaint) düşerdi.
        label.SetResourceReference(Controls.TrackedTextBlock.FontSizeProperty, "FontSize.2xs");
        label.SetResourceReference(Controls.TrackedTextBlock.ForegroundProperty, "Brush.TextDim");
        heading.Children.Add(label);
        group.Children.Add(heading);

        foreach (var note in items)
        {
            var text = new TextBlock
            {
                Text = note.Text,
                Margin = new Thickness(13, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            text.SetResourceReference(FontSizeProperty, "FontSize.Sm");
            text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
            text.SetResourceReference(TextBlock.LineHeightProperty, "LineHeight.Snug13"); // 13px gövde → snug
            group.Children.Add(text);
        }
        return group;
    }

    // ---------------------------------------------------------------- kapatma

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // Scrim tıklaması kapatır; diyaloğun kendi içine tıklama scrim'e ULAŞMAZ.
    private void OnScrimClick(object sender, MouseButtonEventArgs e) => Close();
    private void OnDialogClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }
}
