using System.IO;
using System.Net.Http;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Services.Updates;
using Microsoft.Extensions.Time.Testing;

namespace BuildOrchestrator.Tests.App;

/// <summary>[motor · Task 9] Kontrol sessiz (açılış + 5 s, sonra 4 saat), indirme arka planda, teklif yalnız indirme
/// bitince (tasarım §2.12 "yalnız hazırken"); hata sessiz ve bir sonraki turda yeniden; kurulu olmayan kopya hiç
/// kontrol etmez; aynı sürüm ikinci kez yayımlanmaz; eşit/eski sürüm yok sayılır. Zaman sahtedir (D8).</summary>
public class UpdateServiceTests
{
    private sealed class FakeUpdater : IAppUpdater
    {
        public bool IsInstalled { get; set; } = true;
        public UpdateCandidate? PendingRestart { get; set; }
        public Func<UpdateCandidate?> OnCheck = () => null;
        public Func<UpdateCandidate, Task> OnDownload = _ => Task.CompletedTask;
        public Action OnApply = () => { };
        public int Checks, Downloads;
        public (UpdateCandidate Candidate, bool Restart)? Applied;
        public Task<UpdateCandidate?> CheckAsync(CancellationToken ct) { Checks++; return Task.FromResult(OnCheck()); }
        public Task DownloadAsync(UpdateCandidate c, CancellationToken ct) { Downloads++; return OnDownload(c); }
        public void ApplyOnExit(UpdateCandidate c, bool restart) { Applied = (c, restart); OnApply(); }
    }

    private static readonly UpdateCandidate Newer = new("99.0.0", 19_293_798, "## [99.0.0] - 2026-10-01\n### Fixed\n- D\n");

    /// <param name="beforePublish">Yayım delegate'inin içinde koşar; atarsa teklif <c>Published</c>'a girmez (UI'a
    /// marshal başarısız olmuş demektir).</param>
    private static (UpdateService Service, FakeUpdater Updater, FakeTimeProvider Time, List<UpdateOffer> Published) Rig(
        Action<UpdateOffer>? beforePublish = null)
    {
        var updater = new FakeUpdater();
        var time = new FakeTimeProvider();
        var published = new List<UpdateOffer>();
        return (new UpdateService(updater, time, offer => { beforePublish?.Invoke(offer); published.Add(offer); }), updater, time, published);
    }

    /// <summary>İlk çağrıda atan, sonrakilerde geçen yayım kancası (geçici bir marshal hatası).</summary>
    private static Action<UpdateOffer> FailsOnce()
    {
        var attempts = 0;
        return _ => { if (++attempts == 1) throw new InvalidOperationException("marshal"); };
    }

    [Fact]
    public void Start_does_nothing_when_the_copy_is_not_installed()
    {
        var (service, updater, time, published) = Rig();
        updater.IsInstalled = false;
        updater.OnCheck = () => Newer;
        service.Start();
        time.Advance(UpdateService.CheckInterval * 3);
        Assert.Equal(0, updater.Checks);
        Assert.Empty(published);
    }

    [Fact]
    public async Task The_first_check_waits_five_seconds_and_a_ready_offer_is_published_after_the_download()
    {
        var (service, updater, time, published) = Rig();
        updater.OnCheck = () => Newer;
        service.Start();
        time.Advance(UpdateService.FirstCheckDelay - TimeSpan.FromMilliseconds(1));
        Assert.Equal(0, updater.Checks);
        time.Advance(TimeSpan.FromMilliseconds(1));
        await service.RunningCycle; // test yüzeyi: uçuştaki turun tamamlanması
        Assert.Equal(1, updater.Checks);
        Assert.Equal(1, updater.Downloads);
        var offer = Assert.Single(published);
        Assert.Equal("99.0.0", offer.Version);
        Assert.Equal("18.4 MB", offer.Size);
        Assert.Same(offer, service.Ready);
    }

    [Fact]
    public async Task Checks_repeat_every_four_hours_and_the_same_ready_version_is_not_published_twice()
    {
        var (service, updater, time, published) = Rig();
        updater.OnCheck = () => Newer;
        service.Start();
        time.Advance(UpdateService.FirstCheckDelay); await service.RunningCycle;
        time.Advance(UpdateService.CheckInterval); await service.RunningCycle;
        time.Advance(UpdateService.CheckInterval); await service.RunningCycle;
        Assert.Equal(3, updater.Checks);
        Assert.Equal(1, updater.Downloads);
        Assert.Single(published);
    }

