using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [T53/T54-UI] Proje satırlarını katman gruplarına böler. <b>TEK gruplama kaynağı topolojidir</b>
/// (<see cref="ProjectNode.LayerName"/>/<see cref="ProjectNode.LayerIndex"/>) — App'te katman regex'i YOKTUR
/// (regex yalnız Core'da; mimari kural, <c>LayerGroupingTests</c> pinler). Saf/statik: WPF'siz test edilir.
///
/// <para>Hiçbir düğümün <c>LayerName</c>'i yoksa (tümü null/boş) → tek isimsiz grup = düz build-order
/// (StickyLayerList'te başlıksız). Aksi halde satırlar <c>LayerName</c>'e göre gruplanır; grup sırası
/// ilk-görülme (= build-order = <c>LayerIndex</c>) sırasıdır. Bir düğümün adı olmayan satırları (null/boş
/// <c>LayerName</c>) tek bir isimsiz gruba düşer.</para>
///
/// <para><b>Grafın bantları da buradan türer</b> (<see cref="BandOf"/>): graf listenin EŞLENİĞİDİR — her bant
/// listedeki bir grubun aynısıdır (aynı üyeler, aynı sıra). İkisi aynı bölme kuralını (<see cref="Partition"/>)
/// paylaşır; graf için ikinci bir gruplama kuralı yazılmaz. Katman tanımı yokken liste tek düz listedir, graf da
/// tek bant.</para>
/// </summary>
public static class LayerGrouping
{
    /// <summary>Bir katman grubu — <paramref name="Name"/> null ise başlıksız (düz liste / isimsiz katman).</summary>
    public sealed record Group(string? Name, IReadOnlyList<ProjectRowViewModel> Rows);

    public static IReadOnlyList<Group> Build(
        IReadOnlyList<ProjectRowViewModel> rows, IReadOnlyList<ProjectNode> topology)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(topology);

        var layerById = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        bool anyNamed = false;
        foreach (var node in topology)
        {
            layerById[node.Id] = node.LayerName;
            if (!string.IsNullOrEmpty(node.LayerName)) anyNamed = true;
        }

        // Hiç katman adı yok → düz build-order (tek isimsiz grup). Sync yapılmamış/topoloji boş da buraya düşer.
        if (!anyNamed)
            return [new Group(null, rows.ToList())];

        return Partition(rows, row => layerById.TryGetValue(row.Id, out var name) ? name : null)
            .Select(bucket => new Group(bucket.Key.Length == 0 ? null : bucket.Key, bucket.Members))
            .ToList();
    }

    /// <summary>Grafın bantları: düğüm Id → listede düşeceği grubun SIRASI (0'dan; üstteki grup 0). Bant içi sıra
    /// topolojinin sırası, yani build-order'dır. Liste <see cref="Build"/>'in, graf bunun sonucunu çizer — ikisi
    /// <see cref="Partition"/>'ı paylaştığı için bir proje listede hangi gruptaysa grafta o gruba karşılık gelen
    /// banttadır.</summary>
    public static IReadOnlyDictionary<string, int> BandOf(IReadOnlyList<ProjectNode> topology)
    {
        ArgumentNullException.ThrowIfNull(topology);

        var buckets = Partition(topology, node => node.LayerName);
        var bands = new Dictionary<string, int>(topology.Count, StringComparer.OrdinalIgnoreCase);
        for (int band = 0; band < buckets.Count; band++)
            foreach (var node in buckets[band].Members) bands[node.Id] = band;
        return bands;
    }

    /// <summary>TEK bölme kuralı: öğeler katman ADINA göre kovalara ayrılır; adı olmayanlar (null/boş) tek bir
    /// isimsiz kovada (<c>""</c>) toplanır. Kova sırası ilk görülme (build-order) sırasıdır, kova içinde giriş sırası
    /// korunur. Ad, indeks DEĞİL: aynı adı taşıyan iki katman tek grup olur — liste böyle gösterir.</summary>
    private static List<(string Key, List<T> Members)> Partition<T>(IEnumerable<T> items, Func<T, string?> layerNameOf)
    {
        var buckets = new List<(string Key, List<T> Members)>();
        var byKey = new Dictionary<string, List<T>>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            string key = layerNameOf(item) ?? "";
            if (!byKey.TryGetValue(key, out var members))
            {
                byKey[key] = members = [];
                buckets.Add((key, members));
            }
            members.Add(item);
        }
        return buckets;
    }
}
