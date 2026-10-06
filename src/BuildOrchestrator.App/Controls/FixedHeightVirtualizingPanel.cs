using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// Öğe yüksekliğinin ÖNCEDEN BİLİNDİĞİ dikey listeler için satır paneli: kapsayıcı <see cref="ScrollViewer"/>'a
/// TAHMİNSİZ toplam yüksekliği bildirir, satırları ise AŞAMALI kurar — ilk yerleşim turunda yalnız görünür pencereyi
/// (+ yarım viewport pay), geri kalanını dispatcher boşken küçük dilimlerle (<see cref="IdleSliceRows"/>). Kurulan
/// bir satır bir daha BIRAKILMAZ: pencere kayınca geri dönüştürülmez, sökülmez.
///
/// <para><b>Neden WPF'in <c>VirtualizingStackPanel</c>'i yetmiyor:</b> o, <c>ScrollUnit=Pixel</c>'de realize
/// EDİLMEMİŞ öğelerin yüksekliğini realize olanların ORTALAMASINDAN tahmin eder. Proje listesi karışık
/// yüksekliklidir (36 px satır + 24 px katman başlığı), dolayısıyla ortalama gerçek offset'ten kayar; oysa
/// yapışık başlıklar, follow-mode ve seçim-scroll'unun üçü de <see cref="LayoutMetrics"/>'in KÜMÜLATİF
/// tablosunu okur ve o tablonun <c>ScrollViewer.VerticalOffset</c> ile birebir tutması gerekir.</para>
///
/// <para><b>Neden satırlar kalıcı (ÖLÇÜLDÜ):</b> görünür bir koşuda UI thread'inin en uzun kesintisiz dilimleri
/// (98–226 ms) pencere kaydıkça geri dönüştürülen container'ların YENİ veriye bağlanmasıydı — satır başına metin
/// biçimleme (ölçüm + çizim), UIA peer tazelemesi ve binding yazımı (~2,5 ms); takip kaydırması pencereyi tek
/// karede 30–60 satır taşıdığında hepsi tek dilimde toplanıyor ve graf animasyonları o dilim boyunca duruyordu.
/// Satır İNŞASI dilimin yalnız 8–14 ms'siydi; kaldıraç bu yüzden inşayı ısıtmak değil, yeniden bağlamayı ortadan
/// kaldırmaktır: kontrol satırında kalınca kaydırma hiçbir satırı yeniden bağlamaz, yerleşim yalnız kaydırma
/// çevirisidir. Bedel satır başına BİR KEZ ödenir ve kritik yolun dışındadır; bellek payı satır başına bir
/// kontroldür.</para>
///
/// <para><b>Scroll tesisatı DEĞİŞMEZ.</b> Panel <see cref="IScrollInfo"/> uygulamaz; kaydırmayı yine dıştaki
/// <c>ScrollViewer</c> yürütür ve panel ona <b>gerçek toplam yüksekliği</b> (tahmin değil, tablonun kendisi)
/// <see cref="UIElement.DesiredSize"/> olarak bildirir. Böylece <c>VerticalOffset</c>/<c>ExtentHeight</c>
/// semantiği, <c>ScrollAnimator</c>, bottom-anchor ve follow-mode aynen çalışmaya devam eder.</para>
///
/// <para><b>Öğe değişimleri:</b> çıkan öğenin container'ı sökülür; giren ya da yer değiştiren öğe pencereye düşüyorsa
/// yerleşimde, değilse boşta dolumda (yeniden) kurulur. Yer değiştiren öğenin container'ı BİLEREK bırakılır:
/// WPF'in generator'ı ağaçta kalan bir container'ı yeni indeksine bağlarken, yeni yer kurulmamış bir bloğa
/// komşuysa yanlış ofsete yazar (<c>ItemContainerGenerator.OnItemMoved</c> — bu dal WPF'in kendi panellerinde hiç
/// çalışmaz, onlar da container'ı söker) ve harita koleksiyondan ayrışır; ölçülen sonuç kaybolan satırlardı.
/// Reset (koleksiyon takası) tüm container'ları bırakır; liste bu yüzden koleksiyonu takas etmez, yerinde
/// uzlaştırır (<see cref="ListReconciler"/>) — dokunulmayan satır dokunulmadan kalır.</para>
/// </summary>
public sealed class FixedHeightVirtualizingPanel : VirtualizingPanel
{
    /// <summary>Görünür pencerenin ÜSTÜNE ve ALTINA fazladan kurulan pay (viewport oranı). İlk turda kaydırma
    /// container üretimi bir kare geriden gelmesin diye vardır; dolum tamamlanınca anlamı kalmaz.</summary>
    private const double CacheRatio = 0.5;

