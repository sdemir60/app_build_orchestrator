namespace BuildOrchestrator.App.Controls;

/// <summary>Bir katman: adı (boş → başlıksız grup) ve satır adedi. design-v1 §2.4 gruplaması
/// (Settings regex'leriyle; eşleşmeyen → <c>Other</c>). <b>Varsayılan: katman YOK</b> → tek başlıksız
/// grup (<see cref="LayoutMetrics.Flat"/>).</summary>
public readonly record struct LayerSpec(string Name, int RowCount)
{
    public bool HasHeader => !string.IsNullOrEmpty(Name);
}

/// <summary>In-flow (kaydırılan) bir katman başlığının kümülatif tablodaki yeri — overlay ve testler için.</summary>
/// <param name="LayerIndex">Kaynak <see cref="LayerSpec"/>'in listedeki indeksi (boş gruplar dahil).</param>
/// <param name="SlotIndex">Yalnız GÖRÜNÜR başlıklar arasındaki 0-tabanlı sıra — yapışınca <c>SlotIndex×24</c>'e pinlenir.</param>
/// <param name="ContentTop">Başlığın içerik koordinatındaki mutlak Y'si (üst kenar).</param>
/// <param name="FirstRowIndex">Bu katmanın ilk satırının global satır indeksi.</param>
public sealed record HeaderInfo(int LayerIndex, int SlotIndex, string Name, int RowCount, double ContentTop, int FirstRowIndex);

/// <summary>Belirli bir VerticalOffset'te YAPIŞIK (pinned) bir başlık — overlay bunu <see cref="PinnedY"/>'de çizer.</summary>
public sealed record StuckHeader(int LayerIndex, int SlotIndex, string Name, int RowCount, double PinnedY);

/// <summary>
/// [T58] Projeler listesinin SAF (WPF'siz, test edilebilir) layout aritmetiği — <b>sticky overlay (bu task)
/// ve follow-mode/selection scroll (T59) AYNI instance'ı paylaşır</b> (feasibility §3.3/§4.1). Karışık
/// 36px satır + 24px başlık kümülatif offset tablosunu bir kez kurar; her satırın/başlığın mutlak Y'sini,
/// verilen VerticalOffset'teki yapışık başlık kümesini ve bir satırın scroll hedefini O(1)/O(#başlık) üretir.
///
/// <para><b>Birikimli sticky (accumulation):</b> prototip (<c>BuildApp.jsx:489</c>, <c>position:sticky; top:
/// stick*24</c> — tüm başlıklar TEK scroll kökünün kardeşleri) + README §2.4 ("üsttekiler asılı kalır") →
/// görünür i'inci başlık <c>i×24</c>'e pinlenir ve aşağı kaydırdıkça asılı KALIR (üsttekiler itilip düşmez;
/// alttaki başlık yığına ulaşınca altına yığılır, yığın büyür). Bu yüzden <see cref="StickyHeadersAt"/>
/// yapışık başlıkların bir ÖN-EK'ini (prefix) döner.</para>
///
/// <para><b>Neden virtualization KAPALI (feasibility §4.1):</b> <c>ScrollUnit=Pixel</c>'de bir
/// VirtualizingStackPanel realize edilmemiş item'ları ORTALAMA yükseklikle tahmin eder; karışık 36/24 tabloda
/// bu tahmin gerçek offset'ten kayar → bu aritmetik tablo yalnız virtualization KAPALIYKEN birebir doğrudur
/// (OSYS ~191 satır bu banda rahat girer). 500+ drift kalibrasyon yolu T51/It-5 — burada DEĞİL.</para>
/// </summary>
public sealed class LayoutMetrics
{
    // design-v1 §2.4: satır 36px (compact 30), katman başlığı 24px. Overlay/in-flow şablonlarındaki Height'lar
    // bu sabitlerle BİREBİR eşleşmeli (StickyLayerList) — aksi halde overlay Y'si kayar.
    public const double DefaultRowHeight = 36;
    public const double DefaultHeaderHeight = 24;

    private readonly double[] _rowTops;      // global satır indeksi → içerik Y (üst)
    private readonly HeaderInfo[] _headers;  // yalnız görünür başlıklar, SlotIndex sırasında

