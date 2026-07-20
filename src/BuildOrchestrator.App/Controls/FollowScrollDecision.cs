namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T59] Follow-mode'un ("koşarken + seçim yokken frontier'i yumuşak takip") SAF (WPF'siz) çekirdeği — design-v1
/// §2.4 birebir port (<c>BuildApp.jsx</c> satır 429-450): **550ms throttle** (scroll animasyonu en fazla bunda bir)
/// + **54px dead-band** (hedef sapması bundan azsa dokunma) + üst boşluk = <c>max(150, viewport×oran)</c> — follow
/// %30, seçili-karta-kaydırma %35 (Ek A-11, birbirinden FARKLI).
/// </summary>
public static class FollowScrollDecision
{
    public const double ThrottleMs = 550.0;
    public const double DeadBandPx = 54.0;
    public const double MinTopMargin = 150.0;
    public const double FollowTopMarginFraction = 0.30;
    public const double SelectionTopMarginFraction = 0.35;

    /// <summary>Üst boşluk = <c>max(150, viewportHeight×oran)</c> — 150 tabanı yapışık başlık yığınını (T58) örtecek
    /// kadar büyük olduğundan ayrıca stack telafisi gerekmez (bkz. LayoutMetrics.ScrollTargetForRow XML yorumu).</summary>
    public static double TopMargin(double viewportHeight, double fraction) => Math.Max(MinTopMargin, viewportHeight * fraction);

    /// <summary>Throttle (550ms) VE dead-band (54px) ikisi de geçilirse true — aksi halde dokunma.</summary>
    public static bool ShouldMove(double elapsedMsSinceLastMove, double currentOffset, double targetOffset)
    {
        if (elapsedMsSinceLastMove < ThrottleMs) return false;
        return Math.Abs(currentOffset - targetOffset) >= DeadBandPx;
    }
}
