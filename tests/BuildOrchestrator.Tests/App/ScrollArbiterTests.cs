using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E4/T48] <see cref="ScrollArbiter"/> — üç panelin auto-scroll niyetini hakem eden SAF karar çekirdeği. Semantik
/// otoritesi prototip <c>BuildApp.jsx</c> (bağımsız scroll kutuları; <c>follow = running &amp;&amp; !selected</c>).
/// Kurallar: (1) bölgesel suppress, (2) yo-yo YASAK (frame başına panel başına tek grant + devir), (3) seçim &gt;
/// follow, (4) öncelik frontier &gt; console &gt; stream. Plain <c>[Fact]</c> (WPF'siz).
/// </summary>
public class ScrollArbiterTests
{
    // ================================================================ (1) bölgesel suppress

    [Fact]
    public void User_scroll_suppresses_only_its_own_panel()
    {
        var a = new ScrollArbiter();

        a.NotifyUserScroll(ScrollPanel.Console); // kullanıcı YALNIZ konsolu kaydırdı

        Assert.True(a.IsSuppressed(ScrollPanel.Console));
        Assert.False(a.IsSuppressed(ScrollPanel.Frontier)); // diğer paneller etkilenmez
        Assert.False(a.IsSuppressed(ScrollPanel.Stream));

        // Aynı frame'de üçü de auto-scroll ister: konsol duraklı (düşer), frontier + stream akmaya devam eder.
        var grants = a.Arbitrate(
        [
            (ScrollPanel.Frontier, ScrollKind.Follow),
            (ScrollPanel.Console, ScrollKind.BottomAnchor),
            (ScrollPanel.Stream, ScrollKind.BottomAnchor),
        ]);

        Assert.Equal(new[] { ScrollPanel.Frontier, ScrollPanel.Stream }, grants.Select(g => g.Panel).ToArray());
    }

    [Fact]
    public void Resume_clears_only_the_named_panels_suppression()
    {
        var a = new ScrollArbiter();
        a.NotifyUserScroll(ScrollPanel.Console);
        a.NotifyUserScroll(ScrollPanel.Stream);

        a.Resume(ScrollPanel.Console);

        Assert.False(a.IsSuppressed(ScrollPanel.Console)); // konsol geri döndü
        Assert.True(a.IsSuppressed(ScrollPanel.Stream));   // stream hâlâ duraklı (bağımsız)
    }

    [Fact]
    public void A_suppressed_bottom_anchor_is_denied_until_the_user_returns()
    {
        var a = new ScrollArbiter();
        a.NotifyUserScroll(ScrollPanel.Stream);

        Assert.False(a.Request(ScrollPanel.Stream, ScrollKind.BottomAnchor).Granted); // duraklı → yapışma yok

        a.Resume(ScrollPanel.Stream);
        Assert.True(a.Request(ScrollPanel.Stream, ScrollKind.BottomAnchor).Granted);  // döndü → yeniden yapışır
    }

    // ================================================================ (2) yo-yo YASAK (epoch/devir)

    [Fact]
    public void Follow_and_bottom_anchor_never_fight_within_one_frame()
    {
        // Guard: aynı frame'de bir panele HEM follow HEM bottom-anchor gelirse arbiter TEK grant verir — böylece
        // viewport aynı karede iki yöne (frontier'e vs dibe) çekilemez. Kazanan panel-içi önceliktedir (follow &lt;
        // bottom-anchor), devir tam BİR artar (kaybeden hiç hareket etmez).
        var a = new ScrollArbiter();
        int before = a.EpochOf(ScrollPanel.Frontier);

        var grants = a.Arbitrate(
        [
            (ScrollPanel.Frontier, ScrollKind.Follow),
            (ScrollPanel.Frontier, ScrollKind.BottomAnchor),
        ]);

        Assert.Single(grants);                                      // panel başına tek grant
        Assert.Equal(ScrollKind.Follow, grants[0].Kind);           // panel-içi öncelik: follow kazanır
        Assert.Equal(before + 1, a.EpochOf(ScrollPanel.Frontier)); // devir tam BİR arttı (kaybeden hiç hareketlenmedi)
    }

