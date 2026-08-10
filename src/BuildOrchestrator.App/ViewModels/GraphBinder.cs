using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [D5/T50] VM'in topolojisini + satır VM'lerini <see cref="GraphView"/>'in beslemesine (<see cref="GraphNode"/>/
/// <see cref="GraphEdge"/>) çeviren SAF çekirdek — WPF/process bağımsız, tek başına test edilir. <c>GraphView</c>
/// yalnız TÜKETİCİDİR (yeniden yazılmaz); D5'in işi besleme.
///
/// <para><b>Anahtarlar (kritik):</b> <see cref="GraphView"/> düğümleri <see cref="GraphNode.Name"/> ile,
/// <see cref="GraphLayout"/> konumları yine <c>Name</c> ile anahtarlar — bu yüzden <see cref="GraphEdge.From"/>/
/// <see cref="GraphEdge.To"/> proje Id'si DEĞİL, düğüm <b>Adı</b>dır. <see cref="ProjectNode.Dependencies"/> ise
/// üretici projectId'lerdir (Id = tam csproj yolu); kenar üretilirken Id→Ad çözülür.</para>
///
/// <para><b>Statü otoritesi:</b> <see cref="StatusOf"/> eşlemeyi YENİDEN yazmaz — satır varsa
/// <see cref="ProjectRowViewModel.Status"/>'a delege eder (State/InCycle/WillBuild/IsRunActive'in TEK eşleme
/// yeri). İkinci bir otorite (çift switch) bir review kusuru olurdu.</para>
/// </summary>
public static class GraphBinder
{
    /// <summary>Topolojiyi graf düğümlerine çevirir (build-order KORUNUR — bant-içi sıra da build-order'dır).
    /// Katman = <see cref="LayerOf"/>, statü = <see cref="StatusOf"/> (satır Id ile eşlenir).
    ///
    /// <para>[quiet] Kısa-ad öneki ve dep-hata bayrağı ARTIK TAŞINMAZ: v1.3.0 §2.3'te düğümün üstünde ad
    /// etiketi ve graf içi dep-issue rozeti yoktur, dolayısıyla grafın ikisini de okuyan bir yeri kalmadı.
    /// (Önek otoritesi <see cref="GraphNode.CommonDotPrefix"/> olarak duruyor — onu liste kartı ve şerit
    /// okuyor.)</para></summary>
    public static IReadOnlyList<GraphNode> Nodes(
        IReadOnlyList<ProjectNode> topology, IReadOnlyDictionary<string, ProjectRowViewModel> rows)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(rows);

        var depth = TopologicalDepths(topology);

        var result = new List<GraphNode>(topology.Count);
        foreach (var node in topology)
        {
            rows.TryGetValue(node.Id, out var row);
            // Elde topoloji varsa Sync yapılmıştır (bu metot yalnız o zaman çağrılır) → synced: true.
            var status = StatusOf(row, node.InCycle, synced: true);
            // [Task 5] Üyelik de StatusOf'un savunmacı dalıyla AYNI desen: satır varsa TEK otorite (row.InCycle),
            // yoksa (topoloji düğümünün henüz satırı yok) topolojinin kendi bayrağı.
            bool inCycle = row?.InCycle ?? node.InCycle;
            result.Add(new GraphNode(node.Name, LayerOf(node, depth), status, inCycle));
        }
        return result;
    }

    /// <summary>Bağımlılık kenarları: her düğüm N ve her <c>depId</c> için, <c>depId</c> topolojide bir düğüm D'ye
    /// çözülüyorsa <c>GraphEdge(From: D.Name, To: N.Name)</c> (bağımlılık→bağımlı). Topolojide olmayan dep atlanır.
    /// Build-order KORUNUR (topoloji sırasında gezilir).</summary>
    public static IReadOnlyList<GraphEdge> Edges(IReadOnlyList<ProjectNode> topology)
    {
        ArgumentNullException.ThrowIfNull(topology);

        var nameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in topology) nameById[node.Id] = node.Name;

        var edges = new List<GraphEdge>();
        foreach (var node in topology)
            foreach (var depId in node.Dependencies)
                if (nameById.TryGetValue(depId, out var fromName))
                    edges.Add(new GraphEdge(fromName, node.Name));
        return edges;
    }

    /// <summary>Her düğümün topolojik derinliği (Id → derinlik): bağımsız (topolojideki dep'i olmayan) düğüm 0,
    /// aksi halde <c>1 + max(dep derinlikleri)</c>. Cycle'a karşı güvenli: geri kenar (ziyaret edilmekte olan
    /// düğüme dönüş) 0 katkı sayılır → sonlu derinlik. Topoloji-dışı dep'ler yok sayılır. DAG için standart
    /// en-uzun-yol; cycle üyeleri geri-kenar kırpmasıyla sonlu (paylaşımlı-derinlik SCC'si GEREKMEZ — burada
    /// yalnız katman yerleşimi için; cycle STATÜSÜ <see cref="StatusOf"/>'tan gelir).</summary>
    public static IReadOnlyDictionary<string, int> TopologicalDepths(IReadOnlyList<ProjectNode> topology)
    {
        ArgumentNullException.ThrowIfNull(topology);

        var byId = new Dictionary<string, ProjectNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in topology) byId[node.Id] = node;

        var depth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int Depth(string id)
        {
            if (depth.TryGetValue(id, out int cached)) return cached;
            if (!byId.TryGetValue(id, out var node)) return 0; // topoloji-dışı — çağrılmamalı ama savunmacı
            if (!visiting.Add(id)) return 0;                   // geri kenar (cycle) → 0 katkı, özyinelemeyi kır

            int best = -1;
            foreach (var depId in node.Dependencies)
                if (byId.ContainsKey(depId))
                    best = Math.Max(best, Depth(depId));

            visiting.Remove(id);
            int result = best < 0 ? 0 : best + 1;
            depth[id] = result;
            return result;
        }

        foreach (var node in topology) Depth(node.Id);
        return depth;
    }

    /// <summary>Düğümün katmanı: açık <see cref="ProjectNode.LayerIndex"/> varsa o (katman patternleri
    /// yapılandırılmış); yoksa <paramref name="topoDepth"/>'ten topolojik derinlik (<see cref="TopologicalDepths"/>).</summary>
    public static int LayerOf(ProjectNode node, IReadOnlyDictionary<string, int> topoDepth)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(topoDepth);
        return node.LayerIndex ?? (topoDepth.TryGetValue(node.Id, out int d) ? d : 0);
    }

    /// <summary>Graf statüsü — eşleme TEK otoriteden (<see cref="ProjectRowViewModel.Status"/>) gelir:
    /// <list type="bullet">
    /// <item>Sync yapılmamışsa (<paramref name="synced"/> false) her şey <see cref="GraphStatus.Discovered"/>.</item>
    /// <item>Satır yoksa (topoloji düğümünün henüz satırı yok — savunmacı): <paramref name="inCycle"/> ise
    /// <see cref="GraphStatus.Cycle"/>, değilse <see cref="GraphStatus.Discovered"/>.</item>
    /// <item>Satır varsa doğrudan <see cref="ProjectRowViewModel.Status"/> (State+InCycle+WillBuild+IsRunActive'in
    /// tek eşleme yeri — cycle/queued dahil). Burada eşleme KOPYALANMAZ.</item>
    /// </list></summary>
    public static GraphStatus StatusOf(ProjectRowViewModel? row, bool inCycle, bool synced)
    {
        if (!synced) return GraphStatus.Discovered;
        if (row is null) return inCycle ? GraphStatus.Cycle : GraphStatus.Discovered;
        return row.Status;
    }
}
