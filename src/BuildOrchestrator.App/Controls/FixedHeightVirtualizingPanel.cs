using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// Öğe yüksekliğinin ÖNCEDEN BİLİNDİĞİ dikey listeler için sanallaştırılmış panel: yalnız kapsayıcı
/// <see cref="ScrollViewer"/>'ın o anki penceresine düşen container'lar realize edilir.
///
/// <para><b>Neden WPF'in <c>VirtualizingStackPanel</c>'i yetmiyor:</b> o, <c>ScrollUnit=Pixel</c>'de realize
/// EDİLMEMİŞ öğelerin yüksekliğini realize olanların ORTALAMASINDAN tahmin eder. Proje listesi karışık
/// yüksekliklidir (36 px satır + 24 px katman başlığı), dolayısıyla ortalama gerçek offset'ten kayar; oysa
/// yapışık başlıklar, follow-mode ve seçim-scroll'unun üçü de <see cref="LayoutMetrics"/>'in KÜMÜLATİF
/// tablosunu okur ve o tablonun <c>ScrollViewer.VerticalOffset</c> ile birebir tutması gerekir.
/// (ARCHITECTURE §20'nin "virtualization ertelendi" gerekçesi olan drift tam olarak buydu.)</para>
///
/// <para><b>Scroll tesisatı DEĞİŞMEZ.</b> Panel <see cref="IScrollInfo"/> uygulamaz; kaydırmayı yine dıştaki
/// <c>ScrollViewer</c> yürütür ve panel ona <b>gerçek toplam yüksekliği</b> (tahmin değil, tablonun kendisi)
/// <see cref="UIElement.DesiredSize"/> olarak bildirir. Böylece <c>VerticalOffset</c>/<c>ExtentHeight</c>
/// semantiği, <c>ScrollAnimator</c>, bottom-anchor ve follow-mode aynen çalışmaya devam eder — sanallaştırma
/// yalnız hangi satırların GERÇEKTEN kurulacağını değiştirir.</para>
///
/// <para><b>Container geri dönüşümü:</b> <c>VirtualizationMode.Recycling</c> ile container'lar yeniden
/// kullanılır; satır kontrolü yeni <c>DataContext</c>'te kendini tam tazelemekle yükümlüdür
/// (<c>ProjectRow.OnDataContextChanged</c> bunu zaten yapar).</para>
/// </summary>
public sealed class FixedHeightVirtualizingPanel : VirtualizingPanel
{
    /// <summary>Görünür pencerenin ÜSTÜNE ve ALTINA fazladan realize edilen pay (viewport oranı). Kaydırma
    /// sırasında container üretimi bir kare geriden gelmesin diye vardır; yarım viewport hem akıcı hem ucuz.</summary>
    private const double CacheRatio = 0.5;

    /// <summary>Öğe → yükseklik. <b>ItemsControl'e</b> (panelin sahibi) atanır; panel şablonun içinde doğduğu
    /// için ona doğrudan bir değer geçirmenin başka yolu yoktur. Verilmezse tüm öğeler
    /// <see cref="LayoutMetrics.DefaultRowHeight"/> sayılır.</summary>
    public static readonly DependencyProperty EntryHeightSelectorProperty =
        DependencyProperty.RegisterAttached(
            "EntryHeightSelector", typeof(Func<object, double>), typeof(FixedHeightVirtualizingPanel),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static void SetEntryHeightSelector(DependencyObject element, Func<object, double>? value) =>
        (element ?? throw new ArgumentNullException(nameof(element))).SetValue(EntryHeightSelectorProperty, value);

    public static Func<object, double>? GetEntryHeightSelector(DependencyObject element) =>
        (Func<object, double>?)(element ?? throw new ArgumentNullException(nameof(element)))
            .GetValue(EntryHeightSelectorProperty);

    private double[] _tops = [0];  // öğe indeksi → içerik Y (üst); son eleman TOPLAM yükseklik
    private int _topsCount = -1;   // _tops hangi öğe sayısı için kuruldu
    private ScrollViewer? _scroll; // kapsayıcı ScrollViewer (viewport kaynağı) — bir kez bulunur

    public FixedHeightVirtualizingPanel() => Loaded += (_, _) => AttachScrollViewer();

    /// <summary>Kapsayıcı <see cref="ScrollViewer"/>'ı bulur ve kaydırmada yeniden ölçüm ister — realize
    /// penceresi ancak böyle kayar. ScrollViewer yoksa (izole test) panel her şeyi realize eder: sanallaştırma
    /// bir GÖRÜNÜRLÜK optimizasyonudur, doğruluk şartı değildir.</summary>
    private void AttachScrollViewer()
    {
        if (_scroll is not null) return;
        for (DependencyObject? node = this; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is ScrollViewer viewer) { _scroll = viewer; break; }
        if (_scroll is not null) _scroll.ScrollChanged += (_, _) => InvalidateMeasure();
    }

    // ---------------------------------------------------------------- ölçüm / yerleşim

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null) return default;

