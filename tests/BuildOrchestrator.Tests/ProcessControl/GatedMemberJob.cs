using System.Diagnostics;
using BuildOrchestrator.Core.ProcessControl;

namespace BuildOrchestrator.Tests.ProcessControl;

/// <summary>
/// [temizlik madde 4] Canlı üyeli bir job: kill-on-close job + IOCP + kapılı çocuk (<see cref="JobTestChildren.GatedBurnChildCmdLine"/>) ve
/// çocuğun stdin'de bekleyen torunu <c>more</c> — doğumunu job'un portu bildirir (uyku/poll yok, D8). Kaynak şeridinin okuma yolunu canlı
/// bir üyeyle sınayan testlerin TEK kurulumu: <c>ResourceSamplerTests</c>'te üç, <c>JobResourceQueryTests</c>'te bir test aynı beş satırı
/// yazıyordu (kopya YASAK).
/// <para>Bırakma kurulumun TERS sırasıyladır (<c>more</c> → çocuk → port → job), ayrı <c>using</c> satırlarının yaptığı gibi; kurulum yarıda
/// düşerse o ana kadar kurulanlar bırakılır ve istisna aynen yükselir. Job kill-on-close'dur: kapanınca bekleyen üyeler de ölür.</para>
/// </summary>
internal sealed class GatedMemberJob : IDisposable
{
    public JobObject Job { get; }

    public JobCompletionPort Port { get; }

    /// <summary>Kapılı çocuk (<c>cmd</c>): stdin'i kapanana dek <c>more</c>'u bekler, sonra sınırlı bir yük yakıp çıkar.</summary>
    public JobChildProcess Child { get; }

    /// <summary>Çocuğun stdin'de bekleyen torunu, tutamacı açık. .NET yalnız ".exe" uzantısını atar: <c>more.com</c>'un
    /// <c>ProcessName</c>'i "more.com"dur — seçici bu yüzden önekle bakar.</summary>
    public Process More { get; }

    private GatedMemberJob(JobObject job, JobCompletionPort port, JobChildProcess child, Process more) =>
        (Job, Port, Child, More) = (job, port, child, more);

    public static GatedMemberJob Start()
    {
        var job = JobObject.CreateKillOnClose();
        JobCompletionPort? port = null;
        JobChildProcess? child = null;
        try
        {
            port = job.AttachCompletionPort();
            child = JobProcessLauncher.Launch(job, JobTestChildren.GatedBurnChildCmdLine(), new LaunchOptions(RedirectStdio: true));
            var more = JobBirths.WaitForProcess(port, TimeSpan.FromSeconds(30), "the waiting process was never born",
                process => process.ProcessName.StartsWith("more", StringComparison.OrdinalIgnoreCase));
            return new GatedMemberJob(job, port, child, more);
        }
        catch
        {
            child?.Dispose();
            port?.Dispose();
            job.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        More.Dispose();
        Child.Dispose();
        Port.Dispose();
        Job.Dispose();
    }
}
