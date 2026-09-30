using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Services.Updates;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>[motor · Task 8 · K5] Feed kaydından karta: boyut MB tek ondalık; öne çıkanlar CHANGELOG bölümünün
/// kendisidir (mevcut parser), KindOrder sırasıyla en çok 5 madde, kalanı sayı olarak (kart "+N more" yazar).
/// <para><b>Değişen kural:</b> eski <c>Sample</c>/<c>NextMinor</c> (motor yokken hapı hep gösteren placeholder) kalktı;
/// teklif artık yalnız gerçek bir feed kaydından üretilir. Örnek teklif testlerin fixture'ında (<c>UpdateOffers</c>).</para></summary>
public class UpdateOfferTests
{
    /// <summary>Kategoriler bilerek <see cref="ReleaseNotes.KindOrder"/> sırasında DEĞİL yazılır (<c>Fixed</c> en başta): feed
    /// sırası kartın sırası olsaydı öne çıkanlar <c>D E F A B</c> çıkardı. Fixture eskiden zaten KindOrder sırasındaydı ve
    /// <see cref="UpdateOffer.SelectHighlights"/>'taki sıralama kaldırılınca da testler yeşil kalıyordu (ölçüldü).</summary>
    private const string Notes = "## [1.8.0] - 2026-10-01\n\n### Fixed\n\n- D\n- E\n- F\n\n### Added\n\n- A\n- B\n\n### Changed\n\n- C\n\n### Performance\n\n- G\n";

    [Theory]
    [InlineData(UpdateOffers.SampleBytes, UpdateOffers.SampleSize)]
    [InlineData(1_048_576, "1.0 MB")]
    [InlineData(512_000, "0.5 MB")]
    public void The_size_is_megabytes_with_one_decimal(long bytes, string expected) => Assert.Equal(expected, UpdateOffer.FormatSize(bytes));

    [Fact]
    public void Highlights_are_the_feed_notes_in_category_order_capped_at_five_with_the_rest_counted()
    {
        var offer = UpdateOffer.From(new UpdateCandidate("1.8.0", UpdateOffers.SampleBytes, Notes));
        Assert.Equal("1.8.0", offer.Version);
        Assert.Equal(UpdateOffers.SampleSize, offer.Size);
        Assert.Equal(UpdateOffer.MaxHighlights, offer.Highlights.Count);
        Assert.Equal(["A", "B", "C", "D", "E"], offer.Highlights.Select(n => n.Text));
        Assert.Equal(2, offer.MoreCount);
    }

    [Fact]
    public void A_short_feed_shows_everything_and_counts_nothing()
    {
        var offer = UpdateOffer.From(UpdateOffers.Candidate("1.8.0"));
        Assert.Single(offer.Highlights);
        Assert.Equal(0, offer.MoreCount);
    }

    /// <summary>Feed notu bozuksa (yayın script'i dışından üretilmiş bir paket) kart düşmez: hap yine çıkar, öne
    /// çıkanlar boş. Girdiler parser'ın GERÇEKTEN reddettiği notlardır — ön-koşul <c>Assert.Throws</c> bunu pinler;
    /// parser bir gün bu girdileri kabul etmeye başlarsa test <c>ParseNotes</c>'un <c>catch</c>'ini koruduğunu sessizce
    /// iddia etmeye devam etmez, önce ön-koşulda düşer. İlk girdi GitHub'ın otomatik yayın notunun biçimidir.</summary>
    [Theory]
    [InlineData("## What's Changed\n* x\n")]
    [InlineData("## [1.8.0] - 2026-10-01\nstray text\n")]
    public void Unparsable_notes_yield_an_offer_without_highlights(string notes)
    {
        Assert.Throws<FormatException>(() => ReleaseNotes.Parse(notes));
        var offer = UpdateOffer.From(new UpdateCandidate("1.8.0", 1, notes));
        Assert.Equal("1.8.0", offer.Version);
        Assert.Empty(offer.Highlights);
        Assert.Equal(0, offer.MoreCount);
    }

    /// <summary>Boş not ya da hiç sürüm başlığı taşımayan metin parser'a hata değildir (ilk sürümden önceki her şey
    /// veri sayılmaz) — <c>catch</c>'e ulaşmadan boş öne çıkanlar verir.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("not a changelog")]
    public void Notes_without_a_version_section_yield_an_offer_without_highlights(string notes)
    {
        var offer = UpdateOffer.From(new UpdateCandidate("1.8.0", 1, notes));
        Assert.Empty(offer.Highlights);
        Assert.Equal(0, offer.MoreCount);
    }

    [Fact]
    public void The_more_line_counts_what_the_card_leaves_out()
    {
        Assert.Equal("+3 more in What's new after restart", UpdateText.MoreHighlights(3));
        Assert.Equal("+1 more in What's new after restart", UpdateText.MoreHighlights(1));
    }
}
