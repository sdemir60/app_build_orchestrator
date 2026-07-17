using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.Logs;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Tests.Supervisor;
using Xunit;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.Integration;

/// <summary>
/// [I2-K3 · It-2 KABUL] Gerçek OSYS reposunu (D:\Projects\Delta\OSYS) gerçek Supervisor process'i ile,
/// gerçek <c>MSBuild.exe</c> child'larıyla, Parallelism=6 paralel REBUILD eder ve kabul özelliğini kanıtlar:
/// <b>YEŞİL = orchestrator kaynaklı hata YOK</b> (dispatch/handle/obj/copy-contention/IPC/scheduling).
/// Repo-kaynaklı per-proje hatalar (gerçekten bozuk legacy proje, eksik referans, bu makinede olmayan bir
/// paketin restore'u) KAYDEDİLİR ve tolere edilir — acceptance'ı bloklamaz. Ayrım kritiktir: orchestrator,
/// aksi halde derlenecek bir build'i kendisi KIRMAMALIDIR.
///
/// <para><b>Bu suite normal koşudan HARİÇTİR</b> (<c>[Trait("Category","Acceptance")]</c>). Çalıştırma:
/// <c>dotnet test … --filter "Category=Acceptance"</c>. Normal suite: <c>--filter "Category!=Acceptance"</c>.</para>
///
/// <para>OSYS ya da MSBuild.exe yoksa <see cref="SkippableFact"/> ile ATLANIR (fail değil). Tam koşu dakikalar
/// sürer; genel bir üst sınır (<see cref="OverallBudget"/>) hang'i sonsuz beklemeye değil test hatasına çevirir.
/// [D8] sleep-poll YOK — akış tamamen event üzerinden, sınırlı bekleme ile sürülür.</para>
/// </summary>
[Trait("Category", "Acceptance")]
public sealed class OsysRebuildAcceptanceTests(ITestOutputHelper output)
{
    private const string OsysRoot = @"D:\Projects\Delta\OSYS";
    private const int Parallelism = 6;
    private static readonly TimeSpan OverallBudget = TimeSpan.FromMinutes(30);

    // Kanıt dosyası: test gerçek sayıları buraya yazar; records.md bunlardan doldurulur (uydurma değil, ölçülen).
    private static string EvidencePath =>
        Environment.GetEnvironmentVariable("BO_IT2_EVIDENCE")
        ?? Path.Combine(Path.GetTempPath(), "bo-it2-acceptance-evidence.md");

    // ---------------------------------------------------------------- sınıflandırma kuralı

    private enum FailClass { Orchestrator, Repo, Timeout }

