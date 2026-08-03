using System.Windows;
using System.Windows.Input;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E4 fix — FIX 1 / FIX 2.2] Frontier follow'un CANLI arbitration cycle'ı: GERÇEK (ekran dışı realize edilmiş) bir
/// <see cref="StickyLayerList"/> + GERÇEK bir <see cref="ScrollArbiter"/> üstünde wheel → pause → (uzakta) duraklı kal
/// → near-bottom → RESUME, ve seçim → pause / deselect → resume. Bu, review'ın istediği ≥1 canlı-arbitration
/// regresyon kilididir: "live FollowFrontier kararı arbiter'a bağlı".
///
/// <para>Follow gate = <c>MainWindow.FollowFrontier</c>'ın okuduğu <see cref="ScrollArbiter.CanFollowFrontier"/> VE
/// <c>FollowScrollController.FollowRow</c>'un okuduğu <see cref="ScrollAnimator.GetIsUserSuppressed"/> — ikisi de
/// temiz olmalı ki takip oynasın. Kaydırma near-bottom/away ayrımı için gerçek scroll geometrisi gerektiğinden
/// <c>[StaFact]</c> + ekran dışı realize (StickyReveal deseni).</para>
///
/// <para><b>Kapsam:</b> burası "liste dibine dönüş" yolunu pinler. Diğer iki geri-açılma yolu (frontier'e
/// dönüş, boşta kalma) ve niyet kapıları (seçim, filtre) üretim zinciri üzerinden ayrı sınıflarda pinlidir —
/// <see cref="FrontierFollowResumeTests"/> ve <see cref="FrontierFollowIntentTests"/>.</para>
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

    [StaFact]
    public void A_frontier_wheel_pauses_follow_and_returning_near_the_bottom_resumes_it()
    {
        var arbiter = new ScrollArbiter();
        var list = new StickyLayerList { AnimationsEnabledProvider = () => false, Arbiter = arbiter };
        list.SetGroups([new StickyLayerList.LayerGroup("", Rows(24))]);
        var host = DsResources.NewHost();
        var window = DsResources.Realize(host, list);
        // Kaydırılabilir olana dek pompala (içerik > viewport, eşiğin ötesinde) — near-bottom/away ayrımı gerçek geometri ister.
        DispatcherPump.PumpUntil(
            () => list.Scroll.ScrollableHeight > StickyLayerList.FrontierResumeThresholdPx + 10, TimeSpan.FromSeconds(2));
        Assert.True(list.Scroll.ScrollableHeight > StickyLayerList.FrontierResumeThresholdPx + 10);

        // 0) Başlangıç (tepede, seçim yok, suppress yok) → follow devrede.
        Assert.True(FollowWouldEngage(list, arbiter));

        // 1) Kullanıcı listeyi tekerlekle kaydırdı → İKİ suppress de kurulur (arbiter regional bit + ScrollAnimator flag).
        RaiseFrontierWheel(list);
        Assert.True(arbiter.IsSuppressed(ScrollPanel.Frontier));
        Assert.True(ScrollAnimator.GetIsUserSuppressed(list.Scroll));
        Assert.False(FollowWouldEngage(list, arbiter));                  // follow DURAKLADI

        // 2) Kullanıcı near-bottom DEĞİL (tepede) — resume ATEŞLENMEZ (uzaktayken re-engage yok, follow duraklı kalır).
        Assert.True(list.Scroll.VerticalOffset < StickyLayerList.FrontierResumeThresholdPx); // gerçekten tepedeyiz
        list.ResumeFrontierIfNearBottom();
        Assert.True(ScrollAnimator.GetIsUserSuppressed(list.Scroll));
        Assert.False(FollowWouldEngage(list, arbiter));                  // hâlâ duraklı

        // 3) Kullanıcı dibe (frontier bölgesine) döndü → resume: İKİ suppress de TEK yoldan temizlenir → follow sürer.
        list.Scroll.ScrollToVerticalOffset(list.Scroll.ScrollableHeight);
        list.UpdateLayout();
        DispatcherPump.PumpUntil(
            () => list.Scroll.VerticalOffset >= list.Scroll.ScrollableHeight - 0.5, TimeSpan.FromSeconds(2));
        list.ResumeFrontierIfNearBottom();

        Assert.False(arbiter.IsSuppressed(ScrollPanel.Frontier));        // arbiter regional bit temizlendi
        Assert.False(ScrollAnimator.GetIsUserSuppressed(list.Scroll));   // ScrollAnimator flag temizlendi (ayrışmadılar)
        Assert.True(FollowWouldEngage(list, arbiter));                   // follow RESUME
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
