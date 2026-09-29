using BuildOrchestrator.App.Graph;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı bildirimi 2026-09-29] Bir iş sürerken basılıp bekleyen koşunun KABUKTAKİ yüzü: istek henüz bir koşu
/// değildir, dolayısıyla koşunun görsel dili ancak koşu açılınca gelir. VM kuralları
/// <see cref="RunRequestWaitsForWorkTests"/>'tedir; burada realize edilmiş pencere ölçülür.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class RunRequestWaitsForWorkShellTests
{
    /// <summary>Graf koşu fazına (derlenmeyen düğümlerin 0.13'e söndüğü faz) bekleyen istekle GİRMEZ — iş, örneğin bir
    /// Sync, sürerken graf onun ve önceki sonucun resmidir. Koşu açıldığı anda girer. Ölçülen kusur: istek
    /// <c>IsStarting</c>'i taşıdığı için graf işin tamamı boyunca koşu sanıp sönüyordu.</summary>
    [StaFact]
    public void The_graph_stays_out_of_the_run_phase_while_a_build_waits_and_enters_it_when_the_run_opens()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        MainWindowHost.AcceptSends(vm);
        vm.OnEvent(new SyncStartedEvent(@"C:\src\OSYS", "main")); // Sync düğmesinin Sync'i sürüyor

        Assert.True(CommandPress.Press(vm.BuildCommand));
        Assert.Equal(GraphRunPhase.Idle, window.Shell.GraphHost.RunPhase);

        vm.OnEvent(new WorkspaceTopologyEvent([MainWindowHost.Node("A", 0)], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0)); // iş bitti — koşu açılır

        Assert.Equal(GraphRunPhase.Running, window.Shell.GraphHost.RunPhase);
        GC.KeepAlive(window);
    }
}