    /// <summary>Boşta dolumun dilim boyu. Bir dilim bu kadar satırı kurar, ardından gelen yerleşim turu onları
    /// ölçer ve yerleştirir (referans makinede satır başına ~1 ms inşa + ~1,5 ms ölçüm/yerleşim): dilim ≈ 8 ms,
    /// yani bir karenin (16,7 ms) yarısı — dilimler animasyon kareleri arasına sığar. 200 satır ~70 dilimde,
    /// kabaca bir saniyede dolar.</summary>
    internal const int IdleSliceRows = 3;

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
    private DispatcherOperation? _idleFill; // kuyruktaki dolum dilimi (en çok bir tane)
    private int _fillCursor;       // boşta dolumun bir sonraki adayı (öğe indeksi)

    public FixedHeightVirtualizingPanel()
    {
        Loaded += (_, _) => AttachScrollViewer();
        Unloaded += (_, _) => CancelIdleFill();
    }

    /// <summary>Test yüzeyi: ağaçtaki container sayısı (= kurulmuş öğe sayısı).</summary>
    internal int RealizedCount => InternalChildren.Count;

    /// <summary>Kapsayıcı <see cref="ScrollViewer"/>'ı bulur ve kaydırmada yeniden ölçüm ister — pencere ancak
    /// böyle kayar. ScrollViewer yoksa (izole test) yerleşim turu satır kurmaz; satırlar yalnız boşta dolumla
    /// gelir (o da panel bir pencereye bağlıyken).</summary>
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
        if (count == 0) return new Size(width, 0);

        // Görünür pencere kapsayıcı ScrollViewer'dan okunur. İLK ölçüm turunda viewport HENÜZ BİLİNMEZ (0) ve
        // o turda HİÇBİR satır kurulmaz: panelin bildirdiği yükseklik kurulu çocuklara DEĞİL kümülatif tabloya
        // dayandığı için ScrollViewer viewport'unu bu turda da doğru hesaplar; hemen ardından gelen ScrollChanged
        // yeniden ölçüm ister ve gerçek pencere AYNI UpdateLayout turunda kurulur.
        double viewportHeight = _scroll?.ViewportHeight ?? 0;
        if (viewportHeight > 0)
        {
            double cache = viewportHeight * CacheRatio;
            double offset = _scroll!.VerticalOffset;
            RealizeRange(count, IndexAt(offset - cache), IndexAt(offset + viewportHeight + cache));
        }

        MeasureChildren(width);
        ScheduleIdleFill(count);

