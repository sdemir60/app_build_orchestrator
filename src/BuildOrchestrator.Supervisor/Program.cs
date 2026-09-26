using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Externals;
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
        // [spec 2026-09-18 §5.5 · karar 12] Çökme kurtarması host kurulmadan, HİÇBİR komut kabul edilmeden ÖNCE:
        // önceki motor koşu ortasında öldüyse uçuştaki projeler kanıtsız hata olur. Sayı engineReady ile App'e gider.
        var inFlight = new InFlightLedger(cacheRoot);
        int interruptedProjects = RecoverInterruptedRun(inFlight, stateStore);

        // [A13/B4] Test kancaları (bugün yalnız debugSpawnChildren) VARSAYILAN OLARAK KAPALI. Bayrak DEĞER
        // ALMAZ, bu yüzden GetArg'ın (isim + değer) sözleşmesine girmez. App bu bayrağı HİÇ göndermez
        // (EngineHost Supervisor'ı argümansız başlatır) — kapı yalnız Supervisor tarafındadır, çünkü
        // Supervisor kendi başına da başlatılabilir ve App tarafındaki bir kapı orada koruma sağlamazdı.
        bool debugHooks = args.Contains(SupervisorHost.DebugHooksArg);

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
            stateStore: stateStore, // [Task 19] projectSucceeded → BuildState persist
            inFlight: inFlight); // [§5.5] dispatch → Add, sonuç → Remove, koşu çıkışı → Clear
        var host = new SupervisorHost(writer, new NdjsonReader(stdin), innerJob, coordinator,
            // [A5/T69] sync/branch komutları · [optimize] restore invoker'ı koordinatörle AYNI memoize
            // edilmiş toolset çözümünden gelir (ikinci bir vswhere araması yok).
            WorkspaceServices.Default(cacheRoot,
                async ct => (await ResolveMsBuildAsync(innerJob, ct)).Invoker),
            debugHooks, // [A13/B4] kapalıysa debugSpawnChildren error(debugHooksDisabled) ile reddedilir
            interruptedProjects);
        return await host.RunAsync();

        // Planlama TAMAMEN Core'da [D3]: scan → evaluate (cache'li) → graph → topo → BuildPlan → (fresh modda)
        // incremental willBuild + imza. Planlayıcı yalnız fresh (Rebuild/Build) modda çağrılır (Continue/RetryFailed
        // mevcut plan'dan devam eder — bkz. RunCoordinator).
        // [planlama görünürlüğü] `progress` satırları PlanProgressEvent olarak, runStarted'tan ÖNCE App'e gider
        // (bkz. RunCoordinator'ın planner parametresi). Metinler Core'daki PlanProgressLines'tan gelir — Sync'in
        // yazdıklarıyla AYNI kaynak: iki akış aynı işi anlatır ve tek yerden güncellenir (CLAUDE.md kopya yasağı).
        RunPlan BuildRunPlan(StartRunCommand cmd, Action<string> progress)
        {
            // [design v1.14.0 §9] HARİCİ GÜNCELLEME EN ÖNCE: taramadan önce koşar. Taramadan önce olmak
            // ZORUNDA, çünkü bir fast-forward yeni proje dosyaları getirebilir ve tarama onları görmelidir.
            // Hazırlık hatası (kir, ayrışma, eksik tf.exe) ExternalPreparationException fırlatır ve
            // koordinatörün planlama-hatası kanalından planFailed olarak yüzeye çıkar — koşu hiç başlamaz.
            // [D5] Bayrak kapalıysa TEK BİR VCS komutu bile çalışmaz (kir kapısı da yoktur): harici projeler
            // ana repo gibi, oldukları hâliyle derlenir. [D12] Cycles ana reponun SCC onarımıdır — orada
            // kullanıcının çalışma kopyalarını güncellemek sürpriz olurdu; tarama yine de yapılır ki graf
            // Build'inkiyle AYNI kalsın.
            // [tek proje] Kapsamlı koşuda yalnız hedefi içeren çalışma kopyası güncellenir (ExternalUpdater'ın
            // kapsam kapısı) — kapsam dışına dokunulmaz.
            // Güncellenen kopyaların revizyonları koşunun ilerisinde kullanılır: buradaki okuma
            // fast-forward'dan SONRAKİ hâli anlatır.
            IReadOnlyDictionary<string, string> updatedRevisions =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (ExternalUpdater.ShouldUpdate(cmd.Mode, cmd.UpdateExternals, cmd.ExternalProjects))
                updatedRevisions = new ExternalUpdater(new ProcessRunner())
                    .UpdateAsync(cmd.ExternalProjects!, progress, cmd.ScopeProjectId).GetAwaiter().GetResult();

            string cachePath = Path.Combine(cacheRoot, "evaluation-cache.json");
            // [D3] Kaynak özetleri Sync ile AYNI dosyada paylaşılır — iki yüzey aynı içerikleri iki kez okumaz.
            var sourceHashes = new SourceHashCache(Path.Combine(cacheRoot, SourceHashCache.FileName));
            var scanner = new WorkspaceScanner();
            var evaluator = new CsprojEvaluator();
            var cache = new EvaluationCache(cachePath);
            // [Task 18] TEK tarama: BuildPlanBuilder'ın ScanResult-alan overload'ı kullanılır — packages.config
            // restore'un istediği SolutionDir için .sln YOLLARI (ProjectNode yalnız solution ADI taşır) aynı
            // scan'den (`scan.SlnPaths`) elde edilir, workspace ikinci kez taranmaz.
            // [design v1.14.0 §9] Harici kökler ana taramayla BİRLEŞİR — buradan sonrası harici projeleri
            // ayırt etmez: kenarları, sıraları, imzaları ve derlemeleri sıradan projelerinkiyle aynı yoldan
            // gelir. Çözülemeyen bir kart koşuyu DURDURUR (Sync yalnız uyarır): yapılandırılmış bir haricinin
            // sessizce düşmesi, bayat bir DLL'e link'lenmiş yeşil bir build demektir.
            var external = ExternalWorkspaceResolver.Resolve(
                scanner.Scan(cmd.RootPath), cmd.ExternalProjects, scanner, cmd.RootPath);
            if (external.Problems.Count > 0)
            {
                var first = external.Problems[0];
                throw ExternalPreparationException.NotScanned(first.Name, first.Project.Path, first.Problem);
            }

            var scan = external.Scan;
            progress(PlanProgressLines.ScanningSolutions(scan.SlnPaths.Count));
            progress(PlanProgressLines.ReadingProjectItems(scan.CsprojPaths.Count));
            // [A1/T15] Katman pattern'leri komuttan Core'a AKTARILIR — null/boş ise LayerEngine devre dışıdır
            // (varsayılan, mevcut davranış); dolu ise sert faz bariyeri + ters-katman uyarıları devreye girer.
            var plan = new BuildPlanBuilder(scanner, evaluator, cache)
                .Build(scan, cmd.Configuration, cmd.LayerPatterns, external.ExternalProjectIds);
            progress(PlanProgressLines.DependencyGraph(plan.Cycles.Count));
            progress(PlanProgressLines.BuildOrderResolved(plan.Nodes.Count));
            var solutionRefs = SolutionMapper.MapRefs(scan.SlnPaths, scan.CsprojPaths);

            // Satır işin ÖNCESİNDE: incremental pass (git diff + proje başına imza) planlamanın EN UZUN adımıdır
            // ve kendi sayısını üretmez — sonrasına bırakılsa akış tam da en uzun beklemede sessizleşirdi.
            progress(PlanProgressLines.ComputingIncremental(plan.Nodes.Count));
            // Harici köklerin revizyonu: satırın sha yuvasını besleyen TANI bilgisi. Bayraktan BAĞIMSIZ okunur
            // (yerel `rev-parse HEAD`, ucuz) — güncelleme kapalıyken de kullanıcı hangi sürümü derlediğini görür.
            var externalCommits = new ExternalRevisionReader(new ProcessRunner())
                .ReadAsync(external.Roots, updatedRevisions).GetAwaiter().GetResult();
            var (boundPlan, incremental) = ComputeIncremental(cmd, plan,
                EvaluateAll(scan, evaluator, cache), stateStore, sourceHashes, externalCommits, progress);
            return new RunPlan(boundPlan, solutionRefs, incremental);
        }
    }

    /// <summary>
    /// projectId (tam csproj yolu) → <see cref="EvaluatedProject"/>. Cache SICAK (plan zaten değerlendirdi) —
    /// bu re-call mtime+size hızlı yolundan bellekten döner, XML yeniden okunmaz. <c>GetOrEvaluate</c> canlı
    /// build ↔ scan yarışında kaybolan bir dosya için null dönebilir [Task 0/It-4a]; o yollar elenir.
    /// <para>Değerlendirme çökerse boş harita döner: committed fingerprint boşa düşer, yani over-build —
    /// güvenli taraf.</para>
    /// </summary>
    private static IReadOnlyDictionary<string, EvaluatedProject> EvaluateAll(
        ScanResult scan, CsprojEvaluator evaluator, EvaluationCache cache)
    {
        try
        {
            return scan.CsprojPaths
                .Select(p => (Id: Path.GetFullPath(p), Project: cache.GetOrEvaluate(p, evaluator.Evaluate)))
                .Where(x => x.Project is not null)
                .ToDictionary(x => x.Id, x => x.Project!, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("project evaluation skipped (committed fingerprints will be empty): " + ex);
            return new Dictionary<string, EvaluatedProject>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// [Task 19] Fresh (Rebuild/Build) run için incremental karar: her düğüm için <c>WillBuild</c> + byte-stable
    /// <see cref="BuildOrchestrator.Core.Incremental.BuildSignature"/> imzası hesaplanır.
    /// <b>SALT-OKUR git (K1):</b> HEAD/branch yalnız OKUNUR — checkout/pull/fetch/reset ASLA. Herhangi bir
    /// discovery/hash hatası → plan AYNEN döner (WillBuild=null) ve <c>Incremental=null</c>: Build o durumda
    /// pre-skip yapmaz (hepsini derler, güvenli taraf). Çıktı dosyalarının zamanı ve boyutu yalnız çıktı
    /// kanıtında (<c>ChecksFor</c>, ARCHITECTURE §7.6) okunur ve imzaya girmez. Git sorguları ve
    /// imzanın köke göreli yolları <c>cmd.RootPath</c>'e — kullanıcının çalışma ağacına — göredir.
    /// </summary>
    /// <param name="externalCommits">Harici projelerin kendi çalışma kopyalarının revizyonu — build-state'in
    /// <c>BuiltCommit</c> yuvasına ana reponun HEAD'i yerine bu yazılır.</param>
    private static (BuildPlan Plan, IncrementalPlan? Info) ComputeIncremental(
        StartRunCommand cmd, BuildPlan plan,
        IReadOnlyDictionary<string, EvaluatedProject> evaluatedById, BuildStateStore stateStore,
        SourceHashCache hashes, IReadOnlyDictionary<string, string> externalCommits, Action<string> progress)
    {
        try
        {
            // git yalnız TANI içindir: HEAD ve branch build-state kaydına (BuiltCommit/LastBranch) ve konsol
            // satırlarına gider. [D1] KARARA GİRMEZ — imza diskteki içerikten hesaplanır, bu yüzden git'i
            // bozuk ya da hiç olmayan bir makinede de tam bir incremental karar üretilir.
            var git = new GitService(new ProcessRunner(), cmd.RootPath);
            var headResult = git.GetHeadCommitAsync().GetAwaiter().GetResult();
            string? head = headResult.Success ? headResult.Value : null;
            var branchResult = git.GetCurrentBranchAsync().GetAwaiter().GetResult();
            string? branch = branchResult.Success ? branchResult.Value : null;

            var binder = new IncrementalRunBinder(plan, evaluatedById, cmd.RootPath, hashes);
            binder.Prefill(files =>
            {
                if (files >= SourceHashCache.NoisyPrefillThreshold) progress(PlanProgressLines.IndexingSources(files));
            });

            var state = stateStore.Load();
            // [Faz 3/Task 6 — spec 2026-09-18 §5] Çıktı kanıtı karara girer — Sync'in Safe geçişiyle AYNI
            // kontroller: dışarıda derlenmiş güncel proje BuiltOutside ile pre-skip edilir, kanıtı eksik/bozuk
            // olan derlenir. Kontroller plana da taşınır (koşu önizlemesi).
            var checks = binder.ChecksFor(state);
            var (bound, signatures) = binder.Bind(state, cmd.Mode == RunMode.Cycles, cmd.DependentMode, checks);

            hashes.Flush();
            // [v1.16.0] İçerik özetleri de taşınır: başarılı derlemede deftere yazılır (BuildState.BuiltContent)
            // ve önizlemenin modified ↔ affected ayrımı defterdeki özetle bugünkünün karşılaştırmasından çıkar.
            // [Faz 3/Task 4] OutputsById de aynı binder'dan — Supervisor başarılı derlemeden sonra beslenen
            // kopyaları buradan öğrenir (BuildState.FedOutputs).
            return (bound, new IncrementalPlan(signatures, head, branch, externalCommits, binder.ContentById,
                binder.OutputsById, checks));
        }
        catch (Exception ex)
        {
            // Incremental bir OPTİMİZASYONDUR: discovery/hash yolunda HERHANGİ bir hata (I/O, XML, vb.) tüm
            // run'ı ÖLDÜRMEMELİ. Plan AYNEN döner (WillBuild=null) → Build o durumda pre-skip yapmaz (hepsini
            // derler, güvenli taraf). Tanı için stderr'e bir satır düşülür (stdout YALNIZ NDJSON [D4]).
            Console.Error.WriteLine("incremental pass skipped (plan kept as-is, everything will be built): " + ex);
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

    /// <summary>
    /// [spec 2026-09-18 §5.5] Açılış kurtarmasını koşar ve yeniden derlenecek proje sayısını döner. Defter I/O
    /// hatası motoru AYAĞA KALDIRMAYI engellemez: stderr'e uyarı düşer (stdout YALNIZ NDJSON [D4]), sayı 0 olur ve
    /// dosya yerinde kalır — bir sonraki açılış yeniden dener.
    /// </summary>
    private static int RecoverInterruptedRun(InFlightLedger inFlight, BuildStateStore stateStore)
    {
        try { return inFlight.Recover(stateStore, DateTimeOffset.UtcNow).Count; }
        catch (Exception ex)
        {
            Console.Error.WriteLine("warning: interrupted run could not be recovered (" + inFlight.FilePath + "): " + ex.Message);
            return 0;
        }
    }

    private static string? GetArg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
