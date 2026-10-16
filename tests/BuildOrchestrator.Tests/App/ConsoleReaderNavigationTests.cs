using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using BuildOrchestrator.App.Console;
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
    /// <summary>Okuyucu dipten ayrıldı: takip bırakıldı, <c>⌄ latest</c> görünür, prompt satırı (panele yaslı) gizli — dipte asılı
    /// kalsaydı okunan metnin üstüne binerdi.</summary>
    private static void AssertReleased(RealizedNarrative narrative)
    {
        Assert.False(narrative.View.StickToBottom);
        Assert.Equal(Visibility.Visible, narrative.View.Pill.Visibility);
        Assert.Equal(Visibility.Collapsed, narrative.View.ActiveLineOverlay.Visibility);
    }

    /// <summary>[B2 fix 2 · madde 1] Prompt imleci pill'i izler (bir atlama uçuşta değilken): ikisinden TAM OLARAK biri görünür. Pill
    /// görünürken okuyucu canlı dipten uzaktadır ve panele yaslı imleç okunan metnin üstüne binerdi; pill gizliyken anlatı canlı dipte,
    /// imleç orada yanar. Proje sayfasında imleç hiç yoktur — orada yalnız pill görünürken çağrılır.</summary>
    private static void AssertCaretFollowsPill(RealizedNarrative narrative)
    {
        var pill = narrative.View.Pill.Visibility;
        var caret = narrative.View.ActiveLineOverlay.Visibility;
        Assert.True(pill != caret, $"the prompt caret does not follow the latest pill: pill {pill}, caret {caret}");
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
    /// <para>Not (bu testin konusu değil): PageUp görünümü bir görünür alan yukarı kaydırır (dalga 3 · karar 46 —
    /// <see cref="A_real_page_up_at_the_bottom_of_the_narrative_moves_exactly_one_viewport"/>). Eskiden editörün kendi PageUp'ı koşuyordu:
    /// imleci bir sayfa yukarı taşıyıp görünür kılıyordu ve imleç belgenin başında durduğundan dipteki ilk PageUp dilimin tepesine
    /// sıçrıyordu. Bu testin iddiası (takip ilk basışta bırakılır) iki hâlde de aynıdır.</para>
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
    /// <para>[DEĞİŞEN KURAL — dalga 3 · karar 44] Eski iddia: sayfanın tepesi <c>page100</c>'dü — sayfa logun son 200 satırıyla (render
    /// dilimi) kuruluyor, 300 satırlık bir log 100. satırından açılıyordu. Sahip kararı: proje sayfası logun İLK satırından açılır ve logun
    /// tamamını taşır; tepe artık <c>page0</c>'dır. "Tepe kırpılmadı" iddiası aynı.</para>
    /// </summary>
    [StaFact]
    public void A_project_page_opened_right_after_a_scroll_opens_at_its_top_and_does_not_follow()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.WheelUp();
        narrative.WheelUp(); // okuyucu kaydırdı: direksiyon onda, bekleme kuruldu

        narrative.OpenPage(lines: 300);

        Assert.False(narrative.View.StickToBottom);
        Assert.Equal(Visibility.Visible, narrative.View.Pill.Visibility); // uzun sayfanın tepesi: dip uzakta

        narrative.ElapseAnchorTimers();       // bekleme doldu
        narrative.View.AppendBatch("live\n"); // derlenen projenin canlı satırı
        DispatcherPump.DrainToIdle();

        Assert.False(narrative.View.FollowsBottom);
        Assert.Equal(0, narrative.View.Editor.VerticalOffset);
        Assert.StartsWith("page0\n", narrative.View.Document.Text); // tepe kırpılmadı — sayfa logun ilk satırıdır
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

        narrative.OpenPage(lines: 300);

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

        narrative.OpenPage(lines: 5);

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

        narrative.View.PlayCascade(ConsoleDocument.Text(RealizedNarrative.PageLines(300)));

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
        narrative.OpenPage(lines: 300);
        narrative.Press(Key.PageDown); // okuyucu sayfada gezindi: direksiyon onda

        narrative.View.ReplaceRunDocument(NarrativeSnapshots.Of(narrative.Store)); // ← Back
        DispatcherPump.DrainToIdle();

        AssertFollowingAtTheBottom(narrative);
    }

    // ---------------------------------------------------------------- [dalga 3 · karar 44] proje sayfası logun ilk satırından açılır

    /// <summary>
    /// [dalga 3 · karar 44] Uzun bir proje logu İLK satırından açılır — bir derleme logunda aranan ilk hatadır — ve logun geri kalanına
    /// aşağı kaydırarak ulaşılır: Ctrl+End son satırı gösterir.
    /// <para>KIRMIZI (karardan önce): sayfa logun son 200 satırıyla (render dilimi) kuruluyor ve o dilimin tepesine sabitleniyordu — 1000
    /// satırlık log <c>page800</c>'den açılıyordu; öncesi ancak tepeye kaydırınca dilim dilim geliyordu. ≤ 200 satırlık log zaten 1. satırdan
    /// açılıyordu, kusur yalnız uzun loglardaydı.</para>
    /// </summary>
    [StaFact]
    public void A_long_project_page_opens_at_the_first_line_of_its_log_and_scrolls_down_to_its_last()
    {
        using var narrative = new RealizedNarrative(lines: 400);

        narrative.OpenPage(lines: 1000);

        Assert.Equal(0, narrative.View.Editor.VerticalOffset);
        Assert.Equal("page0", narrative.TopLine());
        Assert.False(narrative.View.FollowsBottom);

        narrative.PressCtrlEnd();

        Assert.Equal(0, narrative.DistanceFromBottom, precision: 1);
        Assert.True(narrative.IsShown("page999"), "the log's last line is not on screen at the end of its page");
    }

    /// <summary>
    /// [dalga 3 · karar 44-45] Derlenen bir projenin canlı satırları sayfanın SONUNA, geldikleri sırayla eklenir ve hiçbiri kaybolmaz —
    /// okuyucu sayfanın tepesinde, ortasında ya da sonunda olsun; görünüm yerinden oynamaz (proje sayfası izlemez).
    /// <para>KIRMIZI (karardan önce): sayfa belgesi logun yalnız son 200 satırıydı — belge logun tamamını ve ardından canlı satırları
    /// taşımıyordu (öncesi görünümün listesinde, tepeye kaydırınca gelirdi).</para>
    /// </summary>
    [StaTheory]
    [InlineData("top")]
    [InlineData("middle")]
    [InlineData("end")]
    public void Live_lines_of_a_building_project_land_in_order_after_its_log_wherever_the_reader_is(string where)
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.OpenPage(lines: 1000);
        if (where == "middle") foreach (int _ in Enumerable.Range(0, 10)) narrative.Press(Key.PageDown);
        if (where == "end") narrative.PressCtrlEnd();
        double readingAt = narrative.View.Editor.VerticalOffset;
        string[] live = [.. Enumerable.Range(0, 6).Select(i => $"live{i}")];

        foreach (var pair in live.Chunk(2))
        {
            narrative.View.AppendBatch(string.Concat(pair.Select(line => line + "\n"))); // pompanın batch'i (kabuk: Route.Raw)
            DispatcherPump.DrainToIdle();
        }

        string[] logThenLive = [.. RealizedNarrative.PageLines(1000), .. live];
        Assert.Equal(logThenLive, ConsoleDocument.LinesOf(narrative.View.Document));
        Assert.Equal(readingAt, narrative.View.Editor.VerticalOffset);
    }

    // ---------------------------------------------------------------- [dalga 3 · karar 45] proje sayfası hiç izlemez

    /// <summary>
    /// [dalga 3 · karar 45] Okuyucu proje sayfasını kendi eliyle sonuna indirse de sayfa İZLEMEZ: bekleme dolsa da yeni canlı satırlar
    /// görünümü oynatmaz.
    /// <para>PİN — karardan önce de yeşildi (ölçüldü): proje modunda bekleme hiç kurulmadığından direksiyon okuyucuda kalıyor, takip
    /// gelmiyordu. Kodun doc'u (<c>PlayCascade</c>: "kullanıcı kendi eliyle dibe inerse takip geri gelir") ve README tersini söylüyordu;
    /// karar bugünkü davranışı kural yaptı.</para>
    /// </summary>
    [StaFact]
    public void A_project_page_scrolled_to_its_end_does_not_follow_new_lines()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.OpenPage(lines: 300);
        narrative.PressCtrlEnd();
        Assert.Equal(0, narrative.DistanceFromBottom, precision: 1); // ön-koşul: okuyucu sayfanın sonunda
        double atTheEnd = narrative.View.Editor.VerticalOffset;

        narrative.ElapseAnchorTimers(); // okuyucu elini çekti
        narrative.View.AppendBatch("live0\nlive1\nlive2\nlive3\n");
        DispatcherPump.DrainToIdle();

        Assert.False(narrative.View.FollowsBottom);
        Assert.Equal(atTheEnd, narrative.View.Editor.VerticalOffset);
    }

    /// <summary>
    /// [dalga 3 · karar 44-45] Proje sayfasında <c>⌄ latest</c> sayfanın GERÇEK sonuna bir kez atlar — logun son satırı ekrandadır — ve
    /// takibi AÇMAZ: sıradaki canlı satırlar görünümü çekmez.
    /// <para>KIRMIZI (karardan önce): pill tıklaması direksiyonu geri alıp sayfayı izler hâle getiriyordu (<c>FollowsBottom</c> true) —
    /// sonraki canlı batch görünümü dibe çekiyordu.</para>
    /// </summary>
    [StaFact]
    public void The_latest_pill_on_a_project_page_jumps_to_its_end_once_and_does_not_follow()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.OpenPage(lines: 1000);
        Assert.Equal(Visibility.Visible, narrative.View.Pill.Visibility); // ön-koşul: uzun sayfanın tepesi

        narrative.ClickLatest();

        Assert.Equal(0, narrative.DistanceFromBottom, precision: 1);
        Assert.True(narrative.IsShown("page999"), "the pill did not land on the log's last line");
        Assert.Equal(Visibility.Collapsed, narrative.View.Pill.Visibility);
        Assert.False(narrative.View.FollowsBottom);
        double landed = narrative.View.Editor.VerticalOffset;

        narrative.View.AppendBatch("live0\nlive1\nlive2\nlive3\n");
        DispatcherPump.DrainToIdle();

        Assert.Equal(landed, narrative.View.Editor.VerticalOffset);
        Assert.False(narrative.View.FollowsBottom);
    }

    /// <summary>
    /// [dalga 3 · karar 45] Anlatıda <c>⌄ latest</c> takibi geri verir: tıklamadan sonra gelen satırlar görünümü yeni dibe çeker.
    /// <para>PİN — karar yalnız proje sayfasını değiştirir; anlatı değişmez.</para>
    /// </summary>
    [StaFact]
    public void After_the_latest_pill_the_narrative_follows_new_output()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.WheelAwayFromTheBottom();
        narrative.ClickLatest();

        NarrativeSnapshots.Deliver(narrative.Store, narrative.View, ["live0", "live1", "live2", "live3"]);
        DispatcherPump.DrainToIdle();

        AssertFollowingAtTheBottom(narrative);
        Assert.EndsWith("live3\n", narrative.View.Document.Text);
    }

    // ---------------------------------------------------------------- [dalga 3 · karar 46] PageUp/PageDown bir görünür alan

    /// <summary>
    /// [dalga 3 · karar 46] Anlatının dibinde gerçek bir PageUp görünümü TAM BİR görünür alan yukarı kaydırır; tepeye yaklaşmadığı için
    /// önceki dilim yüklenmez.
    /// <para>KIRMIZI (karardan önce, ölçüldü dalga 2 dördüncü inceleme): editörün kendi PageUp'ı imleç-göreliydi — imleci bir sayfa yukarı
    /// taşıyıp görünür kılıyordu; imleç belgenin başında (1. satır) durduğundan dipteki ilk PageUp 200 satırlık dilimin TEPESİNE sıçrıyor,
    /// yükleyici de önceki dilimi getiriyordu.</para>
    /// </summary>
    [StaFact]
    public void A_real_page_up_at_the_bottom_of_the_narrative_moves_exactly_one_viewport()
    {
        using var narrative = new RealizedNarrative(lines: 600);
        double before = narrative.ContentTop();
        double viewport = narrative.View.Editor.ViewportHeight;

        narrative.Press(Key.PageUp);

        Assert.Equal(before - viewport, narrative.ContentTop(), precision: 1);
        Assert.Null(narrative.View.LastPrepend);
    }

    /// <summary>
    /// [dalga 3 · karar 46] PageUp'ın ardından PageDown anlatıyı dibine geri getirir; dibe ulaşmak her jestteki gibidir: okuyucu elini
    /// çekince takip geri gelir (anchor kuralları değişmez).
    /// <para>KIRMIZI (karardan önce): PageUp dilimin tepesine sıçrıyordu; PageDown da imleç-göreliydi — dibe dönmüyordu.</para>
    /// </summary>
    [StaFact]
    public void A_real_page_down_after_a_page_up_returns_the_narrative_to_its_bottom()
    {
        using var narrative = new RealizedNarrative(lines: 600);
        double bottom = narrative.ContentTop();
        narrative.Press(Key.PageUp);
        AssertReleased(narrative); // ön-koşul: okuyucu paneli aldı

        narrative.Press(Key.PageDown);

        Assert.Equal(bottom, narrative.ContentTop(), precision: 1);
        Assert.Equal(0, narrative.DistanceFromBottom, precision: 1);
        narrative.ElapseAnchorTimers();
        AssertFollowingAtTheBottom(narrative);
    }

    /// <summary>
    /// [dalga 3 · karar 46] Anlatının dibinde gerçek bir PageDown dipte kalır — dibe ulaşmanın aynısı: pill çıkmaz, okuyucu elini çekince
    /// takip sürer.
    /// <para>KIRMIZI (karardan önce): editörün PageDown'ı imleci (belgenin başında, 1. satır) bir sayfa aşağı taşıyıp görünür kılıyordu —
    /// dipteki okuyucu dilimin başına, yukarı sıçrıyordu.</para>
    /// </summary>
    [StaFact]
    public void A_real_page_down_at_the_bottom_of_the_narrative_stays_at_the_bottom()
    {
        using var narrative = new RealizedNarrative(lines: 600);

        narrative.Press(Key.PageDown);

        Assert.Equal(0, narrative.DistanceFromBottom, precision: 1);
        Assert.Equal(Visibility.Collapsed, narrative.View.Pill.Visibility);
        narrative.ElapseAnchorTimers();
        AssertFollowingAtTheBottom(narrative);
    }

    /// <summary>
    /// [dalga 3 · karar 46 · B2 fix 1 M4] Dipten art arda PageUp'lar anlatıda her basışta TAM BİR görünür alan yukarı yürür — belgenin
    /// (dilimin) tepesini aşacak basış da: önceki dilim basıştan ÖNCE gelir (prepend + telafi), sayfa tam kalır; okuyucunun içerikteki yeri
    /// sıçramaz ve yürüyüş anlatının başına dek sürer — sonunda belge anlatının tamamıdır. Adımı yalnız anlatının başı (içerikte 0) kısaltır.
    /// <para>[DEĞİŞEN KURAL — B2 fix 1 · M4] Eski iddia: belgenin tepesine varan basış her okuyucu jesti gibi orada durur, dilim orada
    /// gelir — o basış bir görünür alandan kısaydı (600 satırda iki kez; ölçüldü: 565,2 yerine ~347 px). Değişme gerekçesi (kontrolcü kararı,
    /// sahip "bir sayfa" istedi): dilimin tepesini aşacak PageUp önce önceki dilimi yükler, sonra bir sayfa kayar.</para>
    /// <para>KIRMIZI (karar 46'dan önce): ilk basış dilimin tepesine sıçrıyordu (içerikte ~170 satır, bir görünür alan ~30 satır).</para>
    /// </summary>
    [StaFact]
    public void Page_ups_walk_the_narrative_up_one_viewport_at_a_time_and_bring_the_previous_slices_on_the_way()
    {
        using var narrative = new RealizedNarrative(lines: 600);
        double viewport = narrative.View.Editor.ViewportHeight;
        int boundaries = 0;

        for (int press = 0; press < 60 && narrative.ContentTop() > 0; press++)
        {
            double before = narrative.ContentTop();
            int lines = narrative.View.Document.LineCount;
            narrative.Press(Key.PageUp);
            Assert.Equal(Math.Max(0, before - viewport), narrative.ContentTop(), precision: 1);
            AssertCaretFollowsPill(narrative);
            if (narrative.View.Document.LineCount == lines) continue;
            // [B2 fix 2 · madde 5] Dilim sınırındaki basış: önceki dilim yüklenip ofset telafi edildi — anchor bunu okuyucunun kaydırması gibi
            // okumalı (takip bırakılmış, pill görünür, imleç gizli); prepend'in büyümesi "dipte" kararı doğurmaz.
            boundaries++;
            AssertReleased(narrative);
        }

        Assert.True(boundaries > 0, "test kurgusu: yürüyüş bir dilim sınırından geçmeliydi");
        Assert.Equal(0, narrative.ContentTop());
        Assert.Equal(string.Concat(Enumerable.Range(0, 600).Select(i => $"line{i}\n")), narrative.View.Document.Text);
    }

    /// <summary>
    /// [dalga 3 · karar 46] Proje sayfasında da PageDown/PageUp görünümü TAM BİR görünür alan kaydırır; sayfa izlemez.
    /// <para>KIRMIZI (karardan önce): editörün PageDown'ı imleç-göreliydi — tepedeki sayfada imleci bir sayfa aşağı taşıyıp yalnız onu
    /// görünür kılacak kadar kaydırıyordu (bir satır kadar).</para>
    /// </summary>
    [StaFact]
    public void Page_down_and_page_up_move_a_project_page_one_viewport_at_a_time()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.OpenPage(lines: 1000);
        double start = narrative.ContentTop();
        double viewport = narrative.View.Editor.ViewportHeight;

        narrative.Press(Key.PageDown);
        Assert.Equal(start + viewport, narrative.ContentTop(), precision: 1);
        narrative.Press(Key.PageDown);
        Assert.Equal(start + 2 * viewport, narrative.ContentTop(), precision: 1);
        narrative.Press(Key.PageUp);
        Assert.Equal(start + viewport, narrative.ContentTop(), precision: 1);
        Assert.False(narrative.View.FollowsBottom);
    }

    // ---------------------------------------------------------------- [B2 fix 1 · F2] klavye gezintisi görünümü kaydırır, imleci değil

    /// <summary>
    /// [B2 fix 1 · F2] Anlatının dibinde gerçek bir ↑ görünümü TAM BİR satır yukarı kaydırır; önceki dilim yüklenmez.
    /// <para>KIRMIZI (düzeltmeden önce): ↑ editörün gizli imlecini taşıyordu — imleç belgenin 1. satırında durduğundan yerinde kalıyor,
    /// görünür kılınırken görünüm dilimin tepesine sıçrıyor ve yükleyici önceki dilimi getiriyordu.</para>
    /// </summary>
    [StaFact]
    public void A_real_up_at_the_bottom_of_the_narrative_moves_exactly_one_line()
    {
        using var narrative = new RealizedNarrative(lines: 600);
        double before = narrative.ContentTop();

        narrative.Press(Key.Up);

        Assert.Equal(before - narrative.View.Editor.TextArea.TextView.DefaultLineHeight, narrative.ContentTop(), precision: 1);
        Assert.Null(narrative.View.LastPrepend);
    }

    /// <summary>
    /// [B2 fix 1 · F2] Anlatının dibinde gerçek bir ↓ dipte kalır — dibe ulaşmanın aynısı: pill çıkmaz, okuyucu elini çekince takip sürer.
    /// <para>KIRMIZI (düzeltmeden önce): ↓ gizli imleci 1. satırdan 2. satıra taşıyıp görünür kılıyordu — dipteki okuyucu dilimin tepesine
    /// sıçrıyordu.</para>
    /// </summary>
    [StaFact]
    public void A_real_down_at_the_bottom_of_the_narrative_stays_at_the_bottom()
    {
        using var narrative = new RealizedNarrative(lines: 600);

        narrative.Press(Key.Down);

        Assert.Equal(0, narrative.DistanceFromBottom, precision: 1);
        Assert.Equal(Visibility.Collapsed, narrative.View.Pill.Visibility);
        narrative.ElapseAnchorTimers();
        AssertFollowingAtTheBottom(narrative);
    }

    /// <summary>
    /// [B2 fix 1 · F2] Proje sayfasının tepesinde End görünümü sayfanın SONUNA götürür — logun son satırı ekranda — ve takip açılmaz.
    /// <para>KIRMIZI (düzeltmeden önce): End gizli imleci 1. satırın sonuna taşıyordu; satır zaten görünür olduğundan görünüm tepede
    /// kalıyordu.</para>
    /// </summary>
    [StaFact]
    public void End_takes_a_project_page_to_its_end_without_following()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.OpenPage(lines: 1000);

        narrative.Press(Key.End);

        Assert.Equal(0, narrative.DistanceFromBottom, precision: 1);
        Assert.True(narrative.IsShown("page999"), "End did not reach the log's last line");
        Assert.False(narrative.View.FollowsBottom);
    }

    /// <summary>
    /// [B2 fix 1 · F2] Ctrl+End ve üç PageUp'tan sonra Home proje sayfasının TEPESİNE gider.
    /// <para>KIRMIZI (düzeltmeden önce): Ctrl+End gizli imleci belgenin sonuna koyuyordu; Home onu son satırın başına taşıyıp görünür
    /// kılıyordu — görünüm tepe yerine yine sona sıçrıyordu.</para>
    /// </summary>
    [StaFact]
    public void Home_takes_a_project_page_back_to_its_top_after_ctrl_end_and_page_ups()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.OpenPage(lines: 1000);
        narrative.PressCtrlEnd();
        foreach (int _ in Enumerable.Range(0, 3)) narrative.Press(Key.PageUp);

        narrative.Press(Key.Home);

        Assert.Equal(0, narrative.View.Editor.VerticalOffset);
        Assert.Equal("page0", narrative.TopLine());
    }

    /// <summary>
    /// [B2 fix 1 · F2] İki PageDown'dan sonra ↓ proje sayfasını TAM BİR satır daha aşağı kaydırır.
    /// <para>KIRMIZI (düzeltmeden önce): ↓ gizli imleci 1. satırdan 2. satıra taşıyıp görünür kılıyordu — okuyucu iki sayfa aşağıdan
    /// sayfanın tepesine sıçrıyordu.</para>
    /// </summary>
    [StaFact]
    public void Down_after_two_page_downs_moves_a_project_page_one_more_line()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.OpenPage(lines: 1000);
        double start = narrative.ContentTop();
        double viewport = narrative.View.Editor.ViewportHeight;
        narrative.Press(Key.PageDown);
        narrative.Press(Key.PageDown);

        narrative.Press(Key.Down);

        Assert.Equal(start + 2 * viewport + narrative.View.Editor.TextArea.TextView.DefaultLineHeight, narrative.ContentTop(), precision: 1);
    }

    /// <summary>
    /// [B2 fix 1 · F2] ← / → ve Ctrl+← / Ctrl+→ uzun satırlı bir sayfayı YANA kaydırır, dikeyde yerinden oynatmaz; End sayfanın TAM sonuna
    /// takibin kendi isteğiyle gider ve yatay okuma konumunu KORUR, Home tepeye ve sol kenara gider.
    /// <para>KIRMIZI (düzeltmeden önce): oklar gizli imleci 1. satırda taşıyıp görünür kılıyordu — iki sayfa aşağıdaki okuyucu sayfanın
    /// tepesine sıçrıyor, yatay konum imlecin sütununa gidiyordu.</para>
    /// <para>[DEĞİŞEN KURAL — B2 fix 2 · madde 3] Eski iddia: End'den sonra yatay konum 0'dır ve dip bir yatay çubuk boyu (10 px) kısa olabilir
    /// (ölçülen hâliyle pinlenmişti: istek <c>ScrollToEnd</c>'di — "dikeyde +∞, yatayda sol kenar"; editör +∞ konumunda yalnız son boş satırı
    /// kurar, yatay genişlik çöker, çubuk kaybolur ve ofset 0'a / dibin 10 px üstüne kenetlenir). Değişme gerekçesi (kontrolcü kararı): takibin
    /// isteği yalnız dikeydir — izleyen anlatıda uzun bir satırı yana kaydırıp okuyan okuyucu her batch'te sol kenara atılıyordu
    /// (<see cref="A_sideways_step_in_a_following_narrative_survives_new_output"/>); istek sonlu olduğu için (belgenin şu anki sonu) dip de tam.
    /// End o isteğin kendisi olarak kalır.</para>
    /// </summary>
    [StaFact]
    public void Arrows_scroll_long_lines_sideways_without_moving_the_page()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.OpenPage([.. Enumerable.Range(0, 1000).Select(LongLine)]);
        narrative.Press(Key.PageDown);
        narrative.Press(Key.PageDown);
        var editor = narrative.View.Editor;
        double vertical = editor.VerticalOffset;
        Assert.Equal(0, editor.HorizontalOffset); // ön-koşul

        narrative.Press(Key.Right);
        double oneStep = editor.HorizontalOffset;
        Assert.True(oneStep > 0, "→ did not scroll the long lines sideways");
        narrative.PressWithModifiers(Key.Right, ModifierKeys.Control);
        Assert.Equal(2 * oneStep, editor.HorizontalOffset, precision: 1);
        narrative.PressWithModifiers(Key.Left, ModifierKeys.Control);
        Assert.Equal(oneStep, editor.HorizontalOffset, precision: 1);
        narrative.Press(Key.Left);
        Assert.Equal(0, editor.HorizontalOffset, precision: 1);
        Assert.Equal(vertical, editor.VerticalOffset);

        narrative.Press(Key.Right);
        narrative.Press(Key.End);
        Assert.Equal(0, narrative.DistanceFromBottom, precision: 1);
        Assert.True(narrative.IsShown(LongLine(999)), "End did not reach the log's last line");
        Assert.Equal(oneStep, editor.HorizontalOffset, precision: 1); // End yalnız dikeydir: yatay okuma konumu kalır
        narrative.Press(Key.Right);
        narrative.Press(Key.Home);
        Assert.Equal(0, editor.VerticalOffset);
        Assert.Equal(0, editor.HorizontalOffset, precision: 1); // Home: tepe ve sol kenar
    }

    /// <summary>Panelden geniş bir sayfa satırı: <c>page{i}</c> + 400 karakter — yatay kaydırma isteyen sayfaların (ve anlatı satırlarının)
    /// tek tanımı.</summary>
    private static string LongLine(int i) => $"page{i} " + new string('x', 400);

    // ---------------------------------------------------------------- [B2 fix 2 · madde 3] yatay okuma takipte kalır

    /// <summary>
    /// [B2 fix 2 · madde 3] Akışı izleyen anlatıda yana bir adım (→) yeni satırlar gelince de yerinde kalır: takibin isteği görünümü yalnız
    /// DİKEYDE, belgenin tam sonuna götürür — uzun bir satırı okuyan okuyucu her batch'te sol kenara atılmaz. Adım direksiyonu almaz (takip
    /// sürer); dipten uzaklık ve pill yatay konumdan bağımsızdır.
    /// <para>KIRMIZI (madde 3'ten önce, ölçüldü): takibin isteği <c>ScrollToEnd</c>'di ("dikeyde +∞, yatayda sol kenar") — editör +∞ konumunda
    /// yalnız son boş satırı kurar, yatay genişlik 2940,6'dan 3,0 px'e çöker ve yerleşim yatay konumu 0'a kenetler (7,2 → 0). Yalnız dikey bir
    /// +∞ isteği de aynısını yapıyordu (ilk deneme — yine kırmızı): hedef sonlu olmalı.</para>
    /// <para>[B2 fix 3 · madde 12] Geniş satırı getiren batch'ten itibaren pill hiç yanmaz (inceleme M-3): ilk geniş satır görünümü bir yatay
    /// çubuk boyu kadar (~10 px) kısa bırakabilir — eşiğin içinde, sonraki satırla kapanır (kabul edilen sınır, inceleme M-2). Anlatı render
    /// diliminin altından başlar: belge her batch'te büyür ve isteğin hedefi eklemenin ardından bilinmelidir (bekleyen dip —
    /// <c>ConsoleView.PendingBottomOffset</c>; bozma: oturmuş dibi hedeflerse dört satırlık batch görünümü ~74 px kısa bırakır, pill yanar).</para>
    /// <para>[son temizlik · madde 2 · DEĞİŞEN KURULUM] ESKİ: tek satır, 100 satırlık anlatı (madde 12'den önce 400). 100 satır belgenin büyüdüğü
    /// rejimdir — bekleyen dibin bozmasını yalnız o ısırır (400 satırda belge yüksekliği sabit kalır). GEREKÇE: 400 satır belgenin render
    /// dilimi olduğu kararlı rejimdir — uzun bir derlemede okuyucunun asıl durduğu yer: her batch tepeden kırpar. Teori iki rejimi de koşar;
    /// takibin isteği +∞ (eski <c>ScrollToEnd</c>) olunca iki satır da kırmızı.</para>
    /// </summary>
    [StaTheory]
    [InlineData(100)] // render diliminin altında: belge her batch'te büyür
    [InlineData(400)] // belge render dilimidir: her batch tepeden kırpar
    public void A_sideways_step_in_a_following_narrative_survives_new_output(int lines)
    {
        using var narrative = new RealizedNarrative(lines);
        using var pill = VisibilityToggles.Of(narrative.View.Pill);
        NarrativeSnapshots.Deliver(narrative.Store, narrative.View, [LongLine(400)]);
        DispatcherPump.DrainToIdle();
        narrative.Press(Key.Right);
        var editor = narrative.View.Editor;
        double sideways = editor.HorizontalOffset;
        Assert.True(sideways > 0, "test kurgusu: → yana kaydırmalıydı");
        Assert.True(narrative.View.FollowsBottom); // ön-koşul: yatay adım direksiyonu almaz

        NarrativeSnapshots.Deliver(narrative.Store, narrative.View, ["live0", "live1", "live2", "live3"]);
        DispatcherPump.DrainToIdle();

        Assert.Equal(sideways, editor.HorizontalOffset, precision: 1);
        Assert.True(narrative.IsShown("live3"), "the follow did not bring the new lines into view");
        AssertFollowingAtTheBottom(narrative);
        Assert.Equal(0, pill.Value);
    }

    /// <summary>
    /// [B2 fix 2 · madde 3] Yana kaydırıp okuyan okuyucu takip geri gelince de (bekleme dönüşü) yatay konumunu korur: dönüşte belge render
    /// dilimine kırpılır ve dibe istenir — istek yine takibin isteğidir, yalnız dikey; dönüş TAM dibe iner (uzaklık 0, pill gizli, imleç
    /// canlı dipte). [B2 fix 3 · madde 11] Kırpma büyük de olabilir: okuyucu yukarıdayken bir görünür alandan fazla satır gelirse dönüş o kadar
    /// satırı kırpar ve eski ofset kırpılmış belgenin sonunun ötesinde kalır.
    /// <para>KIRMIZI (madde 3'ten önce): dönüşün kırpması <c>ScrollToEnd</c> istiyordu — yatay konum 0'a dönüyordu.</para>
    /// <para>[B2 fix 3 · madde 11-12] Büyük kırpma satırı (60) eklendi — PİN, kod değişmeden yeşil: dönüşün dibe isteği eski ofset ölçülmeden
    /// uygulanır. İnceleme M-1'in varsaydığı mekanizma gerçektir (ölçüldü): kırpma ile istek arasına bir yerleşim konursa eski dikey ofset
    /// kırpılmış belgenin sonunun ötesinde ölçülür, editör yalnız son (boş) satırı kurar, yatay içerik genişliği çöker ve yatay konum 0'a
    /// kenetlenir — yalnız 60 satırlık satır kırmızı (<c>Expected: 7,2 / Actual: 0</c>); dönüş +∞ isterse iki satır da kırmızı. Kural kırpmanın
    /// kendisindedir (<c>ConsoleView.TrimNarrativeWhenFollowing</c>: kırpma ile istek arasında yerleşim koşmaz). Dönüşün tam dibe inişi de iddia
    /// edildi (inceleme M-3). Gelen satırlar geniştir: dipte de yatay taşma vardır, yatay konum korunabilir.</para>
    /// </summary>
    [StaTheory]
    [InlineData(4)]
    [InlineData(60)] // bir görünür alandan (~30 satır) fazlası
    public void A_sideways_step_survives_the_return_to_following(int arriving)
    {
        using var narrative = new RealizedNarrative(lines: 400);
        // Uzun satırın altında birkaç satır: okuyucu dipten ayrılınca uzun satır görünür alanda kalır (yatay kaydırma görünür satırlardan ölçülür).
        NarrativeSnapshots.Deliver(narrative.Store, narrative.View, [LongLine(400), .. Enumerable.Range(0, 5).Select(i => $"after{i}")]);
        DispatcherPump.DrainToIdle();
        narrative.WheelAwayFromTheBottom();
        NarrativeSnapshots.Deliver(narrative.Store, narrative.View, [.. Enumerable.Range(0, arriving).Select(i => LongLine(401 + i))]); // takip yokken: kırpılmaz
        DispatcherPump.DrainToIdle();
        Assert.True(narrative.View.Document.LineCount > ConsoleView.SeededDocumentLines, "test kurgusu: dönüşte kırpılacak satır olmalıydı");
        narrative.Press(Key.Right);
        var editor = narrative.View.Editor;
        double sideways = editor.HorizontalOffset;
        Assert.True(sideways > 0, "test kurgusu: → yana kaydırmalıydı");

        narrative.ElapseAnchorTimers(); // okuyucu elini çekti: dibe dönüş, takip, kırpma

        Assert.Equal(ConsoleView.RenderSliceLines, narrative.View.Document.LineCount); // dönüşün kırpması koştu
        Assert.Equal(sideways, editor.HorizontalOffset, precision: 1);
        AssertFollowingAtTheBottom(narrative);
    }

    /// <summary>
    /// [B2 fix 1 · F2 · M3] Shift'li adımlar seçimdir ve editörde kalır: Shift+↓ ilk satırı seçer, Shift+PageDown seçimi bir sayfa büyütür,
    /// Shift+PageUp onu geri küçültür.
    /// <para>PİN — düzeltmeden önce de yeşildi: görünüm kaydırma tablosu yalnız değiştiricisiz (ve Ctrl'li ←/→) adımları alır.</para>
    /// </summary>
    [StaFact]
    public void Shift_steps_still_select_text()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.OpenPage(lines: 1000);
        var editor = narrative.View.Editor;

        narrative.PressWithModifiers(Key.Down, ModifierKeys.Shift);
        Assert.Equal("page0\n", editor.SelectedText);
        narrative.PressWithModifiers(Key.PageDown, ModifierKeys.Shift);
        int page = editor.SelectionLength;
        Assert.True(page > "page0\n".Length, "Shift+PageDown did not extend the selection");
        narrative.PressWithModifiers(Key.PageUp, ModifierKeys.Shift);
        Assert.Equal("page0\n", editor.SelectedText);
    }

    /// <summary>
    /// [B2 fix 1 · F2 · M5] <c>⌄ latest</c>'in kaydırma animasyonu sürerken basılan bir tuş animasyonu durdurur: sıradaki kare tuşun
    /// kaydırmasını ezmez (her kullanıcı kaydırma jesti gibi — <c>UserScrollSignal</c>).
    /// <para>KIRMIZI (düzeltmeden önce): tuş animasyona dokunmuyordu — animasyon editöre bağlı kalıyor, sonraki kareler görünümü dibe
    /// sürmeye devam ediyordu.</para>
    /// <para>Ölçü animasyonun kendisidir (<c>ScrollAnimator.VerticalOffset</c> canlandırılıyor mu) — kareleri bekleyen bir duvar saati
    /// ölçümü değil.</para>
    /// </summary>
    [StaFact]
    public void A_key_pressed_during_the_latest_pills_animation_stops_it()
    {
        using var motion = MotionScope.Enable(new FakeMotionSettings { AnimationsEnabled = true });
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.WheelAwayFromTheBottom();
        narrative.ClickLatest();
        Assert.True(ScrollAnimation.IsAnimating(narrative.View.Editor), "test kurgusu: pill bir kaydırma animasyonu başlatmalıydı");

        narrative.Press(Key.PageUp);

        Assert.False(ScrollAnimation.IsAnimating(narrative.View.Editor));
        Assert.True(narrative.DistanceFromBottom > narrative.View.Editor.ViewportHeight / 2, "the key's scroll did not stand");
    }

    /// <summary>
    /// [B2 fix 2 · madde 2] Okuyucunun HER kaydırma jesti <c>⌄ latest</c>'in uçuştaki animasyonunu durdurur — tekerlek, kaydırma çubuğu ve
    /// editörün kendi işlediği tuşlar (Ctrl+End, Shift'li seçim adımı) da: kural tek yerdedir (<c>UserScrollSignal</c>), jest hangi kanaldan
    /// gelirse gelsin. (Konsolun tuş tablosundaki adım: <see cref="A_key_pressed_during_the_latest_pills_animation_stops_it"/>.)
    /// <para>KIRMIZI (madde 2'den önce): animasyonu yalnız tekerlek (<c>ScrollAnimator.EnableUserCancellation</c>) ve konsolun tuş tablosu
    /// durduruyordu; çubuğu sürüklemek, Ctrl+End ve Shift+PageDown animasyona dokunmuyor, sıradaki kareler görünümü jestin altından dibe
    /// sürmeye devam ediyordu. Tekerlek satırı PİN: eski kancanın yerine geçen kablonun tekerleği de taşıdığını korur.</para>
    /// </summary>
    [StaTheory]
    [InlineData("wheel")]
    [InlineData("scrollbar")]
    [InlineData("Ctrl+End")]
    [InlineData("Shift+PageDown")]
    public void Every_scroll_gesture_during_the_latest_pills_animation_stops_it(string gesture)
    {
        using var motion = MotionScope.Enable(new FakeMotionSettings { AnimationsEnabled = true });
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.WheelAwayFromTheBottom();
        narrative.ClickLatest();
        Assert.True(ScrollAnimation.IsAnimating(narrative.View.Editor), "test kurgusu: pill bir kaydırma animasyonu başlatmalıydı");

        switch (gesture)
        {
            case "wheel": narrative.WheelUp(); break;
            case "scrollbar": narrative.DragTheScrollbar(); break;
            case "Ctrl+End": narrative.PressCtrlEnd(); break;
            default: narrative.PressWithModifiers(Key.PageDown, ModifierKeys.Shift); break;
        }

        Assert.False(ScrollAnimation.IsAnimating(narrative.View.Editor));
    }

    /// <summary>
    /// [B2 fix 2 · madde 2 · B2 fix 3 · madde 2] Yana bir jest (→, yatay çubuk, yatay tekerlek) <c>⌄ latest</c>'in animasyonunu DURDURMAZ: dikey
    /// bir kaydırma jesti değildir — animasyon yalnız dikey konumu sürer, jest yalnız yataydır; direksiyonu da almaz
    /// (<see cref="A_sideways_gesture_in_a_following_narrative_does_not_take_the_steering"/>).
    /// <para>KIRMIZI (→, madde 2'den önce): konsolun tuş tablosu her adımda — ←/→ dahil — animasyonu durduruyordu; kural artık jestin tek
    /// tanımındadır (<c>UserScrollSignal</c>).</para>
    /// <para>KIRMIZI (yatay çubuk, B2 fix 3 · madde 2'den önce): sinyalin çubuk kanalı yönü ayırmıyordu — yatay çubuğu sürüklemek de dikey
    /// animasyonu durduruyordu.</para>
    /// <para>[son temizlik · madde 1] Yatay tekerlek satırı eklendi (PİN — yatay tekerlek o güne dek sinyale hiç ulaşmıyordu; artık yana kanal
    /// olarak ulaşır ve bu satır onun animasyonu durdurmamasını korur). Anlatıya uzun bir satır eklendi: tekerleğin kancası yalnız yatay taşma
    /// varken kaydırır.</para>
    /// </summary>
    [StaTheory]
    [InlineData("→")]
    [InlineData("horizontal bar")]
    [InlineData("horizontal wheel")]
    public void A_sideways_gesture_during_the_latest_pills_animation_does_not_stop_it(string gesture)
    {
        using var motion = MotionScope.Enable(new FakeMotionSettings { AnimationsEnabled = true });
        using var narrative = new RealizedNarrative(lines: 400);
        // Yatay taşma; uzun satırın altında birkaç satır: okuyucu dipten ayrılınca da uzun satır görünür alanda kalır.
        NarrativeSnapshots.Deliver(narrative.Store, narrative.View, [LongLine(400), .. Enumerable.Range(0, 5).Select(i => $"after{i}")]);
        DispatcherPump.DrainToIdle();
        narrative.WheelAwayFromTheBottom();
        narrative.ClickLatest();
        HoldTheJumpMidway(narrative);

        SidewaysGesture(narrative, gesture);

        Assert.True(ScrollAnimation.IsAnimating(narrative.View.Editor));
    }

    /// <summary>
    /// [B2 fix 3 · madde 2] Akışı izleyen anlatıda yana bir jest (→, yatay çubuk, yatay tekerlek) direksiyonu ALMAZ: takip sürer — gelen satırlar
    /// görünümü dibe götürür, pill çıkmaz. Kural tek cümledir (<c>UserScrollSignal</c>): dikey jestler (tekerlek, dikey çubuk, dikey kaydırma
    /// tuşları) direksiyonu alır ve pill uçuşunu durdurur; yana jestler (←/→, Ctrl+←/→, yatay tekerlek, yatay çubuk) ikisini de yapmaz — yalnız
    /// direksiyon ZATEN okuyucudaysa bekleme dönüşünü erteler
    /// (<see cref="A_sideways_gesture_of_a_reader_holding_the_console_postpones_the_return_to_the_bottom"/>).
    /// <para>PİN (→): madde 3'ten beri yeşil — ← / → sinyalin dikey tuşları değildir.</para>
    /// <para>KIRMIZI (yatay çubuk, madde 2'den önce): sinyalin çubuk kanalı yönü ayırmıyordu — yatay çubuğu sürüklemek direksiyonu alıyor,
    /// takibi beş saniye durduruyordu.</para>
    /// <para>[son temizlik · madde 1] Yatay tekerlek satırı eklendi (PİN): tekerlek artık sinyalin yana kanalına ulaşır; bu satır onun direksiyonu
    /// almamasını korur.</para>
    /// </summary>
    [StaTheory]
    [InlineData("→")]
    [InlineData("horizontal bar")]
    [InlineData("horizontal wheel")]
    public void A_sideways_gesture_in_a_following_narrative_does_not_take_the_steering(string gesture)
    {
        using var narrative = new RealizedNarrative(lines: 400);
        NarrativeSnapshots.Deliver(narrative.Store, narrative.View, [LongLine(400)]); // yatay taşma: yatay çubuk görünür
        DispatcherPump.DrainToIdle();

        SidewaysGesture(narrative, gesture);
        NarrativeSnapshots.Deliver(narrative.Store, narrative.View, ["live0", "live1", "live2", "live3"]);
        DispatcherPump.DrainToIdle();

        Assert.True(narrative.IsShown("live3"), "the follow did not bring the new lines into view");
        AssertFollowingAtTheBottom(narrative);
    }

    /// <summary>
    /// [son temizlik · madde 1] Paneli tutan okuyucunun yana jesti (→, yatay çubuk, yatay tekerlek) dibe dönüşü ERTELER: okuyucu tekerlekle
    /// yukarı çıkar (direksiyon onda) ve beş saniye dolmadan uzun bir satırı yana kaydırır — dönüş son DİKEY jestten beş saniye sonra gelmez,
    /// yana jestten beş saniye sonra gelir. Yana jest direksiyonu almaz
    /// (<see cref="A_sideways_gesture_in_a_following_narrative_does_not_take_the_steering"/>); yalnız okuyucunun elinin hâlâ panelde olduğunu söyler.
    /// <para>KIRMIZI (değişiklikten önce): yana jest bekleme dönüşünü kurmuyordu (←/→ sinyalin tuşları değildi, yatay tekerlek sinyale hiç
    /// ulaşmıyordu, yatay çubuk B2 fix 3'ten beri süzülüyordu) — konsol son dikey jestten beş saniye sonra, okuyucu uzun satırı okurken dibe
    /// dönüyordu (sahip gözlemi). B2 fix 3'ten önce yatay çubuk beklemeyi kuruyordu, ama direksiyonu da alıyordu.</para>
    /// </summary>
    [StaTheory]
    [InlineData("→")]
    [InlineData("horizontal bar")]
    [InlineData("horizontal wheel")]
    public void A_sideways_gesture_of_a_reader_holding_the_console_postpones_the_return_to_the_bottom(string gesture)
    {
        using var narrative = new RealizedNarrative(lines: 400);
        // Uzun satırın altında birkaç satır: okuyucu dipten ayrılınca uzun satır görünür alanda kalır (yatay taşma görünür satırlardan ölçülür).
        NarrativeSnapshots.Deliver(narrative.Store, narrative.View, [LongLine(400), .. Enumerable.Range(0, 5).Select(i => $"after{i}")]);
        DispatcherPump.DrainToIdle();
        var idle = TimeSpan.FromMilliseconds(BottomAnchorDecision.IdleResumeMs);
        var shortly = TimeSpan.FromMilliseconds(100);
        narrative.WheelAwayFromTheBottom(); // direksiyon okuyucuda: dönüş beş saniye sonra
        narrative.Elapse(idle - shortly);
        AssertReleased(narrative); // ön-koşul: bekleme henüz dolmadı

        SidewaysGesture(narrative, gesture);
        narrative.Elapse(shortly); // son DİKEY jestten beş saniye sonra

        AssertReleased(narrative); // dönüş yok: yana jest beklemeyi baştan kurdu
        narrative.Elapse(idle - shortly); // yana jestten beş saniye sonra
        AssertFollowingAtTheBottom(narrative);
    }

    /// <summary>Okuyucunun yana bir jesti — üç kanal: → (konsolun tuş tablosu), yatay çubuk ve yatay tekerlek (uygulamanın kancası).</summary>
    private static void SidewaysGesture(RealizedNarrative narrative, string gesture)
    {
        switch (gesture)
        {
            case "→": narrative.Press(Key.Right); break;
            case "horizontal bar": narrative.DragTheHorizontalBar(); break;
            case "horizontal wheel": narrative.WheelSideways(); break;
            default: throw new ArgumentOutOfRangeException(nameof(gesture), gesture, null);
        }
    }

    /// <summary>
    /// [B2 fix 2 · madde 2b] Uçuştaki <c>⌄ latest</c> atlamasını durduran jest atlama penceresini de kapatır: kaydırma hemen bir okuyucu
    /// kaydırması olarak yargılanır — tuştan hemen sonra takip bırakılmış, pill görünür, imleç gizlidir; sıradaki batch görünümü geri
    /// çekmez.
    /// <para>KIRMIZI (madde 2b'den önce): pencere (560 ms) açık kalıyordu — anchor tuşun kaydırmasını yok sayıyor, durum atlamanın iyimser
    /// "dipte"sinde kalıyor ve pill pencere dolana dek gizli kalıyordu. Batch'in görünümü çekmemesi direksiyondandır (PİN). Uçuş belirlenimli
    /// tutulur (<see cref="HoldTheJumpMidway"/>): kural dardır — yalnız GERÇEKTEN uçuşan bir atlamayı durduran jest pencereyi kapatır.</para>
    /// </summary>
    [StaFact]
    public void A_page_up_that_stops_the_latest_pills_flight_hands_the_console_to_the_reader_at_once()
    {
        using var motion = MotionScope.Enable(new FakeMotionSettings { AnimationsEnabled = true });
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.WheelAwayFromTheBottom();
        narrative.ClickLatest();
        HoldTheJumpMidway(narrative);

        narrative.Press(Key.PageUp);

        Assert.Equal(Visibility.Visible, narrative.View.Pill.Visibility); // tuştan hemen sonra, pencere dolmadan
        AssertReleased(narrative);
        double reading = narrative.View.Editor.VerticalOffset;
        NarrativeSnapshots.Deliver(narrative.Store, narrative.View, ["live0", "live1", "live2", "live3"]);
        DispatcherPump.DrainToIdle();
        Assert.Equal(reading, narrative.View.Editor.VerticalOffset);
        Assert.False(narrative.View.FollowsBottom);
    }

    /// <summary>
    /// [B2 fix 1 · M7 · karar 44-45] Hareket açıkken proje sayfasında <c>⌄ latest</c> sayfanın GERÇEK sonuna canlandırılmış bir atlama
    /// başlatır, oraya İNER ve takibi hiçbir anda açmaz: atlama sürerken gelen canlı satır görünümü çekmez ve belgeyi kırpmaz; atlama logun
    /// son satırına iner (atlama başladığında sayfanın sonu olan yer — sonradan gelen satırlar onun altındadır); atlama penceresi (yakalanan
    /// zamanlayıcı) dolunca da takip gelmez.
    /// <para>PİN — karar 45'ten beri yeşil (bozma: anchor'ın takip izni kaldırılınca pencere sonunda takip açılır; animasyon paneli
    /// kaydırmazsa iniş iddiası kırmızı). İniş belirlenimlidir: saat sonuna konumlanır (<see cref="LandTheJump"/> —
    /// <c>SeekAlignedToLastTick</c> bir tik beklemeden yazar), duvar saati beklenmez.</para>
    /// <para>[DEĞİŞEN KURULUM — B2 fix 3 · madde 3] Eski doc'un iddiası: "atlamanın inişi sınanmaz — ileri sarılabilen bir saat tutamağı
    /// yoktur, inişi görmek gerçek zaman pompalamak demektir". Yanlıştı: uygulanan saat okunabilir (<c>ActiveClockInspector.AppliedClock</c>) ve
    /// denetleyicisi saati bir tik beklemeden konumlar; iniş iddiası eklendi.</para>
    /// </summary>
    [StaFact]
    public void An_animated_latest_pill_jump_on_a_project_page_aims_at_its_end_lands_there_and_never_turns_following_on()
    {
        using var motion = MotionScope.Enable(new FakeMotionSettings { AnimationsEnabled = true });
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.OpenPage(lines: 1000);
        var editor = narrative.View.Editor;
        double end = editor.ExtentHeight - editor.ViewportHeight;

        narrative.ClickLatest();

        var clock = ScrollAnimation.ClockOf(editor); // atlama canlandırıldı (saat yoksa fırlatır)
        Assert.Equal(end, ((DoubleAnimationUsingKeyFrames)clock.Timeline).KeyFrames[0].Value, precision: 1);
        Assert.Equal(Visibility.Collapsed, narrative.View.Pill.Visibility); // atlama penceresi
        Assert.False(narrative.View.FollowsBottom);

        narrative.View.AppendBatch("live0\nlive1\nlive2\nlive3\n"); // atlama sürerken derlenen projenin satırları
        DispatcherPump.DrainToIdle();
        Assert.False(narrative.View.FollowsBottom);
        Assert.Equal(1000 + 4 + 1, narrative.View.Document.LineCount); // kırpılmadı

        LandTheJump(narrative);
        Assert.Equal(end, editor.VerticalOffset, precision: 1);
        Assert.True(narrative.IsShown("page999"), "the jump did not land on the log's last line");

        narrative.ElapseAnchorTimers(); // atlama penceresi doldu
        Assert.False(narrative.View.FollowsBottom);
    }

    // ---------------------------------------------------------------- [B2 fix 1 · F1] pill içerik büyüyünce yeniden okunur

    /// <summary>
    /// [B2 fix 1 · F1] Proje sayfasında <c>⌄ latest</c> ile sona atlayan okuyucunun altına derlenen projenin yeni satırları gelince pill
    /// GERİ GELİR — görünüm yerinden oynamaz, sayfa izlemez (pill'in tek kuralı: canlı dipten uzaklık).
    /// <para>KIRMIZI (düzeltmeden önce): konsolun TextView'i yalnız ofset değişince kaydırma olayı yayar; izlenmeyen sayfa büyüyünce olay
    /// yoktu ve pill yeniden okunmuyordu — okuyucu kaydırana dek gizli kalıyordu.</para>
    /// </summary>
    [StaFact]
    public void The_latest_pill_returns_when_new_lines_grow_a_project_page_below_the_reader()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.OpenPage(lines: 1000);
        narrative.ClickLatest();
        Assert.Equal(Visibility.Collapsed, narrative.View.Pill.Visibility); // ön-koşul: sonda
        double landed = narrative.View.Editor.VerticalOffset;

        narrative.View.AppendBatch("live0\nlive1\nlive2\nlive3\n"); // eşikten uzun (4 satır)
        DispatcherPump.DrainToIdle();

        Assert.Equal(Visibility.Visible, narrative.View.Pill.Visibility);
        Assert.Equal(landed, narrative.View.Editor.VerticalOffset);
        Assert.False(narrative.View.FollowsBottom);
        AssertCaretFollowsPill(narrative);
    }

    /// <summary>
    /// [B2 fix 1 · F1] Görünüme sığan kısa bir proje sayfası derlenen projenin satırlarıyla paneli aşınca <c>⌄ latest</c>'i gösterir;
    /// görünüm tepede kalır.
    /// <para>KIRMIZI (düzeltmeden önce): pill açılıştaki kısa sayfanın kararında (gizli) kalıyordu.</para>
    /// </summary>
    [StaFact]
    public void A_short_project_page_that_grows_past_the_panel_offers_the_latest_pill()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.OpenPage(lines: 5);
        Assert.Equal(Visibility.Collapsed, narrative.View.Pill.Visibility); // ön-koşul: sayfa sığıyor

        narrative.View.AppendBatch(string.Concat(Enumerable.Range(0, 100).Select(i => $"live{i}\n")));
        DispatcherPump.DrainToIdle();

        Assert.Equal(Visibility.Visible, narrative.View.Pill.Visibility);
        Assert.Equal(0, narrative.View.Editor.VerticalOffset);
        AssertCaretFollowsPill(narrative);
    }

    /// <summary>
    /// [B2 fix 1 · F1] Anlatıda dibin iki satır üstünde okuyan (eşiğin içinde, direksiyon onda) okuyucuya yeni satırlar gelince
    /// <c>⌄ latest</c> görünür — uzaklık eşiği aştı; görünüm yerinden oynamaz (kontrolcü kararı: kural "pill canlı uzaklığı izler", iki
    /// kipte de). [B2 fix 2 · madde 1] Pill görünürken prompt imleci gizlidir: okuyucu artık canlı dipte değildir.
    /// <para>KIRMIZI (düzeltmeden önce): pill okuyucu kaydırana ya da 5 sn dönüşe dek gizli kalıyordu. İki satır seçildi: son metin satırı
    /// görünür alandaysa AvalonEdit eklemede kendiliğinden yeniden ölçer; iki satır yukarıda ölçüm hiç gelmezdi — ekleme onu ister.</para>
    /// <para>KIRMIZI (madde 1'den önce): imleç yalnız takip durumuna (<c>IsStuck</c>) bakıyordu — okuyucu eşiğin içinden kaydırdığı için
    /// durum "dipte" kalıyor, imleç pill'le birlikte görünüyordu.</para>
    /// </summary>
    [StaFact]
    public void A_reader_two_lines_above_the_end_of_the_narrative_sees_the_latest_pill_when_lines_arrive()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.Press(Key.Up);
        narrative.Press(Key.Up);
        Assert.True(narrative.View.StickToBottom);                          // ön-koşul: eşiğin içinde
        Assert.Equal(Visibility.Collapsed, narrative.View.Pill.Visibility);
        double reading = narrative.View.Editor.VerticalOffset;

        NarrativeSnapshots.Deliver(narrative.Store, narrative.View, ["live0", "live1", "live2", "live3"]);
        DispatcherPump.DrainToIdle();

        Assert.Equal(Visibility.Visible, narrative.View.Pill.Visibility);
        Assert.Equal(reading, narrative.View.Editor.VerticalOffset);
        AssertCaretFollowsPill(narrative);
    }

    /// <summary>
    /// [B2 fix 1 · F1] Akışı izleyen anlatı satır aldıkça <c>⌄ latest</c> HİÇ kımıldamaz — belge render dilimine dek büyürken de, sonra
    /// kırpılırken de: yeniden okuma yalnız izlenmeyen belgede yapılır; izleyen belgede ekleme ile takibin dibe isteğinin
    /// (<c>ConsoleView.RequestBottom</c>) uygulanması arasındaki uzaklık bir an eşiği aşar ve pill yanıp sönerdi. [B2 fix 2 · madde 1] Prompt imleci de hiç kımıldamaz: imleç pill'in
    /// DUYURULAN görünürlüğünü okur (<c>BottomAnchorBehavior.PillShown</c>), o anki uzaklığı değil.
    /// <para>PİN — düzeltmeden önce de yeşildi; düzeltmenin sınırını korur (bozma: takip koruması kaldırılınca pill, imleç canlı
    /// <c>ShowPill</c>'i okuyunca imleç kırmızı). Anlatı render diliminin altından başlar: dilimdeki belge kırpmayla aynı boyda kalır,
    /// büyümez — yanıp sönme yalnız büyüyen belgede görülür.</para>
    /// </summary>
    [StaFact]
    public void A_following_narrative_never_toggles_the_latest_pill_while_lines_arrive()
    {
        using var narrative = new RealizedNarrative(lines: 100);
        using var pill = VisibilityToggles.Of(narrative.View.Pill);
        using var caret = VisibilityToggles.Of(narrative.View.ActiveLineOverlay);

        foreach (int batch in Enumerable.Range(0, 20))
        {
            NarrativeSnapshots.Deliver(narrative.Store, narrative.View, [.. Enumerable.Range(0, 10).Select(i => $"live{batch}.{i}")]);
            DispatcherPump.DrainToIdle();
        }

        Assert.Equal(0, pill.Value);
        Assert.Equal(0, caret.Value);
        AssertFollowingAtTheBottom(narrative);
        AssertCaretFollowsPill(narrative);
    }

    /// <summary>
    /// [B2 fix 1 · F1] Pill'in gizli olduğu bir proje sayfasından (sığan kısa sayfa) ← Back <c>⌄ latest</c>'i bir an bile göstermez: takasın
    /// kendi ölçümünde pill yeniden okunmaz, takasın sonunda oturmuş uzaklıktan okunur (sahada "geri diyorsun latest çıkıyor kayboluyor").
    /// <para>PİN — düzeltmeden önce de yeşildi; düzeltmenin sınırını korur (bozma: takas koruması kaldırılınca kırmızı).</para>
    /// </summary>
    [StaFact]
    public void Back_from_a_short_project_page_never_flashes_the_latest_pill()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.OpenPage(lines: 5);
        Assert.Equal(Visibility.Collapsed, narrative.View.Pill.Visibility); // ön-koşul
        using var pill = VisibilityToggles.Of(narrative.View.Pill);

        narrative.View.ReplaceRunDocument(NarrativeSnapshots.Of(narrative.Store)); // ← Back
        DispatcherPump.DrainToIdle();

        Assert.Equal(0, pill.Value);
        AssertFollowingAtTheBottom(narrative);
        AssertCaretFollowsPill(narrative);
    }

    // ---------------------------------------------------------------- [B2 fix 2 · madde 1] prompt imleci yalnız canlı dipte

    /// <summary>
    /// [B2 fix 2 · madde 1] Canlı dibe dönüş imleci geri getirir: dibin iki satır üstünde okurken satırlar gelip <c>⌄ latest</c> çıkan
    /// okuyucu End'e basınca ya da elini çekince (bekleme) pill kalkar ve prompt imleci yeniden görünür. Bu dönüşte takip durumu
    /// (<c>IsStuck</c>) hiç değişmez — dipte sayılmaya devam ediyordu —; imleç yalnız pill'in duyurulan görünürlüğü yenilendiği için döner.
    /// <para>PİN — madde 1'den önce de yeşildi (imleç yalnız takip durumuna bakıyor, hiç gizlenmiyordu); kuralın dönüş yolunu korur (bozma:
    /// anchor pill değişimini yalnız durum değişince duyurursa imleç gizli, pill görünür kalır).</para>
    /// </summary>
    [StaTheory]
    [InlineData("End")]
    [InlineData("let go")]
    public void Returning_to_the_live_bottom_brings_the_prompt_caret_back(string how)
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.Press(Key.Up);
        narrative.Press(Key.Up);
        NarrativeSnapshots.Deliver(narrative.Store, narrative.View, ["live0", "live1", "live2", "live3"]);
        DispatcherPump.DrainToIdle();
        Assert.Equal(Visibility.Visible, narrative.View.Pill.Visibility); // ön-koşul: okuyucu canlı dipten uzakta

        if (how == "End") narrative.Press(Key.End);
        else narrative.ElapseAnchorTimers();

        Assert.Equal(Visibility.Collapsed, narrative.View.Pill.Visibility);
        AssertCaretFollowsPill(narrative);
    }

    /// <summary>
    /// [B2 fix 2 · madde 1] Proje sayfasında prompt imleci hiç görünmez — <c>⌄ latest</c> gelip giderken de: pill sona atlayınca kalkar,
    /// derlenen projenin satırlarıyla geri gelir, End ile yine kalkar; imleç bir kez bile kımıldamaz (sayfa okunan bir logdur, izlenen bir
    /// akış değil).
    /// <para>PİN — madde 1'den önce de yeşildi (bozma: imleç kuralından proje kipi koşulu kaldırılınca sayfanın sonunda imleç yanar).</para>
    /// </summary>
    [StaFact]
    public void A_project_page_never_shows_the_prompt_caret_while_the_latest_pill_comes_and_goes()
    {
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.OpenPage(lines: 1000);
        Assert.Equal(Visibility.Collapsed, narrative.View.ActiveLineOverlay.Visibility); // ön-koşul
        using var pill = VisibilityToggles.Of(narrative.View.Pill);
        using var caret = VisibilityToggles.Of(narrative.View.ActiveLineOverlay);

        narrative.ClickLatest();                                         // pill kalkar: sayfanın sonu
        narrative.View.AppendBatch("live0\nlive1\nlive2\nlive3\n");      // pill geri gelir: okuyucunun altında yeni satırlar
        DispatcherPump.DrainToIdle();
        narrative.Press(Key.End);                                        // pill yine kalkar

        Assert.True(pill.Value >= 3, $"test kurgusu: pill gelip gitmeliydi, değişim {pill.Value}");
        Assert.Equal(Visibility.Collapsed, narrative.View.Pill.Visibility);
        Assert.Equal(0, caret.Value);
    }

    /// <summary>
    /// [B2 fix 2 · madde 1] <c>⌄ latest</c>'in canlandırılmış atlaması sürerken prompt imleci gizlidir — görünüm henüz canlı dipte değildir
    /// ve imleç (son satır görünür alanda değilken eski yerinde kalır) metnin üstünde "sanki sondaymış gibi" dururdu; imleç atlama PENCERESİ
    /// kapanınca dipte yeniden görünür — inişte değil: atlama indikten sonra pencere açıkken (hareket açıkken ~280 ms) imleç hâlâ gizlidir,
    /// ikinci bir zamanlayıcı kurulmaz (kabul edilen gecikme). İniş belirlenimlidir: saat sonuna konumlanır (<see cref="LandTheJump"/>).
    /// <para>KIRMIZI (madde 1'den önce): atlama başlarken anchor durumu iyimser biçimde "dipte" yapar (<c>BeginJump</c>) ve imleç yalnız
    /// ona bakıyordu — uçuş boyunca görünürdü.</para>
    /// <para>[B2 fix 3 · madde 6] Eski adı <c>…_returns_after_landing</c>; iddia ettiği şey pencerenin kapanışıydı (inceleme C-3) — ad ve
    /// iniş sonrası, pencere açıkken gizli kalma iddiası bunu söylüyor.</para>
    /// </summary>
    [StaFact]
    public void The_prompt_caret_hides_during_the_latest_pills_flight_and_returns_when_the_jump_window_closes()
    {
        using var motion = MotionScope.Enable(new FakeMotionSettings { AnimationsEnabled = true });
        using var narrative = new RealizedNarrative(lines: 400);
        narrative.WheelAwayFromTheBottom();
        narrative.ClickLatest();
        Assert.True(ScrollAnimation.IsAnimating(narrative.View.Editor), "test kurgusu: pill bir kaydırma animasyonu başlatmalıydı");

        Assert.Equal(Visibility.Collapsed, narrative.View.ActiveLineOverlay.Visibility); // uçuşta

        LandTheJump(narrative);
        Assert.Equal(Visibility.Collapsed, narrative.View.ActiveLineOverlay.Visibility); // indi, pencere açık
        narrative.ElapseAnchorTimers(); // atlama penceresi kapandı

        AssertFollowingAtTheBottom(narrative);
    }

    /// <summary>Bir öğenin görünürlüğü kaç kez değişti — sayım bitince bırakılan bir dinleyici (gerçek değer değişimleri): <c>⌄ latest</c> ve
    /// prompt imleci.</summary>
    private sealed class VisibilityToggles : IDisposable
    {
        private static readonly System.ComponentModel.DependencyPropertyDescriptor Visibility =
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(UIElement));
        private readonly UIElement _element;

        private VisibilityToggles(UIElement element)
        {
            _element = element;
            Visibility.AddValueChanged(element, OnChanged);
        }

        public int Value { get; private set; }

        public static VisibilityToggles Of(UIElement element) => new(element);

        private void OnChanged(object? sender, EventArgs e) => Value++;

        public void Dispose() => Visibility.RemoveValueChanged(_element, OnChanged);
    }

    /// <summary>Uçuştaki kaydırma animasyonunu (<c>⌄ latest</c> atlaması) sonuna konumlar ve yerleşimi oturtur: saat bir tik beklemeden
    /// hedefi DP'ye yazar (<see cref="ActiveClockInspector.Seek"/>), görünüm hedefe iner. Atlama penceresi kapanmaz — o anchor'ın
    /// zamanlayıcısıdır (<see cref="RealizedNarrative.ElapseAnchorTimers"/>).</summary>
    private static void LandTheJump(RealizedNarrative narrative)
    {
        ActiveClockInspector.Seek(ScrollAnimation.ClockOf(narrative.View.Editor), progress: 1);
        DispatcherPump.DrainToIdle();
    }

    /// <summary>Uçuştaki atlamayı yolun ortasında TUTAR: saat yarıya konumlanır (DP'ye bir tik beklemeden yazılır) ve duraklatılır —
    /// sonraki jest, testi koşan makinenin hızından bağımsız olarak gerçekten uçuşan bir hareketle karşılaşır (yolun ortası, etkin evre).</summary>
    private static void HoldTheJumpMidway(RealizedNarrative narrative)
    {
        var clock = ScrollAnimation.ClockOf(narrative.View.Editor);
        ActiveClockInspector.Seek(clock, progress: 0.5);
        clock.Controller.Pause();
        Assert.Equal(ClockState.Active, clock.CurrentState); // ön-koşul: atlama uçuşta
    }
}
