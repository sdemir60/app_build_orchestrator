using System.Globalization;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.ViewModels;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 1 · spec §6.6] Koşu anlatısının bellek bütçesi — sentetik 100 k satırlık bir koşu (<c>projectLog</c>
/// akışı; gerçek kabuk ve VM, motor yok). [dalga 2 · spec §6.3] Koşu, motorun yapacağı gibi bir koşu klasörü bildirir
/// (<see cref="MainWindowHost.StartBuildWithLogDirectory"/>): VM anlatıyı oradaki dosyaya yayar.
///
/// <para><b>Yapısal kilit (her koşuda):</b> anlatının TEK sahibi VM'in store'udur (<see cref="NarrativeStore"/>):
/// her satır orada bir kez durur, görünüm kendi kopyasını tutmaz (kabuk konsolu açılışta store'a bağlar —
/// <c>ConsoleView.BindNarrative</c>) ve canlı satır tamponu yükleme yokken boştur (<c>RunViewModel.LiveLineCount</c>). (c) Bellekte
/// yalnız store'un penceresi kalır (<see cref="NarrativeStore.CharCount"/> ≤ <see cref="NarrativeStore.MemoryWindowLines"/> × en
/// uzun satır) ve koşu klasöründeki dosya anlatının TAM satır sayısını tutar — koşunun sonu tamponu diske indirmiştir. Yeni işlem
/// her şeyi sıfırlar (spec §6.6 d): store, spool, canlı satır tamponu, koşu metni ve konsol belgesi. Bir regresyon kilididir: tek
/// kopya, tampon daraltma ve spool işiyle yeşildir; biri geri alınırsa kırmızıya döner.</para>
///
/// <para><b>Ölçüm (kapılı, <c>BO_MEASURE_MEMORY=1</c>):</b> aynı koşu, ardından işlem sonu trimi (üretim toplayıcısı) →
/// yönetilen yığının koşu öncesine göre büyümesi ≤ <see cref="BudgetBytes"/> (8 MB, sabit tavan — spec §6.6 dalga 2). Bellekte
/// kalan: store'un penceresi, ofset indeksi (satır başına 8 B) ve görünümün render dilimi — metinden bağımsız.
/// <b>[DEĞİŞEN BÜTÇE — dalga 2]</b> Eski iddia: büyüme ≤ 1,2 × metin (satırların UTF-16 baytı); metnin üstündeki pay store'un
/// kendi yapısıydı (string başlıkları, satır dizisi) ve görünümün render dilimiydi — ölçüldü (2026-10-13): 26,8 MB / 22,9 MB =
/// 1,167. Değişme gerekçesi: anlatının tamamı bellekteydi ve büyüme metinle orantılıydı; spool'la (spec §6.3) anlatı koşu
/// klasöründeki dosyadadır, büyüme sabit bir tavanın altına iner — ölçüldü (2026-10-14, iki koşu): 1,80 ve 2,10 MB, metin 22,93 MB.
/// Ölçüm üretimin TUTTUĞUNU ölçer: varsayılan fixture'ın pompası hiç tick etmez ve her satırın kanal kaydını tutar — ölçüldü,
/// metnin ~%15'i (dalga 1'de oran 1,318: bütçeyi bu artefakt aşardı); üretimde pompa kanalı ~50 ms'de bir boşaltır. Bu yüzden test
/// pompayı kendisi sürer: <see cref="LinesPerTick"/> satırda bir tick; satırlar üretimdeki yoldan (pompa → Dispatcher →
/// <c>AppendConsoleBatch</c>) belgeye gider ve görünüm render dilimine kırpar. Böylece görünümdeki bir kopya da sayıya girer:
/// konsolun store bağı atlanınca görünüm kırpılan satırları kopyalar — ölçüldü (dalga 1), oran 2,301. Bütçe gevşetilmez; aşım
/// raporlanır.</para>
///
/// <para><b>[idle-memory · dalga 2 · spec §6.5] Kompakt konsol KAPALI koşar</b> (fixture'ın varsayılanı —
/// <see cref="MainWindowHost.New"/>): ölçülen, her satırı anlatıya giren büyük bir koşudur. Kompakt kipte (üretim varsayılanı) aynı
/// koşu anlatıda birkaç satır bırakır — store koşunun hacmini hiç taşımaz
/// (<see cref="A_compact_run_keeps_the_narrative_to_a_handful_of_lines"/>); bütçe en kötü durumu, kompaktı kapatan kullanıcıyı
/// ölçer.</para>
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

    /// <summary>[spec §6.6 · dalga 2] Spool'lu koşudan sonra yönetilen yığının en çok büyümesi: 8 MB — metinden bağımsız sabit tavan.</summary>
    private const long BudgetBytes = 8L * 1024 * 1024;

    /// <summary>Ölçümde pompanın kaç satırda bir tick ettiği: kanal hiçbir an koşunun tamamını tutmaz (üretimde pompa ~50 ms'de
    /// bir boşaltır).</summary>
    private const int LinesPerTick = 1_000;

    /// <summary>Bir tick'in batch'inin belgeye ulaşması için tavan — beklenen süre milisaniyelerdir; tavan yalnız asılı kalmayı
    /// önler (koşul sağlanınca hemen çıkılır).</summary>
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(30);

    private const double BytesPerMb = 1024 * 1024;

    private static string Line(int i) =>
        $"  D:\\repo\\Project{i % 200}\\File{i}.cs(12,5): warning CS1591: Missing XML comment for publicly visible type or member 'X{i}'";

    /// <summary>[final review "Tasks 9-10" hijyeni] Satır sayısı koşunun başlangıcından SONRAKİ taban üstüne sayılır: başlangıç bir
    /// anlatı satırı yazsa da iddia "her <c>projectLog</c> satırı store'da bir kez"dir.
    /// <para>[idle-memory · dalga 2 · spec §6.6 (c)] Koşu bir koşu klasörü bildirir: bellekte yalnız store'un penceresi kalır ve dosya
    /// anlatının tam satır sayısını tutar. Pencere koşunun son satırlarıdır; sınır onların en uzunuyla hesaplanır.</para></summary>
    [StaFact]
    public void A_synthetic_run_leaves_one_copy_of_the_text_and_no_live_line_buffer()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        using var logs = new SpoolDir(vm.Narrative);
        MainWindowHost.StartBuildWithLogDirectory(vm, logs.Path, "A");
        int baseline = vm.Narrative.Count;
        for (int i = 1; i <= Lines; i++) MainWindowHost.LogLine(vm, "A", i, Line(i));
        Assert.Equal(baseline + Lines, vm.Narrative.Count);            // her satır store'da bir kez
        MainWindowHost.FinishBuild(vm, "A");
        DispatcherPump.DrainToIdle();

        Assert.Equal(0, vm.LiveLineCount);                             // yükleme yok: canlı satır tamponu boş
        Assert.False(window.Shell.ConsoleViewControl.BacklogOwnsCopy); // görünüm anlatının kopyasını tutmaz
        long windowBound = NarrativeStore.MemoryWindowLines * (long)Enumerable
            .Range(Lines - NarrativeStore.MemoryWindowLines + 1, NarrativeStore.MemoryWindowLines).Max(i => Line(i).Length);
        Assert.True(vm.Narrative.CharCount <= windowBound,              // (c) bellekte yalnız pencere
            $"the store keeps {vm.Narrative.CharCount} characters in memory, the window holds at most {windowBound}");
        Assert.Equal(vm.Narrative.Count, logs.ReadText().AsSpan().Count('\n')); // (c) dosya tam satır sayısında
        GC.KeepAlive(window);
    }

    /// <summary>[idle-memory · dalga 2 · spec §6.5 · karar 14] Kompakt kipte (üretim varsayılanı) aynı 100 k tekil uyarılık koşu anlatıya
    /// beş satır bırakır: projenin başlığı, ilk üç uyarı ve gizlenen uyarıların tek özeti — store koşunun hacmini hiç taşımaz. Tam çıktı
    /// proje logundadır (motorun dosyası). Yukarıdaki bütçe testleri kompakt KAPALI koşar: ölçtükleri her satırın girdiği anlatıdır.</summary>
    [StaFact]
    public void A_compact_run_keeps_the_narrative_to_a_handful_of_lines()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        vm.CompactConsole = true;
        MainWindowHost.StartBuild(vm, "A");
        int baseline = vm.Narrative.Count;
        for (int i = 1; i <= Lines; i++) MainWindowHost.LogLine(vm, "A", i, Line(i));
        MainWindowHost.FinishBuild(vm, "A");

        int hidden = Lines - CompactConsoleFilter.ShownWarningsPerProject;
        string summary = string.Create(CultureInfo.InvariantCulture,
            $"A: {hidden} more warnings — CS1591 ×{hidden} — see the project log");
        Assert.Equal(["building A", Line(1), Line(2), Line(3), summary], vm.Narrative.Slice(baseline, vm.Narrative.Count));
        Assert.True(vm.Narrative.CharCount < 64 * 1024, $"the compact narrative keeps {vm.Narrative.CharCount} characters");
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [idle-memory · spec §6.6 (d) · final review "Tasks 9-10"] <b>Yeni işlem her şeyi sıfırlar</b> — 100 k satırlık bir koşudan
    /// sonra da: anlatı store'u, canlı satır tamponu, koşu metni ve konsol belgesi. Yeni işlemin (Sync düğmesi) temizliği store'u
    /// boşaltır, cevabı artık beklenmeyen proje log yüklemesini tamponuyla bitirir ve kabuk belgeyi boş store'a bağlar. Ön-koşullar
    /// her birinin önce DOLU olduğunu gösterir: belge koşunun kuyruğunu pompanın yolundan almıştır, tampon koşunun sonunda istenmiş
    /// (cevabı gelmemiş) bir proje logunun isteğinden sonraki satırını tutar.
    /// <para>[idle-memory · dalga 2 · spec §6.3] Koşu anlatısını koşu klasörüne yaymıştır: yeni işlem spool'u da kapatır.</para>
    /// </summary>
    [StaFact]
    public async Task A_new_operation_after_a_synthetic_run_resets_the_store_the_live_buffer_the_run_text_and_the_document()
    {
        using var dir = new TempDir();
        var (window, vm, console, tick) = PumpedHost(dir, productionMemoryCollector: false);
        using var logs = new SpoolDir(vm.Narrative);
        RunSyntheticBuild(vm, console, tick, logs.Path);
        ProjectLogIpc.Request(vm, MainWindowHost.IdOf("A"));             // koşunun sonunda bir proje logu istendi; gönderim düştü
        MainWindowHost.LogLine(vm, "A", Lines + 1, "after the request"); // istekten sonra: canlı tamponda
        Assert.True(vm.Narrative.Count > Lines, "ön-koşul: koşunun anlatısı store'da");
        Assert.True(vm.Narrative.IsSpooling, "ön-koşul: koşunun anlatısı koşu klasörüne yayılıyor");
        Assert.Equal(1, vm.LiveLineCount);                               // ön-koşul: tampon dolu
        Assert.NotEqual(0, console.Document.TextLength);                 // ön-koşul: belge koşunun kuyruğunu gösteriyor

        MainWindowHost.AcceptSends(vm);
        await MainWindowHost.StartSync(vm, SyncMode.Manual);             // yeni işlem: Sync düğmesi

        Assert.Equal(0, vm.Narrative.Count);
        Assert.False(vm.Narrative.IsSpooling);
        Assert.Equal(0, vm.LiveLineCount);
        Assert.Equal("", vm.GetRunDocumentText());
        Assert.Equal(0, console.Document.TextLength);
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
            "managed growth {0:0.00} MB (budget {1:0.00} MB), text {2:0.00} MB, growth/text {3:0.000}, {4} lines",
            growth / BytesPerMb, BudgetBytes / BytesPerMb, text / BytesPerMb, (double)growth / text, Lines));
        Assert.True(growth <= BudgetBytes, string.Create(CultureInfo.InvariantCulture,
            $"growth {growth / BytesPerMb:0.00} MB > budget {BudgetBytes / BytesPerMb:0.00} MB (text {text / BytesPerMb:0.00} MB)"));
    }

    /// <summary>Koşuyu sürer ve yönetilen yığının koşu öncesine göre büyümesini (trim sonrası) ve metnin baytını döner. Trim
    /// üretimin toplayıcısıyla yapılır (fixture'ın <c>productionMemoryCollector</c>'ı); test yalnız onu bir sayaçla sarar
    /// [final review "Tasks 9-10" hijyeni: toplama ikinci kez yazılmaz]. Koşu bir koşu klasörü bildirir (anlatı diske yayılır);
    /// klasörün dispose'u spool'u ölçümden SONRA kapatır.</summary>
    private static (long Growth, long Text) MeasureRun()
    {
        using var dir = new TempDir();
        var (window, vm, console, tick) = PumpedHost(dir, productionMemoryCollector: true);
        using var logs = new SpoolDir(vm.Narrative);
        window.TrimGraceAfterFinale = TimeSpan.Zero; // pay sıfır: boşaltma zamanlayıcı ateşlemez, trim boşta kuyruktan yürür
        int trims = 0;
        var production = window.MemoryCollector;
        window.MemoryCollector = () => { trims++; production(); }; // üretimin toplaması + sayaç

        long before = GC.GetTotalMemory(forceFullCollection: true);
        long text = RunSyntheticBuild(vm, console, tick, logs.Path);
        DispatcherPump.DrainToIdle(); // işlem sonu trimi (pay sıfır: boşta kuyruktan)

        Assert.Equal(1, trims);                                                     // ön-koşul: ölçüm trimden sonra
        Assert.True(console.Document.LineCount <= ConsoleView.SeededDocumentLines, // ön-koşul: görünüm yalnız render dilimini tutar
            $"the console document holds {console.Document.LineCount} lines — the render slice was not trimmed");
        Assert.True(vm.Narrative.IsSpooling, "ön-koşul: koşunun anlatısı koşu klasörüne yayıldı");
        long after = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(window);
        GC.KeepAlive(vm);
        return (after - before, text);
    }

    /// <summary>Pompası testin elinde olan kabuk (tek proje "A"): her <c>Release</c> bir tick (<see cref="Deliver"/>).</summary>
    private static (global::BuildOrchestrator.App.MainWindow Window, RunViewModel Vm, ConsoleView Console, SemaphoreSlim Tick)
        PumpedHost(TempDir dir, bool productionMemoryCollector)
    {
        var tick = new SemaphoreSlim(0);
        var (window, vm, _) = MainWindowHost.NewWithProjectsAndConsole(
            dir, new ConsoleBatcher(ct => tick.WaitAsync(ct)), productionMemoryCollector, ("A", null));
        return (window, vm, window.Shell.ConsoleViewControl, tick);
    }

    /// <summary>Sentetik koşuyu üretim yolundan sürer: koşu <paramref name="logDirectory"/>'yi koşu klasörü olarak bildirir, her satır
    /// modele (<c>projectLog</c>), pompa her <see cref="LinesPerTick"/> satırda bir tick eder ve batch belgeye iner; koşu biter ve son
    /// batch da teslim edilir. Dönen değer metnin UTF-16 baytıdır.</summary>
    private static long RunSyntheticBuild(RunViewModel vm, ConsoleView console, SemaphoreSlim tick, string logDirectory)
    {
        long text = 0;
        MainWindowHost.StartBuildWithLogDirectory(vm, logDirectory, "A");
        for (int i = 1; i <= Lines; i++)
        {
            string line = Line(i);
            text += line.Length * 2L;
            MainWindowHost.LogLine(vm, "A", i, line);
            if (i % LinesPerTick == 0) Deliver(tick, vm, console);
        }
        MainWindowHost.FinishBuild(vm, "A");
        Deliver(tick, vm, console);
        return text;
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
