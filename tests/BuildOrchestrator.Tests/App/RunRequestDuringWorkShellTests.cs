using BuildOrchestrator.App.Graph;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı kararı 2026-10-02] İş sürerken koşu komutlarının KABUKTAKİ yüzü: Sync sürerken graf o işin ve önceki
/// sonucun resmidir, koşu fazına yalnız bir koşu açılınca girer; iş biterken hiçbir koşu kendiliğinden açılmaz. VM
/// kuralları <see cref="RunRequestDuringWorkTests"/>'tedir; burada realize edilmiş pencere ölçülür.
///
/// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-02]</b> Eski dosya (<c>RunRequestWaitsForWorkShellTests</c>,
/// [kullanıcı bildirimi 2026-09-29]) bekleyen isteğin kabuktaki yüzünü pinliyordu: istek henüz bir koşu değildir, graf
/// koşu fazına istekle girmez, koşu iş bitince açılınca girer (ölçülen kusur: istek <c>IsStarting</c>'i taşıdığı için
/// graf işin tamamı boyunca koşu sanıp sönüyordu). İstek kalktı: iş sürerken Build kapalıdır ve iş bitince koşuyu
/// kullanıcının basışı açar. Pinlenen sözleşme aynıdır — graf koşu fazına yalnız koşu açılınca girer — yalnız tetikleyen
/// değişti.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class RunRequestDuringWorkShellTests
{
    /// <summary>Graf koşu fazına (derlenmeyen düğümlerin 0.13'e söndüğü faz) bir Sync sürerken GİRMEZ — Build kapalıdır,
    /// graf Sync'in ve önceki sonucun resmidir. Sync bitince de kendiliğinden girmez; kullanıcı Build'e basınca girer.</summary>
    [StaFact]
    public void The_graph_stays_out_of_the_run_phase_during_a_sync_and_enters_it_when_a_build_opens_after_it()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        MainWindowHost.AcceptSends(vm);
        vm.OnEvent(new SyncStartedEvent(@"C:\src\OSYS", "main")); // Sync düğmesinin Sync'i sürüyor

        Assert.False(CommandPress.Press(vm.BuildCommand)); // kapalı: basış kuyruğa girmez
        Assert.Equal(GraphRunPhase.Idle, window.Shell.GraphHost.RunPhase);

        vm.OnEvent(new WorkspaceTopologyEvent([MainWindowHost.Node("A", 0)], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 0)); // iş bitti — kapı açıldı, koşu açılmadı
        Assert.Equal(GraphRunPhase.Idle, window.Shell.GraphHost.RunPhase);

        Assert.True(CommandPress.Press(vm.BuildCommand)); // kullanıcı basar — koşu açılır
        Assert.Equal(GraphRunPhase.Running, window.Shell.GraphHost.RunPhase);
        GC.KeepAlive(window);
    }
}
