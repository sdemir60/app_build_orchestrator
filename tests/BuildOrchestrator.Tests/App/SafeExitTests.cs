using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [P3 · Task 2 · Exit] Tam çıkış (<see cref="RunViewModel.RequestExit"/>) <b>uçuştaki işi durdurur ve kapanır</b>: uçuşta iş
/// yoksa çıkış HEMEN hazırdır; bir derleme koşuyorsa hard stop gider (uçuştaki derlemeler sonlandırılır) ve çıkış motorun
/// <c>runStopped</c>'ını en çok <see cref="RunViewModel.ExitStopWaitMs"/> bekler; Sync/Deep Clean/Optimize beklenmez (iptalleri yok,
/// kapanış motoru ve çocuklarını outer job'la bitirir); git'in çalışma ağacına yazdığı checkout/pull beklenir, bitişlerine
/// zincirlenen Sync ise bekleyişte başlamaz.
/// Bekleyiş sonsuz DEĞİLDİR: motor susarsa (sessizlik bekçisi) ya da ölürse çıkış serbest kalır. İkinci istek ikinci
/// bir durdurma ya da satır üretmez; bekleyişte kendiliğinden Sync başlamaz.
///
/// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-19 · Exit butonu]</b> ESKİ İDDİA: tam çıkış uçuştaki işi BEKLER — derleme
/// graceful durdurulur ve drain beklenir, Sync/Clean/Optimize/checkout/pull sonuna kadar koşar; çıkış hard stop'a asla
/// tırmanmaz. GEREKÇE: kullanıcı "Exit dediğimde her şey kill olacak" dedi; tasarımın onay kartı "4 projects in flight stay
/// unbuilt" der. Kesilen derlemenin güvencesi motorun in-flight defteridir (ARCHITECTURE §8.7): yarıda kalan proje bir sonraki
/// açılışta kanıtsız hata sayılır ve sonraki Build onu baştan derler. Bekleyişin kaldığı tek yer git yazımıdır (checkout/pull —
/// kullanıcı kararı aynı gün): yarıda kesilen bir checkout ağacı yarı değiştirilmiş ve <c>index.lock</c>'lu bırakırdı.</para>
///
/// <para>Harness VM düzeyidir: motor başlatılmaz, gönderim <see cref="MainWindowHost.AcceptSends"/> ile kabul edilir,
/// giden komutlar <c>DebugOnCommandSent</c> ile izlenir ve motorun cevabı <c>vm.OnEvent(...)</c> ile verilir. D8:
/// saat enjekte edilir, gerçek bekleme yok.</para>
/// </summary>
public sealed class SafeExitTests
{
    private const string Root = @"D:\repo";

    /// <summary>VM + gönderilen komutlar + <see cref="RunViewModel.ExitReady"/> sayacı.</summary>
    private sealed class Harness
    {
        public RunViewModel Vm { get; }
        public List<IpcCommand> Sent { get; } = [];
        public int Ready { get; private set; }

