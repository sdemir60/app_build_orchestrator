using BuildOrchestrator.App.Services.Updates;
using Velopack;
using Velopack.Locators;

namespace BuildOrchestrator.Tests.App;

/// <summary>[motor · Task 10] Gerçek Velopack, ağ yok. Kurulu/kurulu değil kararı ENJEKTE edilen konumlayıcıyla sınanır:
/// Velopack'in kendi <see cref="TestVelopackLocator"/>'ı "kurulu kopya"yı, ondan türeyen <see cref="NotInstalledLocator"/>
/// "konumlayıcı var ama kurulu sürüm yok" durumunu (bin/publish klasöründen çalışan kopya) taklit eder — ikisi de
/// <c>UpdateManager.IsInstalled</c> yolundan geçer. Process-global <c>VelopackLocator.Current</c>'e dokunulmaz: sonuç
/// süitteki başka bir testin sırasına bağlı olmaz. Kurulu kopyadaki Check/Download davranışı yerel feed provasıyla elle
/// doğrulanır (ARCHITECTURE §17.6).</summary>
public class VelopackUpdaterTests
{
    /// <summary>Konumlayıcı var (<c>Program.Main</c>'de <c>VelopackApp.Build().Run()</c> her zaman kurar) ama kurulu
    /// sürüm yok: bin'den ya da publish klasöründen çalışan kopya. Kurucu Velopack'in kendisi olduğundan kurulum
    /// bilgisi yalnız <see cref="TestVelopackLocator.CurrentlyInstalledVersion"/>'dan gelir.</summary>
    private sealed class NotInstalledLocator(string packagesDir) : TestVelopackLocator("bo-test", "1.0.0", packagesDir)
    {
        public override SemanticVersion? CurrentlyInstalledVersion => null;
    }

    private static VelopackUpdater UpdaterFor(IVelopackLocator locator) =>
        new(UpdateFeed.CreateSource(null, prerelease: false), locator);

    private static TestVelopackLocator InstalledCopy(TempDir dir, string version, VelopackAsset? downloaded = null) =>
        new("bo-test", version, dir.Path, appDir: dir.Path, rootDir: dir.Path, updateExe: null, localPackage: downloaded);

    /// <summary>Bu test yalnızca "konumlayıcı YOKKEN fırlatmadan false" kapısını doğrular (<c>IsCurrentSet</c> false →
    /// <c>Manager</c>'a hiç dokunulmaz). Eski adı "kurulmamış kopya kurulmadığını bildirir"di ama kurulmamış kopyayı
    /// değil konumlayıcısı olmayan process'i sınıyordu: <c>&amp;&amp;</c> kısa devre yaptığından
    /// <c>Manager.IsInstalled</c> dalı hiçbir testte koşmuyordu. O dal aşağıdaki iki testte enjekte konumlayıcıyla sınanır.</summary>
    [Fact]
    public void A_process_without_a_Velopack_locator_reports_itself_as_not_installed_without_throwing()
    {
        var updater = new VelopackUpdater(UpdateFeed.CreateSource(null, prerelease: false));
        Assert.False(updater.IsInstalled);
        Assert.Null(updater.PendingRestart);
    }

    [Fact]
    public void A_copy_that_Velopack_installed_reports_itself_as_installed()
    {
        using var dir = new TempDir();
        var updater = UpdaterFor(InstalledCopy(dir, "1.7.0"));
        Assert.True(updater.IsInstalled);
        Assert.Null(updater.PendingRestart); // indirilmiş paket yok
    }

    /// <summary>Üretimdeki durum budur: konumlayıcı her zaman kurulu, kararı yalnız <c>Manager.IsInstalled</c> verir. Bu kapı
    /// "motor yalnız kurulu kopyada" değişmezinin kendisidir — bin/dev kopyası motoru çalıştırmamalı.</summary>
    [Fact]
    public void A_copy_run_from_a_build_folder_is_not_installed_even_though_a_locator_exists()
    {
        using var dir = new TempDir();
        var updater = UpdaterFor(new NotInstalledLocator(dir.Path));
        Assert.False(updater.IsInstalled);
        Assert.Null(updater.PendingRestart);
    }

    [Fact]
    public void A_package_downloaded_in_an_earlier_session_is_the_pending_restart()
    {
        using var dir = new TempDir();
        var downloaded = new VelopackAsset
        {
            Version = SemanticVersion.Parse("1.9.0"), Type = VelopackAssetType.Full, Size = 1234, NotesMarkdown = "### Added\n- Thing",
        };
        var updater = UpdaterFor(InstalledCopy(dir, "1.7.0", downloaded));
        Assert.Equal(new UpdateCandidate("1.9.0", 1234, "### Added\n- Thing"), updater.PendingRestart);
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
