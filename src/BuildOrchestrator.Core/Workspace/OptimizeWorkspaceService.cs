using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Externals;
using BuildOrchestrator.Core.Formatting;
using BuildOrchestrator.Core.Graph;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Core.State;

namespace BuildOrchestrator.Core.Workspace;

/// <summary>
/// [optimize] Workspace doktorunun TAMAMI, Core'da (D3 — iş mantığı App/Supervisor'a sızmaz): tara → bilinen
/// sorun sınıflarını sırayla ele al → düzeltilemeyeni isim isim raporla → tek özet. <see cref="SyncWorkspaceService"/>'in
/// kardeşidir ama ondan bir bakımdan KESİN olarak ayrılır: Sync salt-okurdur, Optimize DİSKİ DEĞİŞTİRİR.
///
/// <para><b>Adımlar</b> (her biri kendi sayacını taşır; yeni bir sorun sınıfı = yeni bir private adım + yeni
/// bir sayaç, akış düz kalır):</para>
/// <list type="number">
/// <item>eksik NuGet paketleri → per-proje <c>-t:restore</c>;</item>
/// <item>restore SONRASI hâlâ eksik HintPath hedefleri → isimli warn (teşhis);</item>
/// <item>LEGACY projelerdeki stale <c>obj</c> NuGet artıkları → silinir;</item>
/// <item>dosyası kaybolmuş defter girdileri → üç defterde de budanır;</item>
/// <item>öksüz <c>.tmp</c> artıkları → süpürülür.</item>
/// </list>
///
/// <para><b>Harici kökler dahildir:</b> kayıtlı harici projeler sıradan projelerdir — aynı grafa girer, aynı
/// kararı alır, dolayısıyla aynı onarımı görürler. Çözümleme <see cref="ExternalWorkspaceResolver"/>'dır,
/// yani Sync'in, koşu planlayıcısının ve Clean'in kullandığı AYNI kapı (kopya YASAK). Onarım izni çözülen
/// projelerle sınırlıdır: kartı verilmemiş bir dizine dokunulmaz.</para>
///
/// <para><b>Dokunulmayanlar:</b> global NuGet cache'leri, <c>NuGet.config</c>, git (tek bir git komutu bile
/// koşulmaz), worktree havuzu, <c>bin</c>/OutDir, run logları. <b>Build kararlarını değiştirmez:</b> imza
/// kaynak-tabanlıdır, ne restore ne artık temizliği bir projeyi dirty yapar.</para>
///
/// <para><b>Hata modeli:</b> exception IPC sınırını GEÇMEZ. Kilitli dosya hata DEĞİLDİR (warn +
/// <c>LockedFileCount</c>, akış sürer); restore'un exit≠0 dönmesi de hata değildir (warn +
/// <c>FailedRestores</c>, sonraki projeye geçilir — offline senaryosu böyle akar); MSBuild hiç çözülemezse
/// yalnız restore adımı atlanır. Tek tanımlı hata, kökün bulunamamasıdır.</para>
///
/// <para><b>İptal yoktur</b> (bilinçli, K-12): uzun bir restore dizisinin tek kaçışı motorun yeniden
/// başlatılmasıdır. Buna karşılık adımlar arasında <c>ct</c> kontrol edilir ve restore beklenirken periyodik
/// bir kalp atışı satırı basılır — 90 sn'lik sessizlik watchdog'u sağlıklı bir indirmeyi ölü sanmasın.</para>
/// </summary>
/// <param name="restoreInvoker">MSBuild toolset'i LAZY çözen fabrika: yalnız gerçekten restore edilecek proje
/// varsa çağrılır ve <see cref="MsBuildResolveException"/> fırlatması Optimize'ı DÜŞÜRMEZ (K-6). Core,
/// Supervisor'daki <c>MsBuildToolset</c> tipine bağlanamadığı için seam bir <see cref="Func{T, TResult}"/>'tür.</param>
public sealed class OptimizeWorkspaceService(
    WorkspaceScanner scanner,
    CsprojEvaluator evaluator,
    EvaluationCache cache,
    BuildStateStore stateStore,
    SourceHashCache sourceHashes,
    Func<CancellationToken, Task<IMsBuildInvoker>> restoreInvoker)
{
    /// <summary>Bir restore child'ı beklenirken kalp atışları arasındaki bekleme — üretimde 30 sn
    /// (<see cref="HeartbeatInterval"/>). [D8] Testte enjekte edilir: gerçek zaman beklenmez.</summary>
    internal Func<CancellationToken, Task>? HeartbeatDelay { get; set; }

    /// <summary>Kalp atışı aralığı: 90 sn'lik motor-sessizliği watchdog'unun ÜÇTE BİRİ — tek bir NuGet
    /// indirmesi 90 sn'den uzun sessiz kalabilir, bu satır sağlıklı motoru "cevapsız" saymaktan korur.</summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    /// <summary>Öksüz <c>.tmp</c> eşiği: bu kadar eski bir geçici dosya ancak düşmüş bir yazımdan kalmıştır
    /// (rename retry penceresi milisaniyelerdir), aktif bir yazımla yarışmaz.</summary>
    private static readonly TimeSpan OrphanTempAge = TimeSpan.FromHours(1);

    /// <summary>Kaç "unresolved reference" satırı tek tek yazılır; kalanı tek bir toplam satırına düşer.
    /// Sayaç (<see cref="OptimizeCompletedEvent.UnresolvedReferences"/>) her zaman GERÇEK toplamı taşır.</summary>
    private const int UnresolvedDetailCap = 30;

    /// <summary>Legacy bir projenin <c>obj</c>'sinde stale sayıldığında silinen NuGet üretimi artıklar.
    /// <c>obj</c> klasörünün kendisi, diğer içeriği, <c>bin</c> ve OutDir'e DOKUNULMAZ.</summary>
    private static readonly string[] StaleObjArtifactPatterns = ["project.assets.json", "*.nuget.g.props", "*.nuget.g.targets"];

    public async Task RunAsync(OptimizeWorkspaceCommand cmd, Action<IpcEvent> emit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentNullException.ThrowIfNull(emit);

        emit(new OptimizeStartedEvent(cmd.RootPath));

        // Girdi kapısı: bozuk kök IPC sınırında EXCEPTION'a değil TANIMLI bir hata event'ine dönüşür
        // (SyncWorkspaceService'in kalıbı; kod Optimize'a özgüdür çünkü App yalnız optimize yüzeyini bırakır).
        if (!Directory.Exists(cmd.RootPath))
        {
            emit(new ErrorEvent("optimizeFailed", $"Workspace root not found: '{cmd.RootPath}'."));
            return;
        }

        // [harici projeler] Ana tarama + kayıtlı harici kökler, TEK çalışma alanı — Sync'in, koşu
        // planlayıcısının ve Clean'in kullandığı AYNI çözümleyici.
        var workspace = ExternalWorkspaceResolver.Resolve(scanner.Scan(cmd.RootPath), cmd.ExternalProjects, scanner);
        foreach (var problem in workspace.Problems)
            emit(Warn(PlanProgressLines.ExternalNotOptimized(problem.Name, problem.Problem)));

        // Defter budaması KÖK BAŞINA yürür: defterlerin anahtarı TAM dosya yoludur, yani ana kökün öneki
        // harici kökü KAPSAMAZ. Clean'in aynı sebeple kurduğu kök listesinin ikizi.
        var roots = new List<string> { Path.TrimEndingDirectorySeparator(Path.GetFullPath(cmd.RootPath)) };
        foreach (var external in workspace.Roots)
        {
            string searchRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(external.SearchRoot));
            if (!roots.Contains(searchRoot, StringComparer.OrdinalIgnoreCase)) roots.Add(searchRoot);
        }

        var scan = workspace.Scan;
        var solutionRefs = SolutionMapper.MapRefs(scan.SlnPaths, scan.CsprojPaths);
        var projects = scan.CsprojPaths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => cache.GetOrEvaluate(p, evaluator.Evaluate))
            .OfType<EvaluatedProject>()
            .OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase) // determinizm [D8]
            .ToList();

        var tally = new Tally { ProjectCount = projects.Count };

        ct.ThrowIfCancellationRequested();
        await RestoreMissingPackagesAsync(projects, solutionRefs, tally, emit, ct);

        ct.ThrowIfCancellationRequested();
        ReportUnresolvedReferences(projects, tally, emit);

        ct.ThrowIfCancellationRequested();
        RemoveStaleObjLeftovers(projects, tally, emit);

        ct.ThrowIfCancellationRequested();
        PruneDeadCacheEntries(roots, tally, emit);

        ct.ThrowIfCancellationRequested();
        SweepOrphanTempFiles(tally, emit);

        EmitSummary(tally, emit);
        emit(new OptimizeCompletedEvent(
            tally.ProjectCount, tally.RestoredProjects, tally.FailedRestores, tally.UnresolvedReferences,
            tally.StaleObjCleaned, tally.PrunedStateEntries, tally.PrunedCacheEntries, tally.PrunedSourceHashEntries,
            tally.RemovedTempFiles, tally.LockedFileCount, tally.BytesReclaimed));
    }

    // ---------------------------------------------------------------- adım 1: eksik NuGet paketleri

    /// <summary>
    /// [K-1] Needy tanımı: csproj'un YANINDA <c>packages.config</c> VAR <b>ve</b> en az bir NuGet
    /// <c>packages</c> HintPath'inin hedefi diskte YOK. <c>packages.config</c>'in İÇERİĞİ parse EDİLMEZ —
    /// NuGet <c>repositoryPath</c> override'ı yüzünden "sln yanındaki packages" varsayımı güvenilmezdir;
    /// paketlerin gerçek yerini HintPath'in kendisi söyler. Aynı mekanizma restore SONRASI teşhisi de besler.
    /// </summary>
    private async Task RestoreMissingPackagesAsync(
        IReadOnlyList<EvaluatedProject> projects, IReadOnlyDictionary<string, IReadOnlyList<Contracts.Model.SolutionRef>> solutionRefs,
        Tally tally, Action<IpcEvent> emit, CancellationToken ct)
    {
        var needy = projects.Where(IsNeedy).ToList();
        emit(Info(needy.Count == 0
            ? $"checking NuGet packages — {projects.Count} projects, all packages present"
            : $"checking NuGet packages — {projects.Count} projects, {needy.Count} need restore"));
        if (needy.Count == 0) return;

        IMsBuildInvoker invoker;
        try
        {
            invoker = await restoreInvoker(ct);
        }
        catch (MsBuildResolveException ex)
        {
            // [K-6] Toolset yoksa Optimize BAŞARISIZ OLMAZ: yalnız bu adım düşer, kalan adımlar koşar.
            emit(Warn($"warning: MSBuild.exe could not be resolved — package restore skipped ({ex.Message})"));
            return;
        }

        foreach (var project in needy)
        {
            ct.ThrowIfCancellationRequested();
            string name = project.AssemblyName;
            string solutionDir = SolutionDirResolver.Resolve(
                project.Path, solutionRefs.TryGetValue(project.Path, out var refs) ? refs : []);
            var args = MsBuildArguments.RestorePackagesConfig(project.Path, solutionDir);
            // Komut satırı argüman listesinin TEK kaynağından yazılır; "msbuild " öneki konsolun komut rengini verir.
            emit(Cmd("msbuild " + string.Join(' ', args)));

            var result = await RunRestoreWithHeartbeatAsync(invoker, project, solutionDir, name, emit, ct);
            if (result.ExitCode == 0 && !result.TimedOut && !result.Killed)
            {
                tally.RestoredProjects++;
            }
            else
            {
                // [K-10] exit≠0 HATA DEĞİLDİR: offline/erişilemez kaynak senaryosu buradan akar, sıradaki
                // projeye geçilir. Kullanıcı hangi projenin düştüğünü isimle görür.
                tally.FailedRestores++;
                emit(Warn($"restore failed for {name} (exit {result.ExitCode})"));
            }
        }
    }

    private static bool IsNeedy(EvaluatedProject project)
    {
        string dir = Path.GetDirectoryName(project.Path)!;
        if (!File.Exists(Path.Combine(dir, "packages.config"))) return false;
        return project.HintPaths.Any(h => IsNuGetPackagesHint(h) && !TargetExists(dir, h));
    }

    /// <summary>
    /// [K-13] Restore child'ını beklerken periyodik bir <c>dim</c> satırı basar. Beklemenin KENDİSİ enjekte
    /// edilebilir bir dikiştir — testte senkron bir sinyale çevrilir, gerçek zaman beklenmez [D8].
    /// </summary>
    private async Task<MsBuildInvokeResult> RunRestoreWithHeartbeatAsync(
        IMsBuildInvoker invoker, EvaluatedProject project, string solutionDir, string name,
        Action<IpcEvent> emit, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var restore = invoker.RestoreAsync(new MsBuildRestoreRequest(project.Path, solutionDir), line => emit(Dim(line)), ct);
        var beat = HeartbeatDelay ?? (token => Task.Delay(HeartbeatInterval, token));

        while (true)
        {
            var finished = await Task.WhenAny(restore, beat(ct));
            if (finished == restore) break;
            emit(Dim($"still restoring {name} ({DurationFormat.Elapsed(started.ElapsedMilliseconds)})"));
        }
        return await restore;
    }

    // ---------------------------------------------------------------- adım 2: kırık referans teşhisi

    /// <summary>
    /// Restore DENENDİKTEN SONRA hâlâ diskte olmayan HintPath hedefleri: NuGet <c>packages</c> hedefleri
    /// (sürüm drift'i — <c>HintPath</c> ≠ <c>packages.config</c> sürümü) ve üreticisi olmayan
    /// <c>bin</c> hedefleri (eksik OSYS platform DLL'i). Bugün bunlar run ortasında kriptik bir derleme
    /// hatası olarak patlıyor; burada tık anında isimli, eyleme dönük bir listeye dönüşürler.
    /// </summary>
    private static void ReportUnresolvedReferences(IReadOnlyList<EvaluatedProject> projects, Tally tally, Action<IpcEvent> emit)
    {
        var producers = ProducerMapBuilder.Build(projects);
        int shown = 0;

        foreach (var project in projects)
        {
            string dir = Path.GetDirectoryName(project.Path)!;
            foreach (var hint in project.HintPaths)
            {
                bool isPackages = IsNuGetPackagesHint(hint);
                // Üreticisi OLAN bir bin hedefi eksik olabilir ve bu NORMALDİR: onu bu repo derler.
                bool isPlatform = !isPackages
                    && !producers.DllToProducer.ContainsKey(hint.BaseName)
                    && HintPathClassifier.IsUnderBin(hint.Raw);
                if ((!isPackages && !isPlatform) || !IsResolvable(hint) || TargetExists(dir, hint)) continue;

                tally.UnresolvedReferences++;
                if (shown >= UnresolvedDetailCap) continue; // sayaç tam toplamı taşır, konsol boğulmaz
                shown++;
                string expected = ResolveTarget(dir, hint);
                emit(Warn(isPackages
                    ? $"unresolved reference: {project.AssemblyName}: {hint.BaseName} — {expected}"
                    : $"unresolved reference: {project.AssemblyName}: {hint.BaseName} — {expected} — build the producing solution first"));
            }
        }

        int hidden = tally.UnresolvedReferences - shown;
        if (hidden > 0) emit(Warn($"... and {hidden} more unresolved references"));
    }

    // ---------------------------------------------------------------- adım 3: stale obj artıkları

    /// <summary>
    /// [K-7] YALNIZ legacy (<c>IsSdkStyle == false</c>) projelerde. SDK-style bir projede
    /// <c>project.assets.json</c> MEŞRUDUR ve silinirse motor onu restore ETMEZ (packages.config yoktur) →
    /// build "assets file not found" ile kırılır. Bu, gevşetilemez bir kuraldır.
    /// <para>Koşu başındaki tespit (<c>StaleObjRunStartWarner</c>) salt-teşhis KALIR; silme yalnız
    /// kullanıcı-tetikli Optimize'dadır.</para>
    /// </summary>
    private static void RemoveStaleObjLeftovers(IReadOnlyList<EvaluatedProject> projects, Tally tally, Action<IpcEvent> emit)
    {
        foreach (var project in projects)
        {
            if (project.IsSdkStyle) continue;
            if (project.TargetFrameworkMoniker is not { } tfm || string.IsNullOrWhiteSpace(tfm)) continue; // TFM okunamadı → sessizce atla
            if (!StaleObjDetector.Inspect(project.Path, tfm).IsStale) continue;

            string objDir = Path.Combine(Path.GetDirectoryName(project.Path)!, "obj");
            bool removedAny = false;
            foreach (string pattern in StaleObjArtifactPatterns)
            {
                foreach (string file in SafeEnumerate(objDir, pattern))
                {
                    long size = SafeLength(file);
                    if (TryDelete(file)) { tally.BytesReclaimed += size; removedAny = true; }
                    else { tally.LockedFileCount++; emit(Warn($"{project.AssemblyName}: {Path.GetFileName(file)} could not be removed (in use)")); }
                }
            }
            if (removedAny)
            {
                tally.StaleObjCleaned++;
                emit(Info($"{project.AssemblyName} — stale NuGet leftovers removed from obj"));
            }
        }
    }

    // ---------------------------------------------------------------- adım 4-5: defter hijyeni

    /// <summary>Üç defter de aynı ölçütle budanır: anahtarın gösterdiği dosya diskte yoksa girdi ölüdür. İlk
    /// ikisi csproj yoluyla, üçüncüsü KAYNAK DOSYA yoluyla anahtarlıdır — bu yüzden en hızlı biriken odur ve
    /// ayrı sayılır. Her kök ayrı süpürülür (harici kök ana kökün öneki ALTINDA değildir).</summary>
    private void PruneDeadCacheEntries(IReadOnlyList<string> roots, Tally tally, Action<IpcEvent> emit)
    {
        tally.PrunedStateEntries = roots.Sum(stateStore.PruneMissingUnderRoot);
        tally.PrunedCacheEntries = roots.Sum(cache.PruneMissingUnderRoot);
        tally.PrunedSourceHashEntries = roots.Sum(sourceHashes.PruneMissingUnderRoot);
        emit(tally.PrunedTotal == 0
            ? Dim("caches are clean")
            : Info($"pruned {tally.PrunedStateEntries} build-state, {tally.PrunedCacheEntries} evaluation-cache "
                   + $"and {tally.PrunedSourceHashEntries} source-hash entries"));
    }

    private void SweepOrphanTempFiles(Tally tally, Action<IpcEvent> emit)
    {
        tally.RemovedTempFiles = stateStore.SweepOrphanTempFiles(OrphanTempAge)
                                 + cache.SweepOrphanTempFiles(OrphanTempAge)
                                 + sourceHashes.SweepOrphanTempFiles(OrphanTempAge);
        if (tally.RemovedTempFiles > 0) emit(Dim($"swept {tally.RemovedTempFiles} orphaned temp files"));
    }

    // ---------------------------------------------------------------- özet

    private static void EmitSummary(Tally tally, Action<IpcEvent> emit)
    {
        bool foundNothing = tally.RestoredProjects == 0 && tally.FailedRestores == 0 && tally.UnresolvedReferences == 0
            && tally.StaleObjCleaned == 0 && tally.PrunedTotal == 0 && tally.RemovedTempFiles == 0;

        emit(Info(foundNothing
            ? $"Optimize complete — nothing to fix, {tally.ProjectCount} projects healthy"
            : $"Optimize complete — {tally.RestoredProjects} restored · {tally.UnresolvedReferences} unresolved refs · "
              + $"{tally.StaleObjCleaned} obj cleaned · {tally.PrunedTotal} cache entries pruned · {ByteFormat.Size(tally.BytesReclaimed)} reclaimed"));

        if (tally.LockedFileCount > 0)
            emit(Warn($"warning: {tally.LockedFileCount} files could not be removed (in use) — close the running application and run Optimize again"));
    }

    // ---------------------------------------------------------------- HintPath çözümü (K-3)

    /// <summary><c>$(...)</c> içeren ham HintPath MSBuild property'si taşır ve bu servis MSBuild
    /// değerlendirmesi YAPMAZ: böyle bir yol HİÇBİR sayıma girmez (ne needy tetikler ne unresolved sayılır) —
    /// çözülemeyen bir yol hakkında sessiz kalmak, yanlış bir teşhis üretmekten iyidir [K-1].</summary>
    private static bool IsResolvable(RawHintPath hint) => !hint.Raw.Contains("$(", StringComparison.Ordinal);

    private static bool IsNuGetPackagesHint(RawHintPath hint) =>
        IsResolvable(hint) && HintPathClassifier.IsNuGetPackagesPath(hint.Raw);

    /// <summary>[K-3] Ham HintPath csproj DİZİNİNE göre mutlaklaştırılır — <c>EvaluatedProject.HintPaths</c>
    /// ham göreli metin taşır ve repoda bunu çözen başka bir yardımcı yoktur.</summary>
    private static string ResolveTarget(string csprojDir, RawHintPath hint)
    {
        try { return Path.GetFullPath(Path.Combine(csprojDir, hint.Raw)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return hint.Raw; }
    }

    private static bool TargetExists(string csprojDir, RawHintPath hint)
    {
        try { return File.Exists(ResolveTarget(csprojDir, hint)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; } // okunamıyorsa var say: yanlış alarm üretme
    }

    // ---------------------------------------------------------------- dosya sistemi yardımcıları (never-throw)

    // Clean'in silme yardımcılarına benzerler ama BİLİNÇLİ olarak ayrıdırlar: Clean bir klasör ağacını
    // götürür ve kısa bir sharing-violation penceresini yutmak için üç deneme + backoff kullanır. Buradaki
    // silme tek tek, adı bilinen birkaç artık dosyadır; kilitliyse beklemenin anlamı yoktur — kilidin sahibi
    // çalışan bir VS/OSYS'tir ve kullanıcıya "kapat, yeniden çalıştır" demek doğru cevaptır. Bu yüzden tek
    // deneme, backoff yok ve dolayısıyla enjekte edilecek bir gecikme dikişi de yok.

    private static IEnumerable<string> SafeEnumerate(string dir, string pattern)
    {
        try { return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, pattern).ToList() : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static long SafeLength(string file)
    {
        try { return new FileInfo(file).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    private static bool TryDelete(string file)
    {
        try { File.Delete(file); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    // ---------------------------------------------------------------- sayaçlar + satır fabrikaları

    /// <summary>Adımların biriktirdiği sayaçlar — <see cref="OptimizeCompletedEvent"/>'in kaynağı. Sınıf olması
    /// bilinçlidir: her adım kendi alanını yazar, hiçbir sayaç iki adımda hesaplanmaz.</summary>
    private sealed class Tally
    {
        public int ProjectCount;
        public int RestoredProjects;
        public int FailedRestores;
        public int UnresolvedReferences;
        public int StaleObjCleaned;
        public int PrunedStateEntries;
        public int PrunedCacheEntries;
        public int PrunedSourceHashEntries;
        public int RemovedTempFiles;
        public int LockedFileCount;
        public long BytesReclaimed;

        /// <summary>Üç defterden budanan toplam — özet satırı ve "hiçbir şey bulunamadı" kararı bunu okur.</summary>
        public int PrunedTotal => PrunedStateEntries + PrunedCacheEntries + PrunedSourceHashEntries;
    }

    private static OptimizeProgressEvent Cmd(string line) => new(line, "cmd");
    private static OptimizeProgressEvent Info(string line) => new(line, "info");
    private static OptimizeProgressEvent Dim(string line) => new(line, "dim");
    private static OptimizeProgressEvent Warn(string line) => new(line, "warn");
}
