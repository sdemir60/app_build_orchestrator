using System.Windows;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.11.0 §2.3 "Renk kuralı"] Düğümün ÇEKİRDEĞİ (içteki küp) <b>border'la AYNI</b> görsel durumdan
/// boyanır — ayrı bir "plan" ya da "cycle" çekirdeği YOKTUR.
///
/// <para><b>[DEĞİŞEN KURAL]</b> Bu dosya v1.7.0'ın üç-kanallı modelini pinliyordu: çekirdek plan kanalını
/// söylerdi (amber "derlenecek" / gri "güncel"), kuyruğa alınmak onu DEĞİŞTİRMEZDİ ve döngü üyeliği her şeyi
/// turuncuyla EZERDİ. v1.11.0 §9-1 o kanalları kaldırdı — <i>renk yalnız son işlemin hikâyesini anlatır</i>:
/// plan bilgisi satırın çift SHA metnine, döngü üyeliği liste satırındaki tek amber üçgene indi. Eski
/// iddialar silinmedi, YENİ kurala göre yeniden yazıldı.</para>
///
/// <para><b>Korunan davranış:</b> "basış anında düğüm ÖNCE söner, görünümü SONRA değişir" — o, renk
/// kanallarından bağımsız bir sıra kuralıdır ve aynen duruyor.</para>
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

    private static Color BorderColour(GraphView view, string node) =>
        ((SolidColorBrush)view.NodeVisuals[node].Square.Stroke).Color;

    private static Color Token(GraphView view, string key) =>
        ((SolidColorBrush)view.FindResource(key)).Color;

    /// <summary>[design v1.20.0 §2.3] Sync'ten sonra her node kendi çıktı durumundadır: güncel yeşil, derlenecek
    /// düz gri; kesikli çerçeve yalnız kararı olmayan (hiç Sync görmemiş) node'dadır.
    /// <para><b>[DEĞİŞEN KURAL — design v1.20.0 §2.3]</b> Eski ad/iddia:
    /// <c>After_sync_nothing_is_coloured_because_everyone_is_in_the_fresh_start_mode</c> — Sync'ten sonra HİÇBİR
    /// ŞEY renklenmez, herkes kesikli başlangıç modundadır. Değişme gerekçesi (kullanıcı ölçümü): Sync sonrası
    /// neyin güncel olduğu renkten okunmuyordu. Yeni kural: Sync renk verir.</para></summary>
    [StaFact]
    public void After_sync_every_node_wears_its_standing()
    {
        var view = Realized(
            new("dirty", "dirty", 0, GraphStatus.Discovered, VisualStatus.Stale),
            new("clean", "clean", 0, GraphStatus.Discovered, VisualStatus.Current),
            new("never", "never", 0, GraphStatus.Discovered, VisualStatus.Unknown));

        Assert.Equal(Token(view, "Brush.TextFaint"), CoreColour(view, "dirty"));
        Assert.Equal(Token(view, "Brush.BorderStrong"), BorderColour(view, "dirty"));
        Assert.Empty(view.NodeVisuals["dirty"].Square.StrokeDashArray);    // derlenecek DÜZ gri
        Assert.Equal(Token(view, "Brush.StatusSuccessText"), CoreColour(view, "clean"));
        Assert.Equal(Token(view, "Brush.StatusSuccess"), BorderColour(view, "clean"));
        Assert.NotEmpty(view.NodeVisuals["never"].Square.StrokeDashArray); // karar yok: başlangıç modu KESİKLİ
    }

    /// <summary>Çekirdek ve çerçeve HER durumda aynı aileden boyanır — "tek statü kanalı" iddiasının kendisi.</summary>
    [StaTheory]
    [InlineData(VisualStatus.Marked, "Brush.Amber", "Brush.AmberText")]
    [InlineData(VisualStatus.Queued, "Brush.Amber", "Brush.AmberText")]
    [InlineData(VisualStatus.Building, "Brush.Amber", "Brush.AmberText")]
    [InlineData(VisualStatus.Succeeded, "Brush.StatusSuccess", "Brush.StatusSuccessText")]
    [InlineData(VisualStatus.Failed, "Brush.StatusFail", "Brush.StatusFailText")]
    // [DEĞİŞEN KURAL — design v1.20.0 §2.3 · §1.4] Eski satırlar: Skipped → StatusSkippedBorder/Text ve
    // Discovered → BorderStrong/TextFaint. Değişme gerekçesi: atlanmak artık bir node rengi değildir (— yalnız
    // run-story'de; node kendi çıktı durumunu taşır) ve Discovered'ın yerini çıktı durumu aldı. Yeni satırlar:
    // güncel (yeşil) ve derlenecek (bugünkü nötr gri).
    [InlineData(VisualStatus.Current, "Brush.StatusSuccess", "Brush.StatusSuccessText")]
    [InlineData(VisualStatus.Stale, "Brush.BorderStrong", "Brush.TextFaint")]
    public void The_border_and_the_core_are_painted_from_one_channel(
        VisualStatus state, string borderKey, string coreKey)
    {
        var view = Realized(new GraphNode("n", "n", 0, GraphStatus.Discovered, VisualStatus.Unknown));

        view.UpdateStatuses([new("n", "n", 0, GraphStatus.Discovered, state)]);

        Assert.Equal(Token(view, borderKey), BorderColour(view, "n"));
        Assert.Equal(Token(view, coreKey), CoreColour(view, "n"));
    }

    /// <summary>[design v1.20.0 §2.3] Kesikli çerçeve YALNIZ başlangıç modundadır (karar yok); derlenecek
    /// (<c>stale</c>) DÜZ gridir.
    /// <para><b>[DEĞİŞEN KURAL — design v1.20.0 §2.3]</b> Eski ad: <c>Only_the_fresh_start_mode_is_dashed</c>;
    /// düz gri <c>discovered</c> ("işlem başladı, kapsam dışı") idi. <c>Discovered</c> kalktı, düz gri artık
    /// <c>stale</c>'dir; iddianın kendisi (yalnız başlangıç modu kesikli) aynıdır.</para></summary>
    [StaFact]
    public void Only_the_unknown_start_mode_is_dashed()
    {
        var view = Realized(new GraphNode("n", "n", 0, GraphStatus.Discovered, VisualStatus.Unknown));
        Assert.NotEmpty(view.NodeVisuals["n"].Square.StrokeDashArray);

        view.UpdateStatuses([new("n", "n", 0, GraphStatus.Discovered, VisualStatus.Stale)]);
        DispatcherPump.PumpUntil(
            () => view.NodeVisuals["n"].Square.StrokeDashArray.Count == 0, TimeSpan.FromSeconds(3));

        Assert.Empty(view.NodeVisuals["n"].Square.StrokeDashArray);
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
        var view = Realized(new GraphNode("n", "n", 0, GraphStatus.Discovered, VisualStatus.Unknown));
        var dashedAtRest = view.NodeVisuals["n"].Square.StrokeDashArray;

        view.RunPhase = GraphRunPhase.Running;                                              // basış
        view.UpdateStatuses([new("n", "n", 0, GraphStatus.Queued, VisualStatus.Queued)]);        // plan hemen ardından geldi

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
