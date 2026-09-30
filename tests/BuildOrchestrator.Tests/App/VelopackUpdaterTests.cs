using BuildOrchestrator.App.Services.Updates;
using Velopack;

namespace BuildOrchestrator.Tests.App;

/// <summary>[motor · Task 10] Gerçek Velopack, ağ yok: test host'u Velopack ile KURULMAMIŞ bir kopyadır — sarmalayıcı bunu
/// doğru bildirmeli (UpdateService kapısı buna dayanır) ve bekleyen paket olmamalı. Kurulu kopyadaki davranış yerel feed
/// provasıyla elle doğrulanır (ARCHITECTURE §17.6).</summary>
public class VelopackUpdaterTests
{
    [Fact]
    public void A_copy_that_was_not_installed_by_Velopack_reports_itself_as_not_installed()
    {
        var updater = new VelopackUpdater(UpdateFeed.CreateSource(null, prerelease: false));
        Assert.False(updater.IsInstalled);
        Assert.Null(updater.PendingRestart);
    }

    [Fact]
    public void The_download_size_is_the_deltas_when_they_exist_and_the_full_package_otherwise()
    {
        Assert.Equal(300, VelopackUpdater.DownloadBytes(full: 1000, deltas: [100, 200]));
        Assert.Equal(1000, VelopackUpdater.DownloadBytes(full: 1000, deltas: []));
    }

    /// <summary>Velopack'in <c>SemanticVersion.ToString()</c>'i normalize biçimi verir ("1.8.0", sıfır revizyon yazılmaz) —
    /// UpdateService/AppIdentity'nin <c>System.Version</c> karşılaştırması bu biçime dayanır. Notu olmayan paket
    /// (feed'de <c>NotesMarkdown</c> null) boş nota döner: UpdateOffer.From null görmez.</summary>
    [Fact]
    public void A_feed_asset_becomes_a_candidate_with_a_plain_version_and_empty_notes_when_it_has_none()
    {
        var asset = new VelopackAsset { Version = SemanticVersion.Parse("1.8.0"), NotesMarkdown = null! };
        Assert.Equal(new UpdateCandidate("1.8.0", 42, ""), VelopackUpdater.ToCandidate(asset, bytes: 42));

        var withNotes = new VelopackAsset { Version = SemanticVersion.Parse("1.9.2"), NotesMarkdown = "### Added\n- Thing" };
        Assert.Equal(new UpdateCandidate("1.9.2", 7, "### Added\n- Thing"), VelopackUpdater.ToCandidate(withNotes, bytes: 7));
    }
}
