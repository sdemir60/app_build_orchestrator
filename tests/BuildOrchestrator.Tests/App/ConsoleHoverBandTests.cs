using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [Task 6/design v1.17.0 §9 "3 — Konsol ve event stream"] Konsol gövdesinin imleci standart OK'tur (el işareti
/// yalnız tıklanabilir öğelere aittir — AvalonEdit'in bağlantı üstündeki <c>Hand</c>'i DIŞINDA, review round 1
/// I-1) ve imlecin altındaki satır tam genişlik, <c>Brush.Surface</c> zeminli bir bantla işaretlenir. Prototip:
/// <c>BuildApp.jsx</c> konsol gövdesi <c>cursor: default</c> (~satır 1080) + <c>.bo-cline</c> (satır 62-63).
///
/// <para><b>Neden <see cref="ConsoleView.UpdateHoverBand"/> bir Y PARAMETRESİ alır, gerçek <c>MouseMove</c>'dan
/// okumaz:</b> WPF'in gerçek <c>MouseDevice</c> konumu (<c>MouseEventArgs.GetPosition</c>) işletim sistemi
/// imlecinin GERÇEK ekran konumunu sorar — headless'ta simüle edilemez (bkz. <c>ActionBarHoverTests</c>'in aynı
/// gözlemi). Desen <see cref="ConsoleView.EvaluateChunkScroll"/> ile AYNIDIR: üretim <c>MouseMove</c> kablosu
/// gerçek konumu ÇIKARIP bu metoda GEÇER, testler üretimin ÇAĞIRDIĞI metodun ta kendisini doğrudan sürer
/// (paralel bir kopya yol DEĞİL). Aynı gerekçeyle scroll/ekleme/mod-değişimi tazelemesi (<c>RefreshHoverBand</c>)
/// da CANLI <c>Mouse.GetPosition</c> sorgulamaz — en son bilinen GERÇEK <c>MouseMove</c> konumunu yeniden besler
/// (review round 1 M-2), bu yüzden GERÇEK <c>OnScrollOffsetChanged</c> kablosuyla test edilebilir.</para>
///
/// <para><b>Cursor testleri neden gerçek bir olay yükseltir:</b> AvalonEdit'in <c>SelectionMouseHandler</c>'ı
/// IBeam'i statik <c>Cursor</c> özelliği üzerinden DEĞİL, yönlendirilmiş <c>QueryCursor</c> olayını
/// <c>TextArea</c> seviyesinde ele alarak dayatır (decompile ile doğrulandı — <c>TextArea</c>/<c>TextView</c>
/// hiçbir yerde kendi <c>Cursor</c>'ını atamaz). <c>RaiseEvent</c> GERÇEK WPF yönlendirme motorunu (kabarcıklanma
/// + kayıtlı sınıf handler'ları) sürer ve konum sorgusuna BAĞLI DEĞİLDİR — <see cref="ConsoleView.ForceArrowCursor"/>
/// konuma bakmaksızın (yalnız AvalonEdit'in kararına bakarak, review round 1 I-1) SON SÖZÜ söyler, bu yüzden
/// gerçek donanım imleç konumu testin sonucunu etkilemez.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class ConsoleHoverBandTests
{
    private static ConsoleView Realized(double width = 200, double height = 200)
    {
        var view = new ConsoleView { AnimationsEnabledProvider = () => true };
        DsResources.Realize(DsResources.NewHost(), view);
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        return view;
    }

    // ---------------------------------------------------------------- saf çekirdek: ConsoleHoverBand.LineAt

    [Fact]
    public void LineAt_returns_the_line_whose_range_contains_documentY()
    {
        var lines = new (double Top, double Height)[] { (0, 20), (20, 20), (40, 20) };

        Assert.Equal((20.0, 20.0), ConsoleHoverBand.LineAt(lines, 25));
    }

    [Fact]
    public void LineAt_top_boundary_is_inclusive_bottom_boundary_is_exclusive()
    {
        var lines = new (double Top, double Height)[] { (0, 20), (20, 20) };

        Assert.Equal((20.0, 20.0), ConsoleHoverBand.LineAt(lines, 20)); // tam sınırda: alttaki satır
        Assert.Equal((0.0, 20.0), ConsoleHoverBand.LineAt(lines, 19.999));
    }

    [Fact]
    public void LineAt_returns_null_outside_every_range()
    {
        var lines = new (double Top, double Height)[] { (10, 20) };

        Assert.Null(ConsoleHoverBand.LineAt(lines, 5));
        Assert.Null(ConsoleHoverBand.LineAt(lines, 30));
    }

    [Fact]
    public void LineAt_returns_null_for_an_empty_list()
    {
        Assert.Null(ConsoleHoverBand.LineAt([], 0));
    }

    // ---------------------------------------------------------------- imleç: standart ok (yalnız IBeam -> Arrow)

    [StaFact]
    public void Console_body_cursor_is_Arrow_on_the_editor_TextArea_and_TextView()
    {
        var view = Realized();

        Assert.Equal(Cursors.Arrow, view.Editor.Cursor);
        Assert.Equal(Cursors.Arrow, view.Editor.TextArea.Cursor);
        Assert.Equal(Cursors.Arrow, view.Editor.TextArea.TextView.Cursor);
    }

    /// <summary>AvalonEdit'in GERÇEK <c>SelectionMouseHandler</c>'ı devrede (metin yüklü, layout gerçek) — olay
    /// <c>TextView</c>'den kabarcıklanarak yükseltilir, tıpkı üretimde olduğu gibi. Düz metin üstünde AvalonEdit
    /// IBeam ister; son değer Arrow olmalıdır.</summary>
    [StaFact]
    public void QueryCursor_bubbling_from_the_real_TextView_resolves_to_Arrow_even_though_AvalonEdit_claims_it_first()
    {
        var view = Realized();
        view.AppendBatch("line0\nline1\nline2\n");
        view.UpdateLayout();

        var args = new QueryCursorEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.QueryCursorEvent };
        view.Editor.TextArea.TextView.RaiseEvent(args);

        Assert.Equal(Cursors.Arrow, args.Cursor);
        Assert.True(args.Handled);
    }

    /// <summary>Üretimin ÇAĞIRDIĞI metodun ta kendisi (<see cref="ConsoleView.ForceArrowCursor"/>) — AvalonEdit
    /// zaten IBeam'i "ele almış" (<c>Handled=true</c>) olsa bile SON SÖZ bize aittir (<c>handledEventsToo:true</c>
    /// kablosunun sözleşmesi).</summary>
    [StaFact]
    public void ForceArrowCursor_converts_an_IBeam_AvalonEdit_already_claimed_to_Arrow()
    {
        var view = Realized();
        var args = new QueryCursorEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.QueryCursorEvent, Cursor = Cursors.IBeam, Handled = true };

        view.ForceArrowCursor(view.Editor, args);

        Assert.Equal(Cursors.Arrow, args.Cursor);
        Assert.True(args.Handled);
    }

    /// <summary>[Review round 1 I-1] AvalonEdit'in <c>EnableHyperlinks</c>'i varsayılan AÇIKTIR ve Ctrl basılıyken
    /// bir bağlantı üstünde <c>Cursors.Hand</c> döner — eski (koşulsuz) davranış bunu Arrow'a çeviriyordu ve "el
    /// işareti yalnız tıklanabilir öğelere aittir" kuralını bağlantılarda BOZUYORDU. Doğrusu: yalnız IBeam'i
    /// (ya da hiç ele alınmamışı) çevir, AvalonEdit'in verdiği başka HER kararı (Hand dahil) olduğu gibi bırak.</summary>
    [StaFact]
    public void ForceArrowCursor_leaves_a_Hand_cursor_from_a_hyperlink_untouched()
    {
        var view = Realized();
        var args = new QueryCursorEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.QueryCursorEvent, Cursor = Cursors.Hand, Handled = true };

        view.ForceArrowCursor(view.Editor, args);

        Assert.Equal(Cursors.Hand, args.Cursor);
        Assert.True(args.Handled);
    }

    /// <summary>Hiç ele alınmamış bir sorguda (AvalonEdit'in hiç dokunmadığı köşe durumlar — Cursor null,
    /// Handled false) varsayılan hâlâ Arrow'dur.</summary>
    [StaFact]
    public void ForceArrowCursor_defaults_an_untouched_query_to_Arrow()
    {
        var view = Realized();
        var args = new QueryCursorEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.QueryCursorEvent };

        view.ForceArrowCursor(view.Editor, args);

        Assert.Equal(Cursors.Arrow, args.Cursor);
        Assert.True(args.Handled);
    }

    // ---------------------------------------------------------------- satır hover bandı

    /// <summary>Bandın Y aralığı, yüksekliği VE genişliği hep AYNI GERÇEK çağrıya bağlıdır — genişlik ayrı bir
    /// testte YALITILMIŞ olsaydı (review round 1 M-3) yalnız statik XAML <c>Stretch</c>'i sınardı ve
    /// <see cref="ConsoleView.UpdateHoverBand"/> hiç çalışmasa da geçerdi.</summary>
    [StaFact]
    public void UpdateHoverBand_bands_the_real_visual_line_under_the_given_Y_with_full_width_and_Brush_Surface()
    {
        var view = Realized(width: 240, height: 200);
        view.AppendBatch("line0\nline1\nline2\nline3\n");
        view.UpdateLayout();

        var textView = view.Editor.TextArea.TextView;
        Assert.True(textView.VisualLinesValid);
        Assert.True(textView.VisualLines.Count >= 3);
        var target = textView.VisualLines[2]; // "line2"
        double mouseY = target.VisualTop - textView.ScrollOffset.Y + 1; // satırın 1px içi

        view.UpdateHoverBand(mouseY);
        view.UpdateLayout();

        var expected = textView.TransformToAncestor(view.TiltHost)
            .Transform(new Point(0, target.VisualTop - textView.ScrollOffset.Y));
        Assert.Equal(expected.Y, view.HoverBand.Margin.Top, precision: 2);
        Assert.Equal(target.Height, view.HoverBand.Height, precision: 2);
        Assert.Equal(HorizontalAlignment.Stretch, view.HoverBand.HorizontalAlignment);

        // [M-3] Genişlik AYNI (genuine) çağrının sonucudur: panel kenarından kenaradır (editörün 12+12px iç
        // dolgusunu — Padding — belirgin biçimde aşar), TiltHost'un tam genişliğine eşittir.
        Assert.Equal(view.TiltHost.ActualWidth, view.HoverBand.ActualWidth, precision: 1);
        Assert.True(view.HoverBand.ActualWidth > 200);

        var fill = Assert.IsType<SolidColorBrush>(view.HoverBand.Fill);
        var surface = DsResources.TokenColor(view, "Brush.Surface");
        Assert.Equal(surface, fill.Color);
    }

    /// <summary>[Review round 1 M-3] Önce bandın GERÇEKTEN görünür olduğu doğrulanır (aksi halde bu test bir
    /// stub'la da — hiçbir satır hiç bantlanmasa da — sessizce geçerdi, çünkü varsayılan Fill zaten saydamdır).</summary>
    [StaFact]
    public void UpdateHoverBand_outside_every_line_hides_a_previously_shown_band()
    {
        var view = Realized();
        view.AppendBatch("line0\nline1\n");
        view.UpdateLayout();

        view.UpdateHoverBand(1); // önce GERÇEKTEN görünür yap
        Assert.True(((SolidColorBrush)view.HoverBand.Fill).Color.A > 0, "test kurgusu: bant ilk çağrıda görünür olmalıydı");

        view.UpdateHoverBand(100_000); // belgenin dışında bir Y

        var fill = Assert.IsType<SolidColorBrush>(view.HoverBand.Fill);
        Assert.Equal(0, fill.Color.A);
    }

    /// <summary>
    /// [Review round 1 I-2] Fare AYNI satır aralığında kaldığı sürece (sürekli gelen <c>MouseMove</c> akışının
    /// EN SIK durumu) <c>UpdateHoverBand</c> renk geçişini YENİDEN KURMAZ —
    /// <see cref="MotionTokens.TransitionColor"/> her çağrıda yeni bir <c>ColorAnimationUsingKeyFrames</c> inşa
    /// edip <c>BeginAnimation</c> çağırırdı; sayaç bunun olmadığını KANITLAR (üretim kodunun kendisi sayar,
    /// paralel bir ölçüm yolu değil).
    /// </summary>
    [StaFact]
    public void UpdateHoverBand_does_not_retransition_while_the_pointer_stays_on_the_same_line()
    {
        var view = Realized();
        view.AppendBatch("line0\nline1\nline2\n");
        view.UpdateLayout();

        var textView = view.Editor.TextArea.TextView;
        var target = textView.VisualLines[1];
        double topY = target.VisualTop - textView.ScrollOffset.Y;

        view.UpdateHoverBand(topY + 1);
        Assert.Equal(1, view.HoverColorTransitionCount);

        view.UpdateHoverBand(topY + 2); // AYNI satır, farklı piksel
        view.UpdateHoverBand(Math.Min(topY + target.Height - 0.5, topY + 4));
        Assert.Equal(1, view.HoverColorTransitionCount); // yeniden kurulmadı

        var next = textView.VisualLines[2];
        view.UpdateHoverBand(next.VisualTop - textView.ScrollOffset.Y + 1); // FARKLI satır
        Assert.Equal(2, view.HoverColorTransitionCount);
    }

    /// <summary>
    /// [Review round 1 M-1] Yarım satır kadar kaydırılınca ilk görsel satır EKRANIN ÜSTÜNDE kısmen görünür kalır
    /// (üst kenarı negatif ekran-Y'dedir) — bandın gerçek (kırpılmamış) üst kenarı <c>TextView</c>'in kendi
    /// (0,0) noktasının YUKARISINA taşar; test önce bunu doğrular (senaryonun GERÇEKTEN taştığını kanıtlar,
    /// aksi halde kırpma hiç devreye girmeden sessizce geçebilirdi), sonra bandın kırpılmış hâlinin
    /// <c>TextView</c>'in dolgu tarafına (üst 8px) HİÇ taşmadığını kanıtlar.
    /// </summary>
    [StaFact]
    public void UpdateHoverBand_clips_a_partially_visible_line_to_the_TextViews_own_bounds()
    {
        var view = Realized(width: 200, height: 60);
        view.AppendBatch(string.Concat(Enumerable.Range(0, 20).Select(i => $"line{i}\n")));
        view.UpdateLayout();
        view.StickToBottom = false;

        var textView = view.Editor.TextArea.TextView;
        double lineHeight = textView.DefaultLineHeight;
        view.Editor.ScrollToVerticalOffset(lineHeight / 2); // tam yarım satır — ilk satır tepede kesilir
        view.UpdateLayout();

        var first = textView.VisualLines[0];
        double unclippedTop = first.VisualTop - textView.ScrollOffset.Y;
        Assert.True(unclippedTop < 0, "test kurgusu: ilk satır tepede kesilmiyor");

        view.UpdateHoverBand(unclippedTop + 1); // satırın (ekran-dışı) üst ucuna yakın, ama hâlâ ONUN aralığında

        double viewTopInHost = textView.TransformToAncestor(view.TiltHost).Transform(new Point(0, 0)).Y;
        Assert.True(view.HoverBand.Margin.Top >= viewTopInHost - 0.01, "bant TextView'in üst sınırının YUKARISINA taştı");
    }

    /// <summary>Fare panelden — editörün 12px iç dolgusu (Padding) DAHİL — çıkınca bant kalkar (design v1.17.0
    /// §9). [Review round 1 M-1] Kablo artık <c>TextView</c> değil <c>EditorControl</c> seviyesindedir (dolguyu
    /// da kapsasın diye); bu test GERÇEK <c>MouseLeave</c> olayını <c>EditorControl</c>'ın ta kendisinde
    /// yükseltir — üretimin ctor'da kurduğu KABLONUN aynısı, bir yardımcı metodu değil.</summary>
    [StaFact]
    public void Real_MouseLeave_on_the_EditorControl_hides_the_band_even_when_only_the_padding_was_hovered()
    {
        var view = Realized();
        view.AppendBatch("line0\nline1\n");
        view.UpdateLayout();
        view.UpdateHoverBand(1);
        Assert.True(((SolidColorBrush)view.HoverBand.Fill).Color.A > 0);

        view.Editor.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });

        Assert.Equal(0, ((SolidColorBrush)view.HoverBand.Fill).Color.A);
    }

    /// <summary>
    /// [Review round 1 M-2] İmleç FİZİKSEL olarak kımıldamadan (aynı <c>TextView</c>-yerel EKRAN konumu) bir
    /// scroll olursa, o pikselin ALTINDAKİ satır değişmiş olabilir — bant ESKİ satırda asılı kalmamalı, YENİ
    /// satırı takip etmelidir. <c>OnScrollOffsetChanged</c> üretimin GERÇEK <c>TextView.ScrollOffsetChanged</c>
    /// kablosunun çağırdığı metodun ta kendisidir (dosyanın kendi <c>I-1 test gözlemi</c> deseni).
    /// </summary>
    [StaFact]
    public void Real_scroll_with_the_pointer_position_fixed_moves_the_band_to_the_new_line_under_it()
    {
        var view = Realized(width: 200, height: 80);
        view.AppendBatch(string.Concat(Enumerable.Range(0, 60).Select(i => $"line{i}\n")));
        view.UpdateLayout();
        // [test kurgusu] AppendBatch varsayılan takip (StickToBottom) açıkken dibe kaydırır — bu testin kendi
        // scroll'u TEPEDEN başlamalı (aksi halde "3 satır aşağı" isteği zaten dipteyken kelepçelenir, hiçbir
        // şey değişmez). Takip kapatılır ve tepeye dönülür; scroll'u artık yalnız BU test yönetir.
        view.StickToBottom = false;
        view.Editor.ScrollToVerticalOffset(0);
        view.UpdateLayout();

        const double fixedScreenY = 5; // imlecin SABİT ekran konumu — bir daha hiç kımıldamaz
        var textView = view.Editor.TextArea.TextView;
        view.UpdateHoverBand(fixedScreenY);
        double heightBefore = view.HoverBand.Height;
        Assert.True(heightBefore > 0, "test kurgusu: ilk hover görünür olmalıydı");
        var lineBefore = ConsoleHoverBand.LineAt(
            textView.VisualLines.Select(v => (v.VisualTop, v.Height)).ToList(),
            fixedScreenY + textView.ScrollOffset.Y);
        Assert.NotNull(lineBefore); // hangi BELGE satırının bantlandığı — ekran konumu değil, satır KİMLİĞİ

        // Kullanıcı tekerleği çevirir: imleç YERİNDE durur (fixedScreenY hiç değişmez — dolayısıyla bandın
        // EKRAN konumu da aynı kalmalıdır, bu BEKLENEN ve DOĞRU davranıştır), içerik onun ALTINDAN kayar — üç
        // satır kadar. Değişmesi gereken şey ekrandaki o pikselin ALTINDAKİ belge satırının KİMLİĞİDİR.
        view.Editor.ScrollToVerticalOffset(view.Editor.VerticalOffset + (heightBefore * 3));
        view.UpdateLayout();
        view.OnScrollOffsetChanged(); // üretimin GERÇEK ScrollOffsetChanged kablosunun çağırdığı metodun ta kendisi

        Assert.True(textView.ScrollOffset.Y > 0, "test kurgusu: scroll gerçekten olmadı");

        // Yeni konum, GÜNCEL scroll-offset'e göre o pikselin altındaki satırla tutarlı olmalı — üretimin
        // KENDİSİNİN kullandığı saf çekirdekle (ConsoleHoverBand.LineAt) çapraz doğrulanır.
        var lineAfter = ConsoleHoverBand.LineAt(
            textView.VisualLines.Select(v => (v.VisualTop, v.Height)).ToList(),
            fixedScreenY + textView.ScrollOffset.Y);
        Assert.NotNull(lineAfter);
        Assert.NotEqual(lineBefore!.Value.Top, lineAfter!.Value.Top); // GERÇEKTEN farklı bir belge satırına geçti
        Assert.Equal(lineAfter.Value.Height, view.HoverBand.Height, precision: 2); // bant O satırı yansıtıyor
    }

    /// <summary>
    /// [Review round 2 M-2, senaryo a] Bir satırdan KÜÇÜK bir scroll (animasyonlu kaydırmanın ara kareleri gibi)
    /// belge-Y'yi hâlâ AYNI satırın aralığında bırakabilir — <see cref="ConsoleView.RefreshHoverBand"/> `_forceHoverRefresh`
    /// bayrağını işaretlemeden <see cref="ConsoleView.UpdateHoverBand"/>'a devretseydi, o metodun kendi "aynı satır"
    /// kısayolu (I-2 perf) devreye girer ve EKRAN konumu YENİDEN HESAPLANMAZDI — bant içerikten kopup eski
    /// pikselde asılı kalırdı. İmleç fiziksel olarak kımıldamasa da (yeniden çağrılan Y AYNI), scroll GERÇEKTEN
    /// olduğu için bandın ekran konumu genel olarak izlenebilir biçimde güncellenmelidir.
    /// </summary>
    [StaFact]
    public void Real_scroll_smaller_than_one_line_still_recomputes_the_bands_screen_geometry()
    {
        var view = Realized(width: 200, height: 200);
        view.AppendBatch(string.Concat(Enumerable.Range(0, 60).Select(i => $"line{i}\n")));
        view.UpdateLayout();
        view.StickToBottom = false;
        view.Editor.ScrollToVerticalOffset(0);
        view.UpdateLayout();

        var textView = view.Editor.TextArea.TextView;
        double lineHeight = textView.DefaultLineHeight;
        const double fixedScreenY = 100; // bir satır sınırından uzak, ortada bir yer

        view.UpdateHoverBand(fixedScreenY);
        double topBefore = view.HoverBand.Margin.Top;
        double documentYBefore = fixedScreenY + textView.ScrollOffset.Y;

        // Yarım satırdan da küçük bir scroll — belge-Y büyük ihtimalle HÂLÂ aynı satırın aralığındadır.
        double tinyScroll = lineHeight * 0.3;
        view.Editor.ScrollToVerticalOffset(view.Editor.VerticalOffset + tinyScroll);
        view.UpdateLayout();
        double documentYAfter = fixedScreenY + textView.ScrollOffset.Y;
        var sameLineBothTimes = ConsoleHoverBand.LineAt(
            textView.VisualLines.Select(v => (v.VisualTop, v.Height)).ToList(), documentYBefore) ==
            ConsoleHoverBand.LineAt(textView.VisualLines.Select(v => (v.VisualTop, v.Height)).ToList(), documentYAfter);
        Assert.True(sameLineBothTimes, "test kurgusu: küçük scroll satır kimliğini değiştirmemeliydi (asıl sınanan senaryo bu)");

        view.OnScrollOffsetChanged(); // üretimin GERÇEK ScrollOffsetChanged kablosunun çağırdığı metodun ta kendisi

        // Satır kimliği AYNI kalsa da, içerik lineHeight*0.3 kadar kaydığı için bandın EKRAN konumu da o kadar
        // kaymalıdır — eski pikselde (topBefore) KALAMAZ.
        Assert.NotEqual(topBefore, view.HoverBand.Margin.Top);
        Assert.Equal(topBefore - tinyScroll, view.HoverBand.Margin.Top, precision: 1);
    }

    /// <summary>
    /// [Review round 2 M-2, senaryo b] Aynı scroll offsette (mod değişimi/`ClearRunDocument` sözleşmesi) çok
    /// daha KISA bir belgeye geçilirse, eski bantlı satır artık YOK — `_forceHoverRefresh` bayrağı işaretlenmeden
    /// bırakılsaydı belge-Y hâlâ eski (yanlış) aralıkta sayılabilir ve bant, hiçbir satırın olmadığı bir yerde asılı kalırdı.
    /// </summary>
    [StaFact]
    public void Real_document_swap_at_the_same_offset_does_not_keep_a_stale_band_where_no_line_exists()
    {
        var view = Realized(width: 200, height: 200);
        view.AppendBatch(string.Concat(Enumerable.Range(0, 60).Select(i => $"line{i}\n")));
        view.UpdateLayout();
        view.StickToBottom = false;
        view.Editor.ScrollToVerticalOffset(0);
        view.UpdateLayout();

        var textView = view.Editor.TextArea.TextView;
        var fifthLine = textView.VisualLines[5];
        double y = fifthLine.VisualTop - textView.ScrollOffset.Y + 1;
        view.UpdateHoverBand(y);
        Assert.True(((SolidColorBrush)view.HoverBand.Fill).Color.A > 0, "test kurgusu: 5. satır bantlanmalıydı");

        // Aynı offsette (0), çok daha KISA bir belgeye geç (mod değişimi sözleşmesi) — eski 5. satır artık YOK.
        view.ClearRunDocument();
        view.UpdateLayout(); // VisualLinesChanged GERÇEK kablosu burada ateşlenir (RefreshHoverBand'ı tetikler)

        // Y hâlâ eski 5. satırın olduğu ekran konumunda ama belge artık tek boş satırdan ibaret — o konum
        // ARTIK hiçbir satırın aralığında değil, bant KALKMALIDIR (var olmayan eski satırda asılı kalamaz).
        Assert.Equal(0, ((SolidColorBrush)view.HoverBand.Fill).Color.A);
    }

    /// <summary>
    /// [Review round 2 M-2, senaryo c] ÜRETİM SIRASI: AvalonEdit <c>ScrollOffsetChanged</c>'i YENİDEN
    /// ÖLÇÜMDEN ÖNCE yayınlar (decompile ile doğrulandı — <c>IScrollInfo.SetVerticalOffset</c> önce olayı
    /// ateşler, <c>InvalidateMeasure</c> sonra gelir) — eski viewport'un ÇOK ötesine tek seferde atlayan bir
    /// scroll'da (büyük bir takip batch'i, dibe anlık zıplama) bu an <c>VisualLines</c> hâlâ ESKİ bölgeyi
    /// yansıtır. Doğru davranış: o anda GEÇİCİ olarak gizlenmek (eşleşme yok, GERÇEKTEN) ama imleç konumunu
    /// SAKLAMAK — biraz sonra gelecek gerçek <c>VisualLinesChanged</c> (yeniden ölçüm bitince) bandı doğru
    /// satırda GERİ GETİRMELİDİR.
    /// </summary>
    [StaFact]
    public void Real_scroll_event_before_relayout_hides_then_the_later_VisualLinesChanged_recovers_the_band()
    {
        var view = Realized(width: 200, height: 80);
        view.AppendBatch(string.Concat(Enumerable.Range(0, 300).Select(i => $"line{i}\n")));
        view.UpdateLayout();
        view.StickToBottom = false;
        view.Editor.ScrollToVerticalOffset(0);
        view.UpdateLayout();

        const double fixedScreenY = 5;
        view.UpdateHoverBand(fixedScreenY);
        Assert.True(((SolidColorBrush)view.HoverBand.Fill).Color.A > 0, "test kurgusu: ilk hover görünür olmalıydı");

        var textView = view.Editor.TextArea.TextView;
        double farOffset = textView.DefaultLineHeight * 250; // eski viewport'un ÇOK ötesinde bir atlama

        // ÜRETİM SIRASI: scroll offset'i değiştir ve GERÇEK ScrollOffsetChanged kablosunun çağırdığı metodu
        // hemen çağır — UpdateLayout BİLEREK burada henüz çağrılmaz (VisualLines hâlâ ESKİ viewport'u yansıtır).
        view.Editor.ScrollToVerticalOffset(farOffset);
        view.OnScrollOffsetChanged();

        Assert.Equal(0, ((SolidColorBrush)view.HoverBand.Fill).Color.A); // eski VisualLines'ta karşılık yok → gizlenir

        // Şimdi gerçek yeniden ölçüm olur — VisualLinesChanged GERÇEK kablosu ateşlenir ve bandı tazeler.
        view.UpdateLayout();

        var fresh = view.Editor.TextArea.TextView;
        var expected = ConsoleHoverBand.LineAt(
            fresh.VisualLines.Select(v => (v.VisualTop, v.Height)).ToList(),
            fixedScreenY + fresh.ScrollOffset.Y);
        Assert.NotNull(expected); // test kurgusu: yeni konumda gerçekten bir satır olmalı
        Assert.True(((SolidColorBrush)view.HoverBand.Fill).Color.A > 0, "bant VisualLinesChanged sonrasında geri gelmeliydi");
        Assert.Equal(expected!.Value.Height, view.HoverBand.Height, precision: 2);
    }

    /// <summary>Aktif prompt satırı overlay'i bant tarafından etkilenmez: overlay ayrı bir öğedir, bandın
    /// Margin/Height'ı ona hiç dokunmaz.</summary>
    [StaFact]
    public void UpdateHoverBand_never_touches_the_active_line_overlay()
    {
        var view = Realized();
        view.ShowReady();
        view.UpdateLayout();
        var marginBefore = view.ActiveLineOverlay.Margin;
        var visibilityBefore = view.ActiveLineOverlay.Visibility;

        view.UpdateHoverBand(1);

        Assert.Equal(marginBefore, view.ActiveLineOverlay.Margin);
        Assert.Equal(visibilityBefore, view.ActiveLineOverlay.Visibility);
    }

    // ---------------------------------------------------------------- seçim işlevi etkilenmez

    /// <summary>
    /// [Review round 1 M-3] Programatik <c>Select</c> kadar GÜÇLÜ değil ama daha DOĞRUDAN bir kanıt: AvalonEdit'in
    /// KENDİ <c>SelectionMouseHandler</c>'ı, bir sol-tık basışını yalnız <c>e.Handled</c> HENÜZ false iken ele
    /// alır (decompile ile doğrulandı — aksi halde <c>mode = MouseSelectionMode.None</c>'da bırakıp hiçbir şey
    /// yapmadan döner). Normal bir tıklamanın sonunda AvalonEdit KOŞULSUZ <c>e.Handled = true</c> yazar; bu
    /// değerin GERÇEKTEN true çıkması, hover/cursor kablomuzun (QueryCursor, MouseMove/MouseLeave) bu basışı
    /// ÖNCEDEN yutmadığının — yani sürükle-seç'in hâlâ başlayabildiğinin — doğrudan kanıtıdır.
    /// </summary>
    [StaFact]
    public void Real_MouseLeftButtonDown_still_reaches_AvalonEdits_own_selection_start_unblocked()
    {
        var view = Realized();
        view.AppendBatch("hello\nworld\n");
        view.UpdateLayout();

        var args = MouseInput.PressLeft(view.Editor.TextArea.TextView);

        Assert.True(args.Handled, "AvalonEdit'in kendi seçim-başlatma mantığı çalışmadı — bir şey basışı önceden yuttu");

        MouseInput.ReleaseLeft(view.Editor.TextArea.TextView);
    }

    /// <summary>Renk kuralı, hiza, satır yüksekliği, METİN SEÇİLEBİLİRLİĞİ değişmez (design v1.17.0 §9): hover
    /// bandı ve zorunlu-Arrow kablosu eklendikten SONRA da gerçek bir seçim GERÇEKTEN kurulabilmeli.</summary>
    [StaFact]
    public void Text_selection_still_works_after_the_hover_and_cursor_wiring_run()
    {
        var view = Realized();
        view.AppendBatch("hello\nworld\n");
        view.UpdateLayout();

        view.UpdateHoverBand(1);
        view.Editor.TextArea.TextView.RaiseEvent(new QueryCursorEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.QueryCursorEvent });

        view.Editor.Select(0, 5);

        Assert.Equal(5, view.Editor.SelectionLength);
        Assert.Equal("hello", view.Editor.SelectedText);
    }
}
