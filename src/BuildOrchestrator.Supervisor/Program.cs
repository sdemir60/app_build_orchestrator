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

/// <summary>
/// [A4] Bir run için ÇÖZÜLMÜŞ workspace — <see cref="Program.PrepareAsync"/> çıktısı.
/// </summary>
/// <param name="ScanRoot">Projelerin taranacağı kök: worktree hazırlandıysa worktree yolu, aksi halde
/// <c>cmd.RootPath</c>. Incremental pass'in git sorguları ve repo-relative yolları da BU köke göredir —
/// tarama ile imza aynı ağacı görmezse committed fingerprint anlamsızlaşır.</param>
/// <param name="WorktreeObjRoot">Worktree modunda obj izolasyon kökü (<see
/// cref="Core.MsBuild.WorktreeObjPathResolver"/> bunu proje başına izole <c>BaseIntermediateOutputPath</c>'e
/// çevirir); in-place'de <c>null</c>.</param>
/// <param name="InPlace">İmzanın local-diff terimi dahil edilsin mi (<see
/// cref="Core.Incremental.BuildSignature"/>). Bu bir TAHMİN DEĞİLDİR: yalnız worktree GERÇEKTEN
/// hazırlanabildiyse false olur. Hazırlık başarısız olup in-place'e düşüldüyse true kalır — aksi halde
/// dirty working tree üzerinde koşan bir derleme için "temiz commit'ten derlendi" diyen bir imza persist
/// edilirdi (yanlış pre-skip).</param>
public sealed record PreparedWorkspace(string ScanRoot, string? WorktreeObjRoot, bool InPlace);

/// <summary>
/// [Fix wave 1 — Finding 3] Seçili branch AKTİF branch'ten FARKLI iken worktree hazırlığı başarısız oldu:
/// bu durumda in-place'e DÜŞÜLEMEZ. Worktree, farklı bir branch'i derlemenin TEK mekanizmasıdır (K1 — aktif
/// branch asla checkout edilmez); in-place fallback sessizce YANLIŞ branch'i (kullanıcının kirli çalışma
/// ağacını) derlerdi. Bu exception <see cref="Program.PrepareAsync"/>'ten planner yoluyla yukarı çıkar ve
/// <see cref="RunCoordinator"/>'ın mevcut planlama-hatası kanalına (<c>error(planFailed)</c>) dönüşür; run
/// hiç başlamaz. <see cref="Exception.Message"/> KULLANICIYA GÖSTERİLİR — İngilizce, branch adını ve asıl
/// sebebi taşır.
/// </summary>
public sealed class WorktreePreparationException : Exception
{
    public WorktreePreparationException(string message) : base(message) { }

    public WorktreePreparationException(string message, Exception innerException) : base(message, innerException) { }

    public WorktreePreparationException() { }
}

public static class Program
{
    /// <summary>
    /// [T14] Worktree havuzunun disk cap'i. Settings'te henüz kullanıcıya açılmamıştır — sabit varsayılan.
    /// </summary>
    private const long WorktreePoolCapBytes = 20L * 1024 * 1024 * 1024;

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
        // [A4] Taze (Rebuild/Build) run'ın çözülmüş workspace'i. BuildRunPlan YAZAR, worktreeObjRootResolver
        // OKUR — koordinatör resolver'ı planner'dan SONRA çağırdığı için sıra garantilidir; tek seferde tek
        // run (A6) olduğu için de iki run'ın burada yarışması imkansızdır. Continue/RetryFailed bu resolver'ı
        // HİÇ çağırmaz (orijinal run'ın kökünü koordinatörden miras alır).
        PreparedWorkspace? prepared = null;
        using var coordinator = new RunCoordinator(
            planner: BuildRunPlan,
            msbuildFactory: ct => ResolveMsBuildAsync(innerJob, ct),
            logFactory: startedAt => new RunLogWriter(logsRoot, startedAt),
            writer: writer,
            innerJob: innerJob,
            nowMs: () => Environment.TickCount64, // MONOTONİK — duvar saati geri atlayabilir, elapsed negatife düşerdi
            console: Console.Error.WriteLine,
            worktreeObjRootResolver: _ => prepared?.WorktreeObjRoot, // [A4] obj izolasyonu artık CANLI
            stateStore: stateStore); // [Task 19] projectSucceeded → BuildState persist
        var host = new SupervisorHost(writer, new NdjsonReader(stdin), innerJob, coordinator);
        return await host.RunAsync();

