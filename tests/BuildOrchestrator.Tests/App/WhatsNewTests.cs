using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.9.0 §2.10] About'un dördüncü sekmesi: <b>What's new</b>. Sürüm notları ayrı bir pencere ya da
/// açılış pop-up'ı DEĞİLDİR — sürüm numarasının zaten göründüğü yerde yaşarlar (§8: "sürüm notları için
/// açılış pop-up'ı yok").
///
/// <para>Kategori satır başına DEĞİL <b>BLOĞA</b> yazılır (Keep a Changelog mantığı): 6px renkli kare + caps
/// başlık, maddeler altında işaretsiz durur. Satır başına ikon ve diff sigili varyantları denendi, blok
/// başlığı seçildi.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class WhatsNewTests
{
    // ---------------------------------------------------------------- veri (saf)

    /// <summary>Kategoriler SABİT sırayla çizilir ve her birinin kendi rengi vardır (§2.10).</summary>
    [Fact]
    public void The_categories_have_a_fixed_order_and_their_own_colours()
    {
        Assert.Equal(
            [NoteKind.Added, NoteKind.Changed, NoteKind.Fixed, NoteKind.Performance, NoteKind.Removed],
            ReleaseNotes.KindOrder);

        Assert.Equal("Brush.Amber", ReleaseNotes.SwatchBrushKey(NoteKind.Added));
        Assert.Equal("Brush.TextDim", ReleaseNotes.SwatchBrushKey(NoteKind.Changed));
        Assert.Equal("Brush.StatusSuccess", ReleaseNotes.SwatchBrushKey(NoteKind.Fixed));
        Assert.Equal("Brush.StatusCycle", ReleaseNotes.SwatchBrushKey(NoteKind.Performance));
        Assert.Equal("Brush.StatusFail", ReleaseNotes.SwatchBrushKey(NoteKind.Removed));
    }

    /// <summary>Liste en yeni SÜRÜM ÜSTTE olacak şekilde durur ve çalışan sürümün girdisi vardır — <c>CURRENT</c>
    /// etiketi ona konur.</summary>
    [Fact]
    public void The_running_version_has_an_entry_so_it_can_be_marked_current()
    {
        Assert.NotEmpty(ReleaseNotes.All);
        Assert.NotNull(ReleaseNotes.Current);
        Assert.Equal(AppIdentity.Version, ReleaseNotes.All[0].Version);
        Assert.All(ReleaseNotes.All, e => Assert.NotEmpty(e.Notes));
    }

    /// <summary>Katlı kısmın etiketi kaç sürümün gizlendiğini söyler.</summary>
    [Fact]
    public void The_fold_button_counts_what_it_hides()
    {
        Assert.Equal(3, ReleaseNotes.OpenByDefault);
        Assert.Equal("Earlier versions (4)", ReleaseNotes.EarlierVersionsLabel(4));
    }

    // ---------------------------------------------------------------- görünüm

    /// <summary>[§2.10] Sekme, sürüm başına bir blok çizer; blokta mono sürüm numarası, sessiz <c>CURRENT</c>
    /// etiketi ve tarih durur.</summary>
    [StaFact]
    public void The_tab_draws_one_block_per_version_with_a_quiet_current_label()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            dialog.WhatsNew.IsChecked = true;
            dialog.UpdateLayout();

            var blocks = dialog.WhatsNewBlocks;
            Assert.Equal(Math.Min(ReleaseNotes.OpenByDefault, ReleaseNotes.All.Count), blocks.Count);

            var texts = DsResources.Descendants(blocks[0]).OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains(AppIdentity.Version, texts);
            Assert.Contains(ReleaseNotes.All[0].Date, texts);
            // CURRENT bir TrackedTextBlock'tur (harf aralıklı caps) — o bir TextBlock DEĞİL, kendi
            // FrameworkElement'idir; ayrı aranır. Etiket SESSİZDİR: zemin/çerçeve yok, yalnız text-faint metin.
            var current = DsResources.Descendants(blocks[0]).OfType<TrackedTextBlock>()
                .Single(t => t.Text == "CURRENT");
            Assert.Same(dialog.FindResource("Brush.TextFaint"), current.Foreground);
        }
    }

    /// <summary>[§2.10] Kategori BLOK başlığıdır: 6px renkli kare + caps etiket. Maddelerin başında ikon ya da
    /// sigil YOKTUR — o varyantlar denendi ve istenmedi.</summary>
    [StaFact]
    public void Each_category_is_a_block_heading_with_a_six_pixel_swatch_and_no_per_line_sigils()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            dialog.WhatsNew.IsChecked = true;
            dialog.UpdateLayout();

            var first = dialog.WhatsNewBlocks[0];
            var swatches = DsResources.Descendants(first).OfType<Rectangle>().ToList();
            var kinds = ReleaseNotes.All[0].Notes.Select(n => n.Kind).Distinct().ToList();
            Assert.Equal(kinds.Count, swatches.Count);            // kategori başına TEK kare
            Assert.All(swatches, r => Assert.Equal(6.0, r.Width));
            Assert.All(swatches, r => Assert.Equal(6.0, r.Height));

            // Başlıklar SABİT sırada ve caps.
            var headings = DsResources.Descendants(first).OfType<TrackedTextBlock>()
                .Select(t => t.Text).Where(t => t != "CURRENT").ToList();
            Assert.Equal(ReleaseNotes.KindOrder.Where(kinds.Contains).Select(ReleaseNotes.Label), headings);

            // Madde satırlarında ikon/sigil YOK: bloktaki tek çizim kategori kareleridir.
            Assert.Empty(DsResources.Descendants(first).OfType<System.Windows.Shapes.Path>());
        }
    }

    /// <summary>[§2.10] Son 3 sürüm açık, gerisi katlı; katlanacak bir şey yoksa düğme HİÇ çizilmez.</summary>
    [StaFact]
    public void The_fold_button_only_appears_when_there_is_something_to_unfold()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            dialog.WhatsNew.IsChecked = true;
            dialog.UpdateLayout();

            var expected = ReleaseNotes.All.Count > ReleaseNotes.OpenByDefault
                ? Visibility.Visible
                : Visibility.Collapsed;
            Assert.Equal(expected, dialog.EarlierVersions.Visibility);
        }
    }
}
