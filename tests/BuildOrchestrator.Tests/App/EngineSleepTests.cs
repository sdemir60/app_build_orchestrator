using System.Windows;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.Tests.App.EngineSleepProbe;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 3 · spec §6.4 · Task 20] Boştaki motorun uykusu. Kural (<see cref="EngineIdleRule"/>) 200 ms'lik tikin pencere gizliyken
/// de koşan bölümünden, motor sessizlik bekçisinin yanında sorulur (<see cref="RunViewModel.TickElapsed"/>) ve doğruysa motor uyutulur.
/// Kuralın motor girdisi VM'in TEK motor durumudur (<see cref="RunViewModel.EngineState"/>): yalnız hazır motor uyur — açılışta hazır bildirimi
/// gelene dek hiç (C1), uyku sürerken ikinci kez değil (tek uçuş, C3). Son motor etkinliği tek damgadır (C8). Uykunun iki ters bitişi
/// (T18 yeniden incelemesinin eki): çıkışta vazgeçilen uyku sessizdir; başka bir nedenle düşen uyku motoru kaybettirir ve bugünkü ölüm
/// yolundan bildirilir. Uyandırma bu sınıfın değil Task 21'in işidir (<c>EngineWakeTests</c>).
///
/// <para><b>Test yüzeyleri:</b> saat testin elindedir (<c>nowMs</c>); uyku konağa gitmez — dikişin sahtesi (<see cref="EngineSleepProbe"/>, uyku ve
/// uyanmanın TEK sahtesi) sayar ve bitmiş, bekletilen (TCS: uyku sürüyor) ya da düşen bir görev döner, yani yarış pencereleri gerçek zamanlama
/// olmadan açık tutulur.
/// Tik üretimdeki gövdesiyle sürülür (<c>MainWindow.OnElapsedTick</c>). Fixture ayarı varsayılan KAPALI açar (<c>MainWindowHost.New</c>,
/// karar 9): uyuyan her test ayarı fixture döndükten sonra açıkça açar. Dikişin üretim varsayılanını (konağın uykusu) ve yeniden başlatmanın
/// beklemesini iki test gerçek motorla (izole önbellek) sınar.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class EngineSleepTests
{
    /// <summary>Üretime benzer saat: makine günlerdir açık — VM'in üretim saati açılıştan beri geçen süredir (<c>Environment.TickCount64</c>).</summary>
    private static readonly long LongUptimeMs = (long)TimeSpan.FromDays(3).TotalMilliseconds;

    // ---------------------------------------------------------------- açılış ve hazır yolu (C1)

    /// <summary>[C1] Tik motor başlamadan önce koşar (zamanlayıcı pencerenin ctor'unda kurulur, motor <c>Loaded</c>'da başlar) ve üretimin saati
    /// makinenin açık kalma süresidir: hiç olay gelmemişken "son etkinlikten beri" günler geçmiştir. Hazır bildirimi gelmeden kural motoru hiç
    /// uyutmaz — açılıştaki motoru uyutmak başlatmanın hazır bekleyişini düşürür ve açılışın Sync'i hiç gitmezdi.</summary>
    [StaFact]
    public void A_starting_engine_is_never_put_to_sleep_however_long_the_machine_has_been_up()
    {
        using var dir = new TempDir();
        long now = LongUptimeMs;
        var (window, vm) = MainWindowHost.New(dir, nowMs: () => now);
        vm.StopEngineWhenIdle = true;
        var sleep = Attach(vm);

        for (int i = 0; i < 3; i++)
        {
            window.OnElapsedTick();
            now += IdleAfterMs;
        }

        Assert.Equal(0, sleep.SleepCalls);
        Assert.Equal(EngineState.Starting, vm.EngineState);
        GC.KeepAlive(window);
    }

    /// <summary>[C1 · karar 13] Hazır bildirimi motor etkinliğidir: uzun bir açık kalma süresinden sonra hazır olan motorun beş dakikası O ANDA
    /// başlar. Workspace yoktur — açılış Sync'i gitmez, damgayı başka hiçbir şey yazmaz. Damga hazır olayının olay pompasından geçişini
    /// beklemez: kabuğun hazır yolu ile pompanın sırası garanti değildir ve tik ikisinin arasına düşerse kural hiç yazılmamış damgayı okur,
    /// motoru hazır olur olmaz uyuturdu.</summary>
    [StaFact]
    public void An_engine_ready_after_a_long_uptime_starts_its_idle_window_then()
    {
        using var dir = new TempDir();
        long now = LongUptimeMs;
        var (window, vm) = MainWindowHost.New(dir, nowMs: () => now);
        vm.StopEngineWhenIdle = true;
        var sleep = Attach(vm);
        Assert.False(vm.HasWorkspace); // ön-koşul: açılış Sync'i yok

        vm.OnEngineReady(EngineVersion, Pid);
        window.OnElapsedTick();
        Assert.Equal(0, sleep.SleepCalls);

        now += IdleAfterMs - 1;
        window.OnElapsedTick();
        Assert.Equal(0, sleep.SleepCalls);

        now += 1;
        window.OnElapsedTick();
        Assert.Equal(1, sleep.SleepCalls);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- kural (karar 16)

    /// <summary>[karar 16] Beş işsiz dakika motoru BİR kez uyutur: pencere son motor etkinliğiyle (koşunun olayları) başlar, bir ms eksiği
    /// uyutmaz, dolduğu tik uyutur, sonraki tikler uyuyan motora dokunmaz. Uyku konsola satır yazmaz. VM uyuyan motorun PID'ini tutmaz —
    /// ölü bir süreç canlıymış gibi görünmez; sürüm kalır (uyanan motor aynı ikilidir).</summary>
    [StaFact]
    public void Five_idle_minutes_put_the_engine_to_sleep_once()
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm, sleep) = ReadyShell(dir, () => now);
        Assert.Equal(Pid, vm.EnginePid); // ön-koşul: hazır yolunun kimliği

        now += 60_000;
        MainWindowHost.RunBuild(vm, "A"); // son motor etkinliği: koşunun olayları (şimdi)
        now += IdleAfterMs - 1;
        window.OnElapsedTick();
        Assert.Equal(0, sleep.SleepCalls);
        string console = vm.GetRunDocumentText();

        now += 1;
        window.OnElapsedTick();
        Assert.Equal(1, sleep.SleepCalls);
        Assert.Equal(EngineState.Asleep, vm.EngineState);
        Assert.Equal(console, vm.GetRunDocumentText());
        Assert.Null(vm.EnginePid);
        Assert.Equal(EngineVersion, vm.EngineVersion);

        now += IdleAfterMs;
        window.OnElapsedTick();
        window.OnElapsedTick();
        Assert.Equal(1, sleep.SleepCalls);
        GC.KeepAlive(window);
    }

    /// <summary>Ayar kapalıyken motor ne kadar işsiz kalırsa kalsın uyutulmaz. Aynı motor ayar açılınca ilk tikte uyur: testteki tek engel
    /// ayardır (vakum karşıtı).</summary>
    [StaFact]
    public void The_setting_off_keeps_the_engine_running()
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm, sleep) = ReadyShell(dir, () => now, stopWhenIdle: false);
        Assert.False(vm.StopEngineWhenIdle); // fixture'ın varsayılanı (karar 9)

        now += IdleAfterMs * 10;
        window.OnElapsedTick();
        Assert.Equal(0, sleep.SleepCalls);

        vm.StopEngineWhenIdle = true;
        window.OnElapsedTick();
        Assert.Equal(1, sleep.SleepCalls);
        GC.KeepAlive(window);
    }

    /// <summary>[C3 · karar 15] Uyku tek uçuşludur: konağın uykusu (shutdown, en çok iki saniyelik zarif çıkış, gerekirse öldürme) sürerken
    /// 200 ms'lik tik kuralı defalarca sorar; uyku bir GEÇİŞTİR ve kural geçiş sürerken uyutmaz. Uyku bitince motor uyur ve tik ona dokunmaz.
    /// Sahte uyku bekletilen bir görevdir: pencere gerçek bir yarış olmadan açık tutulur.</summary>
    [StaFact]
    public void A_tick_during_a_sleep_in_flight_does_not_start_a_second_one()
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm, sleep) = ReadyShell(dir, () => now);
        var sleeping = new TaskCompletionSource();
        sleep.NextSleep = () => sleeping.Task;

        now += IdleAfterMs;
        window.OnElapsedTick();
        Assert.Equal(1, sleep.SleepCalls);
        for (int i = 0; i < 5; i++)
        {
            now += 200;
            window.OnElapsedTick();
        }
        Assert.Equal(1, sleep.SleepCalls);
        Assert.Equal(EngineState.Sleeping, vm.EngineState);

        sleeping.SetResult();
        DispatcherPump.DrainToIdle(); // uykunun devamı (bağlam yakalandıysa dispatcher'da) koşsun
        Assert.Equal(EngineState.Asleep, vm.EngineState);
        now += IdleAfterMs;
        window.OnElapsedTick();
        Assert.Equal(1, sleep.SleepCalls);
        GC.KeepAlive(window);
    }

    /// <summary>Uçuştaki bir işlem uykuyu bekletir: koşu sürerken (<c>runCompleted</c>'a dek) motor ne kadar sessiz kalırsa kalsın uyutulmaz —
    /// sessiz ama sağlıklı bir derleme dakikalarca tek satır yazmayabilir. Girdi VM'in tek tanımıdır (<c>IsOperationInFlight</c>: bellek
    /// triminin ve kendiliğinden Sync'in sorusu). Koşu bitince pencere onun son olayından başlar ve dolunca motor uyur.</summary>
    [StaFact]
    public void An_operation_in_flight_keeps_the_engine_awake()
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm, sleep) = ReadyShell(dir, () => now);

        MainWindowHost.StartBuild(vm, "A");
        Assert.True(vm.IsOperationInFlight); // ön-koşul: koşu uçuşta
        now += IdleAfterMs * 3;
        window.OnElapsedTick();
        Assert.Equal(0, sleep.SleepCalls);

        MainWindowHost.FinishBuild(vm, "A"); // son motor etkinliği
        now += IdleAfterMs;
        window.OnElapsedTick();
        Assert.Equal(1, sleep.SleepCalls);
        GC.KeepAlive(window);
    }

    /// <summary>Cevabı beklenen bir proje log yüklemesi uykuyu bekletir: istek motora gitti, cevabı gelmedi — motoru o arada uyutmak cevabı
    /// kaybettirirdi. İstek damgayı yazmaz (bekleyiş penceresi kurmaz; cevabı chunk olarak damgalanır), yani kuralı tutan
    /// <c>HasPendingLogLoad</c> girdisidir. Cevap gelince pencere ondan başlar.</summary>
    [StaFact]
    public void A_pending_log_load_keeps_the_engine_awake()
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm, sleep) = ReadyShell(dir, () => now);
        string projectId = MainWindowHost.IdOf("A");

        var (load, request) = ProjectLogIpc.Request(vm, projectId);
        Assert.True(vm.HasPendingLogLoad); // ön-koşul: cevap bekleniyor
        now += IdleAfterMs * 3;
        window.OnElapsedTick();
        Assert.Equal(0, sleep.SleepCalls);

        vm.OnEvent(ProjectLogIpc.NotFound(request, projectId)); // cevap — son motor etkinliği
        Assert.False(vm.HasPendingLogLoad);
        now += IdleAfterMs;
        window.OnElapsedTick();
        Assert.Equal(1, sleep.SleepCalls);
        GC.KeepAlive(load);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- motor durumu (karar 14)

    /// <summary>Kendiliğinden ölmüş (toparlamak "Restart engine"in işi) ya da hiç doğamamış (kurulum) motor uyutulmaz: uyutulacak süreç yoktur.
    /// VM'in motor durumu şeridin bayraklarıyla aynı şeyi söyler — ölüm yeniden başlatılabilir bir hatadır, erişilemezlik kalıcıdır.</summary>
    [StaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_dead_or_missing_engine_is_not_put_to_sleep(bool died)
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm, sleep) = ReadyShell(dir, () => now);

        if (died) vm.OnEngineExited(1);
        else vm.OnEngineUnavailable(@"C:\app\supervisor\BuildOrchestrator.Supervisor.exe");
        Assert.Equal(died ? EngineState.Dead : EngineState.Unavailable, vm.EngineState);
        Assert.NotNull(vm.EngineDiedMessage);
        Assert.Equal(!died, vm.IsEngineUnavailable);

        now += IdleAfterMs * 3;
        window.OnElapsedTick();
        Assert.Equal(0, sleep.SleepCalls);
        GC.KeepAlive(window);
    }

    /// <summary>[T20 M6 · defter 9 · PİN] Bilinen ayrışma (1) — güvenli yönde: ölü motorun borusunda kalmış bir <c>runStarted</c> ölüm metnini
    /// temizler (şerit artık ölümü göstermez) ama durumu Ready'ye ÇEKMEZ — Ready yalnız hazır yolundan gelir. Durum en kötü "ölü" kalır: koşu
    /// bitip beş işsiz dakika fazlasıyla geçse de boşta kuralı ölü motoru uyutmaz. PİN: kırmızısı gösterilemez — ayrışmanın yönünü tutar;
    /// <c>OnRunStarted</c> durumu Ready'ye çekseydi kural, süreci olmayan bir motoru "uyutmaya" kalkardı.</summary>
    [StaFact]
    public void A_run_started_on_a_dead_engine_clears_the_death_text_but_never_lets_the_rule_sleep_it()
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm, sleep) = ReadyShell(dir, () => now);
        vm.OnEngineExited(1);
        Assert.Equal(EngineState.Dead, vm.EngineState); // ön-koşul

        MainWindowHost.StartBuild(vm, "A"); // ölen motorun borusunda kalmış runStarted
        Assert.Null(vm.EngineDiedMessage);  // ayrışma: ölüm metni temizlendi...
        Assert.Equal(EngineState.Dead, vm.EngineState); // ...durum Ready'ye çekilmedi
        MainWindowHost.FinishBuild(vm, "A");
        Assert.False(vm.IsOperationInFlight); // ön-koşul: kuralı tutan tek şey motor durumu
        now += IdleAfterMs * 3;
        window.OnElapsedTick();

        Assert.Equal(0, sleep.SleepCalls);
        Assert.Equal(EngineState.Dead, vm.EngineState);
        GC.KeepAlive(window);
    }

    /// <summary>Uyku sürerken motor kendiliğinden ölürse ölüm kazanır: konakta ölüm hakkını bekçi aldıysa uyku hiçbir şey yapmadan döner ve
    /// ölüm sinyali gelir. Uyku bitince durum "uyuyor"a dönmez — şerit ölüm satırını ve "Restart engine"i gösterir, kural ölü motoru bir daha
    /// uyutmaya çalışmaz.</summary>
    [StaFact]
    public void A_death_during_the_sleep_wins_over_it()
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm, sleep) = ReadyShell(dir, () => now);
        var sleeping = new TaskCompletionSource();
        sleep.NextSleep = () => sleeping.Task;
        now += IdleAfterMs;
        window.OnElapsedTick();
        Assert.Equal(1, sleep.SleepCalls); // ön-koşul: uyku uçuşta

        vm.OnEngineExited(1);
        sleeping.SetResult();
        DispatcherPump.DrainToIdle();

        Assert.Equal(EngineState.Dead, vm.EngineState);
        Assert.NotNull(vm.EngineDiedMessage);
        now += IdleAfterMs;
        window.OnElapsedTick();
        Assert.Equal(1, sleep.SleepCalls);
        GC.KeepAlive(window);
    }

    /// <summary>[spec §6.4] Pencere tepsideyken de motor uyur: kural tikin gizliyken de koşan bölümünden — sessizlik bekçisiyle birlikte —
    /// sorulur. Tepside saatlerce duran uygulama boştaki belleğin asıl yeridir.</summary>
    [StaFact]
    public void A_hidden_window_still_puts_the_engine_to_sleep()
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm, sleep) = ReadyShell(dir, () => now);
        window.SetSurfaceHidden(true);

        now += IdleAfterMs;
        window.OnElapsedTick();

        Assert.Equal(1, sleep.SleepCalls);
        Assert.Equal(EngineState.Asleep, vm.EngineState);
        GC.KeepAlive(window);
    }

    /// <summary>[T18 yeniden incelemesi · ek 2] Çıkışta uykudan vazgeçilirse sessiz kalınır: dispose uçuştaki uykunun önüne geçer
    /// (<see cref="OperationCanceledException"/>). Konsola satır yazılmaz, şerit ölüm göstermez (ölen yok: uygulama kapanıyor ve motoru kendi
    /// kapanışı alıyor). Durum "uyuyor"dur: motorun nesli uykuda ve dispose'ta emekliye ayrılmıştır, konuşacak süreç yoktur — "hazır" demek ölü
    /// bir süreci canlı saymak olurdu; tik vazgeçen uykuyu her 200 ms'de yeniden denemez. [T20 incelemesi M2] <see cref="ObjectDisposedException"/>
    /// satırı SAVUNMA dalını sınar: konak dispose'tan sonra gelen bir uykuyu fırlatmadan no-op yapar (<c>EngineHost.SleepAsync</c>), yani bu
    /// istisna bugün üretimde gelmez — gelirse dal yine sessiz kalmalıdır.</summary>
    [StaTheory]
    [InlineData(true)]  // dispose uçuştaki uykunun önüne geçti
    [InlineData(false)] // savunma: ObjectDisposedException (konak dispose sonrası uykuyu fırlatmadan no-op yapar)
    public void A_sleep_given_up_at_exit_writes_nothing_and_reports_no_death(bool cancelled)
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm, sleep) = ReadyShell(dir, () => now);
        sleep.NextSleep = () => cancelled
            ? Task.FromCanceled(new CancellationToken(canceled: true))
            : Task.FromException(new ObjectDisposedException(nameof(EngineHost)));
        string console = vm.GetRunDocumentText();

        now += IdleAfterMs;
        window.OnElapsedTick();
        DispatcherPump.DrainToIdle();
        Assert.Equal(1, sleep.SleepCalls);
        Assert.Equal(console, vm.GetRunDocumentText());
        Assert.Null(vm.EngineDiedMessage);
        Assert.Equal(Visibility.Collapsed, window.Shell.Ribbon.RestartEngineAction.Visibility);

        now += IdleAfterMs;
        window.OnElapsedTick();
        Assert.Equal(1, sleep.SleepCalls);
        Assert.Equal(EngineState.Asleep, vm.EngineState);
        GC.KeepAlive(window);
    }

    /// <summary>[T18 yeniden incelemesi · ek 1] Uyku başka bir nedenle düşerse (ör. öldürme fırlattı) motor KAYBEDİLMİŞTİR: konak nesli
    /// emekliye ayırdıktan sonra düştü — ölüm sinyali hiç gelmeyecek, konak ne uyuyor ne canlı (uyandırma ve gönderim "Engine is not running."
    /// ile düşer). Bunu bugünkü ölüm yolu gösterir, ikinci bir kopyası değil: durum <see cref="EngineState.Dead"/>, şeritte kırmızı ölüm satırı
    /// ve "Restart engine". Teori iki kaynağı AYNI iddialardan geçirir — kendiliğinden ölüm (bugünkü yol, kontrol) ve düşen uyku: ikisi de
    /// konsola satır yazmaz (ölüm yolu boştayken yazmaz), ikisi de bir kez bildirilir (tik düşen uykuyu yeniden denemez).</summary>
    [StaTheory]
    [InlineData(true)]  // düşen uyku
    [InlineData(false)] // kendiliğinden ölüm — bugünkü yol (kontrol)
    public void A_failed_sleep_reports_the_engine_lost_through_the_death_path(bool sleepFailed)
    {
        using var dir = new TempDir();
        long now = 1_000;
        var (window, vm, sleep) = ReadyShell(dir, () => now);
        sleep.NextSleep = () => Task.FromException(new InvalidOperationException("the kill failed"));
        string console = vm.GetRunDocumentText();
        now += IdleAfterMs;

        if (sleepFailed) window.OnElapsedTick();
        else vm.OnEngineExited(1);
        DispatcherPump.DrainToIdle();

        Assert.Equal(sleepFailed ? 1 : 0, sleep.SleepCalls);
        Assert.Equal(EngineState.Dead, vm.EngineState);
        Assert.True(vm.EngineRestartable);
        Assert.False(vm.IsEngineUnavailable);
        Assert.Equal(sleepFailed ? "Engine stopped unexpectedly (idle shutdown failed)" : "Engine stopped unexpectedly (exit 1)",
            vm.EngineDiedMessage);
        Assert.Equal(vm.EngineDiedMessage, window.Shell.Ribbon.PhaseText.Text);
        Assert.Equal(Visibility.Visible, window.Shell.Ribbon.RestartEngineAction.Visibility);
        Assert.Equal(console, vm.GetRunDocumentText());

        now += IdleAfterMs;
        window.OnElapsedTick();
        Assert.Equal(sleepFailed ? 1 : 0, sleep.SleepCalls);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- gerçek motor (izole önbellek)

    /// <summary>[karar 13] "Restart engine" bir başlatmadır: yeni motor hazır olana dek durum <see cref="EngineState.Starting"/>'tir ve kural onu
    /// uyutmaz — hazır bir motordan başlatılsa ve pencere çoktan dolmuş olsa bile (komut her an çalıştırılabilir). Yeni motor hazır olunca
    /// pencere o andan başlar. Gerçek motor: yeniden başlatmanın beklemesi ancak böyle gerçekten açıktır.</summary>
    [Fact]
    public async Task A_restart_in_flight_is_not_put_to_sleep()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = sandbox.IsolatedEngineHost(TestPaths.WideStartupTimeout);
        long now = 1_000;
        var vm = RealEngineVm(engine, () => now); // workspace yok: hazır yolu Sync göndermez
        var sleep = Attach(vm);
        vm.OnEngineReady(EngineVersion, Pid);
        now += IdleAfterMs; // pencere doldu: yeniden başlatma olmasa bu an uyuturdu

        var restart = vm.RestartEngineCommand.ExecuteAsync(null);
        Assert.False(restart.IsCompleted); // ön-koşul: yeni motor henüz hazır değil
        vm.TickElapsed();
        Assert.Equal(0, sleep.SleepCalls);
        Assert.Equal(EngineState.Starting, vm.EngineState);

        await restart;
        Assert.Equal(EngineState.Ready, vm.EngineState);
        vm.TickElapsed();
        Assert.Equal(0, sleep.SleepCalls); // pencere yeni motorun hazır anından başlar
        now += IdleAfterMs;
        vm.TickElapsed();
        Assert.Equal(1, sleep.SleepCalls);
    }

    /// <summary>Dikişin üretim varsayılanı konağın uykusudur (<see cref="EngineHost.SleepAsync"/>): kural doğru dediğinde GERÇEK motor durur —
    /// konak uyur, süreci yoktur. Sahte uykulu testler kararı ve VM'in durumunu sınar; bu test dikişin gerçekten konağa gittiğini pinler.</summary>
    [Fact]
    public async Task The_idle_rule_puts_the_real_engine_to_sleep()
    {
        using var sandbox = new SupervisorSandbox();
        await using var engine = sandbox.IsolatedEngineHost(TestPaths.WideStartupTimeout);
        var ready = await engine.StartAsync();
        long now = 1_000;
        var vm = RealEngineVm(engine, () => now); // workspace yok: hazır yolu Sync göndermez
        Task? sleeping = null;
        var production = vm.SleepEngine;
        vm.SleepEngine = () => sleeping = production(); // yalnız gözlem: görevi testin beklemesi için tutar
        vm.OnEngineReady(ready.EngineVersion, ready.Pid);

        now += IdleAfterMs;
        vm.TickElapsed();
        Assert.NotNull(sleeping);
        await sleeping.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(engine.IsAsleep);
        Assert.Null(engine.EnginePid);
    }
}
