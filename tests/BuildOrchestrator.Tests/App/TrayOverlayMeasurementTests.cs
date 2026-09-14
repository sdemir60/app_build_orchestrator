using System.Diagnostics;
using System.Globalization;
using System.Windows.Media;
using BuildOrchestrator.App.Views;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [tray indicator/round 2] ÖLÇÜM — gerçek overlay'i ekrana çıkarır ve saniyeler boyunca pompalar; bu yüzden
/// varsayılan koşuda ÇALIŞMAMASI GEREKİR ama <c>Category=Measurement</c> etiketi TEK BAŞINA bunu sağlamaz:
/// standart komut <c>--filter "Category!=Acceptance"</c> VSTest'in <c>!=</c> operatörüyle "Acceptance
/// dışındaki HER Category değerini" kabul eder — <c>Measurement</c> de dahil olur. Gerçek kapı ilk satırdaki
/// <c>Skip.IfNot</c>'tur: <c>BO_MEASURE_OVERLAY</c> ortam değişkeni <c>"1"</c> değilse test SKIPPED raporlanır
/// ve pencere hiç açılmaz; değişken kurulduğunda gövde gerçekten koşar ve Topmost bir overlay'i ~17 sn ekranda
/// tutar. Trait yalnız hedefli koşu için kalır (<c>FullyQualifiedName~TrayOverlayMeasurementTests</c>).
///
/// <para><b>Nasıl koşulur:</b> <c>$env:BO_MEASURE_OVERLAY='1'; dotnet test
/// tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj -c Release --filter
/// "FullyQualifiedName~TrayOverlayMeasurementTests"</c>.</para>
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

    [SkippableFact]
    public async Task The_loop_costs_this_much_on_this_machine()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("BO_MEASURE_OVERLAY") == "1",
            "Shows a real Topmost overlay for several seconds — opt in with BO_MEASURE_OVERLAY=1.");

        // [StaFact] DEĞİL: yukarıdaki Skip.IfNot'un attığı SkipException'ı StaFact'in runner'ı tanımaz
        // (Skipped yerine sessizce Failed üretir) — DragReorderTests/AppShutdownTests'teki aynı kısıt. Gövde
        // bu yüzden ortak StaThread.RunAsync ile manuel bir STA thread'de koşar; test metodu (Skip.IfNot
        // dahil) [SkippableFact] altında kalır.
        await StaThread.RunAsync(() =>
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
        }, name: "tray-overlay-measurement-sta");
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
