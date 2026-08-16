using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.7.0 §2.5 · animasyon spec §2] Panel geçişi (proje logu ↔ <c>← Back</c>) TEK PARÇA "tilt in"dir:
/// log bloğu alt kenarından menteşeli biçimde, 14 px aşağıdan yukarı oturur ve aynı anda görünür olur.
///
/// <para><b>[DEĞİŞEN KURAL] Gerçek perspektif.</b> Eski iddia: geçiş bir 2B <c>ScaleY 0.965 → 1</c> +
/// <c>TranslateY 14 → 0</c>'dır (spec §2.4'ün "görsel olarak yeterli" yaklaşımı). Değişme gerekçesi
/// (kullanıcı, sahada): jest "kâğıdın masaya oturması" değil salt "aşağıdan kayma" gibi okunuyor, yataydaki
/// hareket eksik. Doğrusu şu: prototipin <c>perspective(900px) rotateX(7deg)</c>'i bir TRAPEZ üretir — üst
/// kenar geriye giderken daralır, alt kenar öne gelirken genişler — ve WPF'in 2D dönüşümleri AFİN olduğu
/// için bunu üretemezler. Bu yüzden spec §2.4'ün ikinci yolu (gerçek 3B) uygulanır.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ConsoleTiltInTests
{
    /// <summary>Gerçek bir geçiş için panelin ÖLÇÜLMÜŞ olması şart (doku ondan alınır).
    /// <para>Varsayılan küçük panel yeter: geçiş dokusu gerçek bir bitmap render'ıdır ve süit paralel koşarken
    /// gereksiz piksel işi, zamanlamaya duyarlı komşu testleri bütçelerinin üstüne itiyor. Kaydırma
    /// geometrisine bakan testler ölçüyü kendileri büyütür — orada viewport'u aşan bir anlatı şarttır.</para></summary>
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
    private static string LongNarrative() =>
        string.Concat(Enumerable.Range(0, 300).Select(i => $"line{i}\n"));

    private static (AxisAngleRotation3D Rotation, TranslateTransform3D Translate) SceneOf(ConsoleView view)
    {
        var visual = Assert.IsType<ModelVisual3D>(Assert.Single(view.Tilt3D.Children));
        var group = Assert.IsType<Model3DGroup>(visual.Content);
        var model = group.Children.OfType<GeometryModel3D>().Single();
        var transforms = Assert.IsType<Transform3DGroup>(model.Transform);
        var translate = Assert.IsType<TranslateTransform3D>(transforms.Children[0]);
        var rotate = Assert.IsType<RotateTransform3D>(transforms.Children[1]);
        return (Assert.IsType<AxisAngleRotation3D>(rotate.Rotation), translate);
    }

    /// <summary>
    /// <b>Dönüş geçişi açılışın TAM AYNASIDIR.</b> Açılışta blok 14 px AŞAĞIDAN yükselir ve menteşe ALT
    /// kenardadır; dönüşte 14 px YUKARIDAN oturur ve menteşe ÜST kenardadır — "kapak geri kapanıyor". Aynı
    /// süre, aynı eğri, aynı açı; değişen yalnız yön ve menteşenin kenarı (kullanıcı kararı).
    /// <para>Menteşe kenarı <c>RotateTransform3D.CenterY</c>'den okunur: WPF'te +Y yukarıdır, yani düzlemin
    /// yarı yüksekliği kadar EKSİ değer alt kenar, ARTI değer üst kenardır.</para></summary>
    [StaFact]
    public void The_return_transition_is_the_mirror_of_the_opening_one()
    {
        const double height = 100;
        var view = Realized(height: height);

        view.ShowRunDocument("first\n");

        var (rotation, translate) = SceneOf(view);
        Assert.Equal(new Vector3D(1, 0, 0), rotation.Axis);    // menteşe yine X ekseni
        Assert.Equal(7.0, rotation.Angle, precision: 3);       // açılışın TERSİ işaret (alt kenar geride)
        Assert.Equal(14.0, translate.OffsetY, precision: 3);   // ve 14px YUKARIDA
        Assert.Equal(view.TiltHost.ActualHeight / 2, HingeCentreY(view), precision: 3); // menteşe ÜST kenarda
    }

    private static double HingeCentreY(ConsoleView view)
    {
        var visual = Assert.IsType<ModelVisual3D>(Assert.Single(view.Tilt3D.Children));
        var group = Assert.IsType<Model3DGroup>(visual.Content);
        var model = group.Children.OfType<GeometryModel3D>().Single();
        var transforms = Assert.IsType<Transform3DGroup>(model.Transform);
        return Assert.IsType<RotateTransform3D>(transforms.Children[1]).CenterY;
    }

    [StaFact]
    public void The_block_opens_as_a_real_perspective_hinge_at_its_bottom_edge()
    {
        var view = Realized();

        view.PlayCascade(["first line"]);

        Assert.Equal(-view.TiltHost.ActualHeight / 2, HingeCentreY(view), precision: 3); // menteşe ALT kenarda

        Assert.Equal(Visibility.Visible, view.Tilt3D.Visibility);
        var camera = Assert.IsType<PerspectiveCamera>(view.Tilt3D.Camera);
        Assert.Equal(900, camera.Position.Z);                 // spec §2.2: perspective(900px)

        var (rotation, translate) = SceneOf(view);
        Assert.Equal(new Vector3D(1, 0, 0), rotation.Axis);   // menteşe X ekseni
        Assert.Equal(-7.0, rotation.Angle, precision: 3);     // üst kenar GERİYE yatık (rotateX 7°)
        Assert.Equal(-14.0, translate.OffsetY, precision: 3); // ve 14px AŞAĞIDA (CSS +Y aşağı, WPF yukarı)
        Assert.Equal(0.0, view.Tilt3D.Opacity);               // görünmezden açılır
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

    /// <summary>Geçiş bitince 3B katman çekilir ve gerçek editör tam opak geri gelir (metin yeniden keskin).</summary>
    [StaFact]
    public void The_transition_hands_the_panel_back_to_the_real_editor()
    {
        var view = Realized();
        view.PlayCascade(["first line"]);
        Assert.Equal(0.0, view.TiltHost.Opacity); // ön-koşul: geçiş sürerken gerçek blok saklı

        DispatcherPump.PumpUntil(() => view.Tilt3D.Visibility == Visibility.Collapsed, TimeSpan.FromSeconds(3));

        Assert.Equal(Visibility.Collapsed, view.Tilt3D.Visibility);
        Assert.Empty(view.Tilt3D.Children); // doku bırakıldı — geçiş başına bir görüntü tutulmaz
        Assert.Equal(1.0, view.TiltHost.Opacity);
    }

    /// <summary>
    /// [animasyon spec §2.1/§3] Geçiş YALNIZ log bloğu remount edildiğinde oynar. Canlı bir satır eklemek onu
    /// YENİDEN TETİKLEMEZ — konsolun canlı satırları animasyonsuzdur.
    /// </summary>
    [StaFact]
    public void Live_output_never_replays_the_transition()
    {
        var view = Realized();
        view.ShowRunDocument("first\n");
        DispatcherPump.PumpUntil(() => view.Tilt3D.Visibility == Visibility.Collapsed, TimeSpan.FromSeconds(3));

        view.AppendBatch("second\n");
        view.AppendNarrativeBatch("third\n");

        Assert.Equal(Visibility.Collapsed, view.Tilt3D.Visibility);
        Assert.Equal(1.0, view.TiltHost.Opacity);
    }

    /// <summary>
    /// <b>Anlatıya dönüş, anlatı ZATEN SON SATIRINDA ÇİZİLMİŞKEN başlar.</b>
    ///
    /// <para><b>Ölçülen kusur (kullanıcı, sahada):</b> <c>← Back</c>'te geçiş, metnin BAŞINI gösteriyor ve
    /// biter bitmez panel bir karede dibe atlıyordu. Sebebi sıradır: <see cref="ConsoleView"/> belgeyi
    /// değiştirip dibe pinliyor, ama pin'den SONRA bir yerleşim turu ZORLAMIYORDU. <c>ScrollToEnd</c> yalnız bir
    /// İSTEKTİR — <c>ScrollViewer</c> onu bir sonraki ölçüm turunda <c>TextView</c>'e iletir. Geçişin dokusu
    /// (<c>BuildTiltScene</c> → <c>RenderTargetBitmap</c>) o turdan ÖNCE alındığı için hâlâ eski kareyi, yani
    /// metnin başını taşıyordu.</para>
    ///
    /// <para>Test bunu "offset doğru mu" diye SORMAZ — offset zaten mantıksal olarak doğruydu, çizilen kare
    /// yanlıştı. Bu yüzden asıl iddia şudur: dokunun alındığı anda belgenin SON SATIRI gerçekten görünür
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

        // Dokunun alındığı KARE: belgenin son satırı gerçekten çizilmiş olmalı.
        var textView = view.Editor.TextArea.TextView;
        Assert.True(textView.VisualLinesValid, "görsel satırlar geçiş anında kurulmamış — doku boş/bayat bir kare taşır");
        int lastLine = view.Document.LineCount;
        Assert.Contains(textView.VisualLines,
            v => v.FirstDocumentLine.LineNumber <= lastLine && v.LastDocumentLine.LineNumber >= lastLine);

        // Ve imleç son satırın yanındadır — yani panelin alt yarısında, metnin üstünde değil.
        Assert.Equal(Visibility.Visible, view.ActiveLineOverlay.Visibility);
        Assert.InRange(view.ActiveLineOverlay.Margin.Top, view.TiltHost.ActualHeight / 2, view.TiltHost.ActualHeight);
    }

    /// <summary>
    /// <b>Dönüşte kaydırma animasyonu YOKTUR.</b> Panel geçişten önce zaten son satırdadır; hareket eden tek
    /// şey menteşedir. İki animasyonu üst üste bindirmek (geçiş + kaydırma) kullanıcı kararıyla reddedildi:
    /// dönüş bir yer değiştirme değil, bir mod değişimidir.
    /// </summary>
    [StaFact]
    public void Returning_to_the_narrative_never_animates_the_scroll()
    {
        var view = Realized(width: 800, height: 400);

        view.ShowRunDocument(LongNarrative());
        double landedAt = view.Editor.VerticalOffset;

        Assert.False(
            DependencyPropertyHelper.GetValueSource(view.Editor, ScrollAnimator.VerticalOffsetProperty).IsAnimated,
            "dönüşte kaydırma animasyonu kuruldu — geçiş tek hareket olmalı");

        DispatcherPump.PumpUntil(() => view.Tilt3D.Visibility == Visibility.Collapsed, TimeSpan.FromSeconds(3));
        Assert.Equal(landedAt, view.Editor.VerticalOffset); // geçiş boyunca panel HİÇ oynamaz
    }

    /// <summary>Reduced-motion: hiç oynatılmaz, içerik doğrudan son hâlinde görünür (motion sözleşmesi).</summary>
    [StaFact]
    public void Reduced_motion_shows_the_content_without_any_transition()
    {
        var view = new ConsoleView { AnimationsEnabledProvider = () => false };
        DsResources.Realize(DsResources.NewHost(), view);
        // Küçük bir panel yeter: geçiş dokusu gerçek bir bitmap render'ıdır ve süit paralel koşarken
        // gereksiz piksel işi, zamanlamaya duyarlı komşu testleri bütçelerinin üstüne itiyor.
        view.Measure(new Size(200, 100));
        view.Arrange(new Rect(0, 0, 200, 100));
        view.UpdateLayout();

        view.PlayCascade(["first line"]);

        Assert.Equal(Visibility.Collapsed, view.Tilt3D.Visibility);
        Assert.Equal(1.0, view.TiltHost.Opacity);
    }
}
