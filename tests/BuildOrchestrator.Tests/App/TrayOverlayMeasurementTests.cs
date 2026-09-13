using System.Diagnostics;
using System.Globalization;
using System.Windows.Media;
using BuildOrchestrator.App.Views;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [tray indicator/round 2] ÖLÇÜM — varsayılan süitten hariçtir (<c>Category=Measurement</c>), çünkü gerçek
/// overlay'i ekrana çıkarır ve saniyeler boyunca pompalar.
///
/// <para><b>Soru:</b> tepsi göstergesi kendi başına ne kadar tutuyor? Kullanıcı derleme sırasında "ekran da
/// animasyon da donuyor" dedi; katmanlı pencere + gölge efektleri suçlu olabilir, derlemenin kendisi de
/// (kullanıcı profili <c>FullPower</c>: altı paralel MSBuild, CPU tavanı yok). İkisini ayırmanın tek yolu
/// göstergeyi derleme OLMADAN, boş makinede döndürüp kendi kare hızını ve işlemci payını okumaktır.</para>
///
/// <para>İki örnek alınır: statik kare (saat yok) ve döngü. Fark, animasyonun kendi maliyetidir; kare sayısı
/// <see cref="CompositionTarget.Rendering"/>'den (bu thread'de çizilen her kare), işlemci süresi sürecin
/// toplamından okunur. Bu bir pin DEĞİL, bir okuma — sayılar test çıktısına yazılır.</para>
/// </summary>
[Trait("Category", "Measurement")]
[Collection("Console UI (serial)")]
public sealed class TrayOverlayMeasurementTests(ITestOutputHelper output)
{
    private static readonly TimeSpan SampleWindow = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan SettleWindow = TimeSpan.FromMilliseconds(600);

    [StaFact]
    public void The_loop_costs_this_much_on_this_machine()
    {
        var overlay = new TrayBuildOverlayWindow(DsResources.NewScope());
        try
        {
            var idle = Sample(overlay, loop: false);
            var loop = Sample(overlay, loop: true);

            output.WriteLine(Line("static frame", idle));
            output.WriteLine(Line("loop        ", loop));
            output.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "loop minus static: {0:0.0} frames/s, {1:0.0} % of one core",
                loop.FramesPerSecond - idle.FramesPerSecond, loop.CpuPercent - idle.CpuPercent));
        }
        finally
        {
            overlay.Close();
        }
    }

    private static string Line(string label, (double FramesPerSecond, double CpuPercent) s) =>
        string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.0} frames/s, {2:0.0} % of one core",
            label, s.FramesPerSecond, s.CpuPercent);

    private static (double FramesPerSecond, double CpuPercent) Sample(TrayBuildOverlayWindow overlay, bool loop)
    {
        if (loop) overlay.ShowLoop(); else overlay.ShowStatic();
        DispatcherPump.PumpFor(SettleWindow); // yerleşim + ilk kare; ölçüme girmez

        int frames = 0;
        void OnRender(object? sender, EventArgs e) => frames++;
        CompositionTarget.Rendering += OnRender;

        var process = Process.GetCurrentProcess();
        process.Refresh();
        var cpuBefore = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();

        DispatcherPump.PumpFor(SampleWindow);

        clock.Stop();
        process.Refresh();
        var cpu = process.TotalProcessorTime - cpuBefore;
        CompositionTarget.Rendering -= OnRender;

        overlay.HideNow();
        DispatcherPump.PumpFor(SettleWindow);

        return (frames / clock.Elapsed.TotalSeconds, cpu.TotalMilliseconds / clock.Elapsed.TotalMilliseconds * 100.0);
    }
}
