using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [v1.17.0 §2.4 "Katman başlıkları tıklanabilir" — Task 5] Yapışık katman başlığı artık bir gezinme
/// kontrolü: hover'da bir yüzey adımı açılır, tıklama (in-flow VEYA overlay — AYNI şablon) o grubun ilk
/// satırını yığılmış başlıkların hemen altına getirir. Seçim/filtre/konsol/graf DEĞİŞMEZ — yalnız scroll.
/// Aritmetik <see cref="LayoutMetricsTests"/>'te SAF olarak pinlidir; burada WPF kablajı.
///
/// <para>Headless'ta GERÇEK mouse hover (<c>IsMouseOver</c>) simüle edilemez (ActionBarHoverTests'in aynı
/// notu) — hover hedefleri bu yüzden STİL TETİKLEYİCİLERİ üzerinden okunur (ActionBarTests.Hover deseni).
/// Tıklama ise Direct bir routed event (<see cref="UIElement.MouseLeftButtonUpEvent"/>) olduğundan GERÇEKTEN
/// realize edilmiş Border üzerinde <c>RaiseEvent</c> ile tetiklenip GERÇEK <c>Scroll.VerticalOffset</c>
/// üzerinden doğrulanır.</para>
///
/// <para><b>Ortam notu (flaky kaynağı):</b> basış GERÇEK bir HWND'ye karşı yükseltilir (<c>CaptureMouse()</c>
/// bir HWND olmadan sessizce başarısız olur — bkz. <see cref="DsResources.Realize"/> çağrısındaki "GERÇEK
/// HWND" notu), yani bu testler headless/ekran-dışı bir çizimden FARKLI olarak gerçek pencere/capture/dispatcher
/// mekaniğine bağımlıdır. Makine eşzamanlı ağır bir işle meşgulken (paralel build, yoğun disk/CPU) tıklama
/// yanlış hedefe düşebilir ve bu testler KIRMIZI verebilir — bir regresyondan şüphelenmeden ÖNCE sakin bir
/// makinede yeniden çalıştır.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class StickyLayerHeaderClickTests
{
    private sealed record Proj(string Name);

    // StickyOverlayTests/StickyScrollTriggerTests ile AYNI topoloji: 3 katman (3+5+6 satır) → 3×24 + 14×36 = 576px.
    private static IReadOnlyList<StickyLayerList.LayerGroup> SampleGroups() =>
    [
        new("L0", [new Proj("a"), new Proj("b"), new Proj("c")]),
        new("L1", [new Proj("d"), new Proj("e"), new Proj("f"), new Proj("g"), new Proj("h")]),
        new("L2", [new Proj("i"), new Proj("j"), new Proj("k"), new Proj("l"), new Proj("m"), new Proj("n")]),
    ];

    /// <summary>
    /// [400px viewport] İçeriğin (576px) TAMAMINDAN küçük — GERÇEKTEN kaydırılabilir kalır (ScrollableHeight
    /// 176) ama L0/L1/L2 başlıklarının hepsi (contentTop 0/132/336) sıfır scroll'da yine de in-flow realize
    /// olur (sanallaştırma penceresi 0..400'ü kapsar) — bu dosyanın çoğu testi ayrıca scroll/pump UĞRAŞMADAN
    /// başlık container'ına erişir. Üç başlığın AYNI ANDA yapışık olduğu (τ2=288) senaryolar (overlay testleri)
    /// KENDİ ÇAĞRILARINDA daha küçük bir height ister (288'e kadar kaydırma payı için).
    ///
    /// <para>[D3/T5] Reveal'in kendi 0'a-scroll'u (Dispatcher.Loaded'da ertelenir) tüketilir — aksi halde
    /// testlerin kendi ScrollToVerticalOffset çağrıları sonradan sıfırlanır (StickyScrollTriggerTests ile AYNI
    /// ders).</para>
    /// </summary>
    private static StickyLayerList RealizeThenFeed(out Window window, double height = 400)
    {
        var list = new StickyLayerList { AnimationsEnabledProvider = () => false };
        var host = DsResources.NewHost();
        window = DsResources.Realize(host, list, width: 400, height: height);
        list.SetGroups(SampleGroups());
        list.UpdateLayout();
        DispatcherPump.PumpUntil(() => list.RevealGeneration > 0, TimeSpan.FromSeconds(3));
        return list;
    }

    private static Border InFlowHeaderBorder(StickyLayerList list, string name) =>
        DsResources.RealizedObjects(list.RowFlow)
            .OfType<Border>()
            .Single(b => b.DataContext is StickyLayerList.HeaderEntry h && h.Name == name);

    private static Border OverlayHeaderBorder(StickyLayerList list, string name) =>
        DsResources.RealizedObjects(list.Overlay)
            .OfType<Border>()
            .Single(b => b.DataContext is StuckHeader h && h.Name == name);

    /// <summary>Basar + bırakır (aynı elementte — <see cref="Press_and_release_must_both_land_on_the_header"/>
    /// M-5 kilidinin gerektirdiği sırayla) ve ArdIndan bir layout turu zorlar — <c>ScrollViewer.
    /// ScrollToVerticalOffset</c>/<c>ScrollAnimator</c>'ın (reduced-motion'da anında) yazdığı hedef,
    /// <c>VerticalOffset</c>'e ancak bir layout pass'ından SONRA yansır (StickyScrollTriggerTests'in
    /// <c>DispatcherPump.PumpUntil</c> deseniyle AYNI ders — burada animasyon yok, tek bir <c>UpdateLayout</c>
    /// yeter).</summary>
    private static void Click(StickyLayerList list, Border header)
    {
        Press(header);
        Release(list, header);
        list.UpdateLayout();
    }

    private static void Press(Border header) =>
        header.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        { RoutedEvent = UIElement.MouseLeftButtonDownEvent });

    /// <summary>[Final review M-2] Bırakma, üretimin <c>MouseLeftButtonUp</c> kablosunun çağırdığı
    /// <see cref="StickyLayerList.ReleaseHeader"/>'ın ta kendisiyle sürülür — gerçek <c>e.GetPosition</c> işletim
    /// sistemi imlecinin GERÇEK konumunu sorar ve headless'ta (ekran dışı pencere) hep başlığın DIŞINDA kalırdı
    /// (ConsoleView.UpdateHoverBand ile AYNI desen). Varsayılan konum başlığın ortasıdır.</summary>
    private static void Release(StickyLayerList list, Border header, Point? positionInHeader = null) =>
        list.ReleaseHeader(header, positionInHeader ?? new Point(header.ActualWidth / 2, header.ActualHeight / 2));

    private static void ScrollTo(StickyLayerList list, double offset)
    {
        list.Scroll.ScrollToVerticalOffset(offset);
        list.UpdateLayout();
    }

    // ---------------------------------------------------------------- stil: dinlenme/hover fırçaları + imleç

    [StaFact]
    public void The_header_style_rests_at_surface_border_subtle_text_faint_and_uses_a_hand_cursor()
    {
        // [Ds.LayerHeader/.Text] StickyLayerList'in KENDİ UserControl.Resources'ında tanımlıdır — global
        // merge zincirinde (DsResources.NewHost) DEĞİL; bu yüzden instance'ın kendi FindResource'u okunur
        // (InitializeComponent Resources'ı ctor'da senkron kurar, realize/host GEREKMEZ).
        var style = (Style)new StickyLayerList().FindResource("Ds.LayerHeader");

        var cursor = Assert.Single(style.Setters.OfType<Setter>(), s => s.Property == Border.CursorProperty);
        Assert.Equal(Cursors.Hand, cursor.Value);

        var bg = Assert.Single(style.Setters.OfType<Setter>(), s => s.Property == DsTransition.AnimatedBackgroundProperty);
        var border = Assert.Single(style.Setters.OfType<Setter>(), s => s.Property == DsTransition.AnimatedBorderBrushProperty);
        Assert.Equal("Brush.Surface", ((System.Windows.DynamicResourceExtension)bg.Value).ResourceKey);
        Assert.Equal("Brush.BorderSubtle", ((System.Windows.DynamicResourceExtension)border.Value).ResourceKey);
    }

    [StaFact]
    public void The_header_style_hovers_to_surface_raised_border_and_text_secondary()
    {
        var host = new StickyLayerList();
        var headerStyle = (Style)host.FindResource("Ds.LayerHeader");

        var hover = Assert.Single(headerStyle.Triggers.OfType<Trigger>(), t => t.Property == UIElement.IsMouseOverProperty && Equals(t.Value, true));
        var bg = Assert.Single(hover.Setters.OfType<Setter>(), s => s.Property == DsTransition.AnimatedBackgroundProperty);
        var border = Assert.Single(hover.Setters.OfType<Setter>(), s => s.Property == DsTransition.AnimatedBorderBrushProperty);
        Assert.Equal("Brush.SurfaceRaised", ((System.Windows.DynamicResourceExtension)bg.Value).ResourceKey);
        Assert.Equal("Brush.Border", ((System.Windows.DynamicResourceExtension)border.Value).ResourceKey);

        var textStyle = (Style)host.FindResource("Ds.LayerHeader.Text");
        var restFg = Assert.Single(textStyle.Setters.OfType<Setter>(), s => s.Property == DsTransition.AnimatedForegroundProperty);
        Assert.Equal("Brush.TextFaint", ((System.Windows.DynamicResourceExtension)restFg.Value).ResourceKey);

        var hoverTrigger = Assert.Single(textStyle.Triggers.OfType<DataTrigger>());
        // [XAML kuralı] DataTrigger.Value tipi Binding kaynağının statik tipini BİLMEZ; WPF metin "True"yu
        // burada boolean DEĞİL string olarak saklar (Trigger.Property=IsMouseOverProperty'nin aksine — orada
        // hedef DP'nin tipi ÖNCEDEN bilinir). Gerçek WPF karşılaştırması (DataTrigger tetiklenirken) türü ZATEN
        // ondan bağımsız çözer; burada yalnız "true" niyetini metinsel doğrularız.
        Assert.Equal("True", hoverTrigger.Value?.ToString(), ignoreCase: true);
        var hoverFg = Assert.Single(hoverTrigger.Setters.OfType<Setter>(), s => s.Property == DsTransition.AnimatedForegroundProperty);
        Assert.Equal("Brush.TextSecondary", ((System.Windows.DynamicResourceExtension)hoverFg.Value).ResourceKey);
    }

    /// <summary>Realize edilmiş gerçek başlıklar (in-flow VE overlay) bu iki stili GERÇEKTEN taşır — stil doğru
    /// tanımlanmış olsa da XAML'de yanlış anahtara bağlansa yukarıdaki iki test hâlâ yeşil kalırdı (ActionBarTests
    /// "kablolamayı doğrular" deseni).</summary>
    [StaFact]
    public void Realized_headers_in_flow_and_overlay_actually_carry_the_header_style()
    {
        var list = RealizeThenFeed(out var window, height: 200); // 288'e kadar kaydırma payı (bkz. RealizeThenFeed notu)

        var header = InFlowHeaderBorder(list, "L0");
        Assert.Same(list.FindResource("Ds.LayerHeader"), header.Style);

        ScrollTo(list, 288);
        DispatcherPump.PumpUntil(() => ((IReadOnlyList<StuckHeader>)list.Overlay.ItemsSource).Count == 3, TimeSpan.FromSeconds(3));
        list.UpdateLayout();
        var overlayHeader = OverlayHeaderBorder(list, "L0");
        Assert.Same(list.FindResource("Ds.LayerHeader"), overlayHeader.Style);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- tooltip: tek kaynak InteractionText.JumpToLayer

    [StaFact]
    public void The_header_tooltip_says_jump_to_the_layer_name_via_the_single_source_helper()
    {
        var list = RealizeThenFeed(out var window);

        var header = InFlowHeaderBorder(list, "L1");
        Assert.Equal(InteractionText.JumpToLayer("L1"), header.ToolTip);
        GC.KeepAlive(window);
    }

    /// <summary>[Final review M-3] Tasarım başlığa native <c>title</c> ister; uygulamanın kuralı native-title
    /// tooltip'lerine işletim sisteminin bekleme süresini verir (<see cref="AppTooltipDefaults.NativeDelayMs"/>) —
    /// gecikmesiz uygulama varsayılanı DEĞİL. Hem in-flow hem yapışık overlay kopyası (AYNI şablon) taşır.</summary>
    [StaFact]
    public void The_header_tooltip_opens_with_the_native_delay_not_the_app_wide_zero_delay()
    {
        AppTooltipDefaults.Apply(); // uygulama geneli gecikmesiz varsayılan — aksi halde WPF varsayılanı ölçülürdü
        var list = RealizeThenFeed(out var window, height: 200);
        Assert.NotEqual(0, AppTooltipDefaults.NativeDelayMs); // test kurgusu: iki değer ayırt edilebilir

        Assert.Equal(AppTooltipDefaults.NativeDelayMs, ToolTipService.GetInitialShowDelay(InFlowHeaderBorder(list, "L0")));

        ScrollTo(list, 288);
        DispatcherPump.PumpUntil(() => ((IReadOnlyList<StuckHeader>)list.Overlay.ItemsSource).Count == 3, TimeSpan.FromSeconds(3));
        list.UpdateLayout();
        Assert.Equal(AppTooltipDefaults.NativeDelayMs, ToolTipService.GetInitialShowDelay(OverlayHeaderBorder(list, "L0")));
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- tıklama: doğru scroll hedefi

    [StaFact]
    public void Clicking_the_first_groups_inflow_header_clamps_the_scroll_to_zero()
    {
        var list = RealizeThenFeed(out var window);
        ScrollTo(list, 50); // sıfırdan uzakta başla ki jump GERÇEKTEN bir şey yapsın.
        Assert.Equal(50, list.Scroll.VerticalOffset, precision: 3);

        Click(list, InFlowHeaderBorder(list, "L0"));

        Assert.Equal(list.Metrics!.JumpTargetForHeader(0), list.Scroll.VerticalOffset, precision: 3);
        Assert.Equal(0, list.Scroll.VerticalOffset, precision: 3);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Clicking_a_middle_groups_inflow_header_lands_the_first_row_just_below_the_stacked_headers()
    {
        var list = RealizeThenFeed(out var window);

        Click(list, InFlowHeaderBorder(list, "L1"));

        Assert.Equal(list.Metrics!.JumpTargetForHeader(1), list.Scroll.VerticalOffset, precision: 3);
        Assert.Equal(108, list.Scroll.VerticalOffset, precision: 3);
        GC.KeepAlive(window);
    }

    /// <summary>Yapışık overlay başlığı da AYNI tıklamayı verir — kullanıcı çoğunlukla ona tıklar (brief notu).</summary>
    [StaFact]
    public void Clicking_the_stuck_overlay_header_scrolls_to_the_same_target_as_the_inflow_one()
    {
        var list = RealizeThenFeed(out var window, height: 200); // 288'e kadar kaydırma payı (bkz. RealizeThenFeed notu)
        ScrollTo(list, 288); // üç başlık da yapışık.
        DispatcherPump.PumpUntil(() => ((IReadOnlyList<StuckHeader>)list.Overlay.ItemsSource).Count == 3, TimeSpan.FromSeconds(3));
        list.UpdateLayout();

        Click(list, OverlayHeaderBorder(list, "L1"));

        Assert.Equal(list.Metrics!.JumpTargetForHeader(1), list.Scroll.VerticalOffset, precision: 3);
        GC.KeepAlive(window);
    }

    /// <summary>Overlay artık hit-test'e AÇIK — regresyon kilidi (eskiden IsHitTestVisible=False idi).</summary>
    [StaFact]
    public void The_overlay_is_hit_test_visible_so_its_headers_are_reachable_by_the_mouse()
    {
        var list = new StickyLayerList();
        Assert.True(list.Overlay.IsHitTestVisible);
    }

    // ---------------------------------------------------------------- [review round 1 · M-5] basış + bırakış AYNI başlıkta olmalı

    /// <summary>
    /// [review round 1 · M-5] Ölçülen kusur: <c>MouseLeftButtonUp</c> yalnız BIRAKMA noktasındaki elementi bilir
    /// — bir satıra basıp (satır kendi tıklamasını/sürüklemesini başlatır) imleci bir başlığın üstüne getirip
    /// BIRAKMAK, önceki kodda hiçbir kontrol olmadığı için jump'ı TETİKLİYORDU. Fix: başlık kendi
    /// <c>MouseLeftButtonDown</c>'ında mouse'u yakalar (<see cref="UIElement.CaptureMouse"/>); bu test basışı
    /// HİÇ yapmadan (== "basış BAŞKA bir elementte başladı") doğrudan bir <c>MouseLeftButtonUp</c> yükseltir —
    /// tam olarak "satırdan sürüklenip başlıkta bırakılan" olayın header'ın kendi görüş açısından ayırt
    /// edilemez hâli (o da yakalamayı hiç TUTMUYOR olurdu).
    /// </summary>
    [StaFact]
    public void A_release_without_a_matching_press_on_the_same_header_does_not_jump()
    {
        var list = RealizeThenFeed(out var window);
        double before = list.Scroll.VerticalOffset;

        Release(list, InFlowHeaderBorder(list, "L1")); // basış YOK — sürükleyip bırakma senaryosunun header-tarafı eşi
        list.UpdateLayout();

        Assert.Equal(before, list.Scroll.VerticalOffset, precision: 3);
        GC.KeepAlive(window);
    }

    /// <summary>[Final review M-2] Başlığa basıp imleci DIŞARI sürükleyip bırakmak jump YAPMAZ — yakalama
    /// bırakmayı yine başlığa yönlendirir (<c>IsMouseCaptured</c> true), bu yüzden bırakma konumunun başlığın
    /// sınırları içinde olması ayrıca şarttır (native bir tıklamanın iptal jesti).</summary>
    [StaFact]
    public void Press_on_a_header_then_release_outside_its_bounds_does_not_jump()
    {
        var list = RealizeThenFeed(out var window);
        var header = InFlowHeaderBorder(list, "L1");
        double before = list.Scroll.VerticalOffset;

        Press(header);
        Assert.True(header.IsMouseCaptured, "test kurgusu: basış yakalamayı almadı");
        Release(list, header, new Point(header.ActualWidth / 2, header.ActualHeight + 40)); // başlığın altında
        list.UpdateLayout();

        Assert.False(header.IsMouseCaptured); // yakalama yine bırakıldı
        Assert.Equal(before, list.Scroll.VerticalOffset, precision: 3);
        GC.KeepAlive(window);
    }

    /// <summary>[Final review M-2] Basış ile bırakma arasında container geri dönüştürülüp (Recycling) başka bir
    /// katmanın başlığına bağlanırsa, yakalama AYNI Border'da kalır ama DataContext değişmiştir — bırakma, basılan
    /// başlığın slotuyla AYNI slotta değilse jump yapmaz.</summary>
    [StaFact]
    public void A_release_whose_container_was_recycled_to_another_slot_since_the_press_does_not_jump()
    {
        var list = RealizeThenFeed(out var window);
        var header = InFlowHeaderBorder(list, "L1");
        double before = list.Scroll.VerticalOffset;

        Press(header);
        header.DataContext = new StickyLayerList.HeaderEntry("L2", 6, SlotIndex: 2); // geri dönüşüm taklidi
        Release(list, header);
        list.UpdateLayout();

        Assert.Equal(before, list.Scroll.VerticalOffset, precision: 3);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- seçim/filtre DEĞİŞMEZ

    [StaFact]
    public void Clicking_a_header_does_not_touch_selection_or_follow_state()
    {
        var list = RealizeThenFeed(out var window);
        bool followingBefore = list.FollowController!.IsFollowing;

        Click(list, InFlowHeaderBorder(list, "L2"));

        Assert.Equal(followingBefore, list.FollowController!.IsFollowing);
        // Gruplar/Metrics AYNI kalır — SetGroups tekrar ÇAĞRILMADI (yalnız scroll).
        Assert.Equal(14, list.Metrics!.RowCount);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [review round 1 · M-6, fixed round 2 · finding 1] Yukarıdaki test yalnız <see cref="StickyLayerList"/>'in
    /// KENDİ yüzeyini (follow/Metrics) doğruluyordu — gerçek "seçim" (<see cref="RunViewModel.SelectedProjectId"/>)
    /// ve "aktif filtre" (<see cref="RunViewModel.ActiveFilters"/>) VM'de yaşar, listenin bilmediği bir katman.
    ///
    /// <para><b>[round 2 · finding 1 — ÖLÇÜLMÜŞ boşluk]</b> İlk round-1 sürümü <see cref="MainWindowHost.NewWithProjects"/>
    /// kullanıyordu — ama o yalnız <c>Measure</c>/<c>Arrange</c> yapar (bkz. <see cref="MainWindowHost.Realize"/>'ın
    /// KENDİ XML doc'u), GERÇEK bir <see cref="System.Windows.PresentationSource"/> (HWND) KURMAZ. M-5'in
    /// <c>CaptureMouse()</c>'u bir HWND olmadan SESSİZCE BAŞARISIZ olur → <c>IsMouseCaptured</c> hep <c>false</c>
    /// kalır → <c>HeaderRoot_MouseLeftButtonUp</c> jump'a hiç ULAŞMADAN erken döner. Test bu yüzden VACUOUS'tu:
    /// tıklama koduna dokunmasa da (jump path'i tamamen SİLİNSE de) yeşil kalırdı. <see cref="MainWindow"/>'u
    /// GERÇEKTEN <c>Show()</c> etmek ÇÖZÜM DEĞİL — <see cref="MainWindowHost"/>'un KENDİ değişmezi ("motor ASLA
    /// doğmaz... pencere hiç Show() edilmez") tam da bunu yasaklıyor (gerçek engine/tray/persist yan etkileri).
    /// Fix: <see cref="MainWindow"/> HİÇ kurulmaz; bunun yerine GERÇEK bir <see cref="RunViewModel"/> (üretimdeki
    /// <see cref="RunViewModel.BuildLayerGroups"/> ile) + bağımsız, GERÇEKTEN realize edilmiş (<see cref="DsResources.Realize"/>
    /// → <c>AnimationHost.ShowOffscreen</c> → gerçek HWND) bir <see cref="StickyLayerList"/> kurulur — bu dosyanın
    /// diğer testleriyle AYNI realize deseni, yalnız veri kaynağı artık gerçek VM'in <c>BuildLayerGroups()</c>'ı.</para>
    ///
    /// <para><b>Non-vacuous kanıt (round 2'nin istediği):</b> tek bir katman/satır görünürken "ilk başlık zaten
    /// 0'a kelepçeli" olduğundan offset'in 0'da KALMASI hem "jump çalıştı" hem "jump hiç çalışmadı" ile aynı
    /// görünürdü — bu yüzden HER İKİ katman da (Core + Ui) filtre sonrası görünür bırakılır, ikinci (Ui, slot 1)
    /// başlığa tıklanır ve scroll'un sıfırdan UZAK bir başlangıçtan TAM OLARAK <c>JumpTargetForHeader(1)</c>'e
    /// (36 — ne başlangıç ne "hiçbir şey olmadı" değeriyle çakışan bir sayı) gittiği doğrudan doğrulanır — bu,
    /// tıklamanın GERÇEKTEN jump path'ine ulaştığının (dolayısıyla capture'ın bu kurulumda GERÇEKTEN çalıştığının)
    /// kanıtıdır. Elle doğrulandı: <c>DsResources.Realize</c> çağrısı (gerçek HWND) geçici olarak kaldırılınca bu
    /// test KIRMIZI verir (round-1'in vacuous halini yeniden üretir) — bkz. task-5-report.md round 2.</para>
    ///
    /// <para><c>ToggleFilter</c> seçimi KENDİSİ temizlediğinden (üretim kuralı: filtreye basmak seçimi düşürür)
    /// sıra önemlidir: önce filtre, SONRA seçim, SONRA tıklama.</para>
    /// </summary>
    [StaFact]
    public void Clicking_a_header_does_not_touch_the_view_models_selection_or_active_filter()
    {
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), MainWindowHost.NeverTickingBatcher(), () => "r1")
        { RootPath = @"C:\src\OSYS" };
        ProjectNode[] nodes = [MainWindowHost.Node("Alpha", 0, "Core"), MainWindowHost.Node("Beta", 1, "Core"), MainWindowHost.Node("Gamma", 2, "Ui")];
        vm.OnEvent(new WorkspaceTopologyEvent(nodes, [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, nodes.Length, 0)); // → Idle

        // İKİ katman da filtre sonrası görünür kalsın (bkz. non-vacuous kanıt notu yukarıda).
        vm.OnEvent(new ProjectFailedEvent("r1", MainWindowHost.IdOf("Beta"), 10, "boom"));
        vm.OnEvent(new ProjectFailedEvent("r1", MainWindowHost.IdOf("Gamma"), 10, "boom"));
        vm.ToggleFilter(ProjectFilter.Failed); // → Beta (Core) + Gamma (Ui) görünür; seçim bu adımda temizlenir.
        vm.SelectProject(MainWindowHost.IdOf("Beta"));
        Assert.Equal(MainWindowHost.IdOf("Beta"), vm.SelectedProjectId); // ön-koşul
        Assert.Equal(new[] { ProjectFilter.Failed }, vm.ActiveFilters.ToArray()); // ön-koşul

        // MainWindow.ApplyProjectGroups'un yaptığı AYNI tek satırlık çeviri — MainWindow'un KENDİSİ KURULMAZ.
        // İçerik (2 başlık×24 + 2 satır×36 = 120px) viewport'tan (50px) BÜYÜK — GERÇEKTEN kaydırılabilir
        // (ScrollableHeight 70) olsun diye küçük bir yükseklik (bkz. non-vacuous kanıt notu yukarıda: viewport
        // içeriğe eşit/büyük olsaydı ScrollableHeight 0 olur, HER scroll — jump dahil — sessizce 0'a KENETLENİR
        // ve "jump çalıştı" ile "jump hiç çalışmadı" AYNI sonucu (0) verirdi).
        var list = new StickyLayerList { AnimationsEnabledProvider = () => false };
        var window = DsResources.Realize(DsResources.NewHost(), list, height: 50); // GERÇEK HWND — CaptureMouse burada ÇALIŞIR.
        var groups = vm.BuildLayerGroups()
            .Select(g => new StickyLayerList.LayerGroup(g.Name ?? "", g.Rows.Cast<object>().ToList()))
            .ToList();
        list.SetGroups(groups);
        list.UpdateLayout();
        DispatcherPump.PumpUntil(() => list.RevealGeneration > 0, TimeSpan.FromSeconds(3));

        ScrollTo(list, 9999); // en dibe kelepçelenir (ScrollableHeight'a) — "Ui" başlığı bu pencerede realize olur.
        double before = list.Scroll.VerticalOffset;
        Assert.True(before > 40, $"ön-koşul: liste GERÇEKTEN kaydırılabilir değil (ScrollableHeight={list.Scroll.ScrollableHeight})");
        Click(list, InFlowHeaderBorder(list, "Ui"));

        // Non-vacuous kanıt: offset TAM OLARAK beklenen (dipten FARKLI) jump hedefine gitti — "hiçbir şey
        // olmadı" (offset `before`'da kalırdı) ile "jump çalıştı" burada AYIRT EDİLEBİLİR.
        Assert.Equal(list.Metrics!.JumpTargetForHeader(1), list.Scroll.VerticalOffset, precision: 3);
        Assert.Equal(36, list.Scroll.VerticalOffset, precision: 3);
        Assert.NotEqual(before, list.Scroll.VerticalOffset, 3);

        Assert.Equal(MainWindowHost.IdOf("Beta"), vm.SelectedProjectId);
        Assert.Equal(new[] { ProjectFilter.Failed }, vm.ActiveFilters.ToArray());
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- tekerlek: overlay üstündeyken bile Scroll'a ulaşır

    [StaFact]
    public void Mouse_wheel_over_the_stacked_overlay_headers_still_scrolls_the_list()
    {
        var list = RealizeThenFeed(out var window, height: 200); // sanallaştırılmış/gerçekten kaydırılabilir viewport
        DispatcherPump.PumpUntil(() => list.Scroll.ScrollableHeight > 0, TimeSpan.FromSeconds(3));
        ScrollTo(list, 288);
        DispatcherPump.PumpUntil(() => ((IReadOnlyList<StuckHeader>)list.Overlay.ItemsSource).Count == 3, TimeSpan.FromSeconds(3));
        list.UpdateLayout();
        double before = list.Scroll.VerticalOffset;

        // Overlay'in ÜRETİMDEKİ kablosu PreviewMouseWheel'e (tunnel) bağlıdır (ForwardWheelToScroll) — gerçek
        // girdi bunu OS'ten otomatik ateşler, testte AYNI (tunnel) olayı doğrudan yükseltmek gerekir; bubbling
        // MouseWheelEvent'i raise etmek PreviewMouseWheel handler'ını TETİKLEMEZ (ayrı bir RoutedEvent'tir).
        // Pozitif delta → WPF varsayılanı yukarı kaydırır, VerticalOffset AZALIR.
        list.Overlay.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120)
        { RoutedEvent = UIElement.PreviewMouseWheelEvent });
        list.UpdateLayout();

        Assert.True(list.Scroll.VerticalOffset < before,
            $"tekerlek overlay üstünde Scroll'a ULAŞMADI (önce={before}, sonra={list.Scroll.VerticalOffset})");
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [review round 1 · I-1] Ölçülen kusur: overlay hit-test'e açılmadan (bu task'ın KENDİSİ) ÖNCE yığılmış
    /// başlık bandı üstünde tekerlek çevirmek hep Scroll'a ULAŞIYORDU (overlay <c>IsHitTestVisible=False</c>
    /// olduğundan hit-test onu atlıyordu) — bu yüzden <c>Scroll.PreviewMouseWheel</c>'e bağlı üç bookkeeping
    /// (<c>ScrollAnimator.EnableUserCancellation</c>, <c>_lastUserScrollAtMs</c> damgası, <c>Arbiter.
    /// NotifyUserScroll</c>) dolaylı yoldan çalışıyordu. Overlay hit-test'e açılınca (bu task)
    /// <c>ForwardWheelToScroll</c> yalnız asıl KAYDIRMAYI yeniden yükseltiyordu — bu ÜÇÜNÜ ATLAYARAK: koşan bir
    /// build'de yığılmış başlık üstünde tekerlek çevirmek follow-mode'u DURAKLATMIYOR ve merkezi arbiter'a HİÇ
    /// haber VERMİYORDU. Bu test <see cref="StickyLayerList.IsFollowSuppressedByUser"/> VE
    /// <see cref="ScrollArbiter.IsSuppressed"/>(Frontier) ikisini BİRDEN doğrular.
    /// </summary>
    [StaFact]
    public void Wheel_over_the_stacked_overlay_headers_suppresses_follow_and_notifies_the_arbiter()
    {
        var list = RealizeThenFeed(out var window, height: 200);
        var arbiter = new ScrollArbiter();
        list.Arbiter = arbiter;
        DispatcherPump.PumpUntil(() => list.Scroll.ScrollableHeight > 0, TimeSpan.FromSeconds(3));
        ScrollTo(list, 288);
        DispatcherPump.PumpUntil(() => ((IReadOnlyList<StuckHeader>)list.Overlay.ItemsSource).Count == 3, TimeSpan.FromSeconds(3));
        list.UpdateLayout();
        Assert.False(list.IsFollowSuppressedByUser); // ön-koşul
        Assert.False(arbiter.IsSuppressed(ScrollPanel.Frontier)); // ön-koşul

        list.Overlay.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120)
        { RoutedEvent = UIElement.PreviewMouseWheelEvent });

        Assert.True(list.IsFollowSuppressedByUser, "overlay tekerleği follow-mode'u DURAKLATMADI");
        Assert.True(arbiter.IsSuppressed(ScrollPanel.Frontier), "overlay tekerleği arbiter'a HABER VERMEDİ");
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [review round 1 · I-1] Aynı ölçülen kusurun ikinci yarısı: overlay tekerleği uçuştaki bir smooth scroll'u
    /// (bir başlığa tıklamanın başlattığı animasyon) hiç İPTAL ETMİYORDU — Scroll'un kendi
    /// <c>PreviewMouseWheel</c>'ine bağlı <see cref="ScrollAnimator.CancelForUser"/> yalnız Scroll'un KENDİ
    /// tekerleğinden tetikleniyordu. Gerçek bir uçuştaki animasyon gerektiğinden (reduced-motion'da anında biter,
    /// iptal edilecek bir şey KALMAZ) bu test <see cref="MotionScope"/> ile GERÇEK motion'ı açar — bu dosyanın
    /// tek "gerçek animasyon" testi (diğerleri headless varsayılanına — animasyon YOK — güvenir).
    /// </summary>
    [StaFact]
    public void Wheel_over_the_stacked_overlay_headers_cancels_an_in_flight_smooth_scroll()
    {
        using var _ = MotionScope.Enable(new MotionSettings(new FakeMotionSignal { AnimationsEnabled = true }));
        var list = RealizeThenFeed(out var window); // varsayılan 400px: L1 sıfır scroll'da yine de in-flow realize olur.
        DispatcherPump.PumpUntil(() => list.Scroll.ScrollableHeight > 0, TimeSpan.FromSeconds(3));

        // Bir başlığa tıklamak GERÇEK (motion açık) bir animasyon başlatır — hedef (L1 → 108) mevcut konumdan (0) uzak.
        Click(list, InFlowHeaderBorder(list, "L1"));
        Assert.True(list.Scroll.HasAnimatedProperties, "ön-koşul: tıklama GERÇEKTEN uçuşan bir animasyon başlatmadı");

        list.Overlay.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120)
        { RoutedEvent = UIElement.PreviewMouseWheelEvent });

        Assert.False(list.Scroll.HasAnimatedProperties, "overlay tekerleği uçuştaki animasyonu İPTAL ETMEDİ");
        Assert.True(list.IsFollowSuppressedByUser);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Katman başlığına tıklamak da bir KULLANICI KAYDIRMASIDIR: liste kullanıcının istediği yere gider ve
    /// frontier takibi — tekerlek/çubuk/tuşta olduğu gibi — duraklar.
    ///
    /// <para><b>Ölçülen kusur:</b> jump <c>ScrollAnimator.AnimateTo</c>'ya iniyor, o da "yeni programatik
    /// hareket, kullanıcı iptali artık geçersiz" diyerek suppress bayrağını TEMİZLİYORDU. Sonuç: koşarken bir
    /// katmana atlamak takibi serbest bırakıyor, bir sonraki 200 ms'lik tick kullanıcıyı derlenen satıra geri
    /// çekiyordu. Takibi duraklatmak jump'ın KENDİ hareketini iptal etmez — ikisi ayrı şeydir.</para>
    /// </summary>
    [StaFact]
    public void Clicking_a_layer_header_pauses_follow_like_any_other_user_scroll()
    {
        var list = RealizeThenFeed(out var window);
        var arbiter = new ScrollArbiter();
        list.Arbiter = arbiter;
        DispatcherPump.PumpUntil(() => list.Scroll.ScrollableHeight > 0, TimeSpan.FromSeconds(3));
        Assert.False(list.IsFollowSuppressedByUser);              // ön-koşul
        Assert.False(arbiter.IsSuppressed(ScrollPanel.Frontier)); // ön-koşul

        Click(list, InFlowHeaderBorder(list, "L1"));

        Assert.True(list.Scroll.VerticalOffset > 0, "ön-koşul: jump listeyi HİÇ kaydırmadı");
        Assert.True(list.IsFollowSuppressedByUser, "katman başlığına tıklamak follow'u DURAKLATMADI");
        Assert.True(arbiter.IsSuppressed(ScrollPanel.Frontier), "katman başlığı jump'ı arbiter'a HABER VERMEDİ");
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- klavye modeli KIRILMAZ (mouse-only karar)

    /// <summary>
    /// [ruling: Enter/Space YALNIZ mevcut ok-tuşu/tab modeli bozulmadan eklenebiliyorsa] Başlık kökü BİLEREK
    /// bir <see cref="Border"/> kalır (Button/Control DEĞİL) — Border varsayılan olarak <c>Focusable=false</c>'dır,
    /// yani Tab sırasına ve <c>DirectionalNavigation=Contained</c> altındaki ok-tuşu gezinmesine HİÇ GİRMEZ.
    /// Mouse-only karar burada budur: başlık tıklanabilir ama odaklanamaz, satırlar arası ok-tuşu gezinmesi
    /// (<see cref="AccessibilityTests.The_project_list_lets_arrow_keys_move_between_rows"/>) DEĞİŞMEDEN kalır.
    /// </summary>
    [StaFact]
    public void Layer_headers_remain_non_focusable_so_arrow_key_row_navigation_is_unaffected()
    {
        var list = RealizeThenFeed(out var window);

        var header = InFlowHeaderBorder(list, "L1");
        Assert.False(header.Focusable);

        Assert.Equal(KeyboardNavigationMode.Contained, KeyboardNavigation.GetDirectionalNavigation(list.RowFlow));
        GC.KeepAlive(window);
    }
}
