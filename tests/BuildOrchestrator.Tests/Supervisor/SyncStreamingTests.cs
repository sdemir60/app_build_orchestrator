using System.IO;
using System.Text;
using System.Text.Json;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Logs;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Core.Workspace;
using BuildOrchestrator.Supervisor;
using BuildOrchestrator.Tests.Git;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [T69 · Fix wave 1 — Finding 1] <c>syncWorkspace</c>'in event'leri ÜRETİLDİKLERİ ANDA stdout'a düşer.
/// Mevcut <c>SupervisorIpcTests.SyncWorkspace_streams_topology_and_completion…</c> yalnız SON DURUMDAKİ sırayı
/// denetler — event'ler sonda tek seferde yazılsa da geçer. Bu test AYIRT EDİCİDİR: servisin GEÇ bir aşaması
/// (git fetch), testin ERKEN bir event'i (<c>syncStarted</c>) telde görmesine kadar BLOKLANIR (sinyal:
/// <see cref="TaskCompletionSource"/>, <c>Thread.Sleep</c> YOK [D8]). Event'ler tamponlanıyor olsaydı
/// <c>syncStarted</c> hiç yazılmaz, kapı hiç açılmaz ve test kilitlenip zaman aşımına uğrardı.
/// <para>Gerçek process YOK: gerçek <see cref="SupervisorHost"/> + gerçek <see cref="SyncWorkspaceService"/>,
/// stdin bir MemoryStream'den, stdout gözlemlenen bir stream'e (kapatılan bir stdin = EOF = düzenli çıkış).</para>
/// </summary>
public class SyncStreamingTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    /// <summary>Yazılan her baytı biriktiren, yalnız-yazılır stdout taklidi: <see cref="FirstSyncStartedOnWire"/>
    /// <c>syncStarted</c> satırı TELE DÜŞTÜĞÜ anda tamamlanır.</summary>
    private sealed class ObservingStream : Stream
    {
        private readonly StringBuilder _text = new();
        private readonly object _gate = new();

        public TaskCompletionSource FirstSyncStartedOnWire { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Text { get { lock (_gate) return _text.ToString(); } }

        public override bool CanRead => false;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            string all;
            lock (_gate)
            {
                _text.Append(Encoding.UTF8.GetString(buffer.Span));
                all = _text.ToString();
            }
            if (all.Contains("\"syncStarted\"", StringComparison.Ordinal)) FirstSyncStartedOnWire.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Belirli bir git alt-komutunu, verilen kapı açılana kadar BEKLETİR; gerisini gerçek
    /// <see cref="ProcessRunner"/>'a geçirir (gerçek repo, gerçek git — yalnız zamanlama kontrol edilir).</summary>
    private sealed class GatedProcessRunner(string gatedArgument, Task gate) : IProcessRunner
    {
        private readonly ProcessRunner _inner = new();

        public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
        {
            if (spec.Arguments.Contains(gatedArgument, StringComparer.Ordinal)) await gate;
            return await _inner.RunAsync(spec, ct);
        }
    }

    private static void SeedWorkspace(GitTestRepo repo)
    {
        repo.WriteFile(Path.Combine("src", "A", "A.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>A</AssemblyName>"
            + "<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repo.WriteFile(Path.Combine("src", "A", "A.cs"), "public class A { }");
        repo.WriteFile("Osys.sln",
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"A\", \"src\\A\\A.csproj\", \"{1}\"\nEndProject\n");
        repo.CommitAll("c1");
    }

    private static List<IpcEvent> ParseWire(string text) => text
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(l => JsonSerializer.Deserialize<IpcEvent>(l, IpcJson.Options)
                     ?? throw new InvalidOperationException("NDJSON olmayan satır [D4]: " + l))
        .ToList();

    [Fact]
    public async Task Sync_events_reach_stdout_while_the_sync_is_still_running()
    {
        using var repo = new GitTestRepo();
        SeedWorkspace(repo);
        string branch = repo.CurrentBranchName();
        string sandbox = Directory.CreateTempSubdirectory("bo-syncstream-").FullName;

        // Kapı: git fetch, test syncStarted'ı TELDE GÖRENE kadar bloklanır.
        var releaseFetch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gatedRunner = new GatedProcessRunner("fetch", releaseFetch.Task);

        var stdout = new ObservingStream();
        var writer = new NdjsonWriter(stdout);

        var stdin = new MemoryStream();
        await new NdjsonWriter(stdin).WriteAsync(new SyncWorkspaceCommand(repo.RootPath, branch));
        stdin.Position = 0; // komut okunduktan sonra EOF → host düzenli çıkar

        using var job = JobObject.CreateKillOnClose();
        using var coordinator = new RunCoordinator(
            planner: (_, _) => throw new InvalidOperationException("bu testte run yok"),
            msbuildFactory: _ => throw new InvalidOperationException("bu testte run yok"),
            logFactory: startedAt => new RunLogWriter(Path.Combine(sandbox, "logs"), startedAt),
            writer: writer, innerJob: job, nowMs: () => 0, console: _ => { });

        // Sync servisi GERÇEK; yalnız git process runner'ı kapılı. Diğer fabrikalar bu testte kullanılmaz.
        var services = new WorkspaceServices(
            root => new SyncWorkspaceService(
                new WorkspaceScanner(), new CsprojEvaluator(),
                new EvaluationCache(Path.Combine(sandbox, "evaluation-cache.json")),
                new GitService(gatedRunner, root), new BuildStateStore(sandbox)),
            root => new GitService(new ProcessRunner(), root),
            root => new WorktreeManager(new ProcessRunner(), root, Path.Combine(sandbox, "worktrees")));

        var host = new SupervisorHost(writer, new NdjsonReader(stdin), job, coordinator, services);
        var hostTask = Task.Run(() => host.RunAsync());

        // ---- ASIL İDDİA: syncStarted, Sync BİTMEDEN ÖNCE telde. (Tamponlansaydı burada kilitlenirdik.)
        await stdout.FirstSyncStartedOnWire.Task.WaitAsync(Limit);
        string wireDuringSync = stdout.Text;
        Assert.False(hostTask.IsCompleted); // host hâlâ fetch kapısında — Sync KOŞMAYA DEVAM EDİYOR

        var duringSync = ParseWire(wireDuringSync);
        Assert.Contains(duringSync, e => e is SyncStartedEvent);
        Assert.DoesNotContain(duringSync, e => e is SyncCompletedEvent); // "sonda tek seferde" DEĞİL

        releaseFetch.SetResult();
        Assert.Equal(0, await hostTask.WaitAsync(Limit));

        // ---- akış bittiğinde tam sıra + [D4] her satır NDJSON
        var all = ParseWire(stdout.Text);
        Assert.IsType<EngineReadyEvent>(all[0]);
        Assert.IsType<SyncStartedEvent>(all[1]);
        Assert.IsType<SyncCompletedEvent>(all[^1]);
        Assert.Contains(all, e => e is WorkspaceTopologyEvent);
        Assert.Contains(all, e => e is SyncProgressEvent p && p.Line == $"▸ git fetch origin {branch}");
    }
}
