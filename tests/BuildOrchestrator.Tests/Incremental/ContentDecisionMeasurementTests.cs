using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Core.Processes;
using Xunit;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.Incremental;

/// <summary>
/// [Faz 0 · plan "İçerikten Karar"] ÖLÇÜM — test DEĞİL, KAPI. Kararı git blob tablosundan diskteki içeriğe
/// taşımanın gerçek bedelini gerçek bir repoda (varsayılan: OSYS) ölçer ve sayıları dosyaya yazar. Hiçbir şey
/// assert etmez; plan bu sayılara bakarak ya uygulanır ya durur.
///
/// <para><b>Ölçülenler.</b> M0 = bugünkü taban (<c>ls-tree -r HEAD</c> + <c>status --porcelain</c> + kirli
/// dosyaların okunup özetlenmesi). M1 = D2 girdi kümesinin tamamının okunup SHA256'lanması, ilk geçiş.
/// M2 = aynı işlem hemen ardından (OS dosya önbelleği sıcak). M3 = yalnız stat geçişi (boyut + mtime) —
/// D3 özet önbelleğinin sıcak durumunda her Sync/Build'de ödenecek GERÇEK bedel budur.</para>
///
/// <para><b>Kapı:</b> M3 ≤ 500 ms ve M3 ≤ 1,5 × M0; M2 ≤ 2 s; M1 ≤ 6 s.</para>
///
/// <para>Varsayılan süitten hariçtir (<c>Category=Measurement</c>). Repo kökü <c>BO_MEASURE_ROOT</c> ile
/// verilir; yoksa OSYS varsayılanı denenir, o da yoksa ölçüm atlanır.</para>
/// </summary>
[Trait("Category", "Measurement")]
public sealed class ContentDecisionMeasurementTests(ITestOutputHelper output)
{
    private const string DefaultRoot = @"D:\Projects\Delta\OSYS";

    private static readonly string[] DirectoryLevelFiles =
        ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"];

    private static string Root =>
        Environment.GetEnvironmentVariable("BO_MEASURE_ROOT") is { Length: > 0 } r ? r : DefaultRoot;

    private static string ResultPath =>
        Environment.GetEnvironmentVariable("BO_MEASURE_OUT")
        ?? Path.Combine(Path.GetTempPath(), "bo-faz0-measurement.md");

    private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    [SkippableFact]
    public async Task Measure_content_decision_cost_against_the_git_baseline()
    {
        string root = Root;
        Skip.IfNot(Directory.Exists(root), $"Measurement root not found ({root}) — Faz 0 measurement skipped.");

        var report = new StringBuilder();
        void Line(string text) { output.WriteLine(text); report.AppendLine(text); }

        Line($"# Faz 0 measurement — {root}");
        Line("");

        // ---- Girdi kümesi (D2). Toplama süresi de raporlanır: üretimde bu iş zaten tarama sırasında yapılır.
        var sw = Stopwatch.StartNew();
        var projects = EnumerateProjects(root).ToList();
        var evaluator = new CsprojEvaluator();
        var inputsByProject = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string csproj in projects)
        {
            EvaluatedProject? evaluated = null;
            try { evaluated = evaluator.Evaluate(csproj); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException) { }
            inputsByProject[csproj] = CollectInputs(root, csproj, evaluated);
        }
        sw.Stop();
        long collectMs = sw.ElapsedMilliseconds;

        var allFiles = inputsByProject.Values.SelectMany(f => f)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        long totalBytes = 0;
        int missing = 0;
        foreach (string f in allFiles)
        {
            var info = new FileInfo(f);
            if (info.Exists) totalBytes += info.Length; else missing++;
        }

        Line(Inv($"- projects: {projects.Count}"));
        Line(Inv($"- input files (distinct): {allFiles.Count} ({missing} missing on disk)"));
        Line(Inv($"- total bytes: {totalBytes / 1024.0 / 1024.0:F1} MB"));
        Line(Inv($"- collection (enumerate + evaluate csproj): {collectMs} ms"));
        Line("");

        // ---- M0: bugünkü taban.
        long m0 = await MeasureGitBaselineAsync(root, Line);

        // ---- M1 / M2: tam okuma + SHA256.
        long m1 = MeasureHashPass(allFiles, out int hashed);
        long m2 = MeasureHashPass(allFiles, out _);

