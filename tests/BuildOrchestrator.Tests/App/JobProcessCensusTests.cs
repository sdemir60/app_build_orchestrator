using BuildOrchestrator.App.Services;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Tests.ProcessControl;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [Exit] Onay kartının "ENDS ON EXIT" listesi: outer job'un canlı üyeleri, ada göre sayılmış (<see cref="JobProcessCensus"/>). Motorun
/// kendisi listede yoktur — uygulamanın parçasıdır, tasarımın listesi işi yapan süreçleri sayar; konsol konakları (<c>conhost.exe</c>)
/// da yoktur — job'a motorun ve çocuklarının konsolu olarak girerler (<c>JobBirths</c>'in notu), iş yapmazlar. Sıra: en kalabalık önce,
/// eşitlikte ada göre. Job okunamazsa liste boştur — kart süreç bloğunu hiç çizmez.
/// </summary>
public sealed class JobProcessCensusTests
{
    [Fact]
    public void Names_are_counted_most_first_then_by_name()
    {
        var tally = JobProcessCensus.Tally(["MSBuild.exe", "VBCSCompiler.exe", "MSBuild.exe", "msbuild.exe", "git.exe"]);

        Assert.Equal(
            [new ProcessTally("MSBuild.exe", 3), new ProcessTally("git.exe", 1), new ProcessTally("VBCSCompiler.exe", 1)],
            tally);
    }

    [Fact]
    public void Console_hosts_are_not_listed()
    {
        Assert.Equal([new ProcessTally("MSBuild.exe", 1)], JobProcessCensus.Tally(["conhost.exe", "MSBuild.exe", "Conhost.exe"]));
    }

    /// <summary>Gerçek job: cmd (motor yerine) + powershell torunu. Liste torunu adlandırır, "motoru" dışarıda bırakır.</summary>
    [Fact]
    public void The_census_lists_the_job_members_but_not_the_engine()
    {
        using var job = JobObject.CreateKillOnClose();
        using var port = job.AttachCompletionPort();
        using var engine = JobProcessLauncher.Launch(job, JobTestChildren.SleepChildCmdLine(), new LaunchOptions(RedirectStdio: false));
        JobBirths.Until(port, TimeSpan.FromSeconds(30), "powershell was not born into the job",
            pid => ProcessImage.NameOf(pid) is "powershell.exe");

        var tally = new JobProcessCensus(job, () => engine.Pid).Take();

        Assert.Contains(new ProcessTally("powershell.exe", 1), tally);
        Assert.DoesNotContain(tally, t => t.Name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(tally, t => t.Name.Equals("conhost.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_closed_job_reads_as_an_empty_list()
    {
        var job = JobObject.CreateKillOnClose();
        job.Dispose();

        Assert.Empty(new JobProcessCensus(job, () => null).Take());
    }
}