        // Planlama TAMAMEN Core'da [D3]: scan → evaluate (cache'li) → graph → topo → BuildPlan → (fresh modda)
        // incremental willBuild + imza. Planlayıcı yalnız fresh (Rebuild/Build) modda çağrılır (Continue/RetryFailed
        // mevcut plan'dan devam eder — bkz. RunCoordinator).
        RunPlan BuildRunPlan(StartRunCommand cmd)
        {
            // [A4/K3] Worktree Build ANINDA hazırlanır (branch seçimi yalnız NİYETTİR, git'e dokunmaz) ve
            // hazırlık planlamanın İLK adımıdır: tarama da, incremental imza da AYNI çözülmüş kökü görmelidir.
            // Senkron çağrı, planner'ın zaten senkron/I/O yapan sözleşmesiyle uyumludur (bkz. ComputeIncremental'ın
            // aynı desendeki git çağrıları) — koordinatör onu arka plan task'ından çağırır, IPC loop'u bloklanmaz.
            var workspace = PrepareAsync(cmd, new ProcessRunner(), WorktreeManager.DefaultPoolRoot,
                Console.Error.WriteLine).GetAwaiter().GetResult();
            prepared = workspace;

            string cachePath = Path.Combine(cacheRoot, "evaluation-cache.json");
            var scanner = new WorkspaceScanner();
            var evaluator = new CsprojEvaluator();
            var cache = new EvaluationCache(cachePath);
            // [Task 18] TEK tarama: BuildPlanBuilder'ın ScanResult-alan overload'ı kullanılır — packages.config
            // restore'un istediği SolutionDir için .sln YOLLARI (ProjectNode yalnız solution ADI taşır) aynı
            // scan'den (`scan.SlnPaths`) elde edilir, workspace ikinci kez taranmaz.
            var scan = scanner.Scan(workspace.ScanRoot);
            // [A1/T15] Katman pattern'leri komuttan Core'a AKTARILIR — null/boş ise LayerEngine devre dışıdır
            // (varsayılan, mevcut davranış); dolu ise sert faz bariyeri + ters-katman uyarıları devreye girer.
            var plan = new BuildPlanBuilder(scanner, evaluator, cache).Build(scan, cmd.Configuration, cmd.LayerPatterns);
            var solutionRefs = SolutionMapper.MapRefs(scan.SlnPaths, scan.CsprojPaths);

            var (boundPlan, incremental) = ComputeIncremental(cmd, workspace, plan, scan, evaluator, cache, stateStore);
            return new RunPlan(boundPlan, solutionRefs, incremental);
        }
    }

    /// <summary>
    /// [A4/K3] Build ANINDA workspace'i çözer: <paramref name="cmd"/> worktree istemiyorsa in-place; istiyorsa
    /// <see cref="WorktreeManager"/> ile havuzda <c>git worktree add --detach</c> ile gerçek bir worktree açar.
    /// <para>
    /// <b>K1:</b> aktif branch ASLA checkout/switch/pull/reset edilmez — ANA REPO üzerindeki tek mutasyon
    /// <c>worktree add</c>'dir; seçilen branch'in COMMITTED hâli ayrı bir dizinde açılır, kullanıcının çalışma
    /// ağacına dokunulmaz. [Fix wave 1] Yeniden kullanım yolundaki <c>reset --hard</c> ise HAVUZ WORKTREE'SİNİN
    /// İÇİNDE (daima detached, bizim dizinimiz) koşar — bkz. <see cref="WorktreeManager.ReuseWorktreeAsync"/>.
    /// </para>
    /// <para>
    /// <b>Başarısızlık = in-place fallback + uyarı, run ÖLMEZ</b> (git yok / branch çözülemedi / worktree add
    /// patladı / beklenmeyen I/O). Kritik olan, fallback'in <see cref="PreparedWorkspace.InPlace"/>'i
    /// <c>true</c> BIRAKMASIDIR: eski kod <c>inPlace = !cmd.UseWorktree</c> ile TAHMİN ediyordu, yani hazırlık
    /// olmadan da imza local-diff terimini atlıyor (worktree modu sanıyor) ama derleme dirty working tree
    /// üzerinde koşuyordu → "temiz commit'ten derlendi" diyen YANLIŞ imza persist ediliyordu. Burada
    /// <c>InPlace=false</c> döndürebilen TEK yol, worktree'nin gerçekten oluşturulmuş olmasıdır.
    /// </para>
    /// <para>
    /// [T14] Havuz cap'i yeni worktree EKLENMEDEN ÖNCE uygulanır (klasik cache-eviction yerleşimi): yer
    /// açılacaksa yeni worktree diski doldurmadan ÖNCE açılmalıdır. Prune best-effort'tur — başarısızlığı
    /// yalnız uyarıya dönüşür, build'i engellemez.
    /// </para>
    /// <para>
    /// [Fix wave 1 — Finding 3] <b>Ancak fallback SADECE aktif branch için geçerlidir.</b> Seçili branch
    /// aktif branch'ten FARKLIYSA worktree ZORUNLUDUR (K1 — aktif branch asla checkout edilmez): orada
    /// in-place'e düşmek "X'i derle" denmişken sessizce Y'yi (kullanıcının kirli çalışma ağacını) derlemek
    /// olurdu. O yolda hazırlığın HER başarısızlığı <see cref="WorktreePreparationException"/> fırlatır ve run
    /// <c>error(planFailed)</c> ile hiç başlamaz.
    /// </para>
    /// <para>
    /// [Fix wave 1 — Finding 2] Havuzda seçili branch'in bir worktree'si zaten varsa YENİDEN KULLANILIR
    /// (<see cref="WorktreeManager.ReuseWorktreeAsync"/>) — yenisi ancak aday yoksa ya da yeniden kullanım
    /// başarısız olursa açılır. Havuzun kalıcı olmasının amacı budur: aynı dizin ⇒ aynı obj ⇒ SICAK cache.
    /// </para>
    /// </summary>
    /// <param name="poolRoot">Worktree havuzunun kökü (üretimde <see cref="WorktreeManager.DefaultPoolRoot"/>;
    /// testler izole bir temp dizin verir — kullanıcının gerçek havuzu kirletilmez).</param>
    /// <param name="warn">Uyarı kanalı (üretimde <c>Console.Error.WriteLine</c> — stdout YALNIZ NDJSON'dır [D4]).</param>
    /// <exception cref="WorktreePreparationException">Seçili branch aktif branch'ten farklıyken worktree
    /// hazırlanamadıysa (bkz. yukarıdaki Finding 3 notu).</exception>
    public static async Task<PreparedWorkspace> PrepareAsync(
        StartRunCommand cmd, IProcessRunner runner, string poolRoot, Action<string> warn, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(warn);

        var inPlace = new PreparedWorkspace(cmd.RootPath, null, InPlace: true);
        // Toggle KAPALI ve branch SEÇİLMEMİŞ (App'in bugünkü in-place Build'i) → tek bir git çağrısı bile
        // yapılmadan in-place. Branch DOLU ise toggle'a bakılmaz: farklı bir branch, toggle kapalı olsa bile
        // worktree'yi ZORUNLU kılar (PlanWorktree'nin 3-durum matrisi, K1).
        if (!cmd.UseWorktree && string.IsNullOrWhiteSpace(cmd.Branch)) return inPlace;

        string selectedBranch = cmd.Branch;
        bool mandatory = false; // seçili branch ≠ aktif branch ⇒ in-place'e düşme HAKKI YOK

        // Başarısızlığın TEK karar noktası: zorunlu worktree yolunda fırlat, aksi halde uyar + in-place.
        PreparedWorkspace FailOrFallback(string reason)
        {
            if (mandatory) throw new WorktreePreparationException(MandatoryWorktreeFailure(selectedBranch, reason));
            warn($"warning: worktree preparation failed ({reason}) — falling back to in-place build");
            return inPlace;
        }

        try
        {
            var git = new GitService(runner, cmd.RootPath);
            var activeBranchResult = await git.GetCurrentBranchAsync(ct);
            // Detached HEAD (Ok(null)) da dahil: "aktif branch" bilinmeden 3-durum matrisi kurulamaz.
            if (!activeBranchResult.Success || activeBranchResult.Value is not { } activeBranch)
            {
                string reason = "the active branch could not be determined: "
                    + (activeBranchResult.Error ?? "HEAD is detached");
                // Kullanıcı AÇIKÇA bir branch seçtiyse, onun aktif branch'le aynı olduğu İSPATLANAMAZ →
                // güvenli yön, sessizce yanlış ağacı derlemek değil, run'ı durdurmaktır.
                if (!string.IsNullOrWhiteSpace(cmd.Branch))
                    throw new WorktreePreparationException(MandatoryWorktreeFailure(cmd.Branch, reason));
                warn($"warning: worktree preparation failed ({reason}) — falling back to in-place build");
                return inPlace;
            }

            // Branch boş gelirse (App bugün böyle yolluyor) niyet "aktif branch'in COMMITTED hâli"dir.
            if (string.IsNullOrWhiteSpace(selectedBranch)) selectedBranch = activeBranch;
            mandatory = !string.Equals(selectedBranch, activeBranch, StringComparison.Ordinal);
            if (!mandatory && !cmd.UseWorktree) return inPlace; // 3-durum matrisi: aktif branch + toggle kapalı

            string? sha = await ResolveSelectedShaAsync(git, activeBranch, selectedBranch, ct);
            if (sha is null) return FailOrFallback($"no commit could be resolved for branch '{selectedBranch}'");

            var manager = new WorktreeManager(runner, cmd.RootPath, poolRoot);

            // [Finding 2] ÖNCE yeniden kullanım: havuzdaki aynı branch worktree'si hedef commit'e güncellenir.
            // Başarısızlık BUILD'İ ÖLDÜRMEZ (zorunlu yolda bile) — yalnız yeni bir worktree açılır.
            string? reusedPath = null;
            var reuse = await manager.ReuseWorktreeAsync(selectedBranch, sha, cmd.WorktreeName, ct);
            if (reuse.Success) reusedPath = reuse.Value;
            else warn($"warning: worktree reuse failed ({reuse.Error}) — a new pool worktree will be created");

            // [T14] Cap: KULLANIMDAKİ worktree hariç budanır. Yeni açılacaksa (reusedPath null) korunacak bir şey
            // yoktur — tek seferde tek run (A6) olduğu için başka bir run'ın worktree'si kullanımda olamaz.
            var prune = await manager.PruneToCapAsync(WorktreePoolCapBytes,
                reusedPath is null ? null : Path.GetFileName(reusedPath), ct);
            if (!prune.Success) warn("warning: worktree havuzu budanamadı (cap uygulanmadı): " + prune.Error);

            if (reusedPath is not null) return new PreparedWorkspace(reusedPath, reusedPath, InPlace: false);

            var plan = manager.PlanWorktree(activeBranch, selectedBranch, useWorktreeToggle: cmd.UseWorktree, selectedSha: sha);
            var result = await manager.PrepareWorktreeAsync(plan, ct);
            if (!result.Success) return FailOrFallback(result.Error!);

            return new PreparedWorkspace(result.Value!, result.Value, InPlace: false);
        }
        catch (Exception ex) when (ex is not WorktreePreparationException)
        {
            // Worktree bir OPTİMİZASYON/İZOLASYONDUR: hazırlık yolundaki HERHANGİ bir beklenmeyen hata (I/O,
            // erişim, bozuk havuz dizini) run'ı ÖLDÜRMEMELİ — AMA yalnız aktif branch yolunda. Farklı branch
            // seçiliyken güvenli taraf in-place DEĞİL, run'ı durdurmaktır (Finding 3).
            if (mandatory) throw new WorktreePreparationException(MandatoryWorktreeFailure(selectedBranch, ex.Message), ex);
            warn($"warning: worktree preparation failed ({ex.Message}) — falling back to in-place build");
            return inPlace;
        }
    }

    /// <summary>[Fix wave 1 — Finding 3] Zorunlu-worktree yolunun KULLANICIYA GÖSTERİLEN hata metni (İngilizce, [Global] UI-metni kuralı): hangi branch, neden.</summary>
    private static string MandatoryWorktreeFailure(string selectedBranch, string reason) =>
        $"Cannot build branch '{selectedBranch}': it is not the branch checked out in the workspace, so it must be "
        + $"built in an isolated worktree (the active branch is never checked out). Worktree preparation failed: {reason}";

    /// <summary>
    /// [A4] Worktree'nin detach edileceği commit. Aktif branch seçiliyse (toggle senaryosu) yerel HEAD —
    /// kullanıcının gördüğü commit.
    /// <para>
    /// [Fix wave 1 — Finding 4] FARKLI bir branch seçiliyse ÖNCE YEREL ref (<c>refs/heads/&lt;branch&gt;</c>) —
    /// kullanıcının UI'da gördüğü branch listesi <c>refs/heads/*</c>'ı da içerir, yani yalnız remote-tracking
    /// ref'e bakan bir çözücü tam olarak o (henüz push edilmemiş) branch'leri çözemez ve run sessizce
    /// başarısız olurdu. Yerel ref yoksa <c>refs/remotes/origin/&lt;branch&gt;</c>'e düşülür: yalnız bir fetch'ten
    /// (Sync — K1, ref-only) bilinen branch'lerin tek meşru kaynağı odur. Çözülemezse <c>null</c> → çağıran
    /// karar verir (aktif branch: in-place fallback; farklı branch: run durur).
    /// </para>
    /// </summary>
    private static async Task<string?> ResolveSelectedShaAsync(
        GitService git, string activeBranch, string selectedBranch, CancellationToken ct)
    {
        if (string.Equals(activeBranch, selectedBranch, StringComparison.Ordinal))
        {
            var head = await git.GetHeadCommitAsync(ct);
            return head.Success ? head.Value : null; // unborn HEAD → Ok(null) → detach edilecek commit YOK
        }

        var local = await git.GetLocalBranchShaAsync(selectedBranch, ct);
        if (local.Success && local.Value is { } localSha) return localSha;

        var remote = await git.GetRemoteTrackingShaAsync(selectedBranch, ct);
        return remote.Success ? remote.Value : null;
    }

    /// <summary>
    /// [Task 19] Fresh (Rebuild/Build) run için incremental karar: her düğüm için <c>WillBuild</c> + byte-stable
    /// <see cref="BuildOrchestrator.Core.Incremental.BuildSignature"/> imzası hesaplanır.
    /// <b>SALT-OKUR git (K1):</b> HEAD/branch/dirty/ls-tree yalnız OKUNUR — checkout/pull/fetch/reset ASLA. Herhangi
    /// bir git/discovery hatası ya da hollow (HEAD yok) → plan AYNEN döner (WillBuild=null) ve <c>Incremental=null</c>:
    /// Build o durumda pre-skip yapmaz (hepsini derler, güvenli taraf). §4: DLL/bin/obj timestamp'ı okunmaz.
    /// <para>
    /// [A4] TÜM git sorguları ve repo-relative yollar <see cref="PreparedWorkspace.ScanRoot"/>'a göredir —
    /// tarama worktree'yi görürken imzanın ana repoyu okuması, proje yollarının repo dışına düşmesine ve
    /// committed fingerprint'in tamamen boşa çıkmasına yol açardı. Worktree modunda bu kök detached HEAD'dir:
    /// HEAD = seçilen commit, dirty boş — <see cref="PreparedWorkspace.InPlace"/>=false ile tutarlı.
    /// </para>
    /// </summary>
    private static (BuildPlan Plan, IncrementalPlan? Info) ComputeIncremental(
        StartRunCommand cmd, PreparedWorkspace workspace, BuildPlan plan, ScanResult scan,
        CsprojEvaluator evaluator, EvaluationCache cache, BuildStateStore stateStore)
    {
        try
        {
            var git = new GitService(new ProcessRunner(), workspace.ScanRoot);
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
            // mtime+size hızlı yolundan bellekten döner, XML yeniden okunmaz. GetOrEvaluate canlı build ↔ scan
            // yarışında kaybolan bir dosya için null dönebilir [Task 0/It-4a] — o yollar burada sessizce elenir.
            var evaluatedById = scan.CsprojPaths
                .Select(p => (Id: Path.GetFullPath(p), Project: cache.GetOrEvaluate(p, evaluator.Evaluate)))
                .Where(x => x.Project is not null)
                .ToDictionary(x => x.Id, x => x.Project!, StringComparer.OrdinalIgnoreCase);

            // [A4] TAHMİN (`!cmd.UseWorktree`) DEĞİL, ÇÖZÜLMÜŞ workspace: worktree istenip de hazırlanamadıysa
            // burası true kalır ve imza local-diff terimini DAHİL eder — derlemenin gerçekte üzerinde koştuğu ağaç.
            var (bound, signatures) = IncrementalRunBinder.Bind(
                plan, evaluatedById, workspace.ScanRoot, head, tracked, dirty,
                stateStore.Load(), workspace.InPlace, cmd.DependentMode);
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
