using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// DS'in 120 ms'lik renk geçişleri koyu zemin üzerinde ORTASINDA PARLAMAZ.
///
/// <para><b>Kusur:</b> <c>Colors.Transparent</c> <c>#00FFFFFF</c>'tir — alfası sıfır ama RGB'si BEYAZ. WPF renk
/// kanallarını premultiply ETMEDEN interpole ettiğinden alfa 0'dan yükselirken RGB de beyazdan hedefe iner:
/// <c>#00FFFFFF → #FF1a1a1e</c> geçişinin ortası ≈ <c>#80BCBCBD</c>, koyu zemine bindiğinde ≈ <c>#656565</c>.
/// Kullanıcının gördüğü "satırdan satıra geçerken gelip giden açık renk" tam olarak budur; hover hedefi zeminle
/// aynı renk olduğunda (bakım kutusundaki Ghost butonlar) geriye YALNIZ bu çakma kalır.</para>
///
/// <para><b>Ölçüm biçimi:</b> zaman çizelgesi bir saat olmadan, keyframe'lerin KENDİ
/// <see cref="ColorKeyFrame.InterpolateValue"/>'sıyla örneklenir — WPF'in ara kareyi hesaplarken kullandığı
/// fonksiyonun aynısı, ama deterministik (D8: testte gerçek zaman beklenmez). Örnek renk uygulamanın taban
/// zeminine bindirilir ve algılanan parlaklığı ölçülür.</para>
/// </summary>
public sealed class ColorTransitionFlashTests
{
    /// <summary>Geçiş süresi/eğrisi bu testin konusu değil — ara karelerin RENGİ konusu. Token'ların kendi
    /// doğruluğunu <c>MotionResourcesTests</c> pinler.</summary>
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(120);

    /// <summary>Dondurulur, çünkü <see cref="KeySpline"/> bir <see cref="System.Windows.Freezable"/>'dır ve
    /// donmamış hâli KURULDUĞU iş parçacığına aittir — StaFact'ler ayrı thread'lerde koşar, paylaşılan statik
    /// örnek ikincisinde <c>VerifyAccess</c>'ten patlardı.</summary>
    private static readonly KeySpline EaseStandard = FrozenSpline(0.4, 0, 0.2, 1);

    private static KeySpline FrozenSpline(double x1, double y1, double x2, double y2)
    {
        var spline = new KeySpline(x1, y1, x2, y2);
        spline.Freeze();
        return spline;
    }

    /// <summary>Bir bayt kanalı kadar tolerans — yuvarlama gürültüsü çakma sayılmaz.</summary>
    private const double LuminanceTolerance = 1.0;

    private const int SampleCount = 20;

    [StaFact]
    public void Hover_in_from_transparent_never_brightens_past_either_endpoint()
    {
        var host = DsResources.NewHost();
        Color backdrop = DsResources.TokenColor(host, "Brush.SurfaceBase");
        Color hover = DsResources.TokenColor(host, "Brush.SurfaceHover");

        AssertNoFlash(Colors.Transparent, hover, backdrop);
    }

    [StaFact]
    public void Hover_out_to_transparent_never_brightens_past_either_endpoint()
    {
        var host = DsResources.NewHost();
        Color backdrop = DsResources.TokenColor(host, "Brush.SurfaceBase");
        Color hover = DsResources.TokenColor(host, "Brush.SurfaceHover");

        AssertNoFlash(hover, Colors.Transparent, backdrop);
    }

    /// <summary>Ghost buton (bakım kutusu: Clean · Optimize · Resolve cycles) hover'ı zemine EŞİT bir renge
    /// gider — doğru davranış "hiçbir şey olmaması"dır, geçişin ortasında bir çakma değil.</summary>
    [StaFact]
    public void Ghost_button_hover_onto_a_same_colored_surface_stays_invisible()
    {
        var host = DsResources.NewHost();
        Color surface = DsResources.TokenColor(host, "Brush.SurfaceRaised");

        // Zemin de hedefin kendisi: her ara kare zeminle aynı görünmeli (fark = 0, tolerans kadar).
        foreach ((double progress, Color sample) in Samples(Colors.Transparent, surface))
        {
            double delta = Math.Abs(LuminanceOver(sample, surface) - LuminanceOver(surface, surface));
            Assert.True(delta <= LuminanceTolerance,
                $"progress {progress:0.00}: hover transition is visible ({Describe(sample)}) on an identically " +
                $"colored surface — luminance delta {delta:0.0}");
        }
    }

