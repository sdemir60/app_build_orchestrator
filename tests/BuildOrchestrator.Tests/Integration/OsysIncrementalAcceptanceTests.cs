using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Tests.Supervisor;
using Xunit;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.Integration;

/// <summary>
/// [It-3 KABUL · Task 19] Gerçek OSYS reposunu (<c>D:\Projects\Delta\OSYS</c>) gerçek Supervisor + gerçek
/// <c>MSBuild.exe</c> ile <b>incremental</b> derler ve It-3'ün kalbini CANLI sayılarla kanıtlar:
/// <list type="number">
/// <item><b>Incremental all-skipped:</b> bir <c>Build</c> başarıyla BuildState kurar; kaynak DEĞİŞMEDEN ikinci
///   <c>Build</c> → Run 1'de <b>satır persist eden</b> projelerin HEPSİ "skipped — up to date". (En güçlü tek
///   gösterim.) [A2] Bu küme "Run 1'de başarılı olan HER proje" DEĞİLDİR: depIssue taşıyan bir success taze imza
///   persist etmez, dolayısıyla Run 2'de MEŞRU olarak yeniden derlenir (bkz. <c>DepIssueCarriers</c>).</item>
/// <item><b>Minimal rebuild (L1→L3 dirty):</b> kurulu state üstünde TEK bir projenin kaynağı "dirty" simüle
///   edilir (OSYS working tree'ye DOKUNULMADAN — sentetik dirty path) → yalnız o proje + transitive dependent'ları
///   WillBuild=true, ilgisiz projeler skip kalır. (Gerçek OSYS grafı + gerçek committed hash'ler + gerçek state.)</item>
/// </list>
/// <para><b>[DEĞİŞEN KURAL — spec 2026-09-18 §1-1]</b> Üçüncü bir iddia vardı — "Branch-bounce: A→B→A seçimi doğru
/// worktree/in-place matrisi + K3 niyet satırı üretir (<c>WorktreeManager.PlanWorktree</c>)". Worktree modu
/// kalktı; o iddia yalnız worktree kararını ölçüyordu, bu yüzden düştü. Minimal-rebuild'in sentetik değişikliği
/// de worktree'nin fiziksel-yol eşleyicisini kullanıyordu; eşleyici kalktığı için değişiklik artık özet
/// önbelleğine tohumlanır (aşağıda).</para>
/// <b>[K1]</b> OSYS aktif branch + HEAD koşu boyunca ASLA değişmez (assert öncesi/sonrası). Normal suite'ten
/// HARİÇ (<c>[Trait("Category","Acceptance")]</c>). [D8] sleep-poll YOK — event-driven, sınırlı bekleme.
/// </summary>
[Trait("Category", "Acceptance")]
public sealed class OsysIncrementalAcceptanceTests(ITestOutputHelper output)
{
    private const string OsysRoot = @"D:\Projects\Delta\OSYS";
    private const int Parallelism = 6;
    private static readonly TimeSpan OverallBudget = TimeSpan.FromMinutes(30);

    private static string EvidencePath =>
        Environment.GetEnvironmentVariable("BO_IT3_EVIDENCE")
        ?? Path.Combine(Path.GetTempPath(), "bo-it3-acceptance-evidence.md");

    private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    /// <param name="DepIssueCarriers">[A2] BAŞARILI olduğu hâlde depIssue TAŞIYAN projeler — bir bağımlılığı bu
    /// run'da fail ettiği için onun BAYAT (önceki) çıktısına link'lidirler. A2'den beri bunlar taze imza persist
    /// ETMEZ, dolayısıyla bir sonraki Build'de MEŞRU olarak yeniden derlenirler.</param>
    private sealed record RunOutcomeData(
        IReadOnlyList<string> Succeeded,
        IReadOnlyList<(string ProjectId, string Reason)> Failed,
        IReadOnlyList<(string ProjectId, string Reason)> Skipped,
        IReadOnlyList<string> DepIssueCarriers,
        RunStartedEvent? Started,
        RunCompletedEvent? Completed);

