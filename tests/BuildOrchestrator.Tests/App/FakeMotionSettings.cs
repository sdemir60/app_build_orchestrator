using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Motion sinyalini (<see cref="IMotionSettings"/>) enjekte edilebilir kılan paylaşılan test double'ı —
/// açık/kapalı sürülebilir ve <see cref="Flip"/> ile CANLI değişim yükseltir.
///
/// <para>Tanım <c>GraphRenderTests</c>'in içinde private duruyordu; ikinci bir tüketici çıkınca kopya
/// olurdu (CLAUDE.md: ortak fixture tek yerde). <c>MotionOwnerHygieneTests.CountingMotion</c> AYRI kalır —
/// o sinyali sürmez, ABONE SAYISINI gözler.</para>
/// </summary>
internal sealed class FakeMotionSettings : IMotionSettings
{
    public bool AnimationsEnabled { get; set; }

    public event EventHandler? AnimationsEnabledChanged;

    public TimeSpan Effective(TimeSpan token) => AnimationsEnabled ? token : TimeSpan.Zero;

    public void Flip(bool enabled)
    {
        AnimationsEnabled = enabled;
        AnimationsEnabledChanged?.Invoke(this, EventArgs.Empty);
    }
}
