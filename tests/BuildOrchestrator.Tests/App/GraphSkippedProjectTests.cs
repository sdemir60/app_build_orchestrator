using System.Windows;
using System.Windows.Media.Animation;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [quiet · atlanan proje] <b>Atlanan projede hiçbir canlandırma YOKTUR.</b> Sonuç renginde sessizce yerine
/// oturur; ne amber yörünge, ne parlak bekleme, ne dalga.
///
/// <para><b>Eski iddia (bu dosyanın önceki hâli, <c>GraphSkipFlashTests</c>):</b> atlanan düğüm derlenen
/// düğümle aynı anlatımı alıyordu — kısa bir parlak bekleme, tek atımlık amber yörünge çakışı ve aynı
/// tick'te atlananlar için build-order boyunca 45ms adımlı bir dalga. Gerekçesi kullanıcının gözlemiydi:
/// "atlanan projeler hiç işlem görmemiş gibi duruyor, halbuki atlandı, kontrol edildi."</para>
///
/// <para><b>Değişme gerekçesi:</b> gözle bakıldığında sonuç ikna etmedi. Düğüm gri ("atlandı" diyor) ama
/// etrafında amber yörünge dönüyor ("çalışılıyor" diyor) — iki sinyal çelişiyordu, ve tam anlatıma geçmek
/// (kareyi bir an amber boyamak) daha kötüsünü getirecekti: bu araçta en sık koşu "hiçbir şey değişmedi"
/// koşusudur, dolayısıyla grafın TAMAMI her Derle'de saniyelerce kıpırdardı. Sessiz grafın anlattığı şey
/// "ne oldu" değil "ne DEĞİŞTİ"dir; atlanmak tam olarak hiçbir şeyin değişmemesidir. Bilgi ayrıca liste
/// satırındaki will-build noktasında, şerit sayacında ve konsolda zaten vardır.</para>
///
/// <para>Bu dosya o kararı pinler: eski davranış sessizce silinmedi, tersine çevrildi ve tersi test edildi.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphSkippedProjectTests
{
    private static IReadOnlyList<GraphNode> Nodes(GraphStatus data) =>
        [new("OSYS.Base", 0, GraphStatus.Building), new("OSYS.Data", 1, data)];

    private static GraphView Running()
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph(Nodes(GraphStatus.Queued), [new("OSYS.Base", "OSYS.Data")]);
        view.RunPhase = GraphRunPhase.Running;
        return view;
    }

    /// <summary>AYIRT EDİCİ — atlanan düğüm parlak beklemez: düz 280ms geçişle sonuç opaklığına iner.</summary>
    [StaFact]
    public void A_skipped_project_glides_straight_to_its_result_with_no_bright_hold()
    {
        var view = Running();

        view.UpdateStatuses(Nodes(GraphStatus.Skipped));

        var animation = Assert.IsType<DoubleAnimationUsingKeyFrames>(view.OpacityAnimationOf("OSYS.Data"));
        var only = Assert.Single(animation.KeyFrames.Cast<DoubleKeyFrame>());
        Assert.Equal(TimeSpan.FromMilliseconds(GraphNodeOpacity.GlideMs), only.KeyTime.TimeSpan);
        Assert.Equal(GraphNodeOpacity.Finished, only.Value, 6);
    }

    /// <summary>AYIRT EDİCİ — atlanan düğümde yörünge HİÇ kurulmaz. Yörünge yalnız DERLENEN düğümün
    /// işaretidir.</summary>
    [StaFact]
    public void A_skipped_project_never_gets_a_bead_orbit()
    {
        var view = Running();

        view.UpdateStatuses(Nodes(GraphStatus.Skipped));

        Assert.Null(view.NodeVisuals["OSYS.Data"].Beads);
        Assert.NotNull(view.NodeVisuals["OSYS.Base"].Beads); // derlenen düğümünki DURUYOR
    }

    /// <summary>Toplu atlanma da sessizdir: hiçbir şeyin değişmediği bir koşuda planlayıcı hepsini tek
    /// tick'te işaretler — hiçbiri gecikmez, hiçbiri parlamaz.</summary>
    [StaFact]
    public void A_whole_workspace_being_skipped_stays_silent()
    {
        var (nodes, _) = SyntheticGraph.Build(24, 4, 2.0);
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph([.. nodes.Select(n => n with { Status = GraphStatus.Queued })], []);
        view.RunPhase = GraphRunPhase.Running;

        view.UpdateStatuses([.. nodes.Select(n => n with { Status = GraphStatus.Skipped })]);

        Assert.All(nodes, n => Assert.Null(view.NodeVisuals[n.Name].Beads));
        Assert.All(nodes, n => Assert.Equal(TimeSpan.Zero, view.OpacityAnimationOf(n.Name)!.BeginTime));
    }

    /// <summary>
    /// Hold-fade'in hedef kümesi: İŞ sonuçları. Atlanmak bir iş sonucu DEĞİLDİR.
    ///
    /// <para><b>Eski iddia:</b> <c>Only_a_result_status_counts_as_settled</c> <see cref="GraphStatus.Skipped"/>'ı
    /// da sonuç sayıyordu. Yukarıdaki gerekçeyle çıkarıldı. Kuralın diğer yarısı — "building'den çıkış" değil
    /// "sonuca varış" — KORUNDU: statü itişi 200ms'de bir gelir ve hızlı bir proje <c>queued → succeeded</c>
    /// diye tek tick'te görünebilir; o düğüm eski kuralla parlak anını hiç almazdı.</para>
    /// </summary>
    [Theory]
    [InlineData(GraphStatus.Succeeded, true)]
    [InlineData(GraphStatus.Failed, true)]
    [InlineData(GraphStatus.Cycle, true)]
    [InlineData(GraphStatus.Skipped, false)]
    [InlineData(GraphStatus.Queued, false)]
    [InlineData(GraphStatus.Building, false)]
    [InlineData(GraphStatus.Discovered, false)]
    public void Only_a_work_result_counts_as_settled(GraphStatus status, bool settled) =>
        Assert.Equal(settled, GraphNodeOpacity.IsSettled(status));

    /// <summary>Kuralın korunan yarısı: derlendiğini hiç GÖRMEDİĞİMİZ ama başarıyla biten bir düğüm de
    /// parlak bekler.</summary>
    [StaFact]
    public void A_project_that_finishes_without_ever_being_seen_building_still_holds_bright()
    {
        var view = Running();

        view.UpdateStatuses([new("OSYS.Base", 0, GraphStatus.Building), new("OSYS.Data", 1, GraphStatus.Succeeded)]);

        var animation = Assert.IsType<DoubleAnimationUsingKeyFrames>(view.OpacityAnimationOf("OSYS.Data"));
        Assert.Equal(3, animation.KeyFrames.Count); // parlak → parlak → sonuç
        Assert.Equal(GraphNodeOpacity.Full, animation.KeyFrames[0].Value, 6);
    }
}
