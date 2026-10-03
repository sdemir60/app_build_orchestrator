using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı kararı 2026-10-02] Bir workspace işi (Sync — kendiliğinden olanı dahil —, Clean, Optimize, checkout,
/// pull) sürerken koşu komutları KAPALIDIR: Build, Rebuild, Build menüsünün Clean'i, Resolve cycles ve satır komutları
/// basılamaz; basış kuyruğa alınmaz, konsola satır düşmez, düğme Stop olmaz. İş bitince kapı kendiliğinden açılır
/// (<c>NotifySyncGatedCommands</c>). Motorun kendi zincirleri (Clean → Sync, Optimize → Sync, pull → Sync, checkout →
/// Sync) bu kuraldan etkilenmez: kapı zincirin son Sync'i bitince açılır.
///
/// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-02]</b> Eski iddia ([kullanıcı bildirimi 2026-09-29], bu dosyanın
/// eski adı <c>RunRequestWaitsForWorkTests</c>): iş sürerken basılan koşu KAYBOLMAZ — basış bir istektir, düğme hemen
/// Stop olur ve komut iş bitince gider; Stop/Esc, tam çıkış, branch değişimi ve motor kaybı isteği geri alır. O iddia
/// ölçülen bir kusurdan doğmuştu: pencereye dönüşün kendiliğinden başlattığı sessiz Sync, Clean/Optimize/Resolve'dan
/// dönen kullanıcının Build'ini yutuyordu.</para>
///
/// <para><b>Değişme gerekçesi:</b> kuyruk yanlış stratejiydi. Sync sürerken ekran Sync modundadır ve pasif düğmeler
/// tıklanamaz; kullanıcı tıklamasının tutulduğunu görmez. Ölçülen kusur başka iki şeyle karşılanır: tepsideyken yok sayılan
/// kısayol için balon ve pencereye dönüşün Sync'inin kısalması (gizli modda ekran işi yok). Kuyruğu pinleyen testler
/// SİLİNDİ — pinledikleri davranış artık yok: iş bitince başlayan Build (her iş türü için), Stop'un isteği geri alması,
/// motor kaybının isteği düşürmesi, geç gelen koşu sonunun bekleyen Build'e dokunmaması, beklenen iş düşünce ya da
/// satırın hedefi kaybolunca geri alma, tam çıkışın ve branch değişiminin isteği geri alması, beklerken gelen Sync
/// hatasının atfı ve isteğin konsol satırı. Aynı ailenin başka dosyalardaki testleri de silindi: arka plandaki pencereye Build
/// tıklamasıyla dönüş (<c>ActionBarTests</c>), checkout düşünce bekleyen Build'in geri alınması ve checkout'un Sync'inden
/// sonra başlaması (<c>BranchCheckoutTests</c>), Sync sürerken basılan koşunun beklemesi (<c>RunViewModelStateTests</c>) ve
/// bekleyen Build'in pill'i canlandırmaması (<c>StickyRibbonTests</c>). Bunların yerine kapının işin tüm türlerinde kapalı, iş bitince açık olduğu
/// aşağıda pinlenir; <c>CancelPendingRun</c> yalnız açılış koreografisi penceresi için kalır (kendi testleri
/// <c>GraphFilterRunSuspendTests</c>'te).</para>
///
/// <para>Harness: gönderimler sahte ama CANLI bir motora gider (<see cref="RunViewModel.DebugSendOverride"/>) —
/// başlatılmamış bir motorda her gönderim senkron düşer ve iş hiç sürmezdi. Tıklama WPF'in yaptığı gibi yapılır:
/// kapısı kapalı komut çalıştırılmaz (<see cref="CommandPress"/>). HEAD ve izleyici sahtedir, saat enjekte edilir.</para>
/// </summary>
public class RunRequestDuringWorkTests
{
    private const string A = VmTopology.DefaultProjectId;
    private const string HeadSha = "1111111111111111111111111111111111111111";

    private static readonly BranchRef FeatureX = new("feature/x", "bbbbbbbbbbbb", false, false);

    /// <summary>Koşu komutlarını kapatan workspace işleri: Sync (düğmeden ve kendiliğinden), Clean, Optimize, checkout, pull.</summary>
    public enum Work { ManualSync, SilentSync, Clean, Optimize, Checkout, Pull }

    private sealed class Rig
    {
        public long Now = 1_000;
        public string HeadBranch = "main";
        public RunViewModel Vm = null!;
        public readonly List<IpcCommand> Sent = [];
        public readonly Queue<Action> Posted = new();

        public IEnumerable<StartRunCommand> Runs => Sent.OfType<StartRunCommand>();
        public IEnumerable<SyncWorkspaceCommand> Syncs => Sent.OfType<SyncWorkspaceCommand>();
    }

    public static TheoryData<Work> AllWork()
    {
        var data = new TheoryData<Work>();
        foreach (var work in Enum.GetValues<Work>()) data.Add(work);
        return data;
    }

    public static TheoryData<Work, RunMode> WorkAndRunMode()
    {
        var data = new TheoryData<Work, RunMode>();
        foreach (var work in Enum.GetValues<Work>())
            foreach (var mode in new[] { RunMode.Build, RunMode.Rebuild, RunMode.Clean, RunMode.Cycles })
                data.Add(work, mode);
        return data;
    }

    /// <summary>Sync'lenmiş tek satırlı bir workspace: kendiliğinden Sync açık, motor canlı, son Sync şimdi.</summary>
    private static Rig NewRig()
    {
        var rig = new Rig();
        rig.Vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), MainWindowHost.NeverTickingBatcher(),
            () => "r1", () => rig.Now) { RootPath = @"D:\repo" };
        rig.Vm.DebugSendOverride = _ => Task.CompletedTask;
        VmTopology.Seed(rig.Vm);
        rig.Vm.OnEvent(Synced());
        rig.Vm.EnableAutoSync(rig.Posted.Enqueue, _ => new HeadState(rig.HeadBranch, HeadSha), () => new FakeHeadWatcher());
        rig.Vm.DebugOnCommandSent = rig.Sent.Add;
        return rig;
    }

    private static SyncCompletedEvent Synced(string branch = "main") =>
        new(branch, "sha1234", false, 1, 0, HeadSha: HeadSha, ActiveBranch: branch);

    /// <summary>Resolve cycles'ın ön-koşulu: grafikte bir döngü var — kapıyı iş kapatsın, döngüsüzlük değil.</summary>
    private static void SeedCycle(Rig rig) =>
        rig.Vm.OnEvent(new WorkspaceTopologyEvent(
            [
                new ProjectNode(@"C:\p\a.csproj", "A", @"C:\p\a.csproj", ["Osys"], [], 0, null, null, true, null),
                new ProjectNode(@"C:\p\b.csproj", "B", @"C:\p\b.csproj", ["Osys"], [], 1, null, null, true, null),
            ],
            [[@"C:\p\a.csproj", @"C:\p\b.csproj"]], [], []));

    /// <summary>Kullanıcı bir dakika başka pencerede kaldı ve döndü: dönüş sessiz bir Sync ister.</summary>
    private static void ReturnToTheWindow(Rig rig)
    {
        rig.Now += 60_000;
        rig.Vm.OnWindowActivated();
        Assert.Single(rig.Syncs); // ön-koşul: kendiliğinden Sync gerçekten istendi
    }

    /// <summary>Motorun bir Sync'i baştan sona cevaplaması: başladı, topoloji, bitti.</summary>
    private static void AnswerSync(RunViewModel vm, string branch = "main")
    {
        vm.OnEvent(new SyncStartedEvent(@"D:\repo", branch));
        VmTopology.Seed(vm);
        vm.OnEvent(Synced(branch));
    }

    /// <summary>İşi, ÜRETİMDEKİ girişinden başlatır ve motorun "başladı" cevabını verir: iş sürüyor.</summary>
    private static async Task Begin(Rig rig, Work work)
    {
        var vm = rig.Vm;
        switch (work)
        {
            case Work.ManualSync:
                Assert.True(CommandPress.Press(vm.SyncCommand));
                vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
                break;
            case Work.SilentSync:
                ReturnToTheWindow(rig);
                vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
                break;
            case Work.Clean:
                Assert.True(CommandPress.Press(vm.CleanCommand));
                vm.OnEvent(new CleanStartedEvent(@"D:\repo"));
                break;
            case Work.Optimize:
                Assert.True(CommandPress.Press(vm.OptimizeCommand));
                vm.OnEvent(new OptimizeStartedEvent(@"D:\repo"));
                break;
            case Work.Checkout:
                vm.OnEvent(new BranchListEvent([new BranchRef("main", "aaaaaaaaaaaa", IsActive: true, IsRemoteTracking: false), FeatureX]));
                await vm.SelectBranch(FeatureX);
                Assert.True(vm.CheckoutBusy); // ön-koşul
                break;
            case Work.Pull:
                vm.OnEvent(Synced() with { Behind = 2 }); // chip görünür
                Assert.True(CommandPress.Press(vm.PullRepositoryCommand));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(work), work, null);
        }
        // Ön-koşul, her iş türü için aynı soru: Sync kapısı workspace işi sürerken kapalıdır.
        Assert.False(vm.SyncCommand.CanExecute(null), $"{work}: precondition — the work must be in flight");
    }

    /// <summary>İşin bitişi, motorun zincirlediği Sync dahil: o Sync de bitince workspace boşalır.</summary>
    private static void End(Rig rig, Work work)
    {
        var vm = rig.Vm;
        switch (work)
        {
            case Work.ManualSync:
            case Work.SilentSync:
                AnswerSync(vm);
                break;
            case Work.Clean:
                vm.OnEvent(new CleanCompletedEvent(1, 2, 1_024, 0, 1)); // devir: zincirli Sync istenir
                AnswerSync(vm);
                break;
            case Work.Optimize:
                vm.OnEvent(new OptimizeCompletedEvent(1)); // devir: zincirli Sync istenir
                AnswerSync(vm);
                break;
            case Work.Checkout:
                vm.OnEvent(new CheckoutCompletedEvent(CheckoutStatus.Switched, "main", "feature/x", HeadSha, 0, null, null));
                AnswerSync(vm, "feature/x");
                break;
            case Work.Pull:
                vm.OnEvent(new PullCompletedEvent(Succeeded: true)); // Sync zincirlenir
                AnswerSync(vm);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(work), work, null);
        }
    }

    private static System.Windows.Input.ICommand RunCommand(RunViewModel vm, RunMode mode) => mode switch
    {
        RunMode.Build => vm.BuildCommand,
        RunMode.Rebuild => vm.RebuildCommand,
        RunMode.Clean => vm.CleanAllCommand,
        _ => vm.BuildCyclesCommand,
    };

    // ---------------------------------------------------------------- iş sürerken koşu komutları kapalı

    /// <summary>Build, Rebuild, Build menüsünün Clean'i ve Resolve cycles, workspace'in her iş türünde kapalıdır: komut
    /// çalışmaz, motora hiçbir şey gitmez, konsola satır düşmez, düğme Stop olmaz (<see cref="RunViewModel.IsStarting"/>
    /// açılmaz). Clean ve Optimize kendi tıklamalarında listeyi de boşaltır; kapıyı kapatan yalnız o değildir —
    /// Sync, checkout ve pull listeyi boşaltmaz ve yine kapalıdır.</summary>
    [Theory]
    [MemberData(nameof(WorkAndRunMode))]
    public async Task Every_run_command_is_not_executable_while_a_sync_clean_optimize_checkout_or_pull_is_in_flight(
        Work work, RunMode mode)
    {
        var rig = NewRig();
        SeedCycle(rig);
        await Begin(rig, work);
        int sends = 0;
        rig.Vm.DebugSendOverride = _ => { sends++; return Task.CompletedTask; };
        string console = rig.Vm.GetRunDocumentText();
        var command = RunCommand(rig.Vm, mode);

        Assert.False(command.CanExecute(null), $"{work}/{mode}: the run gate must be closed while the work runs");
        Assert.False(CommandPress.Press(command));

        Assert.Equal(0, sends);                                   // motora hiçbir şey gitmedi
        Assert.Empty(rig.Runs);
        Assert.Equal(console, rig.Vm.GetRunDocumentText());       // konsola satır düşmedi
        Assert.False(rig.Vm.IsStarting);                          // düğme Stop olmadı
    }

    /// <summary>Satırın play düğmesi ve ⋯ menüsü de AYNI kapıdan geçer: iş sürerken kapalıdır, hedef satır Stop'a dönmez
    /// (hedef tıklamada yazılırdı).</summary>
    [Theory]
    [MemberData(nameof(AllWork))]
    public async Task Row_run_commands_are_not_executable_while_the_work_is_in_flight(Work work)
    {
        var rig = NewRig();
        await Begin(rig, work);
        System.Windows.Input.ICommand[] rowCommands =
            [rig.Vm.BuildProjectCommand, rig.Vm.RebuildProjectCommand, rig.Vm.CleanProjectCommand];

        foreach (var command in rowCommands)
            Assert.False(CommandPress.Press(command, A), $"{work}: a row command must be closed while the work runs");

        Assert.Null(rig.Vm.RunTargetId);
        Assert.False(rig.Vm.IsStarting);
        Assert.Empty(rig.Runs);
    }

    /// <summary>Sessiz (kendiliğinden) Sync de kapıyı kapatır ve Sync düğmesi standart meşgul hâlini (amber + spinner —
    /// aksiyon barı <see cref="RunViewModel.SyncBusy"/>'yi okur) gösterir; sessiz olduğu için faz
    /// <see cref="AppPhase.Syncing"/>'e GEÇMEZ, ekran yeniden kurulmaz.</summary>
    [Fact]
    public void A_silent_sync_locks_the_run_buttons_and_shows_the_sync_button_busy()
    {
        var rig = NewRig();
        ReturnToTheWindow(rig); // sessiz Sync istendi
        rig.Vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));

        Assert.True(rig.Vm.SyncBusy);
        Assert.False(rig.Vm.BuildCommand.CanExecute(null));
        Assert.False(rig.Vm.RebuildCommand.CanExecute(null));
        Assert.NotEqual(AppPhase.Syncing, rig.Vm.Phase);
    }

    // ---------------------------------------------------------------- iş bitince kapı açılır

    /// <summary>İş bitince — Clean, Optimize, pull ve checkout'ta motorun zincirlediği Sync de bitince — kapı
    /// yeniden sorulur ve açılır; kullanıcının basışı hemen koşar. Kapıyı açan şey koşunun kendiliğinden başlaması değil,
    /// kullanıcının basışıdır: iş biterken hiçbir koşu başlamaz.</summary>
    [Theory]
    [MemberData(nameof(AllWork))]
    public async Task Run_commands_become_executable_again_when_the_work_ends(Work work)
    {
        var rig = NewRig();
        await Begin(rig, work);
        int told = 0;
        rig.Vm.BuildCommand.CanExecuteChanged += (_, _) => told++;
        Assert.False(rig.Vm.BuildCommand.CanExecute(null)); // ön-koşul

        End(rig, work);

        Assert.True(told > 0, $"{work}: the gate must be asked again when the work ends");
        Assert.Empty(rig.Runs); // iş bitti ama hiçbir koşu kendiliğinden başlamadı
        Assert.True(rig.Vm.BuildCommand.CanExecute(null));
        Assert.True(CommandPress.Press(rig.Vm.BuildCommand));
        Assert.Equal(RunMode.Build, Assert.Single(rig.Runs).Mode);
    }

    /// <summary>
    /// Tam çıkış beklerken (Close to tray kapalı, ×) koşu kapısı kapalıdır ve çıkış iş bitince kapanır: bekleyiş iş bitince
    /// kapanmak içindir, o sırada başlayan bir koşu çıkışı bir tam derleme boyunca bekletirdi. Ölçülen yol üretimdekidir:
    /// Clean'in Sync'e devri <see cref="RunViewModel.OperationHold"/>'un zamanlayıcısından döner, yani iş bir motor
    /// olayının DIŞINDA biter; çıkış beklerken Sync başlamaz ve kapı orada açılır.
    /// </summary>
    [Fact]
    public void The_exit_closes_when_the_work_ends_and_no_run_starts_behind_it()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null); // bekleyişin devamı, zamanlayıcı gibi tamamlayanda koşar
        try
        {
            var rig = NewRig();
            var holds = new Queue<TaskCompletionSource>();
            rig.Vm.OperationHold = _ =>
            {
                var hold = new TaskCompletionSource();
                holds.Enqueue(hold);
                return hold.Task;
            };
            Assert.True(CommandPress.Press(rig.Vm.CleanCommand));
            rig.Vm.OnEvent(new CleanStartedEvent(@"D:\repo"));
            bool ready = false;
            rig.Vm.ExitReady += (_, _) => ready = true;
            rig.Vm.RequestExit(); // Clean sürerken tam çıkış — iş bitince kapanır

            Assert.False(CommandPress.Press(rig.Vm.BuildCommand));

            rig.Vm.OnEvent(new CleanCompletedEvent(1, 2, 1_024, 0, 1));
            while (holds.TryDequeue(out var hold)) hold.SetResult(); // adım ve boşluk — devir olayın dışında biter
            Assert.True(ready);
            Assert.Empty(rig.Runs);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    // ---------------------------------------------------------------- Sync isteği kapıları o anda kapatır

    /// <summary>Bir Sync İSTENDİĞİ anda (motor henüz <c>syncStarted</c> demeden) Sync'in kapattığı her kontrol yeniden
    /// sorulur: bakım kutusu, <c>N behind</c> chip'i ve branch chip'i o anda söner. Ölçülen kusur: istekte yalnız Sync
    /// düğmesi haber alıyordu; ötekiler motor cevap verene dek canlı görünüp tıklamayı yutuyordu.</summary>
    [Fact]
    public void A_sync_request_requeries_the_controls_it_closes_at_once()
    {
        var rig = NewRig();
        var told = new HashSet<string>(StringComparer.Ordinal);
        rig.Vm.CleanCommand.CanExecuteChanged += (_, _) => told.Add("clean");
        rig.Vm.OptimizeCommand.CanExecuteChanged += (_, _) => told.Add("optimize");
        rig.Vm.PullRepositoryCommand.CanExecuteChanged += (_, _) => told.Add("pull");
        rig.Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RunViewModel.CanSwitchBranch)) told.Add("branch");
        };
        Assert.True(rig.Vm.CleanCommand.CanExecute(null)); // ön-koşul

        ReturnToTheWindow(rig); // istek — syncStarted henüz yok

        Assert.False(rig.Vm.CleanCommand.CanExecute(null));
        Assert.Contains("clean", told);
        Assert.Contains("optimize", told);
        Assert.Contains("pull", told);
        Assert.Contains("branch", told);
    }
}
