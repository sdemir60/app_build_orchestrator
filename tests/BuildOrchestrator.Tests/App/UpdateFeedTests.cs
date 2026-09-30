using BuildOrchestrator.App.Services.Updates;
using Velopack.Sources;

namespace BuildOrchestrator.Tests.App;

/// <summary>[motor · Task 7] Güncelleme kaynağı TEK sabittir (GitHub Releases); dev/test kapıları mevcut BO_* deseniyle
/// ortam değişkeninden gelir: BO_UPDATE_SOURCE bir klasör (yerel feed — GitHub'sız uçtan uca prova) ya da URL,
/// BO_UPDATE_PRERELEASE=1 prerelease'leri de görür (aşamalı yayın). UI'da ayar yoktur (tasarım §2.12).</summary>
public class UpdateFeedTests
{
    [Fact]
    public void Without_an_override_the_feed_is_the_GitHub_repository()
    {
        var source = UpdateFeed.CreateSource(null, prerelease: false);
        var github = Assert.IsType<GithubSource>(source);
        Assert.Equal(UpdateFeed.RepositoryUrl, github.RepoUri.ToString().TrimEnd('/'));
        Assert.False(github.Prerelease);
        Assert.True(Assert.IsType<GithubSource>(UpdateFeed.CreateSource("", prerelease: true)).Prerelease);
    }

    [Fact]
    public void A_folder_override_becomes_a_local_file_feed()
    {
        using var temp = new TempDir();
        Assert.IsType<SimpleFileSource>(UpdateFeed.CreateSource(temp.Path, prerelease: false));
    }

    [Fact]
    public void A_web_override_becomes_a_web_feed_and_a_GitHub_url_stays_a_GitHub_feed()
    {
        Assert.IsType<SimpleWebSource>(UpdateFeed.CreateSource("https://updates.example.com/bo/", prerelease: false));
        Assert.IsType<GithubSource>(UpdateFeed.CreateSource("https://github.com/someone/fork", prerelease: false));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("true", false)]
    public void The_prerelease_flag_is_exactly_one(string? flag, bool expected) => Assert.Equal(expected, UpdateFeed.IsEnabled(flag));
}
