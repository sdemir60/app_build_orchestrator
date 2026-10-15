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

    /// <summary>Bir proje sayfası açılır — kabuğun kart seçimiyle aynı çağrı (<c>PlayCascade</c>) — ve yerleşim oturur.</summary>
    private static void OpenPage(RealizedNarrative narrative, int lines)
    {
        narrative.View.PlayCascade(Page(lines));
        DispatcherPump.DrainToIdle();
    }

    /// <summary>Görünür alanın tepesindeki satırın metni — geometri TextView'in kendisinden.</summary>
    private static string TopLine(RealizedNarrative narrative)
    {
        var textView = narrative.View.Editor.TextArea.TextView;
        return narrative.View.Document.GetText(textView.GetDocumentLineByVisualTop(textView.VerticalOffset));
    }

    /// <summary>Metni <paramref name="text"/> olan satır şu an görünür satırlar arasında mı.</summary>
    private static bool IsShown(RealizedNarrative narrative, string text) =>
        narrative.View.Editor.TextArea.TextView.VisualLines.Any(v => narrative.View.Document.GetText(v.FirstDocumentLine) == text);

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

        OpenPage(narrative, lines: 300);

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

        OpenPage(narrative, lines: 300);

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

        OpenPage(narrative, lines: 5);

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
        OpenPage(narrative, lines: 300);
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

        OpenPage(narrative, lines: 1000);

        Assert.Equal(0, narrative.View.Editor.VerticalOffset);
        Assert.Equal("page0", TopLine(narrative));
        Assert.False(narrative.View.FollowsBottom);

        narrative.PressCtrlEnd();

        Assert.Equal(0, narrative.DistanceFromBottom, precision: 1);
        Assert.True(IsShown(narrative, "page999"), "the log's last line is not on screen at the end of its page");
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
        OpenPage(narrative, lines: 1000);
        if (where == "middle") foreach (int _ in Enumerable.Range(0, 10)) narrative.Press(Key.PageDown);
        if (where == "end") narrative.PressCtrlEnd();
        double readingAt = narrative.View.Editor.VerticalOffset;
        string[] live = [.. Enumerable.Range(0, 6).Select(i => $"live{i}")];

        foreach (var pair in live.Chunk(2))
        {
            narrative.View.AppendBatch(string.Concat(pair.Select(line => line + "\n"))); // pompanın batch'i (kabuk: Route.Raw)
            DispatcherPump.DrainToIdle();
        }

        string[] logThenLive = [.. Page(1000), .. live];
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
        OpenPage(narrative, lines: 300);
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
        OpenPage(narrative, lines: 1000);
        Assert.Equal(Visibility.Visible, narrative.View.Pill.Visibility); // ön-koşul: uzun sayfanın tepesi

        narrative.ClickLatest();

        Assert.Equal(0, narrative.DistanceFromBottom, precision: 1);
        Assert.True(IsShown(narrative, "page999"), "the pill did not land on the log's last line");
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
    /// [dalga 3 · karar 46] Dipten art arda PageUp'lar anlatıda her basışta TAM BİR görünür alan yukarı yürür; belgenin tepesine varan
    /// basış, her okuyucu jestindeki gibi orada durur ve önceki dilimi getirir (yükleyici + prepend telafisi): okuyucunun içerikteki yeri
    /// sıçramaz, sıradaki basış yürüyüşü sürdürür — anlatının başına dek; sonunda belge anlatının tamamıdır.
    /// <para>KIRMIZI (karardan önce): ilk basış dilimin tepesine sıçrıyordu (içerikte ~170 satır, bir görünür alan ~30 satır).</para>
    /// </summary>
    [StaFact]
    public void Page_ups_walk_the_narrative_up_one_viewport_at_a_time_and_bring_the_previous_slices_on_the_way()
    {
        using var narrative = new RealizedNarrative(lines: 600);
        double viewport = narrative.View.Editor.ViewportHeight;

        for (int press = 0; press < 60 && narrative.ContentTop() > 0; press++)
        {
            double before = narrative.ContentTop();
            double documentTop = narrative.DocumentTop(); // bir basış belgenin tepesinde durur (her jest gibi), dilim orada gelir
            narrative.Press(Key.PageUp);
            Assert.Equal(Math.Max(documentTop, before - viewport), narrative.ContentTop(), precision: 1);
        }

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
        OpenPage(narrative, lines: 1000);
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
}
