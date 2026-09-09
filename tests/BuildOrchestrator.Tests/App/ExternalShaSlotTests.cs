using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Satırın sha yuvası harici projeleri de taşır. İki kural buradan doğar: kısaltma YALNIZ gerçek bir git
/// sha'sına uygulanır (TFVC changeset'i <c>C48213</c> kırpılırsa anlamsızlaşır) ve hedef yarısı yokken
/// yarım bir ok basılmaz.
/// </summary>
public class ExternalShaSlotTests
{
    private const string Sha = "1234567890abcdef1234567890abcdef12345678";
    private const string Target = "abcdefa234567890abcdef1234567890abcdef12";

    [Fact]
    public void A_git_sha_is_shortened_to_seven()
        => Assert.Equal("1234567", RunViewModel.ShortSha(Sha));

    [Theory]
    [InlineData("C48213")]
    [InlineData("C123456789")]      // uzun changeset de kırpılmaz — 40-hex değil
    [InlineData("not-a-sha")]
    public void Anything_that_is_not_a_git_sha_is_shown_verbatim(string revision)
        => Assert.Equal(revision, RunViewModel.ShortSha(revision));

    [Fact]
    public void A_row_with_both_halves_shows_the_pair()
        => Assert.Equal("1234567 → abcdefa", ProjectRow.ShaSlotText(Sha, Target, dirty: true));

    [Fact]
    public void A_row_without_a_target_shows_only_what_it_has()
    {
        // Harici satırda ana reponun hedef sha'sı YOKTUR — yarım bir ok ("C48213 → ") pürüzdür.
        Assert.Equal("C48213", ProjectRow.ShaSlotText("C48213", null, dirty: true));
        Assert.Equal("C48213", ProjectRow.ShaSlotText("C48213", "", dirty: true));
    }

    [Fact]
    public void A_row_that_will_not_build_shows_the_single_target()
        => Assert.Equal("1234567", ProjectRow.ShaSlotText(Target, Sha, dirty: false));

    [Fact]
    public void A_row_that_was_never_built_shows_only_the_target()
        => Assert.Equal("1234567", ProjectRow.ShaSlotText(null, Sha, dirty: true));

    [Fact]
    public void An_up_to_date_external_shows_its_own_revision()
    {
        // Harici, güncelken de sha'sını gösterir: hedef yarısı olmadığından tek değer basılır.
        Assert.Equal("C48213", ProjectRow.ShaSlotText("C48213", null, dirty: false));
    }
}
