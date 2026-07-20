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
}
