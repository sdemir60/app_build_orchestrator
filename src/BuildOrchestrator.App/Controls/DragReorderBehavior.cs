using System.Collections;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BuildOrchestrator.App.Controls;

/// <summary>[D7] Sürüklenebilir bir kart-liste öğesinin, sürüklenirken görsel "kalkık" durumunu taşıyan VM
/// sözleşmesi. <see cref="DragReorderBehavior"/> öğe tipini bilmeden (decouple) bu bayrağı set eder; kart
/// şablonu zemini/kenarı bundan (template-local trigger, A13.2) sürer.</summary>
public interface IDragReorderItem
{
    bool IsDragging { get; set; }
}

/// <summary>[D7] Katman kartlarının <b>Mouse.Capture tabanlı</b> sürükle-sırala davranışı. <b>A13.2:
/// <c>DragDrop.DoDragDrop</c> YASAK</b> — OLE sürükle-bırak hiç kullanılmaz; sürükleme, grip öğesinin
/// mouse'u yakalaması (<see cref="UIElement.CaptureMouse"/>) + yakalama boyunca gelen <c>MouseMove</c>'larla
/// sürülür. ±<see cref="SwapThreshold"/>px eşikte komşuyla yer değiştirir; her adımda başlangıç Y'si bir tam
/// <see cref="RowStep"/> (kart + boşluk) kaydırılır (çoklu-adım sürükleme). Komşular ANİMASYONSUZ snap eder
/// (sürüklenen karta translate follow uygulanır, komşulara değil). Ölçüler prototipten (BuildApp.jsx).
/// </summary>
public static class DragReorderBehavior
{
    // ---- Ölçüler (BuildApp.jsx — off-token design değeri, kaynak-atıflı sabit) ----
    /// <summary>Kart yüksekliği (BuildApp.jsx:1046 <c>height: 36</c>).</summary>
    public const double CardHeight = 36;
    /// <summary>Kartlar arası boşluk (BuildApp.jsx:1046 <c>marginBottom: 6</c>).</summary>
    public const double CardGap = 6;
    /// <summary>Bir satır adımı = kart + boşluk (BuildApp.jsx:1001 <c>ROWH = 42</c>).</summary>
    public const double RowStep = CardHeight + CardGap;
    /// <summary>Komşuyla swap eşiği (BuildApp.jsx:1011 <c>ROWH/2 = 21</c>). deltaY bu değeri (mutlak) AŞINCA
    /// bir swap tetiklenir (kesin <c>&gt;</c>, prototiple birebir).</summary>
    public const double SwapThreshold = RowStep / 2;

    /// <summary>Bir sürükleme çözümünün sonucu: uygulanacak ardışık komşu-swap'ler (sırayla) ve sürüklenen
    /// öğenin yeni indeksi.</summary>
    public readonly record struct DragResolution(int NewIndex, IReadOnlyList<(int From, int To)> Swaps);

    /// <summary>[D7 — SAF karar, test edilebilir seam] Verilen sürüklenen indeks + toplam sayı + biriken
    /// <paramref name="deltaY"/> (currentY - startY) için hangi ardışık komşu-swap'lerin olacağını hesaplar.
    /// BuildApp.jsx:1008-1013 mantığının birebir portu: deltaY &gt; +21 iken aşağı komşuyla swap ve startY
    /// += 42 (deltaY -= 42); deltaY &lt; -21 iken yukarı komşuyla swap ve startY -= 42 (deltaY += 42);
    /// sınırlarda (0 / count-1) durur. Çoklu-adım: büyük bir deltaY birden çok swap üretir.</summary>
    public static DragResolution Resolve(int index, int count, double deltaY)
    {
        var swaps = new List<(int From, int To)>();
        int cur = index;
        double off = deltaY;
        // Aşağı: BuildApp.jsx:1011 `while (off > ROWH/2 && idx < len-1) { swap(idx,idx+1); idx++; startY+=ROWH; off-=ROWH; }`
        while (off > SwapThreshold && cur < count - 1)
        {
            swaps.Add((cur, cur + 1));
            cur++;
            off -= RowStep;
        }
        // Yukarı: BuildApp.jsx:1012 `while (off < -ROWH/2 && idx > 0) { swap(idx,idx-1); idx--; startY-=ROWH; off+=ROWH; }`
        while (off < -SwapThreshold && cur > 0)
        {
            swaps.Add((cur, cur - 1));
            cur--;
            off += RowStep;
        }
        return new DragResolution(cur, swaps);
    }

    // ======================================================================================== WPF davranışı

    /// <summary>[D7] Grip öğesine takılır: <c>true</c> iken o öğe kart sürükleme kolu olur (mouse'u yakalar).</summary>
    public static readonly DependencyProperty IsDragHandleProperty = DependencyProperty.RegisterAttached(
        "IsDragHandle", typeof(bool), typeof(DragReorderBehavior),
        new PropertyMetadata(false, OnIsDragHandleChanged));

    public static void SetIsDragHandle(DependencyObject d, bool value) => d.SetValue(IsDragHandleProperty, value);
    public static bool GetIsDragHandle(DependencyObject d) => (bool)d.GetValue(IsDragHandleProperty);

