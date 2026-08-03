using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T59] FollowScrollController — <see cref="FollowScrollDecision"/>'ı paylaşılan <see cref="LayoutMetrics"/> +
/// delege-tabanlı scroll host'a bağlayan orkestratör. Saat (D8, <c>nowMs</c>) ve 90ms gecikme zamanlayıcısı
/// (<c>scheduleOnce</c>) enjekte edilir — PLAIN <c>[Fact]</c>, STA/gerçek bekleme gerekmez.
/// </summary>
public class FollowScrollControllerTests
{
    private sealed class Fake
    {
        public double ViewportHeight = 1000;
        public double CurrentOffset = 0; // testler gerçekçilik için ayarlayabilir (varsayılan: sabit 0)
        public readonly List<double> AnimatedTargets = [];
        public long NowMs = 10_000;
        public Action? PendingSchedule;

        public FollowScrollController New(LayoutMetrics metrics) => new(
            metrics, () => ViewportHeight, () => CurrentOffset,
            target => { AnimatedTargets.Add(target); return true; },
            nowMs: () => NowMs,
            scheduleOnce: (_, cb) => PendingSchedule = cb);
    }

    private static LayoutMetrics Flat40() => LayoutMetrics.Flat(40);

    [Fact]
    public void FollowRow_moves_on_the_very_first_call_no_prior_throttle_window()
    {
        var f = new Fake();
        var follow = f.New(Flat40());

        follow.FollowRow(rowIndex: 20); // offsetTop=720, margin=max(150,1000*.3)=300 → target=420

        Assert.Equal([420.0], f.AnimatedTargets);
    }

    [Fact]
    public void FollowRow_second_call_within_550ms_is_throttled_even_with_a_large_delta()
    {
        var f = new Fake();
        var follow = f.New(Flat40());
        follow.FollowRow(rowIndex: 20); // target 420 (current offset 0) — MOVES, arms the throttle clock
        Assert.Single(f.AnimatedTargets);

        f.NowMs += 549;
        follow.FollowRow(rowIndex: 39); // huge delta, but still inside the throttle window

        Assert.Single(f.AnimatedTargets); // ikinci çağrı GİTMEDİ
    }

    [Fact]
    public void FollowRow_moves_again_once_the_throttle_window_elapses()
    {
        var f = new Fake();
        var follow = f.New(Flat40());
        follow.FollowRow(rowIndex: 20); // target 420 — arms the throttle clock

        f.NowMs += 550;
        follow.FollowRow(rowIndex: 39); // offsetTop=39*36=1404, margin=300 → 1104

        Assert.Equal([420.0, 1104.0], f.AnimatedTargets);
    }

    [Fact]
    public void FollowRow_is_a_no_op_while_a_selection_is_active()
    {
        var f = new Fake();
        var follow = f.New(Flat40());
        follow.SelectRow(5); // follow durur

        follow.FollowRow(rowIndex: 20);

        Assert.Empty(f.AnimatedTargets);
    }

    [Fact]
    public void FollowRow_is_a_no_op_while_the_user_has_suppressed_it_via_wheel_cancel()
    {
        var f = new Fake();
        var follow = f.New(Flat40());

        follow.FollowRow(rowIndex: 20, userSuppressed: true);

        Assert.Empty(f.AnimatedTargets);
    }

    [Fact]
    public void SelectRow_schedules_a_90ms_delayed_scroll_with_the_wider_35_percent_margin()
    {
        var f = new Fake();
        var follow = f.New(Flat40());

        follow.SelectRow(rowIndex: 20); // offsetTop=720, margin=max(150,1000*.35)=350 → 370
        Assert.Empty(f.AnimatedTargets); // henüz ateşlenmedi (90ms gecikme)

        f.PendingSchedule!();

        Assert.Equal([370.0], f.AnimatedTargets);
    }

    [Fact]
    public void ClearSelection_resumes_follow_and_invalidates_a_pending_selection_scroll()
    {
        var f = new Fake();
        var follow = f.New(Flat40());
        follow.SelectRow(20);
        var staleCallback = f.PendingSchedule!;

        follow.ClearSelection();
        staleCallback(); // seçim kalkmadan ÖNCE zamanlanmış — artık BAYAT, uygulanmamalı

        Assert.Empty(f.AnimatedTargets);
        Assert.True(follow.IsFollowing);

        follow.FollowRow(rowIndex: 10); // follow kaldığı yerden sürüyor
        Assert.Single(f.AnimatedTargets);
    }