        // ---- M3: yalnız stat (boyut + mtime) — sıcak önbellek durumu.
        long m3 = MeasureStatPass(allFiles);
        long m3Second = MeasureStatPass(allFiles);

        // Bilgi amaçlı: paralel geçişler (uygulama isterse bu yolu seçebilir).
        long m1Parallel = MeasureHashPassParallel(allFiles);
        long m3Parallel = MeasureStatPassParallel(allFiles);

        Line("");
        Line("| Measurement | ms |");
        Line("|---|---|");
        Line(Inv($"| M0 · git baseline (ls-tree + status + dirty reads) | {m0} |"));
        Line(Inv($"| M1 · full read + SHA256, first pass ({hashed} files) | {m1} |"));
        Line(Inv($"| M2 · full read + SHA256, second pass | {m2} |"));
        Line(Inv($"| M3 · stat only (size + mtime) | {m3} |"));
        Line(Inv($"| M3 · stat only, second pass | {m3Second} |"));
        Line(Inv($"| (info) M1 parallel | {m1Parallel} |"));
        Line(Inv($"| (info) M3 parallel | {m3Parallel} |"));
        Line("");

        bool gateM3 = m3 <= 500 && m3 <= 1.5 * Math.Max(m0, 1);
        bool gateM2 = m2 <= 2000;
        bool gateM1 = m1 <= 6000;
        Line(Inv($"- gate M3 ≤ 500 ms and ≤ 1.5 × M0 ({1.5 * m0:F0} ms): {(gateM3 ? "PASS" : "FAIL")}"));
        Line(Inv($"- gate M2 ≤ 2000 ms: {(gateM2 ? "PASS" : "FAIL")}"));
        Line(Inv($"- gate M1 ≤ 6000 ms: {(gateM1 ? "PASS" : "FAIL")}"));
        Line(Inv($"- OVERALL: {(gateM3 && gateM2 && gateM1 ? "PASS" : "FAIL")}"));

