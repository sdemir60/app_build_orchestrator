using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T38+T70] Sticky şeridin SAF metin/ilerleme mantığı (<see cref="RibbonText"/>) — design-v1
/// <c>BuildApp.jsx:752-776</c> birebir. 11 faz-metni satırının HER BİRİ pinlenir (gerçek sayılarla, prototip
/// <c>36</c> placeholder'ı DEĞİL), ETA eşikleri ve progress kuralları ayrıca doğrulanır. Saf/hızlı — WPF YOK.
/// </summary>
public class RibbonTextTests
{
    // RunCounters(Total, Building, Queued, Succeeded, Failed, Skipped, DepAffected)
    private static RunCounters Counters(int building = 0, int queued = 0, int succeeded = 0,
                                        int failed = 0, int skipped = 0, int dep = 0, int total = 14)
        => new(total, building, queued, succeeded, failed, skipped, dep);

    [Fact]
    public void No_repository_line_is_the_neutral_invitation_in_faint_text()
    {
        var line = RibbonText.Compose(AppPhase.Empty, hasWorkspace: false, allClean: false, Counters(),
            willBuild: 0, finishedOfWillBuild: 0, totalProjects: 14, elapsedMs: 0, etaMs: null, checkDurMs: null, warnings: 0);
        Assert.Equal("Not ready — no repository selected", line.Text);
        Assert.Equal("Brush.TextFaint", line.BrushKey);
        Assert.Null(line.Glyph);
    }

    [Fact]
    public void Boot_line_waits_for_sync_in_dim_text()
    {
        var line = RibbonText.Compose(AppPhase.Boot, true, false, Counters(),
            0, 0, 14, 0, null, null, 0);
        Assert.Equal("▸ Waiting for Sync — project states appear after Sync", line.Text);
        Assert.Equal("Brush.TextDim", line.BrushKey);
        Assert.Null(line.Glyph);
    }

    [Fact]
    public void Syncing_line_shows_the_git_fetch_message_in_secondary_text()
    {
        var line = RibbonText.Compose(AppPhase.Syncing, true, false, Counters(),
            0, 0, 14, 0, null, null, 0);
        Assert.Equal("▸ Sync — git fetch origin…", line.Text);
        Assert.Equal("Brush.TextSecondary", line.BrushKey);
    }

    [Fact]
    public void Idle_all_clean_line_says_everything_looks_up_to_date()
    {
        var line = RibbonText.Compose(AppPhase.Idle, true, allClean: true, Counters(),
            0, 0, 14, 0, null, null, 0);
        Assert.Equal("▸ Ready — everything looks up to date", line.Text);
        Assert.Equal("Brush.TextSecondary", line.BrushKey);
    }

    [Fact]
    public void Idle_dirty_line_splits_to_build_and_up_to_date_counts()
    {
        var line = RibbonText.Compose(AppPhase.Idle, true, allClean: false, Counters(),
            willBuild: 7, finishedOfWillBuild: 0, totalProjects: 14, elapsedMs: 0, etaMs: null, checkDurMs: null, warnings: 0);
        Assert.Equal("▸ Ready — 7 to build · 7 up to date", line.Text);
    }

    // [E2/T10] Repo Sync'lendi ama hiç proje yok (0-proje state) → şerit "Ready — nothing to build".
    [Fact]
    public void Idle_with_zero_projects_says_nothing_to_build()
    {
        var line = RibbonText.Compose(AppPhase.Idle, true, allClean: true, Counters(total: 0),
            willBuild: 0, finishedOfWillBuild: 0, totalProjects: 0, elapsedMs: 0, etaMs: null, checkDurMs: null, warnings: 0);
        Assert.Equal("Ready — nothing to build", line.Text);
        Assert.Equal("Brush.TextSecondary", line.BrushKey);
        Assert.Null(line.Glyph);
    }

    // [E2/T10] Son Sync başarısız oldu → şerit KIRMIZI "Sync failed — {reason}" (faz-metnini EZER).
    [Fact]
    public void Sync_failed_shows_a_red_reason_line_over_the_phase_text()
    {
        var line = RibbonText.Compose(AppPhase.Idle, hasWorkspace: true, allClean: false, Counters(),
            willBuild: 3, finishedOfWillBuild: 0, totalProjects: 14, elapsedMs: 0, etaMs: null, checkDurMs: null,
            warnings: 0, engineDiedMessage: null, syncError: "fatal: could not read from remote repository");
        Assert.Equal("Sync failed — fatal: could not read from remote repository", line.Text);
        Assert.Equal("Brush.StatusFailText", line.BrushKey);
        Assert.Equal("failed", line.Glyph);
    }

