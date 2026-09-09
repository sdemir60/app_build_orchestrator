using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Externals;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Core.Workspace;
using BuildOrchestrator.Tests.Git;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// [D5/D13] Sync'in harici projelerle birleşimi: hariciler topolojinin ve önizlemenin BAŞINDA görünür, kir
/// uyarı üretir ama Sync'i durdurmaz, ve <b>sayaçlar ile "no changes" anlatısı ana workspace'i anlatmaya
/// devam eder</b> — hariciler onlara karışmaz.
/// </summary>
public class ExternalSyncIntegrationTests
{
    private const string SlnName = "Osys.sln";

    private static void WriteWorkspace(GitTestRepo repo)
    {
        repo.WriteFile(Path.Combine("src", "A", "A.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>A</AssemblyName>"
            + "<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repo.WriteFile(Path.Combine("src", "A", "A.cs"), "public class A { }");
        repo.WriteFile(SlnName,
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"A\", \"src\\A\\A.csproj\", \"{1}\"\nEndProject\n");
    }

    private static string NewCacheRoot() => Directory.CreateTempSubdirectory("bo-ext-sync-").FullName;

    private static SyncWorkspaceService ServiceFor(string root, string cacheRoot) =>
        new(new WorkspaceScanner(), new CsprojEvaluator(),
            new EvaluationCache(Path.Combine(cacheRoot, "evaluation-cache.json")),
            new GitService(new ProcessRunner(), root), new BuildStateStore(cacheRoot),
            new ExternalSyncInspector(new ProcessRunner()));

    /// <summary>Harici repo: tek bir <c>Mail.sln</c> + bir kaynak dosya, tek commit. Döndürülen sha HEAD'dir.</summary>
    private static string SeedExternal(GitTestRepo external)
    {
        external.WriteFile("Mail.cs", "one");
        external.WriteFile("Mail.sln", "");
        return external.CommitAll("mail");
    }

    private static ExternalProject ExternalAt(string directory) => new(directory, VcsKind.Git);

    private static string TargetOf(GitTestRepo external) => Path.Combine(external.RootPath, "Mail.sln");

    private static async Task<List<IpcEvent>> RunSyncAsync(GitTestRepo main, string cacheRoot, params ExternalProject[] externals)
    {
        var events = new List<IpcEvent>();
        await ServiceFor(main.RootPath, cacheRoot).RunAsync(
            new SyncWorkspaceCommand(main.RootPath, "master", ExternalProjects: externals.Length == 0 ? null : externals),
            events.Add);
        return events;
    }

    private static WorkspaceTopologyEvent Topology(List<IpcEvent> events) => events.OfType<WorkspaceTopologyEvent>().Single();

    private static IReadOnlyList<string> ProgressLines(List<IpcEvent> events)
        => events.OfType<SyncProgressEvent>().Select(e => e.Line).ToList();

    [Fact]
    public async Task Externals_lead_the_topology_and_carry_the_external_layer()
    {
        using var main = new GitTestRepo();
        WriteWorkspace(main);
        main.CommitAll("workspace");
        using var external = new GitTestRepo();
        SeedExternal(external);

        var topology = Topology(await RunSyncAsync(main, NewCacheRoot(), ExternalAt(external.RootPath)));

        var first = topology.Nodes[0];
        Assert.Equal("Mail", first.Name);
        Assert.Equal(TargetOf(external), first.Id);
        Assert.Equal(ExternalProjectsConventions.LayerName, first.LayerName);
        Assert.Equal(ExternalProjectsConventions.LayerIndex, first.LayerIndex);
        Assert.Equal(VcsKind.Git, first.ExternalVcs);
        Assert.Contains(topology.Nodes, n => n.Name == "A" && n.ExternalVcs is null);
    }

    [Fact]
    public async Task The_build_order_stays_a_dense_sequence_after_externals_are_prepended()
    {
        // Nodes[i].BuildOrder == i değişmezi topolojiyi okuyan her algoritmanın dayanağıdır.
        using var main = new GitTestRepo();
        WriteWorkspace(main);
        main.CommitAll("workspace");
        using var external = new GitTestRepo();
        SeedExternal(external);

        var topology = Topology(await RunSyncAsync(main, NewCacheRoot(), ExternalAt(external.RootPath)));

        Assert.Equal(Enumerable.Range(0, topology.Nodes.Count), topology.Nodes.Select(n => n.BuildOrder));
    }

    [Fact]
    public async Task The_preview_carries_the_external_decision_and_its_last_revision()
    {
        using var main = new GitTestRepo();
        WriteWorkspace(main);
        main.CommitAll("workspace");
        using var external = new GitTestRepo();
        string head = SeedExternal(external);
        string cacheRoot = NewCacheRoot();
        // Defterde bu revizyondan derlenmiş bir kayıt var → önizleme "up to date" demeli ve sha'yı taşımalı.
        new BuildStateStore(cacheRoot).Upsert(new BuildState(TargetOf(external),
            ExternalSignature.Compute("Debug", VcsKind.Git, head), BuiltCommit: head, LastResult: BuildResult.Succeeded));

        var events = await RunSyncAsync(main, cacheRoot, ExternalAt(external.RootPath));

        var item = events.OfType<BuildPreviewEvent>().Single().Items[0];
        Assert.Equal(TargetOf(external), item.ProjectId);
        Assert.False(item.WillBuild);
        Assert.Equal(WillBuildReason.UpToDate, item.Reason);
        Assert.Equal(head, item.BuiltCommit);
    }

    [Fact]
    public async Task A_dirty_external_warns_but_the_sync_still_completes()
    {
        using var main = new GitTestRepo();
        WriteWorkspace(main);
        main.CommitAll("workspace");
        using var external = new GitTestRepo();
        SeedExternal(external);
        File.WriteAllText(Path.Combine(external.RootPath, "Mail.cs"), "edited");

        var events = await RunSyncAsync(main, NewCacheRoot(), ExternalAt(external.RootPath));

        Assert.Contains(ProgressLines(events), l => l.Contains("has uncommitted changes", StringComparison.Ordinal));
        Assert.Single(events.OfType<SyncCompletedEvent>());
        Assert.Empty(events.OfType<ErrorEvent>());
    }

    [Fact]
    public async Task Counters_keep_describing_the_main_workspace()
    {
        using var main = new GitTestRepo();
        WriteWorkspace(main);
        main.CommitAll("workspace");
        using var external = new GitTestRepo();
        SeedExternal(external);

        var withExternal = await RunSyncAsync(main, NewCacheRoot(), ExternalAt(external.RootPath));
        var withoutExternal = await RunSyncAsync(main, NewCacheRoot());

        var a = withExternal.OfType<SyncCompletedEvent>().Single();
        var b = withoutExternal.OfType<SyncCompletedEvent>().Single();
        Assert.Equal(b.ProjectCount, a.ProjectCount);
        Assert.Equal(b.ToBuildCount, a.ToBuildCount);
        Assert.Equal(b.ChangedCount, a.ChangedCount);
        Assert.Equal(b.UpToDateCount, a.UpToDateCount);
    }

    [Fact]
    public async Task An_empty_external_list_leaves_the_flow_exactly_as_it_was()
    {
        using var main = new GitTestRepo();
        WriteWorkspace(main);
        main.CommitAll("workspace");

        var withNull = await RunSyncAsync(main, NewCacheRoot());
        var withEmpty = new List<IpcEvent>();
        await ServiceFor(main.RootPath, NewCacheRoot()).RunAsync(
            new SyncWorkspaceCommand(main.RootPath, "master", ExternalProjects: []), withEmpty.Add);

        Assert.Equal(ProgressLines(withNull), ProgressLines(withEmpty));
        Assert.Equal(Topology(withNull).Nodes.Count, Topology(withEmpty).Nodes.Count);
    }

    [Fact]
    public async Task An_unresolvable_external_path_warns_and_still_appears_as_a_hollow_row()
    {
        using var main = new GitTestRepo();
        WriteWorkspace(main);
        main.CommitAll("workspace");
        var missing = ExternalAt(Path.Combine(Path.GetTempPath(), "Ocr-77aa"));

        var events = await RunSyncAsync(main, NewCacheRoot(), missing);

        Assert.Contains(ProgressLines(events), l => l.Contains("was not found", StringComparison.Ordinal));
        var node = Topology(events).Nodes[0];
        Assert.Equal("Ocr-77aa", node.Name);
        Assert.Null(node.WillBuild);
    }
}
