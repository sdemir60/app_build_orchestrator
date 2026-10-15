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
            JobBirths.Until(iocp, TimeSpan.FromSeconds(10), "IOCP doğum bildirimi gelmedi", pid =>
            {
                births.Add(pid);
                return births.Count >= 2;
            });
        } // job.Dispose → KILL_ON_JOB_CLOSE kaskadı
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var pid in births)
        {
            try { await System.Diagnostics.Process.GetProcessById(pid).WaitForExitAsync(new CancellationTokenSource(2000).Token); }
            catch (ArgumentException) { /* zaten öldü — kabul */ }
        }
        Assert.True(sw.ElapsedMilliseconds <= 2000, $"kaskat {sw.ElapsedMilliseconds}ms");
    }

    /// <summary>[idle-memory · dalga 3 · B1 fix 1 · I-3 · PİN] Job'a ZATEN girmiş, çalışan bir üye, IOCP SONRADAN bağlandığında da doğum olarak
    /// (<c>JOB_OBJECT_MSG_NEW_PROCESS</c>) bildirilir — Win32'nin ilişkilendirme davranışı. "Port bağlandıktan sonraki ilk doğum yeni süreçtir"
    /// varsayımı bu yüzden yanlıştır; belirli bir süreci bekleyen testler onu kimliğiyle seçer (<see cref="JobBirths.WaitForProcess"/>).
    /// PİN: işletim sisteminin davranışıdır, kodumuzun değil — değişirse bu test söyler (imaj süzgeci ön koşulu yine kesin tutar).</summary>
    [Fact]
    public void A_member_already_running_is_reported_as_born_when_a_port_is_attached_later()
    {
        using var job = JobObject.CreateKillOnClose();
        using var child = JobProcessLauncher.Launch(job, SleepChildCmdLine(), new LaunchOptions());
        using var iocp = job.AttachCompletionPort(); // üye ZATEN job'da ve çalışıyor

        var born = JobBirths.WaitForBirth(iocp, child.Pid, TimeSpan.FromSeconds(10));

        Assert.Contains(child.Pid, born);
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
