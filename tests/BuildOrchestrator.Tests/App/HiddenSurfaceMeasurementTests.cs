using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [perf Faz A · A8] ÖLÇÜM — pencere gizliyken (tepsi) derlenen bir koşunun olay akışı UI thread'ine ne kadar yük bindirir.
/// Faz A'nın amacı tepsideki derlemede arayüz thread'ini boşta seviyesine yaklaştırmaktır. Kalıcı pin
/// (<c>HiddenSurfaceTests.Run_events_and_console_batches_leave_the_realized_shell_measure_valid_while_the_surface_is_hidden</c>)
/// yalnız "ölçüm geçersizlenmedi"yi sınar; bu test gerçek bir pencerede gerçek layout geçişlerini ve thread döngüsünü OKUR.
/// Bu bir pin DEĞİL, bir okuma: sayılar test çıktısına yazılır, eşik yoktur.
///
/// <para><b>Neden kapılı:</b> gerçek (ekran dışı) bir pencere açar ve dört okuma boyunca ~55 sn pompalar.
/// <c>Category=Measurement</c> etiketi TEK BAŞINA bunu sağlamaz: <c>Category!=Acceptance</c> filtresi diğer her kategoriyi kabul
/// eder. Gerçek kapı ilk satırdaki <c>Skip.IfNot</c>'tur: <c>BO_MEASURE_HIDDEN</c> <c>"1"</c> değilse test SKIPPED raporlanır ve
/// pencere hiç açılmaz. Gövde <c>[StaFact]</c>'te DEĞİL, <see cref="StaThread.RunAsync{T}"/>'te koşar: <c>[StaFact]</c>'in
/// runner'ı Skip'i tanımaz.</para>
///
/// <para><b>Nasıl koşulur:</b> uygulama kapalıyken <c>$env:BO_MEASURE_HIDDEN='1'; dotnet test
/// tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj -c Release --filter
/// "FullyQualifiedName~HiddenSurfaceMeasurementTests" --logger "console;verbosity=detailed"</c>. Sayılar yalnız ayrıntılı
/// çıktıda görünür.</para>
///
/// <para><b>Ne ölçülür:</b> gerçek <c>MainWindow</c> kurulur ama <c>Show()</c> EDİLMEZ — <c>OnSourceInitialized</c> gerçek bir
/// tepsi simgesi kurar ve global kısayol kaydeder. Pencerenin tik zamanlayıcısı, VM kablajı ve konsol/graf/liste kapıları
/// yerinde kalır; kabuğun içeriği ise ekran dışı gerçek bir pencereye (<c>DsResources.Realize</c>) taşınır ki layout
/// gerçek bir <c>PresentationSource</c> altında koşsun. Gizli okumada o pencere <c>Hide()</c> edilir ve sinyal iki yere yazılır:
/// kabuğun görünümleri kalıtsal sinyali barındıran pencereden, <c>MainWindow</c>'un kendi kapıları kendi DP'sinden okur
/// (üretimde ikisi aynı pencere). Sentetik akış 177 projeyi ~10 sn'ye yayar: her proje başlar, birkaç log satırı yazar ve biter;
/// konsol batch'leri pompanın yapacağı gibi <c>AppendConsoleBatch</c>'e verilir. Okunan: UI thread'in döngü sayacı
/// (<c>QueryThreadCycleTime</c>, saniyede milyon döngü) ve pencerenin <c>LayoutUpdated</c> sayısı (layout geçişi).</para>
///
/// <para><b>Okumanın sınırları:</b> dört okuma sırayla koşar — gizli ve OLAYSIZ (taban: pompanın yoklaması, tik zamanlayıcısı,
/// boştaki pencere), gizli (JIT soğuk), görünür, gizli (JIT sıcak). Olayların maliyeti gizli okumadan tabanı çıkararak okunur;
/// ikinci okuma ortak kodun JIT'ini öder, karşılaştırma için sıcak olanı kullan. Sayaç thread'in TÜM işini içerir (akışı besleyen
/// test kodu ve VM'in olay işleme işi dahil); bunlar olaylı okumalarda aynıdır. Headless'ta <c>App.Motion</c> yoktur: hareket
/// kapalıdır (reduced-motion), yani görünür okuma sonsuz animasyonların maliyetini İÇERMEZ — o farkı gerçek uygulama ölçümü
/// gösterir.</para>
/// </summary>
[Trait("Category", "Measurement")]
[Collection("Console UI (serial)")]
public sealed class HiddenSurfaceMeasurementTests(ITestOutputHelper output)
{
    private const int ProjectCount = 177;
    private const int LogLinesPerProject = 6;
    private static readonly TimeSpan StreamWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FeedInterval = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan SettleWindow = TimeSpan.FromMilliseconds(600);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentThread();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryThreadCycleTime(nint thread, out ulong cycles);

