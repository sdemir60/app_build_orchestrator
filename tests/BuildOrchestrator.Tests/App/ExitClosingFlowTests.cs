using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [Exit] "Stop and exit"ten kapanışa kabuğun akışı: kart kapanır, kapanış ekranı (<see cref="ClosingScreen.PlayExit"/>) işin adı ve
/// ilerlemesiyle açılır, VM işi durdurur (<see cref="RunViewModel.RequestExit"/>); VM çıkışın hazır olduğunu söyleyince
/// (<see cref="RunViewModel.ExitReady"/> — motorun <c>runStopped</c>'ı, durdurma süresinin dolması ya da kesilen bir iş) ekran ikinci adıma
/// geçer ("Ending N processes…" — N kartın saydığı süreçler) ve uygulama ancak çubuk dolunca kapanır — bir kez. Kart açıkken iş bittiyse
/// kartın "Exit"i ekransız, hemen kapatır. Ekran görünürken pencere klavyeyi yok sayar (restart ekranıyla AYNI kural ve katman).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class ExitClosingFlowTests
{
    private sealed class Rig
    {
        public MainWindow Window { get; }
        public RunViewModel Vm { get; }
        public ClosingScreenTime Time { get; }
        public List<IpcCommand> Sent { get; } = [];
        public ExitSeamFakes Fakes { get; }
        public int Shutdowns => Fakes.Shutdowns;

        public Rig(TempDir temp)
        {
            (Window, Vm, _) = MainWindowHost.NewWithProjects(temp, ("A", null), ("B", null));
            Fakes = new ExitSeamFakes(Window); // kartın sayımı: 4 + 1 = 5 süreç
            Time = new ClosingScreenTime(Window.ClosingOverlay);
            MainWindowHost.AcceptSends(Vm);
            Vm.DebugOnCommandSent = Sent.Add;
        }

        public ClosingScreen Screen => Window.ClosingOverlay;

        /// <summary>İki projelik bir Build, biri bitti: <c>1/2</c>.</summary>
        public void StartBuild()
        {
            MainWindowHost.StartBuild(Vm, "A", "B");
            MainWindowHost.StartProject(Vm, "A");
            MainWindowHost.StartProject(Vm, "B");
            MainWindowHost.SucceedProject(Vm, "A");
            Vm.WillBuildCount = 2; // olaylardan SONRA: her proje olayı koşu yüzeyini önizlemeden yeniden hesaplar (burada önizleme yok)
            Vm.FinishedOfWillBuild = 1;
        }

        public void OpenCardAndStopAndExit()
        {
            CommandPress.Click(Window.ExitButton);
            Assert.True(DsChrome.GetIsActive(Window.ExitButton), "ön-koşul: kart açılmadı");
            CommandPress.Click(Window.ExitCardView.PART_Action);
        }
    }

    [StaFact]
    public void Stop_and_exit_plays_the_closing_screen_with_the_work_and_its_progress()
    {
        using var temp = new TempDir();
        var rig = new Rig(temp);
        rig.StartBuild();

        rig.OpenCardAndStopAndExit();

        Assert.False(DsChrome.GetIsActive(rig.Window.ExitButton)); // kart kapandı
        Assert.True(rig.Screen.IsPlayingExit);
        Assert.Equal("Build · 1/2", rig.Screen.PART_Context.Text);
        Assert.Equal("Stopping build…", rig.Screen.PART_Step.Text);
        Assert.Equal(StopKind.Hard, Assert.Single(rig.Sent.OfType<StopRunCommand>()).Kind);
        Assert.Equal(0, rig.Shutdowns);
        GC.KeepAlive(rig.Window);
    }

    /// <summary>Uygulama motorun cevabını bekler, sonra çubuk dolunca kapanır: cevap gelmeden kareler kapatmaz; cevap ikinci adımı
    /// başlatır ("Ending 5 processes…" — kartın saydığı 4 + 1); çubuk dolunca TEK kapanış.</summary>
    [StaFact]
    public void The_app_closes_once_the_engine_answers_and_the_bar_fills()
    {
        using var temp = new TempDir();
        var rig = new Rig(temp);
        rig.StartBuild();
        rig.OpenCardAndStopAndExit();

        rig.Time.FrameAt(ExitClosingTimeline.StoppingMinMs + ExitClosingTimeline.EndingMs + 100);
        DispatcherPump.DrainToIdle();
        Assert.Equal(0, rig.Shutdowns); // motor henüz cevap vermedi
        Assert.Equal("Stopping build…", rig.Screen.PART_Step.Text);

        rig.Vm.OnEvent(new RunStoppedEvent("r1", WasHard: true)); // ExitReady → ekran ikinci adıma
        long answeredAt = rig.Time.Now - ClosingScreenTime.StartMs;
        rig.Time.FrameAt(answeredAt + 10);
        Assert.Equal("Ending 5 processes…", rig.Screen.PART_Step.Text);
        Assert.Equal(0, rig.Shutdowns);

        rig.Time.FrameAt(answeredAt + ExitClosingTimeline.EndingMs);
        DispatcherPump.PumpUntil(() => rig.Shutdowns > 0, TimeSpan.FromSeconds(2));
        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, rig.Shutdowns);
        GC.KeepAlive(rig.Window);
    }

    /// <summary>Kesilen iş (Sync) beklenmez: çıkış aynı çağrıda hazırdır, ama ekran ilk adımı en az kendi süresi kadar gösterir; uygulama
    /// çizelge bitince kapanır.</summary>
    [StaFact]
    public async Task A_sync_cut_off_by_exit_still_shows_both_steps_then_closes()
    {
        using var temp = new TempDir();
        var rig = new Rig(temp);
        await rig.Vm.SyncCommand.ExecuteAsync(null);
        Assert.Equal(ExitWork.Sync, rig.Vm.ExitWork); // ön-koşul

        rig.OpenCardAndStopAndExit();
        Assert.Equal("Sync", rig.Screen.PART_Context.Text);
        Assert.Equal("Stopping sync…", rig.Screen.PART_Step.Text);

        rig.Time.FrameAt(ExitClosingTimeline.StoppingMinMs - 1);
        Assert.Equal(0, rig.Shutdowns);
        rig.Time.FrameAt(ExitClosingTimeline.StoppingMinMs + 1);
        Assert.Equal("Ending 5 processes…", rig.Screen.PART_Step.Text);
        rig.Time.FrameAt(ExitClosingTimeline.StoppingMinMs + ExitClosingTimeline.EndingMs);
        DispatcherPump.PumpUntil(() => rig.Shutdowns > 0, TimeSpan.FromSeconds(2));
        Assert.Equal(1, rig.Shutdowns);
        GC.KeepAlive(rig.Window);
    }

    /// <summary>Kart açıkken iş bittiyse kartın "Exit"i ekransız, hemen kapatır — karar düğmeye basıldığı ANDAKİ işe göredir.</summary>
    [StaFact]
    public void With_the_work_over_the_cards_exit_quits_at_once_without_the_screen()
    {
        using var temp = new TempDir();
        var rig = new Rig(temp);
        rig.StartBuild();
        CommandPress.Click(rig.Window.ExitButton);
        rig.Vm.OnEvent(new RunStoppedEvent("r1", WasHard: false));
        rig.Vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 1, 0, 0, 1, 400));
        Assert.Equal("Exit", rig.Window.ExitCardView.PART_ActionLabel.Text); // ön-koşul: kart güncellendi

        CommandPress.Click(rig.Window.ExitCardView.PART_Action);
        DispatcherPump.PumpUntil(() => rig.Shutdowns > 0, TimeSpan.FromSeconds(2));

        Assert.False(rig.Screen.IsShowing);
        Assert.Equal(1, rig.Shutdowns);
        GC.KeepAlive(rig.Window);
    }

    /// <summary>Kapanış ekranı görünürken pencere klavyeyi yok sayar — restart ekranının kuralı, aynı katman: Esc seçimi temizlemez.
    /// Kontrol ekran açılmadan ÖNCE yapılır: aynı Esc seçimi temizler.</summary>
    [StaFact]
    public void The_window_ignores_the_keyboard_while_the_closing_screen_shows()
    {
        using var temp = new TempDir();
        var rig = new Rig(temp);
        rig.StartBuild();
        var anchor = new Border();
        var keepAlive = DsResources.Realize(DsResources.NewHost(), anchor);
        var source = PresentationSource.FromVisual(anchor)!;
        rig.Vm.SelectProject(MainWindowHost.IdOf("A"));
        KeyInput.Press(rig.Window, Key.Escape, source);
        Assert.Null(rig.Vm.SelectedProjectId); // kontrol: ekran yokken Esc bağlamaya ulaşır
        rig.Vm.SelectProject(MainWindowHost.IdOf("A"));

        rig.OpenCardAndStopAndExit();
        var esc = KeyInput.Press(rig.Window, Key.Escape, source);

        Assert.True(esc.Handled);
        Assert.Equal(MainWindowHost.IdOf("A"), rig.Vm.SelectedProjectId);
        GC.KeepAlive(rig.Window);
        GC.KeepAlive(keepAlive);
    }
}
