using System.Globalization;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Core.Diagnostics;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 1 · spec §6.6] Koşu anlatısının bellek bütçesi — sentetik 100 k satırlık bir koşu (<c>projectLog</c>
/// akışı; gerçek kabuk ve VM, motor yok).
///
/// <para><b>Yapısal kilit (her koşuda):</b> anlatının bellekteki TEK kopyası VM'in store'udur (<see cref="NarrativeStore"/>):
/// her satır orada bir kez durur, görünüm kendi kopyasını tutmaz (kabuk konsolu açılışta store'a bağlar —
/// <c>ConsoleView.BindNarrative</c>) ve canlı satır tamponu yükleme yokken boştur (<c>RunViewModel.LiveLineCount</c>). Bir
/// regresyon kilididir: dalga 1'in tek kopya ve tampon daraltma işiyle yeşildir; bağ ya da daraltma geri alınırsa kırmızıya
/// döner.</para>
///
/// <para><b>Ölçüm (kapılı, <c>BO_MEASURE_MEMORY=1</c>):</b> aynı koşu, ardından işlem sonu trimi (üretim toplayıcısı) →
/// yönetilen yığının koşu öncesine göre büyümesi ≤ 1,2 × metin (satırların UTF-16 baytı). Metnin üstündeki pay store'un kendi
/// yapısıdır (string başlıkları, satır dizisi) ve görünümün render dilimidir — ölçüldü (2026-10-13): 26,8 MB / 22,9 MB = 1,167.
/// Ölçüm üretimin TUTTUĞUNU ölçer: varsayılan fixture'ın pompası hiç tick etmez ve her satırın kanal kaydını tutar — ölçüldü,
/// metnin ~%15'i (oran 1,318: bütçeyi bu artefakt aşardı); üretimde pompa kanalı ~50 ms'de bir boşaltır. Bu yüzden test
/// pompayı kendisi sürer: <see cref="LinesPerTick"/> satırda bir tick; satırlar üretimdeki yoldan (pompa → Dispatcher →
/// <c>AppendConsoleBatch</c>) belgeye gider ve görünüm render dilimine kırpar. Böylece görünümdeki bir kopya da sayıya girer:
/// konsolun store bağı atlanınca görünüm kırpılan satırları kopyalar — ölçüldü, oran 2,301. Bütçe gevşetilmez; aşım raporlanır.
/// Dalga 2 (diske yayılmış anlatı) bu bütçeyi sabit bir tavana indirir.</para>
///
/// <para><b>Neden kapılı:</b> koşu ve tam toplamalar birkaç saniye sürer ve <c>GC.GetTotalMemory</c> SÜREÇ GENELİDİR — başka bir
/// testin ayırması ölçüme girerdi (bu koleksiyon başka hiçbir koleksiyonla eşzamanlı koşmaz; yine de tek başına koşulur).
/// <c>Category=Measurement</c> etiketi tek başına kapı değildir (<c>Category!=Acceptance</c> filtresi onu da koşturur); kapı ilk
/// satırdaki <c>Skip.IfNot</c>'tur. Gövde <see cref="StaThread.RunAsync{T}"/>'te koşar: <c>[StaFact]</c>'in runner'ı Skip'i
/// tanımaz. Nasıl koşulur: uygulama kapalıyken <c>$env:BO_MEASURE_MEMORY='1'; dotnet test
/// tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~NarrativeBudgetTests" --logger
/// "console;verbosity=detailed"</c> — sayılar ayrıntılı çıktıda.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public sealed class NarrativeBudgetTests(ITestOutputHelper output)
{
    private const int Lines = 100_000;

    /// <summary>Ölçümde pompanın kaç satırda bir tick ettiği: kanal hiçbir an koşunun tamamını tutmaz (üretimde pompa ~50 ms'de
    /// bir boşaltır).</summary>
    private const int LinesPerTick = 1_000;

    /// <summary>Bir tick'in batch'inin belgeye ulaşması için tavan — beklenen süre milisaniyelerdir; tavan yalnız asılı kalmayı
    /// önler (koşul sağlanınca hemen çıkılır).</summary>
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(30);

    private const double BytesPerMb = 1024 * 1024;

    private static string Line(int i) =>
        $"  D:\\repo\\Project{i % 200}\\File{i}.cs(12,5): warning CS1591: Missing XML comment for publicly visible type or member 'X{i}'";

    [StaFact]
    public void A_synthetic_run_leaves_one_copy_of_the_text_and_no_live_line_buffer()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        MainWindowHost.StartBuild(vm, "A");
        for (int i = 1; i <= Lines; i++) MainWindowHost.LogLine(vm, "A", i, Line(i));
        MainWindowHost.FinishBuild(vm, "A");
        DispatcherPump.DrainToIdle();

        Assert.Equal(Lines, vm.Narrative.Count);                       // her satır store'da bir kez
        Assert.Equal(0, vm.LiveLineCount);                             // yükleme yok: canlı satır tamponu boş
        Assert.False(window.Shell.ConsoleViewControl.BacklogOwnsCopy); // görünüm anlatının kopyasını tutmaz
        GC.KeepAlive(window);
    }

    [SkippableFact]
    [Trait("Category", "Measurement")]
    public async Task Managed_memory_after_a_synthetic_run_and_a_trim_stays_within_budget()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("BO_MEASURE_MEMORY") == "1",
            "Feeds a 100k-line run and forces full collections — opt in with BO_MEASURE_MEMORY=1.");

        var (growth, text) = await StaThread.RunAsync(MeasureRun, name: "narrative-budget-sta");

        output.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "managed growth {0:0.00} MB, text {1:0.00} MB, growth/text {2:0.000} (budget 1.200), {3} lines",
            growth / BytesPerMb, text / BytesPerMb, (double)growth / text, Lines));
        Assert.True(growth <= text * 12 / 10, string.Create(CultureInfo.InvariantCulture,
            $"growth {growth / BytesPerMb:0.00} MB > 1.2 x text {text / BytesPerMb:0.00} MB"));
    }

    /// <summary>Koşuyu sürer ve yönetilen yığının koşu öncesine göre büyümesini (trim sonrası) ve metnin baytını döner.</summary>
    private static (long Growth, long Text) MeasureRun()
    {
        using var dir = new TempDir();
        var tick = new SemaphoreSlim(0); // pompanın tick'i testin elinde: her Release bir tick
        var (window, vm, _) = MainWindowHost.NewWithProjectsAndConsole(
            dir, new ConsoleBatcher(ct => tick.WaitAsync(ct)), ("A", null));
        var console = window.Shell.ConsoleViewControl;
        window.TrimGraceAfterFinale = TimeSpan.Zero; // pay sıfır: boşaltma zamanlayıcı ateşlemez, trim boşta kuyruktan yürür
        int trims = 0;
        window.MemoryCollector = () => { trims++; MemoryTrim.CollectAndRelease(); }; // üretimin toplaması (+ sayaç)

        long before = GC.GetTotalMemory(forceFullCollection: true);
        long text = 0;
        MainWindowHost.StartBuild(vm, "A");
        for (int i = 1; i <= Lines; i++)
        {
            string line = Line(i);
            text += line.Length * 2L;
            MainWindowHost.LogLine(vm, "A", i, line);
            if (i % LinesPerTick == 0) Deliver(tick, vm, console);
        }
        MainWindowHost.FinishBuild(vm, "A");
        Deliver(tick, vm, console);
        DispatcherPump.DrainToIdle(); // işlem sonu trimi (pay sıfır: boşta kuyruktan)

        Assert.Equal(1, trims);                                                     // ön-koşul: ölçüm trimden sonra
        Assert.True(console.Document.LineCount <= ConsoleView.RenderSliceLines + 1, // ön-koşul: görünüm yalnız render dilimini tutar
            $"the console document holds {console.Document.LineCount} lines — the render slice was not trimmed");
        long after = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(window);
        GC.KeepAlive(vm);
        return (after - before, text);
    }

    /// <summary>Pompaya bir tick verir ve anlatının son satırı belgeye ulaşana dek pompalar: kanal boşalır, batch üretimdeki yoldan
    /// (pompa → Dispatcher → <c>AppendConsoleBatch</c>) görünüme gider.</summary>
    private static void Deliver(SemaphoreSlim tick, RunViewModel vm, ConsoleView console)
    {
        tick.Release();
        string last = vm.Narrative.Join(vm.Narrative.Count - 1, vm.Narrative.Count);
        DispatcherPump.PumpUntil(() => console.Document.Text.EndsWith(last, StringComparison.Ordinal), DeliveryTimeout);
        Assert.EndsWith(last, console.Document.Text, StringComparison.Ordinal); // ön-koşul: pompa kanalı boşalttı, belge teslim aldı
    }
}
