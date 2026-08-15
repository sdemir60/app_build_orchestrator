using System.Windows;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.7.0 §2.3/§5] Düğümün ÇEKİRDEĞİ (içteki küp) plan kanalını söyler: amber = derlenecek,
/// gri = güncel. Kenar "bu koşuda ne oldu" derken çekirdek "ne olacak" der.
///
/// <para><b>Neden bu dosya var:</b> kuyruğa alınmak bir SONUÇ değildir ama çekirdek onu sonuç gibi
/// okuyordu — statü <c>Queued</c>'a geçer geçmez küp statü grisine dönüyordu. Bedeli basış anında
/// görülüyordu: Sync'ten sonra derlenecek düğümlerin küpü amber durur, Build'e basılınca hepsi aynı anda
/// griye döner (renk geçişi yok, anında) ve düğümler de sönmeye başlar — ekrandaki tek renkli şey aynı anda
/// kaybolduğu için kullanıcı bunu "derlenecekler bir yanıp söndü" diye gördü.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphPlanCoreTests
{
    private static GraphView Realized(params GraphNode[] nodes)
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph(nodes, []);
        return view;
    }

    private static Color CoreColour(GraphView view, string node) =>
        ((SolidColorBrush)view.NodeVisuals[node].Icon.Stroke).Color;

    private static Color Token(GraphView view, string key) =>
        ((SolidColorBrush)view.FindResource(key)).Color;

    /// <summary>Sync'ten sonra: derlenecek amber, güncel gri.</summary>
    [StaFact]
    public void After_sync_the_core_says_what_will_be_built()
    {
        var view = Realized(
            new("dirty", 0, GraphStatus.Discovered, false, true),
            new("clean", 0, GraphStatus.Discovered, false, false));

        Assert.Equal(Token(view, "Brush.DotDirty"), CoreColour(view, "dirty"));
        Assert.Equal(Token(view, "Brush.DotClean"), CoreColour(view, "clean"));
    }

    /// <summary>
    /// AYIRT EDİCİ — kuyruğa alınınca çekirdek DEĞİŞMEZ. Basış anında düğümün tek görünür değişimi
    /// sönmesidir; rengi yerinde kalır, yani "hepsi toplu söner" ve hiçbir şey yanıp sönmez.
    /// </summary>
    [StaFact]
    public void Queueing_a_node_does_not_change_its_core_colour()
    {
        var view = Realized(new GraphNode("dirty", 0, GraphStatus.Discovered, false, true));
        var beforePress = CoreColour(view, "dirty");

        view.RunPhase = GraphRunPhase.Running;                                    // basış: graf soluklaşır
        view.UpdateStatuses([new("dirty", 0, GraphStatus.Queued, false, true)]);  // plan geldi: kuyruk

        Assert.Equal(beforePress, CoreColour(view, "dirty"));
        Assert.Equal(Token(view, "Brush.DotDirty"), CoreColour(view, "dirty"));
    }

    /// <summary>Kontrol: SONUÇ statüleri çekirdeği DEVRALIR — plan bittiğinde söylenecek şey sonuçtur.</summary>
    [StaTheory]
    [InlineData(GraphStatus.Succeeded, "Brush.StatusSuccessText")]
    [InlineData(GraphStatus.Failed, "Brush.StatusFailText")]
    [InlineData(GraphStatus.Skipped, "Brush.StatusSkippedText")]
    [InlineData(GraphStatus.Building, "Brush.AmberText")]
    public void A_result_status_takes_the_core_over(GraphStatus status, string tokenKey)
    {
        var view = Realized(new GraphNode("n", 0, GraphStatus.Discovered, false, true));

        view.UpdateStatuses([new("n", 0, status, false, true)]);

        Assert.Equal(Token(view, tokenKey), CoreColour(view, "n"));
    }

    /// <summary>Döngü üyeliği her şeyi ezer — kalıcı yapısal olgu (§5).</summary>
    [StaFact]
    public void Cycle_membership_still_wins()
    {
        var view = Realized(new GraphNode("n", 0, GraphStatus.Queued, true, true));

        Assert.Equal(Token(view, "Brush.StatusCycle"), CoreColour(view, "n"));
    }

    /// <summary>
    /// Koşuya girerken graf ÖNCE söner, görünüm SONRA değişir.
    ///
    /// <para>Basış anında iki şey birden oluyordu: sönme başlıyor (280 ms sürer) ve aynı karede kesikli
    /// çerçeveler düze dönüyordu (çerçeve/renk değişimleri anında uygulanır). Değişim tam parlaklıkta
    /// görülüp sönme sonra geldiği için ekran "önce derlenecekler belirdi, sonra hepsi söndü" diyordu.
    /// İstenen sıra: topluca sön → görünüm değişsin → derleme başlasın.</para>
    /// </summary>
    [StaFact]
    public void Entering_a_run_dims_before_it_repaints()
    {
        var view = Realized(new GraphNode("n", 0, GraphStatus.Discovered, false, true));
        var dashedAtRest = view.NodeVisuals["n"].Square.StrokeDashArray;

        view.RunPhase = GraphRunPhase.Running;                                 // basış
        view.UpdateStatuses([new("n", 0, GraphStatus.Queued, false, true)]);   // plan hemen ardından geldi

        // Sönme oynarken çerçeve HÂLÂ kesikli: görünüm değişimi beklemede.
        Assert.Equal(dashedAtRest, view.NodeVisuals["n"].Square.StrokeDashArray);
        Assert.Equal(GraphNodeOpacity.RunDim, view.NodeVisuals["n"].OpacityTarget, 6);

        DispatcherPump.PumpUntil(
            () => !Equals(view.NodeVisuals["n"].Square.StrokeDashArray, dashedAtRest),
            TimeSpan.FromSeconds(3));

        // Sönme bitince görünüm uygulanır — çerçeve düze döner.
        Assert.NotEqual(dashedAtRest, view.NodeVisuals["n"].Square.StrokeDashArray);
    }
}
