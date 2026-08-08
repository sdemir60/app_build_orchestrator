using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Animation;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// design v1.3.0 §2.3 "Koşu yaşam döngüsü" — WPF kablajı. Saf karar
/// <see cref="GraphNodeOpacityTests"/>'te; burada ZAMANLAMA yaşar.
///
/// <para>§2.3: "Proje bitince sonuç rengine döner ve tam opak KALIR, sonra <b>700ms'de 0.2'ye</b> söner.
/// Uygulama hilesi: opacity değeri anında 0.2 yazılır, beklemeyi CSS taşır → <c>transition: opacity 700ms
/// var(--ease-standard) hold</c> (timer/ek render yok)." WPF karşılığı <c>BeginTime</c>'lı TEK ATIMLIK bir
/// animasyondur — aynı gerekçeyle ne timer ne ek render turu. Bekleme süresi
/// <see cref="GraphNodeOpacity.HoldMs"/>'te yaşar (§2.3'ün 2400'ü kullanıcı kararıyla kısaldı); sayı buraya
/// GÖMÜLMEZ.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphRunLifecycleTests(ITestOutputHelper output)
{
    private static IReadOnlyList<GraphNode> Nodes(GraphStatus baseStatus, GraphStatus dataStatus) =>
        [new("OSYS.Base", 0, baseStatus), new("OSYS.Data", 1, dataStatus)];

    private static GraphView Running(bool animations = true)
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => animations);
        view.SetGraph(Nodes(GraphStatus.Building, GraphStatus.Queued), [new("OSYS.Base", "OSYS.Data")]);
        view.RunPhase = GraphRunPhase.Running;
        return view;
    }

    [StaFact]
    public void A_run_that_starts_fades_the_queued_nodes_and_keeps_the_building_one_bright()
    {
        var view = Running(animations: false);

        Assert.Equal(1.0, view.NodeVisuals["OSYS.Base"].Body.Opacity, 6);
        Assert.Equal(GraphNodeOpacity.RunDim, view.NodeVisuals["OSYS.Data"].Body.Opacity, 6);
    }

    /// <summary>
    /// AYIRT EDİCİ — hold-fade. Building'den çıkan düğüm parlak bekleyip sonra 0.2'ye söner; bekleme
    /// keyframe'lerle taşınır, bir timer DEĞİL.
    ///
    /// <para><b>Eski iddia:</b> bekleme bir <c>BeginTime</c>'dı ve animasyonun İKİ keyframe'i vardı
    /// (<c>Discrete(1)@0</c> + <c>Spline(0.2)@700</c>); parlaklık, düğümün bekleme boyunca ZATEN parlak
    /// olmasına bırakılıyordu. <b>Değişme gerekçesi:</b> o varsayım yalnız building'den çıkan düğüm için
    /// doğru. Atlanan düğüm 0.13'ten gelir — parlaklığa onu bir şeyin ÇIKARMASI gerekir, yoksa hiç
    /// parlamadan söner. Bekleme artık üçüncü bir keyframe'de AÇIKÇA yazılı ve <c>BeginTime</c> dalgadaki
    /// sıraya (atlanma için) ayrıldı. Görünen davranış değişmedi.</para>
    /// </summary>
    [StaFact]
    public void A_node_that_leaves_building_holds_bright_and_then_fades_to_0_2()
    {
        var view = Running();

        view.UpdateStatuses(Nodes(GraphStatus.Succeeded, GraphStatus.Queued));

        var animation = Assert.IsType<DoubleAnimationUsingKeyFrames>(view.OpacityAnimationOf("OSYS.Base"));
        Assert.Equal(TimeSpan.Zero, animation.BeginTime); // derlenen düğüm dalgaya girmez

        // Süre son key frame'in zamanıdır (Duration = Automatic). Bekleme boyunca değer TAM OPAK tutulur
        // (CSS `both` fill paritesi), sonunda 0.2'ye iner.
        var start = Assert.IsType<DiscreteDoubleKeyFrame>(animation.KeyFrames[0]);
        Assert.Equal(GraphNodeOpacity.Full, start.Value, 6);
        Assert.Equal(TimeSpan.Zero, start.KeyTime.TimeSpan);
        var hold = Assert.IsType<DiscreteDoubleKeyFrame>(animation.KeyFrames[1]);
        Assert.Equal(GraphNodeOpacity.Full, hold.Value, 6);
        Assert.Equal(TimeSpan.FromMilliseconds(GraphNodeOpacity.HoldMs), hold.KeyTime.TimeSpan);
        var end = Assert.IsType<SplineDoubleKeyFrame>(animation.KeyFrames[2]);
        Assert.Equal(GraphNodeOpacity.Finished, end.Value, 6);
        Assert.Equal(
            TimeSpan.FromMilliseconds(GraphNodeOpacity.HoldMs + GraphNodeOpacity.FadeMs), end.KeyTime.TimeSpan);
    }

    /// <summary>
    /// Hold-fade yalnız SONUÇ statüsüne girişte doğar: koşu içindeki ara geçişler (discovered → queued)
    /// normal 280ms'lik düz geçişi kullanır.
    ///
    /// <para><b>Eski iddia:</b> bu test <c>A_node_that_never_built_gets_the_plain_280ms_glide_not_the_hold_fade</c>
    /// adıyla <c>queued → skipped</c> geçişini düz geçişe pinliyordu; gerekçesi "hiç parlamamış bir düğüm
    /// 2.4 saniye parlar gibi görünmesin"di. <b>Değişme gerekçesi:</b> gerçek koşuda gözlenen sonuç tersiydi —
    /// atlanan projeler hiç canlanmadığı için "bunlar hiç işlem görmedi" hissi veriyordu, oysa atlanmak bir
    /// karardır. Kural artık <see cref="GraphNodeOpacity.IsSettled"/>'dır ve atlanan düğüm de parlak
    /// bekler; o davranış <see cref="GraphSkipFlashTests"/>'te pinlidir. Test burada YENİ kuralın ara-geçiş
    /// tarafını tutuyor.</para>
    /// </summary>
    [StaFact]
    public void A_transition_that_lands_on_no_result_gets_the_plain_280ms_glide()
    {
        var view = Running();

        view.UpdateStatuses([new("OSYS.Base", 0, GraphStatus.Building), new("OSYS.Data", 1, GraphStatus.Discovered)]);

        var animation = Assert.IsType<DoubleAnimationUsingKeyFrames>(view.OpacityAnimationOf("OSYS.Data"));
        Assert.Equal(TimeSpan.Zero, animation.BeginTime); // bekleme YOK
        var only = Assert.Single(animation.KeyFrames.Cast<DoubleKeyFrame>());
        Assert.Equal(TimeSpan.FromMilliseconds(GraphNodeOpacity.GlideMs), only.KeyTime.TimeSpan);
    }

    [StaFact]
    public void Reduced_motion_snaps_the_finished_node_to_0_2_with_no_hold_and_no_animation()
    {
        var view = Running(animations: false);

        view.UpdateStatuses(Nodes(GraphStatus.Succeeded, GraphStatus.Queued));

        Assert.Null(view.OpacityAnimationOf("OSYS.Base"));
        Assert.Equal(GraphNodeOpacity.Finished, view.NodeVisuals["OSYS.Base"].Body.Opacity, 6);
    }

    /// <summary>Değişmeyen bir tick hold-fade'i YENİDEN BAŞLATMAZ — koşarken statü itişi saniyede birkaç
    /// kez gelir ve her seferinde 3.1 saniyelik bir animasyon doğurmak sönmeyi sonsuza dek erteler.</summary>
    [StaFact]
    public void A_status_tick_that_changes_nothing_never_restarts_the_hold_fade()
    {
        var view = Running();
        view.UpdateStatuses(Nodes(GraphStatus.Succeeded, GraphStatus.Queued));
        var first = view.OpacityAnimationOf("OSYS.Base");
        Assert.NotNull(first);

        for (int i = 0; i < 5; i++) view.UpdateStatuses(Nodes(GraphStatus.Succeeded, GraphStatus.Queued));

        Assert.Same(first, view.OpacityAnimationOf("OSYS.Base"));
    }

    /// <summary>Koşu bitince (done/stopped) tümü sonuç renginde TAM OPAK olur — uçuştaki hold-fade iptal
    /// edilir (§2.3: "Koşu bitince (done/stopped): tümü sonuç renginde tam opak").</summary>
    [StaFact]
    public void Ending_the_run_brings_every_node_back_to_full_opacity_in_its_result_colour()
    {
        var view = Running(animations: false);
        view.UpdateStatuses(Nodes(GraphStatus.Succeeded, GraphStatus.Skipped));
        Assert.Equal(GraphNodeOpacity.Finished, view.NodeVisuals["OSYS.Base"].Body.Opacity, 6);

        view.RunPhase = GraphRunPhase.Idle;

        Assert.Equal(1.0, view.NodeVisuals["OSYS.Base"].Body.Opacity, 6);
        Assert.Equal(1.0, view.NodeVisuals["OSYS.Data"].Body.Opacity, 6);
    }

    /// <summary>Seçim koşu kararını EZER ve seçim kalkınca koşu kararı geri gelir — iki sistem birbirini
    /// latch'lemez.</summary>
    [StaFact]
    public void A_selection_overrides_the_run_dim_and_gives_it_back_when_released()
    {
        var view = Running(animations: false);

        view.SelectedNode = "OSYS.Data";
        Assert.Equal(1.0, view.NodeVisuals["OSYS.Data"].Body.Opacity, 6);  // odakta (seçili)
        Assert.Equal(1.0, view.NodeVisuals["OSYS.Base"].Body.Opacity, 6);  // odakta (doğrudan bağımlılık)

        view.SelectedNode = null;
        Assert.Equal(GraphNodeOpacity.RunDim, view.NodeVisuals["OSYS.Data"].Body.Opacity, 6);
        Assert.Equal(1.0, view.NodeVisuals["OSYS.Base"].Body.Opacity, 6);  // building → parlak
    }

    /// <summary>
    /// RİSK ÖLÇÜMÜ — en kötü durum: 177 projenin TAMAMI aynı tick'te biterse düğüm başına iki animasyon
    /// doğar (beads çıkışı + opaklık hold-fade'i), yani 354 animasyon.
    ///
    /// <para><b>Hangi bütçe ve NEDEN:</b> <see cref="UiResponsivenessBudgetTests.BudgetMs"/> (tek UI bloğu
    /// tavanı), <c>EventBudgetMs</c> DEĞİL. İkisinin ayrımı o dosyada tanımlıdır: <c>EventBudgetMs</c>
    /// "koşan bir run'da TEK event'in tavanı — akış boyunca sürekli tekrarlar" içindir ve grafın TEKRARLAYAN
    /// tick'i onunla zaten ölçülüyor (<c>The_mid_run_graph_status_tick_stays_negligible</c>). Buradaki senaryo
    /// tekrarlamaz: bir koşuda en fazla bir kez olur (hepsi birden biter). Dolayısıyla bu bir TEK BLOKTUR ve
    /// doğru tavan 120 ms'dir. Bu bir eşik gevşetmesi değil, var olan iki eşikten doğru olanının
    /// seçilmesidir.</para>
    ///
    /// <para><b>Ölçüldü:</b> referans makinede ~55 ms. Sayı buraya GÖMÜLMEZ (bayatlar) — çıktıya yazılır.</para>
    /// </summary>
    [StaFact]
    public void A_whole_workspace_finishing_in_one_tick_stays_inside_the_ui_budget()
    {
        var (nodes, _) = SyntheticGraph.Build(177, 6, 2.2);
        var building = nodes.Select(n => n with { Status = GraphStatus.Building }).ToList();
        var done = nodes.Select(n => n with { Status = GraphStatus.Succeeded }).ToList();

        // Warmup + GC + medyan iskeleti kardeş perf testleriyle ORTAK (PerfMeasure): tek atımlık bir
        // ölçüm burada JIT'i de ölçer ve bütçenin kenarında gürültüyle salınır.
        double median = PerfMeasure.MedianOf(
            () =>
            {
                var view = GraphTestView.Realized(new Size(900, 520), () => true);
                view.SetGraph(building, []);
                view.RunPhase = GraphRunPhase.Running;

                var sw = Stopwatch.StartNew();
                view.UpdateStatuses(done);
                sw.Stop();
                return sw.Elapsed.TotalMilliseconds;
            },
            warmups: 2, samples: 5);

        output.WriteLine($"[quiet] 177 düğüm × (beads çıkışı + hold-fade) medyanı = {median:F1} ms " +
            $"(tek-blok bütçesi {UiResponsivenessBudgetTests.BudgetMs:N0} ms)");
        Assert.True(median < UiResponsivenessBudgetTests.BudgetMs,
            $"en kötü durum statü bloğu bütçeyi aştı: {median:F1} ms");
    }
}