    [Fact]
    public void Each_grant_advances_the_panel_epoch_by_one()
    {
        // [E4 fix] Ad gövdeye göre düzeltildi: bu test YALNIZ devrin +1 ilerlediğini doğrular. Epoch'u TÜKETİP bir
        // şeyi geçersiz kılan CANLI (ya da test) bir yol YOKTUR — yo-yo koruması Arbitrate'in panel başına tek-grant
        // `break`'inden gelir, epoch'tan değil. Epoch spec-forward bir yüzeydir (bkz. ScrollArbiter sınıf XML notu).
        var a = new ScrollArbiter();
        var g1 = a.Request(ScrollPanel.Console, ScrollKind.BottomAnchor);
        var g2 = a.Request(ScrollPanel.Console, ScrollKind.BottomAnchor);

        Assert.True(g1.Granted);
        Assert.True(g2.Granted);
        Assert.Equal(g1.Epoch + 1, g2.Epoch); // her grant devri tam BİR artırır
    }

    // ================================================================ (5) [E4 fix] CANLI frontier follow gate
    // CanFollowFrontier, MainWindow.FollowFrontier'ın HER tick okuduğu CANLI karardır — regional wheel-suppress bit'i
    // (IsSuppressed[Frontier]) artık yalnız yazılan değil OKUNAN bir bit'tir (FIX 2.1).

    [Fact]
    public void CanFollowFrontier_is_true_by_default_running_and_unselected()
    {
        Assert.True(new ScrollArbiter().CanFollowFrontier);
    }

    [Fact]
    public void CanFollowFrontier_pauses_on_a_frontier_wheel_and_resumes_when_the_user_returns()
    {
        var a = new ScrollArbiter();

        a.NotifyUserScroll(ScrollPanel.Frontier);           // kullanıcı listeyi kaydırdı (liste wheel'i)
        Assert.False(a.CanFollowFrontier);                  // follow duraklar (regional suppress CANLI okunur)

        a.Resume(ScrollPanel.Frontier);                     // near-bottom'a dönüş (StickyLayerList.ResumeFrontierIfNearBottom)
        Assert.True(a.CanFollowFrontier);                   // follow sürer
    }

    [Fact]
    public void CanFollowFrontier_pauses_while_a_card_is_selected()
    {
        var a = new ScrollArbiter();

        a.SetSelection(true);
        Assert.False(a.CanFollowFrontier);                  // seçim > follow (BuildApp.jsx:1388)

        a.SetSelection(false);
        Assert.True(a.CanFollowFrontier);                   // seçim kalkınca follow kaldığı yerden sürer
    }

    [Fact]
    public void CanFollowFrontier_ignores_other_panels_suppression()
    {
        var a = new ScrollArbiter();

        a.NotifyUserScroll(ScrollPanel.Console);            // başka panelin wheel'i frontier follow'u ETKİLEMEZ (regional)
        a.NotifyUserScroll(ScrollPanel.Stream);

        Assert.True(a.CanFollowFrontier);
    }

    [Fact]
    public void A_denied_request_does_not_advance_the_epoch()
    {
        var a = new ScrollArbiter();
        a.NotifyUserScroll(ScrollPanel.Console);
        int before = a.EpochOf(ScrollPanel.Console);

        var denied = a.Request(ScrollPanel.Console, ScrollKind.BottomAnchor);

        Assert.False(denied.Granted);
        Assert.Equal(before, a.EpochOf(ScrollPanel.Console)); // reddedilen istek devri KIMILDATMAZ
    }

    // ================================================================ (3) seçim > follow

    [Fact]
    public void Selection_scroll_wins_over_follow()
    {
        var a = new ScrollArbiter();
        a.SetSelection(true); // bir kart seçili

        Assert.False(a.Request(ScrollPanel.Frontier, ScrollKind.Follow).Granted);    // follow reddedilir
        Assert.True(a.Request(ScrollPanel.Frontier, ScrollKind.Selection).Granted);  // seçim-scroll geçer
    }

