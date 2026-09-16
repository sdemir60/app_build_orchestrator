using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.13.0/v1.13.1/v1.19.0 §2.11 · D4/T9] What's new — kendi diyalogu. Kabuk (scrim + Ds.Dialog + odak
/// tuzağı + Esc/scrim) üç dialogun ORTAK kabuğudur (<c>Controls/ModalDialog</c>, bkz. DialogShellTests); bu
/// XAML kökünün kendi realize kanıtı yine burada durur (CLAUDE.md realize kuralı). İçerik testleri
/// <c>AboutDialogTests</c>/<c>WhatsNewTests</c>'in eski <c>dialog.WhatsNew.IsChecked = true</c> sekme-testlerinin
/// YERİNİ alır (About'un dördüncü sekmesi KALKTI — bkz. AboutDialogTests, WhatsNewTests).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class NotesDialogTests
{
    private static Border Shell(BuildOrchestrator.App.Views.NotesDialog dialog) =>
        (Border)VisualTreeHelper.GetChild(dialog.Scrim, 0);

    private static List<string> VisibleTexts(FrameworkElement dialog) =>
        [.. DsResources.Descendants(dialog).OfType<TextBlock>().Select(t => t.Text)];

    /// <summary>Bir sürüm bloğunun 2 kolonlu grid'i (84 · 26 · kalan).</summary>
    private static Grid BlockGrid(FrameworkElement block) =>
        DsResources.Descendants(block).OfType<Grid>().First(g => g.ColumnDefinitions.Count == 3);

    private static FrameworkElement Column(Grid grid, int column) =>
        grid.Children.Cast<FrameworkElement>().Single(c => Grid.GetColumn(c) == column);

    private static double TopIn(FrameworkElement element, FrameworkElement root) =>
        element.TranslatePoint(new Point(0, 0), root).Y;

    // ---------------------------------------------------------------- kabuk

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — design v1.19.0 §2.11]</b> ESKİ İDDİA (v1.13.1): dialog 620px genişti, yüksekliği
    /// içerikten (başlık + sabit 400px gövde + footer) doğardı. v1.19.0 dialogu 720×600'e SABİTLEDİ: sürüm
    /// kimliği sol sütuna taşındı (84px + 26px aralık), maddelerin 500px ölçüsü ancak 720'de sığıyor; yükseklik
    /// artık dialogun kendisinde sabittir ve gövde kalan alanı doldurur.
    /// </summary>
    [StaFact]
    public void The_dialog_realizes_at_seven_hundred_twenty_by_six_hundred()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            Assert.Equal(Visibility.Visible, dialog.Visibility);
            Assert.Equal(720.0, Shell(dialog).Width);
            Assert.Equal(600.0, Shell(dialog).Height);
            Assert.Equal(720.0, Shell(dialog).ActualWidth); // realize zorunlu — literal okumak yetmez
            Assert.Equal(600.0, Shell(dialog).ActualHeight);
        }
    }

    /// <summary>Yapısal kanıt: scrim bir Cycle klavye-gezinme kapsayıcısı ve bir odak kapsamı — About/Settings
    /// ile AYNI kabuk kuralı, bu XAML kökünde de baştan kurulur.</summary>
    [StaFact]
    public void The_scrim_is_a_cyclic_keyboard_focus_scope()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(dialog.Scrim));
            Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetControlTabNavigation(dialog.Scrim));
            Assert.True(FocusManager.GetIsFocusScope(dialog.Scrim));
        }
    }

    [StaFact]
    public void Tab_navigation_cannot_escape_the_open_dialog()
    {
        var background = new Button { Content = "Background Build", Focusable = true, Width = 90, Height = 24 };
        var (dialog, scope) = NotesDialogHost.OpenRealized(backgroundSibling: background);
        using (scope)
            FocusTrap.AssertCannotEscape(dialog.Scrim, background);
    }

    /// <summary>[design-v1.2.1/v1.13.0 §2.11] Diyalog giriş animasyonu About'la AYNI: 180ms fade + 6px yukarı
    /// (Controls.PopIn paylaşılır — kopya YASAK).</summary>
    [StaFact]
    public void The_dialog_enters_with_a_180ms_fade_and_a_6px_rise()
    {
        Assert.Equal(180.0, PopIn.DialogDurationMs);
        Assert.Equal(6.0, PopIn.DialogRiseFromPx);
    }

    [StaFact]
    public void Opening_the_dialog_installs_the_entrance_transform_on_the_shell()
    {
        using var _ = MotionScope.Enable(new MotionSettings(new FakeMotionSignal { AnimationsEnabled = true }));
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
            Assert.IsType<TranslateTransform>(Shell(dialog).RenderTransform);
    }

    [StaFact]
    public void Reduced_motion_snaps_the_dialog_to_its_final_state()
    {
        using var _ = MotionScope.Enable(new MotionSettings(new FakeMotionSignal { AnimationsEnabled = false }));
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            Assert.Equal(1.0, Shell(dialog).Opacity);
            Assert.Equal(Transform.Identity, Shell(dialog).RenderTransform);
        }
    }

    [StaFact]
    public void Close_dialog_hides_it()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            dialog.CloseDialog();
            Assert.Equal(Visibility.Collapsed, dialog.Visibility);
        }
    }

    /// <summary>[§2.11] Dialog açıldığı ANDA görüldü işaretlenir — prototipte <c>onSeen</c>, <c>open</c> olduğu
    /// anda (BuildApp.jsx:1557). Kalıcı duruma yazma MainWindow'un işi (bkz. NotesDialogWiringTests); burada
    /// yalnız olgu bildirimi pinlenir.</summary>
    [StaFact]
    public void Opening_the_dialog_raises_notes_seen()
    {
        int seenCount = 0;
        var (_, scope) = NotesDialogHost.OpenRealized(configure: d => d.NotesSeen += () => seenCount++);
        using (scope)
            Assert.Equal(1, seenCount);
    }

    // ---------------------------------------------------------------- başlık satırı

    /// <summary>[v1.19.0 §2.11] Başlık satırı padding <c>20px 18px 16px</c>, altta 1px <c>border-subtle</c>;
    /// alt başlık başlığın 3px altındadır.</summary>
    [StaFact]
    public void The_header_shows_the_title_and_subtitle_inside_a_20_18_16_padded_row()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            var texts = VisibleTexts(dialog);
            Assert.Contains("What's new", texts);
            Assert.Contains("Release notes for Build Orchestrator.", texts);

            var title = DsResources.Descendants(dialog).OfType<TextBlock>().Single(t => t.Text == "What's new");
            var subtitle = DsResources.Descendants(dialog).OfType<TextBlock>()
                .Single(t => t.Text == "Release notes for Build Orchestrator.");
            Assert.Equal(3.0, subtitle.Margin.Top);

            var row = DsResources.Ancestors(title).OfType<Border>().First(b => b.BorderThickness == new Thickness(0, 0, 0, 1));
            Assert.Equal(new Thickness(18, 20, 18, 16), row.Padding);
            Assert.Equal(DsResources.TokenColor(dialog, "Brush.BorderSubtle"), DsResources.ColorOf(row.BorderBrush));
        }
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — design v1.19.0 §2.11]</b> ESKİ İDDİA (v1.13.1): başlığın sağında iki satırlı bir
    /// blok dururdu — 10px caps <c>INSTALLED VERSION</c> etiketi + mono 14px/500 sürüm, sağ padding'i 28px
    /// (scrollbar hizası). v1.19.0 bunu TEK bir mono çipe indirdi: 20px yüksek, yatay padding 7px, 1px
    /// <c>border-strong</c>, <c>radius-xs</c>, <c>surface</c> zemin, mono 12px <c>text-secondary</c>. Sürüm
    /// numarası yine LİTERAL DEĞİLDİR — <see cref="AppIdentity.Version"/>'dan gelir. Eski caps etiket bir daha
    /// üretilmez.
    /// </summary>
    [StaFact]
    public void The_header_carries_a_mono_version_chip_reading_app_identity()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            var chip = DsResources.Descendants(dialog).OfType<TextBlock>()
                .Where(t => t.Text == AppIdentity.Version)
                .Select(t => VisualTreeHelper.GetParent(t)).OfType<Border>()
                .Single(b => b.Height == 20.0);
            var text = (TextBlock)chip.Child;

            Assert.Equal(20.0, chip.ActualHeight);
            Assert.Equal(new Thickness(7, 0, 7, 0), chip.Padding);
            Assert.Equal(new Thickness(1), chip.BorderThickness);
            Assert.Equal(DsResources.TokenColor(dialog, "Brush.Surface"), DsResources.ColorOf(chip.Background));
            Assert.Equal(DsResources.TokenColor(dialog, "Brush.BorderStrong"), DsResources.ColorOf(chip.BorderBrush));
            Assert.Equal((CornerRadius)dialog.FindResource("Radius.Xs"), chip.CornerRadius);
            Assert.Equal(AppFonts.Mono, text.FontFamily);
            Assert.Equal((double)dialog.FindResource("FontSize.Xs"), text.FontSize);
            Assert.Equal(DsResources.TokenColor(dialog, "Brush.TextSecondary"), DsResources.ColorOf(text.Foreground));

            Assert.DoesNotContain(DsResources.Descendants(dialog).OfType<TrackedTextBlock>(),
                t => t.Text == "INSTALLED VERSION");
        }
    }

    /// <summary>About'un kimlik bloğu ve Segment'i YOK — dialogun tek işi var (§2.11).</summary>
    [StaFact]
    public void The_dialog_has_no_identity_block_and_no_segment()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            Assert.Empty(DsResources.Descendants(dialog).OfType<AppMark>());
            Assert.Empty(DsResources.Descendants(dialog).OfType<RadioButton>());
        }
    }

    // ---------------------------------------------------------------- gövde

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — design v1.19.0 §2.11]</b> ESKİ İDDİA (<c>The_body_height_is_fixed_at_400px</c>,
    /// v1.13.1): gövde SABİT 400px'lik bir Grid'di, dialogun boyu içerikten doğardı. v1.19.0'da SABİT olan
    /// dialogun kendisidir (600px); gövde başlık ile footer arasında KALAN alanı doldurur ve kendi içinde kayar
    /// — Earlier versions açılınca dialog yine uzamaz. İçerik <c>22px 18px 24px</c> iç boşlukla kayar
    /// (prototipte scroll kutusunun padding'i; WPF'te kayan içeriğin kenar boşluğu).
    /// </summary>
    [StaFact]
    public void The_body_fills_the_space_between_header_and_footer_and_scrolls_inside_itself()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            Assert.DoesNotContain(DsResources.Descendants(dialog).OfType<Grid>(), g => g.Height == 400.0);

            var scroller = DsResources.Descendants(dialog).OfType<ScrollViewer>().Single();
            Assert.Equal(ScrollBarVisibility.Auto, scroller.VerticalScrollBarVisibility);
            Assert.Equal(new Thickness(18, 22, 18, 24), ((FrameworkElement)scroller.Content).Margin);

            var title = DsResources.Descendants(dialog).OfType<TextBlock>().Single(t => t.Text == "What's new");
            var head = DsResources.Ancestors(title).OfType<Border>().First(b => b.BorderThickness == new Thickness(0, 0, 0, 1));
            var close = DsResources.Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "Close"));
            var footer = DsResources.Ancestors(close).OfType<Border>().First(b => b.BorderThickness == new Thickness(0, 1, 0, 0));

            var frame = Shell(dialog);
            double inner = frame.ActualHeight - frame.BorderThickness.Top - frame.BorderThickness.Bottom;
            Assert.Equal(inner - head.ActualHeight - footer.ActualHeight, scroller.ActualHeight, precision: 3);
        }
    }

    // ---------------------------------------------------------------- liste

    /// <summary>[§2.11] Sürüm başına bir blok; blokta mono sürüm numarası ve tarih durur.</summary>
    [StaFact]
    public void The_dialog_draws_one_block_per_version()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            var blocks = dialog.WhatsNewBlocks;
            Assert.Equal(Math.Min(ReleaseNotes.OpenByDefault, ReleaseNotes.All.Count), blocks.Count);

            var texts = DsResources.Descendants(blocks[0]).OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains(AppIdentity.Version, texts);
            Assert.Contains(ReleaseNotes.All[0].Date, texts);
        }
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — design v1.19.0 §2.11]</b> ESKİ İDDİA (v1.13.1): bloğun başlık satırı bir DockPanel'di
    /// — solda sürüm + çip, SAĞA YASLI tarih (tarihler scrollbar'ın dibinde dururdu). v1.19.0 bloğu 2 kolonlu
    /// grid'e çevirdi: 84px sol kolon + 26px aralık + kalan. Sürüm, tarih ve çip SOL kolonda alt alta durur
    /// (tarih sağa yaslı DEĞİL); kategori blokları sağ kolondadır.
    /// </summary>
    [StaFact]
    public void Each_version_block_is_a_two_column_grid_with_the_identity_in_the_84px_left_column()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            var grid = BlockGrid(dialog.WhatsNewBlocks[0]);
            Assert.Equal(new GridLength(84), grid.ColumnDefinitions[0].Width);
            Assert.Equal(new GridLength(26), grid.ColumnDefinitions[1].Width);
            Assert.Equal(new GridLength(1, GridUnitType.Star), grid.ColumnDefinitions[2].Width);

            var left = Column(grid, 0);
            var leftTexts = DsResources.Descendants(left).OfType<TextBlock>().ToList();
            var version = leftTexts.Single(t => t.Text == AppIdentity.Version);
            var date = leftTexts.Single(t => t.Text == ReleaseNotes.All[0].Date);
            Assert.NotEqual(HorizontalAlignment.Right, date.HorizontalAlignment);
            Assert.Equal(0.0, date.TranslatePoint(new Point(0, 0), left).X);

            Assert.Equal(AppFonts.Mono, version.FontFamily);
            Assert.Equal((double)dialog.FindResource("FontSize.Sm"), version.FontSize);
            Assert.Equal(AppFonts.Mono, date.FontFamily);
            Assert.Equal((double)dialog.FindResource("FontSize.2xs"), date.FontSize);
            Assert.Equal(DsResources.TokenColor(dialog, "Brush.TextFaint"), DsResources.ColorOf(date.Foreground));
            // tarih sürümün 6px altında (line-height 1 → satır kutusu = punto)
            Assert.Equal(6.0, TopIn(date, left) - (TopIn(version, left) + version.ActualHeight), precision: 3);

            Assert.Empty(DsResources.Descendants(Column(grid, 2)).OfType<TextBlock>().Where(t => t.Text == ReleaseNotes.All[0].Date));
        }
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — design v1.19.0 §2.11]</b> ESKİ İDDİA (v1.13.1): <c>INSTALLED</c> çipi sürüm
    /// numarasının SAĞINDA dururdu — 17px yüksek, yatay padding 6px, <c>surface-raised</c> zemin, 10px caps
    /// (başlıktaki <c>INSTALLED VERSION</c> ile paylaşılan ölçü). v1.19.0 çipi sol kolona, tarihin altına indirdi:
    /// 16px yüksek, yatay padding 5px, <c>surface</c> zemin (dialog zemininden bir ton koyu), 9.5px caps; sola
    /// yaslı, içeriğe sıkı. Başlık etiketi kalktığı için 10px'lik ortak sabit de kalktı; 9.5 çipin kendi
    /// adlandırılmış sabitidir. Eski <c>CURRENT</c> metni hâlâ üretilmez.
    /// </summary>
    [StaFact]
    public void The_installed_entry_shows_a_16px_installed_chip_in_the_left_column()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            var chipLabel = DsResources.Descendants(dialog).OfType<TrackedTextBlock>()
                .Single(t => t.Text.Equals("INSTALLED", StringComparison.Ordinal));
            var chip = (Border)VisualTreeHelper.GetParent(chipLabel);

            Assert.Equal(16.0, chip.Height);
            Assert.Equal(16.0, chip.ActualHeight);
            Assert.Equal(new Thickness(5, 0, 5, 0), chip.Padding);
            Assert.Equal(new Thickness(1), chip.BorderThickness);
            Assert.Equal(HorizontalAlignment.Left, chip.HorizontalAlignment);
            Assert.Equal(DsResources.TokenColor(dialog, "Brush.Surface"), DsResources.ColorOf(chip.Background));
            Assert.Equal(DsResources.TokenColor(dialog, "Brush.BorderStrong"), DsResources.ColorOf(chip.BorderBrush));
            Assert.Equal((CornerRadius)dialog.FindResource("Radius.Xs"), chip.CornerRadius);
            Assert.Equal(DsResources.TokenColor(dialog, "Brush.TextDim"), DsResources.ColorOf(chipLabel.Foreground));
            Assert.Equal(BuildOrchestrator.App.Views.NotesDialog.InstalledChipCapsPx, chipLabel.FontSize);
            Assert.Equal(9.5, chipLabel.FontSize);

            var left = Column(BlockGrid(dialog.WhatsNewBlocks[0]), 0);
            Assert.True(DsResources.IsSelfOrDescendantOf(chip, left), "çip sol kolonda değil");
            var date = DsResources.Descendants(left).OfType<TextBlock>().Single(t => t.Text == ReleaseNotes.All[0].Date);
            Assert.Equal(8.0, TopIn(chip, left) - (TopIn(date, left) + date.ActualHeight), precision: 3); // 6 + 2

            Assert.DoesNotContain(VisibleTexts(dialog), t => t == "CURRENT");
            Assert.DoesNotContain(DsResources.Descendants(dialog).OfType<TrackedTextBlock>(), t => t.Text == "CURRENT");
        }
    }

    /// <summary>[§2.11] Kategori BLOK başlığıdır: 6px renkli kare + caps <c>text-dim</c> etiket, sabit
    /// sırada. Maddelerin başında ikon/sigil YOKTUR. (WhatsNewTests'ten TAŞINDI — About'un dördüncü sekmesi
    /// kalktı.)
    ///
    /// <para>Başlığın RENGİ de pinlenir: etiket bir <see cref="TrackedTextBlock"/>'tur ve o KENDİ
    /// <c>Foreground</c>/<c>FontSize</c> DP'lerini kaydeder — <c>TextBlock</c>/<c>Control</c> ailesindeki
    /// aynı adlı DP'lere yazmak bu kontrolde ETKİSİZDİR ve etiket sessizce ctor varsayılanıyla
    /// (<c>Brush.TextFaint</c>) çizilir.</para></summary>
    [StaFact]
    public void Each_category_is_a_block_heading_with_a_six_pixel_swatch_and_no_per_line_sigils()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            var first = dialog.WhatsNewBlocks[0];
            var swatches = DsResources.Descendants(first).OfType<System.Windows.Shapes.Rectangle>().ToList();
            var kinds = ReleaseNotes.All[0].Notes.Select(n => n.Kind).Distinct().ToList();
            Assert.Equal(kinds.Count, swatches.Count);
            Assert.All(swatches, r => Assert.Equal(6.0, r.Width));
            Assert.All(swatches, r => Assert.Equal(6.0, r.Height));

            var headings = DsResources.Descendants(first).OfType<TrackedTextBlock>()
                .Where(t => t.Text != "INSTALLED").ToList();
            Assert.Equal(ReleaseNotes.KindOrder.Where(kinds.Contains).Select(ReleaseNotes.Label),
                headings.Select(t => t.Text));
            Assert.All(headings, t => Assert.Equal(
                DsResources.TokenColor(dialog, "Brush.TextDim"), DsResources.ColorOf(t.Foreground)));
            Assert.All(headings, t => Assert.Equal((double)dialog.FindResource("FontSize.2xs"), t.FontSize));

            Assert.Empty(DsResources.Descendants(first).OfType<System.Windows.Shapes.Path>());
        }
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — design v1.19.0 §2.11]</b> ESKİ ÖLÇÜLER (v1.13.1, pinli değildi ama XAML'de öyleydi):
    /// kategoriler arası 10px, başlık altı 4px, maddeler arası 4px, satır yüksekliği snug (17.55), ölçü sınırı
    /// yok. v1.19.0 okunurluk için açtı: kategori blokları arası 15px, başlık ile ilk madde arası 7px, maddeler
    /// 13px içeriden, <b>aralarında 10px</b>, satır yüksekliği 13 × 1.62 = <b>21.06</b>, en çok <b>500px</b>
    /// genişlikte sarılır.
    /// </summary>
    [StaFact]
    public void Notes_are_spaced_10px_apart_with_a_1_62_line_height_and_a_500px_measure()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized(configure: d => d.Releases = NotesDialogHost.SyntheticReleases(1));
        using (scope)
        {
            var right = (Panel)Column(BlockGrid(dialog.WhatsNewBlocks[0]), 2);
            var categories = right.Children.Cast<FrameworkElement>().ToList();
            var notes = DsResources.Descendants(right).OfType<TextBlock>().ToList();
            Assert.NotEmpty(notes);
            Assert.All(notes, n => Assert.Equal(21.06, n.LineHeight, precision: 6));
            Assert.All(notes, n => Assert.Equal(500.0, n.MaxWidth));
            Assert.All(notes, n => Assert.Equal(TextWrapping.Wrap, n.TextWrapping));
            Assert.All(notes, n => Assert.Equal(13.0, n.TranslatePoint(new Point(0, 0), right).X, precision: 3));

            for (int c = 1; c < categories.Count; c++)
                Assert.Equal(15.0, TopIn(categories[c], right) - (TopIn(categories[c - 1], right) + categories[c - 1].ActualHeight), precision: 3);

            // Aynı kategorideki ardışık iki madde: 10px aralık. Başlık ile ilk madde: 7px.
            var withTwo = categories.First(c => DsResources.Descendants(c).OfType<TextBlock>().Count() >= 2);
            var items = DsResources.Descendants(withTwo).OfType<TextBlock>().ToList();
            Assert.Equal(10.0, TopIn(items[1], withTwo) - (TopIn(items[0], withTwo) + items[0].ActualHeight), precision: 3);
            var heading = DsResources.Descendants(withTwo).OfType<TrackedTextBlock>().Single();
            var headingRow = (FrameworkElement)VisualTreeHelper.GetParent(heading);
            Assert.Equal(7.0, TopIn(items[0], withTwo) - (TopIn(headingRow, withTwo) + headingRow.ActualHeight), precision: 3);
        }
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — design v1.19.0 §2.11]</b> ESKİ İDDİA (v1.13.1): sürümler arasında 14px boşluk + 1px
    /// ayraç + 14px. v1.19.0: <b>22px + 1px <c>border-subtle</c> + 22px</b>; ilk sürümün üstünde ayraç yok.
    /// </summary>
    [StaFact]
    public void Versions_are_separated_by_22px_a_hairline_and_22px()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized(configure: d => d.Releases = NotesDialogHost.SyntheticReleases(3));
        using (scope)
        {
            var blocks = dialog.WhatsNewBlocks.Cast<Border>().ToList();
            Assert.Equal(3, blocks.Count);
            Assert.Equal(new Thickness(0), blocks[0].Margin);
            Assert.Equal(new Thickness(0), blocks[0].BorderThickness);
            Assert.All(blocks.Skip(1), b =>
            {
                Assert.Equal(new Thickness(0, 22, 0, 0), b.Margin);
                Assert.Equal(new Thickness(0, 1, 0, 0), b.BorderThickness);
                Assert.Equal(new Thickness(0, 22, 0, 0), b.Padding);
                Assert.Equal(DsResources.TokenColor(dialog, "Brush.BorderSubtle"), DsResources.ColorOf(b.BorderBrush));
            });
        }
    }

    /// <summary>[v1.19.0 §2.11] Sol kolon STICKY'dir: gövde kayarken bloğun üstüne yapışır (CSS
    /// <c>position: sticky; top: 0</c>). WPF'te sticky yoktur — karar saf <see cref="StickyColumn.Offset"/>'tedir
    /// (StickyColumnTests), burada GERÇEK gövdenin <c>ScrollChanged</c>'inin sol kolona o kararı yazdığı
    /// pinlenir.</summary>
    [StaFact]
    public void Scrolling_the_body_moves_the_left_column_by_the_sticky_offset()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized(configure: d => d.Releases = NotesDialogHost.SyntheticReleases(6));
        using (scope)
        {
            dialog.EarlierVersions.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); // uzun liste — gövde gerçekten kayar
            dialog.UpdateLayout();
            Assert.Equal(6, dialog.WhatsNewBlocks.Count);

            var scroller = DsResources.Descendants(dialog).OfType<ScrollViewer>().Single();
            var list = (FrameworkElement)scroller.Content;
            var grid = BlockGrid(dialog.WhatsNewBlocks[0]);
            var left = Column(grid, 0);
            Assert.True(scroller.ScrollableHeight > 60, $"gövde kaymıyor: {scroller.ScrollableHeight}");

            scroller.ScrollToVerticalOffset(40);
            dialog.UpdateLayout();
            DispatcherPump.PumpUntil(() => false, TimeSpan.FromMilliseconds(20));

            double expected = StickyColumn.Offset(scroller.VerticalOffset, TopIn(grid, list), grid.ActualHeight, left.ActualHeight);
            Assert.True(expected > 0, $"senaryo sticky'i tetiklemiyor: {expected}");
            var shift = Assert.IsType<TranslateTransform>(left.RenderTransform);
            Assert.Equal(expected, shift.Y, precision: 3);
        }
    }

    /// <summary>[§2.11] Son 3 sürüm açık, gerisi katlı; katlanacak bir şey yoksa düğme HİÇ çizilmez.
    /// (WhatsNewTests'ten TAŞINDI.)</summary>
    [StaFact]
    public void The_fold_button_only_appears_when_there_is_something_to_unfold()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            var expected = ReleaseNotes.All.Count > ReleaseNotes.OpenByDefault
                ? Visibility.Visible
                : Visibility.Collapsed;
            Assert.Equal(expected, dialog.EarlierVersionsFold.Visibility); // ayraç + buton tek blok (§2.11)
        }
    }

    /// <summary>[§2.11] "Earlier versions (N)" ghost butonu içerik koluna hizalanır: butonun stili
    /// (<c>Ds.Button.Ghost.Sm</c>, Controls.xaml) kendi yatay paddingini <c>10,0</c> verir; sol margin
    /// <b>-10</b> bunu iptal eder, böylece etiket madde metinleriyle tam sola hizalanır.</summary>
    [StaFact]
    public void The_earlier_versions_button_cancels_its_own_horizontal_padding_to_align_with_the_content_column()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
            Assert.Equal(-10.0, dialog.EarlierVersions.Margin.Left);
    }

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — design v1.19.0 §2.11]</b> ESKİ İDDİA (v1.13.1): katlı kısım listenin tam genişliğinde
    /// dururdu — 6px + 1px ayraç + 12px, ikonsuz buton. v1.19.0: bloklarla AYNI 84/26 grid'in SAĞ kolonunda,
    /// üstte 22px + 1px <c>border-subtle</c> + 14px; buton <c>Icon.Down</c> taşır.
    /// </summary>
    [StaFact]
    public void The_earlier_versions_fold_sits_in_the_right_column_of_the_block_grid_with_a_down_icon()
    {
        var releases = NotesDialogHost.SyntheticReleases(5);
        var (dialog, scope) = NotesDialogHost.OpenRealized(configure: d => d.Releases = releases);
        using (scope)
        {
            var fold = (Border)dialog.EarlierVersionsFold;
            Assert.Equal(Visibility.Visible, fold.Visibility);
            Assert.Equal(new Thickness(0, 22, 0, 0), fold.Margin);
            Assert.Equal(new Thickness(0, 1, 0, 0), fold.BorderThickness);
            Assert.Equal(new Thickness(0, 14, 0, 0), fold.Padding);

            var grid = (Grid)fold.Child;
            Assert.Equal(new GridLength(84), grid.ColumnDefinitions[0].Width);
            Assert.Equal(new GridLength(26), grid.ColumnDefinitions[1].Width);
            Assert.Equal(2, Grid.GetColumn(dialog.EarlierVersions));

            var icon = DsResources.Descendants(dialog.EarlierVersions).OfType<System.Windows.Shapes.Path>().Single();
            Assert.Same(dialog.FindResource("Icon.Down"), icon.Data);
            Assert.Contains(ReleaseNotes.EarlierVersionsLabel(releases.Count - ReleaseNotes.OpenByDefault),
                DsResources.Descendants(dialog.EarlierVersions).OfType<TextBlock>().Select(t => t.Text));
        }
    }

    // ---------------------------------------------------------------- footer

    /// <summary>[§2.11] Footer yalnız sağda secondary Close taşır — Copy diagnostics About'ta kalır, buraya
    /// gelmez. "Yalnız" iddiası footer'ın KENDİ çocuk sayısından okunur: Close'un bulunduğunu ve About'un
    /// metninin bulunmadığını ölçmek, footer'a üçüncü bir düğme eklenmesini yakalamazdı.</summary>
    [StaFact]
    public void The_footer_has_only_a_close_button()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            var close = DsResources.Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "Close"));
            var footer = (DockPanel)VisualTreeHelper.GetParent(close);

            Assert.Same(close, Assert.Single(footer.Children.Cast<UIElement>()));
            Assert.DoesNotContain(VisibleTexts(dialog), t => t.Contains("Copy diagnostics", StringComparison.Ordinal));
        }
    }
}