    /// <summary>
    /// Bir <c>projectFailed</c>'i orchestrator-kaynaklı mı repo-kaynaklı mı diye sınıflandırır. Kural somut ve
    /// savunulabilirdir; POZİTİF orchestrator sinyali olmadıkça hata repo-kaynaklı sayılır (orchestrator'ın
    /// "kırmadığını" kanıtlama yükü orchestrator'dadır, ama belirsiz exit-N'i orchestrator'a yıkmak dürüst değildir).
    /// <list type="bullet">
    /// <item><b>Orchestrator:</b> reason <c>invoke error:</c> ile başlar (invoke/log yolunda exception),
    ///   VEYA reason <c>stopped</c> (bu koşuda Stop verilMEDİ → beklenmeyen), VEYA log'da kalıcı copy-contention
    ///   (MSB3027 = retry sayısı aşıldı) VAR ve derleme/restore hatası YOK (paralelliğin tetiklediği çakışma
    ///   retry bütçesini yendi).</item>
    /// <item><b>Timeout:</b> reason <c>timeout</c> (per-proje 10dk sınırı ateşledi) — SÜPHELİ; kaydedilir,
    ///   otomatik yeşili bloklamaz ama belirgin işaretlenir (insan doğrulaması).</item>
    /// <item><b>Repo:</b> derleme hatası (<c>error CS</c>), restore/paket hatası (<c>error NU</c>,
    ///   "Unable to find", "could not be found"), eksik referans/proje (<c>MSB3202</c>/<c>MSB4019</c>/<c>MSB4025</c>/
    ///   <c>MSB4057</c>), ya da başka pozitif sinyali olmayan sıradan exit-N (belirsiz → insan doğrulaması için
    ///   log kuyruğu kanıta yazılır).</item>
    /// </list>
    /// </summary>
    private static (FailClass Cls, string Signal) Classify(string reason, string logText)
    {
        if (reason.StartsWith("invoke error:", StringComparison.Ordinal))
            return (FailClass.Orchestrator, "invoke/log yolunda exception: " + reason);
        if (reason == "stopped")
            return (FailClass.Orchestrator, "beklenmeyen 'stopped' (bu koşuda Stop verilmedi)");
        if (reason == "timeout")
            return (FailClass.Timeout, "per-proje 10dk timeout ateşledi");

        bool compile = logText.Contains("error CS", StringComparison.Ordinal);
        bool restore = logText.Contains("error NU", StringComparison.Ordinal)
                       || logText.Contains("Unable to find", StringComparison.OrdinalIgnoreCase)
                       || logText.Contains("could not be found", StringComparison.OrdinalIgnoreCase)
                       || logText.Contains("bulunamadı", StringComparison.OrdinalIgnoreCase)
                       // Stale-obj poisoning / restore mekanizması dışı: obj'de başka bir framework'ün restore
                       // artefaktları kalmış → Microsoft.NuGet.targets "re-run NuGet restore" ister. Bu, brief'in
                       // "stale-obj poisoning" olarak adlandırdığı REPO-kaynaklı durumdur (standalone build de
                       // aynı stale obj yüzünden aynı hatayı verir — orchestrator obj'ye DOKUNMAZ [§4]).
                       || logText.Contains("re-run NuGet restore", StringComparison.OrdinalIgnoreCase)
                       || logText.Contains("does not reference", StringComparison.OrdinalIgnoreCase);
        bool missingRef = logText.Contains("MSB3202", StringComparison.Ordinal)   // proje referansı bulunamadı
                          || logText.Contains("MSB4019", StringComparison.Ordinal) // import edilen proje yok
                          || logText.Contains("MSB4025", StringComparison.Ordinal) // proje dosyası yüklenemedi
                          || logText.Contains("MSB4057", StringComparison.Ordinal); // hedef yok
        bool persistentContention = logText.Contains("MSB3027", StringComparison.Ordinal); // retry aşıldı

        if (persistentContention && !compile && !restore)
            return (FailClass.Orchestrator, "kalıcı copy-contention (MSB3027) — retry sonrası hâlâ başarısız");
        if (compile) return (FailClass.Repo, "derleme hatası (error CS)");
        if (restore) return (FailClass.Repo, "restore/stale-obj/paket hatası (NU / re-run restore / bulunamadı)");
        if (missingRef) return (FailClass.Repo, "eksik referans/proje (MSB3202/4019/4025/4057)");
        return (FailClass.Repo, "exit-N, tanımlı orchestrator sinyali yok (BELİRSİZ — insan doğrulaması)");
    }

    // ---------------------------------------------------------------- 1) tam 177 paralel rebuild

