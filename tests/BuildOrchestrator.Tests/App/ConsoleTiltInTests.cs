using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.7.0 §2.5 · animasyon spec §2] Panel geçişi (proje logu ↔ <c>← Back</c>) TEK PARÇA "tilt in"dir:
/// LOG BLOĞU alt kenarından menteşeli biçimde, 14 px AŞAĞIDAN yukarı oturur ve aynı anda görünür olur.
///
/// <para>Otorite prototipin keyframe'i ve onun WPF eşlemesidir (spec §2.2/§2.4):
/// <c>from { opacity:0; transform: perspective(900px) rotateX(7deg) translateY(14px) }</c> → <c>to</c>;
/// WPF'te <c>RenderTransformOrigin 0.5,1</c> + <c>ScaleY 0.965 → 1</c> + <c>TranslateY 14 → 0</c> +
/// <c>Opacity 0 → 1</c>, 340 ms, <c>KeySpline 0.22,1 0.36,1</c>.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ConsoleTiltInTests
{
    private static ConsoleView Realized()
    {
        var view = new ConsoleView { AnimationsEnabledProvider = () => true };
        DsResources.Realize(DsResources.NewHost(), view);
        return view;
    }

    private static (ScaleTransform Scale, TranslateTransform Translate) TransformOf(ConsoleView view)
    {
        var group = Assert.IsType<TransformGroup>(view.TiltHost.RenderTransform);
        return (Assert.IsType<ScaleTransform>(group.Children[0]),
                Assert.IsType<TranslateTransform>(group.Children[1]));
    }

    [StaFact]
    public void The_content_settles_upward_from_below_hinged_at_its_bottom_edge()
    {
        var view = Realized();

        view.PlayCascade(["first line"], buildInProgress: false);

        var (scale, translate) = TransformOf(view);
        Assert.Equal(new Point(0.5, 1.0), view.TiltHost.RenderTransformOrigin); // menteşe ALT kenarda
        Assert.Equal(14.0, translate.Y);                                        // spec §2.2: translateY(14px)
        Assert.Equal(0.965, scale.ScaleY, precision: 3);                        // spec §2.4: rotateX 7°'nin eşlemesi
        Assert.Equal(0.0, view.TiltHost.Opacity);                               // ve görünmezden açılır
    }

    /// <summary>
    /// [DEĞİŞEN KURAL] Prompt imleci geçişe KATILMAZ — yerinde durur, içerik ona doğru oturur.
    ///
    /// <para>Eski iddia: metin ve imleç tek parça hareket etmeli, bu yüzden ikisi de tilt kabının içindedir.
    /// Değişme gerekçesi: prototip animasyon spec'i bunun tersini söylüyor (§1.3 "Prompt satırı — log
    /// bloğunun DIŞINDA, onun altındaki kardeş eleman. Tilt animasyonuna KATILMAZ" ve §4 "bu satır da tilt
    /// bloğunun DIŞINDA"). Oturan şey içeriktir; imleç panelin sabit noktasıdır ve ekranda süreklilik
    /// duygusunu o taşır.</para>
    ///
    /// <para>İmlecin konumu yine de kaymaz: <c>PositionPrompt</c> ölçüsünü tilt kabının KENDİ (dönüşümsüz)
    /// koordinat uzayında alır, yani animasyonun ara değerleri hesaba sızmaz.</para>
    /// </summary>
    [StaFact]
    public void The_prompt_line_stays_out_of_the_transition()
    {
        var view = Realized();

        Assert.Same(view.TiltHost, VisualTreeHelper.GetParent(view.Editor));
        Assert.NotSame(view.TiltHost, VisualTreeHelper.GetParent(view.ActiveLineOverlay));
        Assert.NotSame(view.TiltHost, VisualTreeHelper.GetParent(view.BuildProgressOverlay));
    }

    /// <summary>Geçiş bitince içerik yerine oturur: kayma sıfırlanır, ölçek tama döner, panel tam opak olur.</summary>
    [StaFact]
    public void The_transition_lands_on_its_final_values()
    {
        var view = Realized();
        view.PlayCascade(["first line"], buildInProgress: false);

        DispatcherPump.PumpUntil(() => view.TiltHost.Opacity >= 1.0, TimeSpan.FromSeconds(3));

        var (scale, translate) = TransformOf(view);
        Assert.Equal(1.0, view.TiltHost.Opacity, precision: 2);
        Assert.Equal(0.0, translate.Y, precision: 2);
        Assert.Equal(1.0, scale.ScaleY, precision: 2);
    }

    /// <summary>
    /// [animasyon spec §2.1/§3] Geçiş YALNIZ log bloğu remount edildiğinde oynar. Canlı bir satır eklemek
    /// onu YENİDEN TETİKLEMEZ — konsolun canlı satırları animasyonsuzdur ("kullanıcı gerçek MSBuild çıktısıyla
    /// karşılaştırıyor").
    /// </summary>
    [StaFact]
    public void Live_output_never_replays_the_transition()
    {
        var view = Realized();
        view.ShowRunDocument("first\n");
        DispatcherPump.PumpUntil(() => view.TiltHost.Opacity >= 1.0, TimeSpan.FromSeconds(3));

        view.AppendBatch("second\n");
        view.AppendNarrativeBatch("third\n");

        var (scale, translate) = TransformOf(view);
        Assert.Equal(1.0, view.TiltHost.Opacity, precision: 2); // saydamdan yeniden açılmadı
        Assert.Equal(0.0, translate.Y, precision: 2);
        Assert.Equal(1.0, scale.ScaleY, precision: 2);
    }
}
