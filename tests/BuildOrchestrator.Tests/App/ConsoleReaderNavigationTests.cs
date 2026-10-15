using System.Windows;
using System.Windows.Input;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 2 · dördüncü inceleme — ölçüldü] Konsolun dibe yapışması, <c>⌄ latest</c> pill'i ve prompt satırı okuyucunun
/// GERÇEK gezinmesi altında — gerçek pencere, editörün kendi tuş bağlamaları, tekerlek, pill tıklaması (<see cref="RealizedNarrative"/>).
///
/// <para>Ortak kök: bottom-anchor geometriyi ScrollViewer'ın aynasından okuyordu ve karar TextView'in kaydırma olayında verilir — o
/// olayda ScrollViewer henüz eskidir, sonraki yerleşimde güncellenirken yeni olay gelmez. Karar bir olay geride kalıyordu: tek hamlelik bir
/// sıçrama (Ctrl+Home, ilk PageUp) takibi hiç bırakmıyor, eşiği aşan tekerlek çentiği bir çentik geç bırakıyor, dibe dönüş pill'i görünür
/// bırakıyordu. Geometri artık TextView'in kendisinden okunur (<c>ConsoleView.ScrollGeometry</c>).</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ConsoleReaderNavigationTests
{
    /// <summary>Bir proje sayfasının satırları (<c>page0</c>, <c>page1</c>, …) — sayfayı açan testlerin tek kaynağı.</summary>
    private static string[] Page(int lines) => [.. Enumerable.Range(0, lines).Select(i => $"page{i}")];

    /// <summary>Okuyucu dipten ayrıldı: takip bırakıldı, <c>⌄ latest</c> görünür, prompt satırı (panele yaslı) gizli — dipte asılı
    /// kalsaydı okunan metnin üstüne binerdi.</summary>
    private static void AssertReleased(RealizedNarrative narrative)
    {
        Assert.False(narrative.View.StickToBottom);
        Assert.Equal(Visibility.Visible, narrative.View.Pill.Visibility);
        Assert.Equal(Visibility.Collapsed, narrative.View.ActiveLineOverlay.Visibility);
    }

    /// <summary>Okuyucu dipte ve akışı izliyor: pill gizli, prompt satırı görünür.</summary>
    private static void AssertFollowingAtTheBottom(RealizedNarrative narrative)
    {
        Assert.True(narrative.View.FollowsBottom);
        Assert.Equal(0, narrative.DistanceFromBottom, precision: 1);
        Assert.Equal(Visibility.Collapsed, narrative.View.Pill.Visibility);
        Assert.Equal(Visibility.Visible, narrative.View.ActiveLineOverlay.Visibility);
    }

    /// <summary>
    /// Dibe sabitlenmiş anlatıda gerçek bir Ctrl+Home takibi bırakır: <c>⌄ latest</c> çıkar, prompt satırı gider.
    /// <para>KIRMIZI (ölçüldü): tuşun TEK kaydırma olayında ScrollViewer 3173,4 (eski), TextView 0 — anchor dipten uzaklığı 0 sayıp yapışık
    /// kalıyordu; boşta da öyle: <c>StickToBottom</c> true, pill gizli, prompt satırı panelin dibinde, okunan metnin üstünde.</para>
    /// </summary>
    [StaFact]
    public void A_real_ctrl_home_releases_the_console_from_the_bottom()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        AssertFollowingAtTheBottom(narrative); // ön-koşul

        narrative.PressCtrlHome();

        AssertReleased(narrative);
    }

    /// <summary>
    /// Koşu sürerken (satırlar gelmeye devam ederken) gerçek bir Ctrl+Home'dan sonra gelen batch'ler okuyucuyu dibe çekmez: okuma konumu
    /// kalır, belge tepeden kırpılmaz, yeni satırlar sona eklenir.
    /// <para>PİN — düzeltmeden önce de yeşildi (ölçüldü): anchor yapışık kalsa da tuş direksiyonu kullanıcıya veriyordu
    /// (<c>ShouldFollow</c> kapalı), bekleme dolana dek akan içerik dokunmuyordu. Takibin yanlış hesaplanmasının görünür zararı burada
    /// olurdu; bu test onu iki yoldan da korur.</para>
    /// </summary>
    [StaFact]
    public void Output_arriving_after_a_real_ctrl_home_leaves_the_reader_where_they_are()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.PressCtrlHome();
        double readingAt = narrative.View.Editor.VerticalOffset;
        string reading = narrative.View.Document.Text;

        foreach (int batch in Enumerable.Range(0, 3))
        {
            NarrativeSnapshots.Deliver(narrative.Store, narrative.View, [$"live{batch}"]); // derleme akmaya devam ediyor
            DispatcherPump.DrainToIdle();
        }

        Assert.Equal(readingAt, narrative.View.Editor.VerticalOffset);
        Assert.Equal(reading + "live0\nlive1\nlive2\n", narrative.View.Document.Text); // tepeden kırpılmadı, sona eklendi
    }

    /// <summary>
    /// Dipten gerçek bir PageUp takibi İLK basışta bırakır.
    /// <para>KIRMIZI (ölçüldü): ilk basışın olayında ScrollViewer dipte (eski), TextView yukarıda — anchor yapışık kalıyordu; ancak ikinci
    /// basış (ScrollViewer artık ilkinin konumunda) bırakıyordu.</para>
    /// <para>Not (bu testin konusu değil): editörün PageUp'ı imleci bir sayfa yukarı taşıyıp görünür kılar; imleç belgenin başında
    /// durduğundan dipteki ilk PageUp dilimin tepesine sıçrar.</para>
    /// </summary>
    [StaFact]
    public void A_real_page_up_from_the_bottom_releases_the_console_on_the_first_press()
    {
        using var narrative = new RealizedNarrative(lines: 400);

        narrative.Press(Key.PageUp);

        AssertReleased(narrative);
    }

    /// <summary>
    /// Tekerleğin eşiği (<see cref="BottomAnchorDecision.DefaultThresholdPx"/>) aşan çentiği takibi O çentikte bırakır; eşiğin içindeki
    /// çentiklerde takip sürer. Çentik başına kaç piksel kaydığı sistem ayarına bağlıdır — test eşiği geçene kadar çevirir.
    /// <para>KIRMIZI (ölçüldü): çentiğin olayında ScrollViewer bir önceki konumdaydı — 55,8 px'lik ilk çentik (eşik 48) takibi bırakmıyor,
    /// ikinci çentik bırakıyordu.</para>
    /// </summary>
    [StaFact]
    public void The_wheel_notch_that_crosses_the_threshold_releases_the_console()
    {
        using var narrative = new RealizedNarrative(lines: 400);

        for (int notch = 0; notch < 20 && narrative.DistanceFromBottom <= BottomAnchorDecision.DefaultThresholdPx; notch++)
        {
            Assert.True(narrative.View.StickToBottom); // eşiğin içinde takip sürer
            narrative.WheelUp();
        }

        Assert.True(narrative.DistanceFromBottom > BottomAnchorDecision.DefaultThresholdPx, "test kurgusu: tekerlek eşiği aşmalıydı");
        AssertReleased(narrative);
    }

    /// <summary>
    /// <c>⌄ latest</c> tıklaması okuyucuyu dibe getirir, takip geri gelir ve pill kalkar (headless'ta hareket kapalıdır — atlama anında olur,
    /// azaltılmış hareket ayarındaki kullanıcının yolu).
    /// <para>KIRMIZI (ölçüldü): dipte, takip açık — ama pill GÖRÜNÜR kalıyordu. Pill atlamadan önce (uzakken) değerlendiriliyor, atlamanın
    /// olayı da ScrollViewer'ın eski (uzak) ofsetiyle değerlendirdiği için onu kaldırmıyordu.</para>
    /// <para>[DEĞİŞEN KURULUM — dalga 2 kapanış] Eskiden sabit 10 çentikle bırakıyordu; tekerlekle dipten ayrılma artık tek yardımcıda
    /// (<see cref="RealizedNarrative.WheelAwayFromTheBottom"/>, eşik geçilene dek). İddia aynı.</para>
    /// </summary>
    [StaFact]
    public void Clicking_the_latest_pill_brings_the_reader_back_and_hides_it()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.WheelAwayFromTheBottom();
        AssertReleased(narrative); // ön-koşul

        narrative.ClickLatest();

        AssertFollowingAtTheBottom(narrative);
    }

    /// <summary>
    /// Gerçek bir Ctrl+Home'dan sonra okuyucu elini çekince (<see cref="BottomAnchorDecision.IdleResumeMs"/>) konsol dibe döner, akışı
    /// yeniden izler ve pill kalkar — README'nin "about five seconds after you stop, it returns to the bottom" sözü.
    /// <para>KIRMIZI (ölçüldü, dördüncü inceleme): dönüş oluyordu (anchor yanlışlıkla yapışık kaldığı için anında yetişme yolundan), ama
    /// pill dipte GÖRÜNÜR kalıyordu — dönüş öncesi ofsetle değerlendirilmiş, dönüşün olayları da eski ofseti okumuştu.</para>
    /// <para>[DEĞİŞEN KURULUM — dalga 2 beşinci inceleme] Eskiden gerçek zamanlayıcıyı bekliyordu (<c>PumpUntil</c>, ~5 sn duvar saati).
    /// Artık beklemeyi konsolun zamanlayıcı seam'i yakalar ve test onu doldurur (<see cref="RealizedNarrative.ElapseAnchorTimers"/>) — aynı
    /// geri çağırma, aynı pencere ve tuşlar; iddia aynı. Tuştan sonra panelin okuyucuda olduğu ön-koşul olarak pinlenir.</para>
    /// </summary>
    [StaFact]
    public void The_console_returns_to_the_bottom_once_the_reader_lets_go_after_a_real_ctrl_home()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.PressCtrlHome();
        AssertReleased(narrative); // ön-koşul: bekleme dolana dek panel okuyucuda

        narrative.ElapseAnchorTimers();

        AssertFollowingAtTheBottom(narrative);
    }

    /// <summary>
    /// Okuyucu konsolda kaydırdıktan hemen sonra — bekleme dolmadan — açılan proje sayfası baştan okunur ve İZLEMEZ: uzun sayfanın
    /// tepesinde <c>⌄ latest</c> görünür; bekleme dolunca da takip gelmez, derlenen projenin canlı satırı sayfanın tepesini kırpmaz ve
    /// okuyucuyu dibe atmaz (proje logu tepeye sabitlenir, izlemeden açılır).
    /// <para>KIRMIZI (ölçüldü): sayfa takası belgeyi değiştirince TextView kaydırma verisini sıfırlar — tek olay; ofset, içerik ve görünür
    /// alan 0. Anchor o olayda uzaklığı 0 okuyup (direksiyon hâlâ okuyucudaydı) takası başlatan "izleme" kararını ezerek sayfayı izler hâle
    /// getiriyordu ve sonra hiçbir olay yeniden karar vermiyordu: pill gizli, bekleme dolunca takip açık — sıradaki canlı satır tepeyi
    /// kırpıp okuyucuyu dibe atıyordu.</para>
    /// </summary>
    [StaFact]
    public void A_project_page_opened_right_after_a_scroll_opens_at_its_top_and_does_not_follow()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.WheelUp();
        narrative.WheelUp(); // okuyucu kaydırdı: direksiyon onda, bekleme kuruldu

        narrative.View.PlayCascade(Page(lines: 300));
        DispatcherPump.DrainToIdle();

        Assert.False(narrative.View.StickToBottom);
        Assert.Equal(Visibility.Visible, narrative.View.Pill.Visibility); // uzun sayfanın tepesi: dip uzakta

        narrative.ElapseAnchorTimers();       // bekleme doldu
        narrative.View.AppendBatch("live\n"); // derlenen projenin canlı satırı
        DispatcherPump.DrainToIdle();

        Assert.False(narrative.View.FollowsBottom);
        Assert.Equal(0, narrative.View.Editor.VerticalOffset);
        Assert.StartsWith("page100\n", narrative.View.Document.Text); // tepe kırpılmadı
    }

    /// <summary>
    /// Akışı izleyen anlatıdan açılan uzun bir proje sayfası tepesinde açılır, izlemez ve <c>⌄ latest</c>'i gösterir — pill'in tek
    /// kuralı (dipten uzaklık) takasın sonunda oturmuş geometriyle uygulanır; proje modunda prompt satırı yoktur.
    /// <para>Takip kararı takasın sonunda verilir (<c>SwapDocument</c>); onsuz sayfa anlatının takibini taşırdı (bozma: karar
    /// kaldırılınca bu test düşer). Pill: düzeltmeden önce gizliydi (ölçüldü) — karar takastan önce eski belgenin geometrisiyle
    /// veriliyordu (anlatı dipte: uzaklık 0) ve sonra hiçbir olay pill'i yeniden değerlendirmiyordu.</para>
    /// </summary>
    [StaFact]
    public void A_project_page_opened_from_a_following_narrative_opens_at_its_top_and_offers_the_latest_pill()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        Assert.True(narrative.View.FollowsBottom); // ön-koşul: anlatı dibi izliyor

        narrative.View.PlayCascade(Page(lines: 300));
        DispatcherPump.DrainToIdle();

        Assert.False(narrative.View.FollowsBottom);
        Assert.Equal(0, narrative.View.Editor.VerticalOffset);
        Assert.Equal(Visibility.Visible, narrative.View.Pill.Visibility);
        Assert.Equal(Visibility.Collapsed, narrative.View.ActiveLineOverlay.Visibility);
    }

    /// <summary>
    /// Görünüme sığan KISA bir proje sayfası (birkaç satır) izlemeden açılır ve <c>⌄ latest</c>'i göstermez — dip zaten ekranda. Uzun
    /// sayfanın pininin öbür yüzü: pill'in tek kuralı (dipten uzaklık) takasın sonunda oturmuş geometriyle uygulanır.
    /// <para>PİN — yeşil doğar (takip kararı beşinci incelemeden beri takasın sonunda).</para>
    /// </summary>
    [StaFact]
    public void A_short_project_page_opens_without_the_latest_pill_and_does_not_follow()
    {
        using var narrative = new RealizedNarrative(lines: 400);

        narrative.View.PlayCascade(Page(lines: 5));
        DispatcherPump.DrainToIdle();

        Assert.True(narrative.View.Editor.ExtentHeight <= narrative.View.Editor.ViewportHeight, "test kurgusu: sayfa görünüme sığmalıydı");
        Assert.False(narrative.View.FollowsBottom);
        Assert.Equal(Visibility.Collapsed, narrative.View.Pill.Visibility);
    }

    /// <summary>
    /// Belge takası takip kararını TEK kez, sonunda verir: takasın kendi kaydırma olayları (yeni belge atanınca sıfırlanan kaydırma
    /// verisi, pin'in isteği) anchor'a gitmez. Gözlem üretimin olayından: her takas olayında anchor'ın kararı (yapışık mı, pill) takastan
    /// önceki hâlinde kalır.
    /// <para>Senaryo, olayın kararı değiştireceği durumdur: okuyucu kaydırmış (takip bırakılmış, direksiyon onda) ve uzun bir proje
    /// sayfası açılıyor. Takastan önceki ve sonraki karar aynıdır (yapışık değil, pill görünür) — aradaki her farklılık takasın ortasında
    /// verilmiş bir karardır. KIRMIZI (bozma: anchor takas olaylarını yeniden görür — beşinci incelemede bu bozma altında bütün setler
    /// yeşil kalmıştı): sıfırlama olayında yapışık, pill gizli.</para>
    /// <para>[DEĞİŞEN KURULUM — dalga 2 kapanış] Eskiden tam iki tekerlek çentiğiyle bırakıyordu. Çentiğin kaç piksel kaydırdığı Windows
    /// ayarına bağlı: satır başına bir çentikte (~18,6 px) iki çentik eşiği (48 px) geçmez ve ön-koşul düşerdi. Artık eşik geçilene dek
    /// çevrilir (<see cref="RealizedNarrative.WheelAwayFromTheBottom"/>); iddia aynı.</para>
    /// </summary>
    [StaFact]
    public void A_document_swap_decides_following_once_at_its_end_and_never_on_its_own_scroll_events()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.WheelAwayFromTheBottom();
        AssertReleased(narrative); // ön-koşul: takastan önceki karar
        var decisions = new List<(bool Stuck, Visibility Pill)>();
        narrative.View.Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) =>
            decisions.Add((narrative.View.StickToBottom, narrative.View.Pill.Visibility)); // üretimin işleyicisinden SONRA koşar

        narrative.View.PlayCascade(Page(lines: 300));

        Assert.NotEmpty(decisions); // ön-koşul: takas kaydırma olayı yaydı
        Assert.All(decisions, decision => Assert.Equal((false, Visibility.Visible), decision));
        Assert.False(narrative.View.StickToBottom);
        Assert.Equal(Visibility.Visible, narrative.View.Pill.Visibility);
    }

    /// <summary>
    /// Okuyucunun kaydırdığı bir proje sayfasından ← Back: anlatı dibine sabitlenir ve akışı HEMEN yeniden izler — direksiyon geri
    /// alınır, beklemeyi beklemez.
    /// <para>Takip kararı takasın sonunda verilir (<c>SwapDocument</c>); onsuz anlatı sayfanın "izleme" durumunu taşırdı (bozma: karar
    /// kaldırılınca bu test düşer). Düzeltmeden önce de yeşildi (karar <c>ReplaceRunDocument</c>'teydi) — pin.</para>
    /// </summary>
    [StaFact]
    public void Back_from_a_project_page_the_reader_scrolled_follows_the_narrative_again()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.View.PlayCascade(Page(lines: 300));
        DispatcherPump.DrainToIdle();
        narrative.Press(Key.PageDown); // okuyucu sayfada gezindi: direksiyon onda

        narrative.View.ReplaceRunDocument(NarrativeSnapshots.Of(narrative.Store)); // ← Back
        DispatcherPump.DrainToIdle();

        AssertFollowingAtTheBottom(narrative);
    }
}
