using System.Windows;

namespace BuildOrchestrator.App.Graph;

/// <summary>[quiet] Yerleşim sonucu: düğüm MERKEZLERİ (ad → İÇERİK koordinatında nokta) + o yerleşimin
/// seçtiği pitch, düğüm kenarı ve sütun sayısı.</summary>
/// <param name="Positions">Ad → merkez. Koordinatlar İÇERİK kutusuna göredir; çizim tarafı
/// <see cref="QuietGraphLayout.ContentInset"/> kadar ötelenir.</param>
public readonly record struct QuietLayoutResult(
    IReadOnlyDictionary<string, Point> Positions,
    double Pitch,
    double NodeSize,
    int Columns);

/// <summary>
/// [quiet] design v1.3.0 §2.3 "Yerleşim — katman bantları" — prototype/app/BuildApp.jsx <c>graphLayout</c>
/// (satır 259-289) SAF portu. WPF'e hiç dokunmaz, tamamen birim-testlenebilir.
///
/// <para><b>Yerleşim artık PANEL ÖLÇÜSÜNÜN fonksiyonudur.</b> Eski yerleşim (880px taban tuval, 96px satır
/// aralığı, 26px düğüm) panelden bağımsızdı ve graf panele sığmadığında kamera uzaklaşırdı. §2.3 bunu ezdi:
/// "Pitch (node adımı) otomatik: 44px'ten 5'e 0.5 adımla taranır; tüm bantlar + bant boşlukları (0.7×pitch)
/// panel yüksekliğine sığan İLK değer seçilir → graf HER panel boyutunda tam sığar, scrollbar yok."</para>
///
/// <para><b>Bantlar derlenme sırasındadır:</b> layer 0 en üstte, bant içi dizilim build-order (ilk
/// derlenecek solda). Göz üstten alta / soldan sağa okuduğunda derleme sırasını görür; açılış dalgası da
/// aynı sırayı izler.</para>
/// </summary>
public static class QuietGraphLayout
{
    /// <summary>Pitch taramasının tavanı — bol yerde düğümler bundan daha seyrek dizilmez.</summary>
    public const double MaxPitch = 44.0;
    /// <summary>Pitch taramasının tabanı; hiçbir adım sığmazsa bu kullanılır.</summary>
    public const double MinPitch = 5.0;
    public const double PitchStep = 0.5;
    /// <summary>İki bant arasındaki boşluk, pitch cinsinden.</summary>
    public const double BandGapPitches = 0.7;
    /// <summary>Düğüm kenarı = pitch × bu çarpan, <see cref="MinNodeSize"/>–<see cref="MaxNodeSize"/> kelepçeli.</summary>
    public const double NodeSizeFactor = 0.6;
    public const double MinNodeSize = 8.0;
    public const double MaxNodeSize = 24.0;
    /// <summary>Hesap alanının panel kenarlarına bıraktığı pay (her yandan).
    ///
    /// <para><b>§2.3'ün sayısı 12px'ti; kullanıcı kararıyla önce 28, sonra 36px.</b> 12px'te graf panele
    /// yapışık ve "ferah değil" duruyordu; 28 doğru yönde ama yetersiz bulundu. Pay büyüdükçe hesap alanı
    /// daralır, yani aynı panelde pitch bir miktar küçülür — bilinçli takas.</para>
    ///
    /// <para>Pay artık dört yanda SİMETRİKTİR (sağ alttaki mono ipucu satırı kaldırıldığı için ona ayrılan
    /// dikey rezerv de yok) ve <b>tek doğruluk kaynağıdır</b>: overlay katmanı da (tooltip, ad etiketi)
    /// kelepçesini bu sayıdan okur (<see cref="GraphOverlay"/>), böylece odak kipinde etiket köşeye yapışmak
    /// yerine grafın kendi nefes payının içinde durur.</para></summary>
    public const double ContentInset = 36.0;
    /// <summary>Hesabın taban panel ölçüsü: panel bundan küçükse yerleşim yine de üretilir (taşan kırpılır).</summary>
    public const double MinPanelWidth = 240.0;
    public const double MinPanelHeight = 160.0;

