using System.Diagnostics;
using System.IO;
using System.Reflection;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Tests.MsBuild;
using Xunit;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [T9] I2-K1'in torn-DLL tanımını GERÇEK, paralel bir MSBuild ağacında kanıtlar: v1 flag'leri
/// (<c>-p:UseSharedCompilation=false -nodeReuse:false</c>) → VBCSCompiler yok → her writer inner Job'un
/// (ve onun üzerinden outer Job'un) üyesi → outer Job'un kill'inden SAĞ ÇIKAN yazıcı yoktur. Aynı desen
/// <see cref="CascadeKillTests"/>'te sentetik bir ağaç (cmd/powershell) ile kanıtlanmıştı; burada tetikleyici
/// ve öldürülen ağaç GERÇEKTİR: gerçek Supervisor, gerçek <c>startRun</c>, gerçek <c>MSBuild.exe</c> child'ları.
/// </summary>
[Trait("Category", "MsBuild")]
public class KillMidBuildTests
{
    [SkippableFact]
    public async Task Kill_mid_parallel_real_msbuild_leaves_zero_orphans_and_no_torn_dll() // T9
    {
        // ---- fixture: ≥4 bağımsız (birbirine referans vermeyen) v4.6 classlib, hepsi AYNI ortak bin'e
        // post-build copy yapıyor — paralel yazımın gözlemlenebilir hedefi budur.
        string root = Directory.CreateTempSubdirectory("bo-killmid-ws-").FullName;
        string sharedBin = Path.Combine(root, "SharedBin");
        string logsDir = Directory.CreateTempSubdirectory("bo-killmid-logs-").FullName;
        string[] names = ["KM1", "KM2", "KM3", "KM4"];
        foreach (string name in names)
            LegacyFixture.CreateClassLibWithSharedBinCopy(Path.Combine(root, name), name, sharedBin);

        var livePids = new HashSet<int>();          // outer Job'un o ana kadar gördüğü TÜM canlı üyeler (supervisor dahil)
        var liveMsBuildPids = new HashSet<int>();    // yalnız hâlâ canlı GERÇEK MSBuild.exe çocukları — eşik BURADA ölçülür
        var succeededBeforeKill = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<Process> handles;
        var outer = JobObject.CreateKillOnClose(); // using DEĞİL — kill anını biz seçiyoruz (CascadeKillTests deseni)
        try
        {
            using var iocp = outer.AttachCompletionPort(); // Launch'tan ÖNCE bağlanmalı — kaçırılan bildirim olmasın
            var supervisor = JobProcessLauncher.Launch(outer,
                WindowsCommandLine.Build(TestPaths.SupervisorExe, "--logs", logsDir),
                new LaunchOptions(RedirectStdio: true));
            livePids.Add(supervisor.Pid);
            var writer = new NdjsonWriter(supervisor.StandardInput!);
            var reader = new NdjsonReader(supervisor.StandardOutput!);
            Assert.IsType<EngineReadyEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(5)));

            await writer.WriteAsync(new StartRunCommand("r1", RunMode.Rebuild, root, "Debug", Parallelism: 4));

