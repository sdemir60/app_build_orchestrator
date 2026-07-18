using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.Logs;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Core.State;

namespace BuildOrchestrator.Supervisor;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var stdout = Console.OpenStandardOutput();
        var stdin = Console.OpenStandardInput();
        Console.SetOut(Console.Error); // [D4] guard: kaçak Console.WriteLine stderr'e — stdout YALNIZ NDJSON

        string logsRoot = GetArg(args, "--logs") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BuildOrchestrator", "logs");
        Directory.CreateDirectory(logsRoot);

        // Cache + build-state, logsRoot'un YANINDA durur: `--logs` ile izole edilen bir Supervisor kullanıcının
        // gerçek cache/state'ini kirletmez (testler kendi temp logsRoot'unu verir).
        string cacheRoot = Path.GetDirectoryName(logsRoot) ?? logsRoot;
        var stateStore = new BuildStateStore(cacheRoot); // [Task 19] global build-state (projectId anahtarlı)

        using var innerJob = JobObject.CreateKillOnClose(); // §3: inner Job — MSBuild child'ları burada yaşayacak
        // TEK NdjsonWriter: host ve koordinatör AYNI stdout'a yazar; satır bütünlüğü writer'ın kendi kilidiyle
        // korunur — ikinci bir writer örneği o kilidi baypas edip satırları iç içe geçirirdi.
        var writer = new NdjsonWriter(stdout);
        using var coordinator = new RunCoordinator(
            planner: BuildRunPlan,
            msbuildFactory: ct => ResolveMsBuildAsync(innerJob, ct),
            logFactory: startedAt => new RunLogWriter(logsRoot, startedAt),
            writer: writer,
            innerJob: innerJob,
            nowMs: () => Environment.TickCount64, // MONOTONİK — duvar saati geri atlayabilir, elapsed negatife düşerdi
            console: Console.Error.WriteLine,
            stateStore: stateStore); // [Task 19] projectSucceeded → BuildState persist
        var host = new SupervisorHost(writer, new NdjsonReader(stdin), innerJob, coordinator);
        return await host.RunAsync();

        // Planlama TAMAMEN Core'da [D3]: scan → evaluate (cache'li) → graph → topo → BuildPlan → (fresh modda)
        // incremental willBuild + imza. Planlayıcı yalnız fresh (Rebuild/Build) modda çağrılır (Continue/RetryFailed
        // mevcut plan'dan devam eder — bkz. RunCoordinator).
        RunPlan BuildRunPlan(StartRunCommand cmd)
        {
            string cachePath = Path.Combine(cacheRoot, "evaluation-cache.json");
            var scanner = new WorkspaceScanner();
            var evaluator = new CsprojEvaluator();
            var cache = new EvaluationCache(cachePath);
            // [Task 18] TEK tarama: BuildPlanBuilder'ın ScanResult-alan overload'ı kullanılır — packages.config
            // restore'un istediği SolutionDir için .sln YOLLARI (ProjectNode yalnız solution ADI taşır) aynı
            // scan'den (`scan.SlnPaths`) elde edilir, workspace ikinci kez taranmaz.
            var scan = scanner.Scan(cmd.RootPath);
            var plan = new BuildPlanBuilder(scanner, evaluator, cache).Build(scan, cmd.Configuration);
            var solutionRefs = SolutionMapper.MapRefs(scan.SlnPaths, scan.CsprojPaths);

            var (boundPlan, incremental) = ComputeIncremental(cmd, plan, scan, evaluator, cache, stateStore);
            return new RunPlan(boundPlan, solutionRefs, incremental);
        }
    }

    /// <summary>
    /// [Task 19] Fresh (Rebuild/Build) run için incremental karar: her düğüm için <c>WillBuild</c> + byte-stable
    /// <see cref="BuildOrchestrator.Core.Incremental.BuildSignature"/> imzası hesaplanır.
    /// <b>SALT-OKUR git (K1):</b> HEAD/branch/dirty/ls-tree yalnız OKUNUR — checkout/pull/fetch/reset ASLA. Herhangi
    /// bir git/discovery hatası ya da hollow (HEAD yok) → plan AYNEN döner (WillBuild=null) ve <c>Incremental=null</c>:
    /// Build o durumda pre-skip yapmaz (hepsini derler, güvenli taraf). §4: DLL/bin/obj timestamp'ı okunmaz.
    /// </summary>
    private static (BuildPlan Plan, IncrementalPlan? Info) ComputeIncremental(
        StartRunCommand cmd, BuildPlan plan, ScanResult scan, CsprojEvaluator evaluator, EvaluationCache cache,
        BuildStateStore stateStore)
    {
        try
        {
            var git = new GitService(new ProcessRunner(), cmd.RootPath);
            var headResult = git.GetHeadCommitAsync().GetAwaiter().GetResult();
            string? head = headResult.Success ? headResult.Value : null;
            var branchResult = git.GetCurrentBranchAsync().GetAwaiter().GetResult();
            string? branch = branchResult.Success ? branchResult.Value : null;
            var dirtyResult = git.GetDirtyPathsAsync().GetAwaiter().GetResult();
            IReadOnlyList<string> dirty = dirtyResult.Success ? dirtyResult.Value! : [];
            var trackedResult = git.GetTrackedBlobHashesAsync().GetAwaiter().GetResult();
            IReadOnlyDictionary<string, string> tracked = trackedResult.Success
                ? trackedResult.Value! : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // projectId (tam csproj yolu) → EvaluatedProject: cache SICAK (Build zaten değerlendirdi) — bu re-call
            // mtime+size hızlı yolundan bellekten döner, XML yeniden okunmaz.
            var evaluatedById = scan.CsprojPaths.ToDictionary(
                p => Path.GetFullPath(p),
                p => cache.GetOrEvaluate(p, evaluator.Evaluate),
                StringComparer.OrdinalIgnoreCase);

            bool inPlace = !cmd.UseWorktree; // worktree yolu App/Program tarafından HENÜZ hazırlanmıyor → in-place
            var (bound, signatures) = IncrementalRunBinder.Bind(
                plan, evaluatedById, cmd.RootPath, head, tracked, dirty,
                stateStore.Load(), inPlace, cmd.DependentMode);
            return (bound, new IncrementalPlan(signatures, head, branch));
        }
        catch (Exception ex)
        {
            // Incremental bir OPTİMİZASYONDUR: git/discovery/hash yolunda HERHANGİ bir hata (I/O, XML, vb.) tüm
            // run'ı ÖLDÜRMEMELİ. Plan AYNEN döner (WillBuild=null) → Build o durumda pre-skip yapmaz (hepsini
            // derler, güvenli taraf). Tanı için stderr'e bir satır düşülür (stdout YALNIZ NDJSON [D4]).
            Console.Error.WriteLine("incremental pass atlandı (plan aynen, hepsi derlenecek): " + ex);
            return (plan, null);
        }
    }

    // MSBuild çözümü LAZY: vswhere/VS yoksa Supervisor yine ayağa kalkar (ping/getProjectLog çalışır), hata ancak
    // startRun'da error(msbuildNotFound) olarak bildirilir. Tek seferde tek run (A6) → bu lazy init yarışsızdır.
    private static MsBuildToolset? _toolset;

    private static async Task<MsBuildToolset> ResolveMsBuildAsync(JobObject innerJob, CancellationToken ct)
    {
        if (_toolset is not null) return _toolset;
        var location = await new MsBuildResolver(new ProcessRunner()).ResolveAsync(ct: ct);
        // [D10] dotnet build DEĞİL, MSBuild.exe; child'lar JobProcessLauncher ile inner Job içinde doğar.
        // Ham (retry'siz) invoker verilir — retry sarmalaması run'a özgü decision.log'a yazdığı için koordinatörün işi.
        return _toolset = new MsBuildToolset(new MsBuildInvoker(innerJob, location.MsBuildExePath), location.MsBuildExePath);
    }

    private static string? GetArg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