    [SkippableFact]
    public async Task Osys_incremental_build_skips_all_up_to_date_then_minimal_rebuild_on_a_single_dirty_project()
    {
        Skip.IfNot(Directory.Exists(OsysRoot), $"OSYS yok ({OsysRoot}) — It-3 kabul koşusu atlandı.");
        using var overall = new CancellationTokenSource(OverallBudget);
        try { _ = await new MsBuildResolver(new ProcessRunner()).ResolveAsync(ct: overall.Token); }
        catch (MsBuildResolveException ex) { Skip.If(true, "MSBuild.exe yok — It-3 kabul koşusu atlandı: " + ex.Message); }

        // [K1] öncesi
        var (headBefore, branchBefore) = OsysRebuildAcceptanceTests.ReadOsysHeadAndBranch();

        // İki Build AYNI cacheRoot'u (dolayısıyla AYNI build-state.json'ı) paylaşır: logsDir = <shared>\logs →
        // cacheRoot = <shared> (Program.cs: cacheRoot = Path.GetDirectoryName(logsRoot)).
        string shared = Directory.CreateTempSubdirectory("bo-it3-").FullName;
        string logsDir = Path.Combine(shared, "logs");
        Directory.CreateDirectory(logsDir);

        // ---- RUN 1: incremental Build, state YOK → derlenebilir HER ŞEY derlenir, başarılılar persist eder.
        var run1 = await RunBuildAsync(logsDir, "it3-build-1", overall.Token);
        Assert.NotNull(run1.Completed);
        Assert.NotNull(run1.Started);
        Assert.True(run1.Succeeded.Count > 100,
            Inv($"Run 1'de beklenenden az proje başarılı — state kurulamaz: {run1.Succeeded.Count}"));

        // build-state.json GERÇEKTEN yazıldı mı (persist kanıtı — sonraki Build'in incremental olmasının önkoşulu).
        var store = new BuildStateStore(shared);
        var stateAfterRun1 = store.Load();
        // [A2] Persist EDEN küme = TÜM success'ler (DepIssue notlu olsun olmasın — bir success DepIssue taşısa
        // bile PersistBuildStateOnSuccess HER ZAMAN çağrılır, not yalnız kaydın KENDİSİNE `DepIssue:
        // depIssueRoots is not null` olarak işlenir; RunCoordinator.ReportProjectResult) + KANITLI (evidence)
        // FAILED'ler.
        // [DEĞİŞEN KURAL] Muafiyet listesinden depIssue taşıyanlar ÇIKTI: eskiden (A2 öncesi) "deftere HİÇ
        // yazma" kuralı vardı; ölçüldü ki o kural defteri hiç ilerletmiyordu (24 hatanın depIssue'su 96 projeye
        // yayıldığı bir koşuda 74 başarının sıfırı yazıldı). Beklenti artık başarı sayısının kendisidir.
        // [DEĞİŞEN KURAL — spec 2026-09-18 §1-14] Eski iddia: "FAILED'ler BOŞ store'da geçersizleştirilecek
        // kayıt bulamadığı için satır EKLEMEZ." Artık yanlış: kanıtlı bir derleyici hatası (evidence-based red)
        // kayıt yoksa bile taze bir satır AÇAR — `FailedSignature`/`FailedAt` ile, `BuiltSignature: null`
        // (RunCoordinator.InvalidateBuildStateOnFailure, §8.8: "opening a fresh record when the project has
        // never been seen before, so a first-ever compile failure is not lost"). Yalnız KANITSIZ bir
        // başarısızlık (timeout/stopped/invoke error/yakınsamayan SCC üyesi) hâlâ eski davranışı korur: kayıt
        // yoksa hiçbir şey açılmaz.
        // SONUÇ (bu değişikliğin ikinci etkisi): `stateAfterRun1.Count` (TOPLAM kayıt) artık "kaç başarı persist
        // edildi" sorusuna CEVAP VERMEZ — kanıtlı başarısızlıklar da bu sayıyı şişirir (ör. 131 başarı + 13
        // kanıtlı hata = 144 TOPLAM kayıt, ama başarı olarak persist edilen yalnız 131'dir). `stateAfterRun1.Count
        // >= run1.Succeeded.Count` bu yüzden artık SESSİZCE ZAYIFLAR: kanıtlı hatalar toplamı şişirdiği için bir
        // eksik başarı bile bu iddiayı KIRMIZI vermeden geçebilir (131 başarıdan 13'ü kaybolsa bile TOPLAM yine
        // ≥131 kalabilir). Alt sınır bu yüzden TOPLAM değil, özellikle `LastResult == Succeeded` olan kayıt
        // sayısını okumalıdır.
        //
        // İddia ZAYIFLAMAZ: derlenen her BAŞARILI proje için KURAL OLARAK bir BAŞARI kaydı beklenir — biri bile
        // eksik kalırsa sayı bu alt sınırın ALTINA düşer ve test kırmızı verir. Muafiyet listesi tam OLSUN diye:
        // bir persist-etmeme yolu daha vardır ve o da bu iddiaya girmez — PersistBuildStateOnSuccess
        // (RunCoordinator.cs:736-738) incremental planda bu proje için İMZA YOKSA (SignatureById miss: hollow /
        // imzası hesaplanamamış proje) satır YAZMADAN döner. Yani buradaki bir kırmızı "persist eksik" kadar
        // "imzasız success var" da demek olabilir; teşhis için önce kanıt dosyasındaki Run 1 satırına ve
        // build-state.json'a bakılmalı.
        int run1PersistExpected = run1.Succeeded.Count;
        int run1SuccessRecords = stateAfterRun1.Values.Count(s => s.LastResult == BuildResult.Succeeded);
        Assert.True(run1SuccessRecords >= run1PersistExpected,
            Inv($"başarı olarak persist edilen kayıt ({run1SuccessRecords}) < persist etmesi beklenen ({run1PersistExpected} = başarılı) — persist eksik."));
        // [DEĞİŞEN KURAL — spec 2026-09-18 §1-14] bkz. aşağıdaki "KABUL İDDİALARI": Run 2'nin pre-skip etmesi
        // beklenen küme burada hesaplanır ki evidence metni (aşağıda) ve o iddia AYNI sayıyı okusun (kopya
        // YASAK, CLAUDE.md). Yalnız `stateAfterRun1`e bağlıdır, Run 2'yi beklemez.
        int cleanRows = stateAfterRun1.Values.Count(s => s.LastResult == BuildResult.Succeeded && !s.DepIssue);

        // ---- RUN 2: kaynak DEĞİŞMEDEN yeniden Build → önceki başarılıların HEPSİ "skipped — up to date".
        var run2 = await RunBuildAsync(logsDir, "it3-build-2", overall.Token);
        Assert.NotNull(run2.Completed);

        // [DEĞİŞEN KURAL/Task 2] reason artık YALIN ("skipped — " öneki İÇİNDE taşınmaz) — tek kaynak SkipReasons.
        var run2UpToDate = new HashSet<string>(
            run2.Skipped.Where(s => s.Reason == SkipReasons.UpToDate).Select(s => s.ProjectId),
            StringComparer.OrdinalIgnoreCase);
        var run1Succeeded = new HashSet<string>(run1.Succeeded, StringComparer.OrdinalIgnoreCase);

        // Run 1'de başarılı olan projelerden Run 2'de "up to date" skip EDİLMEYENLER.
        var notSkipped = run1Succeeded.Where(id => !run2UpToDate.Contains(id)).ToList();
        // Run 2'de gerçekten dispatch edilen (derlenen) projeler.
        var run2Started = run2.Succeeded.Concat(run2.Failed.Select(f => f.ProjectId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Run 2'de derlenmesi MEŞRU olan küme = Run 1'in FAILED'leri + Run 1'de depIssue TAŞIYAN success'ler.
        // [DEĞİŞEN KURAL] İkinci grubun gerekçesi artık "persist etmiyorlar" değil, "kayıtları DepIssue notlu
        // ve WillBuildEvaluator o notu görünce yine derliyor" — küme aynı. Bundan ÖNCE bu iddialar
        // "notSkipped BOŞ" ve "run2Started ≤ run1.Failed" idi — o
        // beklenti yalnız Run 1 TAMAMEN yeşilken (failed=0 ⇒ carrier=0) doğrudur; bir failure olduğunda onun
        // succeeded dependent'ları meşru olarak yeniden derlenir ve eski iddialar YANLIŞ kırmızı verirdi.
        var run1LegitimateRebuild = run1.Failed.Select(f => f.ProjectId).Concat(run1.DepIssueCarriers)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var notSkippedUnexplained = notSkipped.Except(run1LegitimateRebuild, StringComparer.OrdinalIgnoreCase).ToList();
        var run2Unexplained = run2Started.Except(run1LegitimateRebuild, StringComparer.OrdinalIgnoreCase).ToList();

        // ---- MINIMAL REBUILD (in-process, SALT-OKUR OSYS): TEK proje değişti → o + transitive dependent'ları true.
        var (plan, evaluatedById) = BuildPlanAndEvaluated();

        // Dependent'ı OLAN bir proje seç (cascade'i gösterebilmek için) — bir başkasının Dependencies'inde geçen.
        var dependentsOf = ReverseDependents(plan);
        var targetNode = plan.Nodes.FirstOrDefault(n =>
            !n.InCycle && dependentsOf.TryGetValue(n.Id, out var deps) && deps.Count > 0
            && evaluatedById.TryGetValue(n.Id, out var ev) && ev.CompileFiles.Count > 0);
        Skip.If(targetNode is null, "dependent'ı olan + compile dosyası olan bir proje bulunamadı — minimal-rebuild atlandı.");

        // Sentetik değişiklik — OSYS working tree'ye DOKUNULMADAN: hedef projenin bir kaynak dosyası için
        // izole özet önbelleğine, dosyanın GERÇEK boyut+mtime'ıyla ama FARKLI bir özetle bir kayıt tohumlanır.
        // Önbellek boyut+mtime eşleşince dosyayı AÇMAZ (SourceHashCache.HashOf), yani binder o dosyanın içeriğini
        // "değişmiş" okur. İmzanın yol terimi değişmez, içerik terimi değişir — "o dosya düzenlenmiş"
        // senaryosunun birebir aynısı, tek fark gerçek dosyanın okunmaması. Kayıt önbelleğin kendi test
        // seam'iyle (SourceHashCache.Seed) yazılır — disk biçimi yalnız o sınıfta tanımlıdır.
        string targetFile = Path.GetFullPath(evaluatedById[targetNode!.Id].CompileFiles[0]);
        string cacheRoot = Directory.CreateTempSubdirectory("bo-it3-hash-").FullName;
        var hashes = new SourceHashCache(Path.Combine(cacheRoot, SourceHashCache.FileName));
        hashes.Seed(targetFile, "SIMULATED-EDIT");
        var binder = new IncrementalRunBinder(plan, evaluatedById, OsysRoot, hashes);
        var (dirtyPlan, _) = binder.Bind(stateAfterRun1, buildCycles: false, DependentMode.Safe);
        var dirtyById = dirtyPlan.Nodes.ToDictionary(n => n.Id, n => n.WillBuild, StringComparer.OrdinalIgnoreCase);

        var transitiveDependents = TransitiveDependents(targetNode.Id, dependentsOf);
        // Cycle üyeleri HER ZAMAN WillBuild=false taşır (WillBuildEvaluator: inCycle → false); imza cascade'i onların
        // ÜZERİNDEN downstream'e yine yayılır ama KENDİLERİ derlenmez — cascade assert'inden hariç tutulur.
        var inCycle = new HashSet<string>(
            plan.Nodes.Where(n => n.InCycle).Select(n => n.Id), StringComparer.OrdinalIgnoreCase);
        // [A3] Cycle-tangled transitive under-build KAPANDI: bir SCC artık component başına TEK kompozit imza
        // taşır (üyelerin kendi terimleri + SCC-dışı upstream imzaları), bu yüzden cascade cycle'ların üzerinden
        // de EKSİKSİZ yayılır ve sonuç ziyaret sırasından bağımsızdır. It-3'te "bilinen sınır" diye kayda geçen
        // ve aşağıda ÇOĞUNLUĞA zayıflatılmış olan iddia artık TAM eşitlik olarak koşulur. Doğrudan (cycle-dışı)
        // dependent'lar ayrıca ayrı bir alt-küme olarak da raporlanır (regresyonda hangi katmanın kırıldığını
        // gösterir).
        var directDependents = dependentsOf.GetValueOrDefault(targetNode.Id, [])
            .Where(id => !inCycle.Contains(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(dirtyById[targetNode.Id] == true, "dirty edilen hedef proje WillBuild=true olmalı.");
        var cascadeMisses = directDependents
            .Where(id => dirtyById.TryGetValue(id, out var wb) && wb != true).ToList();

        var transNonCycle = transitiveDependents.Where(id => !inCycle.Contains(id)).ToList();
        int transFlipped = transNonCycle.Count(id => dirtyById.TryGetValue(id, out var wb) && wb == true);

        // İlgisiz (hedef değil + dependent değil + cycle değil), state'i olan bir proje skip (false) kalmalı.
        var unrelatedClean = plan.Nodes.Where(n =>
            !n.InCycle && n.Id != targetNode.Id && !transitiveDependents.Contains(n.Id)
            && dirtyById.TryGetValue(n.Id, out var wb) && wb == false).ToList();

        // ---- [K1] sonrası — HEAD + branch DEĞİŞMEDİ.
        var (headAfter, branchAfter) = OsysRebuildAcceptanceTests.ReadOsysHeadAndBranch();

        WriteEvidence(sb =>
        {
            sb.AppendLine("# It-3 Acceptance — OSYS Incremental (ölçülen, canlı koşu)");
            sb.AppendLine();
            sb.AppendLine(Inv($"- Zaman damgası (UTC): {DateTimeOffset.UtcNow:O}"));
            sb.AppendLine(Inv($"- RootPath: {OsysRoot} · Parallelism: {Parallelism}"));
            sb.AppendLine();
            sb.AppendLine("## Run 1 (Build, state YOK — hepsi derlenir)");
            sb.AppendLine(Inv($"- TotalProjects: {run1.Started?.TotalProjects} · Succeeded: {run1.Completed?.Succeeded} · Failed: {run1.Completed?.Failed} · Skipped: {run1.Completed?.Skipped} · Süre: {run1.Completed?.DurationMs} ms"));
            sb.AppendLine(Inv($"- build-state.json kayıt sayısı (Run 1 sonrası): {stateAfterRun1.Count} · beklenen alt sınır: {run1PersistExpected} (başarılı — depIssue taşıyanlar da NOTLA yazılır)"));
            sb.AppendLine();
            sb.AppendLine("## Run 2 (Build, kaynak DEĞİŞMEDEN — incremental)");
            sb.AppendLine(Inv($"- TotalProjects: {run2.Started?.TotalProjects} · Succeeded: {run2.Completed?.Succeeded} · Failed: {run2.Completed?.Failed} · Skipped: {run2.Completed?.Skipped} · Süre: {run2.Completed?.DurationMs} ms"));
            sb.AppendLine(Inv($"- 'skipped — up to date' sayısı: {run2UpToDate.Count} · Run 1'de NOTSUZ BAŞARI persist edilen satır: {cleanRows} / toplam kayıt {stateAfterRun1.Count} (ilk ikisi EŞİT olmalı — bkz. KABUL İDDİALARI)"));
            sb.AppendLine(Inv($"- Run 1 başarılı ({run1Succeeded.Count}) → Run 2'de up-to-date SKIP edilmeyen: {notSkipped.Count} · bunlardan A2 ile AÇIKLANAMAYAN: {notSkippedUnexplained.Count} (0 OLMALI)"));
            sb.AppendLine(Inv($"- [A2] Run 1: failed={run1.Failed.Count} + depIssue taşıyan success={run1.DepIssueCarriers.Count} → Run 2'de derlenmesi MEŞRU: {run1LegitimateRebuild.Count}"));
            sb.AppendLine(Inv($"- Run 2'de dispatch edilen (derlenen) proje: {run2Started.Count} · bunlardan MEŞRU kümede OLMAYAN: {run2Unexplained.Count} (0 OLMALI)"));
            sb.AppendLine();
            sb.AppendLine("## Minimal rebuild (tek proje değişti, in-process — gerçek OSYS grafı)");
            sb.AppendLine(Inv($"- Değiştirilen hedef: {Path.GetFileNameWithoutExtension(targetNode.Id)} (kaynak: {IncrementalRunBinder.PathTerm(OsysRoot, targetFile)})"));
            sb.AppendLine(Inv($"- Hedef WillBuild: {dirtyById[targetNode.Id]}"));
            sb.AppendLine(Inv($"- Doğrudan (cycle-dışı) dependent: {directDependents.Count} · flip=true olmayan (İHLAL): {cascadeMisses.Count}"));
            sb.AppendLine(Inv($"- Transitive (cycle-dışı) dependent: {transNonCycle.Count} · flip=true olan: {transFlipped} ([A3] TAM cascade bekleniyor)"));
            sb.AppendLine(Inv($"- İlgisiz + skip (false) kalan proje sayısı: {unrelatedClean.Count}"));
            sb.AppendLine();
            sb.AppendLine("## K1 (read-only garanti)");
            sb.AppendLine(Inv($"- HEAD önce/sonra: {headBefore} / {headAfter} · aynı: {headBefore == headAfter}"));
            sb.AppendLine(Inv($"- Branch önce/sonra: {branchBefore} / {branchAfter} · aynı: {branchBefore == branchAfter}"));
        });
        output.WriteLine(File.ReadAllText(EvidencePath));

        // ---- KABUL İDDİALARI
        Assert.Equal(RunOutcome.Completed, run2.Completed!.Outcome);
        // incremental all-skipped: Run 1'de başarılı olan her proje Run 2'de skip olmalı — TEK meşru istisna
        // depIssue taşıyan success'lerdir. [DEĞİŞEN KURAL] Gerekçesi değişti: eskiden "persist etmedikleri
        // için bayat imzalıydılar", artık "kayıtları DepIssue notlu ve evaluator notu görünce yine derliyor".
        // Küme aynı; Run 1 tamamen yeşilse iddia yine "Assert.Empty(notSkipped)"e indirgenir.
        Assert.Empty(notSkippedUnexplained);
        // SABİT bir taban ("en az 100 proje up-to-date olmalı") burada YANLIŞ ölçüdür: Run 2'de pre-skip
        // EDİLEBİLECEK proje sayısı repodaki failure sayısına göre değişir (depIssue notlu satırlar yine
        // derlenir). Doğru ölçü koşunun KENDİ ürettiği sayıdan türer: Run 1'de bir BAŞARIYI temsil eden ve
        // DepIssue notu taşımayan her satır Run 2'de "skipped — up to date" pre-skip EDİLMELİDİR.
        // NEDEN ">=" DEĞİL "==": ters yön de üretim kodunca garanti altındadır — WillBuildEvaluator "false"
        // (⇒ pre-skip) diyebilmek için state'te LastResult=Succeeded + EŞLEŞEN imza taşıyan bir satır ARAR
        // (WillBuildEvaluator.cs:16-18) ve run2UpToDate yalnız "skipped — up to date" reason'ıyla
        // filtrelenmiştir (cycle vb. sebeplerle skip edilenler bu kümede DEĞİL, bkz. yukarıdaki Where). Yani
        // satırı olmayan bir proje bu kümeye giremez ⇒ küme zaten satır kümesinin ALT KÜMESİ. Eşitlik bu ikinci
        // yönü de pinler: satırsız bir "up to date" raporu ("hiç başarıyla derlenmemiş proje güncel sayıldı")
        // gerçek bir bug olurdu. İddia ZAYIF DEĞİL — incremental bozulup satır yazmış tek bir proje bile Run
        // 2'de yeniden derlenirse sol taraf düşer ve test kırmızı verir. Koşunun ÖLÇEĞİ ayrıca "Run 1 başarılı
        // > 100" ve persist alt sınırı iddialarıyla ayrıca pinlidir.
        //
        // [DEĞİŞEN KURAL — spec 2026-09-18 §1-14] Eski iddia: "DepIssue notu taşımayan HER persist edilmiş
        // satır Run 2'de pre-skip edilir" — yani `cleanRows = stateAfterRun1.Values.Count(s => !s.DepIssue)`.
        // Artık yanlış: kanıtlı bir derleyici hatası da (yukarıdaki [A2] notuna bak) artık kayıt yoksa bile
        // taze bir satır AÇAR (`FailedSignature`/`FailedAt` ile, `BuiltSignature: null`) ve böyle bir satır
        // DepIssue TAŞIMAZ ama BAŞARILI da DEĞİLDİR. WillBuildEvaluator onu `BuiltSignature: null` olduğu için
        // `NeverBuilt` sayar (WillBuild=true) — pre-skip ETMEZ, ve bu DOĞRUDUR: kanıtlı-kırmızı bir satırın Run
        // 2'de yeniden derlenmesi beklenen davranıştır, bug değil. Doğru ölçü artık WillBuildEvaluator'ın
        // UpToDate dalıyla AYNI iki şartı okur: `LastResult == Succeeded` (yalnız bir BAŞARI kaydı pre-skip
        // adayıdır — bir başarısızlık kaydı asla `BuiltSignature` taşımaz) VE `!DepIssue` (bayat bağımlılığa
        // link'li değil). Gerekçe: bir kanıtlı-kırmızı satırı "temiz satır" sayıp pre-skip beklemek, testi
        // gerçek üretim kararıyla değil eski bir varsayımla karşılaştırırdı — ölçülen sapma tam Run 1'in kanıtlı
        // başarısızlık sayısıyla örtüşüyordu. (`cleanRows` yukarıda, persist iddiasının hemen ardından
        // hesaplanır — evidence metni de AYNI değişkeni okur.)
        Assert.True(run2UpToDate.Count == cleanRows,
            Inv($"'up to date' pre-skip sayısı ({run2UpToDate.Count}) ≠ Run 1'de NOTSUZ BAŞARI persist edilen satır sayısı ({cleanRows} / toplam {stateAfterRun1.Count}) — incremental çalışmıyor: notsuz başarı satırı yazan HER proje Run 2'de skip edilmeliydi."));
        // Bu bir ÜST SINIR (⊆) iddiasıdır: "Run 2 yalnız meşru kümeden derleyebilir". İfade EDEMEDİĞİ şey,
        // kümenin TAMAMININ gerçekten derlendiği (eşitlik) — bir carrier, DAHA ÖNCEKİ bir koşudan kalan
        // Succeeded kaydı sayesinde meşru olarak skip de EDİLEBİLİR (bu testte Run 1 sıfır state ile başladığı
        // için pratikte eşitlik beklenir, ama iddia bilinçli olarak üst sınırda tutulmuştur — aksi hâlde
        // gelecekte state taşıyan bir varyant yanlış kırmızı verirdi).
        Assert.Empty(run2Unexplained);

        Assert.NotEmpty(directDependents);                               // hedefin gerçekten dependent'ı var (cascade anlamlı)
        Assert.Empty(cascadeMisses);                                     // minimal rebuild: hedef + DOĞRUDAN dependent'lar kesin true
        Assert.True(transFlipped == transNonCycle.Count,                 // [A3] transitive cascade TAM (cycle'lar dahil)
            Inv($"transitive cascade eksik — cycle üzerinden yayılım kopmuş olabilir: {transFlipped}/{transNonCycle.Count}"));
        Assert.NotEmpty(unrelatedClean);                                 // ilgisiz projeler skip kaldı (over-build yok)

        Assert.Equal(headBefore, headAfter);                             // K1
        Assert.Equal(branchBefore, branchAfter);
    }

    // ---------------------------------------------------------------- yardımcılar

    /// <summary>Gerçek Supervisor'ı verilen <paramref name="logsDir"/> ile başlatır, bir <c>Build</c> koşusunu
    /// olay-güdümlü (sleep YOK) sürer ve sonucu toplar. Düzgün shutdown ile kapatır.</summary>
    private async Task<RunOutcomeData> RunBuildAsync(string logsDir, string runId, CancellationToken ct)
    {
        var succeeded = new List<string>();
        var failed = new List<(string, string)>();
        var skipped = new List<(string, string)>();
        var depIssueCarriers = new List<string>(); // [A2] depIssue TAŞIYAN success'ler
        RunStartedEvent? started = null;
        RunCompletedEvent? completed = null;

        using var proc = Process.Start(TestPaths.Psi(logsDir))!;
        var stderrDrain = proc.StandardError.ReadToEndAsync();
        try
        {
            var w = new NdjsonWriter(proc.StandardInput.BaseStream);
            var r = new NdjsonReader(proc.StandardOutput.BaseStream);
            Assert.IsType<EngineReadyEvent>(await r.ReadAsync<IpcEvent>().WaitAsync(ct));
            // [cycles] Build bir SCC'ye HİÇ dokunmaz — üyeler "in dependency cycle" ile atlanır. Bu koşu
            // ürünün sevk ettiği Build'in ta kendisidir; turlar kendi modundadır (RunMode.Cycles).
            await w.WriteAsync(
                new StartRunCommand(runId, RunMode.Build, OsysRoot, "Debug", Parallelism), ct);

            while (true)
            {
                var e = await r.ReadAsync<IpcEvent>().WaitAsync(ct)
                        ?? throw new InvalidOperationException("Supervisor stdout runCompleted'tan ÖNCE kapandı.");
                switch (e)
                {
                    case ErrorEvent { Code: "msbuildNotFound" } err: Skip.If(true, err.Message); break;
                    case RunStartedEvent s: started = s; break;
                    case ProjectSucceededEvent p:
                        succeeded.Add(p.ProjectId);
                        if (p.DepIssues is { Count: > 0 }) depIssueCarriers.Add(p.ProjectId); // [A2]
                        break;
                    case ProjectFailedEvent p: failed.Add((p.ProjectId, p.Reason)); break;
                    case ProjectSkippedEvent p: skipped.Add((p.ProjectId, p.Reason)); break;
                    case RunCompletedEvent c: completed = c; break;
                }
                if (completed is not null) break;
            }
            await w.WriteAsync(new ShutdownCommand(), ct);
            await proc.WaitForExitAsync(ct);
        }
        finally
        {
            if (!proc.HasExited) { try { proc.Kill(entireProcessTree: true); } catch { /* temizlik */ } }
        }
        return new RunOutcomeData(succeeded, failed, skipped, depIssueCarriers, started, completed);
    }

    private static (BuildPlan Plan, IReadOnlyDictionary<string, EvaluatedProject> EvaluatedById) BuildPlanAndEvaluated()
    {
        string cachePath = Path.Combine(Directory.CreateTempSubdirectory("bo-it3-plan-").FullName, "evaluation-cache.json");
        var scanner = new WorkspaceScanner();
        var evaluator = new CsprojEvaluator();
        var cache = new EvaluationCache(cachePath);
        var scan = scanner.Scan(OsysRoot);
        var plan = new BuildPlanBuilder(scanner, evaluator, cache).Build(scan, "Debug");
        // GetOrEvaluate canlı build ↔ scan yarışında kaybolan bir dosya için null dönebilir [Task 0/It-4a] —
        // o yollar burada sessizce elenir (bkz. EvaluationCache.GetOrEvaluate XML doc).
        var evaluatedById = scan.CsprojPaths
            .Select(p => (Id: Path.GetFullPath(p), Project: cache.GetOrEvaluate(p, evaluator.Evaluate)))
            .Where(x => x.Project is not null)
            .ToDictionary(x => x.Id, x => x.Project!, StringComparer.OrdinalIgnoreCase);
        return (plan, evaluatedById);
    }

    /// <summary>projectId → DOĞRUDAN dependent'ları (bu projeyi Dependencies'inde bulunduran projeler).</summary>
    private static IReadOnlyDictionary<string, List<string>> ReverseDependents(BuildPlan plan)
    {
        var map = plan.Nodes.ToDictionary(n => n.Id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var n in plan.Nodes)
            foreach (var dep in n.Dependencies)
                if (map.TryGetValue(dep, out var list)) list.Add(n.Id);
        return map;
    }

    private static HashSet<string> TransitiveDependents(string id, IReadOnlyDictionary<string, List<string>> dependentsOf)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(id);
        while (stack.Count > 0)
        {
            string cur = stack.Pop();
            if (!dependentsOf.TryGetValue(cur, out var deps)) continue;
            foreach (var d in deps)
                if (result.Add(d)) stack.Push(d);
        }
        return result;
    }

    private static void WriteEvidence(Action<StringBuilder> build)
    {
        var sb = new StringBuilder();
        build(sb);
        File.WriteAllText(EvidencePath, sb.ToString());
    }
}