    public double RowHeight { get; }
    public double HeaderHeight { get; }
    public int RowCount { get; }
    public double TotalHeight { get; }
    public IReadOnlyList<HeaderInfo> Headers => _headers;

    public LayoutMetrics(IReadOnlyList<LayerSpec> layers, double rowHeight = DefaultRowHeight, double headerHeight = DefaultHeaderHeight)
    {
        ArgumentNullException.ThrowIfNull(layers);
        RowHeight = rowHeight;
        HeaderHeight = headerHeight;

        var rowTops = new List<double>();
        var headers = new List<HeaderInfo>();
        double y = 0;
        int layerIndex = 0;
        foreach (var layer in layers)
        {
            if (layer.HasHeader)
            {
                headers.Add(new HeaderInfo(
                    LayerIndex: layerIndex,
                    SlotIndex: headers.Count,
                    Name: layer.Name,
                    RowCount: layer.RowCount,
                    ContentTop: y,
                    FirstRowIndex: rowTops.Count));
                y += headerHeight;
            }
            for (int r = 0; r < layer.RowCount; r++)
            {
                rowTops.Add(y);
                y += rowHeight;
            }
            layerIndex++;
        }

        _rowTops = [.. rowTops];
        _headers = [.. headers];
        RowCount = _rowTops.Length;
        TotalHeight = y;
    }

    /// <summary>Varsayılan (katman YOK): tek başlıksız grup, uniform 36px — sticky devrede DEĞİL.</summary>
    public static LayoutMetrics Flat(int rowCount, double rowHeight = DefaultRowHeight) =>
        new([new LayerSpec("", rowCount)], rowHeight);

    /// <summary>Global satır indeksinin içerik koordinatındaki mutlak Y'si (üst kenar).</summary>
    public double OffsetOfRow(int rowIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(rowIndex, _rowTops.Length);
        return _rowTops[rowIndex];
    }

    /// <summary>Görünür başlıklar arasındaki <paramref name="slotIndex"/>'inci başlığın içerik Y'si (üst kenar).</summary>
    public double OffsetOfHeader(int slotIndex) => _headers[slotIndex].ContentTop;

    /// <summary>
    /// Verilen VerticalOffset'te YAPIŞIK başlık kümesi (SlotIndex sırasında ön-ek). Başlık <c>j</c>, doğal
    /// içerik konumu (ContentTop) slotuna (<c>j×24</c>) ulaştığında yapışır: eşik <c>τ_j = ContentTop_j -
    /// j×HeaderHeight</c>. <c>τ_j</c> j'de artan olduğundan (her katman en az bir başlık ekler) küme daima bir
    /// ön-ek'tir. Pinned Y = <c>SlotIndex×HeaderHeight</c> (accumulation — üstteki itilmez). Katman yoksa boş.
    /// </summary>
    public IReadOnlyList<StuckHeader> StickyHeadersAt(double verticalOffset)
    {
        var stuck = new List<StuckHeader>();
        foreach (var h in _headers)
        {
            double threshold = h.ContentTop - h.SlotIndex * HeaderHeight;
            if (verticalOffset < threshold) break; // ön-ek: ilk yapışmayan başlıktan sonrası da yapışmaz
            stuck.Add(new StuckHeader(h.LayerIndex, h.SlotIndex, h.Name, h.RowCount, h.SlotIndex * HeaderHeight));
        }
        return stuck;
    }

    /// <summary>
    /// [T59 ile ORTAK] Bir satırı görünür kılacak scroll hedefi (VerticalOffset) — satırın offsetTop'undan
    /// <paramref name="topMargin"/> çıkarılır, 0'ın altına inmez. Prototip (BuildApp.jsx:435/446): follow'da
    /// <c>topMargin = max(150, viewport×0.3)</c>, seçimde <c>×0.35</c>; 150 tabanı yapışık başlık yığınını
    /// (en çok #katman×24) örtecek kadar büyüktür, bu yüzden ayrıca stack telafisi gerekmez. Boşluk POLİTİKASI
    /// (viewport oranı) çağırana (T59) ait; burada yalnız kümülatif offset tablosu.
    /// </summary>
    public double ScrollTargetForRow(int rowIndex, double topMargin = 0) =>
        Math.Max(0, OffsetOfRow(rowIndex) - topMargin);
}
