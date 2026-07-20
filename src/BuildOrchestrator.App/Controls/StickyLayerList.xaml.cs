using System.Windows;
using System.Windows.Controls;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T58] Birikimli yapışan katman başlıkları — liste ScrollViewer + overlay Canvas (feasibility §3.3).
/// Layout aritmetiği <see cref="LayoutMetrics"/>'te (SAF, testli, T59 follow-mode ile ORTAK instance);
/// bu control yalnız WPF kablajı: grupları in-flow entry akışına çevirir, ScrollChanged'de yapışık başlık
/// kümesini overlay'e sürer. Virtualization KAPALI (§4.1 — aritmetik tablo yalnız o zaman birebir).
/// </summary>
public partial class StickyLayerList : UserControl
{
    /// <summary>Sıralı bir katman: adı (boş → başlıksız, sticky devrede değil) + satır nesneleri (RowTemplate
    /// <c>{Binding Name}</c>'e bağlanır — <see cref="ViewModels.ProjectRowViewModel"/> / SampleGraphData.Node
    /// gibi bir <c>Name</c> taşıyan her nesne).</summary>
    public sealed record LayerGroup(string Name, IReadOnlyList<object> Rows);

    /// <summary>In-flow başlık entry'si — <see cref="LayoutMetrics.HeaderInfo"/>'nun WPF-binding karşılığı;
    /// overlay'in StuckHeader'ı ile AYNI <c>Name</c>/<c>RowCount</c> alanlarını taşır ki tek şablon ikisine de
    /// bağlansın.</summary>
    public sealed record HeaderEntry(string Name, int RowCount, int SlotIndex);

    private static readonly IReadOnlyList<StuckHeader> NoHeaders = [];

    /// <summary>[T59 ile ORTAK] Gruplardan kurulan kümülatif offset servisi — follow-mode/selection scroll
    /// hedefleri AYNI instance'tan üretilir. <see cref="SetGroups"/>'tan önce null.</summary>
    public LayoutMetrics? Metrics { get; private set; }

    public StickyLayerList()
    {
        InitializeComponent();
        Flow.ItemTemplateSelector = new EntrySelector(this);
        Overlay.ItemsSource = NoHeaders;
        // Salt aritmetik overlay recompute: kaydırmada yapışık küme değişir (ScrollUnit=Pixel → VerticalOffset px).
        Scroll.ScrollChanged += (_, _) => UpdateOverlay(Scroll.VerticalOffset);
    }

    /// <summary>In-flow ve overlay başlıklarının paylaştığı TEK DataTemplate (geçişin görünmezliği bunu gerektirir).</summary>
    public DataTemplate HeaderTemplate => (DataTemplate)Resources["HeaderTemplate"];
    public DataTemplate RowTemplate => (DataTemplate)Resources["RowTemplate"];

    /// <summary>Flow'un seçici üzerinden başlıklar için kullandığı şablon — <see cref="HeaderTemplate"/> ile
    /// AYNI nesne olmalı (test kanıtlar).</summary>
    internal DataTemplate HeaderTemplateForFlow() => ((EntrySelector)Flow.ItemTemplateSelector).Header;

    /// <summary>Grupları kur: kümülatif metrics + in-flow entry akışı (başlık + satırlar) + overlay ilk hesap.
    /// Adı boş grup → başlıksız (varsayılan tek liste); sticky devrede değil.</summary>
    public void SetGroups(IReadOnlyList<LayerGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        Metrics = new LayoutMetrics(groups.Select(g => new LayerSpec(g.Name ?? "", g.Rows.Count)).ToList());

        var entries = new List<object>();
        int slot = 0;
        foreach (var g in groups)
        {
            if (!string.IsNullOrEmpty(g.Name))
                entries.Add(new HeaderEntry(g.Name, g.Rows.Count, slot++));
            entries.AddRange(g.Rows);
        }
        Flow.ItemsSource = entries;
        UpdateOverlay(Scroll.VerticalOffset);
    }

    /// <summary>Verilen VerticalOffset'teki yapışık başlıkları overlay'e ver. ScrollChanged production'da bunu
    /// <c>Scroll.VerticalOffset</c> ile çağırır; testler deterministik olsun diye offset'i doğrudan enjekte eder
    /// (D8: gerçek scroll plumbing'e bağlı değil).</summary>
    internal void UpdateOverlay(double verticalOffset)
        => Overlay.ItemsSource = Metrics?.StickyHeadersAt(verticalOffset) ?? NoHeaders;

    private sealed class EntrySelector(StickyLayerList owner) : DataTemplateSelector
    {
        public DataTemplate Header { get; } = owner.HeaderTemplate;
        public DataTemplate Row { get; } = owner.RowTemplate;

        public override DataTemplate SelectTemplate(object item, System.Windows.DependencyObject container)
            => item is HeaderEntry ? Header : Row;
    }
}
