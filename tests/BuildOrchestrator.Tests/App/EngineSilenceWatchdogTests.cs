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
/// <para><b>Neden yalnız iki pencere:</b> watchdog SADECE bir geçiş beklenirken (<c>IsStarting</c> ya da faz
/// <c>Stopping</c>) kuruludur. Koşan bir run'da uzun ve sessiz bir derleme adımı meşrudur; orada uyarmak
/// yanlış alarm üretirdi. <b>Watchdog hiçbir şeyi OTOMATİK açmaz</b> — yalnız çıkış kapısını gösterir:
/// graceful drain dakikalarca sürebilir ve kilidi kendiliğinden açmak, hâlâ koşan bir motora ikinci bir run
/// başlatmaya izin vermek demekti.</para>
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
