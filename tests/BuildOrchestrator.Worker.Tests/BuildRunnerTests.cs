using System.Collections.Concurrent;
using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Graph;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Worker.MsBuild;
using BuildOrchestrator.Worker.ProcessControl;

namespace BuildOrchestrator.Worker.Tests;

/// <summary>Captures emitted run events for assertions.</summary>
internal sealed class RecordingSink : IRunEventSink
{
    public ConcurrentQueue<string> Events { get; } = new();
    public List<string> Started { get; } = new();
    public List<string> Succeeded { get; } = new();
    public List<(string Id, string Reason)> Failed { get; } = new();
    public List<string> Skipped { get; } = new();
    public RunSummary? Summary { get; private set; }
    public string? CancelReason { get; private set; }
    private readonly object _gate = new();

    public void RunStarted(string runId, IReadOnlyList<string> plannedProjectIds) => Events.Enqueue("runStarted");
    public void ProjectStarted(string runId, string projectId) { lock (_gate) Started.Add(projectId); }
    public void ProjectLog(string runId, string projectId, string line, bool isError) { }
    public void ProjectSucceeded(string runId, string projectId, long elapsedMs) { lock (_gate) Succeeded.Add(projectId); }
    public void ProjectFailed(string runId, string projectId, string reason, long elapsedMs) { lock (_gate) Failed.Add((projectId, reason)); }
    public void ProjectSkipped(string runId, string projectId, string reason) { lock (_gate) Skipped.Add(projectId); }
    public void RunCompleted(string runId, RunSummary summary) => Summary = summary;
    public void RunCancelled(string runId, string reason) => CancelReason = reason;
}

public class BuildRunnerTests
{
    private static ProjectNode N(string id, params string[] deps)
        => new() { Id = id, Name = id, ProjectPath = id, Dependencies = deps.ToList() };

    private static DependencyGraph OrderedGraph(params ProjectNode[] nodes)
    {
        var g = new DependencyGraph(nodes);
        TopologicalSorter.ApplyBuildOrder(nodes, TopologicalSorter.Sort(g));
        return g;
    }

    private static List<ProjectDecision> BuildAll(IEnumerable<ProjectNode> nodes)
        => nodes.Select(n => new ProjectDecision(n.Id, true, BuildReason.Rebuild)).ToList();

    private static Task PersistNoop(string a, string b, ProjectStatus c, string? d) => Task.CompletedTask;

    [Fact]
    public void ParallelDegree_LightIsSerial()
    {
        Assert.Equal(1, ParallelDegree.For(PerformanceMode.Light));
        Assert.True(ParallelDegree.For(PerformanceMode.FullPower) >= ParallelDegree.For(PerformanceMode.Balanced));
    }

    [Fact]
    public async Task Runner_BuildsDependenciesBeforeDependents()
    {
        var a = N("A");
        var b = N("B", "A");
        var c = N("C", "B");
        var graph = OrderedGraph(a, b, c);
        var completed = new ConcurrentQueue<string>();

        using var job = new JobObject();
        var runner = new BuildRunner(graph, job, async (path, _, _, _, _) =>
        {
            await Task.Delay(10);
            completed.Enqueue(path);
            return new ProjectBuildResult(true, null);
        });

        var sink = new RecordingSink();
        await runner.RunAsync("r1", BuildAll(new[] { a, b, c }), BuildConfiguration.Debug,
            PerformanceMode.FullPower, sink, PersistNoop, CancellationToken.None);

        var order = completed.ToList();
        Assert.True(order.IndexOf("A") < order.IndexOf("B"));
        Assert.True(order.IndexOf("B") < order.IndexOf("C"));
        Assert.Equal(3, sink.Succeeded.Count);
        Assert.Equal(0, sink.Summary!.Failed);
    }

