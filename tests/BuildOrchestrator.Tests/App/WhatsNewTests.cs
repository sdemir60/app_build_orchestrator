using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.9.0 §2.10] Sürüm notlarının SAF verisi/kuralları: sabit kategori sırası + rengi, çalışan
/// sürümün girdisi, "son 3 açık" katlama sayısı. Sürüm notları ayrı bir pencere ya da açılış pop-up'ı
/// DEĞİLDİR — sürüm numarasının zaten göründüğü yerde yaşarlar (§8: "sürüm notları için açılış pop-up'ı
/// yok").
///
/// <para>Kategori satır başına DEĞİL <b>BLOĞA</b> yazılır (Keep a Changelog mantığı): 6px renkli kare + caps
/// başlık, maddeler altında işaretsiz durur. Satır başına ikon ve diff sigili varyantları denendi, blok
/// başlığı seçildi.</para>
///
/// <para><b>[DEĞİŞEN KURAL — design v1.13.0 §2.10/§2.11, D4/T9]</b> ESKİ İDDİA (design v1.9.0): bu dosya
/// hem SAF veri testlerini hem de About'un dördüncü sekmesinin (What's new) UI testlerini taşıyordu. v1.13.0
/// What's new'i About'tan çıkarıp kendi diyaloguna (<see cref="BuildOrchestrator.App.Views.NotesDialog"/>)
/// taşıdığı için UI testleri (bir sürüm bloğu çizimi, kategori BLOK başlığı, katlama düğmesinin görünürlüğü)
/// KOPYALANMADI, <c>NotesDialogTests</c>'e TAŞINDI. Burada yalnız <see cref="ReleaseNotes"/>'un kendi SAF
/// karar yüzeyi (hangi UI onu tüketirse tüketsin geçerli kalan kurallar) kalır.</para>
/// </summary>
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
}
