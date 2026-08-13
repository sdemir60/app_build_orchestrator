using System.Windows;
using System.Windows.Media.Animation;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.7.0 — Filtreleme] Listede bir filtre (ya da arama) etkinken graf da süzülür: eşleşmeyen düğümler
/// söner, eşleşenler tam opak kalır. Eşleşme kümesi DIŞARIDAN gelir — kural <c>ProjectFilter.Matches</c>'tır ve
/// liste ile graf onu paylaşır; graf ikinci bir eşleme yazmaz.
///
/// <para><b>Neden bu dosya var:</b> filtre sönmesinin GraphView tarafı hiç test edilmemişti — yalnız saf
/// karar (<see cref="GraphNodeOpacity.Resolve"/>) pinliydi, kablonun kendisi (küme verilince düğümlerin
/// gerçekten sönmesi) değil.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphFilterDimTests
{
    private static GraphView Realized()
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph(
            [new("OSYS.Base", 0, GraphStatus.Succeeded), new("OSYS.Data", 1, GraphStatus.Succeeded)],
            [new("OSYS.Base", "OSYS.Data")]);
        return view;
    }

    [StaFact]
    public void A_filter_dims_the_nodes_it_does_not_match()
    {
        var view = Realized();

        view.FilterMatches = new HashSet<string>(["OSYS.Base"], StringComparer.Ordinal);

        Assert.Equal(GraphNodeOpacity.Full, view.NodeVisuals["OSYS.Base"].OpacityTarget, 6);
        Assert.Equal(GraphNodeOpacity.Unfocused, view.NodeVisuals["OSYS.Data"].OpacityTarget, 6);
    }

    [StaFact]
    public void Clearing_the_filter_brings_everything_back()
    {
        var view = Realized();
        view.FilterMatches = new HashSet<string>(["OSYS.Base"], StringComparer.Ordinal);

        view.FilterMatches = null;

        Assert.Equal(GraphNodeOpacity.Full, view.NodeVisuals["OSYS.Data"].OpacityTarget, 6);
    }

    /// <summary>
    /// Filtre geçişi koşu tikinden UZUNDUR ve iki yönde de aynıdır.
    ///
    /// <para>Gerekçe: koşu sırasındaki opaklık geçişleri bir durum değişimini bildirir ve saniyede birkaç kez
    /// olur — kısa olmalıdır (§2.3: 280ms). Filtre ise kullanıcının kendi hareketidir, tek seferliktir ve
    /// grafın yarısını birden söndürür; kullanıcı "biraz daha animasyonlu solsun, geri gelsin" dedi. Süre
    /// ayrımı bu yüzden bilinçli — tasarımın 280ms'inden sapma burada kayıtlıdır.</para>
    /// </summary>
    [StaFact]
    public void The_filter_fade_is_slower_than_a_run_tick_and_symmetric()
    {
        var view = Realized();

        view.FilterMatches = new HashSet<string>(["OSYS.Base"], StringComparer.Ordinal);
        Assert.Equal(
            TimeSpan.FromMilliseconds(GraphNodeOpacity.FilterFadeMs),
            Assert.IsType<DoubleAnimationUsingKeyFrames>(view.OpacityAnimationOf("OSYS.Data"))
                .KeyFrames.Cast<DoubleKeyFrame>().Single().KeyTime.TimeSpan);

        view.FilterMatches = null; // geri gelirken de AYNI süre
        Assert.Equal(
            TimeSpan.FromMilliseconds(GraphNodeOpacity.FilterFadeMs),
            Assert.IsType<DoubleAnimationUsingKeyFrames>(view.OpacityAnimationOf("OSYS.Data"))
                .KeyFrames.Cast<DoubleKeyFrame>().Single().KeyTime.TimeSpan);

        Assert.True(GraphNodeOpacity.FilterFadeMs > GraphNodeOpacity.GlideMs);
    }

    /// <summary>Kontrol: sıradan bir statü geçişi KISA kalır — filtre süresi oraya sızmaz.</summary>
    [StaFact]
    public void A_plain_status_change_still_uses_the_short_glide()
    {
        var view = Realized();
        view.RunPhase = GraphRunPhase.Running;

        view.UpdateStatuses(
            [new("OSYS.Base", 0, GraphStatus.Building), new("OSYS.Data", 1, GraphStatus.Queued)]);

        Assert.Equal(
            TimeSpan.FromMilliseconds(GraphNodeOpacity.GlideMs),
            Assert.IsType<DoubleAnimationUsingKeyFrames>(view.OpacityAnimationOf("OSYS.Base"))
                .KeyFrames.Cast<DoubleKeyFrame>().Single().KeyTime.TimeSpan);
    }
}