        // Dıştaki ScrollViewer'ın extent'i BU yükseklikten doğar — tahmin değil, kümülatif tablonun kendisi.
        return new Size(width, totalHeight);
    }

    /// <summary>Her kurulu çocuk kendi satır yüksekliğiyle ölçülür. Ölçümü geçerli olan çocukta WPF erken döner:
    /// kaydırma turlarında bu döngü yalnız bir sayımdır.</summary>
    private void MeasureChildren(double width)
    {
        var generator = ItemContainerGenerator;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            int index = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (index < 0 || index >= _tops.Length - 1) continue;
            InternalChildren[i].Measure(new Size(width, _tops[index + 1] - _tops[index]));
        }
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

    /// <summary>[first..last] aralığındaki öğelerin container'larını kurar; zaten kurulu olanlar atlanır.</summary>
    private void RealizeRange(int itemCount, int first, int last)
    {
        last = Math.Min(last, itemCount - 1);
        if (first > last) return;
        var generator = ItemContainerGenerator;
        var startPosition = generator.GeneratorPositionFromIndex(first);
        int childIndex = startPosition.Offset == 0 ? startPosition.Index : startPosition.Index + 1;

        using var generation = generator.StartAt(startPosition, GeneratorDirection.Forward, allowStartAtRealizedItem: true);
        for (int i = first; i <= last; i++, childIndex++)
        {
            if (generator.GenerateNext(out _) is not UIElement child) break;
            // Kurulu bir container ZATEN ağaçtadır ve çocuk sırası generator sırasıyla aynıdır (çıkan/taşınan öğenin
            // container'ı OnItemsChanged'de düşer); yalnız yeni doğan ağaca girer. Ağaçta olup olmadığı ebeveyninden
            // okunur — çocuk listesinde arama O(n) olurdu.
            if (VisualTreeHelper.GetParent(child) == this) continue;
            InsertOrAddChild(childIndex, child);
            generator.PrepareItemContainer(child);
        }
    }

    private void InsertOrAddChild(int childIndex, UIElement child)
    {
        if (childIndex >= InternalChildren.Count) AddInternalChild(child);
        else InsertInternalChild(childIndex, child);
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
        _fillCursor = 0; // boşta dolum baştan tarar — yeni öğe en önde olabilir
        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
                // Çıkan öğenin container'ı (varsa) generator tarafından bırakıldı — ağaçtan da düşer. Replace'te
                // generator AYNI container'ı yeni öğeye bağlar ve olay yaymaz; buraya yalnız kendi container'ı olan
                // bir öğe (bu listede yok) için düşer.
                RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                break;
            case NotifyCollectionChangedAction.Move:
                // Taşınan öğenin container'ı eski yerinden düşer; generator bunu görüp öğeyi kurulmamış sayar ve
                // yeni yerinde yeniden kurulur (gerekçe sınıf doc'unda: ağaçta bırakılan container'ı generator
                // yanlış ofsete bağlayabiliyor). WPF'in kendi panelleri de taşımada böyle yapar.
                RemoveInternalChildRange(args.OldPosition.Index, args.ItemUICount);
                break;
            case NotifyCollectionChangedAction.Reset:
                RemoveInternalChildRange(0, InternalChildren.Count);
                break;
        }
        base.OnItemsChanged(sender, args);
    }

    // ---------------------------------------------------------------- boşta dolum

    private void ScheduleIdleFill(int itemCount)
    {
        if (_idleFill is not null || InternalChildren.Count >= itemCount) return;
        _idleFill = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(FillIdleSlice));
    }

    private void CancelIdleFill()
    {
        _idleFill?.Abort();
        _idleFill = null;
    }

    /// <summary>Bir dilim: kurulmamış ilk öğeden başlayarak en çok <see cref="IdleSliceRows"/> container kurar ve
    /// ölçüm ister; ölçüm turu (<see cref="MeasureOverride"/>) kalan varsa bir sonraki dilimi kuyruklar. Panel bir
    /// pencereye bağlı değilse (ör. HWND'siz ölçüm) dolum bekler — bir sonraki ölçüm turu onu yeniden kuyruklar.</summary>
    private void FillIdleSlice()
    {
        _idleFill = null;
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null || PresentationSource.FromVisual(this) is null) return;
        int count = owner.Items.Count;
        if (InternalChildren.Count >= count) return;

        var generator = owner.ItemContainerGenerator;
        while (_fillCursor < count && generator.ContainerFromIndex(_fillCursor) is not null) _fillCursor++;
        if (_fillCursor >= count) return;

        int last = Math.Min(_fillCursor + IdleSliceRows - 1, count - 1);
        RealizeRange(count, _fillCursor, last);
        _fillCursor = last + 1;
        InvalidateMeasure();
    }
}
