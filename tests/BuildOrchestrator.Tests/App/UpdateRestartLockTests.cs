using System.ComponentModel;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.23.0 §2.12 · plan U3] Güncelleme kartının <c>Restart to update</c> düğmesi, bir iş sürerken kilitlidir ve
/// açıklama satırı nedenini söyler — yarım kalan koşu olmaz. Karar VM'de TEK bir hesaplanan özelliktir
/// (<see cref="RunViewModel.UpdateRestartBlockedReason"/>); sıra: (1) Clean / Optimize / Resolve / checkout / pull →
/// <c>Available once the running task finishes.</c> (2) herhangi bir Sync, sessizi dahil →
/// <c>Available once Sync finishes.</c> (3) koşu sürüyor ya da işaretleniyor (açılış koreografisi) →
/// <c>Available once the build finishes — Esc stops it.</c> İş bitince düğme kendiliğinden açılır.
///
/// <para><b>Tasarımdan sapma (plan U3):</b> tasarım "— F5 stops it." der; uygulamada F5 koşuyu durdurmaz, Esc
/// durdurur. Tuşun adı kısayol kataloğundan okunur (<see cref="ShortcutCatalog"/>).</para>
///
/// <para><b>[DEĞİŞEN KURAL — perf Faz B son toparlama · F-M1]</b> ESKİ İDDİA: koşu nedeni, Stop'un hangi aşamada olduğuna
/// bakmadan hep <c>Available once the build finishes — Esc stops it.</c> der. GEREKÇE: Stopping'de bir sonraki Esc hard
/// stop'tur (ARCHITECTURE §4.5: hiçbir yüzey, bir sonraki basış hard stop iken "Stop" demez) ve Terminating'de Esc hiçbir
/// şey yapmaz — ipucu orada düpedüz yanlıştı. Cümle artık <see cref="StopStage"/>'i izler: istenmedi → eski cümle (aşağıdaki
/// vakalar değişmedi), graceful gitti → <c>… Esc stops it now.</c>, hard gitti → Esc'siz
/// <c>Available once the build stops.</c> (<see cref="UpdateText.WaitForBuildAt"/>).</para>
///
/// <para>Harness: gönderimler sahte ama canlı bir motora gider (<see cref="RunViewModel.DebugSendOverride"/>), tıklama
/// WPF'in yaptığı gibi kapıdan geçer (<see cref="CommandPress"/>).</para>
/// </summary>
public class UpdateRestartLockTests
{
    private static readonly BranchRef FeatureX = new("feature/x", "bbbbbbbbbbbb", IsActive: false, IsRemoteTracking: false);

    /// <summary>Sync'lenmiş, geride kalmış (pull yapılabilir), iki branch'li boşta bir workspace ve kuruluma hazır bir
    /// teklif (<see cref="UpdateOffers.Sample"/>) — kartın testleri de (<see cref="UpdateCardTests"/>) aynı VM'i
    /// kullanır.</summary>
    internal static RunViewModel NewVm()
    {
        var vm = NewVmWithoutOffer();
        vm.AvailableUpdate = UpdateOffers.Sample();
        return vm;
    }

