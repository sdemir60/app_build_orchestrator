using System.IO;
using System.Text.RegularExpressions;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.Diagnostics;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Supervisor;
using BuildOrchestrator.Tests.App;
using BuildOrchestrator.Tests.Diagnostics;
using static BuildOrchestrator.Tests.Supervisor.RunCoordinatorTests;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [bellek trimi] Motorun işlem sonu kapanışı: <see cref="RunCoordinator.TrimAndReportMemory"/> ÖNCE agresif toplamayı
/// (<see cref="MemoryTrim.CollectAndRelease"/>), SONRA bellek tanı satırını yazar — satır trim SONRASI değeri taşır.
/// Dört işlem sonu aynı kuralı paylaşır: koşu, Sync, Clean, Optimize.
///
/// <para><b>Gerçek toplama burada koşmaz.</b> Bloklayan, sıkıştıran bir GC bütün test sürecini durdurur; her koordinatör
/// testi koşu bitişinde onu ödese süit yavaşlar ve paralel koşan zamanlama testlerini bozar. Bu yüzden her test
/// koordinatörü <see cref="NoRealGc.Collection"/> ya da bir sayaç/kayıtçı verir; üretim varsayılanını yalnız
/// <see cref="A_coordinator_built_without_a_trim_seam_trims_with_the_single_production_definition"/> kaynaktan pinler.</para>
/// </summary>
public class RunCoordinatorMemoryTests
{
    // ---------------------------------------------------------------- yardımcılar

    /// <summary>"trim" ve "line" olaylarını TEK sırada tutar: sıra ancak ortak bir zaman çizgisinde karşılaştırılabilir.
    /// <see cref="Trimmed"/> dikiş olarak, <see cref="Console"/> koordinatörün konsol kanalı olarak bağlanır; konsola düşen
    /// diğer satırlar (koşu başlığı gibi) sayılmaz.</summary>
    private sealed class Timeline
    {
        private readonly List<string> _events = [];

        public IReadOnlyList<string> Events { get { lock (_events) return [.. _events]; } }

        public void Trimmed() { lock (_events) _events.Add("trim"); }

        public void Console(string line)
        {
            if (MemoryLineWiringTests.IsMemoryLine(line)) lock (_events) _events.Add("line");
        }
    }

    /// <summary>
    /// Host'u verilen komutlarla (stdin EOF'a kadar) koşturur: gerçek <see cref="SupervisorHost"/>, izole bir önbellek kökünde
    /// <see cref="WorkspaceServices.Default"/> servisleri, gerçek process yok. Döner: telde görülen olaylar ve trim/satır sırası.
    /// <paramref name="commands"/> boş bir çalışma alanı kökünü alır (komutlar kökü taşır). <paramref name="planner"/> yalnız koşuyu
    /// planlamada ASILI tutması gereken (slot dolu kalsın) testler içindir. <paramref name="afterExit"/>, host çıktıktan SONRA ve
    /// koordinatör ile zaman çizgisi hâlâ canlıyken koşar: host döndükten sonra koşunun kendi kapanışını gözleyen testin yeri
    /// (planlamayı serbest bırakır, koşunun bitmesini bekler, zaman çizgisini yeniden okur); sandbox ondan sonra silinir.
    /// </summary>
    private static async Task<(List<IpcEvent> Wire, IReadOnlyList<string> Order)> RunHostAsync(
        Func<string, IpcCommand[]> commands, Func<WorkspaceServices, WorkspaceServices>? services = null,
        Func<StartRunCommand, Action<string>, RunPlan>? planner = null,
        Func<RunCoordinator, List<IpcEvent>, Timeline, Task>? afterExit = null)
    {
        string sandbox = Directory.CreateTempSubdirectory("bo-memtrim-").FullName;
        try
        {
            string root = Directory.CreateDirectory(Path.Combine(sandbox, "workspace")).FullName;
            var timeline = new Timeline();
            var stdout = new SupervisorHostHarness.CollectingStream();
            var writer = new NdjsonWriter(stdout);
            using var job = JobObject.CreateKillOnClose();
            using var coordinator = SupervisorHostHarness.NewIdleCoordinator(job, writer, timeline.Console, timeline.Trimmed, planner);
            var workspace = WorkspaceServices.Default(sandbox, _ => throw new NotSupportedException("no restore in this test"));
            var host = new SupervisorHost(writer, new NdjsonReader(await SupervisorHostHarness.StdinWith(commands(root))), job,
                coordinator, services is null ? workspace : services(workspace));

            Assert.Equal(0, await Task.Run(() => host.RunAsync()).WaitAsync(SupervisorHostHarness.Limit));
            var wire = NdjsonWire.Parse(stdout.Text);
            if (afterExit is not null) await afterExit(coordinator, wire, timeline);
            return (wire, timeline.Events);
        }
        finally { SupervisorHostHarness.TryDeleteDirectories(sandbox); }
    }

