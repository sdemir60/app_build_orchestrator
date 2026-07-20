using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [It-4a Foundation] MotionSettings: SystemParameters.ClientAreaAnimation sinyalini soyutlayan IMotionSignal
/// enjekte edilir (D8 — gerçek SystemParameters'a bağlanmadan, STA gerektirmez). AnimationsEnabled canlı yayılır;
/// Effective(token) kaynak kapalıyken 0 döner. WPF resource-swap tarafı (Attach/ResourceDictionary) [StaFact]
/// MotionResourcesTests'te ayrıca test edilir (bu dosya yalnız saf servis mantığı).
/// </summary>
public class ReducedMotionTests
{
    // FakeMotionSignal: bkz. FakeMotionSignal.cs (MotionResourcesTests ile paylaşılan tek tanım).

    [Fact]
    public void AnimationsEnabled_reflects_the_injected_signal_at_construction()
    {
        var signal = new FakeMotionSignal { AnimationsEnabled = false };
        var settings = new MotionSettings(signal);

        Assert.False(settings.AnimationsEnabled);
    }

    [Fact]
    public void Effective_returns_zero_when_signal_is_off()
    {
        var signal = new FakeMotionSignal { AnimationsEnabled = false };
        var settings = new MotionSettings(signal);

        Assert.Equal(TimeSpan.Zero, settings.Effective(TimeSpan.FromMilliseconds(180)));
    }

    [Fact]
    public void Effective_returns_the_token_duration_when_signal_is_on()
    {
        var signal = new FakeMotionSignal { AnimationsEnabled = true };
        var settings = new MotionSettings(signal);

        Assert.Equal(TimeSpan.FromMilliseconds(180), settings.Effective(TimeSpan.FromMilliseconds(180)));
    }

    [Fact]
    public void AnimationsEnabled_updates_live_when_signal_raises_Changed()
    {
        var signal = new FakeMotionSignal { AnimationsEnabled = true };
        var settings = new MotionSettings(signal);
        Assert.True(settings.AnimationsEnabled);

        signal.AnimationsEnabled = false;
        signal.Raise();

        Assert.False(settings.AnimationsEnabled);
        Assert.Equal(TimeSpan.Zero, settings.Effective(TimeSpan.FromMilliseconds(280)));
    }

    [Fact]
    public void AnimationsEnabledChanged_event_fires_exactly_once_per_Changed_raise()
    {
        var signal = new FakeMotionSignal { AnimationsEnabled = true };
        var settings = new MotionSettings(signal);
        int fired = 0;
        settings.AnimationsEnabledChanged += (_, _) => fired++;

        signal.AnimationsEnabled = false;
        signal.Raise();

        Assert.Equal(1, fired);
    }
}
