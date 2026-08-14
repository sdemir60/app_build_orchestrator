using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using BuildOrchestrator.App.Console;

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
    /// <summary>Gerçek bir geçiş için panelin ÖLÇÜLMÜŞ olması şart (doku ondan alınır).</summary>
    private static ConsoleView Realized()
    {
        var view = new ConsoleView { AnimationsEnabledProvider = () => true };
        DsResources.Realize(DsResources.NewHost(), view);
        view.Measure(new Size(800, 400));
        view.Arrange(new Rect(0, 0, 800, 400));
        view.UpdateLayout();
        return view;
    }

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

    [StaFact]
    public void The_block_opens_as_a_real_perspective_hinge_at_its_bottom_edge()
    {
        var view = Realized();

        view.PlayCascade(["first line"], buildInProgress: false);

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
        Assert.NotSame(view.TiltHost, VisualTreeHelper.GetParent(view.BuildProgressOverlay));
    }

    /// <summary>Geçiş bitince 3B katman çekilir ve gerçek editör tam opak geri gelir (metin yeniden keskin).</summary>
    [StaFact]
    public void The_transition_hands_the_panel_back_to_the_real_editor()
    {
        var view = Realized();
        view.PlayCascade(["first line"], buildInProgress: false);
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

    /// <summary>Reduced-motion: hiç oynatılmaz, içerik doğrudan son hâlinde görünür (motion sözleşmesi).</summary>
    [StaFact]
    public void Reduced_motion_shows_the_content_without_any_transition()
    {
        var view = new ConsoleView { AnimationsEnabledProvider = () => false };
        DsResources.Realize(DsResources.NewHost(), view);
        view.Measure(new Size(800, 400));
        view.Arrange(new Rect(0, 0, 800, 400));
        view.UpdateLayout();

        view.PlayCascade(["first line"], buildInProgress: false);

        Assert.Equal(Visibility.Collapsed, view.Tilt3D.Visibility);
        Assert.Equal(1.0, view.TiltHost.Opacity);
    }
}
