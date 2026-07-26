using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [D6/T40] Branch popover'ı GERÇEKTEN kurulup sürülerek pinlenir (BuildApp.jsx:830-852): arama BÜYÜK/küçük harf
/// duyarsız alt-dize filtreler ve popover kapanınca (<see cref="BranchPopover.IsOpen"/>=false) sorgu SIFIRLANIR.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class PopoverTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    [StaFact]
    public void Branch_popover_filters_case_insensitively_and_resets_its_query_on_close()
    {
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent([
            new BranchRef("main", "aaaaaaaaaaaa", true, false),
            new BranchRef("feature/X", "bbbbbbbccccc", false, true),
            new BranchRef("develop", "cccccccddddd", false, true),
        ]));

        var host = DsResources.NewHost();
        var popover = new BranchPopover { DataContext = vm };
        var window = DsResources.Realize(host, popover);

        popover.IsOpen = true;
        Assert.Equal(3, popover.VisibleBranches.Count); // açılışta tam liste

        popover.SearchBox.Text = "FEA"; // büyük harf sorgu, küçük harf ad → duyarsız eşleşme
        Assert.Single(popover.VisibleBranches);
        Assert.Equal("feature/X", popover.VisibleBranches[0].Name);

        popover.IsOpen = false;                          // kapanış → sorgu sıfırlanır (BuildApp.jsx:833)
        Assert.Equal("", popover.SearchBox.Text);
        Assert.Equal(3, popover.VisibleBranches.Count);  // filtre kalktı
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Branch_popover_shows_the_no_match_empty_state()
    {
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent([new BranchRef("main", "aaaaaaaaaaaa", true, false)]));

        var host = DsResources.NewHost();
        var popover = new BranchPopover { DataContext = vm };
        var window = DsResources.Realize(host, popover);

        popover.IsOpen = true;
        popover.SearchBox.Text = "zzz";
        Assert.Empty(popover.VisibleBranches);
        Assert.True(popover.IsEmptyState); // "No branches match “zzz”."
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [W2 pin] iki popover'ın ORTAK iskeleti

    /// <summary>
    /// [W2 pin] Açılış davranışı İKİ popover'da da AYNI olmalı: (a) 140ms pop-in OYNAR — reduced-motion'da
    /// (headless <c>App.Motion</c> null) son duruma SNAP eder, yani opaklık 1'e çekilir; (b) odak İÇERİ taşınır
    /// (<c>Dispatcher.BeginInvoke(Input)</c>). Fold sırasında bu iki adımdan biri düşerse test kırılır.
    /// </summary>
    [StaTheory]
    [InlineData(typeof(BranchPopover))]
    [InlineData(typeof(WorktreePopover))]
    public void Opening_a_popover_plays_the_pop_in_and_moves_focus_inside(Type popoverType)
    {
        Assert.Null(BuildOrchestrator.App.App.Motion); // reduced yolu: pop-in SNAP eder (vacuous PASS koruması)
        var host = DsResources.NewHost();
        var popover = (UserControl)Activator.CreateInstance(popoverType)!;
        popover.DataContext = NewVm();
        var window = DsResources.Realize(host, popover);

        popover.Opacity = 0.5;      // pop-in çağrılmazsa 0.5'te KALIR → assertion kırılır (non-vacuous)
        SetIsOpen(popover, true);

        Assert.Equal(1.0, popover.Opacity); // PopIn.Play reduced yolu: son duruma snap
        DispatcherPump.PumpUntil(() => popover.IsKeyboardFocusWithin, TimeSpan.FromSeconds(2));
        Assert.True(popover.IsKeyboardFocusWithin); // odak İÇERİ (ilk etkileşimli öğe)
        GC.KeepAlive(window);
    }

    /// <summary>[W2 pin] Esc İKİ popover'da da <c>CloseRequested</c>'i yayar ve olayı YUTAR (ayrı HWND → pencere
    /// Esc zinciri buraya ulaşmaz; popover kendisi yakalamalı).</summary>
    [StaTheory]
    [InlineData(typeof(BranchPopover))]
    [InlineData(typeof(WorktreePopover))]
    public void Escape_inside_a_popover_requests_close_and_is_handled(Type popoverType)
    {
        var host = DsResources.NewHost();
        var popover = (UserControl)Activator.CreateInstance(popoverType)!;
        popover.DataContext = NewVm();
        var window = DsResources.Realize(host, popover);
        SetIsOpen(popover, true);

        bool closeRequested = false;
        AddCloseHandler(popover, () => closeRequested = true);

        var esc = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(popover)!, 0, Key.Escape)
        { RoutedEvent = Keyboard.KeyDownEvent };
        popover.RaiseEvent(esc);

        Assert.True(closeRequested);
        Assert.True(esc.Handled);
        GC.KeepAlive(window);
    }

    private static void SetIsOpen(UserControl popover, bool value)
    {
        switch (popover)
        {
            case BranchPopover b: b.IsOpen = value; break;
            case WorktreePopover w: w.IsOpen = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(popover));
        }
    }

    private static void AddCloseHandler(UserControl popover, Action handler)
    {
        switch (popover)
        {
            case BranchPopover b: b.CloseRequested += handler; break;
            case WorktreePopover w: w.CloseRequested += handler; break;
            default: throw new ArgumentOutOfRangeException(nameof(popover));
        }
    }

    /// <summary>[W2 · REALIZE TESTİ] <see cref="BranchPopover"/> AÇIKKEN realize + layout — <see cref="WorktreePopover"/>
    /// kardeşiyle (aşağıda) AYNI gerekçe: sınıf tabanının değişmesi XAML kökünün taban tipini değiştirir ve headless
    /// suite XAML runtime çözümlemesini görmez (commit <c>c6e9a21</c> dersi: 1198 test yeşil, uygulama açılmıyor).</summary>
    [StaFact]
    public void The_branch_popover_realizes_and_lays_out_while_open()
    {
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent([new BranchRef("main", "aaaaaaaaaaaa", true, false)]));
        var host = DsResources.NewHost();
        var popover = new BranchPopover { DataContext = vm };
        var window = DsResources.Realize(host, popover);

        popover.IsOpen = true;
        popover.UpdateLayout(); // açıkken measure/arrange — token/şablon uyuşmazlığı burada patlar

        Assert.True(popover.ActualWidth > 0);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [D6/T40] <see cref="WorktreePopover"/> AÇIKKEN gerçekten realize olabilmeli. ShellRoot'un launch-fatal'ı
    /// (Double token → GridLength, commit c6e9a21) ActionBar'ın inline Popup içeriğinde tekrarlasaydı LAUNCH
    /// değil CLICK-fatal olurdu: Popup çocuğu parse zamanı kurulur ama measure/arrange ancak IsOpen=true'da
    /// çalışır — yani ShellRoot realize testi bu yolu görmez. Bu test o yolu kapatır: throw = kırmızı.
    /// </summary>
    [StaFact]
    public void The_worktree_popover_realizes_and_lays_out_while_open()
    {
        var host = DsResources.NewHost();
        var popover = new WorktreePopover { DataContext = NewVm() };
        var window = DsResources.Realize(host, popover);

        popover.IsOpen = true;
        popover.UpdateLayout(); // açıkken measure/arrange — token/şablon uyuşmazlığı burada patlar

        Assert.True(popover.ActualWidth > 0);
        GC.KeepAlive(window);
    }
}
