using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T58] StickyLayerList: liste ScrollViewer üstünde overlay ItemsControl. Overlay, LayoutMetrics'ten gelen
/// YAPIŞIK başlıkları (StuckHeader) doğru Y'de (i×24), in-flow başlıkla AYNI DataTemplate + opak zeminle
/// çizer (geçiş görünmez). Virtualization KAPALI (feasibility §4.1). Katman yoksa overlay boş.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class StickyOverlayTests
{
    private sealed record Proj(string Name);

    private static IReadOnlyList<StickyLayerList.LayerGroup> SampleGroups() =>
    [
        new("L0", [new Proj("a"), new Proj("b"), new Proj("c")]),                 // 3 satır
        new("L1", [new Proj("d"), new Proj("e"), new Proj("f"), new Proj("g"), new Proj("h")]), // 5
        new("L2", [new Proj("i"), new Proj("j"), new Proj("k"), new Proj("l"), new Proj("m"), new Proj("n")]), // 6
    ];

    [StaFact]
    public void List_disables_virtualization_and_scrolls_by_pixel()
    {
        var list = new StickyLayerList();
        list.SetGroups(SampleGroups());

        // feasibility §4.1: aritmetik tablo yalnız virtualization KAPALIYKEN birebir doğru.
        Assert.False(VirtualizingPanel.GetIsVirtualizing(list.Flow));
        // production XAML'daki VirtualizingPanel.ScrollUnit="Pixel" ayarını pinler (el ile kurulmuş bir
        // ScrollViewer'ın varsayılanı DEĞİL — list.Scroll.CanContentScroll==false her zaman trivially geçerdi).
        Assert.Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(list.Flow));
    }

    [StaFact]
    public void Metrics_is_built_from_groups_and_is_the_shared_instance()
    {
        var list = new StickyLayerList();
        list.SetGroups(SampleGroups());

        // T59 follow-mode AYNI instance'ı tüketir.
        Assert.NotNull(list.Metrics);
        Assert.Equal(3, list.Metrics!.Headers.Count);
        Assert.Equal(14, list.Metrics.RowCount);
        Assert.Equal(576, list.Metrics.TotalHeight);
    }

    [StaFact]
    public void Overlay_shows_the_stuck_headers_for_the_current_offset_at_i_times_24()
    {
        var list = new StickyLayerList();
        list.SetGroups(SampleGroups());

        // Başlangıç (offset 0): yalnız header 0 yapışık.
        var atTop = (IReadOnlyList<StuckHeader>)list.Overlay.ItemsSource;
        Assert.Single(atTop);
        Assert.Equal("L0", atTop[0].Name);
        Assert.Equal(0, atTop[0].PinnedY);

        // Aşağı kaydır (offset 288): üç başlık da 0/24/48'de yapışık.
        list.UpdateOverlay(288);
        var deep = (IReadOnlyList<StuckHeader>)list.Overlay.ItemsSource;
        Assert.Equal(new[] { "L0", "L1", "L2" }, deep.Select(h => h.Name).ToArray());
        Assert.Equal(new[] { 0.0, 24.0, 48.0 }, deep.Select(h => h.PinnedY).ToArray());
    }

    [StaFact]
    public void Overlay_and_inflow_headers_use_the_exact_same_DataTemplate_instance()
    {
        var list = new StickyLayerList();

        // Geçişin görünmez olması için overlay ile in-flow başlık AYNI şablon nesnesini kullanır.
        Assert.Same(list.HeaderTemplate, list.Overlay.ItemTemplate);
        Assert.Same(list.HeaderTemplate, list.HeaderTemplateForFlow());
    }

    [StaFact]
    public void HeaderTemplate_and_RowTemplate_border_heights_are_bound_to_LayoutMetrics_constants()
    {
        // Regresyon kilidi: StickyLayerList.xaml Border Height'ları {x:Static LayoutMetrics.Default*Height}
        // ile bağlıdır (literal DEĞİL) — aksi halde biri diğerinden sürüklenir (drift) ve overlay Y'si kayar
        // (sticky jitter, bkz. LayoutMetrics.cs sabit yorumu). Bu test compile edilmiş şablonu yükleyip gerçek
        // Height'ı sabitlerle karşılaştırır; sabit değişip XAML güncellenmezse (veya tersi) burada kırılır.
        var list = new StickyLayerList();

        var headerRoot = Assert.IsType<Border>(list.HeaderTemplate.LoadContent());
        Assert.Equal(LayoutMetrics.DefaultHeaderHeight, headerRoot.Height);

        var rowRoot = Assert.IsType<Border>(list.RowTemplate.LoadContent());
        Assert.Equal(LayoutMetrics.DefaultRowHeight, rowRoot.Height);
    }

    [StaFact]
    public void Overlay_header_background_is_opaque_surface_so_it_hides_the_inflow_header()
    {
        // Şablonun kökündeki Border zemini Brush.Surface (opak, A=255) — altındaki in-flow başlığı örter.
        string tokensPath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "Resources", "Tokens.xaml");
        using var stream = File.OpenRead(tokensPath);
        var tokens = (ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream);

        var list = new StickyLayerList();
        var root = (FrameworkElement)list.HeaderTemplate.LoadContent();
        // DynamicResource'un çözülmesi için token'lı bir logical-tree bağlamına koy.
        var host = new Border { Resources = tokens, Child = root };
        host.Measure(new Size(400, 24));

        var border = Assert.IsType<Border>(root);
        var bg = Assert.IsType<SolidColorBrush>(border.Background);
        Assert.Equal(255, bg.Color.A); // opak
        Assert.Equal((Color)ColorConverter.ConvertFromString("#141417")!, bg.Color); // Brush.Surface
        GC.KeepAlive(host);
    }

    [StaFact]
    public void Default_no_layers_yields_an_empty_overlay_plain_list()
    {
        var list = new StickyLayerList();
        // Varsayılan: katman YOK → tek başlıksız grup.
        list.SetGroups([new StickyLayerList.LayerGroup("", [new Proj("x"), new Proj("y"), new Proj("z")])]);

        Assert.Empty(list.Metrics!.Headers);
        var overlay = (IReadOnlyList<StuckHeader>)list.Overlay.ItemsSource;
        Assert.Empty(overlay);
        list.UpdateOverlay(9999);
        Assert.Empty((IReadOnlyList<StuckHeader>)list.Overlay.ItemsSource);
    }
}