    [SkippableFact]
    public async Task Osys_full_rebuild_parallel_is_green_with_zero_orchestrator_caused_failures()
    {
        Skip.IfNot(Directory.Exists(OsysRoot), $"OSYS yok ({OsysRoot}) — kabul koşusu atlandı.");
        using var overall = new CancellationTokenSource(OverallBudget);

        // MSBuild.exe kapısı: yoksa SKIP (fail DEĞİL).
        try { _ = await new MsBuildResolver(new ProcessRunner()).ResolveAsync(ct: overall.Token); }
        catch (MsBuildResolveException ex) { Skip.If(true, "MSBuild.exe yok — kabul koşusu atlandı: " + ex.Message); }

        string logsDir = Directory.CreateTempSubdirectory("bo-it2-acc-logs-").FullName;

        var dispatchOrder = new List<string>();                 // ProjectStarted sırası (dispatch kanıtı)
        var inFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int maxConcurrent = 0;
        var succeeded = new List<string>();
        var failed = new List<(string ProjectId, long DurationMs, string Reason)>();
        var skipped = new List<(string ProjectId, string Reason)>();
        RunCompletedEvent? completed = null;
        RunStartedEvent? started = null;

        using var proc = Process.Start(TestPaths.Psi(logsDir))!;
        var stderrDrain = proc.StandardError.ReadToEndAsync(); // stderr'ı boşalt (konsol uyarıları) — pipe dolmasın
        try
        {
            var w = new NdjsonWriter(proc.StandardInput.BaseStream);
            var r = new NdjsonReader(proc.StandardOutput.BaseStream);
            Assert.IsType<EngineReadyEvent>(await r.ReadAsync<IpcEvent>().WaitAsync(overall.Token));

            var sw = Stopwatch.StartNew();
            await w.WriteAsync(new StartRunCommand("acc-full", RunMode.Rebuild, OsysRoot, "Debug", Parallelism), overall.Token);

            while (true)
            {
                var e = await r.ReadAsync<IpcEvent>().WaitAsync(overall.Token)
                        ?? throw new InvalidOperationException(
                            "Supervisor stdout runCompleted'tan ÖNCE kapandı — process çöktü/hang (orchestrator hatası).");
                switch (e)
                {
                    case ErrorEvent { Code: "msbuildNotFound" } err:
                        Skip.If(true, "MSBuild.exe yok — kabul koşusu atlandı: " + err.Message); break;
                    case RunStartedEvent s: started = s; break;
                    case ProjectStartedEvent p:
                        dispatchOrder.Add(p.ProjectId);
                        inFlight.Add(p.ProjectId);
                        maxConcurrent = Math.Max(maxConcurrent, inFlight.Count);
                        break;
                    case ProjectSucceededEvent p:
                        inFlight.Remove(p.ProjectId); succeeded.Add(p.ProjectId); break;
                    case ProjectFailedEvent p:
                        inFlight.Remove(p.ProjectId); failed.Add((p.ProjectId, p.DurationMs, p.Reason)); break;
                    case ProjectSkippedEvent p:
                        skipped.Add((p.ProjectId, p.Reason)); break;
                    case RunCompletedEvent c: completed = c; break;
                }
                if (completed is not null) break;
            }
            sw.Stop();

            // Düzgün kapat: shutdown → stdout EOF → exit 0 (Supervisor çökmedi kanıtı).
            await w.WriteAsync(new ShutdownCommand(), overall.Token);
            await proc.WaitForExitAsync(overall.Token);
        }
        finally
        {
            if (!proc.HasExited) { try { proc.Kill(entireProcessTree: true); } catch { /* temizlik */ } }
        }

        // ---- kanıt toplama: run dizini, decision.log (retry sayısı), proje logları (sınıflandırma + encoding)
        string? runDir = Directory.Exists(logsDir)
            ? Directory.GetDirectories(logsDir, "run-*").OrderBy(d => d, StringComparer.Ordinal).LastOrDefault()
            : null;
        string decisionLog = runDir is not null && File.Exists(Path.Combine(runDir, "decision.log"))
            ? File.ReadAllText(Path.Combine(runDir, "decision.log")) : "";
        int retryLines = decisionLog.Split('\n')
            .Count(l => l.Contains("Copy contention algılandı", StringComparison.Ordinal));
        var retryProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in decisionLog.Split('\n'))
        {
            int i = line.IndexOf("Copy contention algılandı (", StringComparison.Ordinal);
            if (i < 0) continue;
            int open = line.IndexOf('(', i);
            int close = line.IndexOf(')', open + 1);
            if (open >= 0 && close > open) retryProjects.Add(line[(open + 1)..close]);
        }