    [Fact]
    public void A_new_selection_invalidates_the_previous_selections_pending_scroll()
    {
        var f = new Fake();
        var follow = f.New(Flat40());
        follow.SelectRow(5);
        var firstCallback = f.PendingSchedule!;

        follow.SelectRow(10); // ikinci seçim — birincinin bekleyen callback'i artık BAYAT
        firstCallback();

        Assert.Empty(f.AnimatedTargets); // ilk (bayat) satıra kaydırmadı
    }

    // ---------------------------------------------------------------- [T2 fix-1 · I-D] Rebind

    /// <summary>
    /// <b>[T2 fix-1 · I-D — regresyon]</b> Satır düzeni değişince (<see cref="FollowScrollController.Rebind"/>)
    /// 550ms throttle saati KORUNUR.
    ///
    /// <para><b>Ölçülen kusur:</b> <c>StickyLayerList.SetGroups</c> controller'ı her çağrıda yeniden
    /// yaratıyordu; taze controller'da <c>_lastMoveAtMs == long.MinValue</c> → <c>elapsed = double.MaxValue</c>
    /// → <c>ShouldMove</c> HEP true. 2.5'ten sonra <c>SetGroups</c> görünür küme her değiştiğinde koştuğu için
    /// koşarken bir statü filtresi açıkken throttle tamamen etkisizleşiyordu.</para>
    /// </summary>
    [Fact]
    public void Rebinding_new_metrics_keeps_the_throttle_window_open()
    {
        var f = new Fake();
        var follow = f.New(Flat40());
        follow.FollowRow(rowIndex: 20);
        Assert.Single(f.AnimatedTargets);          // ön-koşul: saat işledi

        follow.Rebind(Flat40());                   // filtre tazelemesi: YENİ metrics, AYNI oturum
        f.NowMs += 100;                            // throttle penceresi (550ms) HÂLÂ açık
        follow.FollowRow(rowIndex: 30);

        Assert.Single(f.AnimatedTargets);          // throttle tuttu — ikinci hareket YOK

        f.NowMs += 600;                            // pencere kapandı
        follow.FollowRow(rowIndex: 30);
        Assert.Equal(2, f.AnimatedTargets.Count);  // ...ve normal kadans devam ediyor
    }

    /// <summary>Rebind seçim durumunu da korur — aksi halde bir filtre tazelemesi "seçili kart" kilidini
    /// sessizce açıp follow'u yeniden devreye sokardı.</summary>
    [Fact]
    public void Rebinding_keeps_the_selection_lock()
    {
        var f = new Fake();
        var follow = f.New(Flat40());
        follow.SelectRow(5);
        Assert.False(follow.IsFollowing); // ön-koşul: seçim kilidi var

        follow.Rebind(Flat40());

        Assert.False(follow.IsFollowing);
        follow.FollowRow(rowIndex: 20);
        Assert.Empty(f.AnimatedTargets);  // seçim varken follow hareket ETMEZ
    }

    /// <summary>
    /// <b>[T2 fix-3 · round-3 bulgu 2 — regresyon]</b> <c>Rebind</c>, <see cref="FollowScrollController.SelectRow"/>'un
    /// bekleyen (90ms gecikmeli) kaydırma callback'ini GEÇERSİZ KILAR — <see cref="ClearSelection"/>'ın
    /// bekleyen bir seçim-scroll'u iptal eden desenin AYNISı.
    ///
    /// <para><b>Ölçülen kusur:</b> callback <c>rowIndex</c>'i SelectRow anında yakalanmış bir tamsayı olarak
    /// taşıyor, satırı yeniden sorgulamıyordu. <c>Rebind</c> tam olarak satır düzeni değiştiği (filtre
    /// tazelemesi) için çağrılıyor — yani o eski <c>rowIndex</c> YENİ metrics'te başka bir satırı (ya da
    /// hiçbirini) gösterebilirdi; iptal edilmeden bırakılsaydı "seçili kart görünür kılınır" niyeti YANLIŞ bir
    /// satıra kaydırarak ihlal edilirdi.</para>
    /// </summary>
    [Fact]
    public void Rebind_invalidates_a_pending_selection_scroll()
    {
        var f = new Fake();
        var follow = f.New(Flat40());
        follow.SelectRow(20);
        var staleCallback = f.PendingSchedule!;

        follow.Rebind(Flat40()); // satır düzeni değişti — bekleyen kaydırma artık BAYAT
        staleCallback();

        Assert.Empty(f.AnimatedTargets);   // eski (bayat) hedefe KAYMADI
        Assert.False(follow.IsFollowing);  // ama seçim DURUMU (follow duraklı) hâlâ korunuyor — Rebind ayrı testte pinli
    }
}
