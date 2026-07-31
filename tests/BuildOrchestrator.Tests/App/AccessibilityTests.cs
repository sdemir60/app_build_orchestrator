using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E5/T47] Ekran-okuyucu (AutomationProperties.Name) kapsamı + klavye odak yönetimi. İkon-yalnız kontroller
/// (sayaç chip'leri, branch/worktree/perf chip'leri, sync/stop) İngilizce UIA-adı taşır (tek kaynak
/// <see cref="AccessibilityNames"/>); proje kartı adını, ayraçlar işlevini duyurur; şerit faz metni bir
/// assertive live region'dır; popover açılınca odak içeri; filtre Ctrl+F ile odaklanır, içindeki Esc yalnız
/// temizler+blur eder.
/// </summary>
[Collection("Console UI (serial)")]
public class AccessibilityTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    // ------------------------------------------------------------------ UIA-adı kapsamı
    [StaFact]
    public void Action_bar_names_its_icon_only_controls_for_screen_readers()
    {
        var vm = NewVm();
        var host = DsResources.NewHost();
        var bar = new ActionBar { DataContext = vm };
        var window = DsResources.Realize(host, bar);

        Assert.Equal(AccessibilityNames.FilterAll, AutomationProperties.GetName(bar.SigmaChip));
        Assert.Equal(AccessibilityNames.FilterBuilding, AutomationProperties.GetName(bar.BuildingChip));
        Assert.Equal(AccessibilityNames.FilterSucceeded, AutomationProperties.GetName(bar.SucceededChip));
        Assert.Equal(AccessibilityNames.FilterFailed, AutomationProperties.GetName(bar.FailedChip));
        Assert.Equal(AccessibilityNames.FilterSkipped, AutomationProperties.GetName(bar.SkippedChip));
        Assert.Equal(AccessibilityNames.FilterDep, AutomationProperties.GetName(bar.DepChip));
        Assert.Equal(AccessibilityNames.BranchChip, AutomationProperties.GetName(bar.BranchChip));
        Assert.Equal(AccessibilityNames.WorktreeChip, AutomationProperties.GetName(bar.WorktreeChip));
        Assert.Equal(AccessibilityNames.PerfChip, AutomationProperties.GetName(bar.PerfChip));
        Assert.Equal(AccessibilityNames.SyncButton, AutomationProperties.GetName(bar.SyncButton));
        Assert.Equal(AccessibilityNames.StopButton, AutomationProperties.GetName(bar.StopButton));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_project_row_exposes_its_project_name_to_screen_readers()
    {
        var row = new ProjectRow { AnimationsEnabledProvider = () => false,
            DataContext = new ProjectRowViewModel(@"C:\p\Foo.csproj", "Foo", ProjectRowState.Pending) };
        Assert.Equal("Foo", AutomationProperties.GetName(row));
    }

    [StaFact]
    public void The_splitters_are_named_for_screen_readers()
    {
        var shell = new ShellRoot();
        Assert.Equal(AccessibilityNames.ColumnSplitter, AutomationProperties.GetName(shell.ColumnSplitter));
        Assert.Equal(AccessibilityNames.GraphListSplitter, AutomationProperties.GetName(shell.LeftSplitter));
        Assert.Equal(AccessibilityNames.ConsoleStreamSplitter, AutomationProperties.GetName(shell.RightSplitter));
    }

    // ------------------------------------------------------------------ [A13/T5] adı olmayan yüzeyler

    /// <summary>
    /// [A13/T5 · n1] Graf düğümü ekran okuyucuya <b>görünür ve anlamlı</b> olmalı: kare/ikon/rozet görselleri
    /// hiçbir şey söylemez, sabit bir "graph node" metni de söylemezdi — ad DÜĞÜM BAŞINA (tam proje adı + statü).
    ///
    /// <para><b>İki ayrı iddia:</b> (a) gövde UIA ağacında bir ÖĞE'dir — düz bir <c>StackPanel</c>'in peer'ı
    /// yoktur ve ona verilen ad ekran okuyucuya hiç ulaşmazdı (bkz. <see cref="GraphNodeBody"/>); (b) ad VERİ
    /// DEĞİŞİNCE tazelenir. Kurulum üretim sırasıyladır (kabuk ÖNCE realize, veri SONRA) ve statü, üretimin kendi
    /// yolundan itilir: <c>ProjectStartedEvent</c> → <c>Counters</c> → <c>PushGraphStatuses</c> →
    /// <c>UpdateStatuses</c>.</para>
    /// </summary>
    [StaFact]
    public void A_graph_node_is_a_named_automation_element_and_its_name_follows_the_data()
    {
        using var temp = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(temp, ("OSYS.Base", null), ("OSYS.Domain", null));
        var body = window.Shell.GraphHost.NodeVisuals["OSYS.Base"].Body;

        var peer = UIElementAutomationPeer.CreatePeerForElement(body);
        Assert.True(peer is not null, "Düğüm gövdesinin automation peer'ı YOK — UIA ağacında hiç görünmüyor.");
        Assert.Equal(AutomationControlType.Button, peer!.GetAutomationControlType()); // tıklanır → buton rolü
        Assert.Equal(AccessibilityNames.GraphNode("OSYS.Base", "Discovered"), peer.GetName());

        // Veri akınca (bu proje derlenmeye başlayınca) ad da tazelenir — bayat ad YASAK.
        vm.OnEvent(new ProjectStartedEvent("r1", MainWindowHost.IdOf("OSYS.Base"), "OSYS.Base"));
        Assert.Equal(AccessibilityNames.GraphNode("OSYS.Base", "Building"), peer.GetName());
        // Komşu düğüm etkilenmez (ad düğüm başına, tek bir ortak metin DEĞİL).
        Assert.Equal(AccessibilityNames.GraphNode("OSYS.Domain", "Discovered"),
            AutomationProperties.GetName(window.Shell.GraphHost.NodeVisuals["OSYS.Domain"].Body));
    }

    /// <summary>[A13/T5 · n2] Konsol başlığındaki ikon-yalnız copy butonu adlanır; ad ile tooltip AYNI sabitten
    /// gelir. Kopyalama geri bildirimi tooltip'i "Copied" yapar ama <b>adı DEĞİŞTİRMEZ</b> — ad kontrolün
    /// işlevini tarif eder, anlık durumunu değil.</summary>
    [StaFact]
    public void The_copy_log_button_is_named_and_keeps_that_name_while_the_tooltip_flips_to_copied()
    {
        var header = new ConsoleHeader { LogTextProvider = () => "log", ClipboardWriter = _ => true };
        Assert.Equal(AccessibilityNames.CopyLog, AutomationProperties.GetName(header.CopyLogButton));
        Assert.Equal(AccessibilityNames.CopyLog, header.CopyLogButton.ToolTip);

        header.CopyLog(); // gerçek panoya DOKUNMAZ (writer enjekte)

        Assert.Equal("Copied", header.CopyLogButton.ToolTip);
        Assert.Equal(AccessibilityNames.CopyLog, AutomationProperties.GetName(header.CopyLogButton));
    }

    /// <summary>[A13/T5 · n3] `⌄ latest` pill'i üç panelde ORTAK bir kontroldür; "latest" etiketi tek başına
    /// ekran okuyucuya hiçbir şey söylemez → adı HANGİ akışın sonuna gidildiğini bilen host verir ve üç ad
    /// FARKLIdır. Ad, kabuğa değil TIKLANAN butona konur (UIA'da buton öğesi odur).</summary>
    [StaFact]
    public void Each_latest_pill_names_the_stream_it_jumps_to()
    {
        using var temp = new TempDir();
        var (window, _, _) = MainWindowHost.NewWithProjects(temp, ("OSYS.Base", null));
        var shell = window.Shell;

        string projects = AutomationProperties.GetName(shell.PART_ProjectsPill.PillButton);
        string console = AutomationProperties.GetName(shell.ConsoleViewControl.Pill.PillButton);
        string events = AutomationProperties.GetName(shell.EventStreamControl.Pill.PillButton);

        Assert.Equal(AccessibilityNames.LatestProjects, projects);
        Assert.Equal(AccessibilityNames.LatestConsole, console);
        Assert.Equal(AccessibilityNames.LatestEvents, events);
        Assert.Equal(3, new[] { projects, console, events }.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>[A13/T5 · n4] Settings katman kartının iki input'u adlanır. Kolon başlıkları (LAYER NAME /
    /// PATTERN) input'a BAĞLI değildir ve watermark bir ipucudur (desen kutusununki bir ÖRNEK regex'tir) —
    /// bu yüzden ad açıkça verilir. Hangi adın hangi kutuya gittiği watermark üzerinden pinlenir.</summary>
    [StaFact]
    public void The_layer_editor_inputs_are_named_for_screen_readers()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(
            run => run.LayerPatterns = [new LayerPattern(0, "^A", "Alpha")]);
        using var _scope = scope;
        dialog.UpdateLayout(); // katman satırı GERÇEKTEN kurulsun (ItemsControl kabı)

        var inputs = DsResources.RealizedObjects(dialog).OfType<TextBox>().ToList();
        Assert.Equal(2, inputs.Count); // ön-koşul: satır kuruldu (yoksa aşağıdaki iddialar vakum olurdu)
        var byWatermark = inputs.ToDictionary(t => DsChrome.GetWatermark(t)!, AutomationProperties.GetName,
            StringComparer.Ordinal);
        Assert.Equal(AccessibilityNames.LayerName, byWatermark["Layer name"]);
        Assert.Equal(AccessibilityNames.LayerPattern, byWatermark[@"^OSYS\.Domain\."]);
    }

    /// <summary>[A13/T5 · n5] Worktree hedef listesindeki çöp kutusu ikon-yalnızdır ve satır başına BİR tane
    /// vardır → ad HANGİ worktree'nin silineceğini söyler (tooltip kısa kalır, ikisi de tek kaynaktan).
    /// Hedef satırları yalnız switch AÇIKKEN kurulur — üretim yolu izlenir.</summary>
    [StaFact]
    public void The_worktree_delete_button_names_the_worktree_it_removes()
    {
        var vm = NewVm();
        vm.Worktrees.Add(new Worktree("main-1", @"D:\wt\main-1", "main", false, null));
        var host = DsResources.NewHost();
        var popover = new WorktreePopover { DataContext = vm };
        var window = DsResources.Realize(host, popover);

        vm.UseWorktree = true; // → Refresh → BuildTargetRows (auto satırı + "main-1" satırı)

        var trash = Assert.Single(DsResources.RealizedObjects(popover.PART_TargetRows).OfType<Button>());
        Assert.Equal(AccessibilityNames.DeleteWorktreeNamed("main-1"), AutomationProperties.GetName(trash));
        Assert.Equal(AccessibilityNames.DeleteWorktree, trash.ToolTip);
        GC.KeepAlive(window);
    }

    // ------------------------------------------------------------------ live region
    [StaFact]
    public void The_ribbon_phase_text_is_an_assertive_live_region()
    {
        var ribbon = new StickyRibbon();
        Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(ribbon.PhaseText));
    }

    // ------------------------------------------------------------------ klavye odak / focus ring
    [StaFact]
    public void A_project_row_is_a_focusable_tab_stop_with_the_amber_focus_ring()
    {
        var host = DsResources.NewHost();
        var row = new ProjectRow { AnimationsEnabledProvider = () => false };
        var window = DsResources.Realize(host, row);
        Assert.True(row.Focusable);
        Assert.True(row.IsTabStop);
        Assert.NotNull(row.FocusVisualStyle); // Ds.FocusVisual (amber halka) çözülür
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_splitter_is_a_keyboard_focusable_tab_stop_with_a_focus_ring()
    {
        var host = DsResources.NewHost();
        var splitter = new DsSplitter();
        var window = DsResources.Realize(host, splitter);
        Assert.True(splitter.Focusable);
        Assert.True(splitter.IsTabStop);
        Assert.NotNull(splitter.FocusVisualStyle);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_project_list_lets_arrow_keys_move_between_rows()
    {
        // Ok tuşlarıyla satırlar arası gezinme: Flow DirectionalNavigation=Contained olmalı (odak liste içinde kalır).
        var list = new StickyLayerList();
        Assert.Equal(KeyboardNavigationMode.Contained, KeyboardNavigation.GetDirectionalNavigation(list.RowFlow));
    }

    [StaFact]
    public void The_branch_popover_moves_focus_into_the_search_box_on_open()
    {
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent([new BranchRef("main", "aaaaaaaaaaaa", true, false)]));
        var host = DsResources.NewHost();
        var popover = new BranchPopover { DataContext = vm };
        var window = DsResources.Realize(host, popover);

        popover.IsOpen = true; // OnIsOpenChanged → Dispatcher.BeginInvoke(Input) ile arama kutusuna odak
        DispatcherPump.PumpUntil(() => popover.SearchBox.IsKeyboardFocused, TimeSpan.FromSeconds(2));

        Assert.True(popover.SearchBox.IsKeyboardFocused);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Escape_inside_the_branch_popover_requests_close_without_leaking()
    {
        var vm = NewVm();
        vm.OnEvent(new BranchListEvent([new BranchRef("main", "aaaaaaaaaaaa", true, false)]));
        var host = DsResources.NewHost();
        var popover = new BranchPopover { DataContext = vm };
        var window = DsResources.Realize(host, popover);
        popover.IsOpen = true;

        bool closeRequested = false;
        popover.CloseRequested += () => closeRequested = true;

        // Esc arama kutusundan bubble eder → popover kapanır (popover ayrı HWND; pencere Esc zinciri buraya gelmez).
        var esc = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(popover.SearchBox)!, 0, Key.Escape)
        { RoutedEvent = Keyboard.KeyDownEvent };
        popover.SearchBox.RaiseEvent(esc);

        Assert.True(closeRequested);
        Assert.True(esc.Handled);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Ctrl_f_focuses_the_project_filter_input()
    {
        var (shell, box, window) = RealizeFilterBox();

        bool focused = shell.FocusProjectFilter(); // Ctrl+F'in çağırdığı GERÇEK metot
        DispatcherPump.PumpUntil(() => box.IsKeyboardFocused, TimeSpan.FromSeconds(2));

        Assert.True(focused);
        Assert.True(box.IsKeyboardFocused);
        Assert.Equal(AccessibilityNames.ProjectFilter, AutomationProperties.GetName(box));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Escape_inside_the_filter_clears_the_query_and_blurs_without_leaking()
    {
        var (_, box, window) = RealizeFilterBox();
        box.Focus();
        box.Text = "core";
        DispatcherPump.PumpUntil(() => box.IsKeyboardFocused, TimeSpan.FromSeconds(2));

        var esc = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box)!, 0, Key.Escape)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        box.RaiseEvent(esc); // ShellRoot ctor'da bağlanan GERÇEK OnFilterKeyDown handler'ı (box'a asılı, reparent'te kalır)

        Assert.True(esc.Handled);            // stopPropagation — global Esc zincirine sızmaz
        Assert.Equal("", box.Text);          // sorgu temizlendi
        Assert.False(box.IsKeyboardFocused); // blur
        GC.KeepAlive(window);
    }

    /// <summary>ShellRoot'un TAMAMI headless realize edilemez (bir RowDefinition'ın <c>Size.ActionBarHeight</c>
    /// Double DynamicResource'u GridLength'e dönüşürken host'a ekleme anında patlar — üretimde App-seviyesi
    /// kaynaklarla sorun olmaz). GERÇEK filtre kutusu + ona ShellRoot ctor'da bağlanan Esc handler'ı korunur:
    /// kutu mevcut ebeveyninden ayrılıp hafif bir host'ta realize edilir; <c>FocusProjectFilter</c> aynı referansı
    /// odakladığından metot da gerçek yolla sınanır.</summary>
    private static (ShellRoot shell, TextBox box, Window window) RealizeFilterBox()
    {
        var shell = new ShellRoot();
        var box = shell.ProjectFilterBox;
        if (LogicalTreeHelper.GetParent(box) is ContentPresenter cp) cp.Content = null; // kutuyu PanelHeader'dan ayır
        var host = DsResources.NewHost();
        var window = DsResources.Realize(host, box);
        return (shell, box, window);
    }

    // ------------------------------------------------------------------ [A13/T5] KAPSAM TESTİ

    /// <summary>
    /// <b>"Etkileşimli yüzey" TANIMI</b> — bu testin sözleşmesi. Realize edilmiş ağaçtaki, UIA'ya <b>kendi öğesi
    /// olarak</b> görünen (automation peer'ı olan) ve kullanıcı eylemi alan öğeler:
    /// <list type="bullet">
    ///   <item><see cref="ButtonBase"/> — Button / ToggleButton / CheckBox / RadioButton / RepeatButton (tıklanır)</item>
    ///   <item><see cref="TextBox"/> — yazılır</item>
    ///   <item><see cref="Thumb"/> — sürüklenir (<c>DsSplitter</c> bir <c>GridSplitter</c>, o da Thumb'dır)</item>
    ///   <item><see cref="GraphNodeBody"/> — graf düğümünün tıklanabilir gövdesi</item>
    /// </list>
    /// <b>Bilinçli olarak KAPSAM DIŞI:</b> peer'ı OLMAYAN tıklanabilir <c>Border</c>/<c>Panel</c> satırlar (branch
    /// listesi, build menüsü, worktree hedef satırı, event stream satırı). Onlar UIA ağacında zaten kendi öğeleri
    /// olarak görünmez — ad vermek TEK BAŞINA yetmez (graf düğümünde ölçülen durumun aynısı, bkz.
    /// <see cref="GraphNodeBody"/>) ve düzeltmeleri T5 kapsamı dışıdır.
    /// </summary>
    private static bool IsInteractiveSurface(DependencyObject node) =>
        node is ButtonBase or TextBox or Thumb or GraphNodeBody;

    /// <summary>
    /// <b>MEŞRU İSTİSNALAR</b> — her biri TEK SATIR gerekçeyle; sessiz istisna YASAK. Liste bayatlamaz: kapsam
    /// testi her girdinin gerçekten bir yüzeye denk geldiğini ayrıca doğrular.
    /// </summary>
    private static readonly (Func<DependencyObject, bool> Match, string Reason)[] NamingExceptions =
    [
        (el => DsResources.SelfAndAncestors(el).OfType<ScrollBar>().Any(),
            "WPF ScrollBar şablonunun parçaları (ok RepeatButton'ları + Track Thumb'ı): adları framework'ün kendi " +
            "scroll pattern'inden gelir, uygulamanın verdiği bir metin değildir."),
        (el => el is ButtonBase { Name: "PART_Primary", TemplatedParent: SplitButton },
            "Build split-button'ın SOL yarımı: görünür etiketi ('Build'/'Continue') ActionBar'da dinamik kurulur, " +
            "UIA adı SplitButton'a yeni bir DP ister → T5 kapsamı dışı BORÇ (task raporunda Concerns)."),
    ];

    /// <summary>Hata mesajında yüzeyi bulunabilir kılar: tip + x:Name + kısa ata zinciri.</summary>
    private static string Identify(DependencyObject node) =>
        $"{node.GetType().Name}{(node is FrameworkElement { Name.Length: > 0 } fe ? "#" + fe.Name : "")}" +
        $" [{string.Join(" < ", DsResources.SelfAndAncestors(node).Skip(1).Take(4).Select(a => a.GetType().Name))}]";

    /// <summary>Taranan yüzey sayısının alt sınırı — bir tarama testinin en büyük riski sessizce HİÇBİR ŞEY
    /// taramamaktır (o hâlde sonsuza dek yeşil kalır). Ölçülen sayı bunun epey üstündedir; sınır, ağacın
    /// çökmesini (ör. fixture'ın artık realize etmemesi) yakalayacak kadar yüksektir.</summary>
    private const int MinimumScannedSurfaces = 40;

    /// <summary>
    /// [A13/T5] <b>KAPSAM TESTİ:</b> uygulamanın etkileşimli yüzeylerini realize edilmiş GERÇEK ağaçlar üzerinde
    /// tarar ve <b>adsız olanı RED eder</b>. Tek tek adları sınayan testlerden farkı: SONRADAN eklenen adsız bir
    /// yüzeyi de yakalar.
    ///
    /// <para><b>Ad = ekran okuyucunun GERÇEKTEN duyacağı ad:</b> ham <c>AutomationProperties.Name</c> değil,
    /// peer'ın <c>GetName()</c>'i okunur — böylece string <c>Content</c>'ten ("Cancel"/"Save"/"← Back") gelen
    /// meşru adlar yanlış pozitif üretmez. Peer'ın KENDİSİ de zorunludur: peer'ı olmayan bir öğe UIA ağacında
    /// hiç yoktur, ona verilen ad ekran okuyucuya ulaşmaz.</para>
    ///
    /// <para><b>Taranan üç kök (hepsi üretim yoluyla realize):</b>
    /// <list type="number">
    ///   <item><b>Kabuk</b> (<see cref="MainWindowHost"/>): graf düğümleri, proje listesi, ayraçlar, konsol
    ///     başlığı, üç pill, title bar, Settings diyaloğu. <b>ActionBar alt-ağacı HARİÇ</b> — ActionBar adlarını
    ///     <c>Loaded</c>'da kurar, bu fixture ise pencereyi ASLA <c>Show</c> etmez (motor doğmasın diye); o yüzden
    ///     ActionBar aşağıda KENDİ kökünde, gerçekten realize edilerek taranır.</item>
    ///   <item><b>ActionBar</b>: sayaç chip'leri, sync/stop, split-button, Debug|Release, branch/worktree
    ///     popover'ları (worktree switch AÇIK → hedef satırı + çöp kutusu da dahil).</item>
    ///   <item><b>Settings diyaloğu</b> (bir katmanla): katman satırının ad/desen input'ları + footer.</item>
    /// </list></para>
    /// </summary>
    [StaFact]
    public void Every_interactive_surface_in_the_app_has_a_screen_reader_name()
    {
        using var temp = new TempDir();
        var (window, _, _) = MainWindowHost.NewWithProjects(temp, ("OSYS.Base", null), ("OSYS.Domain", "Core"));
        var shellObjects = DsResources.RealizedObjects((FrameworkElement)window.Content);
        var actionBarSubtree = DsResources.RealizedObjects(shellObjects.OfType<ActionBar>().Single());

        var vm = NewVm();
        vm.Worktrees.Add(new Worktree("main-1", @"D:\wt\main-1", "main", false, null));
        var barHost = DsResources.NewHost();
        var bar = new ActionBar { DataContext = vm };
        var barWindow = DsResources.Realize(barHost, bar);
        vm.UseWorktree = true; // → worktree popover'ında hedef satırı + çöp kutusu GERÇEKTEN kurulur

        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(
            run => run.LayerPatterns = [new LayerPattern(0, "^A", "Alpha")]);
        using var _scope = scope;
        dialog.UpdateLayout();

        var surfaces = shellObjects.Where(o => !actionBarSubtree.Contains(o))
            .Concat(DsResources.RealizedObjects(bar))
            .Concat(DsResources.RealizedObjects(dialog))
            .Where(IsInteractiveSurface)
            .ToList();

        var offenders = new List<string>();
        var names = new List<string>();
        int[] exceptionHits = new int[NamingExceptions.Length];
        int scanned = 0;

        foreach (var surface in surfaces)
        {
            int exception = Array.FindIndex(NamingExceptions, e => e.Match(surface));
            if (exception >= 0) { exceptionHits[exception]++; continue; }

            scanned++;
            var peer = UIElementAutomationPeer.CreatePeerForElement((UIElement)surface);
            if (peer is null) { offenders.Add(Identify(surface) + " → UIA öğesi DEĞİL (peer yok)"); continue; }
            string name = peer.GetName();
            if (string.IsNullOrWhiteSpace(name)) offenders.Add(Identify(surface) + " → ADSIZ");
            else names.Add(name);
        }

        Assert.True(offenders.Count == 0,
            $"Ekran okuyucuya adsız {offenders.Count} etkileşimli yüzey:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders));

        // Tarama VAKUM DEĞİL: hem toplam sayı hem de T5'in beş yüzeyinin her biri gerçekten tarandı — aksi halde
        // "hepsi adlı" iddiası boş/eksik bir kümede de doğru çıkardı.
        Assert.True(scanned >= MinimumScannedSurfaces, $"Yalnız {scanned} yüzey tarandı — ağaç kurulmamış olabilir.");
        foreach (string expected in new[]
                 {
                     AccessibilityNames.GraphNode("OSYS.Base", "Discovered"), // n1
                     AccessibilityNames.CopyLog,                              // n2
                     AccessibilityNames.LatestProjects,                       // n3
                     AccessibilityNames.LatestConsole,
                     AccessibilityNames.LatestEvents,
                     AccessibilityNames.LayerName,                            // n4
                     AccessibilityNames.LayerPattern,
                     AccessibilityNames.DeleteWorktreeNamed("main-1"),        // n5
                 })
            Assert.Contains(expected, names);

        // İstisna listesi bayatlamaz: her girdi gerçekten taranan bir yüzeye denk gelmeli.
        for (int i = 0; i < NamingExceptions.Length; i++)
            Assert.True(exceptionHits[i] > 0, $"Karşılığı kalmamış istisna [{i}]: {NamingExceptions[i].Reason}");

        GC.KeepAlive(barWindow);
    }

    // ------------------------------------------------------------------ isim sözlüğü İngilizce
    [Fact]
    public void All_accessibility_names_are_non_empty_and_free_of_turkish_letters()
    {
        const string turkish = "çğıİşöüÇĞŞÖÜ";
        foreach (var f in typeof(AccessibilityNames).GetFields(BindingFlags.Public | BindingFlags.Static)
                     .Where(f => f is { IsLiteral: true, IsInitOnly: false }))
        {
            var value = (string)f.GetValue(null)!;
            Assert.False(string.IsNullOrWhiteSpace(value), $"{f.Name} boş");
            Assert.DoesNotContain(value, c => turkish.Contains(c));
        }
    }
}
