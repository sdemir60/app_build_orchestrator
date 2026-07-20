using System.Windows;
using System.Windows.Media.Animation;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T59] Foundation <c>Duration.*</c>/<c>KeySpline.*</c> kaynaklarını bir <see cref="FrameworkElement"/> üzerinden
/// çözen TEK paylaşımlı yardımcı (motion sözleşmesi: "tokens/durations consumed by key — no hardcoded ms/hex").
///
/// <para><b>Neden burada:</b> <see cref="Console.ConsoleView"/>'ın önceki (T56/3b) private <c>ResolveDuration</c>
/// metodunun BİREBİR aynı deseni (<c>TryFindResource</c> + fallback literal) — T59'un ScrollAnimator/BottomAnchor/
/// FollowScroll/LatestPill kablajı da AYNI ihtiyacı duyduğundan buraya çıkarılıp PAYLAŞILDI (kopya YASAK,
/// CLAUDE.md). ConsoleView artık bunu çağırır; davranış DEĞİŞMEDİ (aynı TryFindResource + aynı fallback deseni).</para>
/// </summary>
internal static class MotionTokens
{
    public static Duration ResolveDuration(FrameworkElement host, string key, double fallbackMs)
        => host.TryFindResource(key) is Duration d ? d : new Duration(TimeSpan.FromMilliseconds(fallbackMs));

    public static KeySpline ResolveKeySpline(FrameworkElement host, string key, KeySpline fallback)
        => host.TryFindResource(key) is KeySpline k ? k : fallback;

    /// <summary>[M-1] <see cref="Console.ConsoleView.AnimateToBottom"/> ve
    /// <see cref="StickyLayerList.AnimateScrollTo"/>'nun BİREBİR aynı desenini (taze <c>AnimationsEnabled</c> +
    /// <c>Duration.Slow</c> + <c>KeySpline.EaseInOut</c> + <see cref="ScrollAnimator.AnimateTo"/>) tek yerde
    /// toplar — iki host tipi (AvalonEdit <c>TextEditor</c> / <c>ScrollViewer</c>) FARKLI olsa da ikisi de
    /// <see cref="UIElement"/> + <c>ScrollToVerticalOffset(double)</c> sunduğundan <see cref="ScrollAnimator"/>
    /// üstünden ORTAK sarılabilir (kopya YASAK, CLAUDE.md). Motion sinyali ÇAĞRI ANINDA taze okunur (sözleşme).</summary>
    public static bool AnimateSlowEaseInOut(FrameworkElement host, UIElement scrollTarget, double currentOffset, double targetOffset)
    {
        bool animationsEnabled = BuildOrchestrator.App.App.Motion?.AnimationsEnabled ?? false;
        var duration = ResolveDuration(host, "Duration.Slow", 280.0);
        var spline = ResolveKeySpline(host, "KeySpline.EaseInOut", new KeySpline(0.65, 0, 0.35, 1));
        return ScrollAnimator.AnimateTo(scrollTarget, currentOffset, targetOffset, animationsEnabled, duration.TimeSpan, spline);
    }
}
