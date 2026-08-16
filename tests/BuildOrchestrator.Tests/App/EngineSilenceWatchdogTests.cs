using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Motor SUSARSA kurtulma yolu. Ölçülen üretim vakasında motor planlamanın ortasında dondu (drain edilmeyen
/// stderr pipe'ı): <c>runStarted</c> hiç gelmedi, ardından basılan Stop'un <c>runStopped</c> ack'i de hiç
/// yazılamadı — App <c>IsStarting</c>/<c>Stopping</c> penceresinde SONSUZA DEK kilitli kaldı ve tek çıkış
/// uygulamayı kapatmaktı. O kök neden düzeltildi; bu, BİLİNMEYEN bir sonraki donma için kapıdır.
///
/// <para><b>Neden ping/pong DEĞİL:</b> o vakada motorun komut döngüsü CANLIYDI (koordinatör run'ı arka plan
/// task'ında koşturur, <c>startRun</c> hemen döner) — donan yalnız run task'ıydı, yani bir ping'e pong
/// dönerdi. Doğru sinyal "motor yaşıyor mu" değil, <b>beklenen geçiş + motor sessizliği</b>dir.</para>
///
/// <para><b>Neden yalnız BEKLEYİŞ pencereleri:</b> watchdog SADECE motordan bir geçiş beklenirken kuruludur —
/// run istendi ama <c>runStarted</c> gelmedi (<c>IsStarting</c>), stop istendi ama <c>runStopped</c> gelmedi
/// (faz <c>Stopping</c>), ya da Sync istendi/koşuyor ama <c>syncCompleted</c> gelmedi. Koşan bir run'da uzun
/// ve sessiz bir derleme adımı meşrudur; orada uyarmak yanlış alarm üretirdi. <b>Watchdog hiçbir şeyi
/// OTOMATİK açmaz</b> — yalnız çıkış kapısını gösterir: graceful drain dakikalarca sürebilir ve kilidi
/// kendiliğinden açmak, hâlâ koşan bir motora ikinci bir run başlatmaya izin vermek demekti.</para>
///
/// <para><b>[DEĞİŞEN KURAL] Sync penceresi SONRADAN eklendi.</b> Eskiden kapsam iki pencereydi ve Sync
/// dışarıdaydı; ölçülen bedel: motor Sync ortasında donduğunda şerit sonsuza dek "▸ Sync — git fetch
/// origin…" gösteriyor, "Restart engine" kapısı HİÇ görünmüyordu — tek çıkış uygulamayı kapatmaktı. Sync de
/// tam olarak bir geçiş bekleyişidir; üstelik Sync düğmesi artık uçuşta bir Sync varken kilitli olduğundan
/// (bkz. <c>RunViewModelStateTests</c> Sync guard bölümü) donmuş bir Sync'ten çıkışın TEK yolu bu kapıdır.
/// Sync'in git çağrıları 30 sn'de zaman aşımına uğrar (<c>GitService.CommandTimeout</c>), yani 90 sn'lik
/// eşiğe takılan bir motor "yavaş" değil GERÇEKTEN donmuştur.</para>
///
/// <para>D8: gerçek zaman beklenmez — saat enjekte edilir ve <see cref="RunViewModel.TickElapsed"/> elle
/// çağrılır (üretimde MainWindow'un 200ms'lik <c>DispatcherTimer</c>'ı çağırır).</para>
/// </summary>
public sealed class EngineSilenceWatchdogTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private sealed class FakeClock
    {
        private long _now = 1_000; // 0 DEĞİL: "hiç sinyal alınmadı" ile "t=0'da sinyal" karışmasın
        public long Now => _now;
        public void Advance(long ms) => _now += ms;
    }

    private static (RunViewModel Vm, FakeClock Clock) NewVm(EngineHost engine)
    {
        var clock = new FakeClock();
        return (new RunViewModel(engine, NeverTickingBatcher(), () => "r1", () => clock.Now) { RootPath = @"D:\repo" },
                clock);
    }

    [Fact]
    public async Task A_silent_engine_while_a_run_is_starting_offers_the_escape_hatch()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var (vm, clock) = NewVm(engine);

        vm.IsStarting = true; // Build gönderildi — motor runStarted'a kadar konuşacak
        clock.Advance(RunViewModel.EngineSilenceThresholdMs - 1);
        vm.TickElapsed();
        Assert.Null(vm.EngineOverdueMessage); // eşiğin ALTI: planlama uzun sürüyor olabilir, bu normaldir

        clock.Advance(1);
        vm.TickElapsed();

        Assert.Equal("Engine has stopped responding — no reply for 1m 30s · you can restart it",
                     vm.EngineOverdueMessage);
        Assert.True(vm.IsStarting); // OTOMATİK açma YOK: motor hâlâ koşuyor olabilir
    }

    [Fact]
    public async Task A_silent_engine_while_stopping_offers_the_escape_hatch()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var (vm, clock) = NewVm(engine);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.Phase = AppPhase.Stopping;

        clock.Advance(RunViewModel.EngineSilenceThresholdMs);
        vm.TickElapsed();

        Assert.NotNull(vm.EngineOverdueMessage);
        Assert.Equal(AppPhase.Stopping, vm.Phase); // faz DEĞİŞMEZ — drain hâlâ meşru olabilir
    }

    /// <summary>[DEĞİŞEN KURAL — Sync penceresi] Motor Sync ORTASINDA donarsa da çıkış kapısı gösterilir.
    /// Eskiden bu pencere kapsam dışıydı: şerit sonsuza dek "▸ Sync — git fetch origin…" der, "Restart
    /// engine" hiç görünmezdi.</summary>
    [Fact]
    public async Task A_silent_engine_during_a_sync_offers_the_escape_hatch()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var (vm, clock) = NewVm(engine);
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main")); // Sync uçuşta — motor syncCompleted'a kadar konuşmalı
        Assert.Equal(AppPhase.Syncing, vm.Phase);             // ön-koşul

        clock.Advance(RunViewModel.EngineSilenceThresholdMs - 1);
        vm.TickElapsed();
        Assert.Null(vm.EngineOverdueMessage); // eşiğin ALTI: büyük bir fetch sürüyor olabilir

        clock.Advance(1);
        vm.TickElapsed();

        Assert.Equal("Engine has stopped responding — no reply for 1m 30s · you can restart it",
                     vm.EngineOverdueMessage);
        Assert.Equal(AppPhase.Syncing, vm.Phase); // OTOMATİK açma YOK — kapıyı kullanmak kullanıcının kararı
    }

    /// <summary>Sync BİTİNCE uyarı kendiliğinden kalkar (pencere kapanır) — kalıcı bir hata modu değildir.</summary>
    [Fact]
    public async Task The_sync_warning_clears_when_the_sync_completes()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var (vm, clock) = NewVm(engine);
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        clock.Advance(RunViewModel.EngineSilenceThresholdMs);
        vm.TickElapsed();
        Assert.NotNull(vm.EngineOverdueMessage); // ön-koşul

        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 0, 0));
        vm.TickElapsed();

        Assert.Null(vm.EngineOverdueMessage);
    }

    /// <summary>Sync'in ilerleme satırları da saati sıfırlar: yavaş ama KONUŞAN bir Sync (büyük fetch)
    /// asla uyarı üretmez.</summary>
    [Fact]
    public async Task Sync_progress_lines_keep_resetting_the_silence_clock()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var (vm, clock) = NewVm(engine);
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));

        for (int i = 0; i < 5; i++)
        {
            clock.Advance(RunViewModel.EngineSilenceThresholdMs - 1);
            vm.OnEvent(new SyncProgressEvent("git fetch origin main", "dim"));
            vm.TickElapsed();
            Assert.Null(vm.EngineOverdueMessage);
        }

        clock.Advance(RunViewModel.EngineSilenceThresholdMs);
        vm.TickElapsed();
        Assert.NotNull(vm.EngineOverdueMessage); // ...ama gerçekten susarsa yakalanır
    }

    /// <summary>[Sync penceresi — saat kurma pini] Uzun süre boşta duran bir uygulamada basılan Sync ANINDA
    /// "cevap vermiyor" DEMEZ: bekleyiş tıklamayla başlar, bu yüzden <c>SyncAsync</c> saati gönderimden önce
    /// kurar (<c>ArmEngineWatchdog</c>). Kurulmasaydı, saat son motor event'inden beri bayat olduğu için
    /// uyarı ilk tick'te patlardı. Gözlem noktası gönderim ANIdır: istek bayrağı o an açıktır.</summary>
    [Fact]
    public async Task Pressing_sync_after_a_long_idle_does_not_raise_an_instant_false_alarm()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var (vm, clock) = NewVm(engine);
        clock.Advance(RunViewModel.EngineSilenceThresholdMs * 10); // uygulama uzun süre boşta durdu

        string? overdueAtSendTime = null;
        vm.DebugOnCommandSent = c =>
        {
            if (c is not SyncWorkspaceCommand) return;
            vm.TickElapsed(); // tam bu anda: istek uçuşta, motor henüz cevap vermedi
            overdueAtSendTime = vm.EngineOverdueMessage;
        };

        await vm.SyncCommand.ExecuteAsync(null);

        Assert.Null(overdueAtSendTime); // bekleyiş TAM BURADA başladı — bayat saatten yanlış alarm YOK
    }

    /// <summary>Motorun HERHANGİ bir event'i saati sıfırlar: uzun ama KONUŞAN bir planlama/drain asla
    /// uyarı üretmez. Sinyal "yaşıyor mu" değil "susuyor mu" olduğu için event TÜRÜ önemsizdir.</summary>
    [Fact]
    public async Task Any_engine_event_resets_the_silence_clock()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var (vm, clock) = NewVm(engine);
        vm.IsStarting = true;

        for (int i = 0; i < 5; i++)
        {
            clock.Advance(RunViewModel.EngineSilenceThresholdMs - 1);
            vm.OnEvent(new PlanProgressEvent("Scanning solutions (12)"));
            vm.TickElapsed();
            Assert.Null(vm.EngineOverdueMessage);
        }

        clock.Advance(RunViewModel.EngineSilenceThresholdMs);
        vm.TickElapsed();
        Assert.NotNull(vm.EngineOverdueMessage); // ...ama sonunda gerçekten susarsa yakalanır
    }

    /// <summary>Geçiş tamamlanınca uyarı KENDİLİĞİNDEN kalkar: <c>runStarted</c> hem saati sıfırlar hem de
    /// watchdog'u kurulu tutan pencereyi kapatır (<c>IsStarting</c> düşer).</summary>
    [Fact]
    public async Task The_warning_clears_when_the_engine_finally_answers()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var (vm, clock) = NewVm(engine);
        vm.IsStarting = true;
        clock.Advance(RunViewModel.EngineSilenceThresholdMs);
        vm.TickElapsed();
        Assert.NotNull(vm.EngineOverdueMessage); // ön-koşul

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.TickElapsed();

        Assert.Null(vm.EngineOverdueMessage);
        Assert.Equal(AppPhase.Running, vm.Phase);
    }

    /// <summary>Koşan bir run'da watchdog KURULU DEĞİLDİR: tek bir projenin derlenmesi dakikalarca sürebilir
    /// ve o pencerede sessizlik meşrudur. Burada uyarmak, kullanıcıyı sağlıklı bir build'in ortasında
    /// motoru öldürmeye davet ederdi.</summary>
    [Fact]
    public async Task A_quiet_but_healthy_run_never_raises_the_warning()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var (vm, clock) = NewVm(engine);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        Assert.Equal(AppPhase.Running, vm.Phase); // ön-koşul

        clock.Advance(RunViewModel.EngineSilenceThresholdMs * 10);
        vm.TickElapsed();

        Assert.Null(vm.EngineOverdueMessage);
    }

    /// <summary>Kilit penceresi dışında (Idle/Done/Stopped) sessizlik zaten normaldir — motorun söyleyecek
    /// bir şeyi yoktur.</summary>
    [Fact]
    public async Task An_idle_app_never_raises_the_warning()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var (vm, clock) = NewVm(engine);
        vm.OnEvent(new WorkspaceTopologyEvent([], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 0, 0));

        clock.Advance(RunViewModel.EngineSilenceThresholdMs * 10);
        vm.TickElapsed();

        Assert.Null(vm.EngineOverdueMessage);
    }

    /// <summary>
    /// Kapının GERÇEKTEN açtığı şey: <c>RestartEngineAsync</c> kilitli run state'ini serbest bırakmalıdır.
    /// <para><b>Neden ayrı bir yol gerekiyor:</b> <c>EngineHost.RestartAsync</c> önce <c>_generation</c>'ı
    /// artırır (eski exit-watcher susturulur) ve ancak sonra child'ı öldürür — yani YAŞAYAN bir motoru
    /// yeniden başlatmak <c>EngineExited</c> ATEŞLEMEZ. Eskiden run state'ini yalnız <c>OnEngineExited</c>
    /// temizliyordu; donmuş (ama yaşayan) bir motorda Restart'a basmak şeridi temizler, kilidi AÇMAZDI.</para>
    /// </summary>
    [Fact]
    public async Task Restarting_the_engine_releases_the_locked_run_state()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var (vm, clock) = NewVm(engine);
        VmTopology.Seed(vm); // [topoloji kapısı] run komutlarının ön-koşulu — konu sessizlik kapısı
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.Phase = AppPhase.Stopping;
        clock.Advance(RunViewModel.EngineSilenceThresholdMs);
        vm.TickElapsed();
        Assert.NotNull(vm.EngineOverdueMessage); // ön-koşul: kapı görünür

        await vm.RestartEngineCommand.ExecuteAsync(null);

        Assert.Null(vm.EngineOverdueMessage);
        Assert.False(vm.IsRunning);
        Assert.False(vm.IsStarting);
        Assert.Equal(AppPhase.Stopped, vm.Phase); // kullanıcı zaten durmak istiyordu
        Assert.True(vm.BuildCommand.CanExecute(null));
    }
}