            // Deterministik tetik [D8]: sleep-poll YOK — ≥2 projectStarted gelene kadar IPC okunur (gerçek
            // paralel derlemenin uçuşta olduğunun kanıtı). Cold build yavaş olabilir → cömert timeout (60s);
            // asıl kill→ölüm iddiası aşağıda AYRI ve dar (≤2000ms) tutulur.
            int startedCount = 0;
            while (startedCount < 2)
            {
                var e = await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(60))
                        ?? throw new InvalidOperationException("Supervisor stdout beklenmedik şekilde kapandı.");
                // VS/MSBuild kurulu değilse run başlamadan msbuildNotFound gelir — mevcut MsBuildInvokerTests deseni.
                if (e is ErrorEvent { Code: "msbuildNotFound" } err) Skip.If(true, err.Message);
                if (e is ProjectStartedEvent) startedCount++;
                if (e is ProjectSucceededEvent ps) succeededBeforeKill.Add(ps.ProjectId); // kill ÖNCESİ bitenler
            }

            // pid koleksiyonu: outer IOCP tüm ağacı görür (nested üyelik mirası) — supervisor, MsBuildResolver'ın
            // kısa ömürlü vswhere.exe yardımcı süreci VE gerçek MSBuild.exe çocukları hepsi aynı bildirim akışına
            // düşer (vswhere.exe job-DIŞI bir ProcessRunner ile başlatılsa da, job üyesi Supervisor'ın soyundan
            // geldiği için otomatik job üyesi olur ve saniyeler önce zaten doğup ölmüş olabilir). Bu yüzden eşik
            // HAM canlı sayısı değil, isme göre süzülmüş "hâlâ canlı MSBuild.exe" sayısıdır — aksi halde vswhere.exe
            // gibi çoktan ölmüş bir üye eşiği yanlışlıkla doldurur. Bildirimler kernel'de zaten kuyruklanmıştır
            // (biz dinlemesek de kaybolmaz); ≥2 GERÇEK ve hâlâ canlı MSBuild.exe gözlenene kadar bloklu beklenir
            // (D8: sleep-poll YOK — IOCP bildirimi üzerinde blok).
            while (liveMsBuildPids.Count < 2)
            {
                var n = iocp.WaitNext(TimeSpan.FromSeconds(30)) ?? throw new TimeoutException("IOCP doğumları eksik");
                if (n.MessageId == NativeMethods.JOB_OBJECT_MSG_NEW_PROCESS)
                {
                    livePids.Add(n.Pid);
                    if (IsMsBuildProcess(n.Pid)) liveMsBuildPids.Add(n.Pid);
                }
                if (n.MessageId is NativeMethods.JOB_OBJECT_MSG_EXIT_PROCESS
                                or NativeMethods.JOB_OBJECT_MSG_ABNORMAL_EXIT_PROCESS)
                {
                    livePids.Remove(n.Pid);
                    liveMsBuildPids.Remove(n.Pid);
                }
            }
            handles = livePids.Select(pid => { try { return Process.GetProcessById(pid); } catch (ArgumentException) { return null; } })
                              .Where(p => p is not null).Cast<Process>().ToList(); // handle'ları kill ÖNCESİ aç
            Assert.True(handles.Count >= 3, $"kill öncesi {handles.Count} açık handle — beklenen ≥3 (supervisor + ≥2 gerçek MSBuild.exe)");
        }
        catch
        {
            outer.Dispose(); // erken çıkış (Skip hariç — Skip.If fırlattığı SkipException de buradan geçer, süpürme her yol için şart)
            throw;
        }

        var sw = Stopwatch.StartNew();
        outer.Dispose(); // App'in en sert ölümü: son job handle kapanışı → KILL_ON_JOB_CLOSE kaskadı
        foreach (var p in handles)
            await p.WaitForExitAsync(new CancellationTokenSource(2000).Token); // aşım → OCE → FAIL
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds <= 2000, $"kaskat {sw.ElapsedMilliseconds}ms");
        foreach (var p in handles) // 0 orphan
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(p.Id));

        // Torn DLL yok: kill'den ÖNCE projectSucceeded raporlamış her projenin ortak bin'deki DLL'i geçerli bir
        // PE'dir (yarım/torn olsaydı AssemblyName.GetAssemblyName BadImageFormatException/FileLoadException fırlatırdı).
        // ≤2 proje bitmiş olabilir (kill erken gelmiş olabilir) — bu, "mid-build" senaryosunun ta kendisidir.
        foreach (string projectId in succeededBeforeKill)
        {
            string name = Path.GetFileNameWithoutExtension(projectId);
            string dllPath = Path.Combine(sharedBin, name + ".dll");
            Assert.True(File.Exists(dllPath), $"{name}: succeeded raporlandı ama ortak bin'de DLL yok: {dllPath}");
            var asmName = AssemblyName.GetAssemblyName(dllPath);
            Assert.Equal(name, asmName.Name);
        }
    }

    // MsBuildResolver'ın (bir kez, run'ın en başında) çalıştırdığı vswhere.exe de job-DIŞI bir helper olsa dahi
    // otomatik job üyesi olur ve genelde saniyeler önce çoktan ölmüştür — isim süzgeci onu "gerçek MSBuild.exe
    // çocuğu" eşiğinden ayıklar (bkz. MsBuildInvokerTests'teki "powershell" isim süzgeci ile aynı desen).
    private static bool IsMsBuildProcess(int pid)
    {
        try { return string.Equals(Process.GetProcessById(pid).ProcessName, "MSBuild", StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; } // zaten çıkmış kısa ömürlü job üyesi
    }
}