    [Fact]
    public async Task Runner_IndependentProjectsRunConcurrently()
    {
        var nodes = Enumerable.Range(0, 4).Select(i => N($"P{i}")).ToArray();
        var graph = OrderedGraph(nodes);
        var concurrent = 0;
        var maxConcurrent = 0;
        var gate = new object();

        using var job = new JobObject();
        var runner = new BuildRunner(graph, job, async (path, _, _, _, _) =>
        {
            lock (gate) { concurrent++; maxConcurrent = Math.Max(maxConcurrent, concurrent); }
            await Task.Delay(50);
            lock (gate) { concurrent--; }
            return new ProjectBuildResult(true, null);
        });

        await runner.RunAsync("r1", BuildAll(nodes), BuildConfiguration.Debug,
            PerformanceMode.FullPower, new RecordingSink(), PersistNoop, CancellationToken.None);

        Assert.True(maxConcurrent > 1, $"expected concurrency, saw {maxConcurrent}");
    }

    [Fact]
    public async Task Runner_OneFailureDoesNotStopUnrelatedProjects()
    {
        var a = N("A");
        var b = N("B"); // independent of A
        var graph = OrderedGraph(a, b);

        using var job = new JobObject();
        var runner = new BuildRunner(graph, job, (path, _, _, _, _) =>
            Task.FromResult(path == "A"
                ? new ProjectBuildResult(false, "boom")
                : new ProjectBuildResult(true, null)));

        var sink = new RecordingSink();
        await runner.RunAsync("r1", BuildAll(new[] { a, b }), BuildConfiguration.Debug,
            PerformanceMode.FullPower, sink, PersistNoop, CancellationToken.None);

        Assert.Contains("B", sink.Succeeded);
        Assert.Contains(sink.Failed, f => f.Id == "A");
        Assert.Equal(1, sink.Summary!.Failed);
        Assert.Equal(1, sink.Summary.Succeeded);
    }

    [Fact]
    public async Task Runner_DependentOfFailedProject_FailsFastWithoutBuilding()
    {
        var a = N("A");
        var b = N("B", "A");
        var graph = OrderedGraph(a, b);
        var built = new ConcurrentBag<string>();

        using var job = new JobObject();
        var runner = new BuildRunner(graph, job, (path, _, _, _, _) =>
        {
            built.Add(path);
            return Task.FromResult(path == "A"
                ? new ProjectBuildResult(false, "boom")
                : new ProjectBuildResult(true, null));
        });

        var sink = new RecordingSink();
        await runner.RunAsync("r1", BuildAll(new[] { a, b }), BuildConfiguration.Debug,
            PerformanceMode.FullPower, sink, PersistNoop, CancellationToken.None);

        Assert.DoesNotContain("B", built); // B never invoked MSBuild
        Assert.Contains(sink.Failed, f => f.Id == "B" && f.Reason.Contains("upstream"));
    }

    [Fact]
    public async Task Runner_EmitsSkipForNonSelectedProjects()
    {
        var a = N("A");
        var b = N("B");
        var graph = OrderedGraph(a, b);

        using var job = new JobObject();
        var runner = new BuildRunner(graph, job, (_, _, _, _, _) =>
            Task.FromResult(new ProjectBuildResult(true, null)));

        var plan = new List<ProjectDecision>
        {
            new("A", true, BuildReason.Rebuild),
            new("B", false, null)
        };

        var sink = new RecordingSink();
        await runner.RunAsync("r1", plan, BuildConfiguration.Debug,
            PerformanceMode.FullPower, sink, PersistNoop, CancellationToken.None);

        Assert.Contains("B", sink.Skipped);
        Assert.Equal(1, sink.Summary!.Skipped);
    }

    [Fact]
    public async Task Runner_StopCancelsRun()
    {
        var nodes = Enumerable.Range(0, 6).Select(i => N($"P{i}")).ToArray();
        var graph = OrderedGraph(nodes);
        using var cts = new CancellationTokenSource();

        using var job = new JobObject();
        var runner = new BuildRunner(graph, job, async (path, _, _, _, ct) =>
        {
            cts.Cancel(); // trip stop as soon as the first build starts
            await Task.Delay(2000, ct);
            return new ProjectBuildResult(true, null);
        }, gracefulStopTimeout: TimeSpan.FromMilliseconds(100));

        var sink = new RecordingSink();
        await runner.RunAsync("r1", BuildAll(nodes), BuildConfiguration.Debug,
            PerformanceMode.Light, sink, PersistNoop, cts.Token);

        Assert.NotNull(sink.CancelReason);
    }
}
