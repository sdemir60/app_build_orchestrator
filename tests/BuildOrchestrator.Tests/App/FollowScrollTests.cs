using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T59] FollowScrollDecision — SAF (WPF'siz) follow-mode çekirdeği: **550ms throttle** + **54px dead-band** +
/// üst-boşluk (follow %30 / seçim %35, Ek A-11). Hedefin kendisi (T58'in paylaşılan) <see cref="LayoutMetrics.ScrollTargetForRow"/>'dan.
/// </summary>
public class FollowScrollTests
{
    [Fact]
    public void ShouldMove_false_when_throttle_window_has_not_elapsed_even_with_a_large_delta()
    {
        Assert.False(FollowScrollDecision.ShouldMove(elapsedMsSinceLastMove: 549, currentOffset: 0, targetOffset: 1000));
    }

    [Fact]
    public void ShouldMove_true_exactly_at_the_550ms_throttle_boundary_when_delta_exceeds_deadband()
    {
        Assert.True(FollowScrollDecision.ShouldMove(elapsedMsSinceLastMove: 550, currentOffset: 0, targetOffset: 1000));
    }

    [Fact]
    public void ShouldMove_false_when_target_delta_is_under_the_54px_dead_band()
    {
        Assert.False(FollowScrollDecision.ShouldMove(elapsedMsSinceLastMove: 10_000, currentOffset: 100, targetOffset: 153.99));
    }

    [Fact]
    public void ShouldMove_true_exactly_at_the_54px_dead_band_boundary()
    {
        Assert.True(FollowScrollDecision.ShouldMove(elapsedMsSinceLastMove: 10_000, currentOffset: 100, targetOffset: 154));
    }

    [Fact]
    public void ShouldMove_dead_band_is_symmetric_regardless_of_scroll_direction()
    {
        Assert.False(FollowScrollDecision.ShouldMove(elapsedMsSinceLastMove: 10_000, currentOffset: 500, targetOffset: 500 - 53));
        Assert.True(FollowScrollDecision.ShouldMove(elapsedMsSinceLastMove: 10_000, currentOffset: 500, targetOffset: 500 - 54));
    }

    [Theory]
    [InlineData(300, 0.30, 150)]   // viewport×0.3=90 < taban 150 → 150
    [InlineData(1000, 0.30, 300)]  // viewport×0.3=300 > taban 150 → 300
    [InlineData(1000, 0.35, 350)]  // seçim marjı (Ek A-11) — follow'dan (%30) FARKLI
    public void TopMargin_is_max_of_base_150_and_viewport_times_fraction(double viewportHeight, double fraction, double expected)
    {
        Assert.Equal(expected, FollowScrollDecision.TopMargin(viewportHeight, fraction));
    }

    [Fact]
    public void Target_comes_from_the_shared_LayoutMetrics_ScrollTargetForRow()
    {
        // [T58 ile ORTAK] Follow-mode hedefi LayoutMetrics'in kümülatif offset tablosundan üretilir — burada,
        // FollowScrollDecision'ın topMargin'iyle BİRLİKTE gerçek bir uçtan uca hesap.
        var metrics = LayoutMetrics.Flat(40); // 40 satır, uniform 36px, katman yok
        double viewportHeight = 1000;
        double margin = FollowScrollDecision.TopMargin(viewportHeight, FollowScrollDecision.FollowTopMarginFraction); // 300

        double target = metrics.ScrollTargetForRow(rowIndex: 20, margin); // offsetTop=720

        Assert.Equal(420, target); // 720 - 300
    }
}
