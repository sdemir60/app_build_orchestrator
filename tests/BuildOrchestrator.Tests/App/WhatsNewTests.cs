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

    /// <summary>Liste en yeni SÜRÜM ÜSTTE olacak şekilde durur ve çalışan sürümün girdisi vardır — <c>INSTALLED</c>
    /// çipi ona konur. Gömülü <c>CHANGELOG.md</c>'nin en üst sürümü <c>Directory.Build.props</c>'taki sürümdür:
    /// sürüm numarası notu yazılmadan artırılamaz.</summary>
    [Fact]
    public void The_running_version_has_an_entry_so_it_can_be_marked_current()
    {
        Assert.NotEmpty(ReleaseNotes.All);
        Assert.NotNull(ReleaseNotes.Current);
        Assert.Equal(AppIdentity.Version, ReleaseNotes.All[0].Version);
        Assert.All(ReleaseNotes.All, e => Assert.NotEmpty(e.Notes));
    }

    // ---------------------------------------------------------------- CHANGELOG.md okuyucusu (saf)

    /// <summary>[sürüm notları] Okuyucu sürümleri dosyadaki sırayla, numara ve tarihiyle verir; maddeler
    /// dosyadaki sırayla ve bulundukları kategoriyle gelir. Başlık ve ilk sürümden önceki giriş metni veri
    /// değildir.</summary>
    [Fact]
    public void The_changelog_reader_returns_versions_and_their_notes_in_file_order()
    {
        const string markdown =
            "# Changelog\r\n" +
            "\r\n" +
            "Intro text that is not a note.\r\n" +
            "\r\n" +
            "## [1.1.0] - 2026-02-03\r\n" +
            "\r\n" +
            "### Added\r\n" +
            "\r\n" +
            "- First added.\r\n" +
            "- Second added.\r\n" +
            "\r\n" +
            "### Fixed\r\n" +
            "\r\n" +
            "- One fix.\r\n" +
            "\r\n" +
            "## [1.0.0] - 2026-01-02\r\n" +
            "\r\n" +
            "### Removed\r\n" +
            "\r\n" +
            "- Something old.\r\n";

        var entries = ReleaseNotes.Parse(markdown);

        Assert.Equal(["1.1.0", "1.0.0"], entries.Select(e => e.Version));
        Assert.Equal(["2026-02-03", "2026-01-02"], entries.Select(e => e.Date));
        Assert.Equal(
            [new ReleaseNote(NoteKind.Added, "First added."), new ReleaseNote(NoteKind.Added, "Second added."),
             new ReleaseNote(NoteKind.Fixed, "One fix.")],
            entries[0].Notes);
        Assert.Equal([new ReleaseNote(NoteKind.Removed, "Something old.")], entries[1].Notes);
    }

    /// <summary>Uzun bir madde dosyada girintili devam satırlarıyla sarılabilir — ekranda tek madde olarak,
    /// satırlar tek boşlukla birleşmiş okunur.</summary>
    [Fact]
    public void A_wrapped_note_is_joined_into_one_line()
    {
        const string markdown =
            "## [1.0.0] - 2026-01-02\n" +
            "### Changed\n" +
            "- A long note that\n" +
            "  continues here.\n";

        var note = Assert.Single(ReleaseNotes.Parse(markdown)[0].Notes);
        Assert.Equal("A long note that continues here.", note.Text);
    }

    /// <summary>Ekranda görünmeyecek ya da yanlış görünecek her biçim hatası sessizce atlanmaz, satır
    /// numarasıyla reddedilir: bilinmeyen kategori, kategorisiz madde, bozuk sürüm başlığı, sürümün içinde
    /// serbest metin ve maddesi olmayan sürüm.</summary>
    [Theory]
    [InlineData("## [1.0.0] - 2026-01-02\n### Security\n- x\n", 2)]
    [InlineData("## [1.0.0] - 2026-01-02\n- x\n", 2)]
    [InlineData("## 1.0.0 - 2026-01-02\n### Added\n- x\n", 1)]
    [InlineData("## [1.0] - 2026-01-02\n### Added\n- x\n", 1)]
    [InlineData("## [1.0.0] - 2026-1-2\n### Added\n- x\n", 1)]
    [InlineData("## [1.0.0] - 2026-01-02\n### Added\n- x\nstray prose\n", 4)]
    [InlineData("## [1.1.0] - 2026-01-03\n### Added\n\n## [1.0.0] - 2026-01-02\n### Added\n- x\n", 1)]
    public void A_malformed_changelog_is_rejected_with_its_line_number(string markdown, int line)
    {
        var error = Assert.Throws<FormatException>(() => ReleaseNotes.Parse(markdown));
        Assert.Contains($"line {line}:", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Katlı kısmın etiketi kaç sürümün gizlendiğini söyler.</summary>
    [Fact]
    public void The_fold_button_counts_what_it_hides()
    {
        Assert.Equal(3, ReleaseNotes.OpenByDefault);
        Assert.Equal("Earlier versions (4)", ReleaseNotes.EarlierVersionsLabel(4));
    }

    /// <summary>[design v1.19.0 §2.10/§2.11] "What's new in {sürüm}" cümlesinin TEK yeri: sparkle butonunun
    /// görülmemiş-sürüm tooltip'i (MainWindow) ve About sekmesinin What's new butonu AYNI yardımcıdan okur.
    /// Kaynak guard'ı: cümle kalıbı başka hiçbir üretim dosyasında literal olarak geçmez.</summary>
    [Fact]
    public void The_whats_new_in_sentence_has_a_single_source()
    {
        Assert.Equal("What's new in 1.2.3", ReleaseNotes.WhatsNewInLabel("1.2.3"));

        var offenders = SourceGuard.ScanApp("*.cs", new System.Text.RegularExpressions.Regex("\"What's new in "),
            allowedFiles: [System.IO.Path.Combine("Services", "ReleaseNotes.cs")], skipCommentLines: true)
            .Concat(SourceGuard.ScanApp("*.xaml", new System.Text.RegularExpressions.Regex("What's new in "),
                skipCommentLines: true));
        Assert.Empty(offenders);
    }
}
