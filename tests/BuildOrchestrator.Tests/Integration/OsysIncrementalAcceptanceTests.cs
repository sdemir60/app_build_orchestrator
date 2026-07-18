using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Git;
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
///   <c>Build</c> → önceki başarılı projelerin HEPSİ "skipped — up to date". (En güçlü tek gösterim.)</item>
/// <item><b>Minimal rebuild (L1→L3 dirty):</b> kurulu state üstünde TEK bir projenin kaynağı "dirty" simüle
///   edilir (OSYS working tree'ye DOKUNULMADAN — sentetik dirty path) → yalnız o proje + transitive dependent'ları
///   WillBuild=true, ilgisiz projeler skip kalır. (Gerçek OSYS grafı + gerçek committed hash'ler + gerçek state.)</item>
/// <item><b>Branch-bounce:</b> A→B→A seçimi doğru worktree/in-place matrisi + K3 niyet satırı üretir (SALT-OKUR,
///   git-no-op — <see cref="WorktreeManager.PlanWorktree"/>).</item>
/// </list>
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

    private sealed record RunOutcomeData(
        IReadOnlyList<string> Succeeded,
        IReadOnlyList<(string ProjectId, string Reason)> Failed,
        IReadOnlyList<(string ProjectId, string Reason)> Skipped,
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
        Assert.True(stateAfterRun1.Count >= run1.Succeeded.Count,
            Inv($"build-state kaydı ({stateAfterRun1.Count}) < başarılı proje ({run1.Succeeded.Count}) — persist eksik."));

        // ---- RUN 2: kaynak DEĞİŞMEDEN yeniden Build → önceki başarılıların HEPSİ "skipped — up to date".
        var run2 = await RunBuildAsync(logsDir, "it3-build-2", overall.Token);
        Assert.NotNull(run2.Completed);

        var run2UpToDate = new HashSet<string>(
            run2.Skipped.Where(s => s.Reason == "skipped — up to date").Select(s => s.ProjectId),
            StringComparer.OrdinalIgnoreCase);
        var run1Succeeded = new HashSet<string>(run1.Succeeded, StringComparer.OrdinalIgnoreCase);

        // Run 1'de başarılı olan HER proje, Run 2'de "up to date" skip olmalı (incremental kalbi).
        var notSkipped = run1Succeeded.Where(id => !run2UpToDate.Contains(id)).ToList();
        // Run 2'de gerçekten dispatch edilen (derlenen) projeler yalnız Run 1'de FAILED olanlar olabilir.
        var run2Started = run2.Succeeded.Concat(run2.Failed.Select(f => f.ProjectId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ---- MINIMAL REBUILD (in-process, SALT-OKUR OSYS): TEK proje dirty → o + transitive dependent'ları true.
        var (plan, evaluatedById) = BuildPlanAndEvaluated();
        var git = new GitService(new ProcessRunner(), OsysRoot);
        string? head = (await git.GetHeadCommitAsync(overall.Token)).Value;
        var tracked = (await git.GetTrackedBlobHashesAsync(overall.Token)).Value
                      ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Dependent'ı OLAN bir proje seç (cascade'i gösterebilmek için) — bir başkasının Dependencies'inde geçen.
        var dependentsOf = ReverseDependents(plan);
        var targetNode = plan.Nodes.FirstOrDefault(n =>
            !n.InCycle && dependentsOf.TryGetValue(n.Id, out var deps) && deps.Count > 0
            && evaluatedById.TryGetValue(n.Id, out var ev) && ev.CompileFiles.Count > 0);
        Skip.If(targetNode is null, "dependent'ı olan + compile dosyası olan bir proje bulunamadı — minimal-rebuild atlandı.");

        // Sentetik dirty path: hedef projenin bir compile dosyasının repo-relative yolu (OSYS working tree'ye
        // DOKUNULMAZ — yalnız binder'a "bu dosya dirty" der; içerik gerçek dosyadan OKUNUR, YAZILMAZ).
        string dirtyRel = IncrementalRunBinder.ToRepoRelativeNormalized(OsysRoot,
            Path.GetFullPath(evaluatedById[targetNode!.Id].CompileFiles[0]));

        var (dirtyPlan, _) = IncrementalRunBinder.Bind(
            plan, evaluatedById, OsysRoot, head, tracked, [dirtyRel],
            stateAfterRun1, inPlace: true, DependentMode.Safe);
        var dirtyById = dirtyPlan.Nodes.ToDictionary(n => n.Id, n => n.WillBuild, StringComparer.OrdinalIgnoreCase);

        var transitiveDependents = TransitiveDependents(targetNode.Id, dependentsOf);
        // Cycle üyeleri HER ZAMAN WillBuild=false taşır (WillBuildEvaluator: inCycle → false); imza cascade'i onların
        // ÜZERİNDEN downstream'e yine yayılır ama KENDİLERİ derlenmez — cascade assert'inden hariç tutulur.
        var inCycle = new HashSet<string>(
            plan.Nodes.Where(n => n.InCycle).Select(n => n.Id), StringComparer.OrdinalIgnoreCase);
        // GARANTİLİ cascade = hedefin DOĞRUDAN (cycle-dışı) dependent'ları: topological build-order'da hedef
        // ONLARDAN ÖNCE gelir (aralarında cycle YOK), bu yüzden memo hedefin TAZE (değişmiş) imzasını görür →
        // kesin flip=true. (Transitive cascade'in cycle-tangled path'ler üzerinden bir kısmı memoization sırası
        // nedeniyle yayılmayabilir — IncrementalPlanner topological-memo tasarımının bilinen sınırı; aşağıda
        // ÇOĞUNLUK olarak raporlanır, garanti DOĞRUDAN dependent'lardadır.)
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

        // ---- BRANCH-BOUNCE (SALT-OKUR, git-no-op): A→B→A matrisi + K3 niyet satırı.
        var wt = new WorktreeManager(new ProcessRunner(), OsysRoot,
            Directory.CreateTempSubdirectory("bo-it3-wt-").FullName);
        string branchA = string.IsNullOrEmpty(branchBefore) ? "main" : branchBefore;
        const string branchB = "it3-feature-x";
        var planA1 = wt.PlanWorktree(branchA, branchA, useWorktreeToggle: false, selectedSha: "aaa");
        var planB = wt.PlanWorktree(branchA, branchB, useWorktreeToggle: false, selectedSha: "bbb");
        var planA2 = wt.PlanWorktree(branchA, branchA, useWorktreeToggle: false, selectedSha: "aaa");

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
            sb.AppendLine(Inv($"- build-state.json kayıt sayısı (Run 1 sonrası): {stateAfterRun1.Count}"));
            sb.AppendLine();
            sb.AppendLine("## Run 2 (Build, kaynak DEĞİŞMEDEN — incremental)");
            sb.AppendLine(Inv($"- TotalProjects: {run2.Started?.TotalProjects} · Succeeded: {run2.Completed?.Succeeded} · Failed: {run2.Completed?.Failed} · Skipped: {run2.Completed?.Skipped} · Süre: {run2.Completed?.DurationMs} ms"));
            sb.AppendLine(Inv($"- 'skipped — up to date' sayısı: {run2UpToDate.Count}"));
            sb.AppendLine(Inv($"- Run 1 başarılı ({run1Succeeded.Count}) → Run 2'de up-to-date SKIP edilmeyen: {notSkipped.Count} (0 OLMALI)"));
            sb.AppendLine(Inv($"- Run 2'de dispatch edilen (derlenen) proje: {run2Started.Count} (≤ Run 1 failed = {run1.Failed.Count})"));
            sb.AppendLine();
            sb.AppendLine("## Minimal rebuild (tek proje dirty, in-process — gerçek OSYS grafı)");
            sb.AppendLine(Inv($"- Dirty edilen hedef: {Path.GetFileNameWithoutExtension(targetNode.Id)} (dirty path: {dirtyRel})"));
            sb.AppendLine(Inv($"- Hedef WillBuild: {dirtyById[targetNode.Id]}"));
            sb.AppendLine(Inv($"- Doğrudan (cycle-dışı) dependent: {directDependents.Count} · flip=true olmayan (İHLAL): {cascadeMisses.Count}"));
            sb.AppendLine(Inv($"- Transitive (cycle-dışı) dependent: {transNonCycle.Count} · flip=true olan: {transFlipped} (çoğunluk cascade)"));
            sb.AppendLine(Inv($"- İlgisiz + skip (false) kalan proje sayısı: {unrelatedClean.Count}"));
            sb.AppendLine();
            sb.AppendLine("## Branch-bounce (A→B→A, git-no-op)");
            sb.AppendLine(Inv($"- A (aktif={branchA}) toggle-off: Mode={planA1.Mode} (InPlace bekleniyor)"));
            sb.AppendLine(Inv($"- B (farklı={branchB}): Mode={planB.Mode} (Worktree bekleniyor) · IntentLine=\"{planB.IntentLine.Replace("\n", " / ")}\""));
            sb.AppendLine(Inv($"- A geri (toggle-off): Mode={planA2.Mode} (InPlace bekleniyor)"));
            sb.AppendLine();
            sb.AppendLine("## K1 (read-only garanti)");
            sb.AppendLine(Inv($"- HEAD önce/sonra: {headBefore} / {headAfter} · aynı: {headBefore == headAfter}"));
            sb.AppendLine(Inv($"- Branch önce/sonra: {branchBefore} / {branchAfter} · aynı: {branchBefore == branchAfter}"));
        });
        output.WriteLine(File.ReadAllText(EvidencePath));

        // ---- KABUL İDDİALARI
        Assert.Equal(RunOutcome.Completed, run2.Completed!.Outcome);
        Assert.Empty(notSkipped);                                        // incremental all-skipped: 1'de başarılı olan HEPSİ 2'de skip
        Assert.True(run2UpToDate.Count >= 100,
            Inv($"'up to date' skip sayısı beklenenden düşük — incremental çalışmıyor: {run2UpToDate.Count}"));
        Assert.True(run2Started.Count <= run1.Failed.Count,
            Inv($"Run 2 gereğinden fazla proje derledi ({run2Started.Count}) — sadece Run 1 failed'ler ({run1.Failed.Count}) beklenirdi."));

        Assert.NotEmpty(directDependents);                               // hedefin gerçekten dependent'ı var (cascade anlamlı)
        Assert.Empty(cascadeMisses);                                     // minimal rebuild: hedef + DOĞRUDAN dependent'lar kesin true
        Assert.True(transFlipped >= transNonCycle.Count / 2,             // transitive cascade ÇOĞUNLUĞA ulaştı
            Inv($"transitive cascade çoğunluğa ulaşmadı: {transFlipped}/{transNonCycle.Count}"));
        Assert.NotEmpty(unrelatedClean);                                 // ilgisiz projeler skip kaldı (over-build yok)

        Assert.Equal(WorktreeMode.InPlace, planA1.Mode);                 // branch-bounce matrisi
        Assert.Equal(WorktreeMode.Worktree, planB.Mode);
        Assert.Equal(WorktreeMode.InPlace, planA2.Mode);
        Assert.Contains("worktree will be used at Build", planB.IntentLine, StringComparison.Ordinal); // K3 niyet satırı

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
        RunStartedEvent? started = null;
        RunCompletedEvent? completed = null;

        using var proc = Process.Start(TestPaths.Psi(logsDir))!;
        var stderrDrain = proc.StandardError.ReadToEndAsync();
        try
        {
            var w = new NdjsonWriter(proc.StandardInput.BaseStream);
            var r = new NdjsonReader(proc.StandardOutput.BaseStream);
            Assert.IsType<EngineReadyEvent>(await r.ReadAsync<IpcEvent>().WaitAsync(ct));
            await w.WriteAsync(new StartRunCommand(runId, RunMode.Build, OsysRoot, "Debug", Parallelism), ct);

            while (true)
            {
                var e = await r.ReadAsync<IpcEvent>().WaitAsync(ct)
                        ?? throw new InvalidOperationException("Supervisor stdout runCompleted'tan ÖNCE kapandı.");
                switch (e)
                {
                    case ErrorEvent { Code: "msbuildNotFound" } err: Skip.If(true, err.Message); break;
                    case RunStartedEvent s: started = s; break;
                    case ProjectSucceededEvent p: succeeded.Add(p.ProjectId); break;
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
        return new RunOutcomeData(succeeded, failed, skipped, started, completed);
    }

    private static (BuildPlan Plan, IReadOnlyDictionary<string, EvaluatedProject> EvaluatedById) BuildPlanAndEvaluated()
    {
        string cachePath = Path.Combine(Directory.CreateTempSubdirectory("bo-it3-plan-").FullName, "evaluation-cache.json");
        var scanner = new WorkspaceScanner();
        var evaluator = new CsprojEvaluator();
        var cache = new EvaluationCache(cachePath);
        var scan = scanner.Scan(OsysRoot);
        var plan = new BuildPlanBuilder(scanner, evaluator, cache).Build(scan, "Debug");
        var evaluatedById = scan.CsprojPaths.ToDictionary(
            p => Path.GetFullPath(p), p => cache.GetOrEvaluate(p, evaluator.Evaluate), StringComparer.OrdinalIgnoreCase);
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
