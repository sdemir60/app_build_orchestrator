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

    /// <summary>[A1→D7 fold] Kullanıcı pattern'leri Settings editöründe (D7) serbestçe yazılır. Regex'in
    /// KENDİSİ geçerli (derlenebilir) olsa bile katastrofik-backtracking bir pattern (ör. <c>(a+)+$</c>)
    /// <see cref="Regex.IsMatch(string)"/>'te planlamayı SONSUZA DEK asabilir. Her kullanıcı regex'i bu SINIRLI
    /// per-IsMatch üst sınırıyla derlenir; süre aşılırsa <see cref="RegexMatchTimeoutException"/> fırlar ve
    /// aşağıda non-match olarak ele alınır. Değer bir design token DEĞİL — bu fold'a özgü bir güvenlik sabiti:
    /// meşru pattern'ler kısa assembly adlarına karşı mikrosaniyelerde biter, bu tavana yalnız patolojik
    /// backtracking yaklaşır (bkz. task-D7). Off-token değer → kaynak-atıflı adlandırılmış sabit (CLAUDE.md).</summary>
    public static readonly TimeSpan UserRegexMatchTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>[A1→D7 fold] Bir kullanıcı pattern'ini SINIRLI <see cref="UserRegexMatchTimeout"/> ile derler —
    /// hem LayerEngine hem App'in Settings compile-check'i (D7) AYNI ctor'u kullansın diye TEK yer. Geçersiz
    /// pattern <see cref="RegexParseException"/> (bir <see cref="ArgumentException"/>) fırlatır — bu, planlamada
    /// planFailed yolunu tetikleyen mevcut davranıştır (timeout'tan FARKLI ve korunur).</summary>
    public static Regex CompileUserPattern(string pattern) =>
        new(pattern, RegexOptions.None, UserRegexMatchTimeout);

    /// <summary>[D7] Settings editörünün Save-validation compile-check'i: pattern derlenebiliyor mu (boş pattern
    /// = geçerli). <see cref="CompileUserPattern"/> ile AYNI ctor — davranış tek yerde.</summary>
    public static bool IsPatternCompilable(string pattern)
    {
        try { _ = CompileUserPattern(pattern); return true; }
        catch (RegexParseException) { return false; }
    }

    public static LayerAssignmentResult AssignLayers(
        IReadOnlyList<ProjectNode> nodesInBuildOrder, IReadOnlyList<LayerPattern> patterns)
    {
        ArgumentNullException.ThrowIfNull(nodesInBuildOrder);
        ArgumentNullException.ThrowIfNull(patterns);

        if (patterns.Count == 0)
            return new LayerAssignmentResult(nodesInBuildOrder, []);

        var ordered = patterns.OrderBy(p => p.Order).ToList();
        // [A1→D7 fold] Geçersiz regex burada RegexParseException fırlatır → planFailed (mevcut yol, korunur).
        var compiled = ordered.Select(p => (p.Order, p.Name, Regex: CompileUserPattern(p.Regex))).ToList();
        int otherLayerIndex = ordered.Max(p => p.Order) + 1;

        // [A1→D7 fold] Timeout'a giren pattern adları — her biri için bir kez warn-only uyarı üretilir (spam yok).
        var timedOutPatterns = new HashSet<string>(StringComparer.Ordinal);
        var byId = new Dictionary<string, (int LayerIndex, string LayerName)>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodesInBuildOrder)
        {
            (int Order, string Name)? match = null;
            foreach (var c in compiled)
            {
                bool isMatch;
                try { isMatch = c.Regex.IsMatch(n.Name); }
                catch (RegexMatchTimeoutException)
                {
                    // [A1→D7 fold] Katastrofik pattern süreyi aştı → bu pattern için NON-MATCH kabul edilir
                    // (sıradaki pattern denenir; hiçbiri tutmazsa proje Other'a düşer). Asla asmaz/patlamaz.
                    timedOutPatterns.Add(c.Name);
                    continue;
                }
                if (isMatch) { match = (c.Order, c.Name); break; }
            }
            byId[n.Id] = match is { } m ? (m.Order, m.Name) : (otherLayerIndex, OtherLayerName);
        }

        var warnings = new List<string>();
        foreach (var name in timedOutPatterns)
            warnings.Add(
                $"layer pattern '{name}' match timed out ({(int)UserRegexMatchTimeout.TotalMilliseconds} ms) — " +
                "treated as non-match (check for catastrophic backtracking)");
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
