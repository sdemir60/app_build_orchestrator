using System.IO;
using BuildOrchestrator.App.Services.Updates;
using Velopack;
using Velopack.Locators;

namespace BuildOrchestrator.Tests.App;

/// <summary>[motor · Task 10] Gerçek Velopack, ağ yok. Kurulu/kurulu değil kararı ENJEKTE edilen konumlayıcıyla sınanır:
/// Velopack'in kendi <see cref="TestVelopackLocator"/>'ı "kurulu kopya"yı, ondan türeyen <see cref="NotInstalledLocator"/>
/// "konumlayıcı var ama kurulu sürüm yok" durumunu (bin/publish klasöründen çalışan kopya) taklit eder — ikisi de
/// <c>UpdateManager.IsInstalled</c> yolundan geçer. Process-global <c>VelopackLocator.Current</c>'e dokunulmaz: sonuç
/// süitteki başka bir testin sırasına bağlı olmaz. Çıkışta kurulum <see cref="RecordingLocator"/> ile sınanır (Update.exe
/// başlatılışı kaydedilir, process açılmaz). Kurulu kopyadaki Check/Download davranışı yerel feed provasıyla elle
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

    /// <summary>Update.exe'nin başlatılışını KAYDEDER, gerçek process açmaz: Velopack'in <c>UpdateExe.Apply</c>'ı komut
    /// satırını kurup <c>Locator.Process.StartProcess</c>'e verir; <see cref="TestVelopackLocator"/> o çağrıda gerçekten
    /// process başlatırdı. <see cref="IProcessImpl"/>'in yeniden uygulanması <c>Process =&gt; this</c> çağrısını buraya yönlendirir.</summary>
    private sealed class RecordingLocator(string dir, string updateExe, string installed, VelopackAsset downloaded)
        : TestVelopackLocator("bo-test", installed, dir, appDir: dir, rootDir: dir, updateExe: updateExe, localPackage: downloaded),
            IProcessImpl
    {
        public List<string[]> Started { get; } = [];
        string IProcessImpl.GetCurrentProcessPath() => GetCurrentProcessPath();
        uint IProcessImpl.GetCurrentProcessId() => GetCurrentProcessId();
        void IProcessImpl.Exit(int exitCode) => Exit(exitCode);
        void IProcessImpl.StartProcess(string exePath, IEnumerable<string> args, string workDir, bool showWindow) =>
            Started.Add([.. args]);
    }

    /// <summary>Paket klasöründe yalnız <paramref name="downloadedVersion"/> iner (Velopack yenisini indirince eskiyi siler);
    /// <c>Update.exe</c> ve paket dosyası <c>UpdateExe.Apply</c>'ın <c>File.Exists</c> denetimi için bulunur.</summary>
    private static (VelopackUpdater Updater, RecordingLocator Locator, string PackagePath) UpdaterWithDownload(
        TempDir dir, string downloadedVersion)
    {
        var fileName = $"bo-test-{downloadedVersion}-full.nupkg";
        var package = Path.Combine(dir.Path, fileName);
        File.WriteAllText(package, "");
        var updateExe = Path.Combine(dir.Path, "Update.exe");
        File.WriteAllText(updateExe, "");
        var asset = new VelopackAsset
        {
            Version = SemanticVersion.Parse(downloadedVersion), Type = VelopackAssetType.Full, FileName = fileName, Size = 1,
        };
        var locator = new RecordingLocator(dir.Path, updateExe, installed: "1.7.0", asset);
        return (UpdaterFor(locator), locator, package);
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

    /// <summary>Çıkışta Update.exe indirilmiş paketle, <c>--silent</c> (K6: Velopack penceresi yok) başlatılır;
    /// <c>--norestart</c> yalnız <c>restart</c> false iken eklenir (Later + normal çıkış → sessiz kurulum).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Exit_applies_the_offered_package_silently_and_restarts_only_when_asked(bool restart)
    {
        using var dir = new TempDir();
        var (updater, locator, package) = UpdaterWithDownload(dir, "1.9.0");
        updater.ApplyOnExit(new UpdateCandidate("1.9.0", 1, ""), restart);
        var args = Assert.Single(locator.Started);
        Assert.Contains("--silent", args);
        Assert.Contains("apply", args);
        Assert.Equal(package, args[Array.IndexOf(args, "--package") + 1]);
        Assert.Equal(restart, !args.Contains("--norestart"));
    }

    /// <summary>Teklif 1.8.0 iken sonraki tur 1.9.0'ı indirip yayımı attıysa (UpdateService: yayım atarsa sürüm "hazır"
    /// sayılmaz) paket klasöründe yalnız gösterilmemiş 1.9.0 vardır. Çıkış bunu KURMAZ: <c>UpdatePendingRestart</c> en yeni
    /// indirilmiş paketi verir, aday yok sayılırsa kullanıcının hiç görmediği sürüm sessizce kurulurdu.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Exit_never_applies_a_package_that_was_not_the_one_offered(bool restart)
    {
        using var dir = new TempDir();
        var (updater, locator, _) = UpdaterWithDownload(dir, "1.9.0");
        updater.ApplyOnExit(new UpdateCandidate("1.8.0", 1, ""), restart);
        Assert.Empty(locator.Started);
    }

    /// <summary>Seçici saf kural: kurulacak varlık ADAYIN sürümüdür. Önce diskteki (indirilmiş) paket, yoksa son kontrolün
    /// hedefi; ikisinden ilki aday değilse uygulanmaz — <c>lastCheckTarget</c> aday olsa bile: diskteki en yeni paket
    /// başkasıysa Update.exe <c>--package</c> bulamayıp klasördeki en yeniyi kurardı.</summary>
    [Fact]
    public void The_asset_to_apply_is_the_offered_version_or_nothing()
    {
        var offered = new UpdateCandidate("1.8.0", 1, "");
        var downloaded18 = new VelopackAsset { Version = SemanticVersion.Parse("1.8.0") };
        var downloaded19 = new VelopackAsset { Version = SemanticVersion.Parse("1.9.0") };
        var target18 = new VelopackAsset { Version = SemanticVersion.Parse("1.8.0") };

        Assert.Same(downloaded18, VelopackUpdater.AssetToApply(offered, downloaded18, target18)); // indirilmiş paket önce
        Assert.Same(target18, VelopackUpdater.AssetToApply(offered, downloaded: null, target18)); // diskte bilinen yok → son kontrol
        Assert.Null(VelopackUpdater.AssetToApply(offered, downloaded19, target18));               // en yeni paket gösterilmemiş
        Assert.Null(VelopackUpdater.AssetToApply(offered, downloaded: null, lastCheckTarget: downloaded19));
        Assert.Null(VelopackUpdater.AssetToApply(offered, downloaded: null, lastCheckTarget: null));
    }

    [Fact]
    public void The_download_size_is_the_deltas_when_they_exist_and_the_full_package_otherwise()
    {
        Assert.Equal(300, VelopackUpdater.DownloadBytes(full: 1000, deltas: [100, 200]));
        Assert.Equal(1000, VelopackUpdater.DownloadBytes(full: 1000, deltas: []));
    }

    /// <summary>Velopack'in <c>SemanticVersion.ToString()</c>'i normalize biçimi verir ("1.8.0", sıfır revizyon yazılmaz;
    /// ön sürümde "1.9.0-beta.1"). Notu olmayan paket (feed'de <c>NotesMarkdown</c> null) boş nota döner:
    /// UpdateOffer.From null görmez.</summary>
    [Fact]
    public void A_feed_asset_becomes_a_candidate_with_a_plain_version_and_empty_notes_when_it_has_none()
    {
        var asset = new VelopackAsset { Version = SemanticVersion.Parse("1.8.0"), NotesMarkdown = null! };
        Assert.Equal(new UpdateCandidate("1.8.0", 42, ""), VelopackUpdater.ToCandidate(asset, bytes: 42));

        var withNotes = new VelopackAsset { Version = SemanticVersion.Parse("1.9.2"), NotesMarkdown = "### Added\n- Thing" };
        Assert.Equal(new UpdateCandidate("1.9.2", 7, "### Added\n- Thing"), VelopackUpdater.ToCandidate(withNotes, bytes: 7));
    }

    /// <summary>Üretici (<c>ToCandidate</c>) ile tüketici (<c>UpdateService.IsNewer</c>) arasındaki sürüm sözleşmesi: ön
    /// sürüm paketinin dizgesi ("1.9.0-beta.1", K9 kapısı) kurulu sürümden yeni sayılmalı, kararlısı ondan da yeni.
    /// Sözleşme kırılırsa ön sürüm feed'i hiçbir hap üretmez.</summary>
    [Fact]
    public void A_prerelease_feed_asset_yields_a_version_the_update_gate_orders_correctly()
    {
        string FeedVersion(string v) => VelopackUpdater.ToCandidate(new VelopackAsset { Version = SemanticVersion.Parse(v) }, bytes: 0).Version;
        Assert.Equal("1.9.0-beta.1", FeedVersion("1.9.0-beta.1"));
        Assert.True(UpdateService.IsNewer(FeedVersion("1.9.0-beta.1"), "1.8.0"));
        Assert.True(UpdateService.IsNewer(FeedVersion("1.9.0"), FeedVersion("1.9.0-beta.1")));
    }
}
