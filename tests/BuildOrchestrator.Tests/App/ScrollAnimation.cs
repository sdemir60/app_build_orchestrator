using System.Windows;
using System.Windows.Media.Animation;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [B2 fix 3 · madde 7] Panellerin kaydırma animasyonu (<see cref="ScrollAnimator.VerticalOffsetProperty"/> — konsolun editörü, stream'in ve
/// listenin ScrollViewer'ı) üzerine testlerin TEK okuma yeri: animasyon şu an hedefe bağlı mı, saati hangisi. Saati konumlamak
/// <see cref="ActiveClockInspector.Seek"/>'tedir.
/// </summary>
internal static class ScrollAnimation
{
    /// <summary>Kaydırma animasyonu şu an <paramref name="target"/>'e bağlı mı — uçuşta ya da inmiş ve <c>HoldEnd</c> ile hedefte duruyor.
    /// Saat ilk tikini atana dek false olabilir (ölçüldü — <c>GraphBeadsLifecycleTests</c>).</summary>
    public static bool IsAnimating(DependencyObject target) =>
        DependencyPropertyHelper.GetValueSource(target, ScrollAnimator.VerticalOffsetProperty).IsAnimated;

    /// <summary><paramref name="target"/>'e uygulanmış kaydırma animasyonunun saati; yoksa fırlatır (test kurgusu hatası).</summary>
    public static AnimationClock ClockOf(DependencyObject target) =>
        ActiveClockInspector.AppliedClock(target, ScrollAnimator.VerticalOffsetProperty)
        ?? throw new InvalidOperationException("no scroll animation is applied to the target");
}
