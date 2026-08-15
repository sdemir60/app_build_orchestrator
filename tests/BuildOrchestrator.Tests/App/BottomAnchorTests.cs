using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T59] BottomAnchorDecision — SAF (WPF'siz) "alta yapışık scroll" çekirdeği: içerik-büyümesi vs kullanıcı-scroll
/// ayrımı (<c>ExtentHeightChange</c>), 48px eşik, ve pill'in 560ms "jumping" penceresiyle PAYLAŞILAN guard.
/// </summary>
public class BottomAnchorTests
{
    [Fact]
    public void Content_growth_extentHeightChange_positive_does_not_change_stuck_state()
    {
        var stuck = BottomAnchorState.Initial; // IsStuck=true
        var free = new BottomAnchorState(IsStuck: false, IsJumping: false);

        // İçerik büyüdü (ExtentHeightChange>0) — dipten UZAKLIK ne olursa olsun yapışıklık DEĞİŞMEZ (çağıran,
        // zaten yapışıksa ANINDA tamamlar; serbestse serbest kalmaya devam eder).
        var afterStuck = BottomAnchorDecision.OnScrollChanged(stuck, extentHeightChange: 40, distanceFromBottom: 500, userDriven: true);
        var afterFree = BottomAnchorDecision.OnScrollChanged(free, extentHeightChange: 40, distanceFromBottom: 500, userDriven: true);

        Assert.True(afterStuck.IsStuck);
        Assert.False(afterFree.IsStuck);
    }

    [Fact]
    public void User_scroll_sticks_within_48px_of_bottom()
    {
        var state = new BottomAnchorState(IsStuck: false, IsJumping: false);

        var result = BottomAnchorDecision.OnScrollChanged(state, extentHeightChange: 0, distanceFromBottom: 48, userDriven: true);

        Assert.True(result.IsStuck); // ≤48px eşik dahil (design-v1 §2.5)
    }

    [Fact]
    public void User_scroll_releases_beyond_48px_of_bottom()
    {
        var state = BottomAnchorState.Initial; // IsStuck=true

        var result = BottomAnchorDecision.OnScrollChanged(state, extentHeightChange: 0, distanceFromBottom: 48.01, userDriven: true);

        Assert.False(result.IsStuck);
    }

    /// <summary>
    /// [DEĞİŞEN KURAL] Kullanıcının SEBEP OLMADIĞI bir offset değişimi takibi bırakmaz.
    ///
    /// <para>Eski iddia: <c>extentHeightChange == 0</c> olan her olay "kullanıcı kaydırdı" sayılır ve dipten
    /// uzaklık yeniden hesaplanırdı. Değişme gerekçesi (sahada ölçüldü): offset'i yerleşim, viewport değişimi
    /// ve bizim kendi programatik kaydırmamız da oynatır; o an offset henüz güncellenmemişken ölçülen uzaklık
    /// eşiği aşınca panel kimse dokunmadan takibi bırakıyor, <c>⌄ latest</c> pill'i çıkıyordu. Takip artık
    /// yalnız ham girdiyle değişir.</para>
    /// </summary>
    [Fact]
    public void A_scroll_the_user_did_not_cause_leaves_the_follow_alone()
    {
        var stuck = BottomAnchorState.Initial;

        var result = BottomAnchorDecision.OnScrollChanged(stuck, extentHeightChange: 0, distanceFromBottom: 900, userDriven: false);

        Assert.True(result.IsStuck);
    }

    [Fact]
    public void Jumping_flag_suppresses_recompute_even_when_far_from_bottom()
    {
        var jumping = new BottomAnchorState(IsStuck: true, IsJumping: true);

        // Uçuştaki bir "dibe git" animasyonu SÜRERKEN gelen scroll event'leri (kendi ara-kareleri dahil) yok
        // sayılır — Ek A-15: animasyon kendi event'leriyle YARIŞMASIN.
        var result = BottomAnchorDecision.OnScrollChanged(jumping, extentHeightChange: 0, distanceFromBottom: 900, userDriven: true);

        Assert.Equal(jumping, result);
    }

    [Fact]
    public void BeginJump_sets_jumping_and_optimistically_stuck()
    {
        var free = new BottomAnchorState(IsStuck: false, IsJumping: false);

        var result = BottomAnchorDecision.BeginJump(free);

        Assert.True(result.IsStuck);
        Assert.True(result.IsJumping);
    }

    [Fact]
    public void EndJump_clears_jumping_and_keeps_stuck()
    {
        var jumping = new BottomAnchorState(IsStuck: true, IsJumping: true);

        var result = BottomAnchorDecision.EndJump(jumping);

        Assert.True(result.IsStuck);
        Assert.False(result.IsJumping);
    }

    [Theory]
    [InlineData(48, false)]   // eşitte HENÜZ görünmez (§2.5: "48px'ten fazla")
    [InlineData(48.01, true)]
    [InlineData(500, true)]
    [InlineData(0, false)]
    public void ShouldShowPill_visible_beyond_48px_from_bottom(double distance, bool expectedVisible)
    {
        var state = new BottomAnchorState(IsStuck: false, IsJumping: false);

        Assert.Equal(expectedVisible, BottomAnchorDecision.ShouldShowPill(state, distance));
    }

    [Fact]
    public void ShouldShowPill_hidden_during_the_jumping_window_even_when_far_from_bottom()
    {
        var jumping = new BottomAnchorState(IsStuck: true, IsJumping: true);

        Assert.False(BottomAnchorDecision.ShouldShowPill(jumping, distanceFromBottom: 900));
    }
}
