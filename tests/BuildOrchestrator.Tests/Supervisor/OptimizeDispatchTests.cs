using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Logs;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Core.Workspace;
using BuildOrchestrator.Supervisor;
using Xunit;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [optimize] <c>optimizeWorkspace</c>'in Supervisor tarafı: dispatch → gerçek <see cref="OptimizeWorkspaceService"/>
/// → tek NdjsonWriter. Gerçek <see cref="SupervisorHost"/>, gerçek servis, sahte restore invoker'ı; GERÇEK
/// PROCESS YOK (<see cref="SyncStreamingTests"/>'in host-in-memory kalıbı: stdin bir MemoryStream, stdout
/// gözlemlenen bir stream, kapanan stdin = EOF = düzenli çıkış).
/// </summary>
public class OptimizeDispatchTests
{
    /// <summary>Restore çağrılırsa test bunu görür; build çağrılırsa test KIRILIR (Optimize derleme yapmaz).</summary>
    private sealed class RecordingRestoreInvoker(Func<MsBuildRestoreRequest, MsBuildInvokeResult>? handler = null) : IMsBuildInvoker
    {
        private readonly List<MsBuildRestoreRequest> _requests = [];
        public IReadOnlyList<MsBuildRestoreRequest> Requests { get { lock (_requests) return [.. _requests]; } }

        public Task<MsBuildInvokeResult> InvokeAsync(MsBuildInvokeRequest req, Action<string> onLine, CancellationToken ct)
            => throw new NotSupportedException("Optimize must never build a project");

        public Task<MsBuildInvokeResult> RestoreAsync(MsBuildRestoreRequest req, Action<string> onLine, CancellationToken ct)
        {
            lock (_requests) _requests.Add(req);
            return Task.FromResult(handler?.Invoke(req) ?? new MsBuildInvokeResult(0, 1, false, false));
        }
    }

    // ---------------------------------------------------------------- fixture

    private sealed record Sandbox(string Root, string CacheRoot, string LogsRoot, string PoolRoot);

    private static Sandbox NewSandbox()
    {
        string tmp = Directory.CreateTempSubdirectory("bo-optdispatch-").FullName;
        var box = new Sandbox(Path.Combine(tmp, "repo"), Path.Combine(tmp, "cache"),
            Path.Combine(tmp, "logs"), Path.Combine(tmp, "worktrees"));
        Directory.CreateDirectory(box.Root);
        return box;
    }

    /// <summary>Kök altına packages.config'li, paketi EKSİK bir legacy proje yazar (needy).</summary>
    private static string SeedNeedyProject(Sandbox box, string name = "Needy")
    {
        string dir = Path.Combine(box.Root, name);
        Directory.CreateDirectory(dir);
        string csproj = Path.Combine(dir, name + ".csproj");
        File.WriteAllText(csproj,
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
            + "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">"
            + $"<PropertyGroup><AssemblyName>{name}</AssemblyName><TargetFrameworkVersion>v4.6</TargetFrameworkVersion></PropertyGroup>"
            + "<ItemGroup><Reference Include=\"P\"><HintPath>..\\packages\\P.1.0.0\\lib\\P.dll</HintPath></Reference></ItemGroup>"
            + "</Project>");
        File.WriteAllText(Path.Combine(dir, "packages.config"),
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><packages><package id=\"P\" version=\"1.0.0\" /></packages>");
        return csproj;
    }

