using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [It-4a Foundation] Paylaşılan test double'ı: IMotionSignal'i (SystemParameters.ClientAreaAnimation
/// soyutlaması) gerçek SystemParameters'a dokunmadan enjekte edilebilir kılar. ReducedMotionTests ve
/// MotionResourcesTests dahil, motion tüketen tüm testlerin (T56-T63) ortak kullandığı TEK tanım —
/// çoğaltılmaz (bkz. Task 1 fix wave, Minor #5).
/// </summary>
internal sealed class FakeMotionSignal : IMotionSignal
{
    public bool AnimationsEnabled { get; set; } = true;
    public event EventHandler? Changed;
    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