    /// <summary>Eşit vaka <see cref="AppIdentity.Version"/>'la kurulur (sabit "1.7.0" yazılmaz: sürüm her çıkışta
    /// değişir, testin ikinci bir doğruluk kaynağı olmaz); eski vaka her zaman altında kalan bir sabittir.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_equal_or_older_version_is_ignored(bool equalToInstalled)
    {
        var (service, updater, time, published) = Rig();
        var version = equalToInstalled ? AppIdentity.Version : "0.9.0";
        updater.OnCheck = () => Newer with { Version = version };
        await service.RunCycleAsync();
        Assert.Equal(0, updater.Downloads);
        Assert.Empty(published);
    }

    /// <summary>Karşılaştırma SemVer'dir (Velopack'in <c>SemanticVersion</c>'ı, feed'i sıralayanla aynı): ön sürüm etiketi de
    /// sıralanır — "1.9.0-beta.1" 1.8.0'dan yeni, kararlı "1.9.0" ondan yeni, "beta.2" "beta.1"den yeni; ön sürüm kendi
    /// kararlısından eski. Eşit, eski ve parse edilemeyen (feed ya da kurulu sürüm) elenir: bozuk veri hapı açmasın.</summary>
    [Theory]
    [InlineData("1.9.0-beta.1", "1.8.0", true)]
    [InlineData("1.9.0", "1.9.0-beta.1", true)]
    [InlineData("1.9.0-beta.2", "1.9.0-beta.1", true)]
    [InlineData("1.9.0-beta.1", "1.9.0", false)]
    [InlineData("1.9.0-beta.1", "1.9.0-beta.1", false)]
    [InlineData("1.8.0", "1.8.0", false)]
    [InlineData("1.7.0", "1.8.0", false)]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("not-a-version", "1.8.0", false)]
    [InlineData("1.9.0", "", false)]
    public void Versions_are_compared_as_semantic_versions_including_the_prerelease_label(string incoming, string installed, bool newer) =>
        Assert.Equal(newer, UpdateService.IsNewer(incoming, installed));

    /// <summary>[K9 · <c>BO_UPDATE_PRERELEASE=1</c>] Ön sürüm feed'i "99.0.0-beta.1" gibi bir sürüm üretir
    /// (<c>VelopackUpdater.ToCandidate</c>). Karşılaştırma <c>System.Version</c> ile yapılırken bu dizge parse edilemez,
    /// <c>IsNewer</c> false döner ve dev kapısı hiçbir teklif üretmezdi.</summary>
    [Fact]
    public async Task A_prerelease_of_a_newer_version_is_offered()
    {
        var (service, updater, time, published) = Rig();
        updater.OnCheck = () => Newer with { Version = "99.0.0-beta.1" };
        await service.RunCycleAsync();
        Assert.Equal(1, updater.Downloads);
        Assert.Equal("99.0.0-beta.1", Assert.Single(published).Version);
    }

    [Fact]
    public async Task A_failing_check_or_download_is_silent_and_retried_next_time()
    {
        var (service, updater, time, published) = Rig();
        updater.OnCheck = () => throw new HttpRequestException("offline");
        await service.RunCycleAsync();
        Assert.Empty(published);
        updater.OnCheck = () => Newer;
        updater.OnDownload = _ => throw new IOException("checksum");
        await service.RunCycleAsync();
        Assert.Empty(published);
        updater.OnDownload = _ => Task.CompletedTask;
        await service.RunCycleAsync();
        Assert.Single(published);
    }

    /// <summary>Durum teklif yayımından SONRA işlenir: yayım atarsa sürüm "hazır" sayılmaz. Aksi hâlde aynı sürümün
    /// yinelenen-yayım elemesi (<c>candidate.Version == _ready.Version</c>) oturum boyunca yeniden denemeyi keserdi ve
    /// kullanıcının hiç görmediği paket çıkışta sessizce kurulurdu.</summary>
    [Fact]
    public async Task A_publish_that_fails_leaves_no_ready_offer_and_no_install_on_exit()
    {
        var (service, updater, time, published) = Rig(FailsOnce());
        updater.OnCheck = () => Newer;
        await service.RunCycleAsync();               // yayım atar → sessiz
        Assert.Empty(published);
        Assert.Null(service.Ready);
        service.ApplyOnExit();
        Assert.Null(updater.Applied);                // hiç teklif edilmemiş paket kurulmaz
    }

    [Fact]
    public async Task A_publish_that_fails_is_retried_on_the_next_cycle()
    {
        var (service, updater, time, published) = Rig(FailsOnce());
        updater.OnCheck = () => Newer;
        await service.RunCycleAsync();               // yayım atar → sessiz
        await service.RunCycleAsync();               // sonraki tur aynı sürümü yeniden dener
        var offer = Assert.Single(published);
        Assert.Same(offer, service.Ready);
        Assert.Equal(2, updater.Checks);
    }

    /// <summary>Açılışta önceki oturumdan kalan teklifin yayımı da aynı kurala tabidir: atarsa App açılışına yayılmaz
    /// ve zamanlayıcı yine kurulur (Start ile döngü yolu tutarlı).</summary>
    [Fact]
    public async Task A_pending_offer_that_fails_to_publish_is_silent_and_the_timer_still_runs()
    {
        var (service, updater, time, published) = Rig(FailsOnce());
        updater.PendingRestart = Newer;
        updater.OnCheck = () => Newer;
        service.Start();                             // yayım atar → Start'a yayılmaz
        Assert.Empty(published);
        time.Advance(UpdateService.FirstCheckDelay);
        await service.RunningCycle;
        Assert.Equal(1, updater.Checks);             // zamanlayıcı kuruldu, tur koştu
        var offer = Assert.Single(published);        // ve teklif bu turda yeniden yayımlandı
        Assert.Same(offer, service.Ready);
    }

    [Fact]
    public void A_download_left_from_an_earlier_session_is_offered_at_once()
    {
        var (service, updater, time, published) = Rig();
        updater.PendingRestart = Newer;
        service.Start();
        Assert.Single(published);
        Assert.Equal(0, updater.Checks);
    }

    [Fact]
    public async Task Apply_on_exit_installs_the_ready_update_and_restarts_only_when_asked()
    {
        var (service, updater, time, published) = Rig();
        service.ApplyOnExit();                       // hazır bir şey yok → hiçbir şey
        Assert.Null(updater.Applied);
        updater.OnCheck = () => Newer;
        await service.RunCycleAsync();
        service.ApplyOnExit();                       // Later + normal çıkış → sessiz kurulum, yeniden açılmaz
        Assert.Equal((Newer, false), updater.Applied);
        service.RequestRestart();
        service.ApplyOnExit();                       // Restart to update → yeniden açılır
        Assert.Equal((Newer, true), updater.Applied);
    }

    /// <summary>Kurulumu başlatamamak (Velopack "Cannot find Update.exe" ya da Update.exe'nin başlatılamaması —
    /// <c>Win32Exception</c>) çıkışı çökertmez: <c>App.OnExit</c>'te yakalayan yoktur (DispatcherUnhandledException
    /// handler'ı ve <c>Main</c>'de catch yok), atarsa Application Error çıkar ve hazır teklif her açılışta geri geldiği için
    /// çökme her çıkışta tekrarlanır. Hata sessizdir (servisin genel sözleşmesi) — çağrı denenmiş sayılır, sonraki
    /// açılış yine dener.</summary>
    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(System.ComponentModel.Win32Exception))]
    public async Task A_failing_install_start_on_exit_is_silent(Type failure)
    {
        var (service, updater, time, published) = Rig();
        updater.OnCheck = () => Newer;
        await service.RunCycleAsync();
        updater.OnApply = () => throw (Exception)Activator.CreateInstance(failure)!;
        var thrown = Record.Exception(service.ApplyOnExit);
        Assert.Null(thrown);
        Assert.Equal((Newer, false), updater.Applied); // kurulum gerçekten denendi (atan çağrı buydu)
    }

    [Fact]
    public async Task A_cycle_that_is_still_running_is_not_started_twice()
    {
        var (service, updater, time, published) = Rig();
        var gate = new TaskCompletionSource();
        updater.OnCheck = () => Newer;
        updater.OnDownload = _ => gate.Task;
        var first = service.RunCycleAsync();
        var second = service.RunCycleAsync();       // uçuşta tur var → hemen döner
        Assert.True(second.IsCompleted);
        Assert.Equal(1, updater.Checks);
        gate.SetResult();
        await first;
        Assert.Single(published);
    }
}