    private static WorkspaceServices ServicesWith(Sandbox box, Func<CancellationToken, Task<IMsBuildInvoker>> invokerFactory)
    {
        string evaluationCachePath = Path.Combine(box.CacheRoot, "evaluation-cache.json");
        string sourceHashPath = Path.Combine(box.CacheRoot, SourceHashCache.FileName);

        return new(
            root => new SyncWorkspaceService(
                new WorkspaceScanner(), new CsprojEvaluator(),
                new EvaluationCache(evaluationCachePath),
                new GitService(new ProcessRunner(), root), new BuildStateStore(box.CacheRoot),
                new SourceHashCache(sourceHashPath)),
            root => new GitService(new ProcessRunner(), root),
            root => new WorktreeManager(new ProcessRunner(), root, box.PoolRoot),
            _ => new CleanWorkspaceService(new WorkspaceScanner(), new BuildStateStore(box.CacheRoot)),
            _ => new OptimizeWorkspaceService(
                new WorkspaceScanner(), new CsprojEvaluator(),
                new EvaluationCache(evaluationCachePath), new BuildStateStore(box.CacheRoot),
                new SourceHashCache(sourceHashPath), invokerFactory));
    }

    // ---------------------------------------------------------------- 1) mutlu yol

    [Fact]
    public async Task OptimizeWorkspace_streams_started_progress_and_completion_in_order_on_the_wire()
    {
        var box = NewSandbox();
        SeedNeedyProject(box);
        // Ölü bir cache girdisi: Optimize'ın diskte GERÇEKTEN bir şey yaptığı görülsün.
        string dead = Path.Combine(box.Root, "Gone", "Gone.csproj");
        new BuildStateStore(box.CacheRoot).Upsert(new BuildState(dead, "sig-dead"));

        var invoker = new RecordingRestoreInvoker();
        var stdout = new SupervisorHostHarness.CollectingStream();
        var writer = new NdjsonWriter(stdout);
        var stdin = await SupervisorHostHarness.StdinWith(new OptimizeWorkspaceCommand(box.Root));

        using var job = JobObject.CreateKillOnClose();
        using var coordinator = new RunCoordinator(
            planner: (_, _) => throw new InvalidOperationException("bu testte run yok"),
            msbuildFactory: _ => throw new InvalidOperationException("bu testte run yok"),
            logFactory: startedAt => new RunLogWriter(box.LogsRoot, startedAt),
            writer: writer, innerJob: job, nowMs: () => 0, console: _ => { });

        var host = new SupervisorHost(writer, new NdjsonReader(stdin), job, coordinator,
            ServicesWith(box, _ => Task.FromResult<IMsBuildInvoker>(invoker)));
        Assert.Equal(0, await Task.Run(() => host.RunAsync()).WaitAsync(SupervisorHostHarness.Limit));

        var all = NdjsonWire.Parse(stdout.Text);
        Assert.IsType<EngineReadyEvent>(all[0]);
        Assert.IsType<OptimizeStartedEvent>(all[1]);
        Assert.IsType<OptimizeCompletedEvent>(all[^1]);
        Assert.Contains(all, e => e is OptimizeProgressEvent);
        Assert.Empty(all.OfType<ErrorEvent>());

        Assert.Single(invoker.Requests);                                    // needy proje gerçekten restore edildi
        Assert.False(new BuildStateStore(box.CacheRoot).Load().ContainsKey(dead)); // ölü girdi gerçekten budandı
    }

    // ---------------------------------------------------------------- 2) run uçuşta reddi

