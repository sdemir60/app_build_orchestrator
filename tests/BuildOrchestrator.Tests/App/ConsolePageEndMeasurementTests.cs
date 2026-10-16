using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using BuildOrchestrator.App.Console;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [B2 fix 2 · madde 4] ÖLÇÜM — proje sayfasının sonuna gitmenin UI thread maliyeti, üç yoldan: editörün kendi Ctrl+End'i
/// (<c>MoveToDocumentEnd</c>: gizli imleci belgenin sonuna taşır ve görünür kılar), konsolun End'i (tuş tablosu: takibin dibe gitme isteği)
/// ve o isteğin kendisi + yerleşim (tuşsuz). Sayfa boyları 1 000 – 100 000 satır. Bu bir pin DEĞİL, bir okuma: sayılar test çıktısına
/// yazılır, eşik yoktur. Karar kuralı (kontrolcü): 10 000 ya da 12 000 satırda Ctrl+End'in UI thread CPU'su 50 ms'yi aşar VE maliyet
/// editörün komut yolundaysa (Ctrl+End ≫ End) Ctrl+Home/Ctrl+End tabloya alınır; aksi hâlde kod değişmez.
///
/// <para><b>[B2 fix 3 · madde 13] Satırlar gerçekçidir</b> (<see cref="MsBuildLines.Warning"/>: ~190 karakterlik MSBuild uyarısı) ve sayfa
/// renklendirilir (konsolun kendi renklendiricisi — ön-koşul olarak doğrulanır). [DEĞİŞEN KURULUM] İlk turda satırlar <c>page{i}</c>'ydi
/// (≤ 11 karakter): editörün satır kurma ve renklendirme maliyeti ölçüme girmiyordu (inceleme M-4).</para>
///
/// <para><b>Neden kapılı:</b> gerçek (ekran dışı) bir pencere açar, 100 000 satıra kadar sayfalar kurar ve yüzlerce tuş basar.
/// <c>Category=Measurement</c> etiketi TEK BAŞINA bunu sağlamaz (<c>Category!=Acceptance</c> filtresi her kategoriyi kabul eder): gerçek
/// kapı ilk satırdaki <c>Skip.IfNot</c>'tur — <c>BO_MEASURE_CTRL_END</c> <c>"1"</c> değilse test SKIPPED raporlanır ve pencere hiç açılmaz.
/// Gövde <see cref="StaThread.RunAsync{T}"/>'te koşar: <c>[StaFact]</c>'in runner'ı Skip'i tanımaz.</para>
///
/// <para><b>Nasıl koşulur:</b> uygulama kapalıyken ve makine sakinken <c>$env:BO_MEASURE_CTRL_END='1'; dotnet test
/// tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~ConsolePageEndMeasurementTests"
/// --logger "console;verbosity=detailed"</c>. Sayılar yalnız ayrıntılı çıktıda görünür.</para>
///
/// <para><b>Ne ölçülür:</b> her boy ve her yol için sayfa YENİDEN açılır (<see cref="RealizedNarrative.OpenPage(string[])"/>); açılıştan sonraki
/// ilk iniş ayrı yazılır (sonun görsel satırları ilk kez kurulur; sürecin ilk okumasında JIT de içindedir), ardından bir ısınma ve yedi örnek.
/// Her örnekten önce görünüm sayfanın tepesine döner (Ctrl+Home — ölçülmez, doğrulanır). Ölçülen: tuş (ya da istek) ve dispatcher'ın boşa
/// kadar pompalanması — yerleşim ve kare dahil, kullanıcının beklediği iş. Örnek başına: duvar saati, UI thread'in CPU zamanı
/// (<see cref="PerfMeasure.ThreadCycles"/>, <see cref="PerfMeasure.CyclesPerMs"/>), GC 0/1/2 sayısı ve toplam GC duraklaması farkı. Headless'ta
/// hareket kapalıdır: açılışın geçişi anında biter, örneklere karışmaz.</para>
/// </summary>
[Trait("Category", "Measurement")]
[Collection("Console UI (serial)")]
public sealed class ConsolePageEndMeasurementTests(ITestOutputHelper output)
{
    private const int Samples = 7;
    private static readonly int[] PageSizes = [1_000, 10_000, 12_000, 50_000, 100_000];

    private enum Route { CtrlEnd, End, FollowRequest }

    private readonly record struct Sample(double WallMs, double CpuMs, int Gen0, int Gen1, int Gen2, double PauseMs);