    // [runFailed] Koşan bir run motor tarafında beklenmeyen bir istisnayla düştüğünde runCompleted ASLA gelmez:
    // faz Running'de donar ve şerit "▸ Building 3/10" demeye devam ederdi — derleme çoktan bitmişken. Sync
    // hatasının birebir ikizi: KIRMIZI gerekçe satırı faz-metnini EZER.
    [Fact]
    public void Run_failed_shows_a_red_reason_line_over_the_phase_text()
    {
        var line = RibbonText.Compose(AppPhase.Running, hasWorkspace: true, allClean: false, Counters(building: 2),
            willBuild: 10, finishedOfWillBuild: 3, totalProjects: 14, elapsedMs: 12_000, etaMs: 35_000, checkDurMs: null,
            warnings: 0, engineDiedMessage: null, syncError: null, runError: "access to the log file was denied");
        Assert.Equal("Run failed — access to the log file was denied", line.Text);
        Assert.Equal("Brush.StatusFailText", line.BrushKey);
        Assert.Equal("failed", line.Glyph);
    }

    // Öncelik zinciri: engine ölümü > Sync hatası > run hatası > faz. Engine ölümü her şeyi ezer (motor yoksa
    // diğer iki metnin retry yolu da yoktur); Sync hatası run hatasını ezer (durumlar bilinmiyorsa bir sonraki
    // adım zaten Sync'tir).
    [Fact]
    public void A_sync_failure_outranks_a_run_failure_and_the_engine_death_outranks_both()
    {
        var syncWins = RibbonText.Compose(AppPhase.Running, true, false, Counters(), 10, 3, 14, 0, null, null, 0,
            engineDiedMessage: null, syncError: "fetch failed", runError: "run blew up");
        Assert.Equal("Sync failed — fetch failed", syncWins.Text);

        var engineWins = RibbonText.Compose(AppPhase.Running, true, false, Counters(), 10, 3, 14, 0, null, null, 0,
            engineDiedMessage: "Engine stopped unexpectedly (exit 139)", syncError: "fetch failed", runError: "run blew up");
        Assert.Equal("Engine stopped unexpectedly (exit 139)", engineWins.Text);
    }

    // [E2/T37 · EngineDiedMessage ÖNCELİĞİ] Engine öldüyse şerit, HANGİ Phase'de olursa olsun KIRMIZI ölüm metnini
    // gösterir — Phase (burada Running) ve hatta bir Sync hatası bile YOK SAYILIR (en yüksek öncelik).
    [Fact]
    public void Engine_died_message_overrides_every_phase_and_takes_priority_over_sync_error()
    {
        var line = RibbonText.Compose(AppPhase.Running, hasWorkspace: true, allClean: false, Counters(building: 2),
            willBuild: 5, finishedOfWillBuild: 1, totalProjects: 14, elapsedMs: 1234, etaMs: 8000, checkDurMs: null,
            warnings: 0, engineDiedMessage: "Engine stopped unexpectedly (exit 139)", syncError: "some sync error");
        Assert.Equal("Engine stopped unexpectedly (exit 139)", line.Text);
        Assert.Equal("Brush.StatusFailText", line.BrushKey);
        Assert.Equal("failed", line.Glyph);
    }

    [Fact]
    public void Running_all_clean_line_says_checking()
    {
        var line = RibbonText.Compose(AppPhase.Running, true, allClean: true, Counters(building: 1),
            0, 0, 14, 5000, null, null, 0);
        Assert.Equal("▸ Checking — scanning for changes…", line.Text);
        Assert.Equal("Brush.TextSecondary", line.BrushKey);
    }

    [Fact]
    public void Running_line_shows_finished_over_willbuild_with_elapsed_and_eta()
    {
        var line = RibbonText.Compose(AppPhase.Running, true, allClean: false, Counters(building: 1, queued: 6),
            willBuild: 14, finishedOfWillBuild: 7, totalProjects: 14, elapsedMs: 24_000, etaMs: 34_000, checkDurMs: null, warnings: 0);
        Assert.Equal("▸ Building 7/14 · 24s · ~35s left", line.Text);
        Assert.Equal("Brush.TextSecondary", line.BrushKey);
        Assert.Null(line.Glyph);
    }

