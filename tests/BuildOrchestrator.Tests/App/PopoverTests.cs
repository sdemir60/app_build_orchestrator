using System.Linq;
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

    // ---------------------------------------------------------------- [A13/T3a · a4] kopya metinleri (BİREBİR)

    /// <summary>[A13/T3a · a4] design-v1 §2.8: caps başlık <c>SWITCH BRANCH</c>, alt not (BİREBİR) ve boş-eşleşme
    /// metninin CURLY tırnakları (<c>“…”</c>, BuildApp.jsx:846 — düz <c>"…"</c> DEĞİL).</summary>
    [StaFact]
    public void Branch_popover_pins_the_caps_caption_footnote_and_curly_quoted_empty_state()
    {
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent([new BranchRef("main", "aaaaaaaaaaaa", true, false)]));
        var host = DsResources.NewHost();
        var popover = new BranchPopover { DataContext = vm };
        var window = DsResources.Realize(host, popover);

        var texts = DsResources.RealizedObjects(popover).OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("SWITCH BRANCH", texts);
        Assert.Contains("Picking a non-active branch requires a worktree; the active branch stays untouched.", texts);

        popover.IsOpen = true;
        popover.SearchBox.Text = "zzz";
        Assert.Equal("No branches match “zzz”.", popover.PART_Empty.Text); // curly “ ” — ASCII " " DEĞİL
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [A13/T3a · a1] Worktree popover kopya metinleri

    /// <summary>[A13/T3a · a1] design-v1 §2.8 üç durum açıklaması + source satırının iki varyantı — BİREBİR
    /// (WorktreePopover.xaml.cs Refresh()). forced → on → source hiçbiri süitte pinli DEĞİLDİ.</summary>
    [StaFact]
    public void Worktree_popover_pins_the_three_state_descriptions_and_both_source_line_variants()
    {
        var vm = NewVm();
        vm.Branch = "main";
        var host = DsResources.NewHost();
        var popover = new WorktreePopover { DataContext = vm };
        var window = DsResources.Realize(host, popover);
        popover.IsOpen = true;

        // off: UseWorktree=false, forced=false (hiç branch envanteri yok → IsWorktreeForced=false).
        Assert.Equal("Off: in-place build — local changes included.", popover.PART_Desc.Text);
        Assert.Equal("working directory — local changes included", popover.PART_Source.Text);

        // on: UseWorktree=true, forced=false.
        vm.UseWorktree = true;
        Assert.Equal("The committed HEAD builds in a separate worktree; local changes excluded.", popover.PART_Desc.Text);
        Assert.Equal($"committed HEAD (main) → {vm.EffectiveWorktreeName}", popover.PART_Source.Text);

        // forced: aktif-olmayan bir branch seçildi (K3) → worktree ZORUNLU.
        vm.OnEvent(new BranchListEvent([
            new BranchRef("main", "aaaaaaaaaaaa", true, false),
            new BranchRef("release/x", "bbbbbbbbbbbb", false, true),
        ]));
        vm.SelectBranch(new BranchRef("release/x", "bbbbbbbbbbbb", false, true));
        Assert.True(vm.IsWorktreeForced);
        Assert.Equal(
            "Different branch selected — worktree required. The committed HEAD is built; active branch and local changes stay untouched.",
            popover.PART_Desc.Text);

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

    /// <summary>
    /// [W2 fix-1 · latent bug] Taban sınıf kablajını <b>türevin görsel ağacı kurulduktan SONRA</b> bağlamalıdır.
    /// Taban ctor'u türevin <c>InitializeComponent()</c>'inden ÖNCE koşar; orada <c>DataContextChanged</c>'e abone
    /// olmak, XAML kökü <c>DataContext</c>'i kendi attribute'uyla atadığı anda <c>RefreshContent</c>'i türevin
    /// <c>PART_*</c> alanları HENÜZ null iken çağırırdı. Bugünkü iki popover kökü DataContext atamıyor, yani bu
    /// bir zaman bombasıydı — <see cref="XamlLikePopover"/> tam o sırayı taklit eder ve eski kablajda RED verir.
    /// </summary>
    [StaFact]
    public void A_popover_wires_its_datacontext_hook_only_after_its_own_visual_tree_exists()
    {
        var vm = NewVm();

        var popover = new XamlLikePopover(vm); // ctor içinde: DataContext ÖNCE, adlandırılmış öğe SONRA

        Assert.Same(vm, popover.Model);       // seed: kablajdan önce atanan DataContext kaçırılmadı
        Assert.Equal(1, popover.SubscribeCount);
        Assert.True(popover.RefreshCount >= 1);

        // Kablaj gerçekten CANLI: sonraki DataContext takasında eski VM bırakılır, yenisine abone olunur.
        var next = NewVm();
        popover.DataContext = next;
        Assert.Same(next, popover.Model);
        Assert.Equal(1, popover.UnsubscribeCount);
        Assert.Equal(2, popover.SubscribeCount);
    }

    /// <summary>Bir XAML kökünün yükleme sırasını taklit eden test popover'ı: <c>DataContext</c> kök attribute'u
    /// olarak (adlandırılmış çocuklardan ÖNCE) atanır. <see cref="RefreshContent"/> "PART" alanının kurulmuş
    /// olmasını ŞART koşar — kablaj erken bağlanırsa burada patlar.</summary>
    private sealed class XamlLikePopover : PopoverBase
    {
        private TextBox? _part;

        public XamlLikePopover(RunViewModel vm)
        {
            BeginInit();
            DataContext = vm;          // kök attribute'u
            _part = new TextBox();     // x:Name'li çocuk (connector)
            Content = _part;
            EndInit();                 // → OnInitialized → kablaj + VM seed
        }

        public RunViewModel? Model => Vm;
        public int RefreshCount { get; private set; }
        public int SubscribeCount { get; private set; }
        public int UnsubscribeCount { get; private set; }

        protected override UIElement InitialFocusTarget => _part!;

        protected override void RefreshContent()
        {
            Assert.NotNull(_part); // erken kablajda burası null olurdu → RED
            RefreshCount++;
        }

        protected override void SubscribeVm(RunViewModel vm) => SubscribeCount++;
        protected override void UnsubscribeVm(RunViewModel vm) => UnsubscribeCount++;
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
