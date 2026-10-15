using System.Globalization;
using System.IO;
using System.Threading.Channels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Logs;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Supervisor;
using BuildOrchestrator.Tests.App;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [T28] <c>getProjectLog</c> aktif run'ın dizininden okur ve chunk'lar gerçek <c>ThroughLineNumber</c> taşır —
/// canlı <c>projectLog</c> olaylarıyla dikişin Supervisor tarafı burada kanıtlanır (App-side eleme Task 12'nin
/// işi, burada test edilmez). Gerçek MSBuild YOK — sahte <see cref="IMsBuildInvoker"/> + gerçek
/// <see cref="RunCoordinator"/>/<see cref="RunLogWriter"/> (RunCoordinatorTests ile aynı desen), gerçek
/// <see cref="SupervisorHost"/> IPC döngüsüyle sürülür — process YOK, in-process duplex stream ile (gerçek stdio
/// pipe'ının davranışını taklit eder: ReadAsync veri gelene kadar bloklar, sleep-poll YOK [D8]).
/// </summary>
public class ProjectLogStreamTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);
    private const string FakeMsBuildExe = @"C:\fake\Bin\MSBuild.exe";
    private static readonly string PlanRoot = Path.Combine(Path.GetTempPath(), "bo-plst-plan");

    private static string Id(string name) => Path.Combine(PlanRoot, name, name + ".csproj");

    private static ProjectNode Node(string name) =>
        new(Id(name), name, Id(name), SolutionNames: [], Dependencies: [], BuildOrder: 0,
            LayerIndex: null, LayerName: null, InCycle: false, WillBuild: null);

    private static RunPlan PlanOf(params ProjectNode[] nodes) =>
        new(new BuildPlan([.. nodes.Select((n, i) => n with { BuildOrder = i })], Cycles: [], Configuration: "Debug"),
            new Dictionary<string, IReadOnlyList<SolutionRef>>(StringComparer.OrdinalIgnoreCase));

    private static StartRunCommand Start(string runId = "r1") => new(runId, RunMode.Rebuild, PlanRoot, "Debug", Parallelism: 1);
    private static MsBuildInvokeResult Ok() => new(ExitCode: 0, DurationMs: 3, TimedOut: false, Killed: false);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    // ---------------------------------------------------------------- sahte invoker

    private sealed class FakeInvoker(
        Func<MsBuildInvokeRequest, Action<string>, CancellationToken, Task<MsBuildInvokeResult>> handler,
        Func<MsBuildRestoreRequest, Action<string>, CancellationToken, Task<MsBuildInvokeResult>>? restoreHandler = null)
        : IMsBuildInvoker
    {
        public Task<MsBuildInvokeResult> InvokeAsync(MsBuildInvokeRequest req, Action<string> onLine, CancellationToken ct) =>
            handler(req, onLine, ct);

        // [optimize] Bu testin senaryosunda restore YOKTUR — çağrılırsa sessizce geçilmez, test kırılır.
        public Task<MsBuildInvokeResult> RestoreAsync(MsBuildRestoreRequest req, Action<string> onLine, CancellationToken ct) =>
            restoreHandler is null ? throw new NotSupportedException("this fake has no restore script") : restoreHandler(req, onLine, ct);
    }

    // ---------------------------------------------------------------- in-process duplex stream (gerçek pipe'ı taklit eder)

    /// <summary>Tek yönlü, kanal-tabanlı async stream: process stdio pipe davranışını taklit eder — ReadAsync veri
    /// gelene kadar (gerçek pipe gibi) bloklar, sleep-poll YOK [D8]. Yalnız Memory-tabanlı async yol kullanılır
    /// (NdjsonReader/NdjsonWriter'ın çağırdığı); sync Read/Write/Seek bu testte hiç çağrılmaz.</summary>
    private sealed class ChannelStream : Stream
    {
        private readonly Channel<byte[]> _ch = Channel.CreateUnbounded<byte[]>();
        private ReadOnlyMemory<byte> _pending;

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_pending.IsEmpty)
            {
                if (!await _ch.Reader.WaitToReadAsync(ct).ConfigureAwait(false)) return 0;
                if (!_ch.Reader.TryRead(out var next)) return 0;
                _pending = next;
            }
            int n = Math.Min(buffer.Length, _pending.Length);
            _pending.Span[..n].CopyTo(buffer.Span);
            _pending = _pending[n..];
            return n;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) =>
            await _ch.Writer.WriteAsync(buffer.ToArray(), ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- harness

    /// <summary>Gerçek SupervisorHost + gerçek RunCoordinator/RunLogWriter'ı, gerçek process olmadan sürer.</summary>
    private sealed class Harness : IDisposable
    {
        private readonly ChannelStream _toHost = new();
        private readonly ChannelStream _fromHost = new();
        private readonly NdjsonWriter _cmdWriter;
        private readonly NdjsonReader _eventReader;
        private readonly Task<int> _hostTask;

        public JobObject Job { get; } = JobObject.CreateKillOnClose();
        public string LogsRoot { get; } = Directory.CreateTempSubdirectory("bo-plst-").FullName;
        public RunCoordinator Coordinator { get; }

        public Harness(RunPlan plan, FakeInvoker invoker)
        {
            var supervisorWriter = new NdjsonWriter(_fromHost); // koordinatör VE host AYNI writer'ı paylaşır (Program.cs deseniyle aynı)
            Coordinator = new RunCoordinator(
                planner: (_, _) => plan,
                msbuildFactory: _ => Task.FromResult(new MsBuildToolset(invoker, FakeMsBuildExe)),
                writer: supervisorWriter,
                innerJob: Job,
                nowMs: () => Environment.TickCount64,
                console: _ => { },
                memoryTrim: NoRealGc.Collection,
                logsRoot: LogsRoot); // [dalga 3 · karar 37 · B1 fix 1 · O-2] Program.cs deseni: kökün TEK girişi — yazıcı fabrikası ondan kurulur
            // [A5/T69] Sync/branch servisleri İZOLE köklerle bağlanır (bu harness onları kullanmaz, ama
            // kullanıcının gerçek cache dosyalarına hiçbir koşulda dokunulmamalıdır).
            // [optimize] Restore fabrikası da çağrılmamalıdır — çağrılırsa test sessizce geçmez.
            var host = new SupervisorHost(supervisorWriter, new NdjsonReader(_toHost), Job, Coordinator,
                WorkspaceServices.Default(LogsRoot,
                    _ => throw new NotSupportedException("bu harness'te restore yok")));
            _cmdWriter = new NdjsonWriter(_toHost);
            _eventReader = new NdjsonReader(_fromHost);
            _hostTask = Task.Run(() => host.RunAsync());
        }

        public Task SendAsync(IpcCommand cmd) => _cmdWriter.WriteAsync(cmd);
        public async Task<IpcEvent> ReadEventAsync() =>
            await _eventReader.ReadAsync<IpcEvent>().WaitAsync(Limit) ?? throw new InvalidOperationException("host stream kapandı");

        public async Task ShutdownAsync()
        {
            await SendAsync(new ShutdownCommand());
            await _hostTask.WaitAsync(Limit);
        }

        public void Dispose() { Coordinator.Dispose(); Job.Dispose(); }
    }

    /// <summary>Bir <c>getProjectLog</c> cevabının chunk'larını SON chunk'a dek okur (araya giren diğer olaylar atlanır).</summary>
    private static async Task<List<ProjectLogChunkEvent>> ReadReplyAsync(Harness h)
    {
        var chunks = new List<ProjectLogChunkEvent>();
        while (chunks.Count == 0 || !chunks[^1].IsLast)
        {
            var e = await h.ReadEventAsync();
            if (e is ProjectLogChunkEvent c) chunks.Add(c);
        }
        return chunks;
    }

    /// <summary>64K'yı aşan bir log için satır sayısı (~200KB → ≥3 chunk, LogChunkerTests'teki desenle aynı).</summary>
    private const int LongLogLineCount = 2000;

    /// <summary>A projesi <see cref="LongLogLineCount"/> satır yazdı ve koşu TAMAMEN bitti: <c>getProjectLog</c> en son koşunun
    /// dizininden okur ve cevap birden çok chunk'tır. Harness'i çağıran dispose eder.</summary>
    private static async Task<Harness> CompletedRunWithALongLogAsync()
    {
        var invoker = new FakeInvoker((_, onLine, _) =>
        {
            for (int i = 0; i < LongLogLineCount; i++) onLine(new string('x', 100));
            return Task.FromResult(Ok());
        });
        var h = new Harness(PlanOf(Node("A")), invoker);
        Assert.IsType<EngineReadyEvent>(await h.ReadEventAsync());
        await h.SendAsync(Start());
        IpcEvent ev;
        do { ev = await h.ReadEventAsync(); } while (ev is not RunCompletedEvent); // run TAMAMEN bitti
        Assert.Equal(RunOutcome.Completed, Assert.IsType<RunCompletedEvent>(ev).Outcome);
        return h;
    }

    // ---------------------------------------------------------------- 1) koşan projenin snapshot'ı + canlı dikiş

    [Fact]
    public async Task Running_project_log_snapshots_atomically_and_subsequent_live_lines_exceed_through_line_number()
    {
        var reachedMidpoint = Signal();
        var releaseRest = Signal();
        var invoker = new FakeInvoker(async (_, onLine, _) =>
        {
            onLine("line-1");
            onLine("line-2");
            reachedMidpoint.TrySetResult(); // bu ana kadar diske senkron yazıldı: commandLine + line-1 + line-2 = 3 satır
            await releaseRest.Task;
            onLine("line-3");
            onLine("line-4");
            return Ok();
        });
        using var h = new Harness(PlanOf(Node("A")), invoker);
        Assert.IsType<EngineReadyEvent>(await h.ReadEventAsync());

        await h.SendAsync(Start());
        await reachedMidpoint.Task.WaitAsync(Limit); // build in-flight, henüz bitmedi

        await h.SendAsync(new GetProjectLogCommand(Id("A")));
        var chunks = await ReadReplyAsync(h);

        // Snapshot atomik: metin ⇔ ThroughLineNumber tutarlı, chunk'ların HEPSİ AYNI değeri taşır (chunk'ı değil
        // snapshot'ı tanımlar) — v7Δ-7: satır 1 gerçek MSBuild komut satırı, sonra line-1/line-2.
        int throughLineNumber = chunks[0].ThroughLineNumber;
        Assert.Equal(3, throughLineNumber);
        Assert.All(chunks, c => Assert.Equal(3, c.ThroughLineNumber));
        string reconstructed = string.Concat(chunks.OrderBy(c => c.Sequence).Select(c => c.Text));
        Assert.Equal(3, RunLogWriter.CountLines(reconstructed));
        Assert.EndsWith("line-2" + Environment.NewLine, reconstructed);

        // Snapshot alındıktan SONRA in-flight build'in ürettiği satırlar (henüz release edilmedi, bu yüzden
        // line-3/line-4'ün onLine'ı hiç çağrılmamıştı — deterministik: yarış YOK) release edilip beklenir.
        releaseRest.SetResult();
        var liveAfterSnapshot = new List<ProjectLogEvent>();
        IpcEvent ev;
        do
        {
            ev = await h.ReadEventAsync();
            if (ev is ProjectLogEvent p && p.ProjectId == Id("A")) liveAfterSnapshot.Add(p);
        } while (ev is not RunCompletedEvent);

        // line-3/line-4 satır no'ları 4 ve 5 — snapshot'ın ThroughLineNumber'ından (3) KESİNLİKLE büyük: App bu
        // olayları dikişte ATAR MI diye bakarken LineNumber <= ThroughLineNumber testiyle elemeyecek.
        var newLines = liveAfterSnapshot.Where(p => p.Text is "line-3" or "line-4").ToList();
        Assert.Equal(2, newLines.Count);
        Assert.All(newLines, p => Assert.True(p.LineNumber > throughLineNumber,
            $"LineNumber {p.LineNumber} > ThroughLineNumber {throughLineNumber} olmalı (dikiş kırılır)"));
        Assert.Equal([4, 5], newLines.OrderBy(p => p.LineNumber).Select(p => p.LineNumber));

        await h.ShutdownAsync();
    }

    // ---------------------------------------------------------------- 2) 64K'yı aşan log + tamamlanmış run'dan disk okuma

    [Fact]
    public async Task Completed_run_log_over_64k_is_served_in_multiple_chunks_sharing_one_through_line_number()
    {
        using var h = await CompletedRunWithALongLogAsync();

        // [T28] Run bitti: RunCoordinator._logs artık null (RunLogWriter Dispose edildi) — bu istek en son run
        // dizininden PATH-tabanlı okumaya düşer (bkz. RunCoordinator.TryGetProjectLogSnapshot). Kart tıklaması
        // run bittikten SONRA da çalışmalı.
        await h.SendAsync(new GetProjectLogCommand(Id("A")));
        var chunks = await ReadReplyAsync(h);

        Assert.True(chunks.Count >= 3);
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.Sequence));
        Assert.All(chunks.SkipLast(1), c => Assert.False(c.IsLast));
        Assert.True(chunks[^1].IsLast);
        int through = chunks[0].ThroughLineNumber;
        Assert.All(chunks, c => Assert.Equal(through, c.ThroughLineNumber)); // TÜM chunk'lar AYNI değeri taşır
        Assert.Equal(LongLogLineCount + 1, through); // +1: v7Δ-7 komut satırı

        string reconstructed = string.Concat(chunks.OrderBy(c => c.Sequence).Select(c => c.Text));
        string diskText = await File.ReadAllTextAsync(DiskPathOf(h, Id("A")));
        Assert.Equal(diskText, reconstructed);

        await h.ShutdownAsync();
    }

    /// <summary>[idle-memory · I1] Cevabın HER chunk'ı isteğin kimliğini yankılar — App cevabı isteğiyle yalnız bundan eşleştirir
    /// (gecikmiş bir cevap yerini yenisine bırakmış bir isteğe aitse yok sayılır). Çok chunk'lı cevapta son chunk'ın taşıması
    /// yetmez: App chunk'ları geldikçe biriktirir ve eşleşmeyen bir chunk'ı atar.</summary>
    [Fact]
    public async Task Every_chunk_of_a_reply_echoes_the_request_id()
    {
        using var h = await CompletedRunWithALongLogAsync();

        await h.SendAsync(new GetProjectLogCommand(Id("A"), RequestId: 7));
        var chunks = await ReadReplyAsync(h);

        Assert.True(chunks.Count >= 3);                     // ön-koşul: çok chunk'lı bir cevap
        Assert.All(chunks, c => Assert.Equal(7, c.RequestId));
        Assert.All(chunks, c => Assert.False(c.NotFound));

        await h.ShutdownAsync();
    }

    private static string DiskPathOf(Harness h, string projectId)
    {
        string runDir = Directory.GetDirectories(h.LogsRoot).Single(); // bu testte tek run koştu
        return Path.Combine(runDir, ProjectLogNaming.FileNameFor(projectId));
    }

    // ---------------------------------------------------------------- 3) bilinmeyen proje

    /// <summary>
    /// [idle-memory · I1] Logu olmayan bir projenin cevabı TEK bir chunk'tır: boş metin, son, <c>Through 0</c>, <c>NotFound</c> —
    /// boş bir snapshot — ve isteğin kimliğini yankılar.
    /// <para>[DEĞİŞEN KURAL] <b>Eski iddia</b> (<c>Unknown_project_log_returns_logNotFound</c>): cevap <c>error(logNotFound)</c>,
    /// mesajı proje kimliği. <b>Neden değişti:</b> hata olayı ne isteği ne (kod olarak) projeyi taşıyordu; App onu bekleyen HER
    /// yüklemeye uyguluyordu — başka bir projenin gecikmiş "log yok"u yeni yüklemeyi bitirip yanlış sayfayı açıyordu — ve dikişi
    /// atlıyordu: istek ile cevap arasında gelen satırlar sayfaya girmiyordu (final review I1, 104). "Log yok" artık bir cevap
    /// chunk'ıdır, App onu isteğiyle eşleştirir ve her cevap gibi diker.</para>
    /// </summary>
    [Fact]
    public async Task Unknown_project_log_is_answered_with_one_empty_not_found_chunk()
    {
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(PlanOf(Node("A")), invoker);
        Assert.IsType<EngineReadyEvent>(await h.ReadEventAsync());

        await h.SendAsync(new GetProjectLogCommand(Id("Yok"), RequestId: 5));
        var reply = Assert.IsType<ProjectLogChunkEvent>(await h.ReadEventAsync());

        // "Log yok" cevabının biçimi TEK yerde tanımlıdır (kopya YASAK): App tarafı testlerin motor modeli ile AYNI.
        Assert.Equal(ProjectLogIpc.NotFound(requestId: 5, Id("Yok")), reply);

        // [final review (group A)] Cevap TEK olaydır: bir ping'in pong'u hemen ardından gelir. İkinci bir cevap olayı
        // (eski error(logNotFound) ile birlikte ya da ikinci bir boş chunk) araya girseydi sıradaki olay pong olmazdı.
        await h.SendAsync(new PingCommand(9));
        Assert.Equal(9, Assert.IsType<PongEvent>(await h.ReadEventAsync()).Seq);

        await h.ShutdownAsync();
    }

    // ---------------------------------------------------------------- 4) [idle-memory · dalga 3 · karar 37] isteğin adını verdiği koşu klasörü

    /// <summary>Başka bir motor sürecinin (uykudan önceki ya da ölmüş motorun) bıraktığı koşu: <paramref name="logsRoot"/> altında üretimin
    /// yazıcısıyla açılmış bir koşu klasörü, A projesinin iki satırlık logu. Yazıcı kapanır — klasör diskte kalır, sınanan motor onu hiç
    /// açmadı. Dönen değer klasörün yoludur: o koşunun <c>runStarted.LogDirectory</c>'si, App'in isteğe koyduğu değer.</summary>
    private static string RunLeftByAnEarlierEngine(string logsRoot)
    {
        using var writer = new RunLogWriter(logsRoot, DateTimeOffset.Now);
        using (var log = writer.OpenProjectLog(Id("A")))
        {
            log.AppendLine("line-1");
            log.AppendLine("line-2");
        }
        return writer.RunDirectory;
    }

    /// <summary>A projesinin <paramref name="runDirectory"/>'deki log dosyası — adı üretimin TEK kuralından (<see cref="ProjectLogNaming"/>).</summary>
    private static string LogOfAIn(string runDirectory) => Path.Combine(runDirectory, ProjectLogNaming.FileNameFor(Id("A")));

    /// <summary>
    /// [karar 37 · C2] Taze motor — kendi koşusu yok, canlı yazıcısı yok (uykudan uyanmış ya da yeniden başlatılmış süreç) — önceki motorun
    /// koşusundaki bir projenin logunu isteğin adını verdiği klasörden okur: cevap o dosyanın metnidir, "log yok" değildir, isteğin kimliğini
    /// yankılar ve dikiş noktası dosyanın satır sayısıdır. Kaynak sırası canlı yazıcı → motorun son koşusu → isteğin klasörü; burada ilk ikisi
    /// yoktur.
    /// <para>KIRMIZI: motor isteğin klasörünü hiç okumuyordu — tek, boş, <c>NotFound</c> bir chunk (uyandırılan motorda "No log yet").</para>
    /// </summary>
    [Fact]
    public async Task A_fresh_engine_reads_an_earlier_runs_project_log_from_the_folder_the_request_names()
    {
        var invoker = new FakeInvoker((_, _, _) => throw new InvalidOperationException("no run in this test"));
        using var h = new Harness(PlanOf(Node("A")), invoker);
        Assert.IsType<EngineReadyEvent>(await h.ReadEventAsync());
        string runDirectory = RunLeftByAnEarlierEngine(h.LogsRoot);

        await h.SendAsync(new GetProjectLogCommand(Id("A"), RequestId: 4, RunDirectory: runDirectory));
        var chunks = await ReadReplyAsync(h);

        Assert.All(chunks, c => Assert.False(c.NotFound));
        Assert.All(chunks, c => Assert.Equal(4, c.RequestId));
        Assert.All(chunks, c => Assert.Equal(2, c.ThroughLineNumber));
        Assert.Equal(await File.ReadAllTextAsync(LogOfAIn(runDirectory)), string.Concat(chunks.OrderBy(c => c.Sequence).Select(c => c.Text)));

        await h.ShutdownAsync();
    }

    /// <summary>Taze motorun kendi koşu klasörü olmayan yer — her biri gerçek bir klasör ve içinde A'nın logu var: kabul edilseydi okunurdu.</summary>
    public enum ForeignFolder
    {
        /// <summary>Koşu klasörü, ama başka bir log kökünde.</summary>
        OutsideTheLogsRoot,
        /// <summary>Koşu klasörü, kökün altında ama doğrudan altında değil (iç içe).</summary>
        NestedUnderTheLogsRoot,
        /// <summary>Kökün doğrudan altında, ama adı koşu klasörü kalıbına uymuyor.</summary>
        NotARunFolderName,
    }

    /// <summary>
    /// [karar 37] Taze motor isteğin klasörünü yalnız kendi log kökünün koşu klasörlerinden biriyse kabul eder (tam nitelikli, kökün doğrudan
    /// altında, adı koşu klasörü kalıbında — <see cref="RunLogPaths.TryGetRunDirectory"/>); aksi hâlde bugünkü "log yok" cevabıdır: tek, boş,
    /// <c>NotFound</c> bir chunk. Klasörlerin hepsi gerçek ve A'nın logunu taşır — ret kuralın işidir, eksik dosyanın değil.
    /// <para>KIRMIZI (mutasyon: kabul koşulu yokken — klasörü doğrulamadan okuyan geri düşüş): üç durumda da başka yerdeki log okunup
    /// gönderiliyordu.</para>
    /// </summary>
    [Theory]
    [InlineData(ForeignFolder.OutsideTheLogsRoot)]
    [InlineData(ForeignFolder.NestedUnderTheLogsRoot)]
    [InlineData(ForeignFolder.NotARunFolderName)]
    public async Task A_fresh_engine_refuses_a_folder_that_is_not_one_of_its_run_folders(ForeignFolder kind)
    {
        var invoker = new FakeInvoker((_, _, _) => throw new InvalidOperationException("no run in this test"));
        using var h = new Harness(PlanOf(Node("A")), invoker);
        using var otherRoot = new TempDir();
        Assert.IsType<EngineReadyEvent>(await h.ReadEventAsync());
        string folder = kind switch
        {
            ForeignFolder.OutsideTheLogsRoot => RunLeftByAnEarlierEngine(otherRoot.Path),
            ForeignFolder.NestedUnderTheLogsRoot => RunLeftByAnEarlierEngine(Path.Combine(h.LogsRoot, "nested")),
            _ => Renamed(RunLeftByAnEarlierEngine(h.LogsRoot), "not-a-run"),
        };
        Assert.True(File.Exists(LogOfAIn(folder))); // ön-koşul: log orada — kabul edilseydi okunurdu

        await h.SendAsync(new GetProjectLogCommand(Id("A"), RequestId: 6, RunDirectory: folder));
        var reply = Assert.IsType<ProjectLogChunkEvent>(await h.ReadEventAsync());

        Assert.Equal(ProjectLogIpc.NotFound(requestId: 6, Id("A")), reply);
        await h.ShutdownAsync();

        static string Renamed(string directory, string name)
        {
            string target = Path.Combine(Path.GetDirectoryName(directory)!, name);
            Directory.Move(directory, target);
            return target;
        }
    }

    /// <summary>
    /// [karar 37 · B1 fix 1 · m-5] Okunamayan bir proje logu (başka bir süreç dosyayı paylaşımsız tutuyor; ya da klasör okunurken silindi — uyanan
    /// motorun açılıştaki saklama süpürmesi) motoru düşürmez: cevap bugünkü "log yok" chunk'ıdır ve motor komut almaya devam eder (ping → pong).
    /// <para>KIRMIZI: okuma hatası komut döngüsünden yükseliyor, host çıkıyordu — cevap hiç gelmiyordu (okuma zaman aşımı).</para>
    /// </summary>
    [Fact]
    public async Task A_project_log_that_cannot_be_read_is_answered_as_no_log_and_the_engine_keeps_answering()
    {
        var invoker = new FakeInvoker((_, _, _) => throw new InvalidOperationException("no run in this test"));
        using var h = new Harness(PlanOf(Node("A")), invoker);
        Assert.IsType<EngineReadyEvent>(await h.ReadEventAsync());
        string runDirectory = RunLeftByAnEarlierEngine(h.LogsRoot);

        using (new FileStream(LogOfAIn(runDirectory), FileMode.Open, FileAccess.Read, FileShare.None)) // başka bir sürecin kilidi
        {
            await h.SendAsync(new GetProjectLogCommand(Id("A"), RequestId: 9, RunDirectory: runDirectory));
            Assert.Equal(ProjectLogIpc.NotFound(requestId: 9, Id("A")), Assert.IsType<ProjectLogChunkEvent>(await h.ReadEventAsync()));
        }
        await h.SendAsync(new PingCommand(10));
        Assert.Equal(10, Assert.IsType<PongEvent>(await h.ReadEventAsync()).Seq); // motor komut almaya devam ediyor

        await h.ShutdownAsync();
    }

    /// <summary>
    /// [B1 fix 2 · 4a] Proje log isteğini cevaplarken beklenmedik bir hata — burada bozuk bir istek: kimliksiz (<c>projectId</c> yok) ve kabul
    /// edilen bir koşu klasörü; adlandırma kuralı kimliksiz isteği okuyamaz — motoru düşürmez: komutun kendi yakalaması onu bugünkü "log yok"
    /// cevabına çevirir (tek, boş, <c>NotFound</c>, isteğin kimliğini yankılayan bir chunk) ve motor komut almaya devam eder (ping → pong). Öteki
    /// komutların (Sync, checkout, Clean, Optimize) yakalamasıyla aynı kural: istisna IPC sınırını geçmez.
    /// <para>KIRMIZI: hata komut döngüsünden yükseliyor, host çıkıyordu — cevap hiç gelmiyordu (okuma zaman aşımı).</para>
    /// </summary>
    [Fact]
    public async Task A_project_log_request_that_fails_unexpectedly_is_answered_as_no_log_and_the_engine_keeps_answering()
    {
        var invoker = new FakeInvoker((_, _, _) => throw new InvalidOperationException("no run in this test"));
        using var h = new Harness(PlanOf(Node("A")), invoker);
        Assert.IsType<EngineReadyEvent>(await h.ReadEventAsync());
        string runDirectory = RunLeftByAnEarlierEngine(h.LogsRoot);

        await h.SendAsync(new GetProjectLogCommand(ProjectId: null!, RequestId: 11, RunDirectory: runDirectory));
        Assert.Equal(ProjectLogIpc.NotFound(requestId: 11, projectId: null!), Assert.IsType<ProjectLogChunkEvent>(await h.ReadEventAsync()));
        await h.SendAsync(new PingCommand(12));
        Assert.Equal(12, Assert.IsType<PongEvent>(await h.ReadEventAsync()).Seq); // motor komut almaya devam ediyor

        await h.ShutdownAsync();
    }

    /// <summary>
    /// [karar 37 · PİN] Kendi koşusu olan motor isteğin klasörüne BAKMAZ: kaynak sırası canlı yazıcı → motorun son koşusu → isteğin klasörü.
    /// Koşusu tamamen bitmiş motor, isteğin adını verdiği (geçerli, A'nın iki satırlık logunu taşıyan) başka bir koşu klasörü yerine kendi son
    /// koşusunun uzun logunu verir.
    /// <para>PİN: isteğin klasörünü hiç okumayan bugünkü kod da yeşildir; bu test sıranın tersine dönmesini (isteğin klasörünün motorun kendi
    /// koşusunu gölgelemesini) yakalar.</para>
    /// </summary>
    [Fact]
    public async Task An_engine_with_its_own_last_run_answers_from_it_not_from_the_folder_the_request_names()
    {
        using var h = await CompletedRunWithALongLogAsync();
        string earlier = RunLeftByAnEarlierEngine(h.LogsRoot); // kökün doğrudan altında, kabul edilebilir bir klasör

        await h.SendAsync(new GetProjectLogCommand(Id("A"), RequestId: 8, RunDirectory: earlier));
        var chunks = await ReadReplyAsync(h);

        Assert.All(chunks, c => Assert.Equal(LongLogLineCount + 1, c.ThroughLineNumber)); // kendi koşusu (+1: komut satırı), iki satırlık değil
        Assert.All(chunks, c => Assert.False(c.NotFound));

        await h.ShutdownAsync();
    }
}
