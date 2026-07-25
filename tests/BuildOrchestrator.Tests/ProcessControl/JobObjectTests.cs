using System.IO;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;
using Xunit;

namespace BuildOrchestrator.Tests.ProcessControl;

[Trait("Category", "ProcessControl")]
public class JobObjectTests
{
    static string SleepChildCmdLine() => JobTestChildren.SleepChildCmdLine();

    [Fact]
    public async Task Suspended_assign_resume_then_dispose_kills_tree_within_2s()
    {
        int cmdPid;
        var births = new List<int>();
        using (var job = JobObject.CreateKillOnClose())
        using (var iocp = job.AttachCompletionPort())
        {
            using var child = JobProcessLauncher.Launch(job, SleepChildCmdLine(), new LaunchOptions());
            cmdPid = child.Pid;
            // cmd + powershell torunu: en az 2 doğum bildirimi (nested üyelik otomatik miras)
            while (births.Count < 2)
            {
                var n = iocp.WaitNext(TimeSpan.FromSeconds(10)) ?? throw new TimeoutException("IOCP doğum bildirimi gelmedi");
                if (n.MessageId == NativeMethods.JOB_OBJECT_MSG_NEW_PROCESS) births.Add(n.Pid);
            }
        } // job.Dispose → KILL_ON_JOB_CLOSE kaskadı
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var pid in births)
        {
            try { await System.Diagnostics.Process.GetProcessById(pid).WaitForExitAsync(new CancellationTokenSource(2000).Token); }
            catch (ArgumentException) { /* zaten öldü — kabul */ }
        }
        Assert.True(sw.ElapsedMilliseconds <= 2000, $"kaskat {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Terminate_kills_assigned_child()
    {
        using var job = JobObject.CreateKillOnClose();
        using var child = JobProcessLauncher.Launch(job, SleepChildCmdLine(), new LaunchOptions());
        job.Terminate();
        try
        {
            var p = System.Diagnostics.Process.GetProcessById(child.Pid); // ölmüşse ArgumentException da kabul
            Assert.True(p.WaitForExit(2000), "Terminate 2s içinde öldürmedi");
        }
        catch (ArgumentException)
        {
            // Terminate pid'i test kodu sorgulamadan önce zaten kernelden düşürmüş — daha hızlı kill, kabul.
        }
    }
}
