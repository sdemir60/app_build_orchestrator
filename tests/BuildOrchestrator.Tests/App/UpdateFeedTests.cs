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

    /// <summary>[final review #8] Bozuk bir <c>BO_UPDATE_SOURCE</c> uygulamayı DÜŞÜRMEZ: kaynak <c>App.xaml.cs</c>'te
    /// <c>UpdateService</c>'in çözümlenmesi sırasında, pencere görününce kurulur ve orada yakalayan yoktur. Geçersiz yol
    /// karakteri ya da çözümlenemeyen URL içeren değer yok sayılır ve GitHub kaynağına dönülür — sessiz geri dönüş
    /// BİLİNÇLİDİR: kapı yalnız geliştirme/test içindir, kullanıcıya görünen bir ayarı yoktur ve kırık bir env değeri yüzünden
    /// açılmayan bir uygulama, yanlış kaynaktan güncelleme almaktan kötüdür. Var olmayan ama geçerli bir klasör yok
    /// SAYILMAZ (yerel feed provası, klasör henüz yoksa da o klasörü ister): o durumda kontrol sessizce sonuçsuz kalır.
    /// Ön sürüm kapısı geri dönüşte de geçerlidir.</summary>
    [Theory]
    [InlineData("C:\\bad|path")]
    [InlineData("C:\\bad<path>")]
    [InlineData("C:\\bad\"path")]
    [InlineData("C:\\bad*path")]
    [InlineData("C:\\bad\tpath")]
    [InlineData("C:\\bad\0path")]
    [InlineData("http://")]
    public void A_malformed_override_is_ignored_and_the_GitHub_feed_is_used(string malformed)
    {
        var github = Assert.IsType<GithubSource>(UpdateFeed.CreateSource(malformed, prerelease: false));
        Assert.Equal(UpdateFeed.RepositoryUrl, github.RepoUri.ToString().TrimEnd('/'));
        Assert.True(Assert.IsType<GithubSource>(UpdateFeed.CreateSource(malformed, prerelease: true)).Prerelease);
    }

    [Fact]
    public void A_well_formed_folder_that_does_not_exist_yet_is_still_the_feed()
    {
        Assert.IsType<SimpleFileSource>(UpdateFeed.CreateSource(@"C:\no-such-feed-folder\artifacts\velopack", prerelease: false));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("true", false)]
    public void The_prerelease_flag_is_exactly_one(string? flag, bool expected) => Assert.Equal(expected, UpdateFeed.IsEnabled(flag));
}
