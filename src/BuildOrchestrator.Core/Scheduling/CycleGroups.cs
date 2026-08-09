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

        var orderOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in plan.Nodes) orderOf[node.Id] = node.BuildOrder;

        var byMember = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        foreach (var scc in plan.Cycles)
        {
            // Plan'da bulunmayan üye (savunmacı) sona düşer — sıra yine deterministiktir.
            var ordered = scc.OrderBy(id => orderOf.TryGetValue(id, out int o) ? o : int.MaxValue)
                             .ToList();
            if (ordered.Count == 0) continue;
            count++;
            foreach (string id in ordered) byMember[id] = ordered;
        }

        return new CycleGroups(byMember, count);
    }

    public bool IsMember(string projectId) => _byMember.ContainsKey(projectId);

    /// <summary>Bu projenin SCC üyeleri, build-order sıralı. Üye değilse boş liste.</summary>
    public IReadOnlyList<string> MembersOf(string projectId) =>
        _byMember.TryGetValue(projectId, out var members) ? members : [];
}
