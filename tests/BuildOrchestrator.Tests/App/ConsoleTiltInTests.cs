using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.7.0 §2.5] Panel geçişi (proje logu ↔ <c>← Back</c>) TEK PARÇA "tilt in"dir: içerik alt kenardan
/// menteşeli biçimde, 14 px AŞAĞIDAN yukarı oturur ve aynı anda görünür olur.
///
/// <para>Otorite prototipin keyframe'idir (<c>BuildApp.jsx:37</c>):
/// <c>from { opacity:0; transform: perspective(900px) rotateX(7deg) translateY(14px) }</c> →
/// <c>to { opacity:1; … translateY(0) }</c>, <c>transform-origin: 50% 100%</c>, 340 ms ease-out.</para>
///
/// <para><b>Neden bu dosya var:</b> geçişin hiçbir testi yoktu ve iki kusuru birden taşıyordu — hareket TERS
/// yöndeydi (içerik yukarıdan aşağı iniyordu; alt kenar menteşesiyle bu "oturmak" değil "düşmek" gibi
/// okunuyor) ve dönüşüm YALNIZ editöre uygulandığı için prompt imleci yerinde kalıp içerik onun altından
/// kayıyordu.</para>
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
        Assert.True(translate.Y > 0, "içerik AŞAĞIDAN gelmeli (prototip: translateY(14px) → 0)");
        Assert.True(scale.ScaleY < 1.0, "menteşe etkisi: dikey ölçek tamdan KÜÇÜK başlar");
        Assert.Equal(0.0, view.TiltHost.Opacity);                               // ve görünmezden açılır
    }

    /// <summary>Metin ile prompt imleci AYNI kabın içindedir — geçişte birlikte hareket ederler.</summary>
    [StaFact]
    public void The_caret_travels_with_the_text_as_one_piece()
    {
        var view = Realized();

        Assert.Same(view.TiltHost, VisualTreeHelper.GetParent(view.Editor));
        Assert.Same(view.TiltHost, VisualTreeHelper.GetParent(view.ActiveLineOverlay));
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
}
