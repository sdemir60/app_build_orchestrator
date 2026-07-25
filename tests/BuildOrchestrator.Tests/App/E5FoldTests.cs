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

    // ------------------------------------------------------------------ [a11y kararı] DsSplitter klavye resize persist
    [StaFact]
    public void Keyboard_resizing_the_splitter_commits_through_the_drag_completed_path()
    {
        var grid = new Grid { Width = 400, Height = 200 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var splitter = new DsSplitter { LineOrientation = SplitterLine.Vertical };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);

        var host = DsResources.NewHost();
        var window = DsResources.Realize(host, grid);

        // Basıştan ÖNCEKİ sol-kolon genişliği (ShellRoot persist'i ActualWidth okur). 2 star-kolon 400px'i
        // ~yarı yarıya paylaşır (ayraç Auto ~7px) → başlangıçta > 0 ve iki taraf ~eşit.
        double prePress = grid.ColumnDefinitions[0].ActualWidth;
        Assert.True(prePress > 0);

        bool committed = false;
        double atCompletion = double.NaN;
        splitter.DragCompleted += (_, _) =>
        {
            committed = true;
            // ShellRoot'un persist yolu TAM BURADA ActualWidth okur — ayırt edici gerçek: bu okuma TAZE mi?
            atCompletion = grid.ColumnDefinitions[0].ActualWidth;
        };

        splitter.Focus();
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(splitter)!, 0, Key.Left)
        { RoutedEvent = Keyboard.KeyDownEvent };
        splitter.RaiseEvent(key);

        Assert.True(key.Handled);   // taban GridSplitter ok-tuşuyla resize etti
        Assert.True(committed);     // ...ve DsSplitter persist'i DragCompleted ile tetikledi
        // AYIRT EDİCİ: Sol ok sol-kolonu KÜÇÜLTÜR; DragCompleted anında okunan ActualWidth resize'ı YANSITMALI
        // (basıştan küçük). UpdateLayout() olmadan taban yalnız async arrange planlar → okuma BAYAT kalır
        // (atCompletion == prePress) → persist stale oranı yazar. Bu assert o hatayı yakalar.
        Assert.True(atCompletion < prePress,
            $"DragCompleted anında ActualWidth taze olmalı (resize sonrası küçülmüş): prePress={prePress}, atCompletion={atCompletion}");
        GC.KeepAlive(window);
    }
}
