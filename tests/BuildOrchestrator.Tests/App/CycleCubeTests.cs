using System.Windows;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.20.0 §2.3 · §5] <b>Döngü üyesinde küp HER durumda AMBER</b>; çerçeve ve zemin kendi durumunu
/// (bilinmiyor/derlenecek/derleniyor/sonuç) taşır. Satırdaki amber uyarı üçgeninin grafik vekilidir.
///
/// <para><b>[DEĞİŞEN KURAL — design v1.20.0 §2.3]</b> Eski iddia (v1.12.0): amber küp yalnız "bu işlemde
/// derlenmeyen" üyede yanar — başlangıç modu (Sync) onu ezer, üyeyi gerçekten derleyen işlem (Resolve) onu
/// söndürür; üyelik görsel duruma gömülüydü (<c>Cycle</c>/<c>CycleSkipped</c>). Değişme gerekçesi (kullanıcı
/// ölçümü, v1.20.0): üyelik yapısaldır, bir koşunun sonucu değildir — Sync sonrası da Resolve'dan sonra da
/// proje döngüdedir ve grafta bu okunmuyordu. Yeni kural: küp durumdan bağımsız bir parametreyle boyanır
/// (<see cref="GraphNode.InCycle"/> → <see cref="VisualStatuses.NodeCoreBrushKey"/>); "border ve küp aynı
/// rengi taşır" kuralının tek istisnası budur (<see cref="GraphPlanCoreTests"/> geri kalanını pinler).
/// Eski <c>After_the_neutral_moment_a_cycle_member_becomes_its_own_state</c>,
/// <c>A_skipped_cycle_member_keeps_the_skipped_frame_and_the_amber_cube</c> (+ ekran eşi) ve
/// <c>The_row_view_model_feeds_its_cycle_membership_into_the_visual_status</c> kalkan durumları pinliyordu;
/// yerlerini aşağıdaki durum-bağımsız küp testleri aldı (atlanan üye artık kendi çıktı durumundadır —
/// <see cref="VisualStatusTests"/>).</para>
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

    /// <summary>[DEĞİŞEN KURAL — design v1.20.0 §2.3] Eski ad/iddia:
    /// <c>The_start_mode_still_wins_so_sync_paints_no_amber_cube</c> — başlangıç modu üyeliği EZER, Sync amber
    /// küp çizmez. Değişme gerekçesi: Sync artık renk verir ve üyelik yapısaldır. Yeni kural: Sync sonrası
    /// güncel bir üye yeşil çerçeve + amber küp, derlenecek üye gri çerçeve + amber küp gösterir; karar yokken
    /// de küp amber'dır.</summary>
    [Theory]
    [InlineData(VisualStatus.Unknown)]
    [InlineData(VisualStatus.Current)]
    [InlineData(VisualStatus.Stale)]
    public void Sync_paints_the_amber_cube_because_membership_is_structural(VisualStatus afterSync)
        => Assert.Equal("Brush.AmberText", VisualStatuses.NodeCoreBrushKey(afterSync, inCycle: true));

    /// <summary>[DEĞİŞEN KURAL — design v1.20.0 §2.3] Eski ad/iddia:
    /// <c>A_member_that_is_actually_built_shows_only_its_result</c> — üye GERÇEKTEN derlendiğinde (Resolve
    /// cycles) amber küp YOKTUR, sonuç rengi tek başına konuşur. Değişme gerekçesi: Resolve ile derlense de proje
    /// döngüde kalır. Yeni kural: çerçeve koşuyu/sonucu, küp amber'ı taşır.
    /// <para>[R-M4] Kırmızı çerçeve KANITLI hatanındır: koşu kanıtlı hatayı çıktı durumuna da yazar
    /// (<see cref="StandingStatus.Failed"/>), bu yüzden Failed satırı o durumla sınanır — kanıtsız hata
    /// (bayat durum) gri kalır, bkz. <c>VisualStatusTests.A_failure_that_is_not_evidence_reads_as_its_stale_standing</c>.
    /// Eski fixture dört satırı da <c>Stale</c> ile sınıyordu.</para>
    /// <para><b>[DEĞİŞEN KURAL — final review I2]</b> Succeeded satırı da artık kendi çıktı durumuyla sınanır:
    /// güvenilir başarı durumu güncel yazar (<see cref="StandingStatus.Current"/>). Eski fixture onu <c>Stale</c>
    /// ile sınıyor ve yeşil bekliyordu; başarı artık bayat durumu EZMEZ (Clean/güvenilmez başarı gri kalır, bkz.
    /// <c>VisualStatusTests.Succeeded_falls_to_a_stale_or_failed_standing</c>).</para></summary>
    [Theory]
    [InlineData(GraphStatus.Queued, StandingStatus.Stale, VisualStatus.Queued, "Brush.Amber")]
    [InlineData(GraphStatus.Building, StandingStatus.Stale, VisualStatus.Building, "Brush.Amber")]
    [InlineData(GraphStatus.Succeeded, StandingStatus.Current, VisualStatus.Succeeded, "Brush.StatusSuccess")]
    [InlineData(GraphStatus.Failed, StandingStatus.Failed, VisualStatus.Failed, "Brush.StatusFail")]
    public void A_member_that_is_actually_built_keeps_the_amber_cube_and_frames_its_result(
        GraphStatus status, StandingStatus standing, VisualStatus visual, string frame)
    {
        Assert.Equal(visual, VisualStatuses.For(status, standing, marked: false));
        Assert.Equal("Brush.AmberText", VisualStatuses.NodeCoreBrushKey(visual, inCycle: true));
        Assert.Equal(frame, VisualStatuses.NodeBorderBrushKey(visual));
    }

    /// <summary>Dalga döngü üyesini de kapsayabilir (Resolve cycles): o an renk TAM amberdir.</summary>
    [Fact]
    public void The_marking_wave_paints_a_member_fully_amber()
    {
        Assert.Equal(VisualStatus.Marked,
            VisualStatuses.For(GraphStatus.Discovered, StandingStatus.Current, marked: true));
        Assert.Equal("Brush.Amber", VisualStatuses.NodeBorderBrushKey(VisualStatus.Marked));
        Assert.Equal("Brush.AmberText", VisualStatuses.NodeCoreBrushKey(VisualStatus.Marked, inCycle: true));
    }

    /// <summary>Döngüde OLMAYAN node'da küp çerçeveyle aynı durumu taşır — amber üyeliğe özgüdür.</summary>
    [Fact]
    public void A_non_member_cube_follows_its_frame()
    {
        Assert.Equal("Brush.StatusSuccessText", VisualStatuses.NodeCoreBrushKey(VisualStatus.Current, inCycle: false));
        Assert.Equal("Brush.TextFaint", VisualStatuses.NodeCoreBrushKey(VisualStatus.Stale, inCycle: false));
    }

    // ------------------------------------------------------------------ graf (çizim)

    /// <summary>Çizimde: derlenecek üyede çerçeve gri (düz — başlangıç modu değil), küp amber.</summary>
    [StaFact]
    public void The_node_frame_stays_grey_while_the_cube_turns_amber()
    {
        var view = Realized(new GraphNode("n", "n", 0, GraphStatus.Discovered, VisualStatus.Unknown));

        view.UpdateStatuses([new("n", "n", 0, GraphStatus.Discovered, VisualStatus.Stale, InCycle: true)]);

        Assert.Equal(Token(view, "Brush.BorderStrong"), BorderColour(view, "n"));
        Assert.Equal(Token(view, "Brush.AmberText"), CoreColour(view, "n"));
        Assert.Empty(view.NodeVisuals["n"].Square.StrokeDashArray); // kesikli DEĞİL — karar var
    }

    /// <summary>Resolve ile yeşil biten üyede çerçeve yeşil, küp yine amber.</summary>
    [StaFact]
    public void A_green_member_keeps_the_green_frame_and_the_amber_cube_on_screen()
    {
        var view = Realized(new GraphNode("n", "n", 0, GraphStatus.Discovered, VisualStatus.Unknown));

        view.UpdateStatuses([new("n", "n", 0, GraphStatus.Succeeded, VisualStatus.Succeeded, InCycle: true)]);

        Assert.Equal(Token(view, "Brush.StatusSuccess"), BorderColour(view, "n"));
        Assert.Equal(Token(view, "Brush.AmberText"), CoreColour(view, "n"));
    }

    /// <summary>Liste tarafı DEĞİŞMEDİ (§2.4): satırın görsel durumu döngü üyeliğinden haberdar değildir —
    /// döngü orada tek amber üçgenle anlatılır, ikinci bir amber kanal açılmaz.</summary>
    [Fact]
    public void The_list_row_does_not_carry_the_cycle_colour()
    {
        var member = new ProjectRowViewModel("a", "A", ProjectRowState.Pending)
        { InCycle = true, WillBuild = false, WillBuildReason = WillBuildReason.UpToDate };
        var other = new ProjectRowViewModel("b", "B", ProjectRowState.Pending)
        { WillBuild = false, WillBuildReason = WillBuildReason.UpToDate };

        Assert.Equal(VisualStatus.Current, member.VisualStatus);
        Assert.Equal(other.VisualStatus, member.VisualStatus);
    }

    // ------------------------------------------------------------------ bağlantı (satır → graf)

    /// <summary>Graf düğümü satırın görsel durumunu AYNEN taşır (ikinci bir eşleme yazılmaz); üyelik ayrı bir
    /// alanla gelir ve durumu değiştirmez.</summary>
    [Fact]
    public void The_graph_node_carries_the_rows_state_and_membership_separately()
    {
        var topology = new[] { Node("X", inCycle: true), Node("Y", inCycle: false) };
        var rows = new Dictionary<string, ProjectRowViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in topology)
            rows[n.Id] = new ProjectRowViewModel(n.Id, n.Name, ProjectRowState.Pending)
            { InCycle = n.InCycle, WillBuild = false, WillBuildReason = WillBuildReason.UpToDate };

        var nodes = GraphBinder.Nodes(topology, rows);

        var x = nodes.Single(n => n.Name == "X");
        var y = nodes.Single(n => n.Name == "Y");
        Assert.Equal(VisualStatus.Current, x.Visual);
        Assert.Equal(VisualStatus.Current, y.Visual);
        Assert.True(x.InCycle);
        Assert.False(y.InCycle);
    }
}