        // ZORUNLU ve SIRALI: panelin ItemContainerGenerator'ı ancak InternalChildren'a DOKUNULDUKTAN sonra
        // kurulur (WPF'in kendi custom-VirtualizingPanel örneğinin de baştan yaptığı şey). Atlanırsa generator
        // ilk ölçümde null gelir ve panel NullReferenceException ile düşer.
        _ = InternalChildren;

        AttachScrollViewer(); // Loaded gelmeden ölçülebiliriz (headless realize) — kapı idempotent
        int count = owner.Items.Count;
        RebuildTops(owner, count);

        double totalHeight = _tops[^1];
        double width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        if (count == 0) { CleanUp(0, -1); return new Size(width, 0); }

        // Görünür pencere kapsayıcı ScrollViewer'dan okunur. İLK ölçüm turunda viewport HENÜZ BİLİNMEZ (0) ve
        // o turda HİÇBİR satır realize edilmez: panelin bildirdiği yükseklik realize olmuş çocuklara DEĞİL
        // kümülatif tabloya dayandığı için ScrollViewer viewport'unu bu turda da doğru hesaplar; hemen ardından
        // gelen ScrollChanged yeniden ölçüm ister ve gerçek pencere AYNI UpdateLayout turunda realize olur.
        // (Buraya bir "ekran kadar tahmin et" koymak, kaçınmak için var olduğumuz gereksiz işi geri getirirdi.)
        double viewportHeight = _scroll?.ViewportHeight ?? 0;
        if (viewportHeight <= 0) { CleanUp(0, -1); return new Size(width, totalHeight); }

        double cache = viewportHeight * CacheRatio;
        double offset = _scroll!.VerticalOffset;
        double windowTop = offset - cache;
        double windowBottom = offset + viewportHeight + cache;

        int first = IndexAt(windowTop);
        int last = IndexAt(windowBottom);
        Realize(count, first, last, width);
        CleanUp(first, last);

