using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.Logs;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Core.Workspace;
using BuildOrchestrator.Supervisor;
using Xunit;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [clean] <c>cleanWorkspace</c>'in Supervisor tarafı: dispatch → Core servisi → NDJSON. Gerçek process YOK
/// (gerçek <see cref="SupervisorHost"/> + gerçek <see cref="CleanWorkspaceService"/>, stdin bir MemoryStream,
/// stdout gözlenen bir stream; kapatılan stdin = EOF = düzenli çıkış) — <c>SyncStreamingTests</c>'in deseni.
/// <para>Pinlenenler: event sırası ve diskteki gerçek etki · run uçuştayken <c>cleanRejected</c> (App kapısının
/// ALTINDAKİ ikinci katman) · beklenmeyen bir hatanın IPC sınırını exception olarak GEÇMEMESİ.</para>
/// </summary>
public class CleanDispatchTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bo-cleandispatch-").FullName;
    private readonly string _sandbox = Directory.CreateTempSubdirectory("bo-cleandispatch-sb-").FullName;

    public void Dispose()
    {
        SupervisorHostHarness.TryDeleteDirectories(_root, _sandbox);
        GC.SuppressFinalize(this);
    }

    // ---------------------------------------------------------------- fixture

    private string SeedProject(string name)
    {
        string dir = Path.Combine(_root, "src", name);
        Directory.CreateDirectory(Path.Combine(dir, "bin"));
        Directory.CreateDirectory(Path.Combine(dir, "obj"));
        File.WriteAllText(Path.Combine(dir, name + ".csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        File.WriteAllText(Path.Combine(dir, "bin", name + ".dll"), "output");
        File.WriteAllText(Path.Combine(dir, "obj", name + ".pdb"), "intermediate");
        return dir;
    }

    private WorkspaceServices Services(Func<string, CleanWorkspaceService>? clean = null) => new(
        root => new SyncWorkspaceService(
            new WorkspaceScanner(), new CsprojEvaluator(),
            new EvaluationCache(Path.Combine(_sandbox, "evaluation-cache.json")),
            new GitService(new ProcessRunner(), root), new BuildStateStore(_sandbox),
            new SourceHashCache(Path.Combine(_sandbox, SourceHashCache.FileName))),
        root => new GitService(new ProcessRunner(), root),
        root => new WorktreeManager(new ProcessRunner(), root, Path.Combine(_sandbox, "worktrees")),
        clean ?? (_ => new CleanWorkspaceService(new WorkspaceScanner(), new BuildStateStore(_sandbox))
        {
            DeleteRetryDelay = _ => { }, // [D8] testte gerçek bekleme yok
        }),
        // Bu dosya Clean akışını ölçer; Optimize kurulur ama HİÇ çağrılmaz — restore fabrikası da o yüzden fırlatır.
        _ => new OptimizeWorkspaceService(
            new WorkspaceScanner(), new CsprojEvaluator(),
            new EvaluationCache(Path.Combine(_sandbox, "evaluation-cache.json")),
            new BuildStateStore(_sandbox), new SourceHashCache(Path.Combine(_sandbox, SourceHashCache.FileName)),
            _ => throw new NotSupportedException("no optimize in this test")));


    // ---------------------------------------------------------------- testler

    [Fact]
    public async Task CleanWorkspace_streams_started_progress_and_completion_in_order_and_deletes_bin_obj()
    {
        string a = SeedProject("A");
        var stdout = new SupervisorHostHarness.CollectingStream();
        var writer = new NdjsonWriter(stdout);
        using var job = JobObject.CreateKillOnClose();
        using var coordinator = NewCoordinator(writer, job);

        var host = new SupervisorHost(writer, new NdjsonReader(await SupervisorHostHarness.StdinWith(new CleanWorkspaceCommand(_root))),
            job, coordinator, Services());
        Assert.Equal(0, await Task.Run(() => host.RunAsync()).WaitAsync(SupervisorHostHarness.Limit));

        var all = NdjsonWire.Parse(stdout.Text); // [D4] her satır NDJSON olarak çözülür
        Assert.IsType<EngineReadyEvent>(all[0]);
        Assert.IsType<CleanStartedEvent>(all[1]);
        Assert.IsType<CleanCompletedEvent>(all[^1]);
        Assert.Contains(all, e => e is CleanProgressEvent);

        Assert.False(Directory.Exists(Path.Combine(a, "bin")));
        Assert.False(Directory.Exists(Path.Combine(a, "obj")));
        Assert.True(File.Exists(Path.Combine(a, "A.csproj")));
    }

    // [K-4] App kapısı yarışa açıktır (komut yolda iken run başlayabilir); ikinci katman Supervisor'dadır.
    [Fact]
    public async Task CleanWorkspace_is_rejected_with_cleanRejected_while_a_run_is_active_and_deletes_nothing()
    {
        string a = SeedProject("A");
        var stdout = new SupervisorHostHarness.CollectingStream();
        var writer = new NdjsonWriter(stdout);
        using var job = JobObject.CreateKillOnClose();

        // Planlama, testin sonuna kadar BLOKLANIR → _runActive true kalır (sinyal, Thread.Sleep DEĞİL [D8]).
        var releasePlanning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = new RunCoordinator(
            planner: (_, _) => { releasePlanning.Task.GetAwaiter().GetResult(); throw new InvalidOperationException("plan yok"); },
            msbuildFactory: _ => throw new InvalidOperationException("bu testte msbuild yok"),
            logFactory: startedAt => new RunLogWriter(Path.Combine(_sandbox, "logs"), startedAt),
            writer: writer, innerJob: job, nowMs: () => 0, console: _ => { });

        // startRun SENKRON olarak slotu tutar ve döner; dispatch loop serildir, yani cleanWorkspace okunduğunda
        // run KESİNLİKLE aktiftir — yarış yok.
        var stdin = await SupervisorHostHarness.StdinWith(
            new StartRunCommand("r1", RunMode.Build, _root, "Debug", 2),
            new CleanWorkspaceCommand(_root));
        var host = new SupervisorHost(writer, new NdjsonReader(stdin), job, coordinator, Services());
        Assert.Equal(0, await Task.Run(() => host.RunAsync()).WaitAsync(SupervisorHostHarness.Limit));

        var all = NdjsonWire.Parse(stdout.Text);
        Assert.Contains(all, e => e is ErrorEvent { Code: "cleanRejected" });
        Assert.DoesNotContain(all, e => e is CleanStartedEvent);
        Assert.True(Directory.Exists(Path.Combine(a, "bin")), "reddedilen Clean hiçbir şey silmemeli");
        Assert.True(Directory.Exists(Path.Combine(a, "obj")));

        releasePlanning.SetResult();
    }

    // [K-6] Exception IPC sınırını ASLA geçmez: tanımlı bir hata event'ine dönüşür ve host yaşamaya devam eder.
    [Fact]
    public async Task An_unexpected_clean_exception_becomes_error_cleanFailed_not_a_crash()
    {
        var stdout = new SupervisorHostHarness.CollectingStream();
        var writer = new NdjsonWriter(stdout);
        using var job = JobObject.CreateKillOnClose();
        using var coordinator = NewCoordinator(writer, job);

        var stdin = await SupervisorHostHarness.StdinWith(new CleanWorkspaceCommand(_root), new PingCommand(7));
        var host = new SupervisorHost(writer, new NdjsonReader(stdin), job, coordinator,
            Services(clean: _ => throw new InvalidOperationException("beklenmeyen hata")));
        Assert.Equal(0, await Task.Run(() => host.RunAsync()).WaitAsync(SupervisorHostHarness.Limit));

        var all = NdjsonWire.Parse(stdout.Text);
        Assert.Contains(all, e => e is ErrorEvent { Code: "cleanFailed" });
        Assert.Contains(all, e => e is PongEvent { Seq: 7 }); // host ayakta: sonraki komut hâlâ işleniyor
    }

    private RunCoordinator NewCoordinator(NdjsonWriter writer, JobObject job) => new(
        planner: (_, _) => throw new InvalidOperationException("bu testte run yok"),
        msbuildFactory: _ => throw new InvalidOperationException("bu testte run yok"),
        logFactory: startedAt => new RunLogWriter(Path.Combine(_sandbox, "logs"), startedAt),
        writer: writer, innerJob: job, nowMs: () => 0, console: _ => { });
}
