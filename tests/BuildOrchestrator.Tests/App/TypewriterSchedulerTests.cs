using System.Linq;
using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T56/3a] TypewriterScheduler: SAF, elapsed-bazlı (tick-sayısından bağımsız) daktilo tempo hesabı — açığa çıkan
/// karakter sayısı monoton, satır ≤250ms'de tamamlanır; reduced-motion iken INSTANT (t=0'da tam satır, blink yok).
/// Enjekte edilen saat = TimeSpan argümanı (D8: gerçek DispatcherTimer/sleep YOK).
/// </summary>
public class TypewriterSchedulerTests
{
    [Fact]
    public void Revealed_count_is_monotonic_and_completes_within_250ms()
    {
        const string text = "OSYS.Sales.Core failed — 2 errors (3.1s)"; // 40 karakter
        var sch = new TypewriterScheduler(text.Length, animationsEnabled: true);

        Assert.Equal(0, sch.RevealedAt(TimeSpan.Zero)); // t=0'da hiçbir karakter (daktilo baştan)

        int prev = -1;
        for (double ms = 0; ms <= 300; ms += 3.9) // ~DispatcherTimer'dan daha ince adımlarla tara
        {
            int n = sch.RevealedAt(TimeSpan.FromMilliseconds(ms));
            Assert.True(n >= prev, $"monoton değil: {ms}ms'de {n} < {prev}");
            Assert.InRange(n, 0, text.Length);
            prev = n;
        }

        Assert.True(sch.Duration <= TimeSpan.FromMilliseconds(250), $"süre {sch.Duration.TotalMilliseconds}ms > 250ms");
        Assert.True(sch.IsCompleteAt(sch.Duration));
        Assert.Equal(text.Length, sch.RevealedAt(sch.Duration));
    }

    [Fact]
    public void Long_line_still_completes_within_250ms()
    {
        var sch = new TypewriterScheduler(textLength: 2000, animationsEnabled: true);
        Assert.True(sch.Duration <= TimeSpan.FromMilliseconds(250));
        Assert.Equal(2000, sch.RevealedAt(sch.Duration));
        Assert.Equal(2000, sch.RevealedAt(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Reduced_motion_is_instant_full_line_at_t0_and_no_duration()
    {
        var sch = new TypewriterScheduler(textLength: 42, animationsEnabled: false);

        Assert.True(sch.Instant);
        Assert.Equal(42, sch.RevealedAt(TimeSpan.Zero)); // t=0'da TAM satır (blink yok, anında görünür)
        Assert.True(sch.IsCompleteAt(TimeSpan.Zero));
        Assert.Equal(TimeSpan.Zero, sch.Duration);
    }

    [Fact]
    public void Reveal_actually_progresses_across_the_typing_window_not_all_at_once()
    {
        var sch = new TypewriterScheduler(textLength: 44, animationsEnabled: true);

        // Yarı süre civarında satır ne 0 ne de tam olmalı — gerçekten daktilo ediliyor.
        var half = TimeSpan.FromMilliseconds(sch.Duration.TotalMilliseconds / 2);
        int mid = sch.RevealedAt(half);
        Assert.InRange(mid, 1, 43);
    }

    [Fact]
    public void Zero_length_line_is_immediately_complete_without_throwing()
    {
        var sch = new TypewriterScheduler(textLength: 0, animationsEnabled: true);
        Assert.Equal(0, sch.RevealedAt(TimeSpan.Zero));
        Assert.True(sch.IsCompleteAt(TimeSpan.Zero));
        Assert.Equal(TimeSpan.Zero, sch.Duration);
    }
}