    // [Stopping] Stop'a basıldıktan SONRA, run gerçekten bitene kadar geçen pencere. Graceful stop uçuştaki
    // MSBuild.exe child'larının bitmesini bekler; şerit bu pencerede "hâlâ Building" DEMEMELİDİR (tıklama
    // kaydedilmiş görünmez) ama "Stopped" da diyemez (henüz durmadı). ETA eki BİLEREK yok: yeni dispatch
    // olmadığı için kuyruk tahmini artık anlamsız, kalan tek bilgi kaç projenin uçuşta olduğudur.
    [Fact]
    public void Stopping_line_reports_the_in_flight_count_and_drops_the_eta()
    {
        var line = RibbonText.Compose(AppPhase.Stopping, true, allClean: false, Counters(building: 2, queued: 6),
            willBuild: 14, finishedOfWillBuild: 7, totalProjects: 14, elapsedMs: 24_000, etaMs: 34_000, checkDurMs: null, warnings: 0);
        // "finishing" ARTIK YANLIŞ olurdu: hard stop'ta uçuştaki child'lar bitmiyor, öldürülüyor. Eski metin
        // graceful drain penceresini anlatıyordu (bkz. Stop_sends_a_hard_stop_... testinin gerekçesi).
        Assert.Equal("▸ Stopping — 7/14 · terminating 2 in flight", line.Text);
        Assert.Equal("Brush.TextSecondary", line.BrushKey);
        Assert.Null(line.Glyph);
    }

    // Uçuşta hiçbir şey kalmadığında sayı vermek yanıltıcı olurdu ("finishing 0 in flight") — run kapanmak
    // üzeredir, satır da onu söyler.
    [Fact]
    public void Stopping_line_says_wrapping_up_when_nothing_is_left_in_flight()
    {
        var line = RibbonText.Compose(AppPhase.Stopping, true, allClean: false, Counters(building: 0, queued: 6),
            willBuild: 14, finishedOfWillBuild: 14, totalProjects: 14, elapsedMs: 24_000, etaMs: null, checkDurMs: null, warnings: 0);
        Assert.Equal("▸ Stopping — wrapping up", line.Text);
        Assert.Equal("Brush.TextSecondary", line.BrushKey);
    }

    /// <summary>Eski metin <c>▸ Stopped — 3/10 · rest queued</c> idi. "queued" SÜRDÜRÜLEBİLİRLİK ima ediyordu
    /// (Continue butonu vardı); Continue kaldırıldığı için o söz artık karşılıksız — kalanlar bir sonraki
    /// Build'de baştan işlenir. Yeni metin yalnızca olguyu söyler.</summary>
    [Fact]
    public void Stopped_line_shows_progress_and_what_was_not_built_in_dim_text()
    {
        var line = RibbonText.Compose(AppPhase.Stopped, true, false, Counters(queued: 7),
            willBuild: 10, finishedOfWillBuild: 3, totalProjects: 14, elapsedMs: 30_000, etaMs: null, checkDurMs: null, warnings: 0);
        Assert.Equal("▸ Stopped — 3/10 · 7 not built", line.Text);
        Assert.Equal("Brush.TextDim", line.BrushKey);
    }

    [Fact]
    public void Done_all_clean_line_reports_check_duration_and_succeeded_glyph()
    {
        var line = RibbonText.Compose(AppPhase.Done, true, allClean: true, Counters(),
            willBuild: 0, finishedOfWillBuild: 0, totalProjects: 14, elapsedMs: 4200, etaMs: null, checkDurMs: 4200, warnings: 0);
        Assert.Equal("Everything up to date — 14 projects checked in 4.2s, nothing to build", line.Text);
        Assert.Equal("Brush.StatusSuccessText", line.BrushKey);
        Assert.Equal("succeeded", line.Glyph);
    }