        // Encoding kontrolü (Task 5 MsBuildOutputEncoding gerçek-dünya testi): PROJE loglarında (MSBuild
        // çıktısı) Türkçe okunabilir mi yoksa mojibake mi? decision.log HARİÇ tutulur — o BİZİM UTF-8 logumuz,
        // MSBuild çıktısı değil (onu örneklemek yanıltıcı olurdu). İki mojibake sinyali aranır:
        //   (a) U+FFFD (replacement char) — ANSI bayt haritalanamadığında,
        //   (b) UTF-8-baytlarının-ANSI-CP-olarak-çözülmesi imzaları (Ã/Ä±/ÅŸ/ÄŸ/Ä°/Ã§/Ã¼/Ã¶) — asıl bu makinede
        //       görülen bozulma: MSBuild/csc UTF-8 yazar, MsBuildOutputEncoding CP1254 varsayar → "dosyası"→"dosyasÄ±".
        string[] mojibakeMarks = ["Ã", "Ä±", "ÅŸ", "ÄŸ", "Ä°", "Ã§", "Ã¼", "Ã¶"];
        bool anyMojibake = false;
        string? sampleMojibakeLine = null;
        string? sampleReadableTurkishLine = null;
        if (runDir is not null)
            foreach (string logFile in Directory.GetFiles(runDir, "*.log"))
            {
                if (Path.GetFileName(logFile).Equals("decision.log", StringComparison.OrdinalIgnoreCase)) continue;
                string txt = File.ReadAllText(logFile);
                bool moji = txt.Contains('�') || mojibakeMarks.Any(m => txt.Contains(m, StringComparison.Ordinal));
                if (moji) anyMojibake = true;
                foreach (var line in txt.Split('\n'))
                {
                    if (sampleMojibakeLine is null && mojibakeMarks.Any(m => line.Contains(m, StringComparison.Ordinal)))
                        sampleMojibakeLine = line.Trim();
                    if (sampleReadableTurkishLine is null
                        && line.IndexOfAny(['İ', 'ş', 'ğ', 'Ş', 'Ğ', 'ç', 'Ç', 'ı', 'ö', 'Ö', 'ü', 'Ü']) >= 0
                        && !mojibakeMarks.Any(m => line.Contains(m, StringComparison.Ordinal)))
                        sampleReadableTurkishLine = line.Trim();
                }
            }

