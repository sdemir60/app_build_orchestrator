using System.Windows;
using System.Windows.Input;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E4 fix — FIX 1 / FIX 2.2] Frontier follow'un CANLI arbitration cycle'ı: GERÇEK (ekran dışı realize edilmiş) bir
/// <see cref="StickyLayerList"/> + GERÇEK bir <see cref="ScrollArbiter"/> üstünde wheel → pause → (panelin
/// NERESİNDE olursa olsun) duraklı kal, ve seçim → pause / deselect → resume. Bu, review'ın istediği ≥1
/// canlı-arbitration regresyon kilididir: "live FollowFrontier kararı arbiter'a bağlı".
///
/// <para>Follow gate = <c>MainWindow.FollowFrontier</c>'ın okuduğu <see cref="ScrollArbiter.CanFollowFrontier"/> VE
/// <c>FollowScrollController.FollowRow</c>'un okuduğu <see cref="ScrollAnimator.GetIsUserSuppressed"/> — ikisi de
/// temiz olmalı ki takip oynasın. Kaydırma konumunun karara girmediğini göstermek gerçek scroll geometrisi
/// gerektirdiğinden <c>[StaFact]</c> + ekran dışı realize (StickyReveal deseni).</para>
///
/// <para><b>[DEĞİŞEN KURAL]</b> Bu sınıf eskiden "liste DİBİNE dönüş takibi anında geri açar" yolunu pinliyordu
/// (<c>A_frontier_wheel_pauses_follow_and_returning_near_the_bottom_resumes_it</c>) — konsol/stream'in
/// bottom-anchor'ıyla geometrik simetri gerekçesiyle. Yol kaldırıldı: bu panelde dip "yeni içeriğin geldiği yer"
/// değildir, ve konum-tabanlı geri açılma kullanıcının sürüklemesinin ÜRETTİĞİ ScrollChanged'lerde tetiklenerek
/// duraklamayı siliyordu. Gerekçe ve tek kalan kapı (boşta penceresi) için bkz.
/// <see cref="FrontierFollowResumeTests"/>; niyet kapıları (seçim, filtre) <see cref="FrontierFollowIntentTests"/>.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class FrontierFollowArbitrationTests
{
    private static IReadOnlyList<object> Rows(int n) =>
        [.. Enumerable.Range(0, n).Select(i => (object)new ProjectRowViewModel($"id{i}", $"P{i}", ProjectRowState.Pending))];

    // MainWindow.FollowFrontier (arbiter.CanFollowFrontier) + FollowScrollController.FollowRow (ScrollAnimator
    // per-target suppress) ikisinin ORTAK "frontier follow ŞU AN oynar mı" CANLI kararı.
    private static bool FollowWouldEngage(StickyLayerList list, ScrollArbiter arbiter) =>
        arbiter.CanFollowFrontier && !ScrollAnimator.GetIsUserSuppressed(list.Scroll);

    private static void RaiseFrontierWheel(StickyLayerList list) =>
        list.Scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
        { RoutedEvent = UIElement.PreviewMouseWheelEvent });

    /// <summary>
    /// [DEĞİŞEN KURAL] Tekerlek İKİ suppress'i de kurar ve kullanıcı listenin NERESİNE giderse gitsin — dibine
    /// inse bile — duraklama kalkmaz. Eski iddia bunun tersiydi: dibe 48 px kala duraklama anında kalkardı
    /// (<c>ResumeFrontierIfNearBottom</c>, her <c>ScrollChanged</c>'de). Kaldırılma gerekçesi sınıf özetindedir.
    /// </summary>
    [StaFact]
    public void A_frontier_wheel_pauses_follow_and_no_scroll_position_resumes_it()
    {
        var arbiter = new ScrollArbiter();
        var list = new StickyLayerList { AnimationsEnabledProvider = () => false, Arbiter = arbiter };
        list.SetGroups([new StickyLayerList.LayerGroup("", Rows(24))]);
        var host = DsResources.NewHost();
        var window = DsResources.Realize(host, list);
        // Kaydırılabilir olana dek pompala (içerik > viewport) — "dibe indi" iddiası gerçek geometri ister.
        double scrollableEnough = LayoutMetrics.DefaultRowHeight * 2;
        DispatcherPump.PumpUntil(() => list.Scroll.ScrollableHeight > scrollableEnough, TimeSpan.FromSeconds(2));
        Assert.True(list.Scroll.ScrollableHeight > scrollableEnough);

        // 0) Başlangıç (tepede, seçim yok, suppress yok) → follow devrede.
        Assert.True(FollowWouldEngage(list, arbiter));

        // 1) Kullanıcı listeyi tekerlekle kaydırdı → İKİ suppress de kurulur (arbiter regional bit + ScrollAnimator flag).
        RaiseFrontierWheel(list);
        Assert.True(arbiter.IsSuppressed(ScrollPanel.Frontier));
        Assert.True(ScrollAnimator.GetIsUserSuppressed(list.Scroll));
        Assert.False(FollowWouldEngage(list, arbiter));                  // follow DURAKLADI

        // 2) Kullanıcı listenin DİBİNE iner — gerçek ScrollChanged'ler akar. Duraklama KALKMAZ.
        list.Scroll.ScrollToVerticalOffset(list.Scroll.ScrollableHeight);
        list.UpdateLayout();
        DispatcherPump.PumpUntil(
            () => list.Scroll.VerticalOffset >= list.Scroll.ScrollableHeight - 0.5, TimeSpan.FromSeconds(2));

        Assert.True(arbiter.IsSuppressed(ScrollPanel.Frontier));
        Assert.True(ScrollAnimator.GetIsUserSuppressed(list.Scroll));
        Assert.False(FollowWouldEngage(list, arbiter));                  // hâlâ duraklı — konum karara girmez
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Selecting_a_card_pauses_follow_and_clearing_the_selection_resumes_it()
    {
        var arbiter = new ScrollArbiter();
        var list = new StickyLayerList { AnimationsEnabledProvider = () => false, Arbiter = arbiter };
        list.SetGroups([new StickyLayerList.LayerGroup("", Rows(6))]);
        var host = DsResources.NewHost();
        var window = DsResources.Realize(host, list);

        Assert.True(FollowWouldEngage(list, arbiter));

        arbiter.SetSelection(true);                                     // kart seçildi (seçim > follow, BuildApp.jsx:1388)
        Assert.False(FollowWouldEngage(list, arbiter));

        arbiter.SetSelection(false);                                    // seçim kalktı
        Assert.True(FollowWouldEngage(list, arbiter));                  // follow kaldığı yerden sürer
        GC.KeepAlive(window);
    }
}
