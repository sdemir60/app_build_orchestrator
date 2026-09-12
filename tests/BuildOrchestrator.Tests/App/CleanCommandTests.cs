using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [clean] Bakım kutusundaki Clean düğmesinin VM yüzeyi: komut gönderimi, konsol sıfırlama, karşılıklı
/// dışlama kapıları ve serbest bırakma yolları.
/// <para>Harness <see cref="RunViewModelStateTests"/> ile aynıdır: başlatılmamış <see cref="EngineHost"/> —
/// gönderim SENKRON düşer ve VM içinde yutulur, yani "gönderim başarısız" yolu VARSAYILANDIR. Uçuş durumu
/// gerçek motor olmadan <c>vm.OnEvent(...)</c> ile kurulur. D8: sleep/poll yok.</para>
/// </summary>
public class CleanCommandTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm(Func<long>? nowMs = null) =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1", nowMs) { RootPath = @"D:\repo" };

    private static ProjectNode Node(string id, string name) => new(id, name, id, ["Osys"], [], 0, null, null, false, null);

    /// <summary>Run komutlarının kapısı topolojidir; Clean kapılarını ölçen testler önce onu kurar.</summary>
    private static void SeedTopology(RunViewModel vm) =>
        vm.OnEvent(new WorkspaceTopologyEvent([Node(@"C:\p\a.csproj", "A")], [], [], []));

    private static CleanCompletedEvent Completed() => new(2, 4, 1024, 0, 2);

    // ---------------------------------------------------------------- gönderim

    [Fact]
    public async Task Clean_sends_cleanWorkspace_with_the_workspace_root()
    {
        var vm = NewVm();
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.CleanCommand.ExecuteAsync(null);

        Assert.Equal(@"D:\repo", Assert.Single(sent.OfType<CleanWorkspaceCommand>()).RootPath);
    }

    /// <summary>[harici projeler] Harici kartlar da gider: harici proje sıradan bir projedir ve onun çıktısı da
    /// bu workspace'in çıktısıdır. Liste Sync/Build ile AYNI huniden (<c>ExternalProjectsForWire</c>) geçer —
    /// ikinci bir kaynak açılmaz.</summary>
    [Fact]
    public async Task Clean_carries_the_registered_external_cards()
    {
        var vm = NewVm();
        vm.ExternalProjects = [new ExternalProject(@"D:\ext\Shared")];
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        await vm.CleanCommand.ExecuteAsync(null);

        var cmd = Assert.Single(sent.OfType<CleanWorkspaceCommand>());
        Assert.Equal(@"D:\ext\Shared", Assert.Single(cmd.ExternalProjects!).Path);
    }

    // Clean bir "sıfırdan başla" anıdır: konsol önceki koşunun anlatısıyla karışmamalı. Sıfırlama bloğu
    // BeginRunAsync ile ORTAKTIR (kopya YASAK) — bu test o ortak yolun Clean'den de geçtiğini pinler.
    [Fact]
    public async Task Clean_clears_the_console_and_writes_the_requested_line()
    {
        var vm = NewVm();
        vm.OnEvent(new SyncProgressEvent("git fetch origin main", "cmd"));
        Assert.Contains("git fetch", vm.GetRunDocumentText(), StringComparison.Ordinal);

        await vm.CleanCommand.ExecuteAsync(null);

        string text = vm.GetRunDocumentText();
        Assert.DoesNotContain("git fetch", text, StringComparison.Ordinal);
        Assert.Contains("clean requested", text, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- kapı matrisi

    [Fact]
    public void Clean_is_disabled_without_a_workspace()
    {
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1");
        Assert.False(vm.CleanCommand.CanExecute(null));

        vm.RootPath = @"D:\repo";
        Assert.True(vm.CleanCommand.CanExecute(null));
    }

    [Fact]
    public void Clean_is_disabled_while_a_run_is_starting_or_running()
    {
        var vm = NewVm();
        SeedTopology(vm);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        Assert.False(vm.CleanCommand.CanExecute(null));

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 500));
        Assert.True(vm.CleanCommand.CanExecute(null));
    }

    [Fact]
    public void Clean_is_disabled_while_a_sync_is_in_flight()
    {
        var vm = NewVm();

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(vm.CleanCommand.CanExecute(null));

        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));
        Assert.True(vm.CleanCommand.CanExecute(null));
    }

    [Fact]
    public void Clean_is_disabled_while_the_engine_is_unavailable()
    {
        var vm = NewVm();
        vm.OnEngineUnavailable(@"D:\repo\supervisor\BuildOrchestrator.Supervisor.exe");

        Assert.True(vm.IsEngineUnavailable);
        Assert.False(vm.CleanCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- karşılıklı dışlama

    // Clean silerken bir build başlarsa MSBuild, altından çekilen bir obj/bin ile yarışır. Kapı hem UÇUŞ
    // (cleanStarted geldi) hem İSTEK penceresini (komut yolda, motor henüz cevap vermedi) kapsar.
    [Fact]
    public void Sync_build_rebuild_and_cycles_are_disabled_while_a_clean_is_in_flight_and_reopen_on_completion()
    {
        var vm = NewVm();
        SeedTopology(vm);
        Assert.True(vm.SyncCommand.CanExecute(null));
        Assert.True(vm.BuildCommand.CanExecute(null));
        Assert.True(vm.RebuildCommand.CanExecute(null));

        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));

        Assert.False(vm.SyncCommand.CanExecute(null));
        Assert.False(vm.BuildCommand.CanExecute(null));
        Assert.False(vm.RebuildCommand.CanExecute(null));
        Assert.False(vm.BuildCyclesCommand.CanExecute(null));
        Assert.False(vm.CleanCommand.CanExecute(null)); // ikinci bir Clean de anlamsızdır

        vm.OnEvent(Completed());

        Assert.True(vm.SyncCommand.CanExecute(null));
        Assert.True(vm.BuildCommand.CanExecute(null));
        Assert.True(vm.RebuildCommand.CanExecute(null));
        Assert.True(vm.CleanCommand.CanExecute(null));
    }

    // İstek penceresi: komut gönderildikten hemen SONRA, motor cevap vermeden önce de kapı KAPALIDIR —
    // aksi halde ikinci bir basış motora ikinci bir silme kuyruklatırdı.
    [Fact]
    public async Task The_gate_is_already_closed_in_the_request_window_before_the_engine_answers()
    {
        var vm = NewVm();
        SeedTopology(vm);
        bool closedAtSendTime = true;
        vm.DebugOnCommandSent = cmd =>
        {
            if (cmd is CleanWorkspaceCommand) closedAtSendTime = !vm.CleanCommand.CanExecute(null);
        };

        await vm.CleanCommand.ExecuteAsync(null);

        Assert.True(closedAtSendTime, "kapı GÖNDERİMDEN ÖNCE kapanmalı");
    }

    // ---------------------------------------------------------------- serbest bırakma

    // Motor hazır değilse gönderim SENKRON düşer ve hiçbir cleanStarted GELMEZ — kapı burada açılmazsa
    // düğme kalıcı pasif kalırdı.
    [Fact]
    public async Task A_failed_send_reopens_the_clean_gate()
    {
        var vm = NewVm();
        SeedTopology(vm);

        await vm.CleanCommand.ExecuteAsync(null); // başlatılmamış engine → gönderim düşer

        Assert.True(vm.CleanCommand.CanExecute(null));
        Assert.True(vm.SyncCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("cleanFailed")]
    [InlineData("cleanRejected")]
    public void A_clean_error_releases_the_clean_surface(string code)
    {
        var vm = NewVm();
        SeedTopology(vm);
        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));
        Assert.False(vm.SyncCommand.CanExecute(null));

        vm.OnEvent(new ErrorEvent(code, "something went wrong"));

        Assert.True(vm.CleanCommand.CanExecute(null));
        Assert.True(vm.SyncCommand.CanExecute(null));
        Assert.True(vm.BuildCommand.CanExecute(null));
    }

    // Motor Clean ORTASINDA ölürse hiçbir cleanCompleted/clean-hatası gelmez: bayrak sızarsa yeniden
    // başlatılan motorda da düğmeler kilitli kalırdı.
    [Fact]
    public void Engine_death_mid_clean_releases_the_clean_surface()
    {
        var vm = NewVm();
        SeedTopology(vm);
        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));

        vm.OnEngineExited(1);

        // Ölüm YENİDEN BAŞLATILABİLİRdir (IsEngineUnavailable false kalır) — kapıyı kapatan tek şey
        // sızmış bir clean bayrağı olurdu.
        Assert.True(vm.EngineRestartable);
        Assert.True(vm.CleanCommand.CanExecute(null));
        Assert.True(vm.SyncCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------- konsol + stream

    [Fact]
    public void Clean_progress_lines_flow_into_the_run_document()
    {
        var vm = NewVm();
        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));

        vm.OnEvent(new CleanProgressEvent("A — bin + obj removed (12 MB)", "dim"));

        Assert.Contains("bin + obj removed", vm.GetRunDocumentText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Clean_completion_pushes_one_stream_summary_line()
    {
        var vm = NewVm();
        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));
        int before = vm.StreamEventCount;

        vm.OnEvent(new CleanCompletedEvent(36, 71, 4_294_967_296, 2, 36));

        Assert.Equal(before + 1, vm.StreamEventCount);
        string line = vm.StreamEvents[^1].Text;
        Assert.Contains("Clean", line, StringComparison.Ordinal);
        Assert.Contains("36", line, StringComparison.Ordinal);
    }

    /// <summary>[design v1.13.2 §9] "Konsol + event stream HER işlemde temizlenir" — Clean de bir işlemdir ve
    /// <c>SyncCoreAsync(clearBuffers:true)</c> ile AYNI iki metodu tıklama anında çağırır
    /// (<see cref="RunViewModelStateTests.Sync_clears_the_console_and_stream_left_over_from_the_previous_operation"/>'ın
    /// Clean ikizi). Planın ilk hâli stream'i "mevcut sözleşme" diye koruyordu; o sözleşme v1.13.2 ile değişti.</summary>
    [Fact]
    public async Task Clean_clears_the_stream_left_over_from_the_previous_operation()
    {
        var vm = NewVm();
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 100));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 100));
        Assert.True(vm.StreamEventCount > 0, "ön-koşul: event stream'de ÖNCEKİ işlemden iz yok — vakum");

        await vm.CleanCommand.ExecuteAsync(null);

        Assert.Equal(0, vm.StreamEventCount); // gönderim hatası yalnız konsola yazar, stream'e dokunmaz
    }

    /// <summary>[design v1.11.0 §2.2] Kalıcı işlem pill'i TIKLAMA ANINDA yazılır. Sözcük <c>DEEP CLEAN</c>:
    /// menüdeki tek-proje Clean'i (<c>/t:Clean</c>, <c>CLEAN</c>) ile karıştırılmaz.</summary>
    [Fact]
    public async Task Clean_writes_the_deep_clean_operation_pill_at_click_time()
    {
        var vm = NewVm();
        Assert.NotEqual(OperationLabel.DeepClean, vm.CurrentOperation);

        await vm.CleanCommand.ExecuteAsync(null);

        Assert.Equal(OperationLabel.DeepClean, vm.CurrentOperation);
    }

    // ---------------------------------------------------------------- liste + graf

    /// <summary>
    /// [kullanıcı kararı 2026-09-12] <b>Liste ve graf TIKLAMA ANINDA boşalır.</b> Clean çıktıları siler, yani
    /// ekranda duran her şey (kararlar, süreler, yeşil/kırmızı statüler, düğümler) o an geçersizdir; konsol ve
    /// event stream de aynı karede temizlenir, dolayısıyla plan yüzeyinin farklı bir anda düşmesi tek bir işlemi
    /// iki ayrı sarsıntı gibi gösterirdi. Liste yeniden Sync'in yayınladığı topolojiyle dolar.
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> Önceki iki kural da bu testle değişti. (1) Eskiden satırlar listede KALIP
    /// yalnız kararlarını bırakıyordu ("hollow"); gerekçe, koleksiyon boşalırsa panelin
    /// "<c>No projects found under this folder.</c>" demesiydi. O gerekçe hâlâ geçerli ama çözümü ayrı: faz
    /// <see cref="AppPhase.Boot"/>'a alınır ve davet kararı (<c>ListInvite.Resolve</c>) o fazda hiçbir şey
    /// söylemez — branch değişiminin zaten yaptığı şey. (2) Tetikleyici eskiden motorun kabulüydü
    /// (<c>cleanStarted</c>); kullanıcı "tıkladığım anda olsun" dedi, çünkü aradaki gecikme ekranı iki adımda
    /// boşaltıyor gibi duruyordu. Bedeli kabul edildi: gönderim düşerse ya da komut reddedilirse liste boş kalır
    /// ve geri getirmek kullanıcının Sync'ine kalır.</para>
    /// </summary>
    [Fact]
    public async Task Clean_empties_the_project_list_and_the_graph_at_click()
    {
        var vm = NewVm();
        SeedTopology(vm);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(@"C:\p\a.csproj", "A", true)]));
        vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\p\a.csproj", 1234));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 1234));
        Assert.Single(vm.Projects);        // ön-koşul: ekranda bir proje ve bir sonuç var
        Assert.True(vm.HasTopology);
        Assert.Equal(1, vm.WillBuildCount);
        int topologyChanges = 0;
        vm.TopologyChanged += (_, _) => topologyChanges++;

        await vm.CleanCommand.ExecuteAsync(null);

        Assert.Empty(vm.Projects);
        Assert.False(vm.HasTopology);      // graf da boşalır — kabuk TopologyChanged ile yeniden kurar
        Assert.Equal(1, topologyChanges);
        Assert.Equal(0, vm.WillBuildCount);
        Assert.True(vm.AllClean);
        // Panel YANLIŞ konuşmaz: boş liste + Boot fazı = hiçbir davet (klasörde proje YOK demek olurdu).
        Assert.Equal(AppPhase.Boot, vm.Phase);
        Assert.Equal(ListInviteState.None,
            ListInvite.Resolve(vm.HasWorkspace, vm.Phase, vm.Projects.Count, vm.VisibleProjects.Count));
    }

    /// <summary>[kullanıcı kararı 2026-09-12] Boşaltma TIKLAMADADIR, dolayısıyla motora hiç ulaşmamış bir Clean de
    /// listeyi boşaltmış olur — geri getiren şey kullanıcının Sync'idir.
    /// <para><b>[DEĞİŞEN KURAL]</b> Bu test eskiden tersini pinliyordu ("gönderim düşerse satırlara dokunulmaz");
    /// gerekçesi, reddedilen ya da gönderilemeyen bir Clean'de ekranın bedelsiz bozulmamasıydı. Kullanıcı
    /// anındalığı seçti; bu yol da (motor ölü) zaten kendi başına bir hata durumudur.</para></summary>
    [Theory]
    [InlineData(null)]
    [InlineData("cleanRejected")]
    public async Task A_clean_that_never_runs_still_leaves_the_list_empty(string? errorCode)
    {
        var vm = NewVm();
        SeedTopology(vm);
        vm.OnEvent(new BuildPreviewEvent(
            [new BuildPreviewItem(@"C:\p\a.csproj", "A", false, Reason: WillBuildReason.UpToDate)]));
        Assert.Single(vm.Projects);

        await vm.CleanCommand.ExecuteAsync(null); // harness: gönderim SENKRON düşer
        if (errorCode is not null) vm.OnEvent(new ErrorEvent(errorCode, "a run is in flight"));

        Assert.Empty(vm.Projects);
        Assert.True(vm.CleanCommand.CanExecute(null)); // kapı yine de açılır — düğme kilitli kalmaz
    }

    // ---------------------------------------------------------------- bitişte otomatik Sync

    /// <summary>Clean satırların kararlarını düşürür (<see cref="A_started_clean_hollows_the_rows_and_the_will_build_surface"/>),
    /// dolayısıyla o kararları geri getiren bir şey olmalıdır: bitişte KONSOL KORUNARAK bir Sync koşar —
    /// <c>N behind</c> chip'indeki pull'un birebir deseni. Kullanıcı elle Sync'e basmak zorunda kalmaz ve
    /// satırlar motorun GERÇEK kararlarıyla dolar (hepsi <c>never built</c>).</summary>
    [Fact]
    public void Clean_completion_runs_an_automatic_sync_and_keeps_the_console()
    {
        var vm = NewVm();
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;
        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));
        vm.OnEvent(new CleanProgressEvent("build state reset — 2 entries cleared", "info"));

        vm.OnEvent(Completed());

        Assert.Equal(@"D:\repo", Assert.Single(sent.OfType<SyncWorkspaceCommand>()).RootPath);
        // Konsol KORUNUR: kullanıcı kendi tetiklediği Clean'in transkriptini Sync satırlarının üstünde görmeye
        // devam eder (pull'un clearBuffers:false gerekçesi).
        Assert.Contains("build state reset", vm.GetRunDocumentText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// [kullanıcı kararı 2026-09-12] <b>Clean'in adımı HER ZAMAN aynı süre oynar.</b> Küçük bir workspace'te
    /// silme milisaniyeler sürüyor, spinner görünmeye fırsat bulamıyor ve Sync'in animasyonları üstüne biniyordu
    /// ("tıklıyorum git gel oluyor"). Bu, projenin ölçüp kayda geçirdiği kusurun aynısıdır: bir koreografi
    /// motorun penceresiyle örtüştürüldüğünde aynı tıklama bazen animasyonlu bazen anında olur (bkz.
    /// <c>RunViewModel.BeginRunAsync</c>'in koreografi kapısı — "ya her zaman oynar ya hiç").
    ///
    /// <para>Dizi: adım en az <see cref="RunViewModel.CleanMinStepMs"/> görünür, ardından
    /// <see cref="RunViewModel.CleanStepGapMs"/> kadar hafif bir boşluk, EN SON Sync. Zamanı VM saymaz: bekleme
    /// enjekte edilen bir delegeye sorulur (kabuk onu DispatcherTimer ile karşılar — VM timer türü TAŞIMAZ, D8).</para>
    /// <para><b>[DEĞİŞEN KURAL]</b> Bu test boşluk sırasında <c>busy=False</c> bekliyordu, yani yüzey boşluktan
    /// ÖNCE bırakılıyordu. Ölçüldü ki o pencerede Sync/Clean tıklanabilir haldeydi ve düğmeler kırpışıyordu;
    /// kapı artık Sync devralana kadar KAPALI (bkz.
    /// <see cref="Nothing_is_clickable_between_the_clean_and_the_sync_that_follows_it"/>), dolayısıyla spinner de
    /// devralmaya kadar döner.</para></summary>
    [Fact]
    public async Task A_fast_clean_still_shows_its_step_before_the_sync_takes_over()
    {
        long now = 0;
        var vm = NewVm(() => now);
        var log = new List<string>();
        vm.OperationHold = ms => { log.Add($"hold {ms} busy={vm.CleanBusy}"); return Task.CompletedTask; };
        vm.DebugOnCommandSent = c =>
        {
            if (c is CleanWorkspaceCommand) log.Add("clean sent");
            if (c is SyncWorkspaceCommand) log.Add("sync sent");
        };

        await vm.CleanCommand.ExecuteAsync(null);
        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));
        now = 50; // motor 50 ms'de bitirdi — adımın kalanı yine de oynar
        vm.OnEvent(Completed());

        Assert.Equal(
        [
            "clean sent",
            "hold 390 busy=True", // adım sürüyor: spinner DÖNÜYOR
            "hold 200 busy=True", // iki işlem arasındaki boşluk — kapı hâlâ kapalı, spinner hâlâ dönüyor
            "sync sent",
        ], log);
    }

    /// <summary>
    /// [kullanıcı kararı 2026-09-12] <b>Kapı, Clean'in tıklanmasından Sync'in devralmasına kadar BİR AN bile
    /// açılmaz.</b> İki işlem tek bir meşgul pencere olarak okunur: arada hiçbir düğme canlanmaz, hiçbir şeye
    /// tıklanamaz.
    ///
    /// <para><b>Ölçülen kusur:</b> yüzey iki adım arasındaki boşluktan ÖNCE bırakılıyordu, yani o boşluk boyunca
    /// Build/Rebuild/Sync/Clean tıklanabilir haldeydi ve düğmeler sönük → canlı → sönük diye kırpışıyordu.
    /// Kullanıcı tarifi: "o ara bir şeye tıklanmamalı".</para>
    ///
    /// <para>Yüzey artık Sync kapıyı devraldıktan SONRA bırakılır; spinner de o ana kadar döner, ardından
    /// anlatıyı şeridin <c>SYNC</c> pill'i sürdürür.</para></summary>
    [Fact]
    public async Task Nothing_is_clickable_between_the_clean_and_the_sync_that_follows_it()
    {
        long now = 0;
        var vm = NewVm(() => now);
        SeedTopology(vm);
        var gates = new List<string>();
        vm.OperationHold = ms =>
        {
            gates.Add($"hold {ms}: build={vm.BuildCommand.CanExecute(null)} sync={vm.SyncCommand.CanExecute(null)} " +
                      $"clean={vm.CleanCommand.CanExecute(null)} busy={vm.CleanBusy}");
            return Task.CompletedTask;
        };

        await vm.CleanCommand.ExecuteAsync(null);
        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));
        now = 50;
        vm.OnEvent(Completed());

        Assert.Equal(
        [
            "hold 390: build=False sync=False clean=False busy=True", // adım oynuyor
            "hold 200: build=False sync=False clean=False busy=True", // boşluk — kapı HÂLÂ kapalı
        ], gates);
    }

    /// <summary>Yavaş bir Clean zaten görünmüştür: üstüne bekleme EKLENMEZ, yalnız iki işlem arasındaki boşluk
    /// kalır. Aksi halde uzun bir silmenin sonuna sebepsiz bir yarım saniye eklenirdi.</summary>
    [Fact]
    public async Task A_slow_clean_is_not_held_any_longer_than_the_gap()
    {
        long now = 0;
        var vm = NewVm(() => now);
        var holds = new List<double>();
        vm.OperationHold = ms => { holds.Add(ms); return Task.CompletedTask; };

        await vm.CleanCommand.ExecuteAsync(null);
        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));
        now = 9_000; // 9 saniye sürdü
        vm.OnEvent(Completed());

        Assert.Equal([RunViewModel.CleanStepGapMs], holds);
    }

    /// <summary>Başarısız bir işin arkasına Sync TAKILMAZ: hata zaten konsolda, ikinci bir hata satırı yalnız
    /// gürültü olurdu. Liste boş kalır ve Sync kullanıcıya kalır.
    /// <para>Kurulum <see cref="A_clean_error_releases_the_clean_surface"/> ile aynıdır ve öyle OLMALIDIR: hata
    /// yolu yalnız Clean UÇUŞTAYKEN (<c>CleanBusy</c>) tüketilir, dolayısıyla gönderimi senkron düşmüş bir
    /// Clean'de zincir zaten hiç kurulmaz ve test hiçbir şeyi pinlemezdi.</para></summary>
    [Theory]
    [InlineData("cleanFailed")]
    [InlineData("cleanRejected")]
    public void A_failed_clean_does_not_chain_a_sync(string code)
    {
        var vm = NewVm();
        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;

        vm.OnEvent(new ErrorEvent(code, "boom"));

        Assert.Empty(sent.OfType<SyncWorkspaceCommand>());
    }

    /// <summary>[v1.16.0 · clean] Alt bardaki <c>N behind</c> chip'i de bakım kilidine tabidir: başarılı bir pull
    /// otomatik Sync koşar ve o Sync, tam o sırada silinen bin/obj'i okurdu.</summary>
    [Fact]
    public void The_pull_chip_is_disabled_while_a_clean_is_in_flight_and_reopens_on_completion()
    {
        var vm = NewVm();
        vm.OnEvent(new SyncCompletedEvent("main", "b7e91d4", FetchDegraded: false, 1, 0, Behind: 3));
        Assert.True(vm.PullRepositoryCommand.CanExecute(null));

        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));
        Assert.False(vm.PullRepositoryCommand.CanExecute(null));

        vm.OnEvent(Completed());
        Assert.True(vm.PullRepositoryCommand.CanExecute(null));
    }
}