    [SkippableFact]
    public async Task Going_to_the_end_of_a_project_page_costs_this_much_UI_thread_time()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("BO_MEASURE_CTRL_END") == "1",
            "Opens a real offscreen window and pages up to 100 000 lines — opt in with BO_MEASURE_CTRL_END=1.");

        var report = await StaThread.RunAsync(MeasureAll, name: "page-end-measurement-sta");
        foreach (string line in report) output.WriteLine(line);
    }

    private static List<string> MeasureAll()
    {
        double cyclesPerMs = PerfMeasure.CyclesPerMs();
        string[] all = [.. Enumerable.Range(0, PageSizes.Max()).Select(MsBuildLines.Warning)];
        var report = new List<string>
        {
            string.Format(CultureInfo.InvariantCulture, "UI thread: {0:0.00} Mcycles per ms of CPU (calibrated)", cyclesPerMs / 1_000_000),
            string.Format(CultureInfo.InvariantCulture, "lines: MSBuild warnings, {0:0} characters on average, colorized", all.Average(l => l.Length)),
            "lines   route          first: wall / cpu ms | samples (" + Samples.ToString(CultureInfo.InvariantCulture)
                + "): wall median / max, cpu median / max ms | GC 0/1/2, pause ms",
        };
        using var narrative = new RealizedNarrative(lines: 400);
        if (!narrative.View.Editor.TextArea.TextView.LineTransformers.OfType<ConsoleColorizer>().Any())
            throw new InvalidOperationException("the console is not colorized — the measurement would not see the colorizer's cost");
        foreach (int lines in PageSizes)
        {
            string[] page = all[..lines];
            foreach (var route in Enum.GetValues<Route>())
            {
                narrative.OpenPage(page);
                var first = Measure(narrative, route, cyclesPerMs, fromTheTop: false); // sayfa tepede açılır
                Measure(narrative, route, cyclesPerMs, fromTheTop: true);              // ısınma
                var samples = Enumerable.Range(0, Samples).Select(_ => Measure(narrative, route, cyclesPerMs, fromTheTop: true)).ToList();
                report.Add(Row(lines, route, first, samples));
            }
        }
        return report;
    }

    /// <summary>Bir iniş: (gerekirse tepeye dönüş — ölçülmez) tuş ya da istek, ardından dispatcher boşa kadar.</summary>
    private static Sample Measure(RealizedNarrative narrative, Route route, double cyclesPerMs, bool fromTheTop)
    {
        if (fromTheTop) narrative.PressCtrlHome();
        // Her iniş sayfanın tepesinden başlar: dönüş tutmasaydı ölçülen iş boş bir basış olurdu.
        if (narrative.View.Editor.VerticalOffset > 0) throw new InvalidOperationException($"{route} sample did not start at the top of the page");
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        var pause = GC.GetTotalPauseDuration();
        ulong cycles = PerfMeasure.ThreadCycles();
        var wall = Stopwatch.StartNew();

        switch (route)
        {
            case Route.CtrlEnd: narrative.PressCtrlEnd(); break;
            case Route.End: narrative.Press(Key.End); break;
            default:
                narrative.View.RequestBottom(); // takibin isteği, tuşsuz
                DispatcherPump.DrainToIdle();
                break;
        }

        wall.Stop();
        double cpuMs = (PerfMeasure.ThreadCycles() - cycles) / cyclesPerMs;
        if (narrative.DistanceFromBottom > 48) throw new InvalidOperationException($"{route} did not reach the end of the page");
        return new Sample(wall.Elapsed.TotalMilliseconds, cpuMs,
            GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2,
            (GC.GetTotalPauseDuration() - pause).TotalMilliseconds);
    }

    private static string Row(int lines, Route route, Sample first, List<Sample> samples) =>
        string.Format(CultureInfo.InvariantCulture,
            "{0,7} {1,-14} first {2,7:0.0} / {3,7:0.0} | wall {4,7:0.0} / {5,7:0.0}, cpu {6,7:0.0} / {7,7:0.0} | GC {8}/{9}/{10}, pause {11:0.0} (first: GC {12}/{13}/{14}, pause {15:0.0})",
            lines, route, first.WallMs, first.CpuMs,
            PerfMeasure.Median(samples.Select(s => s.WallMs)), samples.Max(s => s.WallMs),
            PerfMeasure.Median(samples.Select(s => s.CpuMs)), samples.Max(s => s.CpuMs),
            samples.Sum(s => s.Gen0), samples.Sum(s => s.Gen1), samples.Sum(s => s.Gen2), samples.Sum(s => s.PauseMs),
            first.Gen0, first.Gen1, first.Gen2, first.PauseMs);
}