    [Fact]
    public void In_one_frame_a_selection_and_a_follow_resolve_to_the_selection()
    {
        var a = new ScrollArbiter();
        a.SetSelection(true);

        var grants = a.Arbitrate(
        [
            (ScrollPanel.Frontier, ScrollKind.Follow),
            (ScrollPanel.Frontier, ScrollKind.Selection),
        ]);

        Assert.Single(grants);
        Assert.Equal(ScrollKind.Selection, grants[0].Kind); // seçim (küçük ScrollKind + açık niyet) kazanır
    }

    [Fact]
    public void Follow_resumes_once_the_selection_is_cleared()
    {
        var a = new ScrollArbiter();
        a.SetSelection(true);
        Assert.False(a.Request(ScrollPanel.Frontier, ScrollKind.Follow).Granted);

        a.SetSelection(false); // seçim kalktı

        Assert.True(a.Request(ScrollPanel.Frontier, ScrollKind.Follow).Granted); // follow kaldığı yerden sürer
    }

    [Fact]
    public void An_explicit_selection_scroll_re_engages_a_suppressed_frontier()
    {
        // ScrollAnimator.AnimateTo paritesi: her programatik (açık) hareket kullanıcı iptalini temizler.
        var a = new ScrollArbiter();
        a.NotifyUserScroll(ScrollPanel.Frontier);
        Assert.True(a.IsSuppressed(ScrollPanel.Frontier));

        Assert.True(a.Request(ScrollPanel.Frontier, ScrollKind.Selection).Granted);
        Assert.False(a.IsSuppressed(ScrollPanel.Frontier)); // seçim-scroll paneli yeniden devreye aldı
    }

    [Fact]
    public void A_pill_jump_re_engages_a_suppressed_console()
    {
        var a = new ScrollArbiter();
        a.NotifyUserScroll(ScrollPanel.Console);

        Assert.True(a.Request(ScrollPanel.Console, ScrollKind.Jump).Granted); // "⌄ latest" pill — açık dibe git
        Assert.False(a.IsSuppressed(ScrollPanel.Console));                    // yeniden yapışık
    }

    // ================================================================ (4) öncelik frontier > console > stream

    [Fact]
    public void Contended_auto_scrolls_are_ordered_frontier_then_console_then_stream()
    {
        var a = new ScrollArbiter();

        // Giriş sırası KASITEN karışık; kazananlar panel önceliği sırasında dönmeli.
        var grants = a.Arbitrate(
        [
            (ScrollPanel.Stream, ScrollKind.BottomAnchor),
            (ScrollPanel.Frontier, ScrollKind.Follow),
            (ScrollPanel.Console, ScrollKind.BottomAnchor),
        ]);

        Assert.Equal(
            new[] { ScrollPanel.Frontier, ScrollPanel.Console, ScrollPanel.Stream },
            grants.Select(g => g.Panel).ToArray());
    }

    [Fact]
    public void Arbitrate_grants_each_present_panel_at_most_once_and_advances_only_their_epochs()
    {
        var a = new ScrollArbiter();

        var grants = a.Arbitrate(
        [
            (ScrollPanel.Frontier, ScrollKind.Follow),
            (ScrollPanel.Stream, ScrollKind.BottomAnchor),
        ]);

        Assert.Equal(2, grants.Count);
        Assert.Equal(1, a.EpochOf(ScrollPanel.Frontier));
        Assert.Equal(0, a.EpochOf(ScrollPanel.Console)); // frame'de yoktu → devri kımıldamadı
        Assert.Equal(1, a.EpochOf(ScrollPanel.Stream));
    }

    [Fact]
    public void An_empty_frame_grants_nothing()
    {
        var a = new ScrollArbiter();
        Assert.Empty(a.Arbitrate([]));
    }
}
