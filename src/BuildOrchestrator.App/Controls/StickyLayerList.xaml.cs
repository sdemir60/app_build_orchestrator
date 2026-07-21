using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

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
    /// <c>{Binding Name}</c>'e bağlanır — <see cref="ViewModels.ProjectRowViewModel"/> gibi bir <c>Name</c>
    /// taşıyan her nesne).</summary>
    public sealed record LayerGroup(string Name, IReadOnlyList<object> Rows);

    /// <summary>In-flow başlık entry'si — <see cref="LayoutMetrics.HeaderInfo"/>'nun WPF-binding karşılığı;
    /// overlay'in StuckHeader'ı ile AYNI <c>Name</c>/<c>RowCount</c> alanlarını taşır ki tek şablon ikisine de
    /// bağlansın.</summary>
    public sealed record HeaderEntry(string Name, int RowCount);

    private static readonly IReadOnlyList<StuckHeader> NoHeaders = [];

    /// <summary>[T59 ile ORTAK] Gruplardan kurulan kümülatif offset servisi — follow-mode/selection scroll
    /// hedefleri AYNI instance'tan üretilir. <see cref="SetGroups"/>'tan önce null.</summary>
    public LayoutMetrics? Metrics { get; private set; }

    // [T59] Follow-mode/seçili-karta-kaydırma orkestratörü — Metrics her SetGroups'ta yenilendiğinden burada da yenilenir.
    private FollowScrollController? _follow;

    public StickyLayerList()
    {
        InitializeComponent();
        Flow.ItemTemplateSelector = new EntrySelector(this);
        Overlay.ItemsSource = NoHeaders;
        // Salt aritmetik overlay recompute: kaydırmada yapışık küme değişir (ScrollUnit=Pixel → VerticalOffset px).
        Scroll.ScrollChanged += (_, _) => UpdateOverlay(Scroll.VerticalOffset);
        // [T59] Kullanıcı tekerleği çevirdiği anda uçuştaki follow/seçim-scroll animasyonu iptal olur + suppress
        // bayrağı kalkar (feasibility §3.3 — WPF'te wheel'in animasyonu otomatik iptal etmesi YOK, tarayıcının aksine).
        ScrollAnimator.EnableUserCancellation(Scroll);
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
        // [T59] Metrics tazelendi — follow/seçim controller'ı da AYNI (yeni) instance'ı paylaşacak şekilde yenilenir.
        _follow = new FollowScrollController(Metrics, () => Scroll.ViewportHeight, () => Scroll.VerticalOffset, AnimateScrollTo);

        var entries = new List<object>();
        foreach (var g in groups)
        {
            if (!string.IsNullOrEmpty(g.Name))
                entries.Add(new HeaderEntry(g.Name, g.Rows.Count));
            entries.AddRange(g.Rows);
        }
        Flow.ItemsSource = entries;
        UpdateOverlay(Scroll.VerticalOffset);
    }

    /// <summary>[T59] Koşarken + seçim yokken frontier satırının (çağıranın belirlediği — ör. ilk
    /// <c>State==Started</c> proje) görünür kalması için çağrılır. Throttle(550ms)/dead-band(54px)/kullanıcı-
    /// suppress kararı <see cref="FollowScrollController"/>'a aittir.</summary>
    public void FollowRow(int rowIndex) => _follow?.FollowRow(rowIndex, ScrollAnimator.GetIsUserSuppressed(Scroll));

    /// <summary>[T59] Kullanıcı tekerleği çevirerek follow'u en son iptal etti mi — çağıranın (ör. bir "geri frontier'e
    /// dön" affordance'ı) bunu gösterip göstermeyeceğine karar vermesi için.</summary>
    public bool IsFollowSuppressedByUser => ScrollAnimator.GetIsUserSuppressed(Scroll);

    /// <summary>[T59] Kullanıcı-suppress bayrağını YOK SAYARAK satırı zorla görünür kılar ("frontier'e dön" tıklaması) —
    /// <see cref="ScrollAnimator.AnimateTo"/> ZATEN her çağrıda suppress'i temizler (yeni programatik hareket).</summary>
    public void ResumeFollow(int rowIndex) => _follow?.FollowRow(rowIndex, userSuppressed: false);

    /// <summary>[T59] Karta tıklama — follow durur, satır 90ms sonra %35 üst-marjla görünür kılınır (Ek A-11).</summary>
    public void SelectRow(int rowIndex) => _follow?.SelectRow(rowIndex);

    /// <summary>[T59] Seçim kalkar — follow kaldığı yerden sürer.</summary>
    public void ClearSelection() => _follow?.ClearSelection();

    // [T59] ScrollAnimator'a sarar: süre/eğri Foundation'dan, motion sinyali ÇAĞRI ANINDA taze okunur (sözleşme).
    // design-v1 §1.3: "yer değiştirme" = ease-in-out. Scroll'un kendi bir süre token'ı YOK (yalnız throttle/
    // dead-band kadansı verilmiş) — 4 Foundation süresinden en yakını (Slow) gerekçeli seçim (bkz. ScrollAnimator
    // XML yorumu ve task-5-report.md). [M-1] ConsoleView.AnimateToBottom ile AYNI desen — MotionTokens.
    // AnimateSlowEaseInOut'a çıkarıldı (kopya YASAK, CLAUDE.md).
    private bool AnimateScrollTo(double target) =>
        MotionTokens.AnimateSlowEaseInOut(this, Scroll, Scroll.VerticalOffset, target);

    /// <summary>Verilen VerticalOffset'teki yapışık başlıkları overlay'e ver. ScrollChanged production'da bunu
    /// <c>Scroll.VerticalOffset</c> ile çağırır; testler deterministik olsun diye offset'i doğrudan enjekte eder
    /// (D8: gerçek scroll plumbing'e bağlı değil).
    ///
    /// <para><b>[Final review I-2 / A13.2 "koleksiyon reset YOK"]</b> ItemsSource'a atama ItemsControl için TAM
    /// reset'tir (container teardown + yeniden üretim). T59'un animasyonlu scroll'u burayı HER KAREDE çağırdığından
    /// atama yalnız yapışık küme GERÇEKTEN değiştiğinde yapılır: <see cref="LayoutMetrics.StickyHeadersAt"/> aynı
    /// adet için hep AYNI (önbelleklenmiş) instance'ı döndürür, burada da referans eşitliği kontrol edilir.</para>
    /// </summary>
    internal void UpdateOverlay(double verticalOffset)
    {
        var stuck = Metrics?.StickyHeadersAt(verticalOffset) ?? NoHeaders;
        if (ReferenceEquals(Overlay.ItemsSource, stuck)) return;
        Overlay.ItemsSource = stuck;
    }

    private sealed class EntrySelector(StickyLayerList owner) : DataTemplateSelector
    {
        public DataTemplate Header { get; } = owner.HeaderTemplate;
        public DataTemplate Row { get; } = owner.RowTemplate;

        public override DataTemplate SelectTemplate(object item, System.Windows.DependencyObject container)
            => item is HeaderEntry ? Header : Row;
    }
}