    private static int MemoryLinesIn(Harness h)
    {
        lock (h.ConsoleLines) return h.ConsoleLines.Count(MemoryLineWiringTests.IsMemoryLine);
    }

    // ---------------------------------------------------------------- sıra ve hata yutma

    /// <summary>Bellek satırı trim'den SONRA yazılır: satır, toplamanın bıraktığı değeri taşır.</summary>
    [Fact]
    public void The_memory_line_is_written_after_the_trim()
    {
        var timeline = new Timeline();
        using var job = JobObject.CreateKillOnClose();
        using var coordinator = SupervisorHostHarness.NewIdleCoordinator(job, new NdjsonWriter(Stream.Null), timeline.Console, timeline.Trimmed);

        coordinator.TrimAndReportMemory();

        Assert.Equal(["trim", "line"], timeline.Events);
    }

    /// <summary>Trim bir optimizasyondur: toplama fırlatırsa hata yutulur, satır yine yazılır ve çağıran iş (Sync, koşu
    /// kapanışı) onun yüzünden düşmez.
    /// <para>[idle-memory · final review 49] Yutulan hata İZSİZ kalmaz: konsola (stderr) tek <c>warning: memory trim failed:
    /// &lt;ileti&gt;</c> satırı düşer ve bellek satırından ÖNCE yazılır — toplama başarısız olduysa satırın taşıdığı değer
    /// toplanmamış bir yığındır, okuyan bunu bilmelidir. Eskiden hata sessizce yutuluyordu (bir koşunun satırı beklenenden yüksek
    /// çıksa nedeni görülemezdi).</para></summary>
    [Fact]
    public void A_failing_trim_never_breaks_the_report_or_the_work()
    {
        var lines = new List<string>();
        using var job = JobObject.CreateKillOnClose();
        using var coordinator = SupervisorHostHarness.NewIdleCoordinator(job, new NdjsonWriter(Stream.Null), lines.Add,
            memoryTrim: () => throw new InvalidOperationException("collection refused"));

        coordinator.TrimAndReportMemory();

        Assert.Equal(2, lines.Count); // KIRMIZI: bugün hata iz bırakmaz, tek satır (bellek satırı) vardır
        Assert.Equal("warning: memory trim failed: collection refused", lines[0]);
        Assert.StartsWith(MemoryLine.Prefix, lines[1], StringComparison.Ordinal);
    }

    /// <summary>Hata izi de bir tanıdır: konsol kanalı o satırı yazarken patlarsa (ör. kapanmış stderr pipe'ı) çağıran iş yine
    /// düşmez — trim'in hatasını yazmak, trim'in hatasından daha ağır bir hata üretemez.</summary>
    [Fact]
    public void A_console_that_fails_while_the_trim_failure_is_reported_never_breaks_the_work()
    {
        using var job = JobObject.CreateKillOnClose();
        using var coordinator = SupervisorHostHarness.NewIdleCoordinator(job, new NdjsonWriter(Stream.Null),
            _ => throw new IOException("stderr pipe closed"),
            memoryTrim: () => throw new InvalidOperationException("collection refused"));

        coordinator.TrimAndReportMemory();
    }

