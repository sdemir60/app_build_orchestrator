using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [P3 · Task 3] Pencere kapanış kararı (<see cref="WindowCloseRule.Decide"/>) — saf, WPF'siz; <c>MainWindow.OnClosing</c>
/// yalnız uygular (kabuk tarafı <see cref="CloseToTrayTests"/>'te). Dört dal ve aralarındaki öncelik kararın
/// kendisidir: gerçek kapanış her şeyi geçer, bekleyen çıkış pencereyi yerinde tutar, ancak ikisi de yoksa Close to
/// tray söz alır — açık → tepsiye, kapalı → güvenli tam çıkış.
/// </summary>
public sealed class WindowCloseRuleTests
{
    /// <summary>Gerçek kapanış başladıysa (çıkış hazır → Shutdown kuyrukta, ya da Windows oturumu kapanıyor) pencere
    /// kapanır — anahtar ne olursa olsun ve bekleyiş bayrağı hâlâ açık olsa bile: <c>ExitPending</c> bir kez açılınca
    /// geri dönmez, yani bekleyişten sonra gelen Shutdown'ın kendi kapanışı da bu kapıdan geçer.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void A_real_exit_closes_the_window_whatever_the_switch(bool exitPending, bool closeToTray) =>
        Assert.Equal(CloseAction.Close, WindowCloseRule.Decide(exiting: true, exitPending, closeToTray));

    /// <summary>Çıkış uçuştaki işi bekliyorsa × pencereyi gizlemez ve ikinci bir çıkış istemez — anahtar ne olursa
    /// olsun: kullanıcı kapatmayı istedi ve bekleyişi izliyor; pencere tepsiye inseydi kapanmakta olan uygulama
    /// görünmeden sürerdi.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void While_the_exit_waits_the_window_stays(bool closeToTray) =>
        Assert.Equal(CloseAction.Stay, WindowCloseRule.Decide(exiting: false, exitPending: true, closeToTray));

    /// <summary>Close to tray açık (varsayılan): × uygulamayı kapatmaz, pencereyi tepsiye gizler.</summary>
    [Fact]
    public void With_close_to_tray_on_the_window_hides_to_the_tray() =>
        Assert.Equal(CloseAction.HideToTray, WindowCloseRule.Decide(exiting: false, exitPending: false, closeToTray: true));

    /// <summary>Close to tray kapalı: × güvenli tam çıkışı ister (uçuştaki iş beklenir; yoksa hemen kapanır).</summary>
    [Fact]
    public void With_close_to_tray_off_the_window_asks_for_the_safe_exit() =>
        Assert.Equal(CloseAction.RequestExit, WindowCloseRule.Decide(exiting: false, exitPending: false, closeToTray: false));

    // ---------------------------------------------------------------- öne getirme (final review F7)

    /// <summary>Tam çıkış uçuştaki işi bekliyorsa ve gerçek kapanış başlamadıysa pencere öne gelir — tepsi → Exit de
    /// × da: gizli ya da küçültülmüş bir pencerede bekleyen çıkış "hiçbir şey olmuyor" gibi görünürdü.</summary>
    [Fact]
    public void A_waiting_exit_brings_the_window_forward() =>
        Assert.True(WindowCloseRule.ShouldBringForward(exiting: false, exitPending: true));

    /// <summary>Gerçek kapanış başladıysa (bekleyiş istekle AYNI çağrıda bittiyse dahil — bayrak geri dönmez) pencere öne
    /// gelmez: kapanmakta olan pencere bir kare görünürdü. Bekleyen bir çıkış yoksa da gelmez.</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Otherwise_the_window_is_not_brought_forward(bool exiting, bool exitPending) =>
        Assert.False(WindowCloseRule.ShouldBringForward(exiting, exitPending));
}
