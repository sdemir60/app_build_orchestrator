using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.13.0/v1.13.1 §2.11 · D4/T9] What's new — kendi diyalogu. Kabuk (scrim + Ds.Dialog + odak
/// tuzağı + Esc/scrim) About'la BİREBİR aynıdır, bu yüzden o testlerin kabuk kısmı burada TEKRARLANIR
/// (kopya değil — AYRI bir XAML kökü, AYRI bir gerçeklik; her ikisi de kendi realize kanıtını taşımalı,
/// CLAUDE.md realize kuralı). İçerik testleri ise <c>AboutDialogTests</c>/<c>WhatsNewTests</c>'in eski
/// <c>dialog.WhatsNew.IsChecked = true</c> sekme-testlerinin YERİNİ alır (About'un dördüncü sekmesi
/// KALKTI — bkz. AboutDialogTests, WhatsNewTests).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class NotesDialogTests
{
    private static Border Shell(BuildOrchestrator.App.Views.NotesDialog dialog) =>
        (Border)VisualTreeHelper.GetChild(dialog.Scrim, 0);

    private static List<string> VisibleTexts(FrameworkElement dialog) =>
        [.. DsResources.Descendants(dialog).OfType<TextBlock>().Select(t => t.Text)];

    // ---------------------------------------------------------------- kabuk

    [StaFact]
    public void The_dialog_realizes_and_is_six_hundred_twenty_pixels_wide()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            Assert.Equal(Visibility.Visible, dialog.Visibility);
            Assert.Equal(620.0, Shell(dialog).Width);
            Assert.Equal(620.0, Shell(dialog).ActualWidth); // realize zorunlu — literal okumak yetmez
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

    [StaFact]
    public void The_header_shows_the_title_and_subtitle()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            var texts = VisibleTexts(dialog);
            Assert.Contains("What's new", texts);
            Assert.Contains("Release notes for Build Orchestrator.", texts);
        }
    }

    /// <summary>Sürüm numarası LİTERAL DEĞİLDİR — <see cref="AppIdentity.Version"/>'dan gelir (kopya YASAK).
    /// Başlıktaki "INSTALLED VERSION" caps etiketi bir <see cref="TrackedTextBlock"/>'tur (harf aralıklı) —
    /// TextBlock DEĞİL, ayrı aranır.</summary>
    [StaFact]
    public void The_header_version_block_reads_the_installed_version_from_app_identity()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            Assert.Contains(AppIdentity.Version, VisibleTexts(dialog));
            var caption = DsResources.Descendants(dialog).OfType<TrackedTextBlock>()
                .Single(t => t.Text.Equals("INSTALLED VERSION", StringComparison.Ordinal));
            Assert.NotNull(caption);
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

    /// <summary>[§2.11] Gövde yüksekliği SABİT 400px (MIN değil) + dikey scroll — Earlier versions açıldığında
    /// dialog uzayıp ekranın altına/üstüne yapışmaz, liste kendi içinde kayar.</summary>
    [StaFact]
    public void The_body_height_is_fixed_at_400px()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            // Height==400.0 arayışı MinHeight'tan AYRIŞTIRIR: XAML MinHeight kullansaydı Height NaN kalır ve
            // Single() eşleşmezdi (About'un Grid.Height==236 testiyle AYNI desen).
            var body = DsResources.Descendants(dialog).OfType<Grid>().Single(g => g.Height == 400.0);
            Assert.True(body.ActualHeight > 0, "gövde hiç yerleşmedi");
            Assert.Equal(400.0, body.ActualHeight);

            var scroller = DsResources.Descendants(dialog).OfType<ScrollViewer>().Single();
            Assert.Equal(ScrollBarVisibility.Auto, scroller.VerticalScrollBarVisibility);
        }
    }

    // ---------------------------------------------------------------- liste (v1.9.0'dan DEĞİŞMEYEN kurallar)

    /// <summary>[§2.11] Sekme, sürüm başına bir blok çizer; blokta mono sürüm numarası ve (kuruluysa) sağa
    /// yaslı tarih durur. Liste kuralı v1.9.0'dan DEĞİŞMEDİ — yalnız ev artık NotesDialog'tur.</summary>
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
    /// <b>[DEĞİŞEN KURAL — design v1.13.1 §2.11]</b> ESKİ İDDİA (design v1.1.0/v1.9.0, <c>WhatsNewTests</c>'te
    /// pinliydi): güncel sürümde sessiz bir <c>CURRENT</c> metni dururdu (zemin/çerçeve yok). Ölçüm sonrası
    /// (amber rozet VE çerçevesiz caps metin denendi) nötr bir ÇİP seçildi: 17px yüksek, yatay padding 6px,
    /// <c>surface-raised</c> zemin + 1px <c>border-strong</c>, <c>radius-xs</c>, 10px caps <c>text-dim</c>
    /// metin — <c>CURRENT</c> değil <c>INSTALLED</c> yazar (başlık satırındaki "INSTALLED VERSION" bloğuyla
    /// aynı sözcük). Bu test YENİ kuralı pinler; eski "CURRENT" iddiası bir daha üretilmez.
    /// </summary>
    [StaFact]
    public void The_installed_entry_shows_a_neutral_installed_chip_not_a_current_label()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            var chipLabel = DsResources.Descendants(dialog).OfType<TrackedTextBlock>()
                .Single(t => t.Text.Equals("INSTALLED", StringComparison.Ordinal));
            var chip = (Border)VisualTreeHelper.GetParent(chipLabel);

            Assert.Equal(17.0, chip.Height);
            Assert.Equal(new Thickness(6, 0, 6, 0), chip.Padding);
            Assert.Equal(new Thickness(1), chip.BorderThickness);
            Assert.Equal(DsResources.TokenColor(dialog, "Brush.SurfaceRaised"), DsResources.ColorOf(chip.Background));
            Assert.Equal(DsResources.TokenColor(dialog, "Brush.BorderStrong"), DsResources.ColorOf(chip.BorderBrush));
            Assert.Equal((CornerRadius)dialog.FindResource("Radius.Xs"), chip.CornerRadius);
            Assert.Equal(DsResources.TokenColor(dialog, "Brush.TextDim"), DsResources.ColorOf(chipLabel.Foreground));

            // Eski metin bir daha ÜRETİLMEZ.
            Assert.DoesNotContain(VisibleTexts(dialog), t => t == "CURRENT");
            Assert.DoesNotContain(DsResources.Descendants(dialog).OfType<TrackedTextBlock>(),
                t => t.Text == "CURRENT");
        }
    }

    /// <summary>[§2.11] Kategori BLOK başlığıdır: 6px renkli kare + caps etiket, sabit sırada. Maddelerin
    /// başında ikon/sigil YOKTUR. (WhatsNewTests'ten TAŞINDI — About'un dördüncü sekmesi kalktı.)</summary>
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
                .Select(t => t.Text).Where(t => t != "INSTALLED").ToList();
            Assert.Equal(ReleaseNotes.KindOrder.Where(kinds.Contains).Select(ReleaseNotes.Label), headings);

            Assert.Empty(DsResources.Descendants(first).OfType<System.Windows.Shapes.Path>());
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
            Assert.Equal(expected, dialog.EarlierVersions.Visibility);
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

    // ---------------------------------------------------------------- footer

    /// <summary>[§2.11] Footer yalnız sağda secondary Close taşır — Copy diagnostics About'ta kalır, buraya
    /// gelmez.</summary>
    [StaFact]
    public void The_footer_has_only_a_close_button()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            var buttons = DsResources.Descendants(dialog).OfType<Button>()
                .Where(b => b.Visibility == Visibility.Visible)
                .ToList();
            Assert.Contains(buttons, b => Equals(b.Content, "Close"));
            Assert.DoesNotContain(VisibleTexts(dialog), t => t.Contains("Copy diagnostics", StringComparison.Ordinal));
        }
    }
}
