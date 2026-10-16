using System.Windows;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Externals;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T50/D5] <see cref="GraphBinder"/> — topoloji + satır VM'lerini graf besleme modeline (<see cref="GraphNode"/>/
/// <see cref="GraphEdge"/>) çeviren SAF çekirdek. Kenar yönü, bant (listenin grubunun eşleniği), statü
/// eşlemesi (TEK otorite <see cref="ProjectRowViewModel.Status"/>), bant-içi build-order sırası ve veri-türevli
/// kısa-ad öneki burada pinlenir. WPF'siz.
/// </summary>
public class GraphBinderTests
{
    private static string Id(string name) => $@"C:\repo\{name}.csproj";

    private static ProjectNode Node(string name, string[] deps, int? layerIndex = null, bool inCycle = false,
        int buildOrder = 0, bool? willBuild = null, string? layerName = null, bool isExternal = false) =>
        new(Id(name), name, Id(name), SolutionNames: [], Dependencies: [.. deps.Select(Id)],
            BuildOrder: buildOrder, LayerIndex: layerIndex, LayerName: layerName, InCycle: inCycle, WillBuild: willBuild,
            IsExternal: isExternal);

    private static IReadOnlyDictionary<string, ProjectRowViewModel> RowsFor(IReadOnlyList<ProjectNode> topology)
    {
        var dict = new Dictionary<string, ProjectRowViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in topology)
            dict[n.Id] = new ProjectRowViewModel(n.Id, n.Name, ProjectRowState.Pending) { InCycle = n.InCycle };
        return dict;
    }

    /// <summary>Grafın bantları ÜSTTEN ALTA (yerleşimin sırası: bant değeri küçükten büyüğe), her bant üyelerinin
    /// adlarıyla — bant içi sıra besleme sırasıdır.</summary>
    private static string[][] GraphBands(IReadOnlyList<GraphNode> nodes) =>
        [.. nodes.GroupBy(n => n.Layer).OrderBy(g => g.Key).Select(g => g.Select(n => n.Name).ToArray())];

    /// <summary>Listenin grupları ÜSTTEN ALTA, her grup satırlarının adlarıyla — listenin kendi gruplaması
    /// (<see cref="LayerGrouping.Build"/>), satırlar build-order'da (<see cref="RunViewModel.Projects"/> gibi).</summary>
    private static string[][] ListGroups(
        IReadOnlyList<ProjectNode> topology, IReadOnlyDictionary<string, ProjectRowViewModel> rows) =>
        [.. LayerGrouping.Build([.. topology.Select(n => rows[n.Id])], topology)
            .Select(g => g.Rows.Select(r => r.Name).ToArray())];

    /// <summary>Kullanıcının ekran görüntüsünden ölçülen profil (2026-10-12, gerçek OSYS, katman tanımı yok): grafın
    /// üstten alta her bandındaki düğüm sayısı — 28 bant, toplam 177. O sürümde bant topolojik derinlikti.</summary>
    private static readonly int[] MeasuredDepthProfile =
        [17, 12, 14, 10, 6, 3, 3, 3, 3, 13, 9, 17, 19, 8, 4, 5, 4, 5, 2, 1, 5, 1, 1, 4, 4, 2, 1, 1];

    /// <summary>Aynı ekran görüntüsündeki graf zemininin ölçüsü: genişlik başlıktaki sayacın sağ kenarından, yükseklik
    /// seçilen pitch'ten (bu profille 12,5 px yalnız 658–681 px yükseklikte seçilir).</summary>
    private static readonly Size MeasuredPanel = new(1274, 670);

    /// <summary>Profili veren katmansız topoloji: k. derinlikteki her düğüm (k−1). derinliğin ilk düğümüne bağlıdır,
    /// yani derinliği tam k'dir; build-order derinlik sırasıdır.</summary>
    private static ProjectNode[] DeepChainWithoutLayers()
    {
        var nodes = new List<ProjectNode>();
        for (int depth = 0; depth < MeasuredDepthProfile.Length; depth++)
            for (int i = 0; i < MeasuredDepthProfile[depth]; i++)
                nodes.Add(Node($"D{depth:D2}.P{i:D2}", depth == 0 ? [] : [$"D{depth - 1:D2}.P00"], buildOrder: nodes.Count));
        return [.. nodes];
    }

    /// <summary>
    /// [perf Faz B · B5] Bantlar topoloji REFERANSI başına bir kez hesaplanır. Graf her statü değişiminde
    /// <see cref="GraphBinder.Nodes"/>'u yeniden çağırır; topoloji ise yalnız Sync'te değişir (VM yeni bir liste atar,
    /// eskisini değiştirmez). <see cref="GraphBinder.Bands"/>'ı DIŞARIDAN çağıran da aynı önbellekten geçer; aynı
    /// içerikli AMA yeni bir referans yeniden hesaplanır (içerik değil referans anahtardır: topolojiyi yerinde
    /// değiştiren biri olmadığı için ucuz ve güvenli).
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski ad/iddia: <c>The_topological_depths_are_computed_once_per_topology_reference</c> —
    /// önbellekte topolojik derinlik tutuluyordu (her statü itişinde baştan hesaplanan DFS, ölçüm: 25 sn'lik koşuda
    /// 175 ms). Değişme gerekçesi: bant artık derinlik değil listenin grubudur
    /// (<see cref="Without_layers_the_graph_is_one_band_like_the_flat_list"/>); önbellek aynı gerekçeyle bantları tutar.</para>
    /// </summary>
    [Fact]
    public void The_bands_are_computed_once_per_topology_reference()
    {
        var topology = new[] { Node("Base", []), Node("Data.Core", ["Base"]) };
        var rows = RowsFor(topology);
        int before = GraphBinder.BandComputations;

        GraphBinder.Nodes(topology, rows);
        GraphBinder.Nodes(topology, rows);
        GraphBinder.Bands(topology);

        Assert.Equal(before + 1, GraphBinder.BandComputations); // iki Nodes + bir doğrudan Bands çağrısı tek hesaplamayı paylaşır

        var sameContentNewReference = new[] { Node("Base", []), Node("Data.Core", ["Base"]) };
        GraphBinder.Nodes(sameContentNewReference, RowsFor(sameContentNewReference));

        Assert.Equal(before + 2, GraphBinder.BandComputations); // yeni referans → yeniden hesaplanır
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

        // From = bağımlılık (producer), To = bağımlı (consumer) — GraphEdge sözleşmesi (yukarıdan aşağı).
        // [DEĞİŞEN KURAL] Uçlar eskiden düğüm ADIydı. Ad benzersiz değildir (iki kök aynı AssemblyName'i
        // üretebilir) ve Id→Ad çevirisi böyle bir durumda kenarı YANLIŞ düğüme bağlıyordu; uçlar artık Id.
        Assert.Contains(edges, e => e.From == Id("Base") && e.To == Id("Data.Core"));
        Assert.Contains(edges, e => e.From == Id("Data.Core") && e.To == Id("Server.Api"));
        Assert.DoesNotContain(edges, e => e.From == Id("External")); // topoloji-dışı dep atlanır
        Assert.Equal(2, edges.Count);
    }

    /// <summary>
    /// <b>Katman tanımı yoksa graf TEK banttır — liste gibi.</b> Liste katman adı yokken tek, başlıksız, düz
    /// build-order listesidir (<see cref="LayerGrouping"/>); graf onun eşleniğidir: bütün düğümler aynı bantta,
    /// build-order'da (soldan sağa, yukarıdan aşağı okumak build sırasını okumaktır).
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski ad/iddia: <c>Layer_falls_back_to_topological_depth_when_no_layer_patterns_are_configured</c>
    /// — <c>LayerIndex</c> yoksa düğüm topolojik derinliğine düşer (Base 0, Data.Core 1, Api/Portal 2), yani her
    /// derinlik ayrı bir bant olur. Değişme gerekçesi (ölçüm, kullanıcının ekran görüntüsü 2026-10-12): gerçek OSYS'te
    /// bağımlılık zinciri 28 halka → 177 düğüm 28 banda bölündü; panele sığdırma kuralı pitch'i 12,5 px'e, düğümü
    /// 8 px tabanına indirdi (4 px köşe yarıçapıyla daire, küp ikonu nokta), sütun sayısı en kalabalık bantla (19)
    /// sınırlı kaldığı için blok panel genişliğinin ~%20'sini kullandı. Liste aynı anda tek düz liste gösteriyordu —
    /// iki yüzey ayrışmıştı.</para>
    /// </summary>
    [Fact]
    public void Without_layers_the_graph_is_one_band_like_the_flat_list()
    {
        var topology = new[]
        {
            Node("Base", []),
            Node("Data.Core", ["Base"]),
            Node("Server.Api", ["Data.Core"]),
            Node("Web.Portal", ["Data.Core"]),
        };
        var rows = RowsFor(topology);

        var bands = GraphBands(GraphBinder.Nodes(topology, rows));

        Assert.Equal(new[] { new[] { "Base", "Data.Core", "Server.Api", "Web.Portal" } }, bands); // tek bant, build-order
        Assert.Equal(ListGroups(topology, rows), bands);
    }

    /// <summary>
    /// Pattern yokken de harici projeler listede kendi <c>External</c> grubundadır (Core onları ayrılmış katmana koyar);
    /// graf da öyle: hariciler en üstte KENDİ bantlarında, ana repo projeleri altında TEK bantta.
    /// </summary>
    [Fact]
    public void Without_layers_external_projects_keep_their_own_band_on_top_like_their_list_group()
    {
        const int ext = ExternalProjectsConventions.LayerIndex;
        const string external = ExternalProjectsConventions.LayerName;
        var topology = new[]
        {
            Node("Ext.Logo", [], layerIndex: ext, layerName: external, isExternal: true),
            Node("Ext.Integration", ["Ext.Logo"], layerIndex: ext, layerName: external, isExternal: true),
            Node("Base", ["Ext.Logo"]),
            Node("Data.Core", ["Base"]),
            Node("Server.Api", ["Data.Core", "Ext.Integration"]),
        };
        var rows = RowsFor(topology);

        var bands = GraphBands(GraphBinder.Nodes(topology, rows));

        Assert.Equal(new[] { new[] { "Ext.Logo", "Ext.Integration" }, new[] { "Base", "Data.Core", "Server.Api" } }, bands);
        Assert.Equal(ListGroups(topology, rows), bands);
    }

    /// <summary>
    /// Graf bantları listenin gruplarının BİREBİR eşleniğidir — aynı üyeler, aynı sıra. Ayırt edici durum: iki katman
    /// AYNI adı taşıyabilir (Settings yalnız boş adı reddeder). Liste grupları ADA göre kurar ve ikisini tek grupta
    /// birleştirir; bant katman İNDEKSİNE göre kurulsaydı graf aynı projeleri iki ayrı banda bölerdi.
    /// </summary>
    [Fact]
    public void Graph_bands_mirror_the_list_groups_even_when_two_layers_share_a_name()
    {
        // Core'un katman atamasının çıktısı biçiminde: (LayerIndex, build-order)'a göre sıralı, "Other" en sonda.
        var topology = new[]
        {
            Node("Types.Core", [], layerIndex: 0, layerName: "Core"),
            Node("Ui.Shell", ["Types.Core"], layerIndex: 1, layerName: "UI"),
            Node("Types.Extra", ["Types.Core"], layerIndex: 2, layerName: "Core"),
            Node("Tools", [], layerIndex: 3, layerName: "Other"),
        };
        var rows = RowsFor(topology);

        var bands = GraphBands(GraphBinder.Nodes(topology, rows));

        Assert.Equal(new[] { new[] { "Types.Core", "Types.Extra" }, new[] { "Ui.Shell" }, new[] { "Tools" } }, bands);
        Assert.Equal(ListGroups(topology, rows), bands);
    }

    /// <summary>
    /// Kullanıcının gördüğü kusur (2026-10-12): katman yokken 177 projelik OSYS grafı panelin ortasında dar bir huni,
    /// düğümler 8 px'lik minicik daireler. Aynı bant profili ve aynı panel ölçüsüyle düğüm TAVAN boyda olmalı — tek bant
    /// 177 düğümü panele yayar ve pitch tavanda kalır.
    /// </summary>
    [Fact]
    public void A_deep_dependency_chain_without_layers_still_gets_full_size_nodes()
    {
        var topology = DeepChainWithoutLayers();

        var layout = QuietGraphLayout.Compute(GraphBinder.Nodes(topology, RowsFor(topology)), MeasuredPanel);

        Assert.Equal(QuietGraphLayout.MaxNodeSize, layout.NodeSize); // ölçülen: 8 px (taban), pitch 12,5
    }

    /// <summary>
    /// Aynı kusurun genişlik yüzü: sütun sayısı en kalabalık bandı aşamadığı için blok 19 sütunda kalıyordu
    /// (19 × 12,5 ≈ 238 px; 1202 px'lik içerik genişliğinin ~%20'si). Doğrusu: sütunlar paneli doldurur — seçilen
    /// pitch'te genişliğe sığan sütun sayısının tamamı kullanılır.
    /// </summary>
    [Fact]
    public void A_deep_dependency_chain_without_layers_spreads_across_the_panel_width()
    {
        var topology = DeepChainWithoutLayers();

        var layout = QuietGraphLayout.Compute(GraphBinder.Nodes(topology, RowsFor(topology)), MeasuredPanel);

        int fitting = (int)Math.Floor(QuietGraphLayout.ContentSize(MeasuredPanel).Width / layout.Pitch);
        Assert.True(layout.Columns == fitting,
            $"{layout.Columns} sütun × {layout.Pitch} px — genişliğe {fitting} sütun sığıyor");
    }

    /// <summary>
    /// Döngü üyeleri kendi liste gruplarının bandındadır — döngü ayrı bant AÇMAZ; üyeler bant içinde build-order'da.
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski ad/iddia: <c>Cycle_members_get_finite_back_edge_trimmed_depths_not_a_shared_component_depth</c>
    /// — katman yokken bant topolojik derinlikti ve A→B→C→A döngüsünde üç üye geri-kenar kırpmasıyla ÜÇ FARKLI derinlik
    /// (A=3, B=2, C=1), yani üç ayrı bant alıyordu. Değişme gerekçesi: bant artık derinlik değil listenin grubudur
    /// (ölçüm: <see cref="Without_layers_the_graph_is_one_band_like_the_flat_list"/>); derinlik hesabı kalktı.</para>
    /// </summary>
    [Fact]
    public void Cycle_members_sit_in_their_list_groups_band()
    {
        var topology = new[]
        {
            Node("A", ["B"], inCycle: true), // A, B'ye bağımlı
            Node("B", ["C"], inCycle: true), // B, C'ye bağımlı
            Node("C", ["A"], inCycle: true), // C, A'ya bağımlı → döngüyü kapatır
            Node("D", ["A"]),
        };
        var rows = RowsFor(topology);

        var bands = GraphBands(GraphBinder.Nodes(topology, rows));

        Assert.Equal(new[] { new[] { "A", "B", "C", "D" } }, bands);
        Assert.Equal(ListGroups(topology, rows), bands);
    }

    [Fact]
    public void Cycle_membership_is_not_a_status_and_never_overrides_the_real_one()
    {
        // StatusOf: row null + inCycle → Cycle; sync öncesi her şey Discovered (cycle olsa bile).
        // [DEĞİŞEN KURAL — design v1.7.0 §5] Döngü üyeliği statü DEĞİLDİR: satırı olmayan bir düğüm,
        // üye olsa da Discovered'dır; üyelik düğümün kendi InCycle alanında taşınır.
        Assert.Equal(GraphStatus.Discovered, GraphBinder.StatusOf(null, synced: true));
        Assert.Equal(GraphStatus.Discovered, GraphBinder.StatusOf(null, synced: false));

        // Row VARSA statü TEK otoriteden (row.Status) gelir; üyelik onu ezmez — Pending bir üye Discovered'dır.
        var cycleRow = new ProjectRowViewModel(Id("X"), "X", ProjectRowState.Pending) { InCycle = true };
        Assert.Equal(GraphStatus.Discovered, GraphBinder.StatusOf(cycleRow, synced: true));

        // Guard — delegasyon kanıtı: üye bir satır derlenirken Building'dir, üyelik bunu gizlemez.
        var startedInCycleRow = new ProjectRowViewModel(Id("Z"), "Z", ProjectRowState.Started) { InCycle = true };
        Assert.Equal(GraphStatus.Building, GraphBinder.StatusOf(startedInCycleRow, synced: true));

        // Uçtan uca: üyelik STATÜ kanalına girmez — üye bir düğümün statüsü Discovered'dır.
        var topology = new[] { Node("X", [], inCycle: true), Node("Y", ["X"]) };
        var nodes = GraphBinder.Nodes(topology, RowsFor(topology));
        Assert.Equal(GraphStatus.Discovered, nodes.Single(n => n.Name == "X").Status);

        // [DEĞİŞEN KURAL — design v1.20.0 §2.3] Eski iddia (v1.12.0): bu işlemde derlenmeyen üye kendi görsel
        // durumunu taşır (VisualStatus.Cycle: gri node + amber küp), üye olmayan Discovered'dır. Değişme
        // gerekçesi: küp artık HER durumda amber'dır, yani üyelik durumdan bağımsız bir parametredir. Yeni
        // kural: üye ve üye olmayan düğüm AYNI görsel durumu (karar yok → Unknown) taşır; üyelik düğümün kendi
        // InCycle alanındadır ve yalnız küpü boyar.
        Assert.Equal(VisualStatus.Unknown, nodes.Single(n => n.Name == "X").Visual);
        Assert.Equal(VisualStatus.Unknown, nodes.Single(n => n.Name == "Y").Visual);
        Assert.True(nodes.Single(n => n.Name == "X").InCycle);
        Assert.False(nodes.Single(n => n.Name == "Y").InCycle);
    }

    // [quiet · SİLİNDİ] `Nodes_source_the_dep_badge_from_row_HasDepIssue` — v1.3.0 §2.3 "Kaldırılanlar" graf
    // içi dep-issue rozetini kaldırdı (dep bilgisi liste kartlarında yaşıyor), dolayısıyla GraphNode artık
    // HasDepIssue taşımıyor ve binder'ın onu üretecek bir sebebi yok. Bayrağın kendisi
    // ProjectRowViewModel.HasDepIssue olarak duruyor ve ProjectRowTests'te pinli.

    /// <summary>
    /// [design v1.11.0 §2.3] <b>Düğüm TEK renk kanalı taşır.</b>
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> Burada dört test vardı ve hepsi kalkan iki alanı pinliyordu:
    /// <c>GraphNode.InCycle</c> ("üyelik satırdan gelir, satır yoksa topolojiden") ve <c>GraphNode.WillBuild</c>
    /// ("plan satırdan gelir, satır yoksa topolojiden; satır bilirse o kazanır"). v1.11.0 ikisini de kaldırdı:
    /// node border'ı ve içindeki küp AYNI görsel durumdan boyanır, plan bilgisi satırın çift SHA metnine,
    /// döngü üyeliği liste satırındaki tek amber üçgene indi. Dördü bu tek teste indi.</para>
    ///
    /// <para>Görsel durumun kendisi satırdan gelir (koşu statüsü + çıktı durumu + işaretlilik); satır YOKSA
    /// karar da yoktur ve durum <see cref="VisualStatus.Unknown"/>'dır.</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL — design v1.20.0 §2.3]</b> Eski ad/iddia:
    /// <c>Nodes_carry_the_rows_single_visual_status_and_default_to_the_fresh_start_mode</c> — Sync sonrası satır
    /// başlangıç modundadır (<c>Fresh</c>), satırsız düğüm de <c>Fresh</c>. Değişme gerekçesi: Sync artık renk
    /// verir; başlangıç modu yalnız kararın yokluğudur. Yeni kural: kararlı satır kendi çıktı durumunu taşır,
    /// satırsız düğüm <c>Unknown</c>'dır.</para>
    /// </summary>
    [Fact]
    public void Nodes_carry_the_rows_single_visual_status_and_default_to_unknown()
    {
        var topology = new[] { Node("X", [], inCycle: true), Node("Y", ["X"]), Node("Z", ["Y"]) };
        var rows = RowsFor(topology);
        rows[Id("X")].WillBuild = false;            // Sync kararı: güncel
        rows[Id("X")].WillBuildReason = WillBuildReason.UpToDate;
        rows[Id("Y")].Marked = true;                // işlem kapsamı — dalgada amber'a yanar

        var nodes = GraphBinder.Nodes(topology, rows);

        Assert.Equal(VisualStatus.Current, nodes.Single(n => n.Name == "X").Visual);
        Assert.Equal(VisualStatus.Marked, nodes.Single(n => n.Name == "Y").Visual);
        Assert.Equal(VisualStatus.Unknown, nodes.Single(n => n.Name == "Z").Visual); // karar yok

        // Satır yoksa (topoloji düğümünün henüz satırı yok — savunmacı) durum bilinmez; üyelik topolojiden.
        var orphan = GraphBinder.Nodes(topology, new Dictionary<string, ProjectRowViewModel>(StringComparer.OrdinalIgnoreCase));
        Assert.All(orphan, n => Assert.Equal(VisualStatus.Unknown, n.Visual));
        Assert.True(orphan.Single(n => n.Name == "X").InCycle);
    }

    [Fact]
    public void Node_order_within_a_layer_follows_build_order()
    {
        // [D5 fix wave/Fix 2] Zeta ve Alpha AYNI bantta (katman tanımı yok → tek bant); topoloji
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