    /// <summary>[D7 — test seam] Uçuştaki tek sürükleme oturumu (aynı anda tek drag). Üretimde mouse event'leri
    /// sürer; testler yakalamayı kanıtladıktan sonra <see cref="DragSession.DragTo"/>'yu doğrudan çağırarak
    /// pozisyonu deterministik enjekte eder (WPF gerçek imleç pozisyonu enjekte edilemez).</summary>
    internal static DragSession? ActiveSession { get; private set; }

    private static void OnIsDragHandleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement grip) return;
        if ((bool)e.NewValue) grip.MouseLeftButtonDown += OnGripPressed;
        else grip.MouseLeftButtonDown -= OnGripPressed;
    }

    private static void OnGripPressed(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement grip || ActiveSession is not null) return;
        var itemsControl = FindAncestor<System.Windows.Controls.ItemsControl>(grip);
        if (itemsControl?.ItemsSource is not IList list) return;
        object? item = grip.DataContext;
        int index = list.IndexOf(item);
        if (index < 0) return;

        // [A13.2] Sürükleme = Mouse.Capture — OLE DragDrop.DoDragDrop ASLA çağrılmaz. Yakalama grip'te tutulur;
        // yakalama boyunca gelen MouseMove'lar sürüklemeyi sürer, MouseUp/LostCapture bitirir.
        if (!grip.CaptureMouse()) return;
        e.Handled = true;
        var container = itemsControl.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
        ActiveSession = new DragSession(grip, itemsControl, list, item, index, e.GetPosition(itemsControl).Y, container);

        grip.MouseMove += OnGripMove;
        grip.MouseLeftButtonUp += OnGripReleased;
        grip.LostMouseCapture += OnGripLostCapture;
    }

    private static void OnGripMove(object sender, MouseEventArgs e)
    {
        if (ActiveSession is { } s && e.LeftButton == MouseButtonState.Pressed)
            s.DragTo(e.GetPosition(s.ItemsControl).Y);
    }

    private static void OnGripReleased(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement grip) grip.ReleaseMouseCapture(); // → OnGripLostCapture temizler
    }

    private static void OnGripLostCapture(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement grip) return;
        grip.MouseMove -= OnGripMove;
        grip.MouseLeftButtonUp -= OnGripReleased;
        grip.LostMouseCapture -= OnGripLostCapture;
        ActiveSession?.End();
        ActiveSession = null;
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        for (var cur = VisualTreeHelper.GetParent(start); cur is not null; cur = VisualTreeHelper.GetParent(cur))
            if (cur is T match) return match;
        return null;
    }

    private static void MoveItem(IList list, int from, int to)
    {
        // ObservableCollection<T>.Move (Reset yerine Move bildirimi → container/seçim/yakalama korunur, A13.2).
        var move = list.GetType().GetMethod("Move", [typeof(int), typeof(int)]);
        if (move is not null) move.Invoke(list, [from, to]);
        else { var item = list[from]; list.RemoveAt(from); list.Insert(to, item); }
    }

    /// <summary>[D7] Tek sürükleme oturumunun durumu: sürüklenen öğe + indeks + kayan startY + container görseli.
    /// SAF <see cref="Resolve"/> kararını uygular; komşular ANİMASYONSUZ snap (Move), sürüklenen karta residual
    /// translate follow uygulanır.</summary>
    internal sealed class DragSession
    {
        private readonly FrameworkElement _grip;
        private readonly IList _list;
        private readonly object? _item;
        private readonly FrameworkElement? _container;
        private double _startY;
        private int _index;
        private readonly int _originIndex;
        private readonly double _originStartY;
        private readonly int? _prevZIndex;

        public DragSession(FrameworkElement grip, System.Windows.Controls.ItemsControl itemsControl,
            IList list, object? item, int index, double startY, FrameworkElement? container)
        {
            _grip = grip;
            ItemsControl = itemsControl;
            _list = list;
            _item = item;
            _index = index;
            _originIndex = index;
            _startY = startY;
            _originStartY = startY;
            _container = container;
            if (_item is IDragReorderItem d) d.IsDragging = true;
            if (_container is not null)
            {
                _prevZIndex = System.Windows.Controls.Panel.GetZIndex(_container);
                System.Windows.Controls.Panel.SetZIndex(_container, 5); // sürüklenen kart üstte (prototip zIndex 5)
            }
        }

        public System.Windows.Controls.ItemsControl ItemsControl { get; }
        internal double StartY => _startY;
        internal int Index => _index;

        internal void DragTo(double currentY)
        {
            double deltaY = currentY - _startY;
            var res = Resolve(_index, _list.Count, deltaY);
            foreach (var (from, to) in res.Swaps) MoveItem(_list, from, to);
            _startY += (res.NewIndex - _index) * RowStep; // her swap'te startY bir tam adım kayar
            _index = res.NewIndex;
            // Residual: sürüklenen kartın imleci takip eden translate'i (komşular animasyonsuz snap eder).
            double residual = currentY - _startY;
            if (_container is not null) _container.RenderTransform = new TranslateTransform(0, residual);
        }

        internal void End()
        {
            if (_item is IDragReorderItem d) d.IsDragging = false;
            if (_container is not null)
            {
                _container.RenderTransform = Transform.Identity;
                if (_prevZIndex is { } z) System.Windows.Controls.Panel.SetZIndex(_container, z);
            }
        }
    }
}
