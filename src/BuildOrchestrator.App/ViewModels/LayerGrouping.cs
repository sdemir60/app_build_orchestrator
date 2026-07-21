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

        // "" sentinel'i = isimsiz (null LayerName); grup sırası ilk-görülme (build-order).
        var order = new List<string>();
        var buckets = new Dictionary<string, List<ProjectRowViewModel>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            string key = (layerById.TryGetValue(row.Id, out var name) ? name : null) ?? "";
            if (!buckets.TryGetValue(key, out var list))
            {
                buckets[key] = list = [];
                order.Add(key);
            }
            list.Add(row);
        }

        return order.Select(k => new Group(k.Length == 0 ? null : k, buckets[k])).ToList();
    }
}