    // ---------------------------------------------------------------- koşu ve koşu sonu

    /// <summary>
    /// Koşu slotu doluyken (planlama dahil) toplama ATLANIR, tanı satırı yine yazılır: bloklayan GC uçuştaki koşunun IPC ve
    /// MSBuild pompalarını durdururdu. Host, Sync'i koşu sürerken de koşturabilir (yalnız Clean/Optimize/checkout reddeder).
    /// Atlanan toplama kaybolmaz: koşunun KENDİ bitişi yuvayı bıraktıktan sonra toplar.
    /// </summary>
    [Fact]
    public async Task A_trim_asked_while_a_run_is_active_is_skipped_but_the_line_is_still_written()
    {
        int trims = 0;
        var inFlight = Signal();
        var release = Signal();
        var invoker = new FakeInvoker(async (_, _, _) =>
        {
            inFlight.TrySetResult();
            await release.Task;
            return Ok();
        });
        using var h = new Harness(PlanOf(Node("A")), invoker, memoryTrim: () => Interlocked.Increment(ref trims));

        await h.Sut.StartAsync(Start(), default);
        await inFlight.Task.WaitAsync(Limit);      // proje derleniyor: koşu slotu dolu

        h.Sut.TrimAndReportMemory();               // ör. koşu sürerken biten bir Sync

        Assert.Equal(0, Volatile.Read(ref trims));
        Assert.Equal(1, MemoryLinesIn(h));

        release.SetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(1, Volatile.Read(ref trims)); // koşu sonu: yuva bırakıldı, toplama çalıştı
        Assert.Equal(2, MemoryLinesIn(h));
    }

    // ---------------------------------------------------------------- Sync, Clean, Optimize sonu

    /// <summary>Clean tamamlanınca host önce toplar, sonra satırı yazar.</summary>
    [Fact]
    public async Task A_finished_clean_trims_before_it_writes_the_memory_line()
    {
        var (wire, order) = await RunHostAsync(root => [new CleanWorkspaceCommand(root)]);

        Assert.Contains(wire, e => e is CleanCompletedEvent); // Clean gerçekten tamamlandı
        Assert.Equal(["trim", "line"], order);
    }

    /// <summary>Beklenmeyen hata da bir SONUÇTUR (<c>cleanFailed</c>): toplama <c>finally</c>'dedir, catch'e düşen yol da
    /// aynı kapanışı yapar.</summary>
    [Fact]
    public async Task A_clean_that_fails_unexpectedly_still_trims_before_it_writes_the_memory_line()
    {
        var (wire, order) = await RunHostAsync(root => [new CleanWorkspaceCommand(root)],
            services: s => s with { Clean = _ => throw new InvalidOperationException("clean exploded") });

        Assert.Contains(wire, e => e is ErrorEvent { Code: "cleanFailed" }); // yol gerçekten koştu
        Assert.Equal(["trim", "line"], order);
    }

    /// <summary>Optimize tamamlanınca host önce toplar, sonra satırı yazar.</summary>
    [Fact]
    public async Task A_finished_optimize_trims_before_it_writes_the_memory_line()
    {
        var (wire, order) = await RunHostAsync(root => [new OptimizeWorkspaceCommand(root)]);

        Assert.Contains(wire, e => e is OptimizeCompletedEvent); // Optimize gerçekten tamamlandı
        Assert.Equal(["trim", "line"], order);
    }

    [Fact]
    public async Task An_optimize_that_fails_unexpectedly_still_trims_before_it_writes_the_memory_line()
    {
        var (wire, order) = await RunHostAsync(root => [new OptimizeWorkspaceCommand(root)],
            services: s => s with { Optimize = _ => throw new InvalidOperationException("optimize exploded") });

        Assert.Contains(wire, e => e is ErrorEvent { Code: "optimizeFailed" });
        Assert.Equal(["trim", "line"], order);
    }