    private readonly record struct Reading(double MegaCyclesPerSecond, int LayoutPasses, int Projects, int Events, double Seconds);

    [SkippableFact]
    public async Task A_177_project_run_costs_this_much_UI_thread_time_hidden_and_visible()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("BO_MEASURE_HIDDEN") == "1",
            "Opens a real offscreen window and pumps it for about 55 seconds — opt in with BO_MEASURE_HIDDEN=1.");

        // Her okuma kendi STA thread'inde: bir önceki okumanın MainWindow'u (ve tik zamanlayıcısı) sonrakine karışmaz.
        var readings = new (string Label, bool Hidden, bool WithEvents)[]
        {
            ("hidden, no events (floor)", true, false),
            ("hidden (cold JIT)        ", true, true),
            ("visible                  ", false, true),
            ("hidden (warm)            ", true, true),
        };
        foreach (var (label, hidden, withEvents) in readings)
        {
            var reading = await StaThread.RunAsync(() => Measure(hidden, withEvents), name: "hidden-surface-measurement-sta");
            output.WriteLine(Line(label, reading));
        }
    }

    private static string Line(string label, Reading r) => string.Format(CultureInfo.InvariantCulture,
        "{0}: {1:0.0} Mcycles/s of UI thread, {2} layout passes, {3} projects / {4} events in {5:0.0} s",
        label, r.MegaCyclesPerSecond, r.LayoutPasses, r.Projects, r.Events, r.Seconds);

    private static Reading Measure(bool hidden, bool withEvents)
    {
        using var dir = new TempDir();
        string[] names = HiddenSurfaceTests.Names(ProjectCount);
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, HiddenSurfaceTests.ProjectPairs(names));
        var host = MainWindowHost.HostOffscreen(window);
        try
        {
            if (hidden)
            {
                host.Hide();                           // pencere gizli (üretimde IsVisibleChanged'in yaptığı)
                HiddenSurface.SetIsHidden(host, true); // kabuğun görünümleri sinyali barındıran pencereden okur
                window.SetSurfaceHidden(true);         // MainWindow'un kendi kapıları (konsol, graf, liste, tik) kendi DP'sinden okur
            }
            DispatcherPump.PumpFor(SettleWindow);      // ilk yerleşim ve kare ölçüme girmesin
            if (withEvents)
            {
                MainWindowHost.PreviewBuild(vm, names);
                MainWindowHost.StartBuild(vm, names);
                DispatcherPump.PumpFor(SettleWindow);  // koşu başlangıcının işi (ekran temizliği, şerit) ölçüme girmesin
            }

            int layoutPasses = 0, started = 0, events = 0, line = 0;
            Exception? failure = null;
            void OnLayoutUpdated(object? sender, EventArgs e) => layoutPasses++;
            host.LayoutUpdated += OnLayoutUpdated;
            var clock = Stopwatch.StartNew();
            var feed = new DispatcherTimer(DispatcherPriority.Normal) { Interval = FeedInterval };
            feed.Tick += (_, _) =>
            {
                try
                {
                    // Projeler akış süresine eşit yayılır: o ana kadar "vadesi gelen" her proje başlar, log yazar ve biter.
                    int due = Math.Min(ProjectCount, (int)(clock.Elapsed.TotalSeconds / StreamWindow.TotalSeconds * ProjectCount));
                    var batch = new StringBuilder();
                    for (; started < due; started++)
                    {
                        string name = names[started];
                        MainWindowHost.StartProject(vm, name);
                        for (int i = 1; i <= LogLinesPerProject; i++)
                        {
                            string text = $"line {++line}";
                            MainWindowHost.LogLine(vm, name, i, text);
                            batch.Append(text).Append('\n');
                        }
                        MainWindowHost.SucceedProject(vm, name);
                        events += 2 + LogLinesPerProject;
                    }
                    if (batch.Length > 0) window.AppendConsoleBatch(batch.ToString(), window.ConsoleReseedGen);
                }
                catch (Exception ex)
                {
                    failure ??= ex; // dispatcher'ın yakalanmamış istisnası test host'unu düşürürdü: pompadan sonra yeniden fırlatılır
                    feed.Stop();
                }
            };
            if (withEvents) feed.Start();

            QueryThreadCycleTime(GetCurrentThread(), out ulong cyclesBefore);
            DispatcherPump.PumpFor(StreamWindow);
            QueryThreadCycleTime(GetCurrentThread(), out ulong cyclesAfter);
            double seconds = clock.Elapsed.TotalSeconds;

            feed.Stop();
            host.LayoutUpdated -= OnLayoutUpdated;
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            return new Reading((cyclesAfter - cyclesBefore) / seconds / 1e6, layoutPasses, started, events, seconds);
        }
        finally
        {
            host.Close();
        }
    }
}
