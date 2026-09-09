using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Core.Workspace;
using BuildOrchestrator.Tests.Git;

namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// Sync'in harici köklerle birleşimi: harici klasördeki projeler TARANIR, ana taramayla birleşir ve TEK bir
/// grafa girer. Ana bir projenin HintPath'i harici bir projenin DLL'ine denk gelince kenar kendiliğinden
/// oluşur — "önce hariciler" diye bir kural yoktur, sıra graftan gelir.
///
/// <para><b>[DEĞİŞEN KURAL]</b> Bir tur boyunca hariciler topolojinin BAŞINA eklenen, kenarsız, <c>External</c>
/// adlı bir katmanda duran sanal düğümlerdi ve Sync'in sayaçlarına KARIŞMAZLARDI. O tasarım harici projeyi
/// tek bir solution hedefi olarak ele alıyordu; artık sıradan projeler oldukları için sayaçlara da girerler.
/// Sync hâlâ HİÇBİR VCS komutu çalıştırmaz: yalnız dosya sistemini okur.</para>
/// </summary>
public class ExternalSyncIntegrationTests
{
    private const string SlnName = "Osys.sln";

    private static string ProjectXml(string assemblyName, string? hintPathDll = null) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>" + assemblyName
        + "</AssemblyName><TargetFramework>net10.0</TargetFramework></PropertyGroup>"
        + (hintPathDll is null ? "" :
            "<ItemGroup><Reference Include=\"" + Path.GetFileNameWithoutExtension(hintPathDll)
            + "\"><HintPath>..\\lib\\" + hintPathDll + "</HintPath></Reference></ItemGroup>")
        + "</Project>";

    /// <summary>Ana repo: tek bir <c>A</c> projesi; <paramref name="hintPathDll"/> verilirse ona referans verir.</summary>
    private static void WriteWorkspace(GitTestRepo repo, string? hintPathDll = null)
    {
        repo.WriteFile(Path.Combine("src", "A", "A.csproj"), ProjectXml("A", hintPathDll));
        repo.WriteFile(Path.Combine("src", "A", "A.cs"), "public class A { }");
        repo.WriteFile(SlnName,
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"A\", \"src\\A\\A.csproj\", \"{1}\"\nEndProject\n");
    }

    /// <summary>Harici kök: <paramref name="assemblyName"/> adlı tek bir proje, klasör olarak verilir.</summary>
    private static string WriteExternal(string root, string assemblyName)
    {
        string directory = Path.Combine(root, assemblyName);
        Directory.CreateDirectory(directory);
        string csproj = Path.Combine(directory, assemblyName + ".csproj");
        File.WriteAllText(csproj, ProjectXml(assemblyName));
        File.WriteAllText(Path.Combine(directory, assemblyName + ".cs"), "public class " + assemblyName + " { }");
        return csproj;
    }

    private static string NewCacheRoot() => Directory.CreateTempSubdirectory("bo-ext-sync-").FullName;

    private static SyncWorkspaceService ServiceFor(string root, string cacheRoot) =>
        new(new WorkspaceScanner(), new CsprojEvaluator(),
            new EvaluationCache(Path.Combine(cacheRoot, "evaluation-cache.json")),
            new GitService(new ProcessRunner(), root), new BuildStateStore(cacheRoot));

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

    // ---------------------------------------------------------------- tarama ve rozet

    [Fact]
    public async Task Projects_found_under_an_external_folder_become_ordinary_nodes()
    {
        using var main = new GitTestRepo();
        WriteWorkspace(main);
        main.CommitAll("workspace");
        using var external = new TempDir();
        string mail = WriteExternal(external.Path, "Mail");

        var topology = Topology(await RunSyncAsync(main, NewCacheRoot(),
            new ExternalProject(external.Path, VcsKind.Git)));

        var node = Assert.Single(topology.Nodes, n => n.Id == mail);
        Assert.Equal("Mail", node.Name);
        Assert.Equal(VcsKind.Git, node.ExternalVcs);          // rozet: yalnız hangi kaynaktan geldiğini söyler
        Assert.Contains(topology.Nodes, n => n.Name == "A" && n.ExternalVcs is null);
    }

