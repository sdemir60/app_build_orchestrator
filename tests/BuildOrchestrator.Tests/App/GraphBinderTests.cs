using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T50/D5] <see cref="GraphBinder"/> — topoloji + satır VM'lerini graf besleme modeline (<see cref="GraphNode"/>/
/// <see cref="GraphEdge"/>) çeviren SAF çekirdek. Kenar yönü, katman (LayerIndex ?? topolojik derinlik), statü
/// eşlemesi (TEK otorite <see cref="ProjectRowViewModel.Status"/>), katman-içi build-order sırası ve veri-türevli
/// kısa-ad öneki burada pinlenir. WPF'siz.
/// </summary>
public class GraphBinderTests
{
    private static string Id(string name) => $@"C:\repo\{name}.csproj";

    private static ProjectNode Node(string name, string[] deps, int? layerIndex = null, bool inCycle = false, int buildOrder = 0) =>
        new(Id(name), name, Id(name), SolutionNames: [], Dependencies: [.. deps.Select(Id)],
            BuildOrder: buildOrder, LayerIndex: layerIndex, LayerName: null, InCycle: inCycle, WillBuild: null);

    private static IReadOnlyDictionary<string, ProjectRowViewModel> RowsFor(IReadOnlyList<ProjectNode> topology)
    {
        var dict = new Dictionary<string, ProjectRowViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in topology)
            dict[n.Id] = new ProjectRowViewModel(n.Id, n.Name, ProjectRowState.Pending) { InCycle = n.InCycle };
        return dict;
    }

    private static int IndexOf(IReadOnlyList<GraphNode> nodes, string name)
    {
        for (int i = 0; i < nodes.Count; i++) if (nodes[i].Name == name) return i;
        return -1;
    }

    [Fact]
    public void Edges_point_from_dependency_to_dependent()
    {
        var topology = new[]
        {
            Node("Base", []),
            Node("Data.Core", ["Base"]),
            Node("Server.Api", ["Data.Core", "External"]), // "External" topolojide YOK → kenar üretmez
        };

        var edges = GraphBinder.Edges(topology);

        // From = bağımlılık (producer) adı, To = bağımlı (consumer) adı — GraphEdge sözleşmesi (yukarıdan aşağı).
        Assert.Contains(edges, e => e.From == "Base" && e.To == "Data.Core");
        Assert.Contains(edges, e => e.From == "Data.Core" && e.To == "Server.Api");
        Assert.DoesNotContain(edges, e => e.From == "External"); // topoloji-dışı dep atlanır
        Assert.Equal(2, edges.Count);
    }

    [Fact]
    public void Layer_falls_back_to_topological_depth_when_no_layer_patterns_are_configured()
    {
        // LayerIndex hepsi null (katman patterni yok) → derinlik: Base 0, Data 1, Api/Portal 2.
        var topology = new[]
        {
            Node("Base", []),
            Node("Data.Core", ["Base"]),
            Node("Server.Api", ["Data.Core"]),
            Node("Web.Portal", ["Data.Core"]),
        };

        var depth = GraphBinder.TopologicalDepths(topology);
        Assert.Equal(0, depth[Id("Base")]);
        Assert.Equal(1, depth[Id("Data.Core")]);
        Assert.Equal(2, depth[Id("Server.Api")]);
        Assert.Equal(2, depth[Id("Web.Portal")]);

        // LayerOf: LayerIndex yoksa derinliğe düşer...
        Assert.Equal(2, GraphBinder.LayerOf(topology[2], depth));
        // ...ama LayerIndex VARSA o kazanır (fallback değil).
        Assert.Equal(5, GraphBinder.LayerOf(Node("Pinned", ["Base"], layerIndex: 5), depth));

        // Uçtan uca aynı katmanlar Nodes() çıktısında.
        var nodes = GraphBinder.Nodes(topology, RowsFor(topology));
        Assert.Equal(0, nodes.Single(n => n.Name == "Base").Layer);
        Assert.Equal(1, nodes.Single(n => n.Name == "Data.Core").Layer);
        Assert.Equal(2, nodes.Single(n => n.Name == "Web.Portal").Layer);
    }

    [Fact]
    public void Cycle_members_get_finite_back_edge_trimmed_depths_not_a_shared_component_depth()
    {
        // [D5 fix wave/Fix 4] TopologicalDepths döküman notu (brief satır 18) cycle üyelerinin "paylaşımlı
        // component derinliği" taşıdığını söyler; GERÇEK davranış farklıdır — SCC-paylaşımlı DEĞİL, geri-kenar
        // (0 katkı) ile KIRPILMIŞ sonlu derinliktir (bkz. GraphBinder.TopologicalDepths dokümantasyonu). Bu KABUL
        // EDİLEN davranış (D5 kapsamı: sonlu derinlik katman-yerleşimi için yeterli — SCC-paylaşımlı derinlik
        // Minor/görsel bir konu, yalnız katman patterni yokken devreye girer; cycle STATÜSÜ StatusOf'tan ayrı ve
        // doğru — bkz. Cycle_members_are_reported_as_cycle_status). Bu test brief'in ifadesinden BİLİNÇLİ sapmayı
        // AÇIK ve KİLİTLİ hale getirir: A→B→C→A 3-düğümlü cycle'da üç üye ÜÇ FARKLI derinlik alır (paylaşımlı
        // TEK derinlik DEĞİL) — değerler gerçek koddan okunmuştur (A=3, B=2, C=1; TEMP probe ile doğrulanmıştır),
        // varsayılmamıştır.
        var topology = new[]
        {
            Node("A", ["B"], inCycle: true), // A, B'ye bağımlı
            Node("B", ["C"], inCycle: true), // B, C'ye bağımlı
            Node("C", ["A"], inCycle: true), // C, A'ya bağımlı → geri kenar (cycle'ı kapatır)
        };

        var depth = GraphBinder.TopologicalDepths(topology);

        Assert.Equal(3, depth[Id("A")]);
        Assert.Equal(2, depth[Id("B")]);
        Assert.Equal(1, depth[Id("C")]);
        // Üç farklı değer → SCC-paylaşımlı derinlik DEĞİL (brief'ten bilinçli sapma, yukarıda belgelenmiştir).
        Assert.NotEqual(depth[Id("A")], depth[Id("B")]);
        Assert.NotEqual(depth[Id("B")], depth[Id("C")]);
    }

    [Fact]
    public void Cycle_members_are_reported_as_cycle_status()
    {
        // StatusOf: row null + inCycle → Cycle; sync öncesi her şey Discovered (cycle olsa bile).
        Assert.Equal(GraphStatus.Cycle, GraphBinder.StatusOf(null, inCycle: true, synced: true));
        Assert.Equal(GraphStatus.Discovered, GraphBinder.StatusOf(null, inCycle: true, synced: false));

        // Row VARSA statü TEK otoriteden (row.Status) gelir — cycle üyesi satır (InCycle + Pending) Cycle döner
        // (StatusOf cycle'ı YENİDEN eşlemez; row.Status'a delege eder — çift otorite YASAK).
        var cycleRow = new ProjectRowViewModel(Id("X"), "X", ProjectRowState.Pending) { InCycle = true };
        Assert.Equal(GraphStatus.Cycle, GraphBinder.StatusOf(cycleRow, inCycle: true, synced: true));

        // [D5 fix wave/Fix 3] Guard — delegasyon kanıtı: satır varsa `inCycle` argümanı YOK SAYILMALI (ikinci bir
        // "if (inCycle) return Cycle" kısayolu YASAK). State=Started olan (row.Status==Building) bir satır,
        // inCycle:true ile çağrılsa BİLE Cycle DEĞİL Building dönmeli — aksi halde StatusOf ikinci bir otorite
        // taşıyor demektir.
        var startedInCycleRow = new ProjectRowViewModel(Id("Z"), "Z", ProjectRowState.Started) { InCycle = true };
        Assert.Equal(GraphStatus.Building, GraphBinder.StatusOf(startedInCycleRow, inCycle: true, synced: true));

        // Uçtan uca: cycle düğümü grafta Cycle; cycle-dışı düğüm değil.
        var topology = new[] { Node("X", [], inCycle: true), Node("Y", ["X"]) };
        var nodes = GraphBinder.Nodes(topology, RowsFor(topology));
        Assert.Equal(GraphStatus.Cycle, nodes.Single(n => n.Name == "X").Status);
        Assert.NotEqual(GraphStatus.Cycle, nodes.Single(n => n.Name == "Y").Status);
    }

    // [quiet · SİLİNDİ] `Nodes_source_the_dep_badge_from_row_HasDepIssue` — v1.3.0 §2.3 "Kaldırılanlar" graf
    // içi dep-issue rozetini kaldırdı (dep bilgisi liste kartlarında yaşıyor), dolayısıyla GraphNode artık
    // HasDepIssue taşımıyor ve binder'ın onu üretecek bir sebebi yok. Bayrağın kendisi
    // ProjectRowViewModel.HasDepIssue olarak duruyor ve ProjectRowTests'te pinli.

    /// <summary>[Task 5] <c>GraphNode.InCycle</c> — kalıcı köşe rozetinin veri kaynağı. Statünün AKSİNE (satır
    /// yoksa <c>inCycle</c> topoloji bayrağına düşer, StatusOf'un savunmacı dalıyla AYNI desen) burada da satır
    /// varsa <c>row.InCycle</c> otorite, yoksa topolojinin kendi bayrağı.</summary>
    [Fact]
    public void Nodes_pass_the_rows_cycle_membership_through_to_the_graph_node()
    {
        var topology = new[] { Node("X", [], inCycle: true), Node("Y", ["X"]) };
        var rows = RowsFor(topology);

        var nodes = GraphBinder.Nodes(topology, rows);

        Assert.True(nodes.Single(n => n.Name == "X").InCycle);
        Assert.False(nodes.Single(n => n.Name == "Y").InCycle);
    }

    /// <summary>Satır yoksa (topoloji düğümünün henüz satırı yok — savunmacı) üyelik topolojinin KENDİ
    /// bayrağından gelir — <see cref="StatusOf"/>'un row-null dalıyla AYNI desen.</summary>
    [Fact]
    public void Nodes_fall_back_to_the_topology_cycle_flag_when_the_row_is_missing()
    {
        var topology = new[] { Node("X", [], inCycle: true) };

        var nodes = GraphBinder.Nodes(topology, new Dictionary<string, ProjectRowViewModel>(StringComparer.OrdinalIgnoreCase));

        Assert.True(nodes.Single().InCycle);
    }

    [Fact]
    public void Node_order_within_a_layer_follows_build_order()
    {
        // [D5 fix wave/Fix 2] Zeta ve Alpha AYNI katmanda (ikisi de Root'a bağlı → derinlik 1); topoloji
        // build-order'ı BİLEREK alfabetiğin TERSİ (Zeta önce, Alpha sonra) — böylece bu test "aynı katmanda
        // Name'e göre sırala" regresyonunu YAKALAYABİLİR (eski Alpha/Beta örneği alfabetik==build-order olduğu
        // için bunu ayırt edemiyordu).
        var topology = new[]
        {
            Node("Root", []),
            Node("Zeta", ["Root"], buildOrder: 1),
            Node("Alpha", ["Root"], buildOrder: 2),
        };

        var nodes = GraphBinder.Nodes(topology, RowsFor(topology));

        Assert.Equal(nodes.Single(n => n.Name == "Zeta").Layer, nodes.Single(n => n.Name == "Alpha").Layer);
        Assert.True(IndexOf(nodes, "Zeta") < IndexOf(nodes, "Alpha"),
            "aynı katmanda düğüm sırası topolojinin build-order'ını izlemeli (alfabetik DEĞİL — Zeta < Alpha alfabetik olarak yanlış olurdu)");
    }

    /// <summary>
    /// Kısa ad, ortak öneki VERİDEN türeterek atar — hardcode edilmiş bir <c>"OSYS."</c> DEĞİL.
    ///
    /// <para><b>Eski iddia:</b> bu test <c>GraphBinder.Nodes(...)</c>'ın ürettiği <c>GraphNode.ShortName</c>
    /// üzerinden koşuyordu, çünkü kısa ad grafın düğüm etiketiydi. v1.3.0 §2.3 node üstü etiketleri
    /// kaldırdı ⇒ <c>GraphNode</c> artık önek taşımıyor. Kural değişmedi, SAHİBİ değişti: kısa adı hâlâ
    /// liste kartının dep-tooltip'i (<c>ProjectRow</c>) ve şeritteki building chip'leri
    /// (<c>StickyRibbon</c>) kullanıyor, ikisi de aynı iki statik yardımcıyı çağırıyor — test artık
    /// doğrudan onları hedefliyor.</para>
    /// </summary>
    [Fact]
    public void Short_label_strips_the_common_prefix_derived_from_the_data_not_a_hardcoded_one()
    {
        // Adlar OSYS DEĞİL: önek VERİDEN türetilir (Contoso.). Hardcode "OSYS." olsaydı hiçbir şey kırpılmazdı.
        string prefix = GraphNode.CommonDotPrefix(["Contoso.Web", "Contoso.Data.Core"]);
        Assert.Equal("Contoso.", prefix);
        Assert.Equal("Web", GraphNode.ShortLabel("Contoso.Web", prefix));
        Assert.Equal("Data.Core", GraphNode.ShortLabel("Contoso.Data.Core", prefix));

        // Ortak nokta-segmenti yoksa hiç kırpılmaz (tam ad kalır).
        string none = GraphNode.CommonDotPrefix(["Alpha.One", "Beta.Two"]);
        Assert.Equal("", none);
        Assert.Equal("Alpha.One", GraphNode.ShortLabel("Alpha.One", none));
    }
}