    [Fact]
    public async Task OptimizeWorkspace_is_rejected_with_optimizeRejected_while_a_run_is_active_and_changes_nothing()
    {
        var box = NewSandbox();
        SeedNeedyProject(box);
        string dead = Path.Combine(box.Root, "Gone", "Gone.csproj");
        new BuildStateStore(box.CacheRoot).Upsert(new BuildState(dead, "sig-dead"));

        var invoker = new RecordingRestoreInvoker();
        var stdout = new SupervisorHostHarness.CollectingStream();
        var writer = new NdjsonWriter(stdout);
        var stdin = await SupervisorHostHarness.StdinWith(
            new StartRunCommand("run-1", RunMode.Build, box.Root, "Debug", 1),
            new OptimizeWorkspaceCommand(box.Root));

        // Planlama, test bitene kadar BLOKLANIR → run slotu (RunCoordinator._runActive) dolu kalır. Sinyal
        // TaskCompletionSource'tur, sleep-poll YOK [D8].
        var releasePlanner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var job = JobObject.CreateKillOnClose();
        using var coordinator = new RunCoordinator(
            planner: (_, _) => { releasePlanner.Task.GetAwaiter().GetResult(); throw new InvalidOperationException("plan yok"); },
            msbuildFactory: _ => throw new InvalidOperationException("bu testte msbuild yok"),
            logFactory: startedAt => new RunLogWriter(box.LogsRoot, startedAt),
            writer: writer, innerJob: job, nowMs: () => 0, console: _ => { });

        var host = new SupervisorHost(writer, new NdjsonReader(stdin), job, coordinator,
            ServicesWith(box, _ => Task.FromResult<IMsBuildInvoker>(invoker)));
        int exit = await Task.Run(() => host.RunAsync()).WaitAsync(SupervisorHostHarness.Limit);
        releasePlanner.SetResult();
        try { await coordinator.RunCompletion.WaitAsync(SupervisorHostHarness.Limit); } catch (InvalidOperationException) { /* planner kasten patlar */ }

        Assert.Equal(0, exit);
        var all = NdjsonWire.Parse(stdout.Text);
        var rejection = Assert.Single(all.OfType<ErrorEvent>().Where(e => e.Code == "optimizeRejected"));
        Assert.Contains("run", rejection.Message, StringComparison.OrdinalIgnoreCase);

        // Red BİR İŞ YAPMAZ: optimize akışı hiç başlamaz, disk aynen durur.
        Assert.DoesNotContain(all, e => e is OptimizeStartedEvent);
        Assert.DoesNotContain(all, e => e is OptimizeCompletedEvent);
        Assert.Empty(invoker.Requests);
        Assert.True(new BuildStateStore(box.CacheRoot).Load().ContainsKey(dead));
    }

    // ---------------------------------------------------------------- 3) beklenmeyen hata

    [Fact]
    public async Task An_unexpected_optimize_exception_becomes_error_optimizeFailed_not_a_crash()
    {
        var box = NewSandbox();
        SeedNeedyProject(box);

        var stdout = new SupervisorHostHarness.CollectingStream();
        var writer = new NdjsonWriter(stdout);
        // Ping ARKADAN gelir: host'un beklenmeyen hatadan sonra da komut almaya devam ettiği görülsün.
        var stdin = await SupervisorHostHarness.StdinWith(new OptimizeWorkspaceCommand(box.Root), new PingCommand(7));

        using var job = JobObject.CreateKillOnClose();
        using var coordinator = new RunCoordinator(
            planner: (_, _) => throw new InvalidOperationException("bu testte run yok"),
            msbuildFactory: _ => throw new InvalidOperationException("bu testte run yok"),
            logFactory: startedAt => new RunLogWriter(box.LogsRoot, startedAt),
            writer: writer, innerJob: job, nowMs: () => 0, console: _ => { });

        // Toolset fabrikası MsBuildResolveException DEĞİL, tamamen beklenmeyen bir hata fırlatır — servisin
        // kendi K-6 kapısı bunu yutmaz, host'un catch-all'ı tanımlı bir event'e çevirmelidir.
        var host = new SupervisorHost(writer, new NdjsonReader(stdin), job, coordinator,
            ServicesWith(box, _ => throw new InvalidOperationException("boom")));
        Assert.Equal(0, await Task.Run(() => host.RunAsync()).WaitAsync(SupervisorHostHarness.Limit));

        var all = NdjsonWire.Parse(stdout.Text);
        var err = Assert.Single(all.OfType<ErrorEvent>());
        Assert.Equal("optimizeFailed", err.Code);
        Assert.Contains("boom", err.Message, StringComparison.Ordinal);
        Assert.Contains(all, e => e is PongEvent p && p.Seq == 7); // host yaşıyor
    }
}