    [Fact]
    public async Task The_source_badge_is_the_one_the_user_picked()
    {
        using var main = new GitTestRepo();
        WriteWorkspace(main);
        main.CommitAll("workspace");
        using var external = new TempDir();
        string ocr = WriteExternal(external.Path, "Ocr");

        var topology = Topology(await RunSyncAsync(main, NewCacheRoot(),
            new ExternalProject(external.Path, VcsKind.Tfvc)));

        Assert.Equal(VcsKind.Tfvc, Assert.Single(topology.Nodes, n => n.Id == ocr).ExternalVcs);
    }

    [Fact]
    public async Task An_external_project_that_a_repository_project_references_becomes_a_real_edge()
    {
        // Kenar primeri HintPath basename → producer eşlemesidir ve producer map artık İKİ kökü birden görür.
        // Bu, "önce hariciler" kuralının yerini alan mekanizmadır: sıra graftan gelir.
        using var main = new GitTestRepo();
        WriteWorkspace(main, hintPathDll: "Mail.dll");
        main.CommitAll("workspace");
        using var external = new TempDir();
        string mail = WriteExternal(external.Path, "Mail");

        var topology = Topology(await RunSyncAsync(main, NewCacheRoot(),
            new ExternalProject(external.Path, VcsKind.Git)));

        var consumer = Assert.Single(topology.Nodes, n => n.Name == "A");
        Assert.Equal([mail], consumer.Dependencies);
        // Bağımlılık sırası topolojiden: üretici tüketiciden ÖNCE.
        Assert.True(topology.Nodes.Single(n => n.Id == mail).BuildOrder < consumer.BuildOrder);
    }

    [Fact]
    public async Task The_build_order_stays_a_dense_sequence()
    {
        // Nodes[i].BuildOrder == i değişmezi topolojiyi okuyan her algoritmanın dayanağıdır.
        using var main = new GitTestRepo();
        WriteWorkspace(main);
        main.CommitAll("workspace");
        using var external = new TempDir();
        WriteExternal(external.Path, "Mail");

        var topology = Topology(await RunSyncAsync(main, NewCacheRoot(),
            new ExternalProject(external.Path, VcsKind.Git)));

        Assert.Equal(Enumerable.Range(0, topology.Nodes.Count), topology.Nodes.Select(n => n.BuildOrder));
    }

    // ---------------------------------------------------------------- incremental karar

    [Fact]
    public async Task An_external_project_gets_a_real_will_build_decision_without_being_in_the_repository()
    {
        // Harici kök ana reponun git ağacında değildir; fingerprint diskteki İÇERİKTEN gelir. Karar yine de
        // gerçektir — hollow kalmaz.
        using var main = new GitTestRepo();
        WriteWorkspace(main);
        main.CommitAll("workspace");
        using var external = new TempDir();
        string mail = WriteExternal(external.Path, "Mail");
        var card = new ExternalProject(external.Path, VcsKind.Git);

        var first = await RunSyncAsync(main, NewCacheRoot(), card);

        Assert.True(Assert.Single(Topology(first).Nodes, n => n.Id == mail).WillBuild);
        Assert.Equal(WillBuildReason.NeverBuilt,
            Assert.Single(first.OfType<BuildPreviewEvent>().Single().Items, i => i.ProjectId == mail).Reason);
    }

    [Fact]
    public async Task Editing_a_file_in_an_external_project_makes_it_stale_again()
    {
        using var main = new GitTestRepo();
        WriteWorkspace(main);
        main.CommitAll("workspace");
        using var external = new TempDir();
        string mail = WriteExternal(external.Path, "Mail");
        var card = new ExternalProject(external.Path, VcsKind.Git);
        string cacheRoot = NewCacheRoot();

        // "Son derlemede" hangi imza yazıldıysa onu deftere koy → proje güncel görünmeli.
        var before = await RunSyncAsync(main, cacheRoot, card);
        string signature = SignatureOf(main, cacheRoot, card, mail);
        new BuildStateStore(cacheRoot).Upsert(new BuildState(mail, signature, LastResult: BuildResult.Succeeded));
        var current = await RunSyncAsync(main, cacheRoot, card);
        Assert.False(Assert.Single(Topology(current).Nodes, n => n.Id == mail).WillBuild);

        // Commit ETMEDEN dosyayı değiştir: imza içerik tabanlıdır, karar değişmeli.
        File.WriteAllText(Path.Combine(external.Path, "Mail", "Mail.cs"), "public class Mail { int x; }");
        var after = await RunSyncAsync(main, cacheRoot, card);

        Assert.True(Assert.Single(Topology(after).Nodes, n => n.Id == mail).WillBuild);
        Assert.NotEmpty(before);
    }

