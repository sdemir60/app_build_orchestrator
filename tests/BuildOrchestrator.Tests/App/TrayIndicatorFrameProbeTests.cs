using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using BuildOrchestrator.App.Controls;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [tray indicator/3. tur teşhis] ÖLÇÜM — bir pin DEĞİL, bir SONDA. Kullanıcı çıkış evresinde şeritlerin
/// "kesilerek" kaybolduğunu bildirdi; şevron aynı anda temiz çıkıyor. İki aday vardı ve gözle ayırt
/// edilemezlerdi: (a) şeritler bir KIRPMA sınırında yeniyor, (b) yalnız 340 ms'lik solma küçük ölçekte
/// algılanmıyor.
///
/// <para>Sonda ikisini ayırır: storyboard belirli çıkış anlarına <c>Seek</c> edilir, önce transform
/// DEĞERLERİ okunur (parçalar gerçekten nereye gitti), sonra kare bir <see cref="RenderTargetBitmap"/>'e
/// çizilip GERÇEKTEN boyanan piksel aralıkları ölçülür. Ölçülen sağ uç beklenen geometrinin sağ ucundan
/// KISA ise parça kırpılıyordur ve kırpma sınırı okunan sayıdadır; eşitse kusur kırpma değildir.</para>
///
/// <para>Varsayılan koşuda ÇALIŞMAZ: <c>BO_PROBE_TRAY=1</c> yoksa <c>Skip</c>. Gerekçe
/// <see cref="TrayOverlayMeasurementTests"/> ile aynı (<c>Category!=Acceptance</c> filtresi diğer
/// kategorileri dışlamaz) — üstelik bu sonda yavaştır ve çıktısı sayı okumaktır, iddia değil.</para>
/// </summary>
[Trait("Category", "Measurement")]
[Collection("Console UI (serial)")]
public sealed class TrayIndicatorFrameProbeTests(ITestOutputHelper output)
{
    /// <summary>Piksel başına birim: 1 sahne birimi = <c>Zoom</c> piksel. Ölçüm çözünürlüğü 1/4 birim.</summary>
    private const int Zoom = 4;

    /// <summary>Bandın iç koordinattaki sol kenarı — piksel↔iç koordinat dönüşümünün tek yeri.</summary>
    private const double BandLeftInner = -30;

    /// <summary>Şeritlerin kaynak geometrisi (BrandGeometry ile aynı değerler; sonda bunları BEKLENEN olarak
    /// kullanır, kırpma varsa ölçülen bundan kısa çıkar).</summary>
    private static readonly (string Transform, string Label, double Left, double Right, double CenterY)[] Strips =
    [
        ("TopDarkShift", "top dark", 73.5, 108.5, 93.5),
        ("AmberShift",   "amber   ", 119.5, 170.5, 93.5),
        ("MidDarkShift", "mid dark", 51.5, 90.5, 141.5),
        ("WhiteShift",   "white   ", 102.5, 162.5, 142.0),
        ("SilverShift",  "silver  ", 65.5, 122.5, 188.5),
    ];

