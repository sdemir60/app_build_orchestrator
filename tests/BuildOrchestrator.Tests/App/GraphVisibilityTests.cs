using System.Windows;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [quiet] Gizli graf paneli statü akışını GÖRSELE ÇEVİRMEZ.
///
/// <para><b>Kusur:</b> besleme yolu (<c>MainWindow.PushGraphStatuses</c> → <c>RebuildGraph</c>) grafın
/// görünürlüğüne HİÇ bakmıyordu ve <c>ShellRoot.ApplyLayout</c> paneli yalnız <c>Collapsed</c> yapıyordu.
/// Sonuç: <c>list</c>/<c>focus</c> yerleşim modunda panel ekranda YOKKEN de her 200ms'lik statü tick'inde
/// her düğümün stili ve (o zamanki kalıcı ağda) her kenarın stili yeniden hesaplanıyordu.</para>
///
/// <para><b>Kapı nerede:</b> <see cref="GraphView"/>'ın KENDİSİNDE, iki public besleme metodunun girişinde.
/// Çağıranda olsaydı her çağıran aynı kontrolü kopyalamak zorunda kalırdı ve panel tekrar görünür olduğunda
/// "kaçırılanı yakalama" mantığı da orada tekrarlanırdı (kopya YASAK, CLAUDE.md).</para>
///
/// <para><b>Neden <c>Visibility</c>, <c>IsVisible</c> DEĞİL:</b> <c>IsVisible</c> bağlı olmayan bir görsel
/// ağaçta HER ZAMAN false'tur ve <c>IsVisibleChanged</c> hiç ateşlenmez — headless süit ile üretim ayrışırdı.
/// <c>Visibility</c> öğenin kendi özelliğidir ve <c>ShellRoot</c>'un sürdüğü sinyalin ta kendisidir.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphVisibilityTests
{
    private static IReadOnlyList<GraphNode> Nodes(GraphStatus status) =>
    [
        new("OSYS.Base", 0, status),
        new("OSYS.Data", 1, status),
    ];

    private static IReadOnlyList<GraphEdge> Edges() => [new("OSYS.Base", "OSYS.Data")];

    private static GraphView Built()
    {
        var view = GraphTestView.Realized(new Size(600, 400));
        view.SetGraph(Nodes(GraphStatus.Queued), Edges());
        return view;
    }

    /// <summary>
    /// AYIRT EDİCİ: her tick GERÇEKTEN değişen bir statü iter, yani "değişmediyse dokunma" hızlı yolu bu
    /// testi maskeleyemez. Kapı yoksa sayaç tick × düğüm kadar artar.
    /// </summary>
    [StaFact]
    public void A_hidden_panel_does_no_visual_work_when_statuses_are_pushed()
    {
        var view = Built();
        view.Visibility = Visibility.Collapsed;
        int before = view.NodeStatusApplyCount;

        for (int tick = 0; tick < 10; tick++)
            view.UpdateStatuses(Nodes(tick % 2 == 0 ? GraphStatus.Building : GraphStatus.Succeeded));

        Assert.Equal(before, view.NodeStatusApplyCount);
    }

    /// <summary>Kapı bir SUSTURUCU değil bir ERTELEYİCİDİR: panel geri geldiğinde EN SON besleme uygulanır,
    /// aradaki ara durumlar değil.</summary>
    [StaFact]
    public void A_panel_that_becomes_visible_again_shows_the_LATEST_status_not_the_one_it_was_hidden_with()
    {
        var view = Built();
        view.Visibility = Visibility.Collapsed;

        view.UpdateStatuses(Nodes(GraphStatus.Building));
        view.UpdateStatuses(Nodes(GraphStatus.Succeeded));
        view.Visibility = Visibility.Visible;

        Assert.Equal(GraphStatus.Succeeded, view.NodeVisuals["OSYS.Base"].Model.Status);
        Assert.Equal(GraphStatus.Succeeded, view.NodeVisuals["OSYS.Data"].Model.Status);
    }

    /// <summary>Topoloji de ertelenir — gizli panelde 177 düğümlük bir görsel ağaç kurmanın hiçbir karşılığı
    /// yoktur. Sync gizliyken yapılırsa graf, panel açıldığında kurulur.</summary>
    [StaFact]
    public void A_topology_that_arrives_while_hidden_is_built_when_the_panel_comes_back()
    {
        var view = GraphTestView.Realized(new Size(600, 400));
        view.Visibility = Visibility.Collapsed;

        view.SetGraph(Nodes(GraphStatus.Discovered), Edges());
        Assert.Equal(0, view.NodeCount);

        view.Visibility = Visibility.Visible;
        Assert.Equal(2, view.NodeCount);
    }

    /// <summary>Gizliyken önce topoloji sonra statü gelirse SIRA korunur: panel açılınca önce graf kurulur,
    /// sonra statüler onun üstüne yazılır. Ters sırada statü itişi boş bir grafa düşer ve kaybolurdu.</summary>
    [StaFact]
    public void A_topology_and_a_status_push_that_both_arrive_while_hidden_replay_in_order()
    {
        var view = GraphTestView.Realized(new Size(600, 400));
        view.Visibility = Visibility.Collapsed;

        view.SetGraph(Nodes(GraphStatus.Discovered), Edges());
        view.UpdateStatuses(Nodes(GraphStatus.Building));
        view.Visibility = Visibility.Visible;

        Assert.Equal(2, view.NodeCount);
        Assert.Equal(GraphStatus.Building, view.NodeVisuals["OSYS.Base"].Model.Status);
    }

    /// <summary>Görünür panelde kapı ŞEFFAFTIR — bugünkü davranış birebir korunur (regresyon koruması).</summary>
    [StaFact]
    public void A_visible_panel_keeps_applying_statuses_exactly_as_before()
    {
        var view = Built();
        int before = view.NodeStatusApplyCount;

        view.UpdateStatuses(Nodes(GraphStatus.Building));

        Assert.True(view.NodeStatusApplyCount > before);
        Assert.Equal(GraphStatus.Building, view.NodeVisuals["OSYS.Base"].Model.Status);
    }
}
