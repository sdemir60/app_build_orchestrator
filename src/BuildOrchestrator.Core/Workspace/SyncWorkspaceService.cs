using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Core.State;

namespace BuildOrchestrator.Core.Workspace;

/// <summary>
/// [A5/T69] Sync akışının TAMAMI, Core'da (D3 — planlama App/Supervisor'a sızmaz): ref-only fetch → tarama →
/// evaluate/graf/topo (plan) → will-build pass → <see cref="WorkspaceTopologyEvent"/> + <see
/// cref="BuildPreviewEvent"/> + <see cref="SyncCompletedEvent"/>. v7 A5'e göre TAM ANALİZ YALNIZ SYNC'te koşar;
/// Build'in örtük Sync'i evaluation-cache sayesinde ucuzdur.
///
/// <para><b>K1 (mutlak):</b> git adımı YALNIZ <see cref="GitService.FetchRefOnlyAsync"/>'tir. checkout / pull /
/// merge / switch / reset ASLA çağrılmaz; aktif branch, HEAD ve working tree bu servisle DEĞİŞMEZ. Geri kalan
/// tüm git kullanımı salt-okurdur (HEAD, dirty paths, ls-tree). Bkz. <c>SyncWorkspaceServiceTests</c>.</para>
///
/// <para><b>Offline degrade:</b> remote ulaşılamazsa hata YUTULUR — warn tonunda bir progress satırı basılır ve
/// akış YEREL HEAD ile devam eder. Degrade yolu topoloji ve will-build pass'ini ATLAMAZ: offline'da da tam,
/// kullanılabilir bir Sync üretilir (yalnız hedef SHA yerel HEAD'e düşer).</para>
///
/// <para><b>Will-build pass'in kapsamı (bilinen seam):</b> pass IN-PLACE, yani kullanıcının O ANKİ ÇALIŞMA
/// AĞACINA karşı koşar — Sync anında kullanıcının baktığı ağaç budur ve imzanın local-diff terimini anlamlı
/// kılan da odur. Sonuç olarak <see cref="SyncWorkspaceCommand.Branch"/> AKTİF branch'ten farklı bir branch'i
/// adlandırıyorsa, üretilen önizleme adlandırılan branch'i DEĞİL aktif branch'i tarif eder (fetch yine de o
/// branch'in ref'ini günceller ve <see cref="SyncCompletedEvent.TargetSha"/> onu taşır). Bu seam'in
/// kapatılması branch seçimini UI'a bağlayan task'ın (D6) işidir; burada kasıtlı olarak YALNIZ gerçekten
/// hesaplanan şey raporlanır — önizlemenin adlandırılan branch'i tarif ettiği İMA EDİLMEZ.</para>
/// </summary>
/// <param name="git">Kökü <see cref="SyncWorkspaceCommand.RootPath"/>'e BAĞLI bir <see cref="GitService"/> —
/// worktree değil, KULLANICININ REPO KÖKÜ (Sync, Supervisor'ın build-anı worktree hazırlığıyla yarışmaz).</param>
public sealed class SyncWorkspaceService(
    WorkspaceScanner scanner,
    CsprojEvaluator evaluator,
    EvaluationCache cache,
    GitService git,
    BuildStateStore stateStore)
{
    /// <summary>Will-build pass'inin sonucu: bağlanmış plan + §3.1 konsol satırlarının/D2 şeridinin okuduğu sayaçlar.</summary>
    /// <param name="Known">false ⇒ anlamlı bir taban yok (repo'da hiç commit yok ya da pass hata verdi) — TÜM
    /// düğümler hollow (<c>WillBuild=null</c>) kalır ve sayaçlar RAPORLANMAZ (0 yazmak "hepsi güncel" yalanı olurdu).</param>
    private readonly record struct WillBuildOutcome(BuildPlan Plan, int Changed, int ToBuild, int UpToDate, bool Known);

    public async Task RunAsync(SyncWorkspaceCommand cmd, Action<IpcEvent> emit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentNullException.ThrowIfNull(emit);

        emit(new SyncStartedEvent(cmd.RootPath, cmd.Branch));

        // --- girdi kapıları: bozuk kök IPC sınırında EXCEPTION'a değil TANIMLI bir hata event'ine dönüşür.
        // Kod olarak `planFailed` YENİDEN KULLANILIR (yeni kod icat edilmez): Sync tam olarak planlama
        // pipeline'ıdır ve RunCoordinator planlama hatasında zaten bu kodu yayınlar; App tarafında da
        // RunEndingErrorCodes içinde tanınır, yani örtük Sync'li bir Build doğru biçimde iptal olur.
        if (!Directory.Exists(cmd.RootPath))
        {
            emit(new ErrorEvent("planFailed", $"Workspace root not found: '{cmd.RootPath}'."));
            return;
        }

        // Repo kapısı: GetHeadCommitAsync git'i salt-okur çağırır. Fail ⇒ git yok / dizin bir git repo'su değil /
        // repo bozuk. Ok(null) HATA DEĞİLDİR — henüz commit'i olmayan (unborn HEAD) geçerli bir repo (hollow).
        var head = await git.GetHeadCommitAsync(ct);
        if (!head.Success)
        {
            emit(new ErrorEvent("planFailed",
                $"'{cmd.RootPath}' is not a usable git repository: {head.Error}"));
            return;
        }

        // --- 1) ref-only fetch (K1). §3.1 satır 1.
        emit(Cmd($"git fetch origin {cmd.Branch}"));
        var fetch = await git.FetchRefOnlyAsync(cmd.Branch, ct);
        if (fetch.Degraded)
        {
            // Ağ yok/remote geçersiz: AKIŞ DURMAZ — uyarı basılır, hedef yerel HEAD'e düşer ve analiz devam eder.
            emit(Warn($"warning: git fetch failed — continuing against the local HEAD ({fetch.Warning})"));
        }

        // §3.1 satır 2. SHA sabit örnek DEĞİL, gerçekten çözülen hedef commit'tir. Hedef hiç çözülemediyse
        // (commit'siz repo) satır BASILMAZ — "HEAD  — ..." gibi yarım bir satır üretmek yerine sessiz kalınır.
        string? targetSha = fetch.TargetSha;
        if (targetSha is not null) emit(Info($"HEAD {ShortSha(targetSha)} — computing osys-state diff"));

        // --- 2) tarama + plan. [v7 A5/N1] granular adım satırları fetch satırından SONRA, dim/info tonunda.
        // [planlama görünürlüğü] Adım metinleri PlanProgressLines'tan gelir: AYNI satırları Supervisor'ın
        // BuildRunPlan'ı da yayınlar (Build'e basıldığında planlama yeniden koşar) — metin iki yerde
        // tanımlanamaz (CLAUDE.md kopya yasağı). Ton (dim/info) burada kalır: o, Sync transkriptinin
        // sunumudur, satırın kendisi değil.
        var scan = scanner.Scan(cmd.RootPath);
        emit(Dim(PlanProgressLines.ScanningSolutions(scan.SlnPaths.Count)));
        emit(Dim(PlanProgressLines.ReadingProjectItems(scan.CsprojPaths.Count)));

        var plan = new BuildPlanBuilder(scanner, evaluator, cache).Build(scan, cmd.Configuration, cmd.LayerPatterns);
        emit(Dim(PlanProgressLines.DependencyGraph(plan.Cycles.Count)));
        emit(Info(PlanProgressLines.BuildOrderResolved(plan.Nodes.Count)));

        // --- 3) will-build pass (v7 A6: "run'dan ÖNCE"; hollow = SYNC ÖNCESİ) — Sync sonrası düğümler artık
        // dirty/clean bilir, hollow kalmaz. SALT-OKUR: build-state yalnız OKUNUR, Sync hiçbir şey PERSIST ETMEZ,
        // bu yüzden koşan bir build'in state'ini bozamaz.
        // [W1] Build-state TEK KEZ, BURADA okunur ve iki tüketiciye birden verilir: (a) will-build pass'i
        // (BuiltSignature/LastResult), (b) aşağıdaki önizleme projeksiyonu (BuiltCommit = sha çiftinin sol
        // yarısı). Pass'in İÇİNDE kalsaydı hollow/hata dallarında (pass hiç koşmaz) elde state olmazdı ve
        // "durumu bilinmeyen ama daha önce derlenmiş" satır sha'sını kaybederdi. SALT-OKUR: yalnız Load.
        var state = stateStore.Load();
        var outcome = await ComputeWillBuildAsync(cmd, plan, scan, head.Value, state, emit, ct);

        // --- 4) topoloji + önizleme. Önizleme AYRI bir will-build yolu DEĞİLDİR: App'in mevcut
        // BuildPreviewEvent handler'ı satırların WillBuild'ini zaten bu event'ten kurar (ikinci bir yol açılmaz).
        emit(new WorkspaceTopologyEvent(
            Nodes: outcome.Plan.Nodes,
            Cycles: outcome.Plan.Cycles,
            Solutions: ToSolutionRefs(scan),
            LayerWarnings: outcome.Plan.LayerWarnings ?? []));
        emit(new BuildPreviewEvent(
            outcome.Plan.Nodes
                .Select(n => new BuildPreviewItem(n.Id, n.Name, n.WillBuild, BuildStateStore.BuiltCommitOf(state, n.Id)))
                .ToList()));

        // --- 5) §3.1 satır 3 + 4. Sayılar syncCompleted'ın sayaçlarıyla AYNI kaynaktan gelir.
        if (outcome.Known)
        {
            // [Fix wave 1 — Finding 5] TAMAMEN temiz workspace design-v1'de AYRI ve TEK bir satırdır
            // (prototype/app/build-data.js:278 — `allClean` dalı): "0 changed projects, 0 to build" + "0 projects
            // up to date (will skip)" yazmak, en sık görülen kararlı durumu yanlış anlatırdı.
            if (outcome.ToBuild == 0 && outcome.Changed == 0)
                emit(Info($"Sync complete — no changes, {outcome.UpToDate} projects up to date"));
            else
            {
                emit(Info($"Sync complete — {outcome.Changed} changed projects, {outcome.ToBuild} to build"));
                emit(Dim($"{outcome.UpToDate} projects up to date (will skip)"));
            }
        }
        else
        {
            // Taban yok (commit'siz repo / pass hata verdi): "0 changed, 0 to build" YAZILMAZ — o, "her şey
            // güncel" anlamına gelirdi. Gerçekten bilinen tek şey kaç proje bulunduğudur.
            emit(Info($"Sync complete — {outcome.Plan.Nodes.Count} projects, project states unknown"));
        }

        emit(new SyncCompletedEvent(cmd.Branch, targetSha, fetch.Degraded,
            ProjectCount: outcome.Plan.Nodes.Count, CycleCount: outcome.Plan.Cycles.Count,
            ChangedCount: outcome.Changed, ToBuildCount: outcome.ToBuild, UpToDateCount: outcome.UpToDate));
    }

    /// <summary>
    /// [A5/T69] Plan'ı incremental willBuild ile bağlar ve §3.1 sayaçlarını üretir.
    /// <para>
    /// <b>İki pass, çünkü "changed" ≠ "to build":</b> <c>Safe</c> (dirty + transitive dependent) will-build
    /// KÜMESİNİ verir; <c>Fast</c> (cascade YOK — upstream'in dondurulmuş imzası okunur) yalnız KENDİ imza
    /// terimi bayatlamış, yani DOĞRUDAN değişmiş projeleri verir. §3.1'in "7 changed projects, 14 to build"
    /// satırı tam olarak bu iki sayının farkıdır ve biri diğerinden türetilemez.
    /// </para>
    /// <para>
    /// <b>inPlace=true:</b> pass kullanıcının O ANKİ çalışma ağacına karşı koşar (bkz. tip özetindeki seam notu).
    /// <b>DependentMode.Safe:</b> Sync'in önizlemesi güvenli tarafta kalır (Fast yalnız "changed" ölçümü içindir).
    /// </para>
    /// <para>
    /// Incremental bir OPTİMİZASYONDUR (Program.ComputeIncremental ile aynı sözleşme): git/discovery/hash
    /// yolundaki HERHANGİ bir hata Sync'i ÖLDÜRMEZ — plan AYNEN (hollow) döner ve sayaçlar raporlanmaz.
    /// </para>
    /// </summary>
    /// <param name="state">[W1] Çağıranın okuduğu build-state map'i — burada AYRICA <c>Load()</c> ÇAĞRILMAZ
    /// (aynı Sync'te iki disk okuması olurdu; bkz. <see cref="RunAsync"/>).</param>
    private async Task<WillBuildOutcome> ComputeWillBuildAsync(SyncWorkspaceCommand cmd, BuildPlan plan,
        ScanResult scan, string? headCommit, IReadOnlyDictionary<string, BuildState> state,
        Action<IpcEvent> emit, CancellationToken ct)
    {
        // Commit'i olmayan repo: IncrementalPlanner'ın hollow kapısı zaten TÜM düğümleri null yapar — pass'i
        // hiç koşturmaya (ve ls-tree/status maliyetine) gerek yok.
        if (headCommit is null)
        {
            emit(Warn("warning: the repository has no commits yet — project states cannot be determined"));
            return new WillBuildOutcome(plan, 0, 0, 0, Known: false);
        }

        try
        {
            var dirtyResult = await git.GetDirtyPathsAsync(ct);
            IReadOnlyList<string> dirty = dirtyResult.Success ? dirtyResult.Value! : [];
            var trackedResult = await git.GetTrackedBlobHashesAsync(ct);
            IReadOnlyDictionary<string, string> tracked = trackedResult.Success
                ? trackedResult.Value! : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Cache SICAK (BuildPlanBuilder az önce değerlendirdi) — bu re-call mtime+size hızlı yolundan
            // bellekten döner. Canlı build ↔ scan yarışında kaybolan dosyalar (null) sessizce elenir.
            var evaluatedById = scan.CsprojPaths
                .Select(p => (Id: Path.GetFullPath(p), Project: cache.GetOrEvaluate(p, evaluator.Evaluate)))
                .Where(x => x.Project is not null)
                .ToDictionary(x => x.Id, x => x.Project!, StringComparer.OrdinalIgnoreCase);

            // Idle'daki will-dot'ların kaynağı BURASIDIR ve onlar BUILD'i tarif eder: Build bir SCC'yi asla
            // derlemez, bu yüzden buradaki kapı SABİT KAPALIDIR (buildCycles: false) ve cycle üyeleri her zaman
            // WillBuild=false gelir. Onları derleyen tek şey ayrı bir koştur (RunMode.Cycles) ve o koşu kendi
            // önizlemesini kendi başlangıcında yayınlar — Sync burada onun adına söz VERMEZ.
            var (safePlan, _) = IncrementalRunBinder.Bind(
                plan, evaluatedById, cmd.RootPath, headCommit, tracked, dirty, state,
                inPlace: true, buildCycles: false, DependentMode.Safe);
            var (fastPlan, _) = IncrementalRunBinder.Bind(
                plan, evaluatedById, cmd.RootPath, headCommit, tracked, dirty, state,
                inPlace: true, buildCycles: false, DependentMode.Fast);

            return new WillBuildOutcome(
                Plan: safePlan,
                Changed: fastPlan.Nodes.Count(n => n.WillBuild == true),
                ToBuild: safePlan.Nodes.Count(n => n.WillBuild == true),
                UpToDate: safePlan.Nodes.Count(n => n.WillBuild == false),
                Known: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Tanı KULLANICIYA gider (Core'un konsola doğrudan yazması gerekmez — [D4] zaten stdout'u yalnız
            // NDJSON'a ayırır): pass atlandığında will-dot'lar sessizce hollow kalacağı için sebebin görünmesi şart.
            emit(Warn($"warning: change detection was skipped — project states stay unknown ({ex.Message})"));
            return new WillBuildOutcome(plan, 0, 0, 0, Known: false);
        }
    }

    /// <summary>Taranan .sln'lerin ad + TAM YOL karşılıkları (E1 "VS'de Aç" bunu <see cref="ProjectNode.SolutionNames"/>
    /// ile eşler). Determinizm [D8]: ada, sonra yola göre sıralı.</summary>
    private static IReadOnlyList<SolutionRef> ToSolutionRefs(ScanResult scan) => scan.SlnPaths
        .Select(p => new SolutionRef(Path.GetFileNameWithoutExtension(p), Path.GetFullPath(p)))
        .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>Kullanıcıya gösterilen kısa commit kimliği (§3.1'deki <c>b7e91d4</c> formatı — 7 hane).</summary>
    private static string ShortSha(string sha) => sha.Length <= 7 ? sha : sha[..7];

    private static SyncProgressEvent Cmd(string line) => new(line, "cmd");
    private static SyncProgressEvent Info(string line) => new(line, "info");
    private static SyncProgressEvent Dim(string line) => new(line, "dim");
    private static SyncProgressEvent Warn(string line) => new(line, "warn");
}
