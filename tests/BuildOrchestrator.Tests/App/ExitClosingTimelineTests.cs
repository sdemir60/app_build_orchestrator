using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [Exit] Kapanış ekranının zaman çizelgesi (<see cref="ExitClosingTimeline"/>) — saf çekirdek. İki adım: işin durdurulması
/// (<see cref="ExitClosingStep.Stopping"/>) ve süreçlerin bitirilmesi (<see cref="ExitClosingStep.Ending"/>). İlk adım en az
/// <see cref="ExitClosingTimeline.StoppingMinMs"/> görünür (okunabilsin diye) ve VM'in çıkışı hazır demesini bekler — o arada çubuk
/// <see cref="ExitClosingTimeline.StoppingEndPercent"/>'te durur; hazır olunca ikinci adım <see cref="ExitClosingTimeline.EndingMs"/>'de
/// çubuğu doldurur ve çizelge biter. Kapanış ancak çizelge bitince istenir — ekran ilk kez çizilmeden pencere kapanmasın diye
/// (restart ekranının gerekçesi).
/// </summary>
public sealed class ExitClosingTimelineTests
{
    [Fact]
    public void The_stopping_step_fills_half_the_bar_and_holds_until_the_exit_is_ready()
    {
        var start = ExitClosingTimeline.At(0, stoppedAtMs: null);
        Assert.Equal((ExitClosingStep.Stopping, 0.0, false), (start.Step, start.Percent, start.Done));

        var mid = ExitClosingTimeline.At(ExitClosingTimeline.StoppingMinMs / 2, stoppedAtMs: null);
        Assert.Equal(ExitClosingTimeline.StoppingEndPercent / 2, mid.Percent, precision: 6);

        var held = ExitClosingTimeline.At(10_000, stoppedAtMs: null);
        Assert.Equal((ExitClosingStep.Stopping, ExitClosingTimeline.StoppingEndPercent, false), (held.Step, held.Percent, held.Done));
    }

    /// <summary>Çıkış hemen hazır olsa da (Sync kesildi) ilk adım en az <see cref="ExitClosingTimeline.StoppingMinMs"/> görünür.</summary>
    [Fact]
    public void An_exit_ready_at_once_still_shows_the_stopping_step_for_its_minimum()
    {
        var frame = ExitClosingTimeline.At(ExitClosingTimeline.StoppingMinMs - 1, stoppedAtMs: 0);
        Assert.Equal(ExitClosingStep.Stopping, frame.Step);

        var ending = ExitClosingTimeline.At(ExitClosingTimeline.StoppingMinMs, stoppedAtMs: 0);
        Assert.Equal((ExitClosingStep.Ending, ExitClosingTimeline.StoppingEndPercent, false), (ending.Step, ending.Percent, ending.Done));
    }

    [Fact]
    public void Once_the_exit_is_ready_the_ending_step_fills_the_bar_and_the_timeline_ends()
    {
        const double stoppedAt = 1_400;

        var half = ExitClosingTimeline.At(stoppedAt + ExitClosingTimeline.EndingMs / 2, stoppedAt);
        Assert.Equal(ExitClosingStep.Ending, half.Step);
        Assert.Equal((ExitClosingTimeline.StoppingEndPercent + 100) / 2, half.Percent, precision: 6);
        Assert.False(half.Done);

        var full = ExitClosingTimeline.At(stoppedAt + ExitClosingTimeline.EndingMs, stoppedAt);
        Assert.Equal((ExitClosingStep.Ending, 100.0, true), (full.Step, full.Percent, full.Done));

        var after = ExitClosingTimeline.At(stoppedAt + 10_000, stoppedAt);
        Assert.Equal((100.0, true), (after.Percent, after.Done));
    }

    [Fact]
    public void A_clock_that_goes_back_reads_as_the_start()
    {
        Assert.Equal(0.0, ExitClosingTimeline.At(-50, stoppedAtMs: null).Percent);
    }
}
