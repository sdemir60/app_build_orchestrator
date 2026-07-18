using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Logs;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;

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
            console: Console.Error.WriteLine);
        var host = new SupervisorHost(writer, new NdjsonReader(stdin), innerJob, coordinator);
        return await host.RunAsync();

        // Planlama TAMAMEN Core'da [D3]: scan → evaluate (cache'li) → graph → topo → BuildPlan.
        RunPlan BuildRunPlan(string root, string configuration)
        {
            // Cache, logsRoot'un yanında durur: `--logs` ile izole edilen bir Supervisor kullanıcının gerçek
            // cache'ini kirletmez.
            string cachePath = Path.Combine(Path.GetDirectoryName(logsRoot) ?? logsRoot, "evaluation-cache.json");
            var scanner = new WorkspaceScanner();
            // [Task 18] TEK tarama: BuildPlanBuilder'ın ScanResult-alan overload'ı kullanılır — packages.config
            // restore'un istediği SolutionDir için .sln YOLLARI (ProjectNode yalnız solution ADI taşır) aynı
            // scan'den (`scan.SlnPaths`) elde edilir, workspace ikinci kez taranmaz.
            var scan = scanner.Scan(root);
            var plan = new BuildPlanBuilder(scanner, new CsprojEvaluator(), new EvaluationCache(cachePath))
                .Build(scan, configuration);
            return new RunPlan(plan, SolutionMapper.MapRefs(scan.SlnPaths, scan.CsprojPaths));
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
