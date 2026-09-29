using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı bildirimi 2026-09-29] Bir workspace işi (Sync — kendiliğinden olanı dahil —, Clean, Optimize, checkout,
/// pull) sürerken basılan koşu komutu KAYBOLMAZ: basış bir istektir, düğme hemen Stop olur ve komut iş bitince gider.
///
/// <para><b>Ölçülen kusur:</b> Clean, Optimize ya da Resolve sürerken pencereden çıkıp dönen kullanıcının Build
/// tıklaması, dönüşün kendiliğinden başlattığı Sync'e çarpıp yutuluyordu — ilk tık düğme parlakken, ikincisi
/// sönükken; ancak Sync bitince basılan üçüncü tık çalışıyordu. Aynısı bir koşu sırasında pencereden çıkılıp koşu
/// biterken basılan Build'de, dışarıda branch değiştirilip dönülünce ve Clean/Optimize'ın zincirlediği Sync'te de
/// oluyordu (sonuncusunda düğme en azından sönüktü).</para>
///
/// <para>İstek geri alınır: Stop/Esc, tam çıkış, branch değişimi, motor kaybı; ayrıca beklenen iş istenen şeyi
/// getirmeden biterse (Sync ya da bakım işi düştü, checkout branch'i değiştirmedi, pull ağacı ilerletmedi, liste ya da
/// satırın hedefi gelmedi) — iptal satırı işin kendi hata satırlarının ALTINA düşer. Tam çıkış beklerken istek
/// yapılamaz.</para>
///
/// <para>Harness: gönderimler sahte ama CANLI bir motora gider (<see cref="RunViewModel.DebugSendOverride"/>) —
/// başlatılmamış bir motorda her gönderim senkron düşer ve iş hiç sürmezdi. Tıklama WPF'in yaptığı gibi yapılır:
/// kapısı kapalı komut çalıştırılmaz (<see cref="CommandPress"/>). HEAD ve izleyici sahtedir, saat enjekte edilir.</para>
/// </summary>
public class RunRequestWaitsForWorkTests
{
    private const string A = VmTopology.DefaultProjectId;
    private const string HeadSha = "1111111111111111111111111111111111111111";

    private sealed class Rig
    {
        public long Now = 1_000;
        public string HeadBranch = "main";
        public RunViewModel Vm = null!;
        public readonly List<IpcCommand> Sent = [];
        public readonly Queue<Action> Posted = new();

        public IEnumerable<StartRunCommand> Runs => Sent.OfType<StartRunCommand>();
        public IEnumerable<SyncWorkspaceCommand> Syncs => Sent.OfType<SyncWorkspaceCommand>();

        public void Drain()
        {
            while (Posted.Count > 0) Posted.Dequeue()();
        }
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

    private static string LastConsoleLine(RunViewModel vm) =>
        vm.GetRunDocumentText().Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1];

    // ---------------------------------------------------------------- iş sürerken basılan koşu, iş bitince başlar

    /// <summary>Resolve cycles sürerken pencereden çıkıldı; dönüş koşu bitene dek bekletildi ve koşu biter bitmez sessiz
    /// bir Sync başladı. Tam o an basılan Build tutulur ve Sync bitince gider.</summary>
    [Fact]
    public void A_build_pressed_as_a_run_ends_after_leaving_the_window_starts_when_the_automatic_sync_ends()
    {
        var rig = NewRig();
        rig.Vm.OnEvent(new RunStartedEvent("r0", RunMode.Cycles, 1, 4, "Debug"));
        rig.Now += 200_000;
        rig.Vm.OnWindowActivated(); // koşu sürerken dönüş: tetik bekletilir
        rig.Vm.OnEvent(new RunCompletedEvent("r0", RunOutcome.Completed, 1, 0, 0, 0, 1_000));
        rig.Drain();                // koşu bitti → bekleyen tetik → sessiz Sync
        Assert.Single(rig.Syncs);   // ön-koşul

        Assert.True(CommandPress.Press(rig.Vm.BuildCommand));
        Assert.True(rig.Vm.IsStarting); // tık tutuldu: düğme Stop oldu
        Assert.Empty(rig.Runs);         // Sync bitmeden komut gitmez

        AnswerSync(rig.Vm);

        Assert.Equal(RunMode.Build, Assert.Single(rig.Runs).Mode);
    }

    /// <summary>Süpürge Clean'i silerken basılan Build, Clean'in devrettiği Sync de bitince gider — Clean'in bitişi
    /// tek başına yetmez: liste o Sync'le geri gelir.</summary>
    [Fact]
    public void A_build_pressed_during_a_clean_starts_after_the_sync_the_clean_hands_over_to()
    {
        var rig = NewRig();
        Assert.True(CommandPress.Press(rig.Vm.CleanCommand));
        rig.Vm.OnEvent(new CleanStartedEvent(@"D:\repo"));

        Assert.True(CommandPress.Press(rig.Vm.BuildCommand));
        Assert.True(rig.Vm.IsStarting);

        rig.Vm.OnEvent(new CleanCompletedEvent(1, 2, 1_024, 0, 1)); // devir: zincirli Sync istenir
        Assert.Single(rig.Syncs);
        Assert.Empty(rig.Runs); // Clean bitti ama Sync sürüyor

        AnswerSync(rig.Vm);

        Assert.Equal(RunMode.Build, Assert.Single(rig.Runs).Mode);
    }

    /// <summary>Optimize Clean'le aynı akışı oynar: sürerken basılan Build, devrettiği Sync bitince gider.</summary>
    [Fact]
    public void A_build_pressed_during_an_optimize_starts_after_the_sync_the_optimize_hands_over_to()
    {
        var rig = NewRig();
        Assert.True(CommandPress.Press(rig.Vm.OptimizeCommand));
        rig.Vm.OnEvent(new OptimizeStartedEvent(@"D:\repo"));

        Assert.True(CommandPress.Press(rig.Vm.BuildCommand));

        rig.Vm.OnEvent(new OptimizeCompletedEvent(1));
        Assert.Single(rig.Syncs);
        Assert.Empty(rig.Runs);

        AnswerSync(rig.Vm);

        Assert.Equal(RunMode.Build, Assert.Single(rig.Runs).Mode);
    }

    /// <summary>Dışarıda branch değişti ve kullanıcı döndü: dönüş görünür bir BranchChange Sync'i başlatır. O sırada
    /// basılan Build yeni branch'in Sync'i bitince gider.</summary>
    [Fact]
    public void A_build_pressed_during_the_sync_of_an_outside_branch_switch_starts_when_it_ends()
    {
        var rig = NewRig();
        rig.HeadBranch = "feature";
        ReturnToTheWindow(rig);

        Assert.True(CommandPress.Press(rig.Vm.BuildCommand));
        Assert.Empty(rig.Runs);

        AnswerSync(rig.Vm, "feature");

        Assert.Equal(RunMode.Build, Assert.Single(rig.Runs).Mode);
    }

    /// <summary><c>N behind</c> chip'inin pull'u sürerken basılan Build, pull'un zincirlediği Sync bitince gider.</summary>
    [Fact]
    public void A_build_pressed_during_a_pull_starts_after_the_sync_the_pull_hands_over_to()
    {
        var rig = NewRig();
        rig.Vm.OnEvent(Synced() with { Behind = 2 }); // chip görünür
        Assert.True(CommandPress.Press(rig.Vm.PullRepositoryCommand));

        Assert.True(CommandPress.Press(rig.Vm.BuildCommand));

        rig.Vm.OnEvent(new PullCompletedEvent(Succeeded: true)); // Sync zincirlenir
        Assert.Empty(rig.Runs);

        AnswerSync(rig.Vm);

        Assert.Equal(RunMode.Build, Assert.Single(rig.Runs).Mode);
    }

    /// <summary>Satırın play düğmesi de bekler: hedef satır hemen Stop'a döner (hedef tıklamada yazılır) ve koşu Sync
    /// bitince yalnız o projeyle gider.</summary>
    [Fact]
    public void A_row_run_pressed_during_a_sync_keeps_its_target_and_starts_scoped_when_it_ends()
    {
        var rig = NewRig();
        ReturnToTheWindow(rig);

        Assert.True(CommandPress.Press(rig.Vm.BuildProjectCommand, A));
        Assert.Equal(A, rig.Vm.RunTargetId);

        AnswerSync(rig.Vm);

        Assert.Equal(A, Assert.Single(rig.Runs).ScopeProjectId);
    }

    /// <summary>Build menüsünün Rebuild'i ve Clean'i ile bakım kutusunun Resolve cycles'ı da AYNI kapıdan geçer:
    /// Sync sürerken basılırlarsa beklerler ve kendi modlarıyla giderler.</summary>
    [Theory]
    [InlineData(RunMode.Rebuild)]
    [InlineData(RunMode.Clean)]
    [InlineData(RunMode.Cycles)]
    public void Every_run_command_pressed_during_a_sync_waits_and_keeps_its_mode(RunMode mode)
    {
        var rig = NewRig();
        rig.Vm.OnEvent(new WorkspaceTopologyEvent( // Resolve cycles'ın ön-koşulu: grafikte bir döngü var
            [
                new ProjectNode(@"C:\p\a.csproj", "A", @"C:\p\a.csproj", ["Osys"], [], 0, null, null, true, null),
                new ProjectNode(@"C:\p\b.csproj", "B", @"C:\p\b.csproj", ["Osys"], [], 1, null, null, true, null),
            ],
            [[@"C:\p\a.csproj", @"C:\p\b.csproj"]], [], []));
        ReturnToTheWindow(rig);
        System.Windows.Input.ICommand command = mode switch
        {
            RunMode.Rebuild => rig.Vm.RebuildCommand,
            RunMode.Clean => rig.Vm.CleanAllCommand,
            _ => rig.Vm.BuildCyclesCommand,
        };

        Assert.True(CommandPress.Press(command));
        Assert.Empty(rig.Runs);

        AnswerSync(rig.Vm);

        Assert.Equal(mode, Assert.Single(rig.Runs).Mode);
    }

    /// <summary>Bekleyen istek konsola tek satırla kaydedilir — koşunun kendi açılışından farklı olarak konsolu
    /// TEMİZLEMEZ: o an konsol süren işin (burada Sync'in) transkriptidir.</summary>
    [Fact]
    public void A_build_that_waits_says_so_in_the_console_without_clearing_it()
    {
        var rig = NewRig();
        rig.Vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main")); // Sync düğmesinin Sync'i — transkripti görünür
        rig.Vm.OnEvent(new SyncProgressEvent("scanning the workspace", "info"));

        Assert.True(CommandPress.Press(rig.Vm.BuildCommand));

        Assert.Contains("scanning the workspace", rig.Vm.GetRunDocumentText(), StringComparison.Ordinal);
        Assert.StartsWith(RunViewModel.RunRequestedLine(RunMode.Build), LastConsoleLine(rig.Vm), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- isteğin geri alındığı yollar

    /// <summary>Beklerken Stop isteği geri alır: motora ne koşu ne stop gider, konsol neden olmadığını söyler.</summary>
    [Fact]
    public void Stop_while_a_build_waits_takes_the_request_back_and_nothing_is_sent()
    {
        var rig = NewRig();
        ReturnToTheWindow(rig);
        Assert.True(CommandPress.Press(rig.Vm.BuildCommand));

        Assert.True(CommandPress.Press(rig.Vm.StopCommand));

        Assert.False(rig.Vm.IsStarting);
        Assert.Equal(RunViewModel.RunCancelledLine, LastConsoleLine(rig.Vm));
        AnswerSync(rig.Vm);
        Assert.Empty(rig.Runs);
        Assert.Empty(rig.Sent.OfType<StopRunCommand>());
    }

    /// <summary>İsteği geri almak süren işin fazına dokunmaz: görünür bir Sync hâlâ <c>Syncing</c>'tir.</summary>
    [Fact]
    public void Taking_back_a_waiting_build_leaves_the_phase_of_the_running_sync_alone()
    {
        var rig = NewRig();
        rig.Vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.True(CommandPress.Press(rig.Vm.BuildCommand));

        Assert.True(CommandPress.Press(rig.Vm.StopCommand));

        Assert.Equal(AppPhase.Syncing, rig.Vm.Phase);
    }

    /// <summary>Motor beklerken giderse istek de gider — yeniden başlatılan motorun Sync'i onu başlatmaz. Konsol
    /// isteğin neden olmadığını söyler: son satır "…it starts when the work in flight finishes" olarak kalamaz.</summary>
    [Fact]
    public void Losing_the_engine_while_a_build_waits_drops_the_request()
    {
        var rig = NewRig();
        ReturnToTheWindow(rig);
        Assert.True(CommandPress.Press(rig.Vm.BuildCommand));

        rig.Vm.OnEngineExited(1);

        Assert.False(rig.Vm.IsStarting);
        Assert.Equal(RunViewModel.RunCancelledLine, LastConsoleLine(rig.Vm));
        AnswerSync(rig.Vm);
        Assert.Empty(rig.Runs);
    }

    /// <summary>Satırın isteği beklerken o proje listeden çıktıysa (başka branch'e geçildi, proje silindi) koşu
    /// başlatılamaz: hedefi olmayan kapsamlı koşuyu motor "not in plan" diye reddederdi, açılış koreografisi ise
    /// hedef yerine bütün Build kapsamını işaretlerdi. İstek geri alınır.</summary>
    [Fact]
    public void A_row_run_whose_project_is_gone_when_the_work_ends_is_taken_back()
    {
        var rig = NewRig();
        ReturnToTheWindow(rig);
        Assert.True(CommandPress.Press(rig.Vm.BuildProjectCommand, A));

        rig.Vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        VmTopology.Seed(rig.Vm, @"C:\p\b.csproj"); // Sync'in listesinde A artık yok
        rig.Vm.OnEvent(Synced());

        Assert.Empty(rig.Runs);
        Assert.False(rig.Vm.IsStarting);
        Assert.Equal(RunViewModel.RunCancelledLine, LastConsoleLine(rig.Vm));
    }

    /// <summary>Önceki koşunun geç gelen sonu (burada host'un geç onayladığı bir stop) bekleyen isteğe ait değildir:
    /// isteği düşürmez, Stop düğmesini Build'e çevirmez ve süren Sync'in fazını ezmez. İstek kendi kimliğiyle bekler.</summary>
    [Fact]
    public void A_late_end_of_the_previous_run_leaves_a_waiting_build_alone()
    {
        var rig = NewRig();
        rig.Vm.OnEvent(new RunStartedEvent("r0", RunMode.Build, 1, 4, "Debug"));
        rig.Vm.OnEvent(new RunCompletedEvent("r0", RunOutcome.Completed, 1, 0, 0, 0, 1_000));
        rig.Vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.True(CommandPress.Press(rig.Vm.BuildCommand));

        rig.Vm.OnEvent(new RunStoppedEvent("r0", WasHard: false));

        Assert.True(rig.Vm.IsStarting);
        Assert.Equal(AppPhase.Syncing, rig.Vm.Phase);
        AnswerSync(rig.Vm);
        Assert.Single(rig.Runs);
    }

    /// <summary>
    /// Tam çıkış beklerken (Close to tray kapalı, ×) yeni bir koşu istenemez: bekleyiş iş bitince kapanmak içindir, o
    /// sırada basılan bir Build ya çıkışı bir tam derleme boyunca bekletir ya da hiç başlamayıp çıkışı asılı bırakırdı.
    /// Ölçülen yol üretimdekidir: Clean'in Sync'e devri <see cref="RunViewModel.OperationHold"/>'un zamanlayıcısından döner,
    /// yani iş bir motor olayının DIŞINDA biter; çıkış beklerken Sync başlamaz ve kapı orada açılır.
    /// </summary>
    [Fact]
    public void Nothing_can_be_queued_behind_a_pending_exit_and_the_exit_closes_when_the_work_ends()
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

    /// <summary><c>N behind</c> chip'inin pull'u reddedilirse (kirli ağaç, ayrışmış branch) istenen ağaç gelmedi: bekleyen
    /// koşu çekilmemiş ağacı derlemez, geri alınır. Reddin gerekçesi konsolda kalır, iptal satırı ALTINA düşer — koşunun
    /// açılışı onu silmez.</summary>
    [Fact]
    public void A_build_waiting_on_a_refused_pull_is_taken_back_and_the_refusal_stays_readable()
    {
        var rig = NewRig();
        rig.Vm.OnEvent(Synced() with { Behind = 2 });
        Assert.True(CommandPress.Press(rig.Vm.PullRepositoryCommand));
        Assert.True(CommandPress.Press(rig.Vm.BuildCommand));

        rig.Vm.OnEvent(new SyncProgressEvent("warning: pull refused — the working tree has local changes", "warn"));
        rig.Vm.OnEvent(new PullCompletedEvent(Succeeded: false, RefusalReason: PullRefusalReason.Dirty));

        Assert.False(rig.Vm.IsStarting);
        Assert.Empty(rig.Runs);
        Assert.Contains("warning: pull refused", rig.Vm.GetRunDocumentText(), StringComparison.Ordinal);
        Assert.Equal(RunViewModel.RunCancelledLine, LastConsoleLine(rig.Vm));
    }

    /// <summary>İş bittiğinde ortada proje listesi yoksa (Clean düştü, listeyi getirecek Sync zincirlenmedi) istek
    /// başlatılamaz: geri alınır ve konsol söyler.</summary>
    [Fact]
    public void A_build_waiting_on_a_clean_that_fails_is_taken_back_for_want_of_a_project_list()
    {
        var rig = NewRig();
        Assert.True(CommandPress.Press(rig.Vm.CleanCommand)); // tıklama listeyi boşaltır
        rig.Vm.OnEvent(new CleanStartedEvent(@"D:\repo"));
        Assert.True(CommandPress.Press(rig.Vm.BuildCommand));

        rig.Vm.OnEvent(new ErrorEvent("cleanFailed", "the root is gone"));

        Assert.False(rig.Vm.IsStarting);
        Assert.Empty(rig.Runs);
        Assert.Equal(RunViewModel.RunCancelledLine, LastConsoleLine(rig.Vm));
    }

    /// <summary>Beklerken tam çıkış istenirse istek geri alınır ve uygulama iş bitince kapanır — arkada başlamamış bir
    /// koşu bırakılmaz.</summary>
    [Fact]
    public void An_exit_asked_while_a_build_waits_takes_the_build_back_and_closes_when_the_work_ends()
    {
        var rig = NewRig();
        ReturnToTheWindow(rig);
        Assert.True(CommandPress.Press(rig.Vm.BuildCommand));
        bool ready = false;
        rig.Vm.ExitReady += (_, _) => ready = true;

        rig.Vm.RequestExit();

        Assert.False(rig.Vm.IsStarting);
        Assert.False(ready); // Sync hâlâ sürüyor
        AnswerSync(rig.Vm);
        Assert.True(ready);
        Assert.Empty(rig.Runs);
    }

    /// <summary>Beklerken dışarıda HEAD oynarsa (checkout, pull) istek, açılış koreografisinde bekleyen bir koşu gibi
    /// geri alınır: derlenecek ağaç tıklamadaki ağaç değildir.</summary>
    [Fact]
    public async Task A_branch_change_while_a_build_waits_takes_the_request_back()
    {
        var rig = NewRig();
        ReturnToTheWindow(rig);
        Assert.True(CommandPress.Press(rig.Vm.BuildCommand));

        await rig.Vm.AutoSync!.HeadTriggerAsync(HeadMove.Other);

        Assert.False(rig.Vm.IsStarting);
        AnswerSync(rig.Vm);
        Assert.Empty(rig.Runs);
    }

    // ---------------------------------------------------------------- beklerken gelen Sync hatası

    /// <summary>Beklerken gelen <c>planFailed</c> Sync'indir — koşunun komutu henüz gitmedi. Sync kapıyı bırakır ve hata
    /// şeride Sync'in hatası olarak yazılır; eski ayrım (<c>IsStarting</c> açıksa hata koşunundur) burada Sync'i sonsuza
    /// dek "uçuşta" bırakırdı. Beklenen iş düştüğü için istek de geri alınır: aynı planlama koşunun kendisinde de düşerdi
    /// ve açılışı hatanın satırını silerdi — hata konsolda kalır, iptal satırı altına düşer.</summary>
    [Fact]
    public void A_sync_failure_while_a_build_waits_is_the_syncs_and_takes_the_build_back()
    {
        var rig = NewRig();
        ReturnToTheWindow(rig);
        Assert.True(CommandPress.Press(rig.Vm.BuildCommand));
        rig.Vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));

        rig.Vm.OnEvent(new ErrorEvent("planFailed", "a project file could not be read"));

        Assert.Equal("a project file could not be read", rig.Vm.SyncErrorMessage);
        Assert.False(rig.Vm.SyncBusy);
        Assert.False(rig.Vm.IsStarting);
        Assert.Empty(rig.Runs);
        Assert.Contains("[error] planFailed", rig.Vm.GetRunDocumentText(), StringComparison.Ordinal);
        Assert.Equal(RunViewModel.RunCancelledLine, LastConsoleLine(rig.Vm));
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
