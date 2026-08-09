namespace BuildOrchestrator.Core.Scheduling;

using BuildOrchestrator.Contracts.Model;

/// <summary>
/// SCC üyelik haritası — <see cref="BuildPlan.Cycles"/>'ın BUILD-ORDER'a çevrilmiş hâli.
///
/// Neden ayrı bir tip: plan.Cycles üyeleri ORDİNAL sıralı verir (TopoSort determinizmi), ama hem scheduler'ın
/// grup dispatch'i hem coordinator'ın tur döngüsü BUILD-ORDER ister. Bu dönüşüm iki yerde tekrarlanırsa
/// sıralar sessizce ayrışabilir (kopya YASAK) — tek kaynak burasıdır.
///
/// Saf Core state: I/O, process, async, log YOK [D3].
/// </summary>
public sealed class CycleGroups
{
    private readonly Dictionary<string, IReadOnlyList<string>> _byMember;

    private CycleGroups(Dictionary<string, IReadOnlyList<string>> byMember, int count)
    {
        _byMember = byMember;
        Count = count;
    }

    /// <summary>Plandaki SCC sayısı (tek üyeli bileşenler zaten Cycles'a girmez).</summary>
    public int Count { get; }

    public static CycleGroups From(BuildPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return From(plan.Nodes, plan.Cycles);
    }

    /// <summary>
    /// Aynı harita, plan yerine düğüm + SCC listesinden. App'in elinde <see cref="BuildPlan"/> YOKTUR —
    /// topoloji olayı (<c>WorkspaceTopologyEvent</c>) aynı iki listeyi ayrı ayrı taşır; ikinci bir üyelik
    /// haritası kurmak yerine aynı gövde kullanılır (kopya YASAK, CLAUDE.md).
    /// </summary>
    public static CycleGroups From(IReadOnlyList<ProjectNode> nodes, IReadOnlyList<IReadOnlyList<string>> cycles)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(cycles);

        var orderOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes) orderOf[node.Id] = node.BuildOrder;

        var byMember = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        foreach (var scc in cycles)
        {
            // Plan'da bulunmayan üye (savunmacı) sona düşer — sıra yine deterministiktir.
            // Liste SALT-OKUR sarmalanır: aynı örnek grubun TÜM üyelerine dağıtıldığı için, bir tüketicinin
            // onu IList'e cast edip değiştirmesi diğer her üyenin görüşünü sessizce bozardı.
            IReadOnlyList<string> ordered = scc
                .OrderBy(id => orderOf.TryGetValue(id, out int o) ? o : int.MaxValue)
                .ToList()
                .AsReadOnly();
            if (ordered.Count == 0) continue;
            count++;
            foreach (string id in ordered) byMember[id] = ordered;
        }

        return new CycleGroups(byMember, count);
    }

    /// <summary>
    /// [I4] Bir SCC'nin BİLEŞİK İMZA TEMSİLCİSİ — grubun imzası hangi üyeden okunacaksa o. Seçim ORDİNAL
    /// EN KÜÇÜK üyedir: girdi listesinin sırasından (build-order mu, ordinal mi) BAĞIMSIZ olduğu için aynı
    /// SCC'yi farklı sıralarda tutan iki taraf (imzayı YAZAN tur döngüsü build-order'da, OKUYAN pre-skip
    /// <c>plan.Cycles</c>'ta ordinal'de) AYNI üyede buluşur. İki taraf kendi <c>[0]</c>'ını seçtiğinde
    /// üye-başına imzanın farklılaştığı modda (Fast) sessizce ayrışırlardı.
    /// Boş liste ⇒ <c>null</c> (temsilci yok).
    /// </summary>
    public static string? SignatureRepresentative(IReadOnlyList<string> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        return members.Count == 0 ? null : members.Min(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsMember(string projectId) => _byMember.ContainsKey(projectId);

    /// <summary>Bu projenin SCC üyeleri, build-order sıralı ve SALT-OKUR. Üye değilse boş liste.</summary>
    public IReadOnlyList<string> MembersOf(string projectId) =>
        _byMember.TryGetValue(projectId, out var members) ? members : [];
}
