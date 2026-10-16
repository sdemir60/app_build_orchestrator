using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.7.0 §2.5 · animasyon spec §2] Panel geçişi (proje logu ↔ <c>← Back</c>) TEK PARÇA "tilt in"dir:
/// log bloğu 14 px aşağıdan yukarı oturur ve aynı anda görünür olur; dönüş aynasıdır (14 px yukarıdan).
///
/// <para><b>[DEĞİŞEN KURAL — 2026-10-16, ölçüldü] Geçiş 2B'dir, 3B değil.</b> Eski kural (2026-08-15'ten beri): blok,
/// <c>PerspectiveCamera</c>'lı bir <c>Viewport3D</c>'de dokulu bir düzlemdi, X ekseni etrafında 7° → 0 dönüyordu — prototipin
/// <c>perspective(900px) rotateX(7deg)</c> trapezi için. Ölçülen bedel: 4K tam ekranda <c>Viewport3D</c>'nin ilk çizimi
/// WPF'in 3B çizim hattına ~830 MB grafik belleği bağlıyor ve geçiş bitince bırakmıyordu — proje sayfasına ilk tıklamada
/// uygulama 300 MB'den 1,1 GB'ye çıkıyor ve orada kalıyordu (aynı binary, yalnız 3B geçiş kapalıyken 333 MB; v1.9.0'da da
/// aynı). Kullanıcı kararı: trapez ipucundan vazgeçilir, hareket 2B kayma + solma olarak kalır. Doku da (geçişin başında alınan
/// <c>RenderTargetBitmap</c>) artık yoktur: 2B dönüşüm gerçek editörü kaydırır.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ConsoleTiltInTests
{
    /// <summary>Gerçek bir geçiş için panelin ÖLÇÜLMÜŞ olması şart (ölçülmemiş panelde geçiş oynamaz).</summary>
    private static ConsoleView Realized(double width = 200, double height = 100)
    {
        var view = new ConsoleView { AnimationsEnabledProvider = () => true };
        DsResources.Realize(DsResources.NewHost(), view);
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        return view;
    }

    /// <summary>Viewport'u kesin aşan bir anlatı (render dilimi son 200 satırla sınırlar).</summary>
    private static NarrativeSnapshot LongNarrative() =>
        NarrativeSnapshots.FromText(string.Concat(Enumerable.Range(0, 300).Select(i => $"line{i}\n")));

    /// <summary>Geçişin sürdüğü 2B dönüşüm: kabın <c>RenderTransform</c>'u bir <see cref="TranslateTransform"/>'dur.</summary>
    private static TranslateTransform SlideOf(ConsoleView view) => Assert.IsType<TranslateTransform>(view.TiltHost.RenderTransform);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var grandChild in Descendants(child)) yield return grandChild;
        }
    }

    /// <summary>
    /// <b>Kök nedenin pini:</b> geçiş görsel ağaca hiçbir 3B öğe koymaz — ne <see cref="Viewport3D"/> ne <see cref="Visual3D"/> — ve
    /// bir doku da almaz. 4K'da ~830 MB'lik grafik belleği bağlayan tam olarak bu öğeydi (sınıf özeti).
    /// <para>KIRMIZI (eski kodda): <c>PART_Tilt3D</c> adlı bir <c>Viewport3D</c> ağaçta duruyor ve geçişte <c>Visible</c> oluyordu.</para>
    /// </summary>
    [StaFact]
    public void The_transition_puts_no_3D_element_in_the_visual_tree()
    {
        var view = Realized();

        view.PlayCascade("first line\n");

        Assert.True(view.IsTransitioning, "ön koşul: geçiş oynuyor");
        Assert.DoesNotContain(Descendants(view), d => d is Viewport3D or Visual3D);
    }

    /// <summary>
    /// Açılış: blok 14 px AŞAĞIDAN yükselir ve görünmezden açılır; dönüşüm gerçek editörün kabındadır (doku yok).
    /// <para>[DEĞİŞEN KURAL] Eski pin menteşeyi (<c>RotateTransform3D.CenterY</c> = alt kenar), 7°'lik açıyı ve 900 px'lik kamerayı
    /// okuyordu; 2B geçişte menteşe yoktur — kalan ipuçları 14 px'lik kayma ve solmadır.</para>
    /// </summary>
    [StaFact]
    public void The_block_opens_sliding_up_from_14px_below()
    {
        var view = Realized();

        view.PlayCascade("first line\n");

        var slide = SlideOf(view);
        Assert.Equal(14.0, slide.Y, precision: 3);            // 14 px AŞAĞIDA başlar (CSS translateY(14px))
        Assert.Equal(0.0, slide.X);
        Assert.Equal(0.0, view.TiltHost.Opacity);             // görünmezden açılır
        Assert.True(slide.HasAnimatedProperties, "kayma animasyonu kurulmamış");
        Assert.True(view.IsTransitioning);
    }

    /// <summary>
    /// <b>Dönüş geçişi açılışın TAM AYNASIDIR.</b> Açılışta blok 14 px AŞAĞIDAN yükselir; dönüşte 14 px YUKARIDAN oturur — aynı
    /// süre, aynı eğri; değişen yalnız yön (kullanıcı kararı).
    /// <para>[DEĞİŞEN KURAL] Eski pin menteşe kenarının da aynalandığını (ÜST kenar) okuyordu; 2B'de yalnız yön kalır.</para>
    /// </summary>
    [StaFact]
    public void The_return_transition_is_the_mirror_of_the_opening_one()
    {
        var view = Realized();

        view.ShowRunDocument(NarrativeSnapshots.FromText("first\n"));

        var slide = SlideOf(view);
        Assert.Equal(-14.0, slide.Y, precision: 3);           // 14 px YUKARIDAN
        Assert.Equal(0.0, view.TiltHost.Opacity);
        Assert.True(slide.HasAnimatedProperties, "kayma animasyonu kurulmamış");
    }

    /// <summary>
    /// [DEĞİŞEN KURAL] Prompt imleci geçişe KATILMAZ (spec §1.3, §4: "log bloğunun DIŞINDA… tilt
    /// animasyonuna KATILMAZ"). Oturan şey içeriktir; imleç panelin sabit noktasıdır.
    /// </summary>
    [StaFact]
    public void The_prompt_line_stays_out_of_the_transition()
    {
        var view = Realized();

        Assert.Same(view.TiltHost, VisualTreeHelper.GetParent(view.Editor));
        Assert.NotSame(view.TiltHost, VisualTreeHelper.GetParent(view.ActiveLineOverlay));
    }

    /// <summary>Geçiş bitince dönüşüm sıfırlanır ve blok tam opak kalır (metin keskindir — geçiş boyunca da gerçek editördü).
    ///
    /// <para><b><c>LocalOnly</c>:</b> bu test GERÇEK geçiş animasyonunun bitmesini bekler (<c>PumpUntil</c>: geçiş bitene dek) ve
    /// paylaşılan CI runner'ında zamanlaması kararsız — CI koşusunda düştü; lokalde geçiyor. Eşik GEVŞETİLMEZ (CLAUDE.md): yalnız
    /// bu metot CI filtresinden çıkar (sınıfın diğer testleri CI'da koşar), lokal tam süit (yayının kapısı) onu koşturmaya devam
    /// eder.</para></summary>
    [StaFact]
    [Trait("Category", "LocalOnly")]
    public void The_transition_hands_the_panel_back_to_the_real_editor()
    {
        var view = Realized();
        view.PlayCascade("first line\n");
        Assert.Equal(0.0, view.TiltHost.Opacity); // ön-koşul: geçiş görünmezden başlar

        DispatcherPump.PumpUntil(() => !view.IsTransitioning, TimeSpan.FromSeconds(3));

        Assert.False(view.IsTransitioning);
        Assert.Equal(1.0, view.TiltHost.Opacity);
        Assert.Equal(0.0, SlideOf(view).Y, precision: 3);     // kayma bitti, blok yerinde
        Assert.False(SlideOf(view).HasAnimatedProperties);   // animasyon bırakıldı — geçiş başına bir saat tutulmaz
    }

    /// <summary>
    /// [animasyon spec §2.1/§3] Geçiş YALNIZ log bloğu remount edildiğinde oynar. Canlı bir satır eklemek onu
    /// YENİDEN TETİKLEMEZ — konsolun canlı satırları animasyonsuzdur.
    /// <para><b><c>LocalOnly</c>:</b> ön koşulu, açılış geçişinin GERÇEK animasyonla bitmesidir (<c>PumpUntil</c>, 3 sn) —
    /// <see cref="The_transition_hands_the_panel_back_to_the_real_editor"/> ile aynı bekleyiş. CI'da (main <c>cc49ff58</c>,
    /// 2026-10-12) "Collapsed beklenirken Visible" ile düştü: <c>PumpUntil</c> süre dolunca sessizce döner, geçiş paylaşılan
    /// runner'da 3 sn'de bitmemişse canlı satır geçişi yeniden oynatmış gibi görünür. Lokalde normal ve tek çekirdekte 8/8 geçti;
    /// gerçek bir yeniden oynatma her koşuda düşerdi. Ön koşul artık ayrı iddia edilir (bir daha düşerse mesaj sebebi söyler).
    /// Eşik GEVŞETİLMEZ: yalnız bu metot CI filtresinden çıkar, lokal tam süit onu koşturmaya devam eder.</para>
    /// </summary>
    [StaFact]
    [Trait("Category", "LocalOnly")]
    public void Live_output_never_replays_the_transition()
    {
        var view = Realized();
        view.ShowRunDocument(NarrativeSnapshots.FromText("first\n"));
        DispatcherPump.PumpUntil(() => !view.IsTransitioning, TimeSpan.FromSeconds(3));
        Assert.False(view.IsTransitioning, "ön koşul: açılış geçişi 3 sn içinde bitmedi");

        view.AppendBatch("second\n");
        view.AppendNarrativeBatch("third\n");

        Assert.False(view.IsTransitioning);
        Assert.Equal(1.0, view.TiltHost.Opacity);
    }

    /// <summary>
    /// <b>Anlatıya dönüş, anlatı ZATEN SON SATIRINDA ÇİZİLMİŞKEN başlar.</b>
    ///
    /// <para><b>Ölçülen kusur (kullanıcı, sahada):</b> <c>← Back</c>'te geçiş, metnin BAŞINI gösteriyor ve
    /// biter bitmez panel bir karede dibe atlıyordu. Sebebi sıradır: <see cref="ConsoleView"/> belgeyi
    /// değiştirip dibe pinliyor, ama pin'den SONRA bir yerleşim turu ZORLAMIYORDU. <c>ScrollToEnd</c> yalnız bir
    /// İSTEKTİR — <c>ScrollViewer</c> onu bir sonraki ölçüm turunda <c>TextView</c>'e iletir. Geçiş o turdan ÖNCE
    /// başladığı için hâlâ eski kareyi, yani metnin başını gösteriyordu. (Doku kalktı; kural geçişin başladığı kare için aynen
    /// geçerlidir — 2B geçiş gerçek editörü kaydırır, editör de o karede son satırı çizmiş olmalıdır.)</para>
    ///
    /// <para>Test bunu "offset doğru mu" diye SORMAZ — offset zaten mantıksal olarak doğruydu, çizilen kare
    /// yanlıştı. Bu yüzden asıl iddia şudur: geçişin başladığı anda belgenin SON SATIRI gerçekten görünür
    /// satırlar arasındadır. Bu sağlandığında imleç de doğru yerdedir: <c>PositionPrompt</c> son satır görünür
    /// pencerede değilken erken döner ve imleci ESKİ yerinde bırakır — kullanıcının "imleç sanki sondaymış gibi
    /// yazının üstünde çıkıyor" dediği şey buydu.</para>
    /// </summary>
    [StaFact]
    public void Returning_to_the_narrative_starts_with_its_last_line_already_drawn()
    {
        var view = Realized(width: 800, height: 400);

        view.ShowRunDocument(LongNarrative());

        double bottom = view.Editor.ExtentHeight - view.Editor.ViewportHeight;
        Assert.True(bottom > 0, "senaryo kurulamadı: anlatı viewport'u aşmıyor");
        Assert.InRange(view.Editor.VerticalOffset, bottom - 1, bottom + 1);

        // Geçişin başladığı KARE: belgenin son satırı gerçekten çizilmiş olmalı.
        var textView = view.Editor.TextArea.TextView;
        Assert.True(textView.VisualLinesValid, "görsel satırlar geçiş anında kurulmamış — geçiş boş/bayat bir kareyle başlar");
        int lastLine = view.Document.LineCount;
        Assert.Contains(textView.VisualLines,
            v => v.FirstDocumentLine.LineNumber <= lastLine && v.LastDocumentLine.LineNumber >= lastLine);

        // Ve imleç son satırın yanındadır — yani panelin alt yarısında, metnin üstünde değil.
        Assert.Equal(Visibility.Visible, view.ActiveLineOverlay.Visibility);
        Assert.InRange(view.ActiveLineOverlay.Margin.Top, view.TiltHost.ActualHeight / 2, view.TiltHost.ActualHeight);
    }

    /// <summary>
    /// [dalga 3 · karar 44] <b>Uzun bir proje logu, geçişini logun İLK satırı çizilmiş olarak başlatır.</b> Açılış dönüşün aynasıdır:
    /// geçişin başladığı karede belgenin ilk satırı — logun ilk satırı — görünür satırların başındadır ve geçiş oynar.
    /// <para>KIRMIZI (karardan önce): sayfa logun son 200 satırıyla kuruluyordu — 1000 satırlık logun geçişi <c>page800</c>'le başlıyordu.</para>
    /// </summary>
    [StaFact]
    public void A_long_project_log_starts_its_transition_with_its_first_line_already_drawn()
    {
        var view = Realized(width: 800, height: 400);

        view.PlayCascade(ConsoleDocument.Text([.. Enumerable.Range(0, 1000).Select(i => $"page{i}")]));

        Assert.Equal(0, view.Editor.VerticalOffset);
        var textView = view.Editor.TextArea.TextView;
        Assert.True(textView.VisualLinesValid, "görsel satırlar geçiş anında kurulmamış — geçiş boş/bayat bir kareyle başlar");
        Assert.Equal("page0", view.Document.GetText(textView.VisualLines[0].FirstDocumentLine));
        Assert.True(view.IsTransitioning); // geçiş oynuyor
    }

    /// <summary>
    /// <b>Dönüşte kaydırma animasyonu YOKTUR.</b> Panel geçişten önce zaten son satırdadır; hareket eden tek
    /// şey bloğun kaymasıdır. İki animasyonu üst üste bindirmek (geçiş + kaydırma) kullanıcı kararıyla reddedildi:
    /// dönüş bir yer değiştirme değil, bir mod değişimidir.
    /// </summary>
    [StaFact]
    public void Returning_to_the_narrative_never_animates_the_scroll()
    {
        var view = Realized(width: 800, height: 400);

        view.ShowRunDocument(LongNarrative());
        double landedAt = view.Editor.VerticalOffset;

        Assert.False(
            ScrollAnimation.IsAnimating(view.Editor),
            "dönüşte kaydırma animasyonu kuruldu — geçiş tek hareket olmalı");

        DispatcherPump.PumpUntil(() => !view.IsTransitioning, TimeSpan.FromSeconds(3));
        Assert.Equal(landedAt, view.Editor.VerticalOffset); // geçiş boyunca panel HİÇ oynamaz
    }

    /// <summary>Reduced-motion: hiç oynatılmaz, içerik doğrudan son hâlinde görünür (motion sözleşmesi).</summary>
    [StaFact]
    public void Reduced_motion_shows_the_content_without_any_transition()
    {
        var view = new ConsoleView { AnimationsEnabledProvider = () => false };
        DsResources.Realize(DsResources.NewHost(), view);
        view.Measure(new Size(200, 100));
        view.Arrange(new Rect(0, 0, 200, 100));
        view.UpdateLayout();

        view.PlayCascade("first line\n");

        Assert.False(view.IsTransitioning);
        Assert.Equal(1.0, view.TiltHost.Opacity);
        Assert.Equal(0.0, SlideOf(view).Y, precision: 3);
    }
}
