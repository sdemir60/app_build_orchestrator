using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E5 fold'ları] Bu task'ın tükettiği üç birikmiş fold: (L2 M2) StickyRibbon.OnUnloaded'ın VM aboneliğini
/// BIRAKMASI (leak fix); (final/latent) BranchPopover'ın AÇIKKEN gelen branch envanterini canlı yansıtması;
/// (a11y kararı) DsSplitter'ın klavye ok-tuşu resize'ının persist'i mevcut DragCompleted yoluna bağlaması.
/// </summary>
[Collection("Console UI (serial)")]
public class E5FoldTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    // ------------------------------------------------------------------ [L2 M2] StickyRibbon leak fix
    [StaFact]
    public void The_ribbon_stops_reacting_to_the_vm_after_it_unloads()
    {
        var vm = NewVm();
        var host = DsResources.NewHost();
        var ribbon = new StickyRibbon { DataContext = vm };
        var window = DsResources.Realize(host, ribbon); // Loaded → VM'e abone

        vm.Phase = AppPhase.Running;
        string running = ribbon.PhaseText.Text;
        vm.Phase = AppPhase.Done;
        string done = ribbon.PhaseText.Text;
        Assert.NotEqual(running, done); // ayırt edici gerçek: abonelik çalışıyor (metin faz ile değişiyor)

        ribbon.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); // OnUnloaded → VM aboneliği BIRAKILIR
        vm.Phase = AppPhase.Stopped;
        Assert.Equal(done, ribbon.PhaseText.Text); // artık güncellenmiyor → leak kapalı
        GC.KeepAlive(window);
    }

    // ------------------------------------------------------------------ [final/latent] BranchPopover canlı envanter
    [StaFact]
    public void The_branch_popover_reflects_a_branch_inventory_that_arrives_while_it_is_open()
    {
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent([new BranchRef("main", "aaaaaaaaaaaa", true, false)]));

        var host = DsResources.NewHost();
        var popover = new BranchPopover { DataContext = vm };
        var window = DsResources.Realize(host, popover);

        popover.IsOpen = true;
        Assert.Single(popover.VisibleBranches);

        // Ref-only fetch tamamlanınca yeni branch envanteri gelir — popover AÇIKKEN (CollectionChanged aboneliği
        // olmasa liste bayat "1 branch"te kalırdı).
        vm.OnEvent(new BranchListEvent([
            new BranchRef("main", "aaaaaaaaaaaa", true, false),
            new BranchRef("feature/x", "bbbbbbbccccc", false, true),
        ]));

        Assert.Equal(2, popover.VisibleBranches.Count);
        GC.KeepAlive(window);
    }

    // [A13/T1 fix-1 · S2] DsSplitter klavye-resize persist testi konu olarak ayraca aittir → SplitterDragTests'e
    // TAŞINDI (aynı kontrolün sürükleme/renk testleriyle bir arada; kurulum artık ortak SplitterHost'ta).
}
