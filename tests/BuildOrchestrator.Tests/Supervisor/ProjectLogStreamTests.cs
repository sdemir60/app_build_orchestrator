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

    private sealed class FakeInvoker(Func<MsBuildInvokeRequest, Action<string>, CancellationToken, Task<MsBuildInvokeResult>> handler)
        : IMsBuildInvoker
    {
        public Task<MsBuildInvokeResult> InvokeAsync(MsBuildInvokeRequest req, Action<string> onLine, CancellationToken ct) =>
            handler(req, onLine, ct);
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
                planner: _ => plan,
                msbuildFactory: _ => Task.FromResult(new MsBuildToolset(invoker, FakeMsBuildExe)),
                logFactory: startedAt => new RunLogWriter(LogsRoot, startedAt),
                writer: supervisorWriter,
                innerJob: Job,
                nowMs: () => Environment.TickCount64,
                console: _ => { });
            var host = new SupervisorHost(supervisorWriter, new NdjsonReader(_toHost), Job, Coordinator);
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
        var chunks = new List<ProjectLogChunkEvent>();
        while (chunks.Count == 0 || !chunks[^1].IsLast)
        {
            var e = await h.ReadEventAsync();
            if (e is ProjectLogChunkEvent c) chunks.Add(c);
        }

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
        const int lineCount = 2000; // ~200KB → ≥3 chunk (LogChunkerTests'teki desenle aynı)
        var invoker = new FakeInvoker((_, onLine, _) =>
        {
            for (int i = 0; i < lineCount; i++) onLine(new string('x', 100));
            return Task.FromResult(Ok());
        });
        using var h = new Harness(PlanOf(Node("A")), invoker);
        Assert.IsType<EngineReadyEvent>(await h.ReadEventAsync());

        await h.SendAsync(Start());
        IpcEvent ev;
        do { ev = await h.ReadEventAsync(); } while (ev is not RunCompletedEvent); // run TAMAMEN bitti (resumable değil)
        Assert.Equal(RunOutcome.Completed, Assert.IsType<RunCompletedEvent>(ev).Outcome);

        // [T28] Run bitti: RunCoordinator._logs artık null (RunLogWriter Dispose edildi) — bu istek en son run
        // dizininden PATH-tabanlı okumaya düşer (bkz. RunCoordinator.TryGetProjectLogSnapshot). Kart tıklaması
        // run bittikten SONRA da çalışmalı.
        await h.SendAsync(new GetProjectLogCommand(Id("A")));
        var chunks = new List<ProjectLogChunkEvent>();
        while (chunks.Count == 0 || !chunks[^1].IsLast)
        {
            var e = await h.ReadEventAsync();
            if (e is ProjectLogChunkEvent c) chunks.Add(c);
        }

        Assert.True(chunks.Count >= 3);
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.Sequence));
        Assert.All(chunks.SkipLast(1), c => Assert.False(c.IsLast));
        Assert.True(chunks[^1].IsLast);
        int through = chunks[0].ThroughLineNumber;
        Assert.All(chunks, c => Assert.Equal(through, c.ThroughLineNumber)); // TÜM chunk'lar AYNI değeri taşır
        Assert.Equal(lineCount + 1, through); // +1: v7Δ-7 komut satırı

        string reconstructed = string.Concat(chunks.OrderBy(c => c.Sequence).Select(c => c.Text));
        string diskText = await File.ReadAllTextAsync(DiskPathOf(h, Id("A")));
        Assert.Equal(diskText, reconstructed);

        await h.ShutdownAsync();
    }

    private static string DiskPathOf(Harness h, string projectId)
    {
        string runDir = Directory.GetDirectories(h.LogsRoot).Single(); // bu testte tek run koştu
        return Path.Combine(runDir, ProjectLogNaming.FileNameFor(projectId));
    }

    // ---------------------------------------------------------------- 3) bilinmeyen proje

    [Fact]
    public async Task Unknown_project_log_returns_logNotFound()
    {
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(PlanOf(Node("A")), invoker);
        Assert.IsType<EngineReadyEvent>(await h.ReadEventAsync());

        await h.SendAsync(new GetProjectLogCommand(Id("Yok")));
        var err = Assert.IsType<ErrorEvent>(await h.ReadEventAsync());
        Assert.Equal("logNotFound", err.Code);
        Assert.Equal(Id("Yok"), err.Message);

        await h.ShutdownAsync();
    }
}