        // Her failed'i sınıflandır (log'unu okuyarak).
        var classified = new List<(string Name, long DurationMs, string Reason, FailClass Cls, string Signal, string LogTail)>();
        foreach (var (projectId, durationMs, reason) in failed)
        {
            string logPath = runDir is not null ? Path.Combine(runDir, ProjectLogNaming.FileNameFor(projectId)) : "";
            string logText = logPath.Length > 0 && File.Exists(logPath) ? File.ReadAllText(logPath) : "";
            var (cls, signal) = Classify(reason, logText);
            string tail = string.Join(" | ", logText.Split('\n')
                .Select(l => l.Trim()).Where(l => l.Length > 0)
                .Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase) || l.Contains("MSB", StringComparison.Ordinal))
                .TakeLast(3));
            classified.Add((Path.GetFileNameWithoutExtension(projectId), durationMs, reason, cls, signal,
                tail.Length > 300 ? tail[..300] : tail));
        }

        int orchestratorCaused = classified.Count(c => c.Cls == FailClass.Orchestrator);
        int repoCaused = classified.Count(c => c.Cls == FailClass.Repo);
        int timeouts = classified.Count(c => c.Cls == FailClass.Timeout);

        WriteEvidence(sb =>
        {
            sb.AppendLine("# It-2 Acceptance — OSYS Full Rebuild (ölçülen, canlı koşu)");
            sb.AppendLine();
            sb.AppendLine(Inv($"- Zaman damgası (UTC): {DateTimeOffset.UtcNow:O}"));
            sb.AppendLine(Inv($"- RootPath: {OsysRoot} · Configuration: Debug · Parallelism: {Parallelism}"));
            sb.AppendLine(Inv($"- Run dizini: {runDir}"));
            sb.AppendLine();
            sb.AppendLine("## Özet");
            sb.AppendLine(Inv($"- Toplam proje (runStarted.TotalProjects): {started?.TotalProjects}"));
            sb.AppendLine(Inv($"- runCompleted.Outcome: {completed?.Outcome}"));
            sb.AppendLine(Inv($"- Succeeded: {completed?.Succeeded} · Failed: {completed?.Failed} · Skipped: {completed?.Skipped} · Queued: {completed?.Queued}"));
            sb.AppendLine(Inv($"- Toplam süre (runCompleted.DurationMs): {completed?.DurationMs} ms"));
            sb.AppendLine(Inv($"- Max eşzamanlı in-flight (event akışından): {maxConcurrent} (tavan {Parallelism})"));
            sb.AppendLine(Inv($"- ProjectStarted sayısı: {dispatchOrder.Count} · Succeeded event: {succeeded.Count} · Failed event: {failed.Count} · Skipped event: {skipped.Count}"));
            sb.AppendLine();
            sb.AppendLine("## Copy-contention retry");
            sb.AppendLine(Inv($"- decision.log'da retry satırı: {retryLines} · retry gören farklı proje: {retryProjects.Count}"));
            if (retryProjects.Count > 0)
                sb.AppendLine("  - " + string.Join(", ", retryProjects.Select(Path.GetFileNameWithoutExtension)));
            sb.AppendLine();
            sb.AppendLine("## Dispatch sırası (ilk 20 ProjectStarted)");
            for (int i = 0; i < Math.Min(20, dispatchOrder.Count); i++)
                sb.AppendLine(Inv($"  {i + 1,2}. {Path.GetFileNameWithoutExtension(dispatchOrder[i])}"));
            sb.AppendLine();
            sb.AppendLine("## Encoding (Task 5 MsBuildOutputEncoding gerçek-dünya kontrolü)");
            sb.AppendLine(Inv($"- Proje loglarında (MSBuild çıktısı) mojibake: {(anyMojibake ? "VAR — BULGU" : "yok")}"));
            sb.AppendLine(Inv($"- Örnek mojibake satırı: {sampleMojibakeLine ?? "(yok)"}"));
            sb.AppendLine(Inv($"- Örnek okunabilir Türkçe satır: {sampleReadableTurkishLine ?? "(yok)"}"));
            sb.AppendLine();
            sb.AppendLine("## Sınıflandırma");
            sb.AppendLine(Inv($"- Orchestrator-kaynaklı: {orchestratorCaused} (YEŞİL için 0 OLMALI) · Repo-kaynaklı: {repoCaused} · Timeout(şüpheli): {timeouts}"));
            sb.AppendLine();
            sb.AppendLine("| Proje | Reason | Sınıf | Sinyal | Log kuyruğu |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var c in classified.OrderBy(c => c.Cls).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine(Inv($"| {c.Name} | {c.Reason} | {c.Cls} | {c.Signal} | {c.LogTail.Replace('|', '/')} |"));
        });

        output.WriteLine($"Kanıt yazıldı: {EvidencePath}");
        output.WriteLine(File.ReadAllText(EvidencePath));

        // ---- KABUL İDDİALARI (güçlü, temiz invariantlar)
        Assert.NotNull(completed);                                  // koşu tamamlandı: hang/çökme YOK
        Assert.NotNull(started);                                    // runStarted event alındı (TotalProjects kanıtı)
        Assert.True(maxConcurrent <= Parallelism,
            Inv($"paralellik tavanı aşıldı: {maxConcurrent} > {Parallelism}"));
        Assert.Equal(0, orchestratorCaused);                        // I2-K3: orchestrator kendisi hiçbir build'i kırmadı

        // "COMPLETED, GREEN" iddiasını yalnız records.md'ye YAZMAKLA yetinme — testin kendisi ZORUNLU kılsın:
        // Stopped bir outcome ya da geride kalan Queued>0 ile de bu satıra kadar gelinebilirdi (orchestratorCaused
        // hâlâ 0 olurdu), bu yüzden şekil ayrı assert edilir.
        Assert.Equal(RunOutcome.Completed, completed!.Outcome);     // Stopped DEĞİL — koşu gerçekten bitti
        Assert.Equal(0, completed.Queued);                          // dispatch edilmeden kalan proje YOK

        // Sabit "177" yerine SAYAÇ İNVARİANTI tercih edildi: OSYS reposu zamanla proje kazanıp/kaybedebilir
        // (repo drift), 177'yi buraya gömmek testi kırılgan yapardı — repoda meşru bir proje eklenip/silinince
        // test anlamsızca kırılırdı. Bunun yerine üç kovanın (succeeded/failed/skipped) toplamının
        // runStarted.TotalProjects'e eşit olması istenir (hiçbir proje "kaybolmadı"/çift sayılmadı); alt sınır
        // 150 ise boş/yanlış-scan bir koşunun (ör. yanlış RootPath) sessizce "yeşil" geçmesini engelleyen akıl
        // sağlığı tabanıdır.
        Assert.True(started!.TotalProjects > 150,
            Inv($"toplam proje sayısı beklenenden düşük — yanlış-scan/boş koşu şüphesi: {started.TotalProjects}"));
        Assert.Equal(started.TotalProjects, succeeded.Count + failed.Count + skipped.Count);

        // Log kanıtı olmadan "0 orchestrator-kaynaklı" vermek dürüst bir YEŞİL değildir: Classify() MSB3027/
        // invoke-error gibi sinyalleri PROJE LOGLARINDAN okur; runDir çözülemezse ya da loglar okunamazsa her
        // failed'in logText'i boş kalır ve hepsi sessizce "repo-kaynaklı" varsayılır — kanıtsız bir GEÇİŞ.
        // failedCount>0 iken runDir/log kanıtı YOKSA test FAIL etmeli (vacuous-pass'i engelle).
        if (failed.Count > 0)
            Assert.True(runDir is not null, "başarısız proje VAR ama run dizini/log kanıtı çözülemedi — " +
                "0 orchestrator-kaynaklı iddiası log kanıtsız (vacuous pass); GEÇERSİZ.");
    }

    // ---------------------------------------------------------------- 2) dispatch determinizmi (iki koşu)

    [SkippableFact]
    public async Task Dispatch_order_is_deterministic_across_two_runs_for_the_first_projects()
    {
        Skip.IfNot(Directory.Exists(OsysRoot), $"OSYS yok ({OsysRoot}) — determinizm koşusu atlandı.");
        using var overall = new CancellationTokenSource(OverallBudget);
        try { _ = await new MsBuildResolver(new ProcessRunner()).ResolveAsync(ct: overall.Token); }
        catch (MsBuildResolveException ex) { Skip.If(true, "MSBuild.exe yok — determinizm koşusu atlandı: " + ex.Message); }

        const int N = 20;
        var run1 = await CaptureDispatchPrefixAsync(N, overall.Token);
        var run2 = await CaptureDispatchPrefixAsync(N, overall.Token);

        int cmp = Math.Min(run1.Count, run2.Count);
        var a = run1.Take(cmp).Select(p => Path.GetFileNameWithoutExtension(p)!).ToList();
        var b = run2.Take(cmp).Select(p => Path.GetFileNameWithoutExtension(p)!).ToList();
        bool orderedIdentical = a.SequenceEqual(b, StringComparer.OrdinalIgnoreCase);
        bool setIdenticalAtN = new HashSet<string>(a, StringComparer.OrdinalIgnoreCase).SetEquals(b);

        // GARANTİLİ invariant SADECE ilk `parallelism` dispatch'tir: bu N tanesi HERHANGİ bir Complete'ten
        // ÖNCE olur (gerçek MSBuild build'i process spawn + init ile en az yüz(ler)ce ms sürer; worker'ın
        // TryDispatch+TryWrite'ı mikrosaniyeler) → ready-set'in build-order ilk N elemanı, KÜME olarak
        // deterministiktir. Bunun ÖTESİ Complete ZAMANLAMASINA bağlıdır (gerçek paralel MSBuild'de zamanlama
        // non-deterministiktir): hem emisyon SIRASI değişir hem de rastgele bir N=20 kesme NOKTASINDA hangi
        // projenin 20. olduğu (leaf mi level-1 mi) kayar. Bu yüzden N=20 sıralı/küme birebirliği yalnız
        // KAYDEDİLİR; DOĞRULANAN, pre-completion garantili ilk-`parallelism` KÜMESİDİR. [ready-set K2 · D8]
        int guaranteed = Math.Min(Parallelism, cmp);
        var setHeadA = new HashSet<string>(a.Take(guaranteed), StringComparer.OrdinalIgnoreCase);
        bool headSetIdentical = setHeadA.SetEquals(b.Take(guaranteed));

        AppendEvidence(sb =>
        {
            sb.AppendLine();
            sb.AppendLine("## Dispatch determinizmi (iki koşu, graceful-stop ile sınırlı)");
            sb.AppendLine(Inv($"- Karşılaştırılan prefix uzunluğu: {cmp}"));
            sb.AppendLine(Inv($"- Garantili (pre-completion) ilk-{guaranteed} KÜME aynı: {headSetIdentical}  ← DOĞRULANAN invariant"));
            sb.AppendLine(Inv($"- İlk-{cmp} sıralı birebir aynı (kayıt): {orderedIdentical}"));
            sb.AppendLine(Inv($"- İlk-{cmp} küme aynı (kayıt): {setIdenticalAtN} (N=20 kesme sınırında zamanlamaya bağlı 1-2 eleman kayabilir — beklenen)"));
            sb.AppendLine("| # | Koşu 1 | Koşu 2 |");
            sb.AppendLine("|---|---|---|");
            for (int i = 0; i < cmp; i++)
                sb.AppendLine(Inv($"| {i + 1} | {a[i]} | {b[i]} |"));
        });
        output.WriteLine(Inv($"Determinizm: garantili-ilk-{guaranteed}-küme={headSetIdentical} · ilk-{cmp} sıralı-aynı={orderedIdentical} küme-aynı={setIdenticalAtN}"));

        Assert.True(cmp > 0, "hiçbir proje dispatch edilmedi");
        Assert.True(headSetIdentical,
            Inv($"pre-completion ilk-{guaranteed} dispatch KÜMESİ iki koşuda farklı — ready-set determinizmi ihlali. K1=[{string.Join(",", a.Take(guaranteed))}] K2=[{string.Join(",", b.Take(guaranteed))}]"));
    }

    /// <summary>
    /// Supervisor'ı başlatır, rebuild başlatır, ilk <paramref name="n"/> ProjectStarted'ı toplar, graceful Stop
    /// gönderir ve runCompleted'a kadar boşaltır (sınırlı). Dispatch sırası prefix'ini döndürür.
    /// </summary>
    private async Task<List<string>> CaptureDispatchPrefixAsync(int n, CancellationToken ct)
    {
        string logsDir = Directory.CreateTempSubdirectory("bo-it2-det-logs-").FullName;
        var order = new List<string>();
        using var proc = Process.Start(TestPaths.Psi(logsDir))!;
        var stderrDrain = proc.StandardError.ReadToEndAsync();
        try
        {
            var w = new NdjsonWriter(proc.StandardInput.BaseStream);
            var r = new NdjsonReader(proc.StandardOutput.BaseStream);
            Assert.IsType<EngineReadyEvent>(await r.ReadAsync<IpcEvent>().WaitAsync(ct));
            await w.WriteAsync(new StartRunCommand("acc-det", RunMode.Rebuild, OsysRoot, "Debug", Parallelism), ct);

            bool stopSent = false;
            while (true)
            {
                var e = await r.ReadAsync<IpcEvent>().WaitAsync(ct)
                        ?? throw new InvalidOperationException("Supervisor stdout beklenmedik şekilde kapandı.");
                if (e is ErrorEvent { Code: "msbuildNotFound" } err) Skip.If(true, err.Message);
                if (e is ProjectStartedEvent p) order.Add(p.ProjectId);
                if (!stopSent && order.Count >= n)
                {
                    await w.WriteAsync(new StopRunCommand("acc-det", StopKind.Graceful), ct); // yeni dispatch dursun
                    stopSent = true;
                }
                if (e is RunCompletedEvent) break;
            }
            await w.WriteAsync(new ShutdownCommand(), ct);
            await proc.WaitForExitAsync(ct);
        }
        finally
        {
            if (!proc.HasExited) { try { proc.Kill(entireProcessTree: true); } catch { /* temizlik */ } }
        }
        return order;
    }

    // ---------------------------------------------------------------- yardımcılar

    private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    private static void WriteEvidence(Action<StringBuilder> build)
    {
        var sb = new StringBuilder();
        build(sb);
        File.WriteAllText(EvidencePath, sb.ToString());
    }

    private static void AppendEvidence(Action<StringBuilder> build)
    {
        var sb = new StringBuilder();
        build(sb);
        File.AppendAllText(EvidencePath, sb.ToString());
    }
}
