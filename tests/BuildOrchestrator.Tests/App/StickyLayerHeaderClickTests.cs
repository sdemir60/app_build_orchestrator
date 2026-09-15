using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;

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

    /// <summary>Tıklar ve ArdIndan bir layout turu zorlar — <c>ScrollViewer.ScrollToVerticalOffset</c>/
    /// <c>ScrollAnimator</c>'ın (reduced-motion'da anında) yazdığı hedef, <c>VerticalOffset</c>'e ancak bir
    /// layout pass'ından SONRA yansır (StickyScrollTriggerTests'in <c>DispatcherPump.PumpUntil</c> deseniyle
    /// AYNI ders — burada animasyon yok, tek bir <c>UpdateLayout</c> yeter).</summary>
    private static void Click(StickyLayerList list, Border header)
    {
        header.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        list.UpdateLayout();
    }

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