    /// <summary>Bir Sync koşusunun bu proje için hesapladığı imza — deftere yazmak için.</summary>
    private static string SignatureOf(GitTestRepo main, string cacheRoot, ExternalProject card, string projectId)
    {
        var scan = new WorkspaceScanner();
        var evaluator = new CsprojEvaluator();
        var cache = new EvaluationCache(Path.Combine(cacheRoot, "signature-probe.json"));
        var workspace = Core.Externals.ExternalWorkspaceResolver.Resolve(scan.Scan(main.RootPath), [card], scan);
        var plan = new Core.Planning.BuildPlanBuilder(scan, evaluator, cache)
            .Build(workspace.Scan, "Debug", null, workspace.VcsByProjectId);
        var git = new GitService(new ProcessRunner(), main.RootPath);
        var evaluated = workspace.Scan.CsprojPaths
            .Select(p => (Id: Path.GetFullPath(p), Project: cache.GetOrEvaluate(p, evaluator.Evaluate)))
            .Where(x => x.Project is not null)
            .ToDictionary(x => x.Id, x => x.Project!, StringComparer.OrdinalIgnoreCase);

        var (_, signatures) = Core.Incremental.IncrementalRunBinder.Bind(
            plan, evaluated, main.RootPath,
            git.GetHeadCommitAsync().GetAwaiter().GetResult().Value,
            git.GetTrackedBlobHashesAsync().GetAwaiter().GetResult().Value!,
            git.GetDirtyPathsAsync().GetAwaiter().GetResult().Value!,
            new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase),
            inPlace: true, buildCycles: false, DependentMode.Safe,
            workspace.VcsByProjectId.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));

        return signatures[projectId];
    }

    // ---------------------------------------------------------------- uyarılar ve nötr durum

    [Fact]
    public async Task A_card_that_contributes_no_projects_warns_but_the_sync_still_completes()
    {
        using var main = new GitTestRepo();
        WriteWorkspace(main);
        main.CommitAll("workspace");
        var missing = new ExternalProject(Path.Combine(Path.GetTempPath(), "DoganTrend-77aa"), VcsKind.Git);

        var events = await RunSyncAsync(main, NewCacheRoot(), missing);

        Assert.Contains(ProgressLines(events), l =>
            l.Contains("'DoganTrend-77aa'", StringComparison.Ordinal)
            && l.Contains("no projects from it will be built", StringComparison.Ordinal));
        Assert.Single(events.OfType<SyncCompletedEvent>());
        Assert.Empty(events.OfType<ErrorEvent>());
    }

    [Fact]
    public async Task Externals_count_like_any_other_project()
    {
        // [DEĞİŞEN KURAL] Eskiden sayaçlar YALNIZ ana workspace'i anlatırdı; hariciler sıradan proje olduğu
        // için artık onlar da sayılır.
        using var main = new GitTestRepo();
        WriteWorkspace(main);
        main.CommitAll("workspace");
        using var external = new TempDir();
        WriteExternal(external.Path, "Mail");

        var withExternal = await RunSyncAsync(main, NewCacheRoot(), new ExternalProject(external.Path, VcsKind.Git));
        var withoutExternal = await RunSyncAsync(main, NewCacheRoot());

        Assert.Equal(withoutExternal.OfType<SyncCompletedEvent>().Single().ProjectCount + 1,
            withExternal.OfType<SyncCompletedEvent>().Single().ProjectCount);
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
        Assert.All(Topology(withNull).Nodes, n => Assert.Null(n.ExternalVcs));
    }
}
