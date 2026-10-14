using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 1] <b>Bitiş finalinin BİTTİĞİ olay olarak duyurulur:</b> <see cref="GraphView.EndFinaleFinished"/>.
/// Görünür pencerede işlem sonu bellek trimi, finalin ortasında bir toplamayla animasyonu kesmemek için finalin bittiğini bu
/// olaydan öğrenir (<see cref="GraphView.IsEndFinalePlaying"/> yalnız "şimdi" der, "az önce bitti"yi söylemez).
///
/// <para><b>Sözleşme:</b> olay finalin DOĞAL tamamlanmasında (son adım — filtreye dönüş — oynadıktan sonra) ve final hiç
/// oynamadığında (derlenen yok ya da animasyonlar kapalı: <c>PlayEndFinale</c>'in "final yok" dalı) ateşlenir ve ateşlendiğinde
/// <c>IsEndFinalePlaying</c> zaten false'tur. KESİLEN final ateşlemez: <c>CancelEndFinale</c> (ekran baştan başlıyor / pencere
/// gizleniyor), yeni bir işlem (<c>BeginOperation</c>) ve yeni bir <c>PlayEndFinale</c> eskisini bitirmez, iptal eder.</para>
///
/// <para>İlerletme duvar saatini beklemez: <c>AdvanceEndFinaleForTest</c> (<see cref="GraphFilterRunSuspendTests"/> ile aynı yüzey).</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphEndFinaleSignalTests
{
    private const string Data = GraphTestView.DataNode;

    /// <summary>İki düğümlü graf (<see cref="GraphTestView.TwoNodeGraph"/>); finalin oynayabilmesi için animasyon sinyali AÇIK
    /// (varsayılan headless kapalıdır).</summary>
    private static GraphView FinaleGraph(Func<bool>? animationsEnabled = null) =>
        GraphTestView.TwoNodeGraph(animationsEnabled ?? (() => true));

    /// <summary>Olayı sayar ve her ateşlemede finalin hâlâ oynayıp oynamadığını kaydeder (sözleşme: oynamıyor).</summary>
    private sealed class FinaleProbe
    {
        public int Fired { get; private set; }
        public bool PlayingWhenFired { get; private set; }

        public FinaleProbe(GraphView view) =>
            view.EndFinaleFinished += () => { Fired++; PlayingWhenFired |= view.IsEndFinalePlaying; };
    }

    [StaFact]
    public void The_signal_fires_once_when_the_finale_completes()
    {
        var view = FinaleGraph();
        var probe = new FinaleProbe(view);

        view.PlayEndFinale([Data], runCount: 1);
        Assert.True(view.IsEndFinalePlaying);
        Assert.Equal(0, probe.Fired);                                        // final sürerken bitmiş sayılmaz

        view.AdvanceEndFinaleForTest(EndFinale.FilterReturnAtMs(1) - 1);    // son adımdan (filtreye dönüş) hemen önce
        Assert.True(view.IsEndFinalePlaying);
        Assert.Equal(0, probe.Fired);

        view.AdvanceEndFinaleForTest(EndFinale.FilterReturnAtMs(1));        // son adım oynar
        Assert.False(view.IsEndFinalePlaying);
        Assert.Equal(1, probe.Fired);
        Assert.False(probe.PlayingWhenFired);                                // ateşlendiği anda final zaten bitmiş

        view.AdvanceEndFinaleForTest(EndFinale.FilterReturnAtMs(1) + 10_000);
        Assert.Equal(1, probe.Fired);                                        // bitmiş final bir daha ateşlemez
    }

    /// <summary>Derlenen yoksa final HİÇ oynamaz — "final yok" dalı bitişi hemen duyurur: dinleyen beklenecek bir şey bulamaz.</summary>
    [StaFact]
    public void The_signal_fires_at_once_when_nothing_was_built()
    {
        var view = FinaleGraph();
        var probe = new FinaleProbe(view);

        view.PlayEndFinale([], runCount: 1);

        Assert.False(view.IsEndFinalePlaying);
        Assert.Equal(1, probe.Fired);
        Assert.False(probe.PlayingWhenFired);
    }

    /// <summary>Animasyonlar kapalıyken (azaltılmış hareket) derlenen olsa bile final oynamaz — aynı "final yok" dalı.</summary>
    [StaFact]
    public void The_signal_fires_at_once_when_animations_are_off()
    {
        var view = FinaleGraph(animationsEnabled: () => false);
        var probe = new FinaleProbe(view);

        view.PlayEndFinale([Data], runCount: 1);

        Assert.False(view.IsEndFinalePlaying);
        Assert.Equal(1, probe.Fired);
    }

    /// <summary>Ekran baştan başlarken ya da pencere gizlenirken final KESİLİR (<c>CancelEndFinale</c>) — kesilen bir final
    /// bitmiş sayılmaz ve kalan adımları sonradan düşüp olayı ateşlemez.</summary>
    [StaFact]
    public void Cancelling_the_finale_mid_way_does_not_signal()
    {
        var view = FinaleGraph();
        var probe = new FinaleProbe(view);
        view.PlayEndFinale([Data], runCount: 1);
        view.AdvanceEndFinaleForTest(EndFinale.TotalMs(1));                  // filtre dönüş beklemesinde: tek adım kaldı
        Assert.True(view.IsEndFinalePlaying);

        view.CancelEndFinale();

        Assert.False(view.IsEndFinalePlaying);
        Assert.Equal(0, probe.Fired);
        view.AdvanceEndFinaleForTest(EndFinale.FilterReturnAtMs(1));        // kesilen finalin kalanı oynamaz
        Assert.Equal(0, probe.Fired);
    }

    /// <summary>Yeni bir işlem (<c>BeginOperation</c>) oynayan finali ANINDA keser — bitirmez.</summary>
    [StaFact]
    public void A_new_operation_cutting_the_finale_does_not_signal()
    {
        var view = FinaleGraph();
        var probe = new FinaleProbe(view);
        view.PlayEndFinale([Data], runCount: 1);
        view.AdvanceEndFinaleForTest(EndFinale.TotalMs(1));
        Assert.True(view.IsEndFinalePlaying);

        view.BeginOperation();

        Assert.False(view.IsEndFinalePlaying);
        Assert.Equal(0, probe.Fired);
        view.AdvanceEndFinaleForTest(EndFinale.FilterReturnAtMs(1));
        Assert.Equal(0, probe.Fired);
    }

    /// <summary>Oynayan finalin üstüne yeni bir <c>PlayEndFinale</c> gelirse eskisi iptal edilir ve olay YALNIZ yenisi için,
    /// bir kez ateşlenir.</summary>
    [StaFact]
    public void A_finale_replaced_by_a_new_one_signals_only_for_the_new_one()
    {
        var view = FinaleGraph();
        var probe = new FinaleProbe(view);
        view.PlayEndFinale([Data], runCount: 1);
        view.AdvanceEndFinaleForTest(EndFinale.TotalMs(1));

        view.PlayEndFinale([Data], runCount: 2);                             // sonraki koşunun finali eskisini keser
        Assert.Equal(0, probe.Fired);

        view.AdvanceEndFinaleForTest(EndFinale.FilterReturnAtMs(1));
        Assert.Equal(1, probe.Fired);
    }
}