    /// <summary><see cref="NewVm"/>'in workspace'i, teklifsiz — uygulamanın kendi başına vardığı durum.</summary>
    private static RunViewModel NewVmWithoutOffer()
    {
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), MainWindowHost.NeverTickingBatcher(), () => "r1")
        {
            RootPath = @"D:\repo",
        };
        vm.DebugSendOverride = _ => Task.CompletedTask;
        vm.InspectGitOperation = _ => GitOperation.None;
        vm.OnEvent(new BranchListEvent([
            new BranchRef("main", "aaaaaaaaaaaa", IsActive: true, IsRemoteTracking: false),
            FeatureX,
        ]));
        VmTopology.Seed(vm);
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0, Behind: 2));
        return vm;
    }

    /// <summary>Test edilen iş türünü ÜRETİMDEKİ girişinden başlatır.</summary>
    private static async Task EnterAsync(RunViewModel vm, string work)
    {
        switch (work)
        {
            case "clean": Assert.True(CommandPress.Press(vm.CleanCommand)); break;
            case "optimize": Assert.True(CommandPress.Press(vm.OptimizeCommand)); break;
            case "resolve": vm.OnEvent(new RunStartedEvent("r1", RunMode.Cycles, 1, 4, "Debug")); break;
            case "checkout": await vm.SelectBranch(FeatureX); Assert.True(vm.CheckoutBusy); break;
            case "pull": await vm.PullRepositoryCommand.ExecuteAsync(null); Assert.True(vm.PullBusy); break;
            case "sync": Assert.True(CommandPress.Press(vm.SyncCommand)); break;
            case "silentSync": await vm.SyncSilentlyAsync(SilentSyncReason.Refresh); Assert.True(vm.SyncBusy); break;
            case "build": vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0)); break;
            case "marking":
                // Açılış koreografisi oynarken koşu henüz gönderilmedi — kilit tıklama anında başlar.
                vm.OperationChoreography = _ => new TaskCompletionSource().Task;
                Assert.True(CommandPress.Press(vm.BuildCommand));
                Assert.True(vm.IsStarting);
                break;
            case "buildPressedDuringSync":
                // [kullanıcı kararı 2026-10-02] Sync sürerken Build basılamaz: kapı kapalı, istek kuyruğa alınmaz — ne koşu
                // başlar ne de işaretleme penceresi açılır; kilidin nedeni Sync'in nedeni olarak kalır.
                Assert.True(CommandPress.Press(vm.SyncCommand));
                Assert.False(CommandPress.Press(vm.BuildCommand));
                Assert.False(vm.IsStarting);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(work), work, null);
        }
    }

    private static string ExpectedReason(string kind) => kind switch
    {
        "task" => UpdateText.WaitForTask,
        "sync" => UpdateText.WaitForSync,
        "build" => UpdateText.WaitForBuild,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    [Fact]
    public void An_idle_workspace_leaves_the_restart_open()
    {
        var vm = NewVm();

        Assert.Null(vm.UpdateRestartBlockedReason);
        Assert.True(vm.RestartToUpdateCommand.CanExecute(null));
    }

    /// <summary>Her iş türü kendi nedenini söyler ve Restart kapanır. Resolve bir koşudur ama bir görev gibi okunur
    /// (tasarım: Clean/Optimize/Resolve); checkout ve pull tasarımda yoktur, görev kovasına girer. Sessiz Sync de kilitler
    /// (plan U3) — ekranda görünmese de kurulum onu yarıda keserdi. Sync sürerken Build'e basmak hiçbir şey başlatmaz (kapı
    /// kapalı) ve kilidin nedenini DEĞİŞTİRMEZ: neden Sync'in nedeni olarak kalır, <c>IsStarting</c> açılmaz.
    ///
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-02]</b> Önceki ad ve iddia (<c>buildWaitingForSync</c>, kullanıcı
    /// bildirimi 2026-09-29): Sync sürerken basılıp bekleyen Build kilidin nedenini Sync'ten alır (sıra görev &gt; Sync &gt;
    /// koşu) — basış bir istekti ve <c>IsStarting</c>'i açardı. Kuyruk kaldırıldı: basış kapıdan geçmez. Vaka silinmedi, yeni
    /// kurala göre yeniden yazıldı (gerekçe <c>RunRequestDuringWorkTests</c> doc'unda).</para></summary>
    [Theory]
    [InlineData("clean", "task")]
    [InlineData("optimize", "task")]
    [InlineData("resolve", "task")]
    [InlineData("checkout", "task")]
    [InlineData("pull", "task")]
    [InlineData("sync", "sync")]
    [InlineData("silentSync", "sync")]
    [InlineData("build", "build")]
    [InlineData("marking", "build")]
    [InlineData("buildPressedDuringSync", "sync")]
    public async Task Work_in_flight_locks_the_restart_and_names_the_reason(string work, string reason)
    {
        var vm = NewVm();

        await EnterAsync(vm, work);

        Assert.Equal(ExpectedReason(reason), vm.UpdateRestartBlockedReason);
        Assert.False(vm.RestartToUpdateCommand.CanExecute(null));
    }

    /// <summary>Sıra saf karardır: görev Sync'in, Sync koşunun önüne geçer; hiçbiri yoksa neden yoktur.</summary>
    [Theory]
    [InlineData(true, true, true, "task")]
    [InlineData(true, false, false, "task")]
    [InlineData(false, true, true, "sync")]
    [InlineData(false, true, false, "sync")]
    [InlineData(false, false, true, "build")]
    public void The_reason_follows_task_then_sync_then_build(bool task, bool sync, bool build, string expected)
    {
        Assert.Equal(ExpectedReason(expected), UpdateText.RestartBlockedReason(task, sync, build, StopStage.Stop));
    }

    [Fact]
    public void Nothing_in_flight_gives_no_reason()
    {
        Assert.Null(UpdateText.RestartBlockedReason(taskRunning: false, syncRunning: false, buildRunning: false,
            stopStage: StopStage.Stop));
    }

    /// <summary>[F-M1] Aşama yalnız KOŞU cümlesini değiştirir: görev ve Sync, Stop'un hangi aşamada olduğuna bakmadan
    /// önce gelir; iş yoksa hiçbir aşama neden üretmez.</summary>
    [Theory]
    [InlineData(StopStage.Stop)]
    [InlineData(StopStage.StopNow)]
    [InlineData(StopStage.Terminating)]
    public void The_stop_stage_only_changes_the_build_sentence(StopStage stage)
    {
        Assert.Equal(UpdateText.WaitForTask, UpdateText.RestartBlockedReason(true, true, true, stage));
        Assert.Equal(UpdateText.WaitForSync, UpdateText.RestartBlockedReason(false, true, true, stage));
        Assert.Equal(UpdateText.WaitForBuildAt(stage), UpdateText.RestartBlockedReason(false, false, true, stage));
        Assert.Null(UpdateText.RestartBlockedReason(false, false, false, stage));
    }

    /// <summary>Metinler birebir (tasarım §2.12 · §9); koşu nedeni durduran tuşu kataloğun jestiyle yazar — tasarımın
    /// "F5" yazdığı yerde uygulamanın durduran tuşu Esc'tir.</summary>
    [Fact]
    public void The_reasons_are_verbatim_and_the_build_reason_names_the_key_that_stops_it()
    {
        Assert.Equal("Available once the running task finishes.", UpdateText.WaitForTask);
        Assert.Equal("Available once Sync finishes.", UpdateText.WaitForSync);
        Assert.Equal("Available once the build finishes — Esc stops it.", UpdateText.WaitForBuild);
        Assert.Contains(ShortcutCatalog.Get(ShortcutId.Escape).Gestures[0] + " stops it.", UpdateText.WaitForBuild,
            StringComparison.Ordinal);
    }

    /// <summary>[F-M1] Koşu nedeninin metni Stop'un aşamasını izler: istenmeden önce eski cümle, graceful gittikten sonra bir
    /// sonraki Esc'in hard olduğunu söyleyen cümle, hard gittikten sonra Esc'siz cümle. Her geçiş duyurulur (kart satırı ve
    /// komut kapısı tazelenir) — aşama değişimi, iş kilidinin değişimi kadar bir tetikleyicidir.</summary>
    [Fact]
    public async Task The_build_reason_follows_the_stop_stage_and_each_step_is_announced()
    {
        var vm = NewVm();
        int reasonChanges = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RunViewModel.UpdateRestartBlockedReason)) reasonChanges++;
        };
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        Assert.Equal(StopStage.Stop, vm.StopStage); // ön-koşul
        Assert.Equal(UpdateText.WaitForBuildAt(StopStage.Stop), vm.UpdateRestartBlockedReason);
        int afterStart = reasonChanges;

        await vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(StopStage.StopNow, vm.StopStage); // ön-koşul: graceful gitti
        Assert.Equal(UpdateText.WaitForBuildAt(StopStage.StopNow), vm.UpdateRestartBlockedReason);
        Assert.Equal(afterStart + 1, reasonChanges);

        await vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(StopStage.Terminating, vm.StopStage); // ön-koşul: hard gitti
        Assert.Equal(UpdateText.WaitForBuildAt(StopStage.Terminating), vm.UpdateRestartBlockedReason);
        Assert.Equal(afterStart + 2, reasonChanges);
        Assert.False(vm.RestartToUpdateCommand.CanExecute(null)); // koşu bitmedi: kilit sürer
    }

    /// <summary>[F-M1] Aşama metinleri birebir; durduran tuşun adı kataloğun jestiyle yazılır. Terminating'de tuşa HİÇ
    /// değinilmez: o aşamada Esc hiçbir şey yapmaz.</summary>
    [Fact]
    public void The_stop_stage_texts_are_verbatim_and_the_terminating_one_names_no_key()
    {
        string esc = ShortcutCatalog.Get(ShortcutId.Escape).Gestures[0];

        Assert.Equal(UpdateText.WaitForBuild, UpdateText.WaitForBuildAt(StopStage.Stop));
        Assert.Equal("Available once the build stops — Esc stops it now.", UpdateText.WaitForBuildAt(StopStage.StopNow));
        Assert.Contains(esc + " stops it now.", UpdateText.WaitForBuildStopping, StringComparison.Ordinal);
        Assert.Equal("Available once the build stops.", UpdateText.WaitForBuildAt(StopStage.Terminating));
        Assert.DoesNotContain(esc, UpdateText.WaitForBuildTerminating, StringComparison.Ordinal);
    }

    /// <summary>İş bitince düğme kendiliğinden açılır: neden düşer ve yalnız DEĞİŞTİĞİNDE duyurulur (koşunun başı ve
    /// sonu — iki bildirim), komutun kapısı da aynı anlarda yeniden sorulur.</summary>
    [Fact]
    public void The_restart_opens_by_itself_when_the_build_ends_and_each_change_is_announced_once()
    {
        var vm = NewVm();
        int reasonChanges = 0, gateChanges = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RunViewModel.UpdateRestartBlockedReason)) reasonChanges++;
        };
        vm.RestartToUpdateCommand.CanExecuteChanged += (_, _) => gateChanges++;

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        Assert.Equal(UpdateText.WaitForBuild, vm.UpdateRestartBlockedReason);
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 1_000));

        Assert.Null(vm.UpdateRestartBlockedReason);
        Assert.True(vm.RestartToUpdateCommand.CanExecute(null));
        Assert.Equal(2, reasonChanges);
        Assert.True(gateChanges >= 2, $"komut kapısı yeniden sorulmadı ({gateChanges})");
    }

    /// <summary>Görev bitip devrettiği Sync de bitince açılır: Clean → zincirli Sync → topoloji.</summary>
    [Fact]
    public void The_restart_opens_after_a_clean_and_the_sync_it_hands_over_to()
    {
        var vm = NewVm();
        Assert.True(CommandPress.Press(vm.CleanCommand));
        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));
        Assert.Equal(UpdateText.WaitForTask, vm.UpdateRestartBlockedReason);

        vm.OnEvent(new CleanCompletedEvent(1, 2, 1_024, 0, 1)); // devir: zincirli Sync istenir
        Assert.Equal(UpdateText.WaitForSync, vm.UpdateRestartBlockedReason);

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        VmTopology.Seed(vm);
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0));
        Assert.Null(vm.UpdateRestartBlockedReason);
        Assert.True(vm.RestartToUpdateCommand.CanExecute(null));
    }

    /// <summary>Teklif yoksa kurulacak bir şey yoktur — Restart kapalıdır.</summary>
    [Fact]
    public void Without_an_offer_there_is_nothing_to_restart_into()
    {
        var vm = NewVm();
        Assert.True(vm.RestartToUpdateCommand.CanExecute(null)); // ön-koşul: teklif varken açık

        vm.AvailableUpdate = null;

        Assert.False(vm.RestartToUpdateCommand.CanExecute(null));
    }

    /// <summary>Restart'a basmak kabuğa bir istek yayar (restart ekranı ona bağlanır); VM başka hiçbir şey yapmaz —
    /// motora komut gitmez.</summary>
    [Fact]
    public void Restart_to_update_raises_one_request_for_the_shell_and_sends_nothing()
    {
        var vm = NewVm();
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;
        int requests = 0;
        vm.RestartToUpdateRequested += (_, _) => requests++;

        Assert.True(CommandPress.Press(vm.RestartToUpdateCommand));

        Assert.Equal(1, requests);
        Assert.Empty(sent);
    }

    /// <summary>Kapı komutun KENDİSİNDEDİR: kapısı kapalıyken kapıdan geçmeden gelen bir çağrı da (doğrudan
    /// <c>Execute</c>) istek yaymaz — kilit (bir koşu sürüyor) ya da kurulacak teklifin yokluğu restart ekranını hiçbir
    /// yoldan açtırmaz; kabuk kararı ikinci kez yazmaz.</summary>
    [Fact]
    public void A_restart_invoked_past_its_closed_gate_raises_no_request()
    {
        var vm = NewVm();
        int requests = 0;
        vm.RestartToUpdateRequested += (_, _) => requests++;

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        Assert.False(vm.RestartToUpdateCommand.CanExecute(null)); // ön-koşul
        vm.RestartToUpdateCommand.Execute(null);
        Assert.Equal(0, requests);

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 1_000));
        vm.AvailableUpdate = null;
        vm.RestartToUpdateCommand.Execute(null);
        Assert.Equal(0, requests);
    }

    /// <summary>Uygulama teklifsiz açılır — Sync'lenmiş, boşta bir workspace'te bile: teklifi yalnız motor yazar ve
    /// kurulacak bir şey yokken Restart kapalıdır.
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski iddia: "uygulama örnek teklifle açılır" (<c>UpdateOffer.Sample</c> —
    /// motor yokken hapı hep gösteren placeholder, plan U1). Gerekçe: motor geldi; hap yalnız indirilmiş bir teklif
    /// varken görünür (tasarım §2.12 ilkesi). Placeholder kalktı.</para></summary>
    [Fact]
    public void The_app_starts_without_an_offer()
    {
        var vm = NewVmWithoutOffer();
        Assert.Null(vm.AvailableUpdate);
        Assert.False(vm.RestartToUpdateCommand.CanExecute(null));
    }
}