    [SkippableFact]
    public void Where_does_each_piece_actually_land_during_the_exit()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("BO_PROBE_TRAY") == "1",
            "Diagnostic probe (renders frames offscreen) — opt in with BO_PROBE_TRAY=1.");

        foreach (string line in StaThread.RunAsync(Probe, "tray-frame-probe").GetAwaiter().GetResult())
            output.WriteLine(line);
    }

    private static List<string> Probe()
    {
        var log = new List<string>();
        var host = DsResources.NewHost();
        var indicator = new TrayBuildIndicator();
        var window = DsResources.Realize(
            host, indicator, TrayBuildIndicator.StageWidth, TrayBuildIndicator.StageHeight);
        try
        {
            indicator.BeginLoop();

            string? outDir = Environment.GetEnvironmentVariable("BO_PROBE_OUT");
            if (outDir is { Length: > 0 }) Directory.CreateDirectory(outDir);

            // Giriş (silme sürerken), duruş karesi ve çıkış evresinin tamamı (2.100 → 3.000).
            foreach (double at in new[] { 0.350, 0.550, 0.800, 1.700, 2.200, 2.350, 2.500, 2.650, 2.800, 2.950 })
            {
                indicator.Loop.SeekAlignedToLastTick(
                    indicator, TimeSpan.FromSeconds(at), TimeSeekOrigin.BeginTime);
                DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(50));

                // Sayılar SEEK edilen zamana değil, o an okunan transform DEĞERLERİNE göre yorumlanır:
                // seek + pompalama arada birkaç yüz ms kayabiliyor, ölçüm ise değerlerle aynı andan alınıyor.
                log.Add(string.Format(CultureInfo.InvariantCulture,
                    "=== t≈{0:0.000}s · sweep.X={1:0.0} (maske sağ kenarı {2:0.0}) · chevron.X={3:0.0}",
                    at, indicator.SweepShiftTransform.X, 158.5 + indicator.SweepShiftTransform.X,
                    indicator.ChevronShiftTransform.X));

                // WPF'in kırpılan katman için HESAPLADIĞI sınırlar: kirli bölge (yeniden boyanacak alan) bunlardan
                // türer. Şeritler bu sınırın dışına kayıyorsa canlı pencerede o bölge boyanmaz → dik kesik.
                var clipped = (UIElement)VisualTreeHelper.GetChild(indicator.InnerCanvas, 0);
                var layer = (UIElement)VisualTreeHelper.GetChild(clipped, 0);
                log.Add(string.Format(CultureInfo.InvariantCulture,
                    "  clip.Bounds={0} · clipped.DescendantBounds={1} · layer.DescendantBounds={2}",
                    clipped.Clip is { } clip ? Fmt(clip.Bounds) : "(maske yok)",
                    Fmt(VisualTreeHelper.GetDescendantBounds(clipped)),
                    Fmt(VisualTreeHelper.GetDescendantBounds(layer))));

                // Kare GÖZLE bakmak için diske yazılır — kullanıcının gördüğü kompozisyon (şevron dahil).
                if (outDir is { Length: > 0 })
                    Save(Render(indicator), Path.Combine(outDir, FormattableString.Invariant($"frame-{at:0.000}.png")));

                // Ölçüm şevron GİZLİYKEN yapılır: şevron aynı satırlarda duruyor ve amber şeridin sağ ucuyla
                // üst üste biniyor; aranan şey şeritlerin KENDİ sağ ucudur.
                indicator.ChevronFigure.Visibility = Visibility.Collapsed;
                var frame = Render(indicator);
                if (outDir is { Length: > 0 })
                    Save(frame, Path.Combine(outDir, FormattableString.Invariant($"strips-{at:0.000}.png")));
                indicator.ChevronFigure.Visibility = Visibility.Visible;

                foreach (var strip in Strips) log.Add(Measure(indicator, frame, strip));
                log.Add("");
            }
        }
        finally
        {
            window.Close();
        }
        return log;
    }

    /// <summary>
    /// Kareyi <c>Zoom</c> katı çözünürlükte çizer. Çizilen görsel <b>Stage tuvalidir</b>, kontrolün kendisi
    /// DEĞİL: kontrolü çizmek araya <c>Viewbox</c>'ın ölçeğini sokar (pencerenin istemci alanı bandın tam
    /// ölçüsü olmadığı için 1 değildir) ve piksel↔sahne dönüşümü bozulur. Tuvalin kendi koordinat uzayı
    /// bandın ta kendisidir: piksel/Zoom = band birimi, iç x = band x − 30.
    /// </summary>
    private static (byte[] Pixels, int Stride, int Width) Render(TrayBuildIndicator indicator)
    {
        int w = (int)(TrayBuildIndicator.StageWidth * Zoom);
        int h = (int)(TrayBuildIndicator.StageHeight * Zoom);
        var bitmap = new RenderTargetBitmap(w, h, 96.0 * Zoom, 96.0 * Zoom, PixelFormats.Pbgra32);
        bitmap.Render(indicator.StageCanvas);

        int stride = w * 4;
        var pixels = new byte[stride * h];
        bitmap.CopyPixels(pixels, stride, 0);
        return (pixels, stride, w);
    }

    /// <summary>
    /// Şeridin merkez satırında, beklenen aralığın biraz dışına taşan bir pencerede alfa PROFİLİ çıkarır.
    ///
    /// <para>Profil iki kusuru ayırır. Şerit SOLUYORSA profil baştan sona alçalır ama beklenen genişliği
    /// korur. Şerit KIRPILIYORSA profil bir noktada 255'ten 0'a düşer ve o nokta kırpma sınırıdır.</para>
    /// </summary>
    private static string Measure(
        TrayBuildIndicator indicator,
        (byte[] Pixels, int Stride, int Width) frame,
        (string Transform, string Label, double Left, double Right, double CenterY) strip)
    {
        double shift = ((TranslateTransform)indicator.FindName(strip.Transform)).X;
        double left = strip.Left + shift;
        double right = strip.Right + shift;

        int row = (int)((strip.CenterY - 76) * Zoom);   // iç y → band y → piksel satırı
        var samples = new List<string>();
        double step = (right - left) / 10.0;
        for (double x = left - step; x <= right + step + 0.001; x += step)
            samples.Add(Alpha(frame, row, x).ToString(CultureInfo.InvariantCulture));

        return string.Format(CultureInfo.InvariantCulture,
            "  {0} X={1,6:0.0} · [{2:0.0}..{3:0.0}] alfa: {4}",
            strip.Label, shift, left, right, string.Join(" ", samples));
    }

    /// <summary>Kareyi koyu bir zemin üzerine yazar — masaüstünde göründüğü gibi bakılabilsin diye
    /// (şeffaf PNG'de açık renkli şeritler beyaz görüntüleyicide kaybolur).</summary>
    private static void Save((byte[] Pixels, int Stride, int Width) frame, string path)
    {
        int height = frame.Pixels.Length / frame.Stride;
        var backdrop = new byte[frame.Pixels.Length];
        for (int i = 0; i < frame.Pixels.Length; i += 4)
        {
            double a = frame.Pixels[i + 3] / 255.0;
            for (int c = 0; c < 3; c++) backdrop[i + c] = (byte)(frame.Pixels[i + c] + 32 * (1 - a));
            backdrop[i + 3] = 255;
        }

        var bitmap = BitmapSource.Create(
            frame.Width, height, 96.0 * Zoom, 96.0 * Zoom, PixelFormats.Pbgra32, null, backdrop, frame.Stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string Fmt(Rect r) => r.IsEmpty
        ? "(boş)"
        : string.Format(CultureInfo.InvariantCulture, "x[{0:0.0}..{1:0.0}]", r.Left, r.Right);

    /// <summary>İç koordinattaki bir noktanın alfası (0-255); band dışı ise 0.</summary>
    private static byte Alpha((byte[] Pixels, int Stride, int Width) frame, int row, double innerX)
    {
        int px = (int)((innerX - BandLeftInner) * Zoom);
        if (px < 0 || px >= frame.Width || row < 0) return 0;
        int offset = row * frame.Stride + px * 4 + 3;
        return offset >= 0 && offset < frame.Pixels.Length ? frame.Pixels[offset] : (byte)0;
    }
}
