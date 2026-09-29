using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.23.0 §2.12 · plan U1] Güncelleme teklifinin verisi. Güncelleme motoru HENÜZ YOK: hap şimdilik her zaman
/// görünür ve kartın içeriği prototipteki örnek kayıttır (<c>UPDATE_FEED</c>, BuildApp.jsx:1649-1658). Gelen sürüm
/// elle yazılmaz — kurulu sürümün (<see cref="AppIdentity.Version"/>) bir sonraki minor'ıdır; CHANGELOG ve
/// <c>Directory.Build.props</c> bu yüzden değişmez.
/// </summary>
public class UpdateOfferTests
{
    /// <summary>Prototipin <c>nextMinor</c>'u (BuildApp.jsx:1649): minor bir artar, patch sıfırlanır; eksik parça 0
    /// sayılır.</summary>
    [Theory]
    [InlineData("1.7.0", "1.8.0")]
    [InlineData("1.24.0", "1.25.0")]
    [InlineData("2.9.3", "2.10.0")]
    [InlineData("1.7", "1.8.0")]
    [InlineData("3", "3.1.0")]
    public void The_next_minor_raises_the_minor_and_resets_the_patch(string installed, string expected)
    {
        Assert.Equal(expected, UpdateOffer.NextMinor(installed));
    }

    /// <summary>Örnek teklif: gelen sürüm kurulu sürümün bir sonraki minor'ı, boyut <c>18.4 MB</c>, maddeler
    /// prototipin üç örnek maddesi (1 Performance, 2 Fixed) — yazıldıkları sırayla; çizim sırası kartın işidir
    /// (<see cref="ReleaseNotes.KindOrder"/>).</summary>
    [Fact]
    public void The_sample_offer_is_the_next_minor_of_the_installed_version_with_the_prototype_highlights()
    {
        var sample = UpdateOffer.Sample;

        Assert.Equal(UpdateOffer.NextMinor(AppIdentity.Version), sample.Version);
        Assert.NotEqual(AppIdentity.Version, sample.Version);
        Assert.Equal("18.4 MB", sample.Size);
        Assert.Equal(
            new[]
            {
                new ReleaseNote(NoteKind.Performance,
                    "Sync reads project files in parallel — about twice as fast on large solutions."),
                new ReleaseNote(NoteKind.Fixed, "Copy log keeps its line breaks when pasted into Teams or Outlook."),
                new ReleaseNote(NoteKind.Fixed, "A project renamed on disk is picked up by the next Sync."),
            },
            sample.Highlights);
    }
}