        // Dıştaki ScrollViewer'ın extent'i BU yükseklikten doğar — tahmin değil, kümülatif tablonun kendisi.
        return new Size(width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var generator = ItemContainerGenerator;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            int index = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (index < 0 || index >= _tops.Length - 1) continue;
            // Panel'in KENDİSİ kaydırılır (ScrollViewer tarafından) → çocuklar MUTLAK içerik Y'sine yerleşir.
            child.Arrange(new Rect(0, _tops[index], finalSize.Width, _tops[index + 1] - _tops[index]));
        }
        return finalSize;
    }

    private void Realize(int itemCount, int first, int last, double width)
    {
        last = Math.Min(last, itemCount - 1);
        var generator = ItemContainerGenerator;
        var startPosition = generator.GeneratorPositionFromIndex(first);
        int childIndex = startPosition.Offset == 0 ? startPosition.Index : startPosition.Index + 1;

        using var _ = generator.StartAt(startPosition, GeneratorDirection.Forward, allowStartAtRealizedItem: true);
        for (int i = first; i <= last; i++, childIndex++)
        {
            if (generator.GenerateNext(out bool isNewlyRealized) is not UIElement child) break;

            // [KRİTİK — geri dönüşüm] "isNewlyRealized == false" TEK BAŞINA "container zaten ağaçta" DEMEK
            // DEĞİLDİR: havuzdan geri verilen bir container da false ile gelir, oysa geri dönüştürülürken
            // InternalChildren'dan ÇIKARILMIŞTIR. Yalnız isNewlyRealized'a bakan bir panel onu bir daha ağaca
            // koymaz; sonuç, kullanıcının gördüğü boşluk ve aşağı-yukarı kaydırdıkça "eksile eksile kaybolan"
            // listedir (pin: ListVirtualizationScrollTests). Bu yüzden container'ın ağaçtaki YERİ doğrulanır.
            int existing = InternalChildren.IndexOf(child);
            if (existing < 0)
            {
                InsertOrAddChild(childIndex, child);
                generator.PrepareItemContainer(child);
            }
            else if (existing != childIndex)
            {
                // Sıra kaymış (geri dönüşümde olabilir): CleanUp'ın konum aritmetiği InternalChildren sırasının
                // öğe sırasıyla AYNI olmasına dayanır — düzelt.
                RemoveInternalChildRange(existing, 1);
                InsertOrAddChild(childIndex, child);
            }

            child.Measure(new Size(width, _tops[i + 1] - _tops[i]));
        }
    }

    private void InsertOrAddChild(int childIndex, UIElement child)
    {
        if (childIndex >= InternalChildren.Count) AddInternalChild(child);
        else InsertInternalChild(childIndex, child);
    }

    /// <summary>Pencerenin dışına düşen container'ları bırakır. Geri dönüşüm açıksa container HAVUZA döner
    /// (yok edilmez) — kaydırmada yeniden inşa maliyeti böylece ödenmez.</summary>
    private void CleanUp(int first, int last)
    {
        var generator = ItemContainerGenerator;
        bool recycling = GetVirtualizationMode(ItemsControl.GetItemsOwner(this)) == VirtualizationMode.Recycling;
        for (int i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var position = new GeneratorPosition(i, 0);
            int index = generator.IndexFromGeneratorPosition(position);
            if (index >= first && index <= last) continue;

            if (recycling && generator is IRecyclingItemContainerGenerator recycler) recycler.Recycle(position, 1);
            else generator.Remove(position, 1);
            RemoveInternalChildRange(i, 1);
        }
    }

    /// <summary>Kümülatif üst-kenar tablosu (son eleman = toplam yükseklik). Öğe sayısı değişmedikçe yeniden
    /// kurulmaz; öğe kümesi değişimi <see cref="OnItemsChanged"/>'de geçersizleştirilir.</summary>
    private void RebuildTops(ItemsControl owner, int count)
    {
        if (_topsCount == count) return;
        var heightOf = GetEntryHeightSelector(owner);
        var tops = new double[count + 1];
        double y = 0;
        for (int i = 0; i < count; i++)
        {
            tops[i] = y;
            y += heightOf?.Invoke(owner.Items[i]) ?? LayoutMetrics.DefaultRowHeight;
        }
        tops[count] = y;
        _tops = tops;
        _topsCount = count;
    }

    /// <summary>Verilen içerik Y'sindeki öğenin indeksi (kelepçeli). Tablo artan olduğundan ikili arama.</summary>
    private int IndexAt(double contentY)
    {
        int count = _tops.Length - 1;
        if (count <= 0 || contentY <= 0) return 0;
        if (contentY >= _tops[count]) return count - 1;

        int lo = 0, hi = count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_tops[mid] <= contentY) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _topsCount = -1; // öğe kümesi değişti → kümülatif tablo baştan
        switch (args.Action)
        {
            case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Move:
                RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                break;
            case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                RemoveInternalChildRange(0, InternalChildren.Count);
                break;
        }
        base.OnItemsChanged(sender, args);
    }
}