    /// <summary>Yerleşimin hesap alanı: panel eksi kenar payı (dört yanda simetrik). Panel
    /// tabanın altındaysa taban kullanılır — aksi halde negatif bir alan taramayı anlamsız kılardı.</summary>
    public static Size ContentSize(Size panel) => new(
        Math.Max(MinPanelWidth, panel.Width) - 2 * ContentInset,
        Math.Max(MinPanelHeight, panel.Height) - 2 * ContentInset);

    /// <summary>
    /// Pitch taraması: 44'ten 5'e 0.5 adımla iner, TÜM bantların satırları + bant boşlukları hesap
    /// yüksekliğine sığdığı İLK adımı döner.
    ///
    /// <para>Sütun sayısı en kalabalık bandı ASLA aşmaz: aşsaydı hiçbir bant tam satır dolduramaz, her
    /// satır "eksik son satır" sayılıp ortalanır ve blok ortalamasıyla çakışırdı (JSX:269).</para>
    /// </summary>
    public static (double Pitch, int Columns) ResolvePitch(Size content, IReadOnlyList<int> bandCounts)
    {
        ArgumentNullException.ThrowIfNull(bandCounts);
        if (bandCounts.Count == 0) return (MinPitch, 1);

        int widest = 1;
        foreach (int count in bandCounts) widest = Math.Max(widest, count);

        for (double pitch = MaxPitch; pitch >= MinPitch; pitch -= PitchStep)
        {
            int columns = Math.Max(1, Math.Min(widest, (int)Math.Floor(content.Width / pitch)));
            double rows = 0;
            foreach (int count in bandCounts) rows += Math.Ceiling(count / (double)columns);
            if ((rows + (bandCounts.Count - 1) * BandGapPitches) * pitch <= content.Height)
                return (pitch, columns);
        }

        return (MinPitch, 1);
    }

    public static QuietLayoutResult Compute(IReadOnlyList<GraphNode> nodes, Size panel)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var positions = new Dictionary<string, Point>(nodes.Count, StringComparer.Ordinal);
        if (nodes.Count == 0) return new QuietLayoutResult(positions, MinPitch, MinNodeSize, 1);

        // Bantlar KATMAN sırasına göre (besleme sırası bir bant sırası vaadi değildir); bant İÇİNDE giriş
        // sırası, yani build-order korunur. Boş katman indeksleri hiç bant üretmez.
        var byLayer = new SortedDictionary<int, List<string>>();
        foreach (var node in nodes)
        {
            if (!byLayer.TryGetValue(node.Layer, out var band)) byLayer[node.Layer] = band = [];
            band.Add(node.Name);
        }
        var bands = byLayer.Values.ToList();

        var content = ContentSize(panel);
        var (pitch, columns) = ResolvePitch(content, [.. bands.Select(band => band.Count)]);

        double rowCursor = 0;
        for (int bandIndex = 0; bandIndex < bands.Count; bandIndex++)
        {
            var band = bands[bandIndex];
            int rows = (int)Math.Ceiling(band.Count / (double)columns);
            for (int row = 0; row < rows; row++)
            {
                int start = row * columns;
                int count = Math.Min(columns, band.Count - start);
                // Eksik kalan son satır yatayda ORTALANIR.
                double offsetX = (columns - count) / 2.0 * pitch;
                for (int column = 0; column < count; column++)
                    positions[band[start + column]] = new Point(
                        offsetX + (column + 0.5) * pitch,
                        (rowCursor + row + 0.5) * pitch);
            }
            rowCursor += rows + (bandIndex < bands.Count - 1 ? BandGapPitches : 0);
        }

        // Blok, hesap alanında ortalanır.
        double x0 = double.PositiveInfinity, x1 = double.NegativeInfinity;
        double y0 = double.PositiveInfinity, y1 = double.NegativeInfinity;
        foreach (var point in positions.Values)
        {
            x0 = Math.Min(x0, point.X); x1 = Math.Max(x1, point.X);
            y0 = Math.Min(y0, point.Y); y1 = Math.Max(y1, point.Y);
        }
        double shiftX = content.Width / 2 - (x0 + x1) / 2;
        double shiftY = content.Height / 2 - (y0 + y1) / 2;
        foreach (string name in positions.Keys.ToList())
        {
            var point = positions[name];
            positions[name] = new Point(point.X + shiftX, point.Y + shiftY);
        }

        return new QuietLayoutResult(
            positions, pitch, Math.Clamp(pitch * NodeSizeFactor, MinNodeSize, MaxNodeSize), columns);
    }
}
