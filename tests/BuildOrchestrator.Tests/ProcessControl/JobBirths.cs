using System.ComponentModel;
using System.Diagnostics;
using BuildOrchestrator.Core.ProcessControl;

namespace BuildOrchestrator.Tests.ProcessControl;

/// <summary>
/// Bir job'un IOCP'sinden DOĞUM bildirimlerini bekleyen TEK yardımcı. Eskiden her test aynı döngüyü satır içi kopyalıyordu
/// (<c>WaitNext(...) ?? throw new TimeoutException(...)</c> + <c>JOB_OBJECT_MSG_NEW_PROCESS</c> süzgeci); döngü artık burada,
/// testlerde yalnız "ne zaman yeter" kararı kalır.
/// <para><b>Davranış eski kopyalarla birebir:</b> bekleyiş IOCP'ye blokludur (sleep/poll yok — D8); zaman aşımı HER bildirim için
/// ayrıdır — port o süre boyunca hiçbir şey göndermezse <see cref="TimeoutException"/> (<c>what</c> metniyle); doğum dışı bildirimler
/// atlanır, yalnız çıkışları sayan bir test onları <c>onExit</c> ile alır.</para>
/// <para><b>[idle-memory · dalga 3 · B1 fix 1 · I-3] Port bağlandığı anda job'da ZATEN bulunan üyeler de doğum olarak bildirilir</b>
/// (Win32: ilişkilendirmede mevcut her üye için <c>JOB_OBJECT_MSG_NEW_PROCESS</c> — <c>JobObjectTests</c> pinler). "Port bağlandıktan
/// sonraki ilk doğum yeni süreçtir" bu yüzden YANLIŞ bir varsayımdır: bir motoru uyuttuktan ya da öldürdükten sonra bağlanan port, job'da
/// kalmış üyeleri — eski motorun conhost'u, çıkmakta olan eski motor — önce bildirebilir ve bildirilen PID açılmadan gitmiş olabilir. Belirli
/// bir süreci bekleyen test onu kimliğiyle seçer (<see cref="WaitForProcess"/>); PID'ini bildiği süreci bekleyen ya da yalnız sayan test
/// için mevcut üyeler zararsızdır. (Eskiden burada "port bağlanmadan önce doğan üye görülmez" yazıyordu — yanlıştı.)</para>
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

    /// <summary>[I-3] Port'un bildirdiği üyelerden <paramref name="match"/>'e uyan İLK süreci, tutamacı AÇIK olarak döner — tutamaç bildirim
    /// anında alınır: süreç sonradan çıksa ya da PID'i yeniden kullanılsa da nesne o süreçtir. Bildirimle açılış arasında çıkmış bir üye
    /// (ör. eski motorun conhost'u) ve uymayan üye atlanır; <paramref name="match"/>'in okuyamadığı (açıldıktan sonra çıkmış) süreç uymuyor
    /// sayılır. Bildirim sırası job'a giriş sırasıdır. Çağıran dönen süreci dispose eder.</summary>
    public static Process WaitForProcess(JobCompletionPort port, TimeSpan timeout, string what, Func<Process, bool> match)
    {
        ArgumentNullException.ThrowIfNull(match);
        Process? found = null;
        Until(port, timeout, what, pid =>
        {
            var process = TryOpen(pid);
            if (process is null) return false;
            bool matched;
            try { matched = match(process); }
            catch (InvalidOperationException) { matched = false; } // açıldıktan sonra çıktı: kimliği okunamaz
            if (!matched)
            {
                process.Dispose();
                return false;
            }
            found = process;
            return true;
        });
        return found!;
    }

    /// <summary>Bildirilen PID'i açar ve tutamacını HEMEN alır; süreç çoktan çıktıysa <c>null</c>.</summary>
    private static Process? TryOpen(int pid)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(pid);
            _ = process.SafeHandle; // tutamaç şimdi: bundan sonra nesne bu süreçtir
            return process;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            process?.Dispose();
            return null;
        }
    }
}