        public Harness(string root = Root, Func<long>? nowMs = null)
        {
            Vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), MainWindowHost.NeverTickingBatcher(),
                () => "r1", nowMs) { RootPath = root };
            MainWindowHost.AcceptSends(Vm);
            Vm.DebugOnCommandSent = Sent.Add;
            Vm.ExitReady += (_, _) => Ready++;
        }

        /// <summary>Konsolda <see cref="RunViewModel.ExitPendingLine"/> kaç kez yazıldı.</summary>
        public int ExitLines => Vm.GetRunDocumentText().Split('\n').Count(l => l == RunViewModel.ExitPendingLine);
    }

    // ---------------------------------------------------------------- uçuşta iş yok

    /// <summary>Uçuşta hiçbir iş yokken çıkış ANINDA hazırdır: bekleyiş açılmaz, konsola satır düşmez, motora komut
    /// gitmez. İkinci istek ikinci bir atım üretmez.</summary>
    [Fact]
    public void An_exit_with_nothing_in_flight_is_ready_at_once()
    {
        var h = new Harness();

        h.Vm.RequestExit();

        Assert.Equal(1, h.Ready);
        Assert.False(h.Vm.ExitPending);
        Assert.Empty(h.Sent);
        Assert.Equal(0, h.ExitLines);

        h.Vm.RequestExit();
        Assert.Equal(1, h.Ready); // TEK atım
    }

    /// <summary>[idle-memory · dalga 3 · tek çıkış sorusu] Çıkış istendikten SONRA hiçbir Sync gönderilmez — uçuşta iş yokken istenen çıkışta da
    /// (bekleyiş hiç açılmaz, çıkış hemen hazırdır; pencere kapanana dek Sync düğmesi basılabilir). Sync'in kapısı motor kapısıyla AYNI soruyu
    /// sorar: "çıkış istendi" (<c>_exitRequested</c>) — iki ayrı çıkış sorusu yoktur.
    /// <para><b>[DEĞİŞEN KURAL]</b> ESKİ: Sync'in kapısı "çıkış bekliyor"u (<c>ExitPending</c>) soruyordu; uçuşta iş yokken istenen çıkış onu hiç
    /// açmadığı için ardından başlayan bir Sync kapanan uygulamanın motoruna gidiyordu (Sync + branch envanteri). GEREKÇE: motor kapısının R3
    /// kararı — çıkış istendikten sonra yeni iş başlamaz; bayrak bir tanedir (bekleyişte zincirlenen Sync'in kuralı değişmedi).</para>
    /// <para>KIRMIZI (değişiklikten önce): Sync ve envanter gönderiliyordu.</para></summary>
    [Fact]
    public async Task A_sync_started_after_an_exit_with_nothing_in_flight_is_not_sent()
    {
        var h = new Harness();
        h.Vm.RequestExit();
        Assert.Equal(1, h.Ready); // ön-koşul: uçuşta iş yoktu — çıkış hemen hazır, bekleyiş açılmadı
        Assert.False(h.Vm.ExitPending);

        await h.Vm.SyncCommand.ExecuteAsync(null);

        Assert.Empty(h.Sent);
        Assert.False(h.Vm.SyncBusy);
        Assert.Equal(1, h.Ready);
    }

    // ---------------------------------------------------------------- derleme

    /// <summary>Koşan bir derleme beklenmez, SONLANDIRILIR: çıkış TEK <see cref="StopRunCommand"/> gönderir ve o
    /// <see cref="StopKind.Hard"/>'tır (Stop'un "Stop now"ı — konsola onun satırı da düşer). Motor uçuştakileri raporlayıp
    /// <c>runStopped</c> yazana kadar <c>ExitReady</c> yoktur — durdurma motorun kendi kaydıyla biter. Ardından gelen
    /// <c>runCompleted</c> ikinci bir atım üretmez.
    /// <para><b>[DEĞİŞEN KURAL]</b> ESKİ: graceful stop gider, uçuştaki proje drain'de biter (bkz. tip özeti). KIRMIZI
    /// (değişiklikten önce): giden durdurma <see cref="StopKind.Graceful"/>'dı.</para></summary>
    [Fact]
    public void An_exit_during_a_build_terminates_it_and_waits_for_the_engine_to_report_the_stop()
    {
        var h = new Harness();
        MainWindowHost.StartBuild(h.Vm);
        h.Vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));

        h.Vm.RequestExit();

        Assert.Equal(new StopRunCommand("r1", StopKind.Hard), Assert.Single(h.Sent));
        Assert.Equal(AppPhase.Stopping, h.Vm.Phase);
        Assert.True(h.Vm.HardStopRequested);
        Assert.True(h.Vm.ExitPending);
        Assert.Equal(1, h.ExitLines);
        Assert.Contains(RunViewModel.StopNowRequestedLine, h.Vm.GetRunDocumentText());
        Assert.Equal(0, h.Ready);

        h.Vm.OnEvent(new ProjectFailedEvent("r1", @"C:\p\a.csproj", 300, FailureReasons.Stopped)); // sonlandırıldı
        Assert.Equal(0, h.Ready); // motor koşuyu henüz kapatmadı

        h.Vm.OnEvent(new RunStoppedEvent("r1", WasHard: true));
        Assert.Equal(1, h.Ready);

        h.Vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 0, 1, 0, 1, 400));
        Assert.Equal(1, h.Ready); // TEK atım
    }

    /// <summary>Kullanıcının kendi Stop'u drain'i beklerken istenen çıkış drain'i beklemez: hard stop'a TIRMANIR — ikinci
    /// <see cref="StopRunCommand"/> <see cref="StopKind.Hard"/>'tır.
    /// <para><b>[DEĞİŞEN KURAL]</b> ESKİ: çıkış hard stop'a asla tırmanmaz, drain'i bekler. KIRMIZI (değişiklikten önce):
    /// ikinci bir durdurma gitmiyordu.</para></summary>
    [Fact]
    public async Task An_exit_during_the_users_drain_escalates_to_a_hard_stop()
    {
        var h = new Harness();
        MainWindowHost.StartBuild(h.Vm);
        await h.Vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(new StopRunCommand("r1", StopKind.Graceful), Assert.Single(h.Sent)); // ön-koşul: kullanıcının Stop'u

        h.Vm.RequestExit();

        Assert.Equal(
            [new StopRunCommand("r1", StopKind.Graceful), new StopRunCommand("r1", StopKind.Hard)],
            h.Sent.OfType<StopRunCommand>());
        Assert.Equal(0, h.Ready);

        h.Vm.OnEvent(new RunStoppedEvent("r1", WasHard: true));
        Assert.Equal(1, h.Ready);
    }

    /// <summary>[inceleme K2] Kullanıcının graceful Stop'unun gönderimi henüz DÖNMEMİŞKEN istenen çıkış da hard stop'a tırmanır: karar Stop'un
    /// kendi kuralından okunur, komutun kapısından değil — <c>AsyncRelayCommand</c> önceki yürütmesi sürerken kapısını kapalı gösterir ve
    /// çıkış o pencerede hard'ı hiç göndermez, yalnız süresinin dolmasını beklerdi.
    /// <para>KIRMIZI (düzeltmeden önce): ikinci <c>stopRun</c> gitmiyordu.</para></summary>
    [Fact]
    public async Task An_exit_while_the_users_stop_is_still_being_sent_escalates_too()
    {
        var h = new Harness();
        MainWindowHost.StartBuild(h.Vm);
        var sending = new TaskCompletionSource();
        h.Vm.DebugSendOverride = _ => sending.Task;
        var stop = h.Vm.StopCommand.ExecuteAsync(null);
        Assert.False(stop.IsCompleted); // ön-koşul: graceful'un gönderimi askıda
        h.Vm.DebugSendOverride = _ => Task.CompletedTask;

        h.Vm.RequestExit();

        Assert.Equal(
            [new StopRunCommand("r1", StopKind.Graceful), new StopRunCommand("r1", StopKind.Hard)],
            h.Sent.OfType<StopRunCommand>());
        sending.SetResult();
        await stop;
    }

    /// <summary>Hard stop zaten gitmişse (Terminating…) çıkış ikinci bir durdurma göndermez; motorun cevabını bekler.</summary>
    [Fact]
    public async Task An_exit_after_the_users_hard_stop_sends_nothing_more()
    {
        var h = new Harness();
        MainWindowHost.StartBuild(h.Vm);
        await h.Vm.StopCommand.ExecuteAsync(null);
        await h.Vm.StopCommand.ExecuteAsync(null); // Stop now
        Assert.Equal(StopStage.Terminating, h.Vm.StopStage); // ön-koşul

        h.Vm.RequestExit();

        Assert.Equal(2, h.Sent.OfType<StopRunCommand>().Count());
        Assert.Equal(0, h.Ready);
    }

    /// <summary>Motor hard stop'a cevap vermezse (planlamada takılı, ya da cevap yolda) çıkış sessizlik eşiğini (90 sn) BEKLEMEZ:
    /// <see cref="RunViewModel.ExitStopWaitMs"/> dolunca gider — kapanış motoru ve kalan her şeyi outer job'la bitirir, yarıda kalan
    /// projeleri motorun in-flight defteri bir sonraki açılışta geçersizler. Sürenin altı beklenir.
    /// KIRMIZI (değişiklikten önce): sürenin bir sınırı yoktu, yalnız sessizlik eşiği bırakıyordu.</summary>
    [Fact]
    public void An_unanswered_stop_holds_the_exit_only_for_the_stop_wait()
    {
        long now = 1_000;
        var h = new Harness(nowMs: () => now);
        MainWindowHost.StartBuild(h.Vm);
        h.Vm.RequestExit();
        Assert.Equal(0, h.Ready); // ön-koşul: cevap bekleniyor

        now += RunViewModel.ExitStopWaitMs - 1;
        h.Vm.TickElapsed();
        Assert.Equal(0, h.Ready);

        now += 1;
        h.Vm.TickElapsed();
        Assert.Equal(1, h.Ready);
        Assert.Null(h.Vm.EngineOverdueMessage); // serbest bırakan bekçi değil, çıkışın kendi süresi
    }

    /// <summary>İkinci istek (ikinci ×, tepsi → Exit) tam no-op'tur: ikinci bir <c>stopRun</c> ya da ikinci bir konsol
    /// satırı üretmez.</summary>
    [Fact]
    public void A_second_exit_request_sends_no_second_stop_and_no_second_line()
    {
        var h = new Harness();
        MainWindowHost.StartBuild(h.Vm);

        h.Vm.RequestExit();
        h.Vm.RequestExit();

        Assert.Single(h.Sent.OfType<StopRunCommand>());
        Assert.Equal(1, h.ExitLines);
        Assert.Equal(0, h.Ready);
    }

    // ---------------------------------------------------------------- workspace işleri

    /// <summary>Çıkışın beklediği workspace işi.</summary>
    public enum WorkspaceJob { Sync, Clean, Optimize, Checkout, Pull }

    private static readonly BranchRef FeatureX = new("feature/x", "bbbbbbbbbbbb", IsActive: false, IsRemoteTracking: false);

    /// <summary>İşi üretimdeki girişinden başlatır ve (varsa) motorun başlangıç cevabını verir — iş uçuşta.</summary>
    private static async Task StartAsync(RunViewModel vm, WorkspaceJob job)
    {
        switch (job)
        {
            case WorkspaceJob.Sync:
                await vm.SyncCommand.ExecuteAsync(null);
                vm.OnEvent(new SyncStartedEvent(Root, "main"));
                break;
            case WorkspaceJob.Clean:
                await vm.CleanCommand.ExecuteAsync(null);
                vm.OnEvent(new CleanStartedEvent(Root));
                break;
            case WorkspaceJob.Optimize:
                await vm.OptimizeCommand.ExecuteAsync(null);
                vm.OnEvent(new OptimizeStartedEvent(Root));
                break;
            case WorkspaceJob.Checkout:
                vm.OnEvent(new BranchListEvent(
                    [new BranchRef("main", "aaaaaaaaaaaa", IsActive: true, IsRemoteTracking: false), FeatureX]));
                await vm.SelectBranch(FeatureX);
                break;
            case WorkspaceJob.Pull:
                vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0, Behind: 2)); // "N behind" chip'i görünür
                await vm.PullRepositoryCommand.ExecuteAsync(null);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(job), job, null);
        }
    }

    private static readonly SyncCompletedEvent SyncDone = new("main", "sha1234", false, 1, 0);

    /// <summary>Motorun işi bitirişi. Bekleyişte bitişe zincirlenen bir Sync başlamadığı için başka cevap yoktur.</summary>
    private static IpcEvent EndOf(WorkspaceJob job) => job switch
    {
        WorkspaceJob.Sync => SyncDone,
        WorkspaceJob.Clean => new CleanCompletedEvent(1, 2, 1024, 0, 1),
        WorkspaceJob.Optimize => new OptimizeCompletedEvent(ProjectCount: 1),
        WorkspaceJob.Checkout => new CheckoutCompletedEvent(CheckoutStatus.Switched, "main", "feature/x",
            "b7e91d4a0c1f2e3d4c5b6a7980716253443526a1", 0, null, null),
        WorkspaceJob.Pull => new PullCompletedEvent(Succeeded: true),
        _ => throw new ArgumentOutOfRangeException(nameof(job), job, null),
    };

    /// <summary>
    /// İptali olmayan, git'in çalışma ağacına yazmadığı workspace işi (Sync, Deep Clean, Optimize) BEKLENMEZ: çıkış aynı
    /// çağrıda hazırdır; motora durdurma gitmez — işi kapanışın kendisi (motor + outer job) bitirir. Bekleyiş yine de açılır
    /// ve konsola satır düşer: uçuşta bir iş VARDI ve kesildi, kayıt bunu söyler.
    /// <para><b>[DEĞİŞEN KURAL]</b> ESKİ: çıkış bu işleri de sonuna kadar bekliyordu (bkz. tip özeti). Gerekçeler: Sync'in
    /// fetch'i yarıda kesilirse en kötü ihtimalle bir ref kilidi kalır ve sonraki açılış zaten Sync'ler; Deep Clean önce durumu
    /// sonra klasörleri siler (en kötü sonuç fazladan bir derleme, ARCHITECTURE §5.2); Optimize'ın yarıda kalan restore'u bir
    /// sonraki Optimize'da tamamlanır. KIRMIZI (değişiklikten önce): <c>ExitReady</c> işin bitişini bekliyordu.</para>
    /// </summary>
    [Theory]
    [InlineData(WorkspaceJob.Sync)]
    [InlineData(WorkspaceJob.Clean)]
    [InlineData(WorkspaceJob.Optimize)]
    public async Task An_exit_does_not_wait_for_a_job_it_can_cut_off(WorkspaceJob job)
    {
        var h = new Harness();
        await StartAsync(h.Vm, job);

        h.Vm.RequestExit();

        Assert.True(h.Vm.ExitPending);
        Assert.Equal(1, h.Ready);
        Assert.Equal(1, h.ExitLines);
        Assert.Empty(h.Sent.OfType<StopRunCommand>());
    }

    /// <summary>
    /// Git'in çalışma ağacına yazdığı iş (checkout, pull) durdurulmaz ve kesilmez — çıkış onu BEKLER: iş sürerken
    /// <c>ExitReady</c> yoktur, iş bitince bir kez gelir. Motora durdurma gitmez; bitişine zincirlenen Sync de gitmez ve konsol
    /// temizlenmez (bekleyişin satırı yerinde kalır). Kesilseydi ağaç yarı değiştirilmiş ve <c>.git/index.lock</c>'lu kalırdı
    /// (kullanıcı kararı 2026-10-19).
    ///
    /// <para><b>[DEĞİŞEN KURAL — final review I2, kontrolcü kararı]</b> Eski iddia: checkout/pull'un bitişine zincirlenen
    /// Sync de beklenirdi — <c>ExitReady</c> zincirin <c>syncCompleted</c>'ında gelirdi. Değişme gerekçesi: bekleyiş yeni iş
    /// BAŞLATMAMALIDIR; bekleyişte hiçbir Sync başlamaz (<c>SyncCoreAsync</c>'in kapısı) ve zincir işin yüzeyini doğrudan
    /// bırakır.</para>
    /// </summary>
    [Theory]
    [InlineData(WorkspaceJob.Checkout)]
    [InlineData(WorkspaceJob.Pull)]
    public async Task An_exit_waits_for_a_git_write_to_finish(WorkspaceJob job)
    {
        var h = new Harness();
        await StartAsync(h.Vm, job);

        h.Vm.RequestExit();

        Assert.True(h.Vm.ExitPending);
        Assert.Equal(0, h.Ready); // iş hâlâ uçuşta

        h.Vm.OnEvent(EndOf(job));

        Assert.Equal(1, h.Ready);
        Assert.Empty(h.Sent.OfType<StopRunCommand>());
        Assert.Empty(h.Sent.OfType<SyncWorkspaceCommand>()); // zincirlenen Sync gitmez
        Assert.Equal(1, h.ExitLines); // zincirin konsol temizliği de yok
    }

    // ---------------------------------------------------------------- bekleyiş sonsuz değil

    /// <summary>Motor bir git yazımı sırasında susarsa (sessizlik bekçisi — <see cref="RunViewModel.EngineSilenceThresholdMs"/>)
    /// çıkış onu sonsuza dek beklemez. Eşiğin altı meşru bir bekleyiştir ve beklenir. Kilit açılmaz: çıkışı serbest bırakan
    /// bekçinin uyarısıdır, motorun cevabı değil.
    /// <para><b>[DEĞİŞEN KURAL]</b> ESKİ: senaryo bir derlemenin drain'iydi; derleme artık beklenmiyor
    /// (<see cref="An_unanswered_stop_holds_the_exit_only_for_the_stop_wait"/>) — bekçinin çıkışı bıraktığı yer git yazımıdır.</para></summary>
    [Fact]
    public async Task A_silent_engine_does_not_hold_the_exit()
    {
        long now = 1_000;
        var h = new Harness(nowMs: () => now);
        await StartAsync(h.Vm, WorkspaceJob.Checkout);
        h.Vm.RequestExit();
        Assert.True(h.Vm.CheckoutBusy); // ön-koşul: checkout bekleniyor — bekçinin penceresi

        now += RunViewModel.EngineSilenceThresholdMs - 1;
        h.Vm.TickElapsed();
        Assert.Equal(0, h.Ready);

        now += 1;
        h.Vm.TickElapsed();

        Assert.Equal(1, h.Ready);
        Assert.True(h.Vm.CheckoutBusy);
    }

    /// <summary>[final review · F1] Motor çıkış İSTENMEDEN ÖNCE sustuysa — kullanıcının Stop'u drain'i bekliyor ve
    /// bekçinin uyarısı çoktan şeritte — çıkış onu da beklemez: <see cref="RunViewModel.RequestExit"/> koşulu isteğin
    /// İÇİNDE yeniden değerlendirir ve <c>ExitReady</c> hemen gelir. Uyarı zaten açık olduğu için bekçi bir daha
    /// değişmez; o değerlendirme olmasaydı çıkış, susmuş bir motorun asla göndermeyeceği cevabı beklerdi.
    /// <para><b>[DEĞİŞEN KURAL]</b> ESKİ: Stop zaten istendiği için ikinci bir <c>stopRun</c> gitmiyordu; çıkış artık drain'i
    /// hard stop'a tırmandırır (<see cref="An_exit_during_the_users_drain_escalates_to_a_hard_stop"/>) — susmuş motorda da.</para></summary>
    [Fact]
    public async Task An_engine_already_silent_while_stopping_does_not_hold_the_exit()
    {
        long now = 1_000;
        var h = new Harness(nowMs: () => now);
        MainWindowHost.StartBuild(h.Vm);
        await h.Vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(AppPhase.Stopping, h.Vm.Phase); // ön-koşul: kullanıcının Stop'u drain'i bekliyor

        now += RunViewModel.EngineSilenceThresholdMs;
        h.Vm.TickElapsed();
        Assert.NotNull(h.Vm.EngineOverdueMessage); // ön-koşul: motor çıkıştan ÖNCE sustu — uyarı şeritte

        h.Vm.RequestExit();

        Assert.Equal(1, h.Ready);
        Assert.Equal(StopKind.Hard, h.Sent.OfType<StopRunCommand>().Last().Kind);
    }

    /// <summary>[final review · F1] Koşu istendi ama <c>runStarted</c> hiç gelmedi (Starting) ve motor bu arada sustu.
    /// Çıkışın kendi Stop'u fazı Stopping'e çeker ve bu, bekçinin saatini YENİDEN kurar: bir sonraki tık amber satırı
    /// kaldırır. Serbest bırakan, isteğin içindeki değerlendirmedir — <c>ExitReady</c> AYNI çağrıda gelir, araya hiçbir
    /// tık giremeden. Giden durdurma hard'dır.</summary>
    [Fact]
    public async Task An_engine_silent_before_the_run_started_does_not_hold_the_exit()
    {
        long now = 1_000;
        var h = new Harness(nowMs: () => now);
        VmTopology.Seed(h.Vm); // [topoloji kapısı] Build'in ön-koşulu
        await h.Vm.BuildCommand.ExecuteAsync(null);
        Assert.True(h.Vm.IsStarting); // ön-koşul: startRun gitti, runStarted gelmedi

        now += RunViewModel.EngineSilenceThresholdMs;
        h.Vm.TickElapsed();
        Assert.NotNull(h.Vm.EngineOverdueMessage); // ön-koşul: motor çıkıştan ÖNCE sustu

        h.Vm.RequestExit();

        Assert.Equal(AppPhase.Stopping, h.Vm.Phase); // çıkışın Stop'u gitti — bekçinin saati yeniden kuruldu
        Assert.Equal(1, h.Ready);                    // yine de AYNI çağrıda
        Assert.Equal(StopKind.Hard, Assert.Single(h.Sent.OfType<StopRunCommand>()).Kind);
    }

    /// <summary>Bekleyiş sırasında motor ölürse beklenecek bir cevap kalmaz: çıkış serbest kalır.</summary>
    [Fact]
    public void An_engine_that_dies_during_the_wait_releases_the_exit()
    {
        var h = new Harness();
        MainWindowHost.StartBuild(h.Vm);
        h.Vm.RequestExit();
        Assert.Equal(0, h.Ready); // ön-koşul: drain bekleniyor

        h.Vm.OnEngineExited(1);

        Assert.Equal(1, h.Ready);
    }

    // ---------------------------------------------------------------- açılış koreografisi

    /// <summary>[design v1.11.0 §3.1] Açılış koreografisi oynarken koşu komutu henüz GİTMEDİ: çıkış, Stop'un marking
    /// kuralıyla isteği geri alır — motora ne <c>startRun</c> ne <c>stopRun</c> gider ve çıkış hemen hazırdır.
    /// Koreografi sonradan bitse de koşu gönderilmez.</summary>
    [Fact]
    public async Task An_exit_during_the_marking_phase_cancels_the_pending_run()
    {
        var h = new Harness();
        VmTopology.Seed(h.Vm); // [topoloji kapısı] Build'in ön-koşulu
        var choreography = new TaskCompletionSource();
        h.Vm.OperationChoreography = _ => choreography.Task;
        var run = h.Vm.BuildCommand.ExecuteAsync(null);
        Assert.True(h.Vm.IsStarting); // ön-koşul: işlem başladı, komut henüz gitmedi

        h.Vm.RequestExit();

        Assert.Equal(1, h.Ready);
        Assert.Empty(h.Sent);

        choreography.SetResult();
        await run;

        Assert.Empty(h.Sent);
        Assert.Equal(1, h.Ready);
    }

    // ---------------------------------------------------------------- kendiliğinden Sync

    /// <summary>Çıkış beklerken kendiliğinden Sync BAŞLAMAZ: HEAD izleyicisi bırakılır, pencereye dönüş ve HEAD
    /// tetiği hiçbir şey göndermez; koşu bitince bekletilen bir tetik de koşmaz (koordinatör kapandı). Aksi hâlde
    /// drain'in hemen ardından yeni bir iş başlar ve çıkış onu da beklerdi.</summary>
    [Fact]
    public void No_automatic_sync_starts_while_the_exit_waits()
    {
        using var root = FakeHeadWatcher.GitRoot();
        var h = new Harness(root.Path);
        var watcher = new FakeHeadWatcher();
        h.Vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0,
            HeadSha: "1111111111111111111111111111111111111111", ActiveBranch: "main"));
        h.Vm.EnableAutoSync(post => post(), _ => new HeadState("main", "2222222222222222222222222222222222222222"),
            () => watcher);
        Assert.NotNull(watcher.OnSettled); // ön-koşul: izleyici bu köke bağlandı
        MainWindowHost.StartBuild(h.Vm);

        h.Vm.RequestExit();
        h.Vm.OnWindowActivated();
        watcher.OnSettled!(HeadMove.Other);
        h.Vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));
        h.Vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 0, 0, 0, 2, 500));

        Assert.Empty(h.Sent.OfType<SyncWorkspaceCommand>());
        Assert.True(watcher.Disposed);
    }
}
