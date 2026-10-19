using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [Exit] Kapanış ekranının Exit kipi (<see cref="ClosingScreen.PlayExit"/>): "Stop and exit"ten sonra bütün pencereyi örten
/// <c>surface-base</c> zemin, 180ms'de belirir; ortada 232px'lik kolon — 30px logo, "Closing Build Orchestrator" (13px/600), mono 11px
/// <c>text-dim</c> bağlam ("Build · 14/38"), 2px amber çubuk, 11px <c>text-faint</c> adım: "Stopping build…", ardından "Ending 5
/// processes…". Restart kipiyle AYNI kontrol ve kolondur (<see cref="UpdateRestartScreenTests"/> ölçüleri pinler); fark başlık, orta satır
/// ve zaman çizelgesidir (<see cref="ExitClosingTimeline"/>): ikinci adım ancak kabuk çıkışın hazır olduğunu bildirince
/// (<see cref="ClosingScreen.MarkStopped"/>) başlar, çubuk dolunca <see cref="ClosingScreen.BarFilled"/> bir kez gelir.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class ExitClosingScreenTests
{
    private sealed record Rig(ClosingScreen Screen, ClosingScreenTime Time, Window Window);

    private static Rig PlayExit(string stopping = "Stopping build…", string context = "Build · 14/38",
        string ending = "Ending 5 processes…")
    {
        var host = DsResources.NewHost();
        var screen = new ClosingScreen();
        var time = new ClosingScreenTime(screen);
        var window = DsResources.Realize(host, screen, 800, 600);
        screen.PlayExit(stopping, context, ending);
        screen.UpdateLayout();
        return new Rig(screen, time, window);
    }

    [StaFact]
    public void The_exit_screen_names_the_app_closing_and_the_work_in_mono()
    {
        var rig = PlayExit();
        var screen = rig.Screen;

        Assert.True(screen.IsShowing);
        Assert.True(screen.IsPlayingExit);
        Assert.Equal("Closing " + AppIdentity.Product, screen.PART_Heading.Text);
        Assert.Equal(ExitText.ClosingHeading, System.Windows.Automation.AutomationProperties.GetName(screen));
        Assert.Equal(Visibility.Collapsed, screen.PART_Transition.Visibility); // sürüm geçişi restart'ındır

        var context = screen.PART_Context;
        Assert.Equal(Visibility.Visible, context.Visibility);
        Assert.Equal("Build · 14/38", context.Text);
        Assert.Equal(AppFonts.Mono, context.FontFamily);
        Assert.Equal(11.0, context.FontSize);
        Assert.Equal(DsResources.TokenColor(screen, "Brush.TextDim"), DsResources.ColorOf(context.Foreground));
        Assert.Equal(6.0, DsResources.BoundsIn(context, screen.PART_Column).Top
            - DsResources.BoundsIn(screen.PART_Heading, screen.PART_Column).Bottom, precision: 1);
        Assert.Equal("Stopping build…", screen.PART_Step.Text);
        Assert.Equal(0.0, screen.PART_Progress.Value);
        GC.KeepAlive(rig.Window);
    }

    [StaFact]
    public void The_stopping_step_holds_until_the_exit_is_ready_then_the_ending_step_fills_the_bar_once()
    {
        var rig = PlayExit();
        var screen = rig.Screen;
        int filled = 0;
        screen.BarFilled += () => filled++;

        rig.Time.FrameAt(5_000); // çıkış henüz hazır değil: çubuk yarıda bekler
        Assert.Equal("Stopping build…", screen.PART_Step.Text);
        Assert.Equal(ExitClosingTimeline.StoppingEndPercent, screen.PART_Progress.Value);
        Assert.Equal(0, filled);

        screen.MarkStopped(); // 5000. ms'de
        rig.Time.FrameAt(5_000 + ExitClosingTimeline.EndingMs / 2);
        Assert.Equal("Ending 5 processes…", screen.PART_Step.Text);
        Assert.Equal(0, filled);

        rig.Time.FrameAt(5_000 + ExitClosingTimeline.EndingMs);
        Assert.Equal(100.0, screen.PART_Progress.Value);
        Assert.Equal(1, filled);
        Assert.False(rig.Time.Timer.IsRunning); // ekran kalır, zamanlayıcı durur
        Assert.True(screen.IsShowing);
        GC.KeepAlive(rig.Window);
    }

    [StaFact]
    public void Each_step_is_announced_once()
    {
        var rig = PlayExit();
        var screen = rig.Screen;
        rig.Time.FrameAt(100);
        rig.Time.FrameAt(200);
        screen.MarkStopped();
        rig.Time.FrameAt(ExitClosingTimeline.StoppingMinMs + 10);
        rig.Time.FrameAt(ExitClosingTimeline.StoppingMinMs + 20);

        Assert.Equal(2, screen.StepAnnouncements); // Stopping + Ending, kare başına değil
        GC.KeepAlive(rig.Window);
    }

    /// <summary>Restart kipi aynı kontrolde orta satırı sürüm geçişine çevirir — exit'in bağlam satırı görünmez.</summary>
    [StaFact]
    public void The_restart_screen_keeps_its_version_transition_and_hides_the_context()
    {
        var host = DsResources.NewHost();
        var screen = new ClosingScreen();
        _ = new ClosingScreenTime(screen);
        var window = DsResources.Realize(host, screen, 800, 600);

        screen.PlayRestart(AppIdentity.Version, "9.9.0");

        Assert.False(screen.IsPlayingExit);
        Assert.Equal(Visibility.Visible, screen.PART_Transition.Visibility);
        Assert.Equal(Visibility.Collapsed, screen.PART_Context.Visibility);
        Assert.Equal(UpdateText.RestartHeading, screen.PART_Heading.Text);
        GC.KeepAlive(window);
    }

    /// <summary>Kapanış ekranında MarkStopped restart kipinde bir şey yapmaz — restart'ın çizelgesi sabittir.</summary>
    [StaFact]
    public void Mark_stopped_does_nothing_on_the_restart_screen()
    {
        var host = DsResources.NewHost();
        var screen = new ClosingScreen();
        var time = new ClosingScreenTime(screen);
        var window = DsResources.Realize(host, screen, 800, 600);
        screen.PlayRestart(AppIdentity.Version, "9.9.0");

        screen.MarkStopped();
        time.FrameAt(UpdateRestartTimeline.TotalMs / 2);

        Assert.Equal(UpdateText.RestartStepLabel(UpdateRestartStep.Closing), screen.PART_Step.Text);
        Assert.Equal(50.0, screen.PART_Progress.Value, precision: 3);
        GC.KeepAlive(window);
    }
}
