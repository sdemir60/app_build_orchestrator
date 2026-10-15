using BuildOrchestrator.Core.ProcessControl;

namespace BuildOrchestrator.Tests.ProcessControl;

/// <summary>
/// Bir job'un IOCP'sinden DOĞUM bildirimlerini bekleyen TEK yardımcı. Eskiden her test aynı döngüyü satır içi kopyalıyordu
/// (<c>WaitNext(...) ?? throw new TimeoutException(...)</c> + <c>JOB_OBJECT_MSG_NEW_PROCESS</c> süzgeci); döngü artık burada,
/// testlerde yalnız "ne zaman yeter" kararı kalır.
/// <para><b>Davranış eski kopyalarla birebir:</b> bekleyiş IOCP'ye blokludur (sleep/poll yok — D8); zaman aşımı HER bildirim için
/// ayrıdır — port o süre boyunca hiçbir şey göndermezse <see cref="TimeoutException"/> (<c>what</c> metniyle); doğum dışı bildirimler
/// atlanır, yalnız çıkışları sayan bir test onları <c>onExit</c> ile alır. Port bağlanmadan ÖNCE doğan bir üye görülmez — port
/// başlatmadan önce bağlanır (her çağıranın notu).</para>
/// </summary>
internal static class JobBirths
{
    /// <summary>Doğan her PID'i <paramref name="done"/>'a verir; o true dönene dek bekler. <paramref name="onExit"/> verilirse
    /// çıkış bildirimleri (normal ve anormal) ona gider; verilmezse atlanır.</summary>
    public static void Until(JobCompletionPort port, TimeSpan timeout, string what, Func<int, bool> done, Action<int>? onExit = null)
    {
        while (true)
        {
            var n = port.WaitNext(timeout) ?? throw new TimeoutException(what);
            if (n.MessageId == NativeMethods.JOB_OBJECT_MSG_NEW_PROCESS)
            {
                if (done(n.Pid)) return;
            }
            else if (onExit is not null
                     && n.MessageId is NativeMethods.JOB_OBJECT_MSG_EXIT_PROCESS or NativeMethods.JOB_OBJECT_MSG_ABNORMAL_EXIT_PROCESS)
            {
                onExit(n.Pid);
            }
        }
    }

    /// <summary><paramref name="pid"/> job'a doğana dek bekler; o ana kadar doğan her PID'i — kendisi dahil — doğum sırasıyla döner.</summary>
    public static List<int> WaitForBirth(JobCompletionPort port, int pid, TimeSpan timeout)
    {
        var born = new List<int>();
        Until(port, timeout, $"process {pid} was never born into the job", p =>
        {
            born.Add(p);
            return p == pid;
        });
        return born;
    }
}
