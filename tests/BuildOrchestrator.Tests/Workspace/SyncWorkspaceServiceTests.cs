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
using BuildOrchestrator.Tests.Incremental;
using BuildOrchestrator.Tests.MsBuild;
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
            new GitService(runner ?? new ProcessRunner(), root), new BuildStateStore(cacheRoot),
            new SourceHashCache(Path.Combine(cacheRoot, SourceHashCache.FileName)));

    private static IReadOnlyList<SyncProgressEvent> Progress(List<IpcEvent> events) =>
        events.OfType<SyncProgressEvent>().ToList();

    private static SyncProgressEvent LineStartingWith(List<IpcEvent> events, string prefix) =>
        Assert.Single(Progress(events), e => e.Line.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>
    /// AYIRT EDİCİ — <b>iki proje aynı <c>AssemblyName</c>'i üretiyorsa Sync bunu SÖYLER.</b>
    ///
    /// <para>Belirsiz bir DLL determinizm gereği kenar ÜRETMEZ ([D8/D11], <c>ProducerMap</c>): ona HintPath ile
    /// bağlanan her proje bağımlılığını kaybeder ve yanlış sırada derlenebilir. Bu kayıp bugüne dek hesaplanıp
    /// atılıyordu — kullanıcı grafında eksik bir kenar olduğunu hiçbir yerden göremiyordu. Ölçülen vaka: bir
    /// harici kart, repo kökünde zaten duran bir solution'ın ikinci kopyasını getirdi ve 7 DLL birden belirsiz
    /// oldu.</para>
    ///
    /// <para>Satır çözümü de söylemelidir: hangi DLL, hangi projeler.</para>
    /// </summary>
    [Fact]
    public async Task Two_projects_producing_the_same_assembly_are_reported_because_their_edges_are_dropped()
    {
        using var origin = new GitTestRepo();
        WriteWorkspace(origin);
        // C, A ile AYNI AssemblyName'i üretir → "a.dll" belirsizleşir ve B'nin A'ya olan HintPath kenarı düşer.
        origin.WriteFile(Path.Combine("src", "C", "C.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>A</AssemblyName>"
            + "<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();

        var events = new List<IpcEvent>();
        await ServiceFor(cloneRoot, NewCacheRoot())
            .RunAsync(new SyncWorkspaceCommand(cloneRoot, branch), events.Add, CancellationToken.None);

        var warning = LineStartingWith(events, "warning: 2 projects produce ");
        Assert.Equal("warn", warning.Level);
        Assert.Contains("a.dll", warning.Line, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("src", "A", "A.csproj"), warning.Line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine("src", "C", "C.csproj"), warning.Line, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Çakışma YOKKEN tek bir satır bile yazılmaz — uyarı gürültü olmamalı.</summary>
    [Fact]
    public async Task A_workspace_without_duplicate_assembly_names_says_nothing_about_producers()
    {
        using var origin = new GitTestRepo();
        WriteWorkspace(origin);
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();

        var events = new List<IpcEvent>();
        await ServiceFor(cloneRoot, NewCacheRoot())
            .RunAsync(new SyncWorkspaceCommand(cloneRoot, branch), events.Add, CancellationToken.None);

        Assert.DoesNotContain(Progress(events), e => e.Line.Contains("projects produce", StringComparison.Ordinal));
    }

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

        // §3.1 satır 2 [v1.16.0] — YEREL HEAD + uzak uçtan mesafe.
        //
        // DEĞİŞEN KURAL: satır eskiden "HEAD <hedef sha> — computing osys-state diff" idi. İki sorunu vardı:
        // yazdığı sha FETCH EDİLEN hedefti (kullanıcının derlediği yerel HEAD değil) ve "osys-state diff"
        // kullanıcıya hiçbir şey söylemiyordu. Artık satır yerel HEAD'i ve uzak uçtan KAÇ COMMIT geride
        // olduğunu söyler — alt bardaki "N behind" chip'iyle aynı sayı.
        var headLine = LineStartingWith(events, "HEAD ");
        Assert.Equal($"HEAD {done.TargetSha![..7]} · up to date with origin/{branch}", headLine.Line);
        Assert.Equal("info", headLine.Level);
        Assert.Equal(0, done.Behind);

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
    /// <returns>Binder'ın hesapladığı bugünkü içerik özetleri (<see cref="IncrementalRunBinder.ContentById"/>) —
    /// deftere YAZILMAZ; bir testin <c>BuiltContent</c>'i kendisi kurması içindir.</returns>
    private static async Task<IReadOnlyDictionary<string, string?>> PrimeBuildStateAsUpToDateAsync(
        string root, string cacheRoot)
    {
        var scanner = new WorkspaceScanner();
        var evaluator = new CsprojEvaluator();
        var cache = new EvaluationCache(Path.Combine(cacheRoot, "evaluation-cache.json"));
        var scan = scanner.Scan(root);
        var plan = new BuildPlanBuilder(scanner, evaluator, cache).Build(scan, "Debug", null);

        var git = new GitService(new ProcessRunner(), root);
        string? head = (await git.GetHeadCommitAsync()).Value;
        var evaluatedById = scan.CsprojPaths
            .Select(p => (Id: Path.GetFullPath(p), Project: cache.GetOrEvaluate(p, evaluator.Evaluate)))
            .Where(x => x.Project is not null)
            .ToDictionary(x => x.Id, x => x.Project!, StringComparer.OrdinalIgnoreCase);

        var binder = new IncrementalRunBinder(plan, evaluatedById, root,
            new SourceHashCache(Path.Combine(cacheRoot, SourceHashCache.FileName)));
        var (_, signatures) = binder.Bind(
            new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase), buildCycles: false, DependentMode.Safe);

        var store = new BuildStateStore(cacheRoot);
        foreach (var (projectId, signature) in signatures)
            store.Upsert(new BuildState(projectId, signature, head, BuildResult.Succeeded));
        return binder.ContentById;
    }

    /// <summary>
    /// [v1.16.0] Sync, yerel HEAD'in uzak uçtan kaç commit geride olduğunu ölçer ve hem konsola hem tele
    /// yazar — alt bardaki <c>N behind</c> chip'inin tek kaynağı budur.
    /// </summary>
    [Fact]
    public async Task Sync_reports_how_many_commits_the_local_head_is_behind()
    {
        using var origin = new GitTestRepo();
        WriteWorkspace(origin);
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();

        // Klon alındıktan SONRA uzak uçta iki commit daha: yerel HEAD artık 2 geride.
        origin.WriteFile(Path.Combine("src", "A", "A.cs"), "public class A { int x; }");
        origin.CommitAll("c2");
        origin.WriteFile(Path.Combine("src", "A", "A.cs"), "public class A { int y; }");
        origin.CommitAll("c3");

        var events = new List<IpcEvent>();
        await ServiceFor(cloneRoot, NewCacheRoot()).RunAsync(
            new SyncWorkspaceCommand(cloneRoot, branch), events.Add);

        var done = Assert.IsType<SyncCompletedEvent>(events[^1]);
        Assert.Equal(2, done.Behind);
        Assert.Contains(Progress(events), e => e.Line.EndsWith($"· 2 commits behind origin/{branch}", StringComparison.Ordinal));
    }

    /// <summary>Sync fetch'i ve mesafeyi komutun adlandırdığı branch'e değil, CHECKOUT EDİLMİŞ branch'e göre
    /// yapar: araç yalnız çalışma ağacında derler, ölçülecek tek branch odur.
    /// <para><b>[DEĞİŞEN KURAL — spec 2026-09-18 §6.5]</b> Eski ad/iddia:
    /// <c>No_distance_is_reported_when_the_selected_branch_is_not_the_active_one</c> — komut aktif olmayan bir
    /// branch adlandırırsa o branch fetch edilir ve mesafe ÖLÇÜLMEZ (chip çizilmez). Gerekçe worktree'ydi: seçili
    /// branch çalışma ağacından farklı olabiliyordu. Worktree kalktı; komuttaki ad yalnız detached HEAD'de yedektir
    /// ve bayat bir ad (açılış Sync'inin boş adı, envanterden önceki değer) chip'i yanlışlıkla gizliyordu.</para></summary>
    [Fact]
    public async Task Sync_fetches_the_checked_out_branch_whatever_the_command_names()
    {
        using var origin = new GitTestRepo();
        WriteWorkspace(origin);
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();
        origin.WriteFile(Path.Combine("src", "A", "A.cs"), "public class A { int x; }");
        origin.CommitAll("c2");

        var events = new List<IpcEvent>();
        await ServiceFor(cloneRoot, NewCacheRoot()).RunAsync(
            new SyncWorkspaceCommand(cloneRoot, "some-other-branch"), events.Add);

        Assert.Equal($"git fetch origin {branch}", LineStartingWith(events, "git fetch origin ").Line);
        var done = Assert.IsType<SyncCompletedEvent>(events[^1]);
        Assert.Equal(1, done.Behind);
        Assert.Equal(branch, done.Branch);
    }

    /// <summary>[Task 4 review] Açılış Sync'i henüz envanter gelmeden gider ve komutta boş bir branch adı taşır —
    /// fetch yine checkout edilmiş branch'le yapılır ve mesafe ölçülür (chip açılışta da doğru çıkar).</summary>
    [Fact]
    public async Task A_sync_command_with_an_empty_branch_fetches_the_checked_out_branch_and_measures_behind()
    {
        using var origin = new GitTestRepo();
        WriteWorkspace(origin);
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();
        origin.WriteFile(Path.Combine("src", "A", "A.cs"), "public class A { int x; }");
        origin.CommitAll("c2");
        origin.WriteFile(Path.Combine("src", "A", "A.cs"), "public class A { int y; }");
        origin.CommitAll("c3");

        var events = new List<IpcEvent>();
        await ServiceFor(cloneRoot, NewCacheRoot()).RunAsync(new SyncWorkspaceCommand(cloneRoot, ""), events.Add);

        Assert.Equal($"git fetch origin {branch}", LineStartingWith(events, "git fetch origin ").Line);
        Assert.Equal(2, Assert.IsType<SyncCompletedEvent>(events[^1]).Behind);
    }

    /// <summary>[review M2] Detached HEAD'de izlenen bir branch yoktur: fetch komuttaki yedek adla yine yapılır
    /// ama mesafe ÖLÇÜLMEZ — HEAD o branch'in üzerinde değildir, bayat bir adla sayılan "N behind" yalan olurdu.</summary>
    [Fact]
    public async Task A_detached_head_fetches_the_fallback_but_reports_no_distance()
    {
        using var origin = new GitTestRepo();
        WriteWorkspace(origin);
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();
        GitTestRepo.RunGitAt(cloneRoot, "checkout", "--detach");
        origin.WriteFile(Path.Combine("src", "A", "A.cs"), "public class A { int x; }");
        origin.CommitAll("c2");

        var events = new List<IpcEvent>();
        await ServiceFor(cloneRoot, NewCacheRoot()).RunAsync(new SyncWorkspaceCommand(cloneRoot, branch), events.Add);

        Assert.Equal($"git fetch origin {branch}", LineStartingWith(events, "git fetch origin ").Line);
        var done = Assert.IsType<SyncCompletedEvent>(events[^1]);
        Assert.Null(done.Behind);
        Assert.Null(done.ActiveBranch);
    }

    /// <summary>[spec 2026-09-18 §6.2] Fetch'siz Sync (kendiliğinden Sync, branch değişimi) ağa çıkmaz: fetch
    /// satırı yok, git fetch çağrısı yok; <c>N behind</c> son bilinen uzak uca göre yerelde hesaplanır. Uzak uç,
    /// son fetch'ten SONRA bir commit daha ilerledi — o commit bilinmediği için sayılmaz (2, 3 değil).</summary>
    [Fact]
    public async Task A_sync_without_fetch_runs_no_fetch_and_measures_behind_from_the_known_remote()
    {
        using var origin = new GitTestRepo();
        WriteWorkspace(origin);
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();
        origin.WriteFile(Path.Combine("src", "A", "A.cs"), "public class A { int x; }");
        origin.CommitAll("c2");
        origin.WriteFile(Path.Combine("src", "A", "A.cs"), "public class A { int y; }");
        origin.CommitAll("c3");
        GitTestRepo.RunGitAt(cloneRoot, "fetch", "origin");   // son bilinen uzak uç: c3
        origin.WriteFile(Path.Combine("src", "A", "A.cs"), "public class A { int z; }");
        origin.CommitAll("c4");                               // bilinmeyen: fetch edilmedi

        var recorder = new RecordingProcessRunner();
        var events = new List<IpcEvent>();
        await ServiceFor(cloneRoot, NewCacheRoot(), recorder).RunAsync(
            new SyncWorkspaceCommand(cloneRoot, branch, Fetch: false), events.Add);

        Assert.DoesNotContain(recorder.Calls, call => call.Contains("fetch"));
        Assert.DoesNotContain(Progress(events), e => e.Line.StartsWith("git fetch", StringComparison.Ordinal));
        var done = Assert.IsType<SyncCompletedEvent>(events[^1]);
        Assert.False(done.FetchDegraded);
        Assert.Equal(2, done.Behind);
        Assert.Contains(Progress(events), e => e.Line.EndsWith($"· 2 commits behind origin/{branch}", StringComparison.Ordinal));
        Assert.Single(events.OfType<WorkspaceTopologyEvent>()); // analiz yine tam koşar
    }

    /// <summary>[spec 2026-09-18 §6.1] Tamamlanma olayı yerel HEAD'i ve checkout edilmiş branch'i taşır — App'in
    /// çift Sync kontrolü ve branch değeri buradan okunur.</summary>
    [Fact]
    public async Task The_completed_event_carries_head_and_active_branch()
    {
        using var origin = new GitTestRepo();
        WriteWorkspace(origin);
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();
        string localHead = GitTestRepo.RunGitAt(cloneRoot, "rev-parse", "HEAD").Trim();

        var events = new List<IpcEvent>();
        await ServiceFor(cloneRoot, NewCacheRoot()).RunAsync(new SyncWorkspaceCommand(cloneRoot, branch), events.Add);

        var done = Assert.IsType<SyncCompletedEvent>(events[^1]);
        Assert.Equal(localHead, done.HeadSha);
        Assert.Equal(branch, done.ActiveBranch);
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

    /// <summary>
    /// Senaryo 6 (Sync yüzü): önceki Build'de A patladı, B ona rağmen başarıyla derlendi ve A'yı kök olarak not
    /// etti; kaynak değişmedi. Sync önizlemesi B'yi <c>WaitingForDependency</c> gerekçesi ve kök ADLARIYLA
    /// taşır (etiketin tooltip'i bunları yazar).
    ///
    /// <para><b>[Task 4 — carried item 1]</b> "N to build" sayacı B'yi SAYMAZ: B <c>WillBuild=true</c> olsa da
    /// koşullu (bir sonraki düz Build kökü hâlâ hatalıysa onu atlayabilir) — sayaç yalnız KESİN derlenecek A'yı
    /// sayar. Eski kural (tümünü sayardı) B'yi de katardı.</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL — Task 4 review, C1]</b> Eski iddia <c>Assert.False(b.Conditional)</c> idi,
    /// gerekçesi "Conditional bir KOŞU olgusudur, Sync bir koşu değildir". Doğruydu ama App'in gözünden yanlış
    /// sonuç veriyordu: App'in TEK bildiği hâl bir Build tıklanana kadar Sync'in önizlemesidir, ve o her zaman
    /// <c>false</c> derse dalga/kuyruk/etiket B'yi (Build tıklamasının ANINDA, motorun kendi önizlemesi henüz
    /// gelmeden okunan <c>ScopeFor</c>) kesin derlenecek sanır — bir kare sonra motorun GERÇEK önizlemesi
    /// gelince B griye/soluğa döner. Sync'in <c>WillBuild</c>'i zaten "bir sonraki düz Build ne yapar"ın cevabı
    /// olduğundan (§10.2), <c>Conditional</c> artık AYNI soruyu sorar — B burada <c>true</c> olmalı, tıpkı bir
    /// sonraki düz Build'in kendi önizlemesinde olacağı gibi (bkz. <c>ConditionalRebuildRunTests.
    /// The_preview_marks_a_waiting_project_as_conditional_and_carries_its_root_names</c>).</para>
    /// </summary>
    [Fact]
    public async Task The_preview_carries_the_root_names_of_a_project_waiting_for_a_failed_dependency()
    {
        using var origin = new GitTestRepo();
        WriteWorkspace(origin);
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();
        string cacheRoot = NewCacheRoot();

        await PrimeBuildStateAsUpToDateAsync(cloneRoot, cacheRoot);
        string idA = Path.Combine(cloneRoot, "src", "A", "A.csproj");
        string idB = Path.Combine(cloneRoot, "src", "B", "B.csproj");
        var store = new BuildStateStore(cacheRoot);
        var primed = store.Load();
        // [DEĞİŞEN KURAL — spec 2026-09-18 §1-14] Eski iddia: LastResult=Failed TEK BAŞINA A'yı kırmızı
        // ("LastFailed") gösterirdi, hangi imzada patladığı önemsizdi. Artık kırmızı KANITLIDIR: hata anındaki
        // imza (FailedSignature) deftere yazılır ve bugünkü imzayla eşleşmedikçe gerekçe NeverBuilt'e düşer.
        // A burada kendi ANINDAki (primed) imzasında patlıyor — kaynağı bu Sync'e kadar değişmedi, yani
        // FailedSignature = primed[idA].BuiltSignature bugünkü imzayla AYNI kalır ve kanıt gerçekten geçerlidir.
        store.Upsert(primed[idA] with { LastResult = BuildResult.Failed, FailedSignature = primed[idA].BuiltSignature });
        store.Upsert(primed[idB] with { DepIssue = true, DepIssueRoots = [idA] });

        var events = new List<IpcEvent>();
        await ServiceFor(cloneRoot, cacheRoot)
            .RunAsync(new SyncWorkspaceCommand(cloneRoot, branch), events.Add, CancellationToken.None);

        var preview = Assert.Single(events.OfType<BuildPreviewEvent>());
        var b = Assert.Single(preview.Items, i => i.Name == "B");
        Assert.Equal(WillBuildReason.WaitingForDependency, b.Reason);
        Assert.Equal(["A"], b.DependencyRoots);
        Assert.True(b.Conditional); // DEĞİŞEN KURAL — bkz. üstteki XML yorum
        var a = Assert.Single(preview.Items, i => i.Name == "A");
        Assert.Equal(WillBuildReason.LastFailed, a.Reason);
        Assert.Null(a.DependencyRoots);
        Assert.False(a.Conditional); // A kesin derlenecek — kontrol grubu

        var done = Assert.Single(events.OfType<SyncCompletedEvent>());
        Assert.Equal(1, done.ToBuildCount); // yalnız A — B koşullu, kesin değil
    }

    /// <summary>
    /// [Task 3] Önizlemenin <c>FailedAt</c> alanı <see cref="BuildStateStore.FailedAtOf"/>'un AYNI defterden
    /// okuduğu değeri taşır — <c>BuiltCommit</c>/<c>LastBuiltAt</c> ile aynı desen (W1). A kendi ANINDAki
    /// imzasında patlıyor (kaynağı bu Sync'e kadar değişmedi) yani <c>FailedSignature</c> bugünkü imzayla
    /// AYNI kalır ve kanıt geçerli olur; B hiç patlamadı, <c>FailedAt</c> null kalmalı.
    /// </summary>
    [Fact]
    public async Task The_preview_carries_the_failure_time_from_the_ledger()
    {
        using var origin = new GitTestRepo();
        WriteWorkspace(origin);
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();
        string cacheRoot = NewCacheRoot();

        await PrimeBuildStateAsUpToDateAsync(cloneRoot, cacheRoot);
        string idA = Path.Combine(cloneRoot, "src", "A", "A.csproj");
        var store = new BuildStateStore(cacheRoot);
        var primed = store.Load();
        var failedAt = new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);
        store.Upsert(primed[idA] with
        {
            LastResult = BuildResult.Failed,
            FailedSignature = primed[idA].BuiltSignature,
            FailedAt = failedAt,
        });

        var events = new List<IpcEvent>();
        await ServiceFor(cloneRoot, cacheRoot)
            .RunAsync(new SyncWorkspaceCommand(cloneRoot, branch), events.Add, CancellationToken.None);

        var preview = Assert.Single(events.OfType<BuildPreviewEvent>());
        var a = Assert.Single(preview.Items, i => i.Name == "A");
        var b = Assert.Single(preview.Items, i => i.Name == "B");
        Assert.Equal(failedAt, a.FailedAt);
        Assert.Null(b.FailedAt); // hiç patlamamış → uydurulmaz
    }

    /// <summary>
    /// [Task 3] <c>LocalEdits</c>: sahte git bir tek dirty yol raporlar (<c>src/A/A.cs</c>, A'nın girdi kümesinde)
    /// — yalnız A'nın kartı <c>LocalEdits=true</c> gelmeli, B ETKİLENMEMELİ (karşılaştırma proje BAZINDA,
    /// binder'ın girdi kümesiyle kesişimle yapılır).
    /// </summary>
    [Fact]
    public async Task A_dirty_file_marks_only_the_project_that_owns_it()
    {
        using var origin = new GitTestRepo();
        WriteWorkspace(origin);
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();

        var runner = new PorcelainStubProcessRunner(" M src/A/A.cs\n");
        var events = new List<IpcEvent>();
        await ServiceFor(cloneRoot, NewCacheRoot(), runner)
            .RunAsync(new SyncWorkspaceCommand(cloneRoot, branch), events.Add, CancellationToken.None);

        var preview = Assert.Single(events.OfType<BuildPreviewEvent>());
        var a = Assert.Single(preview.Items, i => i.Name == "A");
        var b = Assert.Single(preview.Items, i => i.Name == "B");
        Assert.True(a.LocalEdits);
        Assert.False(b.LocalEdits);
    }

    /// <summary>
    /// [Task 3] <c>git status --porcelain</c> hata verirse (repo bozuk/erişim yok) sorgu YUTULUR ve HİÇBİR
    /// proje işaretlenmez — belirsiz bir sinyal "temiz" olarak gösterilmez ama "kesin dirty" de UYDURULMAZ.
    /// </summary>
    [Fact]
    public async Task A_failing_dirty_query_marks_nothing()
    {
        using var origin = new GitTestRepo();
        WriteWorkspace(origin);
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();

        var runner = new PorcelainStubProcessRunner(stdout: null, exitCode: 128);
        var events = new List<IpcEvent>();
        await ServiceFor(cloneRoot, NewCacheRoot(), runner)
            .RunAsync(new SyncWorkspaceCommand(cloneRoot, branch), events.Add, CancellationToken.None);

        var preview = Assert.Single(events.OfType<BuildPreviewEvent>());
        Assert.All(preview.Items, i => Assert.False(i.LocalEdits));
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

    /// <summary>
    /// [Faz 3/Task 9 fix round 2 — kullanıcı kararı I1] Defter kipinde önizlemenin <c>OwnFilesChanged</c>'ı
    /// koşu önizlemesiyle AYNI kaynaktan gelir: deftere yazılmış içerik özeti ile bugünkünün karşılaştırması
    /// (<see cref="BuildStateStore.OwnFilesChanged"/>), Fast geçişinden DEĞİL. B'nin kendi dosyalarına
    /// dokunulmadı (<c>BuiltContent</c> bugünküyle aynı) ama kaydındaki imza bayat — Fast geçişi B'yi "değişti"
    /// bulur. Satır <c>affected</c> okumalı (Build'in kendi önizlemesi gibi), <c>modified</c> değil. "N changed"
    /// sayacı ise Fast semantiğinde kalır (ruling R9, ARCHITECTURE §5.3) — B orada sayılır.
    /// </summary>
    [Fact]
    public async Task The_preview_reads_own_files_changed_from_the_content_fingerprint_not_the_fast_pass()
    {
        using var origin = new GitTestRepo();
        WriteWorkspace(origin);
        origin.CommitAll("c1");
        string branch = origin.CurrentBranchName();
        string cloneRoot = origin.CloneFull();
        string cacheRoot = NewCacheRoot();

        var content = await PrimeBuildStateAsUpToDateAsync(cloneRoot, cacheRoot);
        string idB = Path.Combine(cloneRoot, "src", "B", "B.csproj");
        var store = new BuildStateStore(cacheRoot);
        store.Upsert(store.Load()[idB] with { BuiltSignature = "stale-signature", BuiltContent = content[idB] });

        var events = new List<IpcEvent>();
        await ServiceFor(cloneRoot, cacheRoot)
            .RunAsync(new SyncWorkspaceCommand(cloneRoot, branch), events.Add, CancellationToken.None);

        var b = Assert.Single(Assert.Single(events.OfType<BuildPreviewEvent>()).Items, i => i.Name == "B");
        Assert.Equal((true, WillBuildReason.SignatureChanged), (b.WillBuild, b.Reason)); // sanity: B derlenecek
        Assert.False(b.OwnFilesChanged);                                                 // affected, modified DEĞİL
        Assert.Equal(1, Assert.Single(events.OfType<SyncCompletedEvent>()).ChangedCount); // R9: sayaç Fast
    }

    // ---------------------------------------------------------------- [Faz 3/Task 6] dışarıdan derleme kredisi

    /// <summary>Repo'ya <c>src\{ad}</c> altında gerçek legacy class library'ler yazar (<see
    /// cref="LegacyFixture.CreateClassLib"/> — <c>Debug|AnyCPU</c> grubunda <c>bin\Debug\</c>, yani derleme kanıtının
    /// yolu türetilebilir) ve commit'ler.</summary>
    private static void CommitLegacyWorkspace(GitTestRepo repo, params string[] names)
    {
        foreach (string name in names) LegacyFixture.CreateClassLib(Path.Combine(repo.RootPath, "src", name), name);
        repo.CommitAll("legacy");
    }

    /// <summary><c>src\{ad}</c> projesinin elle yazılmış derleme kanıtı (<see cref="LegacyFixture.WriteBuiltOutput"/>).</summary>
    private static string WriteBuiltOutput(GitTestRepo repo, string name) =>
        LegacyFixture.WriteBuiltOutput(Path.Combine(repo.RootPath, "src", name), name);

    /// <summary>Ağa çıkmayan bir Sync (<c>Fetch: false</c>) — bu testlerin konusu kararın kendisidir.</summary>
    private static async Task<List<IpcEvent>> SyncWithoutFetchAsync(GitTestRepo repo, string cacheRoot)
    {
        var events = new List<IpcEvent>();
        await ServiceFor(repo.RootPath, cacheRoot).RunAsync(
            new SyncWorkspaceCommand(repo.RootPath, repo.CurrentBranchName(), Fetch: false),
            events.Add, CancellationToken.None);
        return events;
    }

    /// <summary>
    /// [spec 2026-09-18 §5.2/§5.4, P8] Defterde kaydı olmayan ama derleme kanıtı her girdisinden yeni olan proje
    /// (VS'te derlenmiş) zaman kipindedir: Sync onu <see cref="WillBuildReason.BuiltOutside"/> ile güncel sayar ve
    /// önizleme kanıtın zamanını <c>OutputBuiltAt</c> olarak taşır. Eski karar kaydı olmayanı <c>NeverBuilt</c>
    /// sayıp derletirdi.
    /// </summary>
    [Fact]
    public async Task A_project_built_elsewhere_reads_built_outside_with_its_output_time()
    {
        using var repo = new GitTestRepo();
        CommitLegacyWorkspace(repo, "X");
        EvidenceTimes.Stamp(Path.Combine(repo.RootPath, "src"), [WriteBuiltOutput(repo, "X")]);

        var events = await SyncWithoutFetchAsync(repo, NewCacheRoot());

        var x = Assert.Single(Assert.Single(events.OfType<BuildPreviewEvent>()).Items);
        Assert.Equal((false, WillBuildReason.BuiltOutside), (x.WillBuild, x.Reason));
        Assert.Equal(new DateTimeOffset(EvidenceTimes.EvidenceAt), x.OutputBuiltAt);
        var done = Assert.Single(events.OfType<SyncCompletedEvent>());
        Assert.Equal((0, 1), (done.ToBuildCount, done.UpToDateCount));
    }

    /// <summary>
    /// [karar "Sync'in Fast geçişi" + "modified ↔ affected zaman kipinde"] "N changed" sayacı ve satırın
    /// <c>OwnFilesChanged</c>'ı zaman kipinde kanıttan gelir: dışarıda derlenmiş ve güncel <c>X</c> değişmiş
    /// SAYILMAZ (kaydı yok diye Fast geçişi onu "derlenecek" bulsa da); kendi kaynağı çıktısından yeni <c>Y</c>
    /// değişmiştir (<c>OutputStale</c>, <c>modified</c>). Sayaç ve satır AYNI cevaptan sayılır.
    /// </summary>
    [Fact]
    public async Task A_project_built_elsewhere_is_not_counted_as_changed()
    {
        using var repo = new GitTestRepo();
        CommitLegacyWorkspace(repo, "X", "Y");
        EvidenceTimes.Stamp(Path.Combine(repo.RootPath, "src"),
            [WriteBuiltOutput(repo, "X"), WriteBuiltOutput(repo, "Y")]);
        File.SetLastWriteTimeUtc(Path.Combine(repo.RootPath, "src", "Y", "Class1.cs"), EvidenceTimes.EditedAt);

        var events = await SyncWithoutFetchAsync(repo, NewCacheRoot());

        var preview = Assert.Single(events.OfType<BuildPreviewEvent>());
        var x = Assert.Single(preview.Items, i => i.Name == "X");
        var y = Assert.Single(preview.Items, i => i.Name == "Y");
        Assert.Equal((false, WillBuildReason.BuiltOutside, false), (x.WillBuild, x.Reason, x.OwnFilesChanged));
        Assert.Equal((true, WillBuildReason.OutputStale, true), (y.WillBuild, y.Reason, y.OwnFilesChanged));
        Assert.Null(y.OutputBuiltAt); // yaş yalnız taze kanıtta
        var done = Assert.Single(events.OfType<SyncCompletedEvent>());
        Assert.Equal((1, 1, 1), (done.ChangedCount, done.ToBuildCount, done.UpToDateCount));
        Assert.Equal("Sync complete — 1 changed projects, 1 to build", LineStartingWith(events, "Sync complete — ").Line);
    }

    /// <summary>
    /// [spec 2026-09-18 §5.3] Aracın kendisinin derlediği (kaydı güncel, <c>LastRunAt</c> dolu) ama derleme
    /// kanıtı diskten silinmiş proje defter kipindedir ve <see cref="WillBuildReason.OutputMissing"/> ile
    /// derlenir. Eski karar yalnız imzaya bakıp <c>UpToDate</c> derdi — çıktısı olmayan bir projeyi atlardı.
    /// </summary>
    [Fact]
    public async Task A_recorded_project_whose_output_was_deleted_reads_output_missing()
    {
        using var repo = new GitTestRepo();
        CommitLegacyWorkspace(repo, "X");
        EvidenceTimes.Stamp(Path.Combine(repo.RootPath, "src"), []); // kanıt YOK
        string cacheRoot = NewCacheRoot();
        await PrimeBuildStateAsUpToDateAsync(repo.RootPath, cacheRoot);
        var store = new BuildStateStore(cacheRoot);
        var primed = Assert.Single(store.Load().Values);
        store.Upsert(primed with { LastRunAt = new DateTimeOffset(EvidenceTimes.ToolRunAt) });

        var events = await SyncWithoutFetchAsync(repo, cacheRoot);

        var x = Assert.Single(Assert.Single(events.OfType<BuildPreviewEvent>()).Items);
        Assert.Equal((true, WillBuildReason.OutputMissing), (x.WillBuild, x.Reason));
        Assert.Null(x.OutputBuiltAt);
        Assert.Equal(1, Assert.Single(events.OfType<SyncCompletedEvent>()).ToBuildCount);
    }

    /// <summary>
    /// [spec 2026-09-18 §5.2 "kanıtsız"] Derleme kanıtının yolu türetilemeyen proje (SDK-style) bugünkü kararla
    /// karar verir: diskte ondan yeni bir DLL dursa bile hiç derlenmemiş sayılır (<c>NeverBuilt</c>), yaşı
    /// taşınmaz ve "changed" sayılır. Kanıt yalnız yolu bilinen projelere kredi verir.
    /// <para><b>[DEĞİŞEN KURAL — Task 9 fix round 2, kullanıcı kararı I1]</b> Eski iddia
    /// <c>OwnFilesChanged == true</c> idi (defter kipinde cevap Fast geçişinden geliyordu). Cevap artık koşu
    /// önizlemesiyle aynı kaynaktan, defterdeki içerik özetinden gelir; kaydı olmayan projede özet yoktur ve cevap
    /// <c>null</c>dır (bilinmiyor — etiket zaten <c>never built</c>). "N changed" sayacı Fast semantiğinde kalır
    /// (ruling R9), iki proje orada sayılmaya devam eder.</para>
    /// </summary>
    [Fact]
    public async Task A_project_without_derivable_output_is_decided_as_today()
    {
        using var repo = new GitTestRepo();
        WriteWorkspace(repo);
        repo.CommitAll("c1");
        string dll = Path.Combine(repo.RootPath, "src", "A", "bin", "Debug", "net10.0", "A.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
        File.WriteAllBytes(dll, new byte[16]);
        EvidenceTimes.Stamp(Path.Combine(repo.RootPath, "src"), [dll]);

        var events = await SyncWithoutFetchAsync(repo, NewCacheRoot());

        var preview = Assert.Single(events.OfType<BuildPreviewEvent>());
        Assert.All(preview.Items, i =>
        {
            Assert.Equal((true, WillBuildReason.NeverBuilt, (bool?)null), (i.WillBuild, i.Reason, i.OwnFilesChanged));
            Assert.Null(i.OutputBuiltAt);
        });
        var done = Assert.Single(events.OfType<SyncCompletedEvent>());
        Assert.Equal((2, 2, 0), (done.ChangedCount, done.ToBuildCount, done.UpToDateCount));
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

    /// <summary>[Task 3] Yalnız <c>git status --porcelain</c> çağrısını sahteler (LocalEdits testleri için
    /// deterministik dirty-path çıktısı ya da başarısızlık) — geri kalan HER git çağrısı gerçek <see
    /// cref="ProcessRunner"/>'a geçer (fetch/rev-parse/symbolic-ref gerçek repo'ya karşı çalışmaya devam eder).</summary>
    private sealed class PorcelainStubProcessRunner(string? stdout, int exitCode = 0) : IProcessRunner
    {
        private readonly ProcessRunner _inner = new();

        public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
        {
            // Fixture satırları okunur olsun diye '\n' ile yazılır; GitService '-z' ister ve git o zaman girdileri
            // NUL ile bitirir — stub, gerçek git'in bu argümanlara vereceği biçimi üretir.
            if (spec.Arguments.Contains("status") && spec.Arguments.Contains("--porcelain"))
                return new ProcessResult(exitCode,
                    spec.Arguments.Contains("-z") ? (stdout ?? "").Replace('\n', '\0') : stdout ?? "",
                    exitCode == 0 ? "" : "fake porcelain failure", TimeSpan.Zero, TimedOut: false);
            return await _inner.RunAsync(spec, ct);
        }
    }
}