        File.WriteAllText(ResultPath, report.ToString());
        output.WriteLine("");
        output.WriteLine($"written: {ResultPath}");
    }

    /// <summary>
    /// [Faz 0 · ek ölçüm] SOĞUK ilk geçişin gerçek bedeli: OS dosya önbelleği ısınmadan, aynı ağacın
    /// dönüşümlü bölünmüş iki yarısı — biri SIRAYLA, diğeri PARALEL okunup özetlenir. Amaç M1'in
    /// (243 s, sıralı) ne kadarının per-dosya açılış giderinden (on-access tarama) geldiğini ve paralel
    /// okumanın onu ne kadar sakladığını görmek. Ağaç <c>BO_MEASURE_COLD_ROOT</c> ile verilir ve o oturumda
    /// HİÇ okunmamış olmalıdır — ısınmış bir ağaçta bu ölçümün anlamı yoktur.
    /// </summary>
    [SkippableFact]
    public void Measure_cold_first_pass_sequential_versus_parallel()
    {
        string root = Environment.GetEnvironmentVariable("BO_MEASURE_COLD_ROOT") ?? @"D:\Projects\Delta\OSYS-AI";
        Skip.IfNot(Directory.Exists(root), $"Cold measurement root not found ({root}) — skipped.");

        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => BuildSignature.IsBuildAffecting(f) && !IsUnderBuildOutput(root, f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Dönüşümlü bölme: iki yarı da ağaç boyunca aynı dağılıma sahip olsun (bir yarı tek bir alt ağaca düşmesin).
        var sequential = files.Where((_, i) => i % 2 == 0).ToList();
        var parallel = files.Where((_, i) => i % 2 == 1).ToList();

        var swSeq = Stopwatch.StartNew();
        long seqBytes = 0;
        foreach (string f in sequential)
        {
            try { var b = File.ReadAllBytes(f); seqBytes += b.Length; _ = SHA256.HashData(b); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        swSeq.Stop();

        long parBytes = 0;
        var swPar = Stopwatch.StartNew();
        System.Threading.Tasks.Parallel.ForEach(
            parallel,
            new ParallelOptions { MaxDegreeOfParallelism = 16 },
            f =>
            {
                try { var b = File.ReadAllBytes(f); Interlocked.Add(ref parBytes, b.Length); _ = SHA256.HashData(b); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            });
        swPar.Stop();

        output.WriteLine($"# Cold first pass — {root}");
        output.WriteLine(Inv($"- files: {files.Count} (sequential half {sequential.Count}, parallel half {parallel.Count})"));
        output.WriteLine(Inv($"- sequential: {swSeq.ElapsedMilliseconds} ms · {seqBytes / 1024.0 / 1024.0:F1} MB · {swSeq.Elapsed.TotalMilliseconds / Math.Max(sequential.Count, 1):F2} ms/file"));
        output.WriteLine(Inv($"- parallel(16): {swPar.ElapsedMilliseconds} ms · {parBytes / 1024.0 / 1024.0:F1} MB · {swPar.Elapsed.TotalMilliseconds / Math.Max(parallel.Count, 1):F2} ms/file"));
        output.WriteLine(Inv($"- projected full tree, parallel: {swPar.ElapsedMilliseconds * 2} ms"));
    }

    /// <summary>
    /// [Faz 0 sonrası doğrulama] ÜRETİM YOLU: gerçek tarama → gerçek <see cref="IncrementalRunBinder"/> →
    /// gerçek imza haritası, gerçek bir repoda. Faz 0'ın M1/M3'ü girdi kümesini elle kurup ölçüyordu; burada
    /// ölçülen şey kullanıcının Sync'te GERÇEKTEN ödediği bedeldir — klasör taraması, önbellek ve iki bağlama
    /// geçişi dahil.
    ///
    /// <para>İki koşu: önbellek BOŞ (ilk indeksleme) ve önbellek SICAK (steady state). İkincisi her Sync'in
    /// bedelidir.</para>
    /// </summary>
    [SkippableFact]
    public void Measure_the_production_decision_path_cold_and_warm()
    {
        string root = Root;
        Skip.IfNot(Directory.Exists(root), $"Measurement root not found ({root}) — skipped.");

        var scanner = new WorkspaceScanner();
        var evaluator = new CsprojEvaluator();
        string cacheRoot = Directory.CreateTempSubdirectory("bo-measure-cache-").FullName;
        var cache = new EvaluationCache(Path.Combine(cacheRoot, "evaluation-cache.json"));

        var sw = Stopwatch.StartNew();
        var scan = scanner.Scan(root);
        var plan = new BuildPlanBuilder(scanner, evaluator, cache).Build(scan, "Debug", null);
        sw.Stop();
        long planMs = sw.ElapsedMilliseconds;

        var evaluatedById = scan.CsprojPaths
            .Select(p => (Id: Path.GetFullPath(p), Project: cache.GetOrEvaluate(p, evaluator.Evaluate)))
            .Where(x => x.Project is not null)
            .ToDictionary(x => x.Id, x => x.Project!, StringComparer.OrdinalIgnoreCase);
        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase);
        string hashPath = Path.Combine(cacheRoot, SourceHashCache.FileName);

        long Decide(string label, out int files)
        {
            var hashes = new SourceHashCache(hashPath);
            var clock = Stopwatch.StartNew();
            var binder = new IncrementalRunBinder(plan, evaluatedById, root, hashes);
            int collected = binder.PhysicalPaths.Count;
            int read = binder.Prefill();
            binder.Bind(state, buildCycles: false, DependentMode.Safe);
            binder.Bind(state, buildCycles: false, DependentMode.Fast);
            hashes.Flush();
            clock.Stop();

            files = collected;
            output.WriteLine(Inv($"- {label}: {clock.ElapsedMilliseconds} ms · {collected} girdi dosyası · {read} tanesi okundu"));
            return clock.ElapsedMilliseconds;
        }

        output.WriteLine($"# Üretim yolu — {root}");
        output.WriteLine(Inv($"- tarama + graf + değerlendirme: {planMs} ms · {plan.Nodes.Count} proje"));
        long cold = Decide("önbellek BOŞ (ilk indeksleme)", out int inputs);
        long warm = Decide("önbellek SICAK (her Sync'in bedeli)", out _);

        Assert.True(inputs > 0);
        output.WriteLine(Inv($"- soğuk/sıcak oranı: {(warm == 0 ? 0 : cold / (double)warm):F1}×"));
    }

    /// <summary>Bugünkü karar yolu: HEAD blob tablosu + kirli yollar + kirli dosyaların içeriği.</summary>
    private static async Task<long> MeasureGitBaselineAsync(string root, Action<string> line)
    {
        var git = new GitService(new ProcessRunner(), root);

        var sw = Stopwatch.StartNew();
        var blobs = await git.GetTrackedBlobHashesAsync();
        var dirty = await git.GetDirtyPathsAsync();
        int dirtyHashed = 0;
        foreach (string rel in dirty.Value ?? [])
        {
            string abs = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!BuildSignature.IsBuildAffecting(abs) || !File.Exists(abs)) continue;
            _ = SHA256.HashData(File.ReadAllBytes(abs));
            dirtyHashed++;
        }
        sw.Stop();

        line(Inv($"- git baseline: {blobs.Value?.Count ?? 0} tracked blobs, {dirty.Value?.Count ?? 0} dirty paths ({dirtyHashed} hashed)"));
        return sw.ElapsedMilliseconds;
    }

    private static long MeasureHashPass(IReadOnlyList<string> files, out int hashed)
    {
        int count = 0;
        var sw = Stopwatch.StartNew();
        foreach (string f in files)
        {
            try { _ = SHA256.HashData(File.ReadAllBytes(f)); count++; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        sw.Stop();
        hashed = count;
        return sw.ElapsedMilliseconds;
    }

    private static long MeasureHashPassParallel(IReadOnlyList<string> files)
    {
        var sw = Stopwatch.StartNew();
        Parallel.ForEach(files, f =>
        {
            try { _ = SHA256.HashData(File.ReadAllBytes(f)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        });
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    private static long MeasureStatPass(IReadOnlyList<string> files)
    {
        long acc = 0;
        var sw = Stopwatch.StartNew();
        foreach (string f in files)
        {
            var info = new FileInfo(f);
            if (info.Exists) acc += info.Length + info.LastWriteTimeUtc.Ticks;
        }
        sw.Stop();
        return acc == long.MinValue ? -1 : sw.ElapsedMilliseconds;
    }

    private static long MeasureStatPassParallel(IReadOnlyList<string> files)
    {
        var sw = Stopwatch.StartNew();
        Parallel.ForEach(files, f =>
        {
            var info = new FileInfo(f);
            if (info.Exists) _ = info.Length + info.LastWriteTimeUtc.Ticks;
        });
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    private static IEnumerable<string> EnumerateProjects(string root) =>
        Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !IsUnderBuildOutput(root, p) && !WorkspaceScanner.IsTransientBuildArtifact(p));

    /// <summary>[D2] Bir projenin girdi kümesi — csproj + bildirilen öğeler + klasör altındaki
    /// derleme-etkileyen dosyalar + yukarı yürürken bulunan Directory.Build.* dosyaları.</summary>
    private static IReadOnlyList<string> CollectInputs(string root, string csproj, EvaluatedProject? evaluated)
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(csproj) };
        string dir = Path.GetDirectoryName(Path.GetFullPath(csproj))!;

        if (evaluated is not null)
            foreach (string f in evaluated.CompileFiles)
                if (BuildSignature.IsBuildAffecting(f)) set.Add(Path.GetFullPath(f));

        foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            if (BuildSignature.IsBuildAffecting(f) && !IsUnderBuildOutput(dir, f))
                set.Add(Path.GetFullPath(f));

        foreach (string f in DirectoryLevelFilesAbove(root, dir)) set.Add(f);

        return [.. set];
    }

    private static IEnumerable<string> DirectoryLevelFilesAbove(string root, string startDir)
    {
        string full = Path.GetFullPath(root);
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            foreach (string name in DirectoryLevelFiles)
            {
                string candidate = Path.Combine(dir.FullName, name);
                if (File.Exists(candidate)) yield return candidate;
            }
            if (string.Equals(Path.TrimEndingDirectorySeparator(dir.FullName), Path.TrimEndingDirectorySeparator(full), StringComparison.OrdinalIgnoreCase))
                yield break;
            dir = dir.Parent;
        }
    }

    private static bool IsUnderBuildOutput(string root, string file)
    {
        string rel = Path.GetRelativePath(root, file);
        return rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(seg => seg.Equals("obj", StringComparison.OrdinalIgnoreCase)
                     || seg.Equals("bin", StringComparison.OrdinalIgnoreCase)
                     || seg.Equals(".git", StringComparison.OrdinalIgnoreCase));
    }
}
