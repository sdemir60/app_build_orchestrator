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
        // Fix wave 1 / Finding 1: karışık hız — KM1 gecikmesiz (hızlı/kontrol: compile + ortak-bin kopyası kill'den
        // ÖNCE gerçekten biter, succeededBeforeKill'e GARANTİLİ en az bir GERÇEK DLL girer), KM2-4 gecikmeli (yavaş:
        // compile GERÇEKTEN olur ama ortak-bin kopyası kill anında hâlâ MSBuild.exe'nin Exec bloğunda uçuştadır).
        (string Name, int DelaySeconds)[] projects = [("KM1", 0), ("KM2", 3), ("KM3", 3), ("KM4", 3)];
        foreach (var (name, delay) in projects)
            LegacyFixture.CreateClassLibWithSharedBinCopy(Path.Combine(root, name), name, sharedBin, delay);

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
            // [B1/F2 · fix-1] CascadeKillTests ile aynı kök: taze .NET Supervisor process'inin BOOT'unu bekleyen
            // sabit 5 sn, yük altında ölçülmüş kırılma noktası (task-B1-report.md İŞ 4). Tek sahibi:
            // TestPaths.WideStartupTimeout. Aşağıdaki kill→ölüm iddiası (≤2000 ms) BU DEĞİŞİKLİKTEN ETKİLENMEZ —
            // o, testin asıl spec'i olan §3 kabul ölçütüdür ve dokunulmadı.
            Assert.IsType<EngineReadyEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TestPaths.WideStartupTimeout));

            await writer.WriteAsync(new StartRunCommand("r1", RunMode.Rebuild, root, "Debug", Parallelism: 4));

            // Fix wave 1 / Finding 1 + Finding 2: TEK birleşik olay döngüsü — eskiden "phase 1 (IPC)" ve "phase 2
            // (IOCP)" ayrı döngülerdi; phase 2 sırasında gelen ProjectSucceededEvent'ler HİÇ okunmuyordu (Finding 2:
            // kill-öncesi pencere eksikti). Şimdi IPC okuma ve IOCP bekleme AYNI döngüde, Task.WhenAny ile
            // ARALIKSIZ birlikte sürdürülüyor — succeededBeforeKill artık start'tan kill anına kadar TÜM pencereyi
            // kapsıyor. Deterministik tetik [D8]: sleep-poll YOK, ikisi de blok bekleme (IPC: pipe read; IOCP:
            // GetQueuedCompletionStatus) üzerine kurulu. Çıkış koşulu ÜÇ gerçek sinyalin BİRLİKTE sağlanması:
            // ≥2 ProjectStartedEvent (paralel derlemenin uçuşta olduğunun kanıtı), ≥1 ProjectSucceededEvent
            // (Finding 1: succeededBeforeKill GARANTİLİ ≥1 — KM1 gecikmesiz olduğu için bu olay mutlaka erken gelir,
            // sleep YOK, gerçek bir olaya bağlı) ve ≥2 hâlâ canlı gerçek MSBuild.exe (Finding 1: kill anında GERÇEK
            // derleyici/kopya işi uçuşta olduğunun kanıtı — KM2-4 compile'ı bitirmiş, ortak-bin kopyasını henüz
            // YAPMAMIŞ olarak Exec'te bloke haldedir). Cold build yavaş olabilir → cömert timeout'lar (IPC 60s,
            // IOCP 30s); asıl kill→ölüm iddiası aşağıda AYRI ve dar (≤2000ms) tutulur.
            int startedCount = 0;
            Task<IpcEvent?> ipcTask = ReadIpcAsync(reader);
            Task<JobNotification?> iocpTask = Task.Run(() => iocp.WaitNext(TimeSpan.FromSeconds(30)));
            while (startedCount < 2 || succeededBeforeKill.Count < 1 || liveMsBuildPids.Count < 2)
            {
                var completed = await Task.WhenAny(ipcTask, iocpTask);
                if (completed == ipcTask)
                {
                    var e = await ipcTask ?? throw new InvalidOperationException("Supervisor stdout beklenmedik şekilde kapandı.");
                    // VS/MSBuild kurulu değilse run başlamadan msbuildNotFound gelir — mevcut MsBuildInvokerTests deseni.
                    if (e is ErrorEvent { Code: "msbuildNotFound" } err) Skip.If(true, err.Message);
                    if (e is ProjectStartedEvent) startedCount++;
                    if (e is ProjectSucceededEvent ps) succeededBeforeKill.Add(ps.ProjectId); // kill ÖNCESİ bitenler
                    ipcTask = ReadIpcAsync(reader);
                }
                else
                {
                    // outer IOCP tüm ağacı görür (nested üyelik mirası) — supervisor, MsBuildResolver'ın kısa ömürlü
                    // vswhere.exe yardımcı süreci VE gerçek MSBuild.exe çocukları hepsi aynı bildirim akışına düşer
                    // (vswhere.exe job-DIŞI bir ProcessRunner ile başlatılsa da, job üyesi Supervisor'ın soyundan
                    // geldiği için otomatik job üyesi olur ve saniyeler önce zaten doğup ölmüş olabilir). Bu yüzden
                    // eşik HAM canlı sayısı değil, isme göre süzülmüş "hâlâ canlı MSBuild.exe" sayısıdır — aksi halde
                    // vswhere.exe gibi çoktan ölmüş bir üye eşiği yanlışlıkla doldurur.
                    var n = await iocpTask ?? throw new TimeoutException("IOCP doğumları eksik");
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
                    iocpTask = Task.Run(() => iocp.WaitNext(TimeSpan.FromSeconds(30)));
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

        // Fix wave 1 / Finding 1: succeededBeforeKill artık GARANTİLİ ≥1 (bkz. yukarıdaki birleşik döngünün çıkış
        // koşulu — KM1 gecikmesiz olduğu için bu, opsiyonel/tesadüfi bir doğrulama DEĞİL, her koşuda tetiklenen
        // gerçek bir kontrol). Torn DLL yok: kill'den ÖNCE projectSucceeded raporlamış her projenin ortak bin'deki
        // DLL'i geçerli bir PE'dir (yarım/torn olsaydı AssemblyName.GetAssemblyName BadImageFormatException/
        // FileLoadException fırlatırdı). KM2-4 (yavaş) kill anında hâlâ uçuşta olabilir — bu "mid-build"
        // senaryosunun ta kendisidir; onların ortak-bin kopyası hiç yazılmamış (Copy hiç ÇALIŞMAMIŞ) olabilir,
        // o yüzden DLL-geçerlilik kontrolü yalnızca succeededBeforeKill'e girenlere uygulanır.
        Assert.True(succeededBeforeKill.Count >= 1, "succeededBeforeKill boş — birleşik döngünün çıkış koşulu garantisi bozulmuş olmalı");
        foreach (string projectId in succeededBeforeKill)
        {
            string name = Path.GetFileNameWithoutExtension(projectId);
            string dllPath = Path.Combine(sharedBin, name + ".dll");
            Assert.True(File.Exists(dllPath), $"{name}: succeeded raporlandı ama ortak bin'de DLL yok: {dllPath}");
            // Minor fix wave 1: asıl anlamlı kontrol budur — fırlatmaması (throw etmemesi) geçerli bir PE demektir;
            // torn/half-written olsaydı BadImageFormatException/FileLoadException fırlatırdı. dllPath zaten
            // "name + .dll" ile kurulduğu için asmName.Name'i AYNI "name" ile karşılaştırmak totolojikti — kaldırıldı.
            _ = AssemblyName.GetAssemblyName(dllPath);
        }
    }

    // Fix wave 1 / Finding 2: birleşik döngünün IPC bacağı — cömert 60s timeout (cold build payı), Task.WhenAny
    // ile IOCP bekleyişiyle ARALIKSIZ birlikte sürüyor (bkz. yukarıdaki birleşik döngü yorumu).
    private static async Task<IpcEvent?> ReadIpcAsync(NdjsonReader reader) =>
        await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(60));

    // MsBuildResolver'ın (bir kez, run'ın en başında) çalıştırdığı vswhere.exe de job-DIŞI bir helper olsa dahi
    // otomatik job üyesi olur ve genelde saniyeler önce çoktan ölmüştür — isim süzgeci onu "gerçek MSBuild.exe
    // çocuğu" eşiğinden ayıklar (bkz. MsBuildInvokerTests'teki "powershell" isim süzgeci ile aynı desen).
    private static bool IsMsBuildProcess(int pid)
    {
        try { return string.Equals(Process.GetProcessById(pid).ProcessName, "MSBuild", StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; } // zaten çıkmış kısa ömürlü job üyesi
    }
}