    /// <summary>Sync'in kendi kapısı hata olaylarını normal dönüşle yayınlar (kök yok → <c>planFailed</c>); toplama o yolda da
    /// satırdan önce koşar.</summary>
    [Fact]
    public async Task A_sync_that_ends_on_its_own_gate_trims_before_it_writes_the_memory_line()
    {
        var (wire, order) = await RunHostAsync(root => [new SyncWorkspaceCommand(Path.Combine(root, "missing"), "main")]);

        Assert.Contains(wire, e => e is ErrorEvent { Code: "planFailed" });
        Assert.Equal(["trim", "line"], order);
    }

    [Fact]
    public async Task A_sync_that_fails_unexpectedly_still_trims_before_it_writes_the_memory_line()
    {
        var (wire, order) = await RunHostAsync(root => [new SyncWorkspaceCommand(root, "main")],
            services: s => s with { Sync = _ => throw new InvalidOperationException("sync exploded") });

        Assert.Contains(wire, e => e is ErrorEvent { Code: "planFailed" });
        Assert.Equal(["trim", "line"], order);
    }

    /// <summary>
    /// Reddedilen Clean/Optimize hiçbir iş yapmaz: toplama ve satır YOK (çağrı, ret dönüşünden SONRA başlayan try'ın finally'sindedir).
    /// Sonradan bitirilen koşunun kendi kapanışı ise yine toplar ve satırı yazar — rapor o koşunundur.
    /// </summary>
    [Theory]
    [InlineData("clean")]
    [InlineData("optimize")]
    public async Task A_rejected_clean_or_optimize_neither_trims_nor_writes_a_memory_line(string operation)
    {
        // Planlama, kapı kontrolü bitene kadar BLOKLANIR → koşu slotu dolu kalır (sinyal, Thread.Sleep DEĞİL [D8]).
        var releasePlanning = Signal();

        await RunHostAsync(
            root => [new StartRunCommand("r1", RunMode.Build, root, "Debug", 1),
                     operation == "clean" ? new CleanWorkspaceCommand(root) : new OptimizeWorkspaceCommand(root)],
            planner: (_, _) => { releasePlanning.Task.GetAwaiter().GetResult(); throw new InvalidOperationException("no plan"); },
            afterExit: async (coordinator, wire, timeline) =>
            {
                Assert.Contains(wire, e => e is ErrorEvent error && error.Code == operation + "Rejected");
                Assert.Empty(timeline.Events);          // ret yolu ne topladı ne satır yazdı

                releasePlanning.SetResult();
                await coordinator.RunCompletion.WaitAsync(SupervisorHostHarness.Limit);

                Assert.Equal(["trim", "line"], timeline.Events); // yalnız koşunun kendi kapanışı
            });
    }

    // ---------------------------------------------------------------- üretim varsayılanı

    /// <summary>
    /// Üretim varsayılanı: dikiş verilmeyen koordinatör <see cref="MemoryTrim.CollectAndRelease"/> ile toplar — başka bir GC
    /// çağrısıyla ya da boş bir delege ile DEĞİL. Supervisor derlemesi internal'larını testlere açmaz ve gerçek toplamayı bu
    /// testte koşturmak bütün süreci durdurur; bağ, motorun stderr kanalı gibi
    /// (<c>The_engine_wires_its_console_channel_to_stderr_in_production</c>) kaynaktan pinlenir. Yalnız KOD taranır (yorum ve
    /// literaller ayıklanır): yorum olarak kopyalanmış bir bağ, gerçeği silinse de testi tatmin ederdi. Dikişin gerçekten
    /// kullanıldığını yukarıdaki davranış testleri sabitler.
    /// </summary>
    [Fact]
    public void A_coordinator_built_without_a_trim_seam_trims_with_the_single_production_definition()
    {
        string source = SourceLiterals.CodeOnly(
            File.ReadAllText(Path.Combine(RepoPaths.SrcRoot, "BuildOrchestrator.Supervisor", "RunCoordinator.cs")));

        Assert.Matches(new Regex(@"\bmemoryTrim\s*\?\?\s*MemoryTrim\.CollectAndRelease\b"), source);
    }
}
