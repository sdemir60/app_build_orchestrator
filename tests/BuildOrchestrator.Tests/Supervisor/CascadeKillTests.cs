using System.Diagnostics;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;
using Xunit;

namespace BuildOrchestrator.Tests.Supervisor;

[Trait("Category", "ProcessControl")]
public class CascadeKillTests
{
    [Fact]
    public async Task App_death_cascades_through_supervisor_and_inner_children_within_2s_zero_orphans() // §3 kabul
    {
        var livePids = new HashSet<int>();
        List<Process> handles;
        var outer = JobObject.CreateKillOnClose(); // using DEĞİL — kill anını biz seçiyoruz
        try
        {
            using var iocp = outer.AttachCompletionPort();
            // [A13/B4] Bu testin sentetik ağacını debugSpawnChildren doğuruyor; o kanca artık VARSAYILAN
            // OLARAK KAPALI, bu yüzden Supervisor bayrakla başlatılır (bayrağın adı TestPaths'te tek yerde).
            var supervisor = JobProcessLauncher.Launch(outer,
                TestPaths.DebugHooksCommandLine(), new LaunchOptions(RedirectStdio: true));
            livePids.Add(supervisor.Pid);
            var writer = new NdjsonWriter(supervisor.StandardInput!);
            var reader = new NdjsonReader(supervisor.StandardOutput!);
            // [B1/F2 · fix-1] Taze bir .NET Supervisor process'inin BOOT'unu (CLR + JIT) bekleyen sabit 5 sn,
            // yük altında ölçülmüş kırılma noktasıydı — bkz. task-B1-report.md İŞ 4. Gerekçe ve tek sahibi:
            // TestPaths.WideStartupTimeout. Üretim yolu DEĞİŞMEZ; genişleyen yalnız TEST beklemesi.
            Assert.IsType<EngineReadyEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TestPaths.WideStartupTimeout));

            await writer.WriteAsync(new DebugSpawnChildrenCommand(Count: 2, Breakaway: false));
            Assert.IsType<DebugChildrenSpawnedEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(10)));

            // outer IOCP tüm ağacı görür (nested üyelik mirası): supervisor + 2×cmd + 2×powershell ≥ 5
            while (livePids.Count < 5)
            {
                var n = iocp.WaitNext(TimeSpan.FromSeconds(10)) ?? throw new TimeoutException("IOCP doğumları eksik");
                if (n.MessageId == NativeMethods.JOB_OBJECT_MSG_NEW_PROCESS) livePids.Add(n.Pid);
                if (n.MessageId is NativeMethods.JOB_OBJECT_MSG_EXIT_PROCESS
                                or NativeMethods.JOB_OBJECT_MSG_ABNORMAL_EXIT_PROCESS) livePids.Remove(n.Pid);
            }
            handles = livePids.Select(pid => { try { return Process.GetProcessById(pid); } catch (ArgumentException) { return null; } })
                              .Where(p => p is not null).Cast<Process>().ToList(); // handle'ları kill ÖNCESİ aç
            Assert.True(handles.Count >= 5, $"kill öncesi {handles.Count} açık handle — beklenen ≥5 (vakum-geçiş guard)"); // [it0-devir]
        }
        finally { }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        outer.Dispose(); // App'in en sert ölümü: son job handle kapanışı → KILL_ON_JOB_CLOSE kaskadı
        foreach (var p in handles)
            await p.WaitForExitAsync(new CancellationTokenSource(2000).Token); // aşım → OCE → FAIL
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds <= 2000, $"kaskat {sw.ElapsedMilliseconds}ms (spike: 18–34ms)");
        foreach (var p in handles) // 0 orphan
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(p.Id));
    }

    [Fact]
    public async Task Breakaway_from_inside_job_is_denied_err5() // D1 probe — no-breakaway garantisi
    {
        using var outer = JobObject.CreateKillOnClose();
        // [A13/B4] breakaway probe'u da debugSpawnChildren üzerinden koşar — bkz. yukarıdaki test.
        var supervisor = JobProcessLauncher.Launch(outer,
            TestPaths.DebugHooksCommandLine(), new LaunchOptions(RedirectStdio: true));
        var writer = new NdjsonWriter(supervisor.StandardInput!);
        var reader = new NdjsonReader(supervisor.StandardOutput!);
        // [B1/F2 · fix-1] bkz. yukarıdaki test — aynı kök (Supervisor boot'unu bekleyen sabit 5 sn),
        // aynı çözüm (TestPaths.WideStartupTimeout).
        Assert.IsType<EngineReadyEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TestPaths.WideStartupTimeout));
        await writer.WriteAsync(new DebugSpawnChildrenCommand(Count: 1, Breakaway: true));
        var err = Assert.IsType<ErrorEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("spawnFailed", err.Code);
        Assert.Contains("win32=5", err.Message); // ERROR_ACCESS_DENIED — çocuklar job'dan çıkamaz (spike S4 ile aynı)
    }
}
