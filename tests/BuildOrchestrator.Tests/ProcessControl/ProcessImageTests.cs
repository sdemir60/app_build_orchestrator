using System.IO;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Core.ProcessControl;

namespace BuildOrchestrator.Tests.ProcessControl;

/// <summary>
/// [Exit] <see cref="ProcessImage.NameOf"/>: bir PID'in image dosya adı — onay kartının "ENDS ON EXIT" listesi job'un üyelerini bununla
/// adlandırır. Makine çapında süreç taraması yoktur (<c>NoProcessScanGuardTests</c>): PID tek tek açılır. Çıkmış ya da açılamayan süreç
/// adsızdır (<c>null</c>) — <see cref="BuildOrchestrator.Core.Diagnostics.MemoryLine.PrivateBytesOf"/>'un kuralı: tutamacı başka biri
/// açık tuttukça çıkmış bir sürecin <c>OpenProcess</c>'i de başarır, bu yüzden çıkış kodu ayrıca sorulur.
/// </summary>
public sealed class ProcessImageTests
{
    [Fact]
    public void A_live_process_is_named_by_its_image_file()
    {
        Assert.Equal(Path.GetFileName(Environment.ProcessPath), ProcessImage.NameOf(Environment.ProcessId));
    }

    [Fact]
    public async Task A_process_that_has_exited_has_no_name_even_while_its_handle_is_open()
    {
        using var job = JobObject.CreateKillOnClose();
        using var child = JobProcessLauncher.Launch(job, WindowsCommandLine.Build(JobTestChildren.CmdExe, "/c", "exit 0"),
            new LaunchOptions(RedirectStdio: false));
        await child.WaitForExitAsync();

        Assert.Null(ProcessImage.NameOf(child.Pid)); // tutamaç child'da açık — OpenProcess başarır, çıkış kodu söyler
    }

    [Fact]
    public void An_impossible_pid_has_no_name()
    {
        Assert.Null(ProcessImage.NameOf(-1));
    }
}
