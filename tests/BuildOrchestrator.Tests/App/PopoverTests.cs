using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
/// [D6/T40] Branch popover'ı GERÇEKTEN kurulup sürülerek pinlenir (BuildApp.jsx:830-852): arama BÜYÜK/küçük harf
/// duyarsız alt-dize filtreler ve popover kapanınca (<see cref="BranchPopover.IsOpen"/>=false) sorgu SIFIRLANIR.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class PopoverTests
{
    /// <summary>
    /// Popover gövdesi kabuğunun İÇ ALANINA sığar — genişliği iki yerde birden tanımlanmaz.
    ///
    /// <para><b>ÖLÇÜLDÜ:</b> kabuk (<c>Ds.Popover</c>) 272px, 1px kenar + 8px dolgu ile iç alanı 254'tür; gövde
    /// ise kendi <c>Width</c>'ini 256 diye yeniden yazıyordu (kenar payı unutulmuş). WPF taşan gövdeye bir
    /// yerleşim kırpması uygular: arama kutusunun SAĞ kenarı ve satırların sağdaki sha'ları kesiliyordu.</para>
    ///
    /// <para>Doğru kaynak kabuktur: gövde genişliğini kabuktan ALIR (kopya YASAK, CLAUDE.md).</para>
    ///
    /// <para>[spec 2026-09-18 §1-1] Eskiden iki popover sayılırdı (branch + worktree); worktree popover'ı
    /// kalktığı için test tek kabuğu sürer (ad: <c>Both_popover_bodies_fit_the_width_their_shell_gives_them</c>).</para>
    /// </summary>
    [StaFact]
    public void The_branch_popover_body_fits_the_width_its_shell_gives_it()
    {
        var host = DsResources.NewHost();
        var bar = new ActionBar();
        var window = DsResources.Realize(host, bar, 1200, 200);

        var popup = bar.BranchPopup;
        popup.IsOpen = true;
        var shell = (Border)popup.Child;
        shell.UpdateLayout();

        double inner = shell.ActualWidth - shell.Padding.Left - shell.Padding.Right
                       - shell.BorderThickness.Left - shell.BorderThickness.Right;
        var body = DsResources.Descendants(shell).OfType<StackPanel>().First();

        Assert.True(body.ActualWidth <= inner,
            $"kabuk {shell.ActualWidth}px, iç alanı {inner}px — gövde {body.ActualWidth}px ile taşıyor.");
        popup.IsOpen = false;

        GC.KeepAlive(window);
    }

    /// <summary>
    /// [branch row] Satıra sığmayan bir branch adı sağdaki kısa sha'nın (aktif branch'te <c>active</c> rozetinin) ÜSTÜNE taşmaz: ad, onlardan
    /// önce <c>…</c> ile kısalır. Popover gerçek kabuğunda (alt barın 272 px'lik <c>Ds.Popover</c>'ı) açılır ve ölçülür.
    ///
    /// <para><b>Kusur:</b> ad, ikonla birlikte YATAY bir <see cref="StackPanel"/>'in içindeydi. Yatay panel çocuğunu genişlik sınırı vermeden
    /// ölçer: adın <c>CharacterEllipsis</c> kırpması hiç devreye girmez, ad tam boyunda yerleşir ve satırın sağ sütununa — sha'nın ya da
    /// rozetin üstüne — taşar.</para>
    /// </summary>
    [StaTheory]
    [InlineData(false)] // sağda kısa sha
    [InlineData(true)]  // sağda "active" rozeti
    public void A_long_branch_name_ends_in_an_ellipsis_before_the_rows_right_column(bool active)
    {
        const string longName = "feature/a-branch-name-long-enough-to-run-past-the-right-column";
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent([
            new BranchRef("main", "aaaaaaaaaaaa", !active, false),
            new BranchRef(longName, "bbbbbbbccccc", active, false),
        ]));
        var host = DsResources.NewHost();
        var bar = new ActionBar { DataContext = vm };
        var window = DsResources.Realize(host, bar, 1200, 200);
        var popup = bar.BranchPopup;
        popup.IsOpen = true;
        var shell = (Border)popup.Child;
        shell.UpdateLayout();

        var row = DsResources.Descendants(shell).OfType<BranchRow>().Single(r => r.DataContext is BranchRowItem { Branch.Name: longName });
        var texts = DsResources.Descendants(row).OfType<TextBlock>().ToList();
        var name = texts.Single(t => t.Text == longName);
        FrameworkElement right = active
            ? (FrameworkElement)VisualTreeHelper.GetParent(texts.Single(t => t.Text == "active")) // rozetin kendisi (çerçevesiyle)
            : texts.Single(t => t.Text == RunViewModel.Short7("bbbbbbbccccc"));
        double nameRight = name.TransformToAncestor(row).Transform(new Point(name.ActualWidth, 0)).X;
        double rightLeft = right.TransformToAncestor(row).Transform(default).X;

        Assert.True(nameRight <= rightLeft,
            $"ad {nameRight:F1} px'te bitiyor, sağ sütun {rightLeft:F1} px'te başlıyor — ad {nameRight - rightLeft:F1} px üstüne biniyor.");
        popup.IsOpen = false;
        GC.KeepAlive(window);
    }

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), MainWindowHost.NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    // ---------------------------------------------------------------- [branch keys] klavyeyle seçim

    private static readonly BranchRef[] KeyBranches =
    [
        new("main", "aaaaaaaaaaaa", true, false),
        new("feature/x", "bbbbbbbccccc", false, false),
        new("feature/y", "cccccccddddd", false, false),
        new("develop", "dddddddeeeee", false, false),
    ];

    /// <summary>Popover'ı açık kurar (arama kutusu odaktadır); motora gidecek komutları ve popover'ın kapanma isteklerini toplar.</summary>
    private static (BranchPopover Popover, List<IpcCommand> Sent, Func<int> Picks, Window Window) OpenForKeys(BranchRef[]? branches = null)
    {
        var vm = NewVm();
        var sent = new List<IpcCommand>();
        vm.DebugOnCommandSent = sent.Add;
        vm.DebugSendOverride = _ => Task.CompletedTask;
        vm.OnEvent(new BranchListEvent([.. branches ?? KeyBranches]));
        var host = DsResources.NewHost();
        var popover = new BranchPopover { DataContext = vm };
        var window = DsResources.Realize(host, popover);
        popover.IsOpen = true;
        popover.UpdateLayout();
        int picks = 0;
        popover.BranchPicked += () => picks++;
        return (popover, sent, () => picks, window);
    }

    private static void Press(BranchPopover popover, params Key[] keys)
    {
        foreach (var key in keys) KeyInput.Press(popover.SearchBox, key);
        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(50)); // seçim checkout'u kuyruğa bırakır
    }

    private static string? CheckedOut(List<IpcCommand> sent) => sent.OfType<CheckoutBranchCommand>().SingleOrDefault()?.Branch;

    /// <summary>
    /// [branch keys] Aradıktan sonra fareye gitmeden seçim: ↓ ilk eşleşmeyi işaretler, Enter işaretli branch'i seçer — tıklamayla AYNI yol
    /// (popover kapanır, checkout gider). Kullanıcının tarifi: "aradım, sonra aşağı ok ile inip Enter dediğimde seçsin". Eskiden ok tuşları
    /// ve Enter arama kutusunda hiçbir şey yapmıyordu.
    /// </summary>
    [StaFact]
    public void After_a_search_Down_then_Enter_checks_out_the_first_match()
    {
        var (popover, sent, picks, window) = OpenForKeys();
        popover.SearchBox.Text = "feature";

        Press(popover, Key.Down, Key.Enter);

        Assert.Equal(1, picks());
        Assert.Equal("feature/x", CheckedOut(sent));
        GC.KeepAlive(window);
    }

    /// <summary>[branch keys] ↓/↑ işareti eşleşen satırlar arasında gezdirir; son satırda ↓ işareti orada tutar.</summary>
    [StaTheory]
    [InlineData(new[] { Key.Down, Key.Down }, "feature/y")]
    [InlineData(new[] { Key.Down, Key.Down, Key.Up }, "feature/x")]
    [InlineData(new[] { Key.Down, Key.Down, Key.Down, Key.Down }, "feature/y")]
    public void Arrows_move_the_highlight_through_the_matches(Key[] keys, string expected)
    {
        var (popover, sent, _, window) = OpenForKeys();
        popover.SearchBox.Text = "feature";

        Press(popover, [.. keys, Key.Enter]);

        Assert.Equal(expected, CheckedOut(sent));
        GC.KeepAlive(window);
    }

    /// <summary>[branch keys] Enter yalnız işaretli bir satırı seçer — yanlışlıkla bir checkout olmasın: işaret yokken, ilk satırda ↑ ile
    /// arama kutusuna dönüldükten sonra ya da yazmaya devam edilince (sorgu değişince işaret düşer) Enter hiçbir şey seçmez.</summary>
    [StaFact]
    public void Enter_picks_nothing_without_a_highlighted_row()
    {
        var (popover, sent, picks, window) = OpenForKeys();
        popover.SearchBox.Text = "feature";

        Press(popover, Key.Enter);
        Press(popover, Key.Down, Key.Up, Key.Enter);
        Press(popover, Key.Down);
        popover.SearchBox.Text = "feature/";
        Press(popover, Key.Enter);

        Assert.Equal(0, picks());
        Assert.Empty(sent.OfType<CheckoutBranchCommand>());
        GC.KeepAlive(window);
    }

    /// <summary>[branch keys] İşaretli satır fareyle üstüne gelinmiş satır gibi çizilir (<c>surface-raised</c> zemin); diğerleri saydam kalır.</summary>
    [StaFact]
    public void The_highlighted_row_is_drawn_like_a_hovered_row()
    {
        var (popover, _, _, window) = OpenForKeys();

        Press(popover, Key.Down, Key.Down); // main → feature/x
        popover.UpdateLayout();

        var rows = DsResources.Descendants(popover).OfType<BranchRow>().ToList();
        Color Background(string name) =>
            ((SolidColorBrush)((Border)rows.Single(r => r.DataContext is BranchRowItem { Branch.Name: var n } && n == name).Content).Background).Color;
        var raised = ((SolidColorBrush)popover.FindResource("Brush.SurfaceRaised")).Color;
        Assert.Equal(raised, Background("feature/x"));
        Assert.Equal(0, Background("main").A);
        Assert.Equal(0, Background("feature/y").A);
        GC.KeepAlive(window);
    }

    /// <summary>[branch keys] İşaret görünür satırların altına inince liste kayar: işaretli satır kaydırıcının görünen alanındadır.</summary>
    [StaFact]
    public void Moving_down_past_the_visible_rows_scrolls_the_highlight_into_view()
    {
        var many = Enumerable.Range(0, 20).Select(i => new BranchRef($"feature/{i:D2}", $"{i:D12}", i == 0, false)).ToArray();
        var (popover, _, _, window) = OpenForKeys(many);

        Press(popover, Enumerable.Repeat(Key.Down, 12).ToArray()); // feature/11
        popover.UpdateLayout();

        var scroller = DsResources.Descendants(popover).OfType<ScrollViewer>().Single(s => DsResources.Descendants(s).OfType<BranchRow>().Any());
        var row = DsResources.Descendants(scroller).OfType<BranchRow>().SingleOrDefault(r => r.DataContext is BranchRowItem { Branch.Name: "feature/11" });
        Assert.NotNull(row);
        Rect bounds = row.TransformToAncestor(scroller).TransformBounds(new Rect(row.RenderSize));
        Assert.True(bounds.Top >= -0.5 && bounds.Bottom <= scroller.ActualHeight + 0.5,
            $"işaretli satır {bounds.Top:F1}–{bounds.Bottom:F1} px'te, kaydırıcı 0–{scroller.ActualHeight:F1} px gösteriyor.");
        GC.KeepAlive(window);
    }

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
    /// metninin CURLY tırnakları (<c>“…”</c>, BuildApp.jsx:846 — düz <c>"…"</c> DEĞİL).
    /// <para><b>[DEĞİŞEN KURAL — spec 2026-09-18 §1-1/8 · design v1.21.0 §2.8]</b> Eski alt not "Picking a
    /// non-active branch requires a worktree; the active branch stays untouched." idi. Worktree kalktı; branch
    /// değiştirmek artık çalışma ağacında gerçek bir checkout'tur ve not bunu söyler.</para></summary>
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
        Assert.Contains("Switching checks the branch out in your working tree.", texts);

        popover.IsOpen = true;
        popover.SearchBox.Text = "zzz";
        Assert.Equal("No branches match “zzz”.", popover.PART_Empty.Text); // curly “ ” — ASCII " " DEĞİL
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [W2 pin] popover'ın ORTAK iskeleti

    /// <summary>
    /// [W2 pin] Açılış davranışı (PopoverBase'ten gelen): (a) 140ms pop-in OYNAR — reduced-motion'da
    /// (headless <c>App.Motion</c> null) son duruma SNAP eder, yani opaklık 1'e çekilir; (b) odak İÇERİ taşınır
    /// (<c>Dispatcher.BeginInvoke(Input)</c>). Fold sırasında bu iki adımdan biri düşerse test kırılır.
    /// </summary>
    [StaTheory]
    [InlineData(typeof(BranchPopover))]
    [InlineData(typeof(UpdateCard))] // [design v1.23.0 §2.12] güncelleme kartı aynı iskeleti paylaşır
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

    /// <summary>[W2 pin] Esc popover'da <c>CloseRequested</c>'i yayar ve olayı YUTAR (ayrı HWND → pencere
    /// Esc zinciri buraya ulaşmaz; popover kendisi yakalamalı).</summary>
    [StaTheory]
    [InlineData(typeof(BranchPopover))]
    [InlineData(typeof(UpdateCard))] // [design v1.23.0 §2.12] güncelleme kartı aynı iskeleti paylaşır
    public void Escape_inside_a_popover_requests_close_and_is_handled(Type popoverType)
    {
        var host = DsResources.NewHost();
        var popover = (UserControl)Activator.CreateInstance(popoverType)!;
        popover.DataContext = NewVm();
        var window = DsResources.Realize(host, popover);
        SetIsOpen(popover, true);

        bool closeRequested = false;
        AddCloseHandler(popover, () => closeRequested = true);

        var esc = KeyInput.Raise(popover, Key.Escape, Keyboard.KeyDownEvent);

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
        // PopoverBase'in ORTAK iskeleti sürülür — her popover türü için ayrı bir dal gerekmez.
        ((PopoverBase)popover).IsOpen = value;
    }

    private static void AddCloseHandler(UserControl popover, Action handler)
    {
        ((PopoverBase)popover).CloseRequested += handler;
    }

    // ---------------------------------------------------------------- [A13/T4 · m3] pop-in: 140ms · 4px · .985

    /// <summary>[A13/T4 · m3 · fix-1 · A3/C4] Otorite <c>BuildApp.jsx:21,33</c>: <c>.bo-pop-in { animation:
    /// bo-pop-in .14s var(--ease-out) both; } @keyframes bo-pop-in { from { opacity:0; transform: translateY(4px)
    /// scale(.985); } to { opacity:1; transform:none; } }</c>. <c>Opening_a_popover_plays_the_pop_in_and_moves_focus_inside</c>
    /// (yukarıda) YALNIZ reduced-motion kolunu (statik <c>App.Motion</c> null → SNAP) sürer — <see cref="PopIn"/>'in
    /// GERÇEK animasyonlu geometrisi (4px/.985/140ms) hiçbir testte oynamamıştı.
    ///
    /// <para><b>fix-1 · A3:</b> <c>PopIn.DurationMs</c> artık SAF <c>Assert.Equal</c> ile de pinli (önceden yalnız
    /// gerçek-saat penceresi vardı, 140→300 gibi bir sapmayı geçirirdi).</para>
    /// <para><b>fix-1 · C4:</b> <c>App.Motion</c> statik seam set/restore'u artık <see cref="MotionScope"/>
    /// (paylaşılan, TEK yer) üzerinden — önceden bu try/finally <c>MotionOwnerHygieneTests.AssertSubscribesOnce</c>'ın
    /// birebir kopyasıydı.</para>
    ///
    /// <para><b><c>LocalOnly</c>:</b> bu test GERÇEK animasyon saatini bekler (<c>PumpUntil</c> opaklığın hedefe
    /// varmasını bekler, <c>InRange</c> geçen duvar saatini sınar) ve paylaşılan CI runner'ında zamanlaması
    /// kararsız — ardışık CI koşularında düştü; lokalde geçiyor. Eşik GEVŞETİLMEZ (CLAUDE.md): yalnız bu metot CI
    /// filtresinden çıkar (sınıfın diğer testleri CI'da koşar), lokal tam süit (yayının kapısı) onu koşturmaya
    /// devam eder.</para></summary>
    [StaFact]
    [Trait("Category", "LocalOnly")]
    public void Opening_a_popover_plays_a_real_140ms_pop_in_rising_4px_from_a_985_scale()
    {
        Assert.Equal(140.0, PopIn.DurationMs); // BuildApp.jsx:21 `.14s` — saf literal pin (A13/T4 fix-1)

        using var _ = MotionScope.Enable(new MotionSettings(new FakeMotionSignal { AnimationsEnabled = true }));
        var host = DsResources.NewHost();
        var popover = new BranchPopover { DataContext = NewVm() };
        var window = DsResources.Realize(host, popover);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        popover.IsOpen = true; // ÜRETİM yolu: PopoverBase.RefreshContent → PopIn.Play(this)

        // t≈0: başlangıç geometrisi otoriteyle BİREBİR (translateY(4px) scale(.985), opacity 0).
        var group = Assert.IsType<TransformGroup>(popover.RenderTransform);
        var scale = Assert.IsType<ScaleTransform>(group.Children[0]);
        var translate = Assert.IsType<TranslateTransform>(group.Children[1]);
        Assert.Equal(0.985, scale.ScaleX);
        Assert.Equal(0.985, scale.ScaleY);
        Assert.Equal(4.0, translate.Y);
        Assert.True(popover.HasAnimatedProperties, "opacity/scale/translate GERÇEKTEN animasyonlu değil");

        DispatcherPump.PumpUntil(() => popover.Opacity >= 0.99, TimeSpan.FromSeconds(2));
        clock.Stop();

        Assert.True(popover.Opacity >= 0.99, "pop-in hiç tamamlanmadı");
        Assert.Equal(0.0, translate.Y, precision: 1); // hedefe ulaştı: translateY(0)
        // BuildApp.jsx:21 `.14s` — üretim yolundan GERÇEKTEN oynadığının (zamana bağlı, tamamlayıcı) kanıtı;
        // asıl 140ms iddiası artık yukarıdaki saf pin'de.
        Assert.InRange(clock.ElapsedMilliseconds, 60, 400);
        GC.KeepAlive(window);
    }

    /// <summary>[W2 · REALIZE TESTİ] <see cref="BranchPopover"/> AÇIKKEN realize + layout: sınıf tabanının değişmesi
    /// XAML kökünün taban tipini değiştirir ve headless suite XAML runtime çözümlemesini görmez (commit
    /// <c>c6e9a21</c> dersi: 1198 test yeşil, uygulama açılmıyor). Popup çocuğu parse zamanı kurulur ama
    /// measure/arrange ancak IsOpen=true'da çalışır — ShellRoot realize testi bu yolu görmez.</summary>
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

    // [A13/T3 fix-1 · B7] b1 ("ActionBar popover kabuğu 272px") ARTIK ActionBarTests'te: kalem ActionBar'ın
    // KENDİ kabuğuna aittir (ActionBar.xaml), bu dosya ise BranchPopover kontrolünündür; test burada dururken
    // ActionBarTests.Realize'ı inline kopyalıyordu.
}
