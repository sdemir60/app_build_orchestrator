using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Core.Workspace;
using BuildOrchestrator.Tests.Git;
using Xunit;

namespace BuildOrchestrator.Tests.Workspace;

/// <summary>
/// [A5/T69] <see cref="SyncWorkspaceService"/>: Sync'in uçtan uca akışı — ref-only fetch → tarama →
/// plan → will-build pass → <c>workspaceTopology</c> + <c>buildPreview</c> + <c>syncCompleted</c>.
/// Gerçek ephemeral git repo'lar üzerinde (D8 — mock repo yok, sleep-poll yok): bir "origin" repo + ondan
/// tam klon; offline senaryosu remote URL'sini var olmayan bir yola çevirerek deterministik üretilir.
/// <para><b>K1:</b> Sync SALT-OKURDUR — checkout/pull/merge/switch/reset ASLA çağrılmaz, aktif branch ve
/// working tree değişmez (bkz. <see cref="Sync_never_checks_out_pulls_or_resets_the_repository"/>, aynı
/// kanıt deseni <c>SyncFetchTests</c>'ten gelir).</para>
/// </summary>
public class SyncWorkspaceServiceTests
{
    private const string SlnName = "Osys.sln";

    // ---------------------------------------------------------------- fixture

    /// <summary>Repo'ya iki SDK-style proje (B → A ProjectReference) + ikisini içeren bir .sln yazar.</summary>
    private static void WriteWorkspace(GitTestRepo repo)
    {
        repo.WriteFile(Path.Combine("src", "A", "A.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>A</AssemblyName>"
            + "<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        repo.WriteFile(Path.Combine("src", "A", "A.cs"), "public class A { }");
        repo.WriteFile(Path.Combine("src", "B", "B.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>B</AssemblyName>"
            + "<TargetFramework>net10.0</TargetFramework></PropertyGroup>"
            + "<ItemGroup><ProjectReference Include=\"..\\A\\A.csproj\" /></ItemGroup></Project>");
        repo.WriteFile(Path.Combine("src", "B", "B.cs"), "public class B { }");
        repo.WriteFile(SlnName,
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"A\", \"src\\A\\A.csproj\", \"{1}\"\nEndProject\n"
            + "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"B\", \"src\\B\\B.csproj\", \"{2}\"\nEndProject\n");
    }

    /// <summary>İzole bir cache kökü — kullanıcının GERÇEK evaluation-cache/build-state dosyaları ASLA kirletilmez.</summary>
    private static string NewCacheRoot() => Directory.CreateTempSubdirectory("bo-sync-cache-").FullName;

    private static SyncWorkspaceService ServiceFor(string root, string cacheRoot, IProcessRunner? runner = null) =>
        new(new WorkspaceScanner(), new CsprojEvaluator(),
            new EvaluationCache(Path.Combine(cacheRoot, "evaluation-cache.json")),
            new GitService(runner ?? new ProcessRunner(), root), new BuildStateStore(cacheRoot));

    private static IReadOnlyList<SyncProgressEvent> Progress(List<IpcEvent> events) =>
        events.OfType<SyncProgressEvent>().ToList();

    private static SyncProgressEvent LineStartingWith(List<IpcEvent> events, string prefix) =>
        Assert.Single(Progress(events), e => e.Line.StartsWith(prefix, StringComparison.Ordinal));

    // ---------------------------------------------------------------- 1) mutlu yol

    [Fact]
    public async Task Sync_emits_started_then_granular_progress_then_completed_with_topology()
    {
        using var origin = new GitTestRepo();
        WriteWorkspace(origin);
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();
        string cacheRoot = NewCacheRoot();

        var events = new List<IpcEvent>();
        await ServiceFor(cloneRoot, cacheRoot)
            .RunAsync(new SyncWorkspaceCommand(cloneRoot, branch), events.Add, CancellationToken.None);

        var started = Assert.IsType<SyncStartedEvent>(events[0]);
        Assert.Equal(branch, started.Branch);

        // §3.1 satır 1 — cmd tonunda, branch GERÇEK repodan
        var fetchLine = LineStartingWith(events, "git fetch origin ");
        Assert.Equal($"git fetch origin {branch}", fetchLine.Line);
        Assert.Equal("cmd", fetchLine.Level);

        var done = Assert.IsType<SyncCompletedEvent>(events[^1]);
        Assert.False(done.FetchDegraded);
        Assert.NotNull(done.TargetSha);

        // §3.1 satır 2 — SHA sabit örnek DEĞİL, gerçekten çözülen hedef commit'in ilk 7 hanesi
        var headLine = LineStartingWith(events, "HEAD ");
        Assert.Equal($"HEAD {done.TargetSha![..7]} — computing osys-state diff", headLine.Line);
        Assert.Equal("info", headLine.Level);

        // Topoloji: gerçek bağımlılık + solution verisi taşır (D5/D1/E1'in beslendiği kanıt)
        var topology = Assert.Single(events.OfType<WorkspaceTopologyEvent>());
        Assert.Equal(2, topology.Nodes.Count);
        var a = Assert.Single(topology.Nodes, n => n.Name == "A");
        var b = Assert.Single(topology.Nodes, n => n.Name == "B");
        Assert.Empty(a.Dependencies);
        Assert.Equal([a.Id], b.Dependencies);              // B → A kenarı
        Assert.True(a.BuildOrder < b.BuildOrder);          // build-order: bağımlılık önce
        Assert.Empty(topology.Cycles);
        Assert.Equal("Osys", Assert.Single(topology.Solutions).Name);
        Assert.Equal(Path.Combine(cloneRoot, SlnName), Assert.Single(topology.Solutions).Path);

        // Will-build pass GERÇEKTEN koştu: hollow (null) DEĞİL. Hiç derlenmemiş repo → hepsi derlenecek.
        Assert.All(topology.Nodes, n => Assert.True(n.WillBuild));
        var preview = Assert.Single(events.OfType<BuildPreviewEvent>());
        Assert.Equal(topology.Nodes.Select(n => n.Id), preview.Items.Select(i => i.ProjectId));
        Assert.All(preview.Items, i => Assert.True(i.WillBuild));

        Assert.Equal(2, done.ProjectCount);
        Assert.Equal(0, done.CycleCount);
        Assert.Equal(2, done.ChangedCount);   // ikisi de hiç derlenmemiş → doğrudan "changed"
        Assert.Equal(2, done.ToBuildCount);
        Assert.Equal(0, done.UpToDateCount);

        // §3.1 satır 3 + 4 — sayılar syncCompleted'ın SAYAÇLARIYLA aynı kaynaktan
        var completeLine = LineStartingWith(events, "Sync complete — ");
        Assert.Equal("Sync complete — 2 changed projects, 2 to build", completeLine.Line);
        Assert.Equal("info", completeLine.Level);
        var upToDateLine = LineStartingWith(events, "0 projects up to date");
        Assert.Equal("0 projects up to date (will skip)", upToDateLine.Line);
        Assert.Equal("dim", upToDateLine.Level);

        // Sıra: fetch → HEAD → topoloji → "Sync complete" (konsol akışı tasarımın sırasıdır)
        Assert.True(events.IndexOf(fetchLine) < events.IndexOf(headLine));
        Assert.True(events.IndexOf(headLine) < events.IndexOf(topology));
        Assert.True(events.IndexOf(topology) < events.IndexOf(completeLine));
    }

    [Fact]
    public async Task Sync_prints_the_granular_scan_steps_after_the_fetch_line()
    {
        using var origin = new GitTestRepo();
        WriteWorkspace(origin);
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();

        var events = new List<IpcEvent>();
        await ServiceFor(cloneRoot, NewCacheRoot())
            .RunAsync(new SyncWorkspaceCommand(cloneRoot, branch), events.Add, CancellationToken.None);

        // [v7 A5/N1] granular adım satırları fetch satırından SONRA basılır; sayılar gerçek taramadan gelir
        var lines = Progress(events).Select(e => e.Line).ToList();
        int fetchAt = lines.FindIndex(l => l.StartsWith("git fetch origin ", StringComparison.Ordinal));
        int scanAt = lines.IndexOf("Scanning solutions (1)");
        int readAt = lines.IndexOf("Reading HintPath/Compile items (2 projects)");
        int graphAt = lines.IndexOf("Dependency graph — 0 cycles");
        int orderAt = lines.IndexOf("Build order resolved (2)");
        Assert.True(fetchAt >= 0 && scanAt > fetchAt && readAt > scanAt && graphAt > readAt && orderAt > graphAt,
            "N1 tarama satırları fetch satırından sonra ve kendi sıralarında bekleniyor; gelen satırlar: "
            + string.Join(" | ", lines));
    }

    // [Fix wave 1 — Finding 5] Tamamen temiz workspace, design-v1'in AYRI satırını basar
    // (prototype/app/build-data.js:278) — "0 changed projects, 0 to build" + "0 projects up to date (will skip)"
    // DEĞİL: o, en sık görülen kararlı durum için yanlış okunur. Sayı (36) PLACEHOLDER'dır, gerçek veriden gelir.
    [Fact]
    public async Task Sync_prints_the_all_clean_line_when_nothing_has_changed()
    {
        using var origin = new GitTestRepo();
        WriteWorkspace(origin);
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();
        string cacheRoot = NewCacheRoot();

        await PrimeBuildStateAsUpToDateAsync(cloneRoot, cacheRoot);

        var events = new List<IpcEvent>();
        await ServiceFor(cloneRoot, cacheRoot)
            .RunAsync(new SyncWorkspaceCommand(cloneRoot, branch), events.Add, CancellationToken.None);

        // Sayaçlar GERÇEKTEN her şeyin güncel olduğunu söylüyor (satır boş bir kümeden türetilmiyor)
        var done = Assert.IsType<SyncCompletedEvent>(events[^1]);
        Assert.Equal(0, done.ChangedCount);
        Assert.Equal(0, done.ToBuildCount);
        Assert.Equal(2, done.UpToDateCount);
        Assert.All(Assert.Single(events.OfType<WorkspaceTopologyEvent>()).Nodes, n => Assert.False(n.WillBuild));

        var completeLine = LineStartingWith(events, "Sync complete — ");
        Assert.Equal("Sync complete — no changes, 2 projects up to date", completeLine.Line);
        Assert.Equal("info", completeLine.Level);
        // All-clean varyantı TEK satırdır: "(will skip)" satırı bu durumda BASILMAZ
        Assert.DoesNotContain(Progress(events), e => e.Line.EndsWith("(will skip)", StringComparison.Ordinal));
    }

    /// <summary>Her projeyi "en son bu imzayla başarıyla derlendi" diye işaretler — servisin will-build pass'inin
    /// hesapladığı imzanın AYNISI kullanılır (<see cref="IncrementalRunBinder.Bind"/>), böylece sonraki Sync
    /// gerçekten all-clean görür (sayaçlar uydurulmaz).</summary>
    private static async Task PrimeBuildStateAsUpToDateAsync(string root, string cacheRoot)
    {
        var scanner = new WorkspaceScanner();
        var evaluator = new CsprojEvaluator();
        var cache = new EvaluationCache(Path.Combine(cacheRoot, "evaluation-cache.json"));
        var scan = scanner.Scan(root);
        var plan = new BuildPlanBuilder(scanner, evaluator, cache).Build(scan, "Debug", null);

        var git = new GitService(new ProcessRunner(), root);
        string? head = (await git.GetHeadCommitAsync()).Value;
        var tracked = (await git.GetTrackedBlobHashesAsync()).Value!;
        var dirty = (await git.GetDirtyPathsAsync()).Value!;
        var evaluatedById = scan.CsprojPaths
            .Select(p => (Id: Path.GetFullPath(p), Project: cache.GetOrEvaluate(p, evaluator.Evaluate)))
            .Where(x => x.Project is not null)
            .ToDictionary(x => x.Id, x => x.Project!, StringComparer.OrdinalIgnoreCase);

        var (_, signatures) = IncrementalRunBinder.Bind(plan, evaluatedById, root, head, tracked, dirty,
            new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase), inPlace: true, buildCycles: false, mode: DependentMode.Safe);

        var store = new BuildStateStore(cacheRoot);
        foreach (var (projectId, signature) in signatures)
            store.Upsert(new BuildState(projectId, signature, head, BuildResult.Succeeded));
    }

    [Fact]
    public async Task The_preview_carries_the_last_successfully_built_commit_per_project_and_null_when_never_built()
    {
        // [W1] Kartın sha çiftinin SOL yarısı. Kaynak build-state'tir (App o dosyanın yolunu BİLMEZ — cacheRoot
        // yalnız Supervisor tarafında üretilir), bu yüzden değer IPC'den, buildPreview üzerinden geçmek ZORUNDA.
        // A daha önce derlenmiş (kayıt var), B hiç derlenmemiş (kayıt yok) → ikisi ARTIK ayrışır.
        using var origin = new GitTestRepo();
        WriteWorkspace(origin);
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();
        string cacheRoot = NewCacheRoot();

        const string builtCommit = "a3f81c29b4d5e6f708192a3b4c5d6e7f80910a2b"; // 40-hex: sözleşme HAM değer taşır
        string idA = Path.Combine(cloneRoot, "src", "A", "A.csproj");
        // BuiltSignature bilerek BAYAT: A dirty kalsın (sha slotu yalnız dirty satırda görünür) ve sayaçlar kaymasın.
        new BuildStateStore(cacheRoot).Upsert(new BuildState(idA, "stale-signature", builtCommit, BuildResult.Succeeded));

        var events = new List<IpcEvent>();
        await ServiceFor(cloneRoot, cacheRoot)
            .RunAsync(new SyncWorkspaceCommand(cloneRoot, branch), events.Add, CancellationToken.None);

        var preview = Assert.Single(events.OfType<BuildPreviewEvent>());
        var a = Assert.Single(preview.Items, i => i.Name == "A");
        var b = Assert.Single(preview.Items, i => i.Name == "B");
        Assert.Equal(builtCommit, a.BuiltCommit);
        Assert.Null(b.BuiltCommit); // hiç derlenmemiş → uydurulmaz
        Assert.True(a.WillBuild);   // sanity: bayat imza ⇒ satır hâlâ dirty, yani slot GERÇEKTEN görünür

        // Hedef sha AYNI Sync'te ayrıca gelir; ikisi FARKLI ref ailelerinden olduğu için eşit DEĞİLDİR.
        var done = Assert.IsType<SyncCompletedEvent>(events[^1]);
        Assert.NotEqual(builtCommit, done.TargetSha);
    }

    // ---------------------------------------------------------------- 2) offline degrade

    [Fact]
    public async Task Sync_degrades_with_a_warn_line_and_local_head_when_the_remote_is_unreachable()
    {
        using var origin = new GitTestRepo();
        WriteWorkspace(origin);
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();

        // Gerçekten ULAŞILAMAZ bir remote (var olmayan yol) — "kod yolu var mı" değil, fetch GERÇEKTEN başarısız olur.
        string bogusRemote = Path.Combine(Path.GetTempPath(), "bo-sync-nonexistent-" + Guid.NewGuid().ToString("N"));
        GitTestRepo.RunGitAt(cloneRoot, "remote", "set-url", "origin", bogusRemote);
        string localHead = GitTestRepo.RunGitAt(cloneRoot, "rev-parse", "HEAD").Trim();

        var events = new List<IpcEvent>();
        await ServiceFor(cloneRoot, NewCacheRoot())
            .RunAsync(new SyncWorkspaceCommand(cloneRoot, branch), events.Add, CancellationToken.None);

        Assert.Contains(Progress(events), e => e.Level == "warn");

        var done = Assert.IsType<SyncCompletedEvent>(events[^1]);
        Assert.True(done.FetchDegraded);
        Assert.Equal(localHead, done.TargetSha); // K1: hedef yerel HEAD'e düşer

        // Degrade YOLU TOPOLOJİYİ VE ÖNİZLEMEYİ ATLAMAZ — offline'da da kullanılabilir bir Sync üretilir.
        var topology = Assert.Single(events.OfType<WorkspaceTopologyEvent>());
        Assert.Equal(2, topology.Nodes.Count);
        Assert.All(topology.Nodes, n => Assert.True(n.WillBuild)); // will-build pass yerel HEAD'e karşı koştu
        Assert.Equal(2, Assert.Single(events.OfType<BuildPreviewEvent>()).Items.Count);
        Assert.Equal(2, done.ToBuildCount);
    }

    // ---------------------------------------------------------------- 3) K1 — salt-okur

    [Fact]
    public async Task Sync_never_checks_out_pulls_or_resets_the_repository()
    {
        using var origin = new GitTestRepo();
        WriteWorkspace(origin);
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();

        // origin ilerler — Sync'in fetch'i remote-tracking ref'i taşır ama çalışma ağacına DOKUNMAMALIDIR
        origin.WriteFile(Path.Combine("src", "A", "A.cs"), "public class A { public int X; }");
        string newOriginSha = origin.CommitAll("c2");

        string headBefore = GitTestRepo.RunGitAt(cloneRoot, "rev-parse", "HEAD").Trim();
        string fileBefore = File.ReadAllText(Path.Combine(cloneRoot, "src", "A", "A.cs"));
        string branchBefore = GitTestRepo.RunGitAt(cloneRoot, "symbolic-ref", "--short", "-q", "HEAD").Trim();

        var recorder = new RecordingProcessRunner();
        var events = new List<IpcEvent>();
        await ServiceFor(cloneRoot, NewCacheRoot(), recorder)
            .RunAsync(new SyncWorkspaceCommand(cloneRoot, branch), events.Add, CancellationToken.None);

        foreach (var call in recorder.Calls)
        {
            Assert.DoesNotContain("checkout", call);
            Assert.DoesNotContain("pull", call);
            Assert.DoesNotContain("merge", call);
            Assert.DoesNotContain("switch", call);
            Assert.DoesNotContain("reset", call);
        }

        Assert.Equal(headBefore, GitTestRepo.RunGitAt(cloneRoot, "rev-parse", "HEAD").Trim());
        Assert.Equal(fileBefore, File.ReadAllText(Path.Combine(cloneRoot, "src", "A", "A.cs")));
        Assert.Equal(branchBefore, GitTestRepo.RunGitAt(cloneRoot, "symbolic-ref", "--short", "-q", "HEAD").Trim());

        // Fetch GERÇEKTEN iş yaptı (aksi halde "dokunmadı" iddiası boş olurdu): hedef, ilerlemiş remote commit'idir
        var done = Assert.IsType<SyncCompletedEvent>(events[^1]);
        Assert.Equal(newOriginSha, done.TargetSha);
        Assert.NotEqual(headBefore, done.TargetSha);
    }

    // ---------------------------------------------------------------- 4) bozuk girdi

    [Fact]
    public async Task Sync_reports_planFailed_when_the_root_path_does_not_exist()
    {
        string missing = Path.Combine(Path.GetTempPath(), "bo-sync-missing-" + Guid.NewGuid().ToString("N"));

        var events = new List<IpcEvent>();
        await ServiceFor(missing, NewCacheRoot())
            .RunAsync(new SyncWorkspaceCommand(missing, "main"), events.Add, CancellationToken.None);

        var error = Assert.Single(events.OfType<ErrorEvent>());
        Assert.Equal("planFailed", error.Code);
        Assert.Contains(missing, error.Message, StringComparison.Ordinal);
        Assert.Empty(events.OfType<WorkspaceTopologyEvent>());
        Assert.Empty(events.OfType<SyncCompletedEvent>()); // yarım bir "tamamlandı" YAYINLANMAZ
    }

    [Fact]
    public async Task Sync_reports_planFailed_when_the_root_path_is_not_a_git_repository()
    {
        string plainDir = Directory.CreateTempSubdirectory("bo-sync-nogit-").FullName;
        File.WriteAllText(Path.Combine(plainDir, "A.csproj"), "<Project />");

        var events = new List<IpcEvent>();
        await ServiceFor(plainDir, NewCacheRoot())
            .RunAsync(new SyncWorkspaceCommand(plainDir, "main"), events.Add, CancellationToken.None);

        var error = Assert.Single(events.OfType<ErrorEvent>());
        Assert.Equal("planFailed", error.Code);
        Assert.Empty(events.OfType<SyncCompletedEvent>());
    }

    // ---------------------------------------------------------------- yardımcı

    /// <summary>Çalıştırılan git argüman listelerini kaydeder, çağrıyı gerçek <see cref="ProcessRunner"/>'a geçirir (K1 kanıtı).</summary>
    private sealed class RecordingProcessRunner : IProcessRunner
    {
        private readonly ProcessRunner _inner = new();
        private readonly object _gate = new();

        public List<IReadOnlyList<string>> Calls { get; } = [];

        public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
        {
            lock (_gate) Calls.Add(spec.Arguments);
            return await _inner.RunAsync(spec, ct);
        }
    }
}
