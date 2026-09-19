using System.Windows;
using System.Windows.Media.Animation;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı kararı 2026-09-19] <b>Filtre açıkken Build: graf koşu boyunca filtreyi yok sayar.</b>
///
/// <para>Kullanıcının kuralı: "Filtrelediğimde grafta yalnız listelenen projeler parlak, diğerleri soluk — bu
/// kalır. Ama Build'e bastığım andan itibaren grafta filtre yok: standart animasyon süreci işler, iş biter,
/// final ile tüm düğümler yanar, çok kısa bir beklemeden sonra filtre moduna döner." Proje LİSTESİ koşu boyunca
/// filtreli kalır; yalnız graf filtreyi askıya alır.</para>
///
/// <para>Askı işlemin başında (<see cref="GraphView.BeginOperation"/> — açılış dalgasından önce) başlar ve
/// koşunun BİTİŞİ tamamlanınca kalkar: final oynadıysa final + kısa bekleme sonunda — Stop ve motor ölümü de
/// buna dahildir (faz <c>Stopped</c>, bir şey derlendiyse finali oynatır); oynamadıysa (derlenen yok, azaltılmış
/// hareket, açılış koreografisinde Stop, hiç gitmeyen komut) koşu bitince. Ekranın baştan başlaması (Sync
/// düğmesi / branch değişimi) oynayan finali keser ve filtreyi anında döndürür. Dönüş filtrenin kendi geçiş
/// süresiyle (<see cref="GraphNodeOpacity.FilterFadeMs"/>) oynar.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphFilterRunSuspendTests
{
    private const string Base = "OSYS.Base";
    private const string Data = "OSYS.Data";

    /// <summary>İki düğümlü graf; filtre yalnız <see cref="Base"/>'i eşler (<see cref="Data"/> filtre dışı).</summary>
    private static GraphView FilteredGraph()
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph(
            [new(Base, Base, 0, GraphStatus.Succeeded), new(Data, Data, 1, GraphStatus.Succeeded)],
            [new(Base, Data)]);
        view.FilterMatches = new HashSet<string>([Base], StringComparer.Ordinal);
        Assert.Equal(GraphNodeOpacity.Unfocused, view.NodeVisuals[Data].OpacityTarget, 6); // ön-koşul: filtre etkin
        return view;
    }

    /// <summary>Bir koşunun grafa düşen iskeleti: işlem başlar, koşu fazına girilir, statüler akar. Koşuya
    /// girişte statüler sönme bitene dek bekletilir (<c>HoldStatusesUntilDimmed</c>) — pompa o beklemeyi geçer.</summary>
    private static void StartRun(GraphView view, GraphStatus baseStatus, GraphStatus dataStatus)
    {
        view.BeginOperation();
        view.RunPhase = GraphRunPhase.Running;
        view.UpdateStatuses([new(Base, Base, 0, baseStatus), new(Data, Data, 1, dataStatus)]);
        DispatcherPump.PumpUntil(() => view.NodeVisuals[Data].Model.Status == dataStatus, TimeSpan.FromSeconds(2));
        Assert.Equal(dataStatus, view.NodeVisuals[Data].Model.Status); // ön-koşul: statüler uygulandı
    }

    private static TimeSpan GlideOf(GraphView view, string id) =>
        Assert.IsType<DoubleAnimationUsingKeyFrames>(view.OpacityAnimationOf(id))
            .KeyFrames.Cast<DoubleKeyFrame>().Single().KeyTime.TimeSpan;

    /// <summary>
    /// Koşu sürerken filtre DIŞINDA derlenen düğümün gövdesi filtre tarafından 0.1'e bastırılmaz — koşu kuralı
    /// uygulanır (derlenen tam opak). Kuyruktaki filtre dışı düğüm de koşunun soluk seviyesindedir
    /// (<see cref="GraphNodeOpacity.RunDim"/>), filtrenin 0.1'inde değil.
    /// </summary>
    [StaFact]
    public void During_a_run_a_node_outside_the_filter_follows_the_run_rule()
    {
        var view = FilteredGraph();

        StartRun(view, GraphStatus.Queued, GraphStatus.Building);

        Assert.Equal(GraphNodeOpacity.Full, view.NodeVisuals[Data].OpacityTarget, 6);   // derleniyor → parlak
        Assert.Equal(GraphNodeOpacity.RunDim, view.NodeVisuals[Base].OpacityTarget, 6); // kuyrukta → koşu soluğu
    }

    /// <summary>
    /// Final bitince graf filtreye HEMEN dönmez: final görünümü (hepsi tam opak) kısa bir bekleme boyunca durur,
    /// SONRA filtre dışı düğümler filtrenin kendi süresiyle <see cref="GraphNodeOpacity.Unfocused"/>'a söner.
    /// </summary>
    [StaFact]
    public void After_the_end_finale_and_a_short_hold_the_graph_returns_to_the_filter()
    {
        var view = FilteredGraph();
        StartRun(view, GraphStatus.Queued, GraphStatus.Building);
        view.UpdateStatuses([new(Base, Base, 0, GraphStatus.Skipped), new(Data, Data, 1, GraphStatus.Succeeded)]);
        view.RunPhase = GraphRunPhase.Idle;

        view.PlayEndFinale([Data], runCount: 1);
        view.AdvanceEndFinaleForTest(EndFinale.TotalMs(1)); // final bitti — duvar saati beklenmez
        Assert.Equal(EndStep.None, view.EndStep);

        Assert.Equal(GraphNodeOpacity.Full, view.NodeVisuals[Data].OpacityTarget, 6); // final görünümü bekler
        view.AdvanceEndFinaleForTest(EndFinale.FilterReturnAtMs(1) - 1);
        Assert.Equal(GraphNodeOpacity.Full, view.NodeVisuals[Data].OpacityTarget, 6); // bekleme sürüyor

        view.AdvanceEndFinaleForTest(EndFinale.FilterReturnAtMs(1));
        Assert.Equal(GraphNodeOpacity.Unfocused, view.NodeVisuals[Data].OpacityTarget, 6);
        Assert.Equal(GraphNodeOpacity.Full, view.NodeVisuals[Base].OpacityTarget, 6);
        Assert.Equal(TimeSpan.FromMilliseconds(GraphNodeOpacity.FilterFadeMs), GlideOf(view, Data));
    }

    /// <summary>Final hiç oynamazsa (derlenen yok) koşu bitince filtre hemen, filtrenin kendi süresiyle döner.</summary>
    [StaFact]
    public void A_run_without_a_finale_returns_to_the_filter_when_it_ends()
    {
        var view = FilteredGraph();
        StartRun(view, GraphStatus.Skipped, GraphStatus.Skipped);
        view.RunPhase = GraphRunPhase.Idle;
        Assert.Equal(GraphNodeOpacity.Full, view.NodeVisuals[Data].OpacityTarget, 6); // koşu bitti, askı sürüyor

        view.PlayEndFinale([], runCount: 1);

        Assert.Equal(GraphNodeOpacity.Unfocused, view.NodeVisuals[Data].OpacityTarget, 6);
        Assert.Equal(TimeSpan.FromMilliseconds(GraphNodeOpacity.FilterFadeMs), GlideOf(view, Data));
    }

    /// <summary>Koşu sırasında kullanıcının değiştirdiği filtre grafa ASKI kalkınca yansır (liste onu anında
    /// gösterir — VM tarafı).</summary>
    [StaFact]
    public void A_filter_change_during_a_run_reaches_the_graph_when_the_run_ends()
    {
        var view = FilteredGraph();
        StartRun(view, GraphStatus.Building, GraphStatus.Queued);

        view.FilterMatches = new HashSet<string>([Data], StringComparer.Ordinal); // koşu sürerken filtre değişti
        Assert.Equal(GraphNodeOpacity.Full, view.NodeVisuals[Base].OpacityTarget, 6);

        view.RunPhase = GraphRunPhase.Idle;
        view.PlayEndFinale([], runCount: 1);

        Assert.Equal(GraphNodeOpacity.Unfocused, view.NodeVisuals[Base].OpacityTarget, 6);
        Assert.Equal(GraphNodeOpacity.Full, view.NodeVisuals[Data].OpacityTarget, 6);
    }

    /// <summary>
    /// [fix round 1 · regresyon guard'ı] Final bittikten sonraki kısa beklemede (<see cref="EndFinale.FilterReturnAtMs"/>)
    /// İKİNCİ bir Build başlarsa askı SÜRER: bekleyen dönüş adımı iptal edilir ama yeni işlem askıyı yeniden kurar —
    /// graf ikinci koşunun ortasında filtreye dönmez. İkinci koşunun bitişi askıyı kaldırır.
    /// </summary>
    [StaFact]
    public void A_second_build_during_the_post_finale_hold_keeps_the_suspension()
    {
        var view = FilteredGraph();
        StartRun(view, GraphStatus.Queued, GraphStatus.Building);
        view.UpdateStatuses([new(Base, Base, 0, GraphStatus.Skipped), new(Data, Data, 1, GraphStatus.Succeeded)]);
        view.RunPhase = GraphRunPhase.Idle;
        view.PlayEndFinale([Data], runCount: 1);
        view.AdvanceEndFinaleForTest(EndFinale.TotalMs(1));
        Assert.Equal(EndStep.None, view.EndStep);
        Assert.True(view.IsFilterSuspended); // ön-koşul: bekleme penceresindeyiz

        view.BeginOperation(); // ikinci Build
        // Dönüş adımının anı geçer: iptal edilmeseydi askıyı burada kaldırırdı.
        view.AdvanceEndFinaleForTest(EndFinale.FilterReturnAtMs(1));

        Assert.True(view.IsFilterSuspended);
        Assert.Equal(GraphNodeOpacity.Full, view.NodeVisuals[Data].OpacityTarget, 6);

        view.EndOperation(); // ikinci koşu bitti (final yok)
        Assert.False(view.IsFilterSuspended);
        Assert.Equal(GraphNodeOpacity.Unfocused, view.NodeVisuals[Data].OpacityTarget, 6);
    }

    /// <summary>
    /// [final review I-2] Final OYNARKEN ekran baştan başlarsa (Sync düğmesi / branch değişimi —
    /// <see cref="GraphView.CancelEndFinale"/> + boş graf) final ANINDA kesilir ve askı kalkar: yeni grafın
    /// reveal'i filtreli görünür kümeyle oynar. Kesilmeseydi final adımları yeni düğümlerin gövde opaklığını
    /// boyamayı sürdürür, filtre de reveal'den sonra geç sönerdi.
    /// </summary>
    [StaFact]
    public void A_restart_blank_during_the_finale_cuts_it_and_the_reveal_plays_under_the_filter()
    {
        var view = FilteredGraph();
        StartRun(view, GraphStatus.Queued, GraphStatus.Building);
        view.UpdateStatuses([new(Base, Base, 0, GraphStatus.Skipped), new(Data, Data, 1, GraphStatus.Succeeded)]);
        view.RunPhase = GraphRunPhase.Idle;
        view.PlayEndFinale([Data], runCount: 1);
        Assert.NotEqual(EndStep.None, view.EndStep); // ön-koşul: final oynuyor

        view.CancelEndFinale();                 // ekran baştan başlıyor (BlankPlanSurface)
        view.SetGraph([], [], showEmptyState: false);

        Assert.Equal(EndStep.None, view.EndStep);
        Assert.False(view.IsFilterSuspended);

        view.SetGraph( // Sync'in getirdiği yeni graf
            [new(Base, Base, 0, GraphStatus.Succeeded), new(Data, Data, 1, GraphStatus.Succeeded)],
            [new(Base, Data)]);
        Assert.Equal(GraphNodeOpacity.Unfocused, view.NodeVisuals[Data].OpacityTarget, 6);

        // Kesilen finalin kalan adımları sonradan düşmez. Ara an da denetlenir: finalin son adımları EndStep'i
        // None'a, filtreyi geri getirdiği için yalnız sona bakan bir iddia dirilen bir finali göremezdi.
        view.AdvanceEndFinaleForTest(EndFinale.StepAtMs(EndStep.Grey, 1));
        Assert.Equal(EndStep.None, view.EndStep);
        Assert.Equal(GraphNodeOpacity.Unfocused, view.NodeVisuals[Data].OpacityTarget, 6);
        view.AdvanceEndFinaleForTest(EndFinale.FilterReturnAtMs(1));
        Assert.Equal(EndStep.None, view.EndStep);
        Assert.Equal(GraphNodeOpacity.Unfocused, view.NodeVisuals[Data].OpacityTarget, 6);
    }

    /// <summary>Kontrol: filtre YOKKEN koşu opaklıkları değişmez (askının etkisi yalnız filtre dalındadır).</summary>
    [StaFact]
    public void Without_a_filter_the_run_opacities_are_unchanged()
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph(
            [new(Base, Base, 0, GraphStatus.Succeeded), new(Data, Data, 1, GraphStatus.Succeeded)],
            [new(Base, Data)]);

        StartRun(view, GraphStatus.Queued, GraphStatus.Building);
        Assert.Equal(GraphNodeOpacity.Full, view.NodeVisuals[Data].OpacityTarget, 6);
        Assert.Equal(GraphNodeOpacity.RunDim, view.NodeVisuals[Base].OpacityTarget, 6);

        view.RunPhase = GraphRunPhase.Idle;
        view.PlayEndFinale([], runCount: 1);
        Assert.Equal(GraphNodeOpacity.Full, view.NodeVisuals[Data].OpacityTarget, 6);
        Assert.Equal(GraphNodeOpacity.Full, view.NodeVisuals[Base].OpacityTarget, 6);
    }

    // ================================================================ kabuk kablajı (üretim yolu)

    private static GraphNodeVisual VisualOf(MainWindow window, string name) =>
        window.Shell.GraphHost.NodeVisuals[MainWindowHost.IdOf(name)];

    private static IReadOnlyList<string> VisibleNames(RunViewModel vm) => [.. vm.VisibleProjects.Select(p => p.Name)];

    /// <summary>
    /// Üretim yolu: arama filtresi açıkken Build → liste filtreli KALIR, graf filtre dışı derlenen düğümü parlak
    /// gösterir; koşu bitince (bu kabukta hareket kapalı → final yok) graf filtreye döner.
    /// </summary>
    [StaFact]
    public async Task A_build_with_a_filter_keeps_the_list_filtered_but_the_graph_ignores_it_until_the_run_ends()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("Alpha", null), ("Beta", null));
        vm.ProjectQuery = "Alpha";
        Assert.Equal(GraphNodeOpacity.Unfocused, VisualOf(window, "Beta").OpacityTarget, 6); // ön-koşul

        MainWindowHost.AcceptSends(vm);
        await vm.BuildCommand.ExecuteAsync(null);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 2, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", MainWindowHost.IdOf("Beta"), "Beta"));

        Assert.Equal(new[] { "Alpha" }, VisibleNames(vm));                                 // liste filtreli
        Assert.Equal("Alpha", vm.ProjectQuery);
        Assert.Equal(GraphNodeOpacity.Full, VisualOf(window, "Beta").OpacityTarget, 6);     // graf filtreyi yok sayar

        vm.OnEvent(new ProjectSucceededEvent("r1", MainWindowHost.IdOf("Beta"), 100));
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 1, 0, 100));

        Assert.Equal(GraphNodeOpacity.Unfocused, VisualOf(window, "Beta").OpacityTarget, 6); // koşu bitti → filtre
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Koşu hiç BAŞLAMAZSA da (gönderim düştü — motor yok) graf filtreye döner: askı yalnız final/Done yoluna
    /// bağlı olsaydı graf filtresiz asılı kalırdı.
    /// </summary>
    [StaFact]
    public async Task A_build_that_never_starts_returns_the_graph_to_the_filter()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("Alpha", null), ("Beta", null));
        vm.ProjectQuery = "Alpha";

        await vm.BuildCommand.ExecuteAsync(null); // AcceptSends yok → gönderim senkron düşer

        Assert.False(vm.IsMidRunLocked);                                                      // ön-koşul
        Assert.Equal(GraphNodeOpacity.Unfocused, VisualOf(window, "Beta").OpacityTarget, 6);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [fix round 1 · regresyon guard'ı] Açılış koreografisi SÜRERKEN Stop (<c>CancelPendingRun</c> — "Cancelled —
    /// build not started"): komut hiç gitmez, faz Idle'a döner, final oynamaz — graf yine filtreye döner. Bu
    /// kabukta hareket kapalı ve koreografi anında biter; pencereyi açık tutmak için üretim kapısı (askıyı kuran
    /// <c>BeginOperation</c> dahil) sarılır ve bitişi test tarafından bekletilir.
    /// </summary>
    [StaFact]
    public async Task Stop_during_the_opening_choreography_returns_the_graph_to_the_filter()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("Alpha", null), ("Beta", null));
        vm.ProjectQuery = "Alpha";
        MainWindowHost.AcceptSends(vm);
        var production = vm.OperationChoreography!;
        var gate = new TaskCompletionSource();
        vm.OperationChoreography = async scope => { await production(scope); await gate.Task; };

        var build = vm.BuildCommand.ExecuteAsync(null);
        Assert.True(window.Shell.GraphHost.IsFilterSuspended);                            // ön-koşul: askıda
        // Koşu fazındayız: filtre dışı düğüm koşu kuralındadır (RunDim), filtrenin 0.1'inde değil.
        Assert.Equal(GraphNodeOpacity.RunDim, VisualOf(window, "Beta").OpacityTarget, 6);

        await vm.StopCommand.ExecuteAsync(null);
        gate.SetResult();
        await build;

        Assert.Contains("Cancelled — build not started", vm.GetRunDocumentText(), StringComparison.Ordinal);
        Assert.False(window.Shell.GraphHost.IsFilterSuspended);
        Assert.Equal(GraphNodeOpacity.Unfocused, VisualOf(window, "Beta").OpacityTarget, 6);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Motor koşu ORTASINDA ölürse (faz Stopped → final yolu; hareket kapalı → final yok) graf filtreye döner.
    /// </summary>
    [StaFact]
    public async Task An_engine_death_mid_run_returns_the_graph_to_the_filter()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("Alpha", null), ("Beta", null));
        vm.ProjectQuery = "Alpha";
        MainWindowHost.AcceptSends(vm);
        await vm.BuildCommand.ExecuteAsync(null);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 2, "Debug", 0));
        vm.OnEvent(new ProjectStartedEvent("r1", MainWindowHost.IdOf("Beta"), "Beta"));
        Assert.Equal(GraphNodeOpacity.Full, VisualOf(window, "Beta").OpacityTarget, 6); // ön-koşul: askıda

        vm.OnEngineExited(1);

        Assert.Equal(GraphNodeOpacity.Unfocused, VisualOf(window, "Beta").OpacityTarget, 6);
        GC.KeepAlive(window);
    }
}
