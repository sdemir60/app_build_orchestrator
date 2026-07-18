namespace BuildOrchestrator.Core.Planning;

using System.Text.RegularExpressions;
using BuildOrchestrator.Contracts.Model;

/// <summary>[T15][A6/N8] AssignLayers sonucu: sert faz bariyerine göre yeniden sıralanmış Nodes (LayerIndex/
/// LayerName uygulanmış, BuildOrder yeni pozisyona göre yeniden numaralanmış) + ters katman bağımlılığı
/// uyarıları. Warnings, warn-only DATA'dır — hiçbir alan bunları okuyup bloklama/yeniden sıralama yapmaz.</summary>
public sealed record LayerAssignmentResult(IReadOnlyList<ProjectNode> Nodes, IReadOnlyList<string> Warnings);

/// <summary>
/// [T15][A6/N8] Katman ataması: sıralı regex+isim pattern listesine göre her projeye (LayerIndex, LayerName)
/// atar ve sert faz bariyerini (Layer N tümü, Layer N+1 başlamadan) uygular.
///
/// Eşleşme hedefi: <see cref="ProjectNode.Name"/> (AssemblyName türevi kısa ad) — Id (tam csproj yolu) değil;
/// pattern'ler kullanıcı tarafından yazılır ve assembly adı üzerinden düşünülür (bkz. LayerPattern dokümantasyonu).
///
/// Order alanı ÇİFT görev görür: (1) eşleşme önceliği (küçük Order önce denenir, ilk eşleşen kazanır),
/// (2) atanan katmanın LayerIndex'i. Eşleşmeyen projeler "Other" katmanına düşer — index = tüm pattern
/// Order'larının max'ından bir fazla (böylece her zaman son katmandan sonra gelir).
///
/// Sert faz bariyeri: dönen Nodes, (LayerIndex, orijinal build-order pozisyonu) anahtarına göre sıralanır —
/// LINQ OrderBy'ın stabilite garantisi sayesinde katman-içi orijinal topo sırası korunur (ekstra ThenBy
/// gerekmez, çünkü <paramref name="nodesInBuildOrder"/> zaten o sırada gelir). BuildOrder alanı yeni
/// pozisyona göre yeniden numaralanır — Nodes[i].BuildOrder == i değişmezi (BuildPlanBuilder'ın orijinal
/// invaryantı) korunur.
///
/// Boş pattern listesi → layering devre dışı: Nodes aynen (aynı sıra, LayerIndex/LayerName dokunulmamış =
/// zaten null) döner, Warnings boş — mevcut (Task 15 öncesi) davranışla birebir aynı.
///
/// Ters katman bağımlılığı [warn-only]: bir proje P (layer i), bağımlılığı olan bir üretici Q'nun (layer j)
/// j &gt; i olduğu durumda — yani P'nin ürettiği bağımlılık P'den SONRAKİ bir katmanda — bu BLOKLANMAZ veya
/// "düzeltilmek üzere" yeniden sıralanmaz; yalnız <see cref="LayerAssignmentResult.Warnings"/>'e insan-okunur
/// bir satır eklenir. Sert bariyer aynen uygulanır (P, katmanı gereği Q'dan önce çıkar) — bu durumda P'nin
/// kendi bağımlılığından önce dispatch edilebilir hâle gelmesi, warn-only tasarımın kasıtlı (düzeltilmeyen)
/// sonucudur; gerçek düzeltme kullanıcının pattern'leri/projeleri gözden geçirmesidir.
/// </summary>
public static class LayerEngine
{
    public const string OtherLayerName = "Other";

    public static LayerAssignmentResult AssignLayers(
        IReadOnlyList<ProjectNode> nodesInBuildOrder, IReadOnlyList<LayerPattern> patterns)
    {
        ArgumentNullException.ThrowIfNull(nodesInBuildOrder);
        ArgumentNullException.ThrowIfNull(patterns);

        if (patterns.Count == 0)
            return new LayerAssignmentResult(nodesInBuildOrder, []);

        var ordered = patterns.OrderBy(p => p.Order).ToList();
        var compiled = ordered.Select(p => (p.Order, p.Name, Regex: new Regex(p.Regex))).ToList();
        int otherLayerIndex = ordered.Max(p => p.Order) + 1;

        var byId = new Dictionary<string, (int LayerIndex, string LayerName)>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodesInBuildOrder)
        {
            var match = compiled.FirstOrDefault(c => c.Regex.IsMatch(n.Name));
            byId[n.Id] = match.Regex is not null ? (match.Order, match.Name) : (otherLayerIndex, OtherLayerName);
        }

        var warnings = new List<string>();
        foreach (var n in nodesInBuildOrder)
        {
            var (layer, layerName) = byId[n.Id];
            foreach (var depId in n.Dependencies)
            {
                if (byId.TryGetValue(depId, out var dep) && dep.LayerIndex > layer)
                {
                    warnings.Add(
                        $"reverse layer dependency: '{n.Name}' (layer {layer} '{layerName}') depends on " +
                        $"producer '{depId}' (layer {dep.LayerIndex} '{dep.LayerName}')");
                }
            }
        }

        var reordered = nodesInBuildOrder
            .Select(n => n with { LayerIndex = byId[n.Id].LayerIndex, LayerName = byId[n.Id].LayerName })
            .OrderBy(n => n.LayerIndex) // stabil: girdi zaten topo/build-order'da, katman-içi sıra korunur
            .Select((n, i) => n with { BuildOrder = i })
            .ToList();

        return new LayerAssignmentResult(reordered, warnings);
    }
}
