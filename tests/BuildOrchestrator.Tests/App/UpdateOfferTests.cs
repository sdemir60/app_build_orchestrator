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
    private const string Notes = "## [1.8.0] - 2026-10-01\n\n### Added\n\n- A\n- B\n\n### Changed\n\n- C\n\n### Fixed\n\n- D\n- E\n- F\n\n### Performance\n\n- G\n";

    [Theory]
    [InlineData(19_293_798, "18.4 MB")]
    [InlineData(1_048_576, "1.0 MB")]
    [InlineData(512_000, "0.5 MB")]
    public void The_size_is_megabytes_with_one_decimal(long bytes, string expected) => Assert.Equal(expected, UpdateOffer.FormatSize(bytes));

    [Fact]
    public void Highlights_are_the_feed_notes_in_category_order_capped_at_five_with_the_rest_counted()
    {
        var offer = UpdateOffer.From(new UpdateCandidate("1.8.0", 19_293_798, Notes));
        Assert.Equal("1.8.0", offer.Version);
        Assert.Equal("18.4 MB", offer.Size);
        Assert.Equal(UpdateOffer.MaxHighlights, offer.Highlights.Count);
        Assert.Equal(["A", "B", "C", "D", "E"], offer.Highlights.Select(n => n.Text));
        Assert.Equal(2, offer.MoreCount);
    }

    [Fact]
    public void A_short_feed_shows_everything_and_counts_nothing()
    {
        var offer = UpdateOffer.From(new UpdateCandidate("1.8.0", 1, "## [1.8.0] - 2026-10-01\n### Fixed\n- D\n"));
        Assert.Single(offer.Highlights);
        Assert.Equal(0, offer.MoreCount);
    }

    [Fact]
    public void Unparsable_notes_yield_an_offer_without_highlights()
    {
        // Feed notu bozuksa (yayın script'i dışından üretilmiş bir paket) kart düşmez: hap yine çıkar, öne çıkanlar boş.
        var offer = UpdateOffer.From(new UpdateCandidate("1.8.0", 1, "not a changelog"));
        Assert.Empty(offer.Highlights);
        Assert.Equal(0, offer.MoreCount);
        Assert.Empty(UpdateOffer.From(new UpdateCandidate("1.8.0", 1, "")).Highlights);
    }

    [Fact]
    public void The_more_line_counts_what_the_card_leaves_out()
    {
        Assert.Equal("+3 more in What's new after restart", UpdateText.MoreHighlights(3));
        Assert.Equal("+1 more in What's new after restart", UpdateText.MoreHighlights(1));
    }
}
