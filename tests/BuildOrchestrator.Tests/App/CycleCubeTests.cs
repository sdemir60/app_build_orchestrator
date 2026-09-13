using System.Windows;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.12.0 §2.3 · §5] <b>Bu işlemde derlenmeyen döngü üyesinde node gri, içindeki küp AMBER.</b>
/// Satırdaki amber uyarı üçgeninin grafik vekilidir.
///
/// <para><b>Ölçülen kusur:</b> v1.11.0 döngüyü yalnız liste satırındaki üçgenle anlatıyordu; grafta hiçbir iz
/// yoktu ve bitmiş bir koşu incelenirken "bu neden derlenmedi" okunmuyordu.</para>
///
/// <para><b>[DEĞİŞEN KURAL]</b> "Border ve küp HER durumda aynı rengi taşır" kuralının <b>TEK istisnası</b>
/// budur — <see cref="GraphPlanCoreTests"/> onu geri kalan her durum için pinlemeye devam eder. İstisna
/// bilinçlidir: turuncu kanal geri gelmez, kullanılan renk uyarının kendi amberidir.</para>
///
/// <para><b>Ne zaman:</b> işlemin NÖTR anında yanar (başlangıç modu düşer) ve koşu boyunca, finalde ve koşu
/// bittikten sonra da durur — sonraki Sync (yeniden başlangıç modu) ya da kendisini derleyen bir işleme dek.
/// İşaretleme dalgasına KATILMAZ: dalga "derlenecekler"in dilidir.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class CycleCubeTests
{
    private static GraphView Realized(params GraphNode[] nodes)
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph(nodes, []);
        return view;
    }

    private static Color CoreColour(GraphView view, string node) =>
        ((SolidColorBrush)view.NodeVisuals[node].Icon.Stroke).Color;

    private static Color BorderColour(GraphView view, string node) =>
        ((SolidColorBrush)view.NodeVisuals[node].Square.Stroke).Color;

    private static Color Token(GraphView view, string key) =>
        ((SolidColorBrush)view.FindResource(key)).Color;

    private static ProjectNode Node(string name, bool inCycle) =>
        new(@"C:\p\" + name + ".csproj", name, @"C:\p\" + name + ".csproj", SolutionNames: [],
            Dependencies: [], BuildOrder: 0, LayerIndex: null, LayerName: null, InCycle: inCycle, WillBuild: null);

    // ------------------------------------------------------------------ eşleme (saf)

    /// <summary>Başlangıç modu döngü üyeliğini EZER: Sync hiçbir şeyi renklendirmez, amber küp de yoktur
    /// (§2.3 "başlangıç renksizdir" kuralı korunur — Clean/VS Clean sonrası hâl de budur).</summary>
    [Fact]
    public void The_start_mode_still_wins_so_sync_paints_no_amber_cube()
        => Assert.Equal(VisualStatus.Fresh,
            VisualStatuses.For(GraphStatus.Discovered, fresh: true, marked: false, inCycle: true));

    /// <summary>Nötr an: başlangıç modu düştü, proje bu işlemin kapsamında değil ve bir döngüdedir → küp
    /// amber'a döner. Döngüde OLMAYAN aynı satır düz gri kalır.</summary>
    [Fact]
    public void After_the_neutral_moment_a_cycle_member_becomes_its_own_state()
    {
        Assert.Equal(VisualStatus.Cycle,
            VisualStatuses.For(GraphStatus.Discovered, fresh: false, marked: false, inCycle: true));
        Assert.Equal(VisualStatus.Discovered,
            VisualStatuses.For(GraphStatus.Discovered, fresh: false, marked: false, inCycle: false));
    }

    /// <summary>Atlanmış bir döngü üyesi kendi durumunu taşır: çerçeve/zemin <c>skipped</c>'ın, küp amber.</summary>
    [Fact]
    public void A_skipped_cycle_member_keeps_the_skipped_frame_and_the_amber_cube()
        => Assert.Equal(VisualStatus.CycleSkipped,
            VisualStatuses.For(GraphStatus.Skipped, fresh: false, marked: false, inCycle: true));

    /// <summary>Üye GERÇEKTEN derlendiğinde (Resolve cycles) amber küp YOKTUR — sonuç rengi tek başına
    /// konuşur. Kapsamdayken de dalga onu tam amber'a yakar, ayrı bir küp kanalı doğmaz.</summary>
    [StaTheory]
    [InlineData(GraphStatus.Queued, VisualStatus.Queued)]
    [InlineData(GraphStatus.Building, VisualStatus.Building)]
    [InlineData(GraphStatus.Succeeded, VisualStatus.Succeeded)]
    [InlineData(GraphStatus.Failed, VisualStatus.Failed)]
    public void A_member_that_is_actually_built_shows_only_its_result(GraphStatus status, VisualStatus expected)
        => Assert.Equal(expected, VisualStatuses.For(status, fresh: false, marked: false, inCycle: true));

    /// <summary>Dalga döngü üyesini de kapsayabilir (Resolve cycles): o an renk TAM amberdir, "gri node +
    /// amber küp" değil.</summary>
    [Fact]
    public void The_marking_wave_paints_a_member_fully_amber()
        => Assert.Equal(VisualStatus.Marked,
            VisualStatuses.For(GraphStatus.Discovered, fresh: false, marked: true, inCycle: true));

    // ------------------------------------------------------------------ graf (çizim)

    /// <summary>Çizimde tek istisna görünür hâle gelir: çerçeve gri, küp amber.</summary>
    [StaFact]
    public void The_node_frame_stays_grey_while_the_cube_turns_amber()
    {
        var view = Realized(new GraphNode("n", "n", 0, GraphStatus.Discovered, VisualStatus.Fresh));

        view.UpdateStatuses([new("n", "n", 0, GraphStatus.Discovered, VisualStatus.Cycle)]);

        Assert.Equal(Token(view, "Brush.BorderStrong"), BorderColour(view, "n"));
        Assert.Equal(Token(view, "Brush.AmberText"), CoreColour(view, "n"));
        Assert.Empty(view.NodeVisuals["n"].Square.StrokeDashArray); // kesikli DEĞİL — başlangıç modu geçti
    }

    /// <summary>Atlanmış üyede çerçeve/zemin <c>skipped</c>'ın, küp yine amber.</summary>
    [StaFact]
    public void A_skipped_member_keeps_the_skipped_frame_and_the_amber_cube_on_screen()
    {
        var view = Realized(new GraphNode("n", "n", 0, GraphStatus.Discovered, VisualStatus.Fresh));

        view.UpdateStatuses([new("n", "n", 0, GraphStatus.Skipped, VisualStatus.CycleSkipped)]);

        Assert.Equal(Token(view, "Brush.StatusSkippedBorder"), BorderColour(view, "n"));
        Assert.Equal(Token(view, "Brush.AmberText"), CoreColour(view, "n"));
    }

    /// <summary>Liste tarafı DEĞİŞMEDİ (§2.4): şerit ve nokta döngü üyesinde de nötr gridir — döngü orada
    /// zaten tek amber üçgenle anlatılır, ikinci bir amber kanal açılmaz.</summary>
    [StaTheory]
    [InlineData(VisualStatus.Cycle)]
    [InlineData(VisualStatus.CycleSkipped)]
    public void The_list_row_stays_neutral_for_a_cycle_member(VisualStatus state)
        => Assert.Equal("Brush.StatusSkippedBorder", VisualStatuses.StripeBrushKey(state));

    // ------------------------------------------------------------------ bağlantı (satır → graf)

    /// <summary>Satır kendi görsel durumunu üyelikten haberdar üretir — üyelik bilgisi (<c>InCycle</c>) zaten
    /// satırdadır ve eşlemeye AKMASI gerekir, yoksa amber küp hiç doğmaz.</summary>
    [Fact]
    public void The_row_view_model_feeds_its_cycle_membership_into_the_visual_status()
    {
        var row = new ProjectRowViewModel("a", "A", ProjectRowState.Pending) { InCycle = true, Fresh = false };
        Assert.Equal(VisualStatus.Cycle, row.VisualStatus);

        row.Fresh = true;                                   // Sync: başlangıç modu üyeliği ezer
        Assert.Equal(VisualStatus.Fresh, row.VisualStatus);
    }

    /// <summary>Graf düğümü satırın görsel durumunu AYNEN taşır (ikinci bir eşleme yazılmaz) — üyelik de
    /// oradan gelir.</summary>
    [Fact]
    public void The_graph_node_carries_the_rows_cycle_state()
    {
        var topology = new[] { Node("X", inCycle: true), Node("Y", inCycle: false) };
        var rows = new Dictionary<string, ProjectRowViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in topology)
            rows[n.Id] = new ProjectRowViewModel(n.Id, n.Name, ProjectRowState.Pending)
            { InCycle = n.InCycle, Fresh = false };

        var nodes = GraphBinder.Nodes(topology, rows);

        Assert.Equal(VisualStatus.Cycle, nodes.Single(n => n.Name == "X").Visual);
        Assert.Equal(VisualStatus.Discovered, nodes.Single(n => n.Name == "Y").Visual);
    }
}