    [Fact]
    public void Done_with_failures_line_lists_failed_succeeded_dep_skipped_warnings_and_elapsed()
    {
        var c = Counters(succeeded: 4, failed: 5, skipped: 2, dep: 4);
        var line = RibbonText.Compose(AppPhase.Done, true, allClean: false, c,
            willBuild: 11, finishedOfWillBuild: 11, totalProjects: 14, elapsedMs: 65_000, etaMs: null, checkDurMs: null, warnings: 3);
        Assert.Equal("Completed — 5 failed · 4 succeeded (4 dependency-affected) · 2 skipped · 3 warnings · 1m 05s", line.Text);
        Assert.Equal("Brush.StatusFailText", line.BrushKey);
        Assert.Equal("failed", line.Glyph);
    }

    [Fact]
    public void Done_clean_line_lists_succeeded_skipped_and_elapsed_with_success_glyph()
    {
        var c = Counters(succeeded: 12, failed: 0, skipped: 2);
        var line = RibbonText.Compose(AppPhase.Done, true, allClean: false, c,
            willBuild: 12, finishedOfWillBuild: 12, totalProjects: 14, elapsedMs: 24_000, etaMs: null, checkDurMs: null, warnings: 0);
        Assert.Equal("Completed — 12 succeeded · 2 skipped · 24s", line.Text);
        Assert.Equal("Brush.StatusSuccessText", line.BrushKey);
        Assert.Equal("succeeded", line.Glyph);
    }

    [Fact]
    public void Done_with_failures_omits_dependency_and_warnings_segments_when_zero()
    {
        var c = Counters(succeeded: 8, failed: 1, skipped: 0, dep: 0);
        var line = RibbonText.Compose(AppPhase.Done, true, allClean: false, c,
            willBuild: 9, finishedOfWillBuild: 9, totalProjects: 14, elapsedMs: 12_000, etaMs: null, checkDurMs: null, warnings: 0);
        Assert.Equal("Completed — 1 failed · 8 succeeded · 0 skipped · 12s", line.Text);
    }

    [Theory]
    [InlineData(3999, " · almost done")]
    [InlineData(4000, " · ~5s left")]
    [InlineData(34_000, " · ~35s left")]
    [InlineData(1_000, " · almost done")]
    public void Eta_suffix_matches_the_prototype_thresholds(long eta, string expected)
    {
        // Kapı: building + queued > 0 olmalı, aksi halde suffix null döner.
        Assert.Equal(expected, RibbonText.EtaSuffix(eta, Counters(building: 1)));
    }

    [Fact]
    public void Eta_is_hidden_once_nothing_is_building_or_queued()
    {
        Assert.Null(RibbonText.EtaSuffix(34_000, Counters(building: 0, queued: 0)));
        Assert.Null(RibbonText.EtaSuffix(null, Counters(building: 1, queued: 3)));
    }

    [Fact]
    public void Progress_turns_red_as_soon_as_one_project_fails_even_mid_run()
    {
        Assert.Equal("failed", RibbonText.ProgressStatus(AppPhase.Running, Counters(building: 1, failed: 1)));
        Assert.Equal("Brush.StatusFail", RibbonText.FillBrushKeyFor("failed"));
        // Hatasız koşu ortası → amber; hatasız done → yeşil.
        Assert.Equal("building", RibbonText.ProgressStatus(AppPhase.Running, Counters(building: 2)));
        Assert.Equal("succeeded", RibbonText.ProgressStatus(AppPhase.Done, Counters(succeeded: 5)));
    }

    [Fact]
    public void Progress_uses_the_skipped_ratio_during_an_all_clean_check_and_snaps_to_hundred_when_done()
    {
        double mid = RibbonText.Progress(AppPhase.Running, allClean: true, Counters(skipped: 3, total: 12),
            willBuild: 0, finishedOfWillBuild: 0, totalProjects: 12);
        Assert.Equal(25.0, mid, 3);

        double done = RibbonText.Progress(AppPhase.Done, allClean: true, Counters(skipped: 12, total: 12),
            willBuild: 0, finishedOfWillBuild: 0, totalProjects: 12);
        Assert.Equal(100.0, done, 3);
    }

    [Fact]
    public void Progress_uses_finished_over_willbuild_ratio_when_not_all_clean()
    {
        double p = RibbonText.Progress(AppPhase.Running, allClean: false, Counters(),
            willBuild: 8, finishedOfWillBuild: 2, totalProjects: 14);
        Assert.Equal(25.0, p, 3);
    }
}
