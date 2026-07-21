using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T56/3b] CascadeScheduler (SAF, Stopwatch-enjekte): proje-log kaskatı — 26ms'de 3 satır tempo (başlangıç 2),
/// satır başına 140ms opacity-fade; flash yok (açığa çıkmadan opacity 0). Reduced-motion → hepsi t=0'da tam opak.
/// </summary>
public class CascadeSchedulerTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void RevealedAt_starts_at_two_then_adds_three_every_26ms()
    {
        var s = new CascadeScheduler(totalLines: 100, animationsEnabled: true);

        Assert.Equal(2, s.RevealedAt(TimeSpan.Zero));   // prototip: useState(2)
        Assert.Equal(5, s.RevealedAt(Ms(26)));          // +3
        Assert.Equal(8, s.RevealedAt(Ms(52)));          // +3
        Assert.Equal(11, s.RevealedAt(Ms(78)));         // +3
    }

    [Fact]
    public void RevealedAt_is_monotonic_and_clamped_to_total()
    {
        var s = new CascadeScheduler(totalLines: 4, animationsEnabled: true);

        int prev = 0;
        for (double t = 0; t <= 500; t += 5)
        {
            int r = s.RevealedAt(Ms(t));
            Assert.True(r >= prev, "açığa çıkma monoton olmalı");
            Assert.True(r <= 4, "toplamı aşamaz");
            prev = r;
        }
        Assert.Equal(4, s.RevealedAt(Ms(500))); // sonunda hepsi
    }

    [Fact]
    public void OpacityOf_starts_at_zero_when_revealed_no_flash_then_ramps_over_140ms()
    {
        var s = new CascadeScheduler(totalLines: 10, animationsEnabled: true);

        // Satır 0 t=0'da açığa çıkar ama opacity 0'DAN başlar (flash yok).
        Assert.Equal(0.0, s.OpacityOf(0, TimeSpan.Zero), 3);
        Assert.Equal(0.5, s.OpacityOf(0, Ms(70)), 3);   // yarı yol
        Assert.Equal(1.0, s.OpacityOf(0, Ms(140)), 3);  // fade tamam
        Assert.Equal(1.0, s.OpacityOf(0, Ms(400)), 3);  // sonrasında tam opak
    }

    [Fact]
    public void OpacityOf_is_zero_before_a_line_is_revealed_no_premature_paint()
    {
        var s = new CascadeScheduler(totalLines: 10, animationsEnabled: true);

        // Satır index 5 → 52ms'de açığa çıkar (2 + 3*2 = 8 > 5). Öncesinde opacity 0.
        Assert.Equal(Ms(52), s.RevealTimeOf(5));
        Assert.Equal(0.0, s.OpacityOf(5, Ms(20)), 3);   // henüz açığa çıkmadı → görünmez
        Assert.Equal(0.0, s.OpacityOf(5, Ms(52)), 3);   // açıldığı AN opacity 0 (flash yok)
        Assert.Equal(1.0, s.OpacityOf(5, Ms(52 + 140)), 3);
    }

    [Fact]
    public void RevealTimeOf_groups_three_lines_per_step()
    {
        var s = new CascadeScheduler(totalLines: 20, animationsEnabled: true);

        Assert.Equal(TimeSpan.Zero, s.RevealTimeOf(0));
        Assert.Equal(TimeSpan.Zero, s.RevealTimeOf(1));
        Assert.Equal(Ms(26), s.RevealTimeOf(2));
        Assert.Equal(Ms(26), s.RevealTimeOf(3));
        Assert.Equal(Ms(26), s.RevealTimeOf(4));
        Assert.Equal(Ms(52), s.RevealTimeOf(5));
    }

    [Fact]
    public void Duration_is_last_reveal_plus_fade_and_IsComplete_tracks_it()
    {
        var s = new CascadeScheduler(totalLines: 8, animationsEnabled: true);
        // index 7 → ceil((7+1-2)/3)=2 step → 52ms; +140 fade = 192ms.
        Assert.Equal(Ms(192), s.Duration);
        Assert.False(s.IsComplete(Ms(191)));
        Assert.True(s.IsComplete(Ms(192)));
    }

    [Fact]
    public void ReducedMotion_reveals_all_instantly_with_full_opacity_and_no_duration()
    {
        var s = new CascadeScheduler(totalLines: 50, animationsEnabled: false);

        Assert.True(s.Instant);
        Assert.Equal(50, s.RevealedAt(TimeSpan.Zero));
        Assert.Equal(1.0, s.OpacityOf(0, TimeSpan.Zero), 3);
        Assert.Equal(1.0, s.OpacityOf(49, TimeSpan.Zero), 3);
        Assert.Equal(TimeSpan.Zero, s.Duration);
        Assert.True(s.IsComplete(TimeSpan.Zero));
    }

    [Fact]
    public void Fewer_than_two_lines_reveals_all_immediately()
    {
        var s = new CascadeScheduler(totalLines: 1, animationsEnabled: true);
        Assert.Equal(1, s.RevealedAt(TimeSpan.Zero)); // min(2,1)=1
        Assert.Equal(0, new CascadeScheduler(0, true).RevealedAt(TimeSpan.Zero));
    }

    [Fact]
    public void Lines_outside_the_cascade_span_are_fully_opaque()
    {
        // Canlı sonradan eklenen satır (index >= total) fade edilmez — tam opak.
        var s = new CascadeScheduler(totalLines: 3, animationsEnabled: true);
        Assert.Equal(1.0, s.OpacityOf(3, TimeSpan.Zero), 3);
        Assert.Equal(1.0, s.OpacityOf(10, Ms(5)), 3);
    }
}