    /// <summary>Çakmayı yok etmek uçları KAYDIRARAK yapılmaz: geçiş görünür olarak hâlâ tam kaynaktan tam hedefe
    /// gitmelidir. Sıfır-alfalı ucun RGB'si değişebilir (görünmez), ama bindirilmiş rengi değişemez.</summary>
    [StaFact]
    public void Endpoints_still_render_exactly_as_declared()
    {
        var host = DsResources.NewHost();
        Color backdrop = DsResources.TokenColor(host, "Brush.SurfaceBase");
        Color hover = DsResources.TokenColor(host, "Brush.SurfaceHover");

        var animation = MotionTokens.SplineColorTo(Colors.Transparent, hover, Duration, EaseStandard);

        Assert.Equal(LuminanceOver(Colors.Transparent, backdrop), LuminanceOver(SampleAt(animation, 0.0), backdrop), 1);
        Assert.Equal(LuminanceOver(hover, backdrop), LuminanceOver(SampleAt(animation, 1.0), backdrop), 1);
    }

    /// <summary>[kopya YASAK] Renk zaman çizelgesinin TEK kurucusu <see cref="MotionTokens.SplineColorTo"/>'dur —
    /// uçları premultiply-güvenli hâle getiren düzeltme yalnız orada yaşar. Bir tüketici kendi
    /// <c>SplineColorKeyFrame</c>'ini kurarsa düzeltmeyi ATLAR ve çakma o yüzeyde geri gelir.</summary>
    [Fact]
    public void No_app_file_builds_its_own_color_keyframe_outside_the_shared_builder()
        => Assert.Empty(SourceGuard.ScanApp("*.cs", ColorKeyFrameLiteral, skipCommentLines: true,
            allowedFiles: [Path.Combine("Controls", "MotionTokens.cs")]));

    private static readonly Regex ColorKeyFrameLiteral = new(
        @"new\s+(?:Spline|Linear|Discrete|Easing)ColorKeyFrame\b", RegexOptions.Compiled);

    // ---------------------------------------------------------------- ölçüm

    private static void AssertNoFlash(Color from, Color to, Color backdrop)
    {
        double ceiling = Math.Max(LuminanceOver(from, backdrop), LuminanceOver(to, backdrop)) + LuminanceTolerance;

        foreach ((double progress, Color sample) in Samples(from, to))
        {
            double luminance = LuminanceOver(sample, backdrop);
            Assert.True(luminance <= ceiling,
                $"progress {progress:0.00}: {Describe(sample)} composites to luminance {luminance:0.0}, " +
                $"brighter than both endpoints (ceiling {ceiling:0.0}) — the transition flashes.");
        }
    }

    private static IEnumerable<(double Progress, Color Sample)> Samples(Color from, Color to)
    {
        var animation = MotionTokens.SplineColorTo(from, to, Duration, EaseStandard);
        for (int i = 1; i < SampleCount; i++)
        {
            double progress = (double)i / SampleCount;
            yield return (progress, SampleAt(animation, progress));
        }
    }

    /// <summary>Zaman çizelgesini <paramref name="progress"/> (0..1) noktasında değerlendirir: keyframe'ler
    /// zamanına göre sıralanır, örnek hangi aralığa düşüyorsa O keyframe'in kendi
    /// <see cref="ColorKeyFrame.InterpolateValue"/>'si bir öncekinin değerinden çağrılır — WPF'in yaptığının aynısı.</summary>
    private static Color SampleAt(ColorAnimationUsingKeyFrames animation, double progress)
    {
        var frames = animation.KeyFrames.Cast<ColorKeyFrame>().OrderBy(f => f.KeyTime.TimeSpan).ToList();
        long totalTicks = frames[^1].KeyTime.TimeSpan.Ticks;
        long atTicks = (long)(totalTicks * progress);

        Color previous = frames[0].Value;
        long previousTicks = frames[0].KeyTime.TimeSpan.Ticks;
        for (int i = 1; i < frames.Count; i++)
        {
            long endTicks = frames[i].KeyTime.TimeSpan.Ticks;
            if (atTicks <= endTicks)
            {
                long span = endTicks - previousTicks;
                double segment = span == 0 ? 1.0 : (double)(atTicks - previousTicks) / span;
                return frames[i].InterpolateValue(previous, segment);
            }
            previous = frames[i].Value;
            previousTicks = endTicks;
        }
        return previous;
    }

    /// <summary>Yarı saydam bir rengin ARDINDA opak bir zemin varken ekrana çıkan parlaklık (0..255). Katsayılar
    /// göz duyarlılığı ağırlıklarıdır (Rec. 709) — ölçüt "insan bunu parlama olarak görür mü"dür.</summary>
    private static double LuminanceOver(Color color, Color backdrop)
    {
        double alpha = color.A / 255.0;
        double red = alpha * color.R + (1 - alpha) * backdrop.R;
        double green = alpha * color.G + (1 - alpha) * backdrop.G;
        double blue = alpha * color.B + (1 - alpha) * backdrop.B;
        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    private static string Describe(Color color) =>
        $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
