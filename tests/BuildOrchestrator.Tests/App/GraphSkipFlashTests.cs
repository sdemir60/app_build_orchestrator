using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Animation;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [quiet · atlanan proje] "Derlerken atlanan projeler var; onlar da işlem gördüğü belli olsun, diğerleri
/// gibi olsun — sanki bunlar derlenmedi hissi veriyor, hâlbuki atlandı, kontrol edildi."
///
/// <para><b>Kusur:</b> §2.3'ün iki canlandırması da BUILDING'e bağlıydı — beads yörüngesi de, "parlak kal
/// sonra sön" (hold-fade) de. Atlanan proje hiç building olmaz: incremental kontrol onu güncel bulur ve
/// <c>queued → skipped</c> diye geçer. Sonuç: o düğüm koşu boyunca 0.13'ten 0.2'ye sessizce kayıyor, tek bir
/// parlak an bile almıyordu.</para>
///
/// <para><b>Kural artık statünün KENDİSİNE bakar</b> (<see cref="GraphNodeOpacity.IsSettled"/>): nasıl
/// gelindiği değil, bir SONUCA gelinmiş olması önemlidir. Atlanan düğüm de aynı amber yörüngeyi tek atımlık
/// alır ve aynı parlak beklemeden geçer.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphSkipFlashTests(ITestOutputHelper output)
{
    private static IReadOnlyList<GraphNode> Nodes(GraphStatus data) =>
        [new("OSYS.Base", 0, GraphStatus.Building), new("OSYS.Data", 1, data)];

    private static GraphView Running(bool animations = true)
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => animations);
        view.SetGraph(Nodes(GraphStatus.Queued), [new("OSYS.Base", "OSYS.Data")]);
        view.RunPhase = GraphRunPhase.Running;
        return view;
    }

    /// <summary>
    /// AYIRT EDİCİ — atlanan düğüm de parlak bekler, sonra söner.
    ///
    /// <para><b>Eski iddia:</b> <c>A_node_that_never_built_gets_the_plain_280ms_glide_not_the_hold_fade</c>
    /// (GraphRunLifecycleTests) tam bu geçişi — <c>queued → skipped</c> — 280ms'lik düz geçişe pinliyordu.
    /// Gerekçesi "hiç parlamamış bir düğüm 2.4 saniye parlar gibi görünmesin"di. Kullanıcı gözlemi bunun
    /// tersini söyledi: parlamamak, atlanmayı "hiç bakılmadı" gibi gösteriyor. O test yeni kuralı
    /// pinleyecek şekilde YERİNDE yeniden yazıldı.</para>
    /// </summary>
    [StaFact]
    public void A_skipped_project_holds_bright_and_then_fades_just_like_a_built_one()
    {
        var view = Running();

        view.UpdateStatuses(Nodes(GraphStatus.Skipped));

        var animation = Assert.IsType<DoubleAnimationUsingKeyFrames>(view.OpacityAnimationOf("OSYS.Data"));
        // Parlaklığa ÇIKIŞ açıkça yazılır: atlanan düğüm 0.13'ten gelir, "zaten parlaktı" varsayımı yalnız
        // building'den çıkan düğüm için doğruydu.
        Assert.Equal(GraphNodeOpacity.Full, animation.KeyFrames[0].Value, 6);
        Assert.Equal(TimeSpan.Zero, animation.KeyFrames[0].KeyTime.TimeSpan);
        Assert.Equal(GraphNodeOpacity.Full, animation.KeyFrames[1].Value, 6);
        Assert.Equal(TimeSpan.FromMilliseconds(GraphNodeOpacity.SkipHoldMs), animation.KeyFrames[1].KeyTime.TimeSpan);
        var end = Assert.IsType<SplineDoubleKeyFrame>(animation.KeyFrames[2]);
        Assert.Equal(GraphNodeOpacity.Finished, end.Value, 6);
        Assert.Equal(
            TimeSpan.FromMilliseconds(GraphNodeOpacity.SkipHoldMs + GraphNodeOpacity.FadeMs),
            end.KeyTime.TimeSpan);
    }

    /// <summary>Atlanma anlatımı derlemeninkinden KISADIR — kullanıcı kararı: "atlandığı için çok hafif,
    /// görülüp geçilecek seviyede."</summary>
    [Fact]
    public void The_skipped_hold_is_shorter_than_the_built_one() =>
        Assert.True(GraphNodeOpacity.SkipHoldMs < GraphNodeOpacity.HoldMs);

    /// <summary>
    /// AYIRT EDİCİ — aynı tick'te atlanan projeler SIRAYLA yanar, hepsi birden değil.
    ///
    /// <para><b>Neden:</b> atlanan projeler derleme kuyruğuna hiç girmez; hiçbir şeyin değişmediği bir
    /// koşuda planlayıcı hepsini tek tick'te işaretler. Gecikmesiz hâlde yüzlerce düğüm aynı anda yanıp aynı
    /// anda sönüyor ve "sırası geldi, bakıldı, geçildi" yerine tek bir flaş gibi okunuyordu.</para>
    /// </summary>
    [StaFact]
    public void Projects_skipped_in_the_same_tick_ripple_in_build_order()
    {
        var (nodes, _) = SyntheticGraph.Build(24, 4, 2.0);
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph([.. nodes.Select(n => n with { Status = GraphStatus.Queued })], []);
        view.RunPhase = GraphRunPhase.Running;

        view.UpdateStatuses([.. nodes.Select(n => n with { Status = GraphStatus.Skipped })]);

        var delays = nodes
            .Select(n => view.OpacityAnimationOf(n.Name)!.BeginTime!.Value.TotalMilliseconds)
            .ToList();
        Assert.Equal(0, delays[0], 3);
        Assert.Equal(GraphNodeOpacity.SkipStepMs, delays[1], 3);
        Assert.Equal(delays.OrderBy(d => d), delays); // besleme sırası = build-order
        Assert.All(delays, d => Assert.True(d <= GraphNodeOpacity.SkipStaggerCapMs));
        // Yörünge çakışı AYNI sıraya bağlanır — kare ile yörünge ayrı zamanlarda oynayamaz.
        Assert.Equal(delays[1], view.BeadsAnimationOf(nodes[1].Name)!.BeginTime!.Value.TotalMilliseconds, 3);
    }

    /// <summary>Dalga yalnız ATLANANLAR arasındadır: derlenip biten bir düğüm sıraya girmez, o zaten kendi
    /// zamanında bitmiştir.</summary>
    [StaFact]
    public void A_finished_build_is_not_part_of_the_skip_ripple()
    {
        var view = Running();

        view.UpdateStatuses(
            [new("OSYS.Base", 0, GraphStatus.Succeeded), new("OSYS.Data", 1, GraphStatus.Skipped)]);

        Assert.Equal(TimeSpan.Zero, view.OpacityAnimationOf("OSYS.Base")!.BeginTime);
        Assert.Equal(TimeSpan.Zero, view.OpacityAnimationOf("OSYS.Data")!.BeginTime); // dalganın ilki
    }

    /// <summary>Kural statünün kendisinde: sonuç statülerinin HEPSİ (bitti, hata, atlandı, döngü) parlak
    /// bekleme alır; ara statüler almaz.</summary>
    [Theory]
    [InlineData(GraphStatus.Succeeded, true)]
    [InlineData(GraphStatus.Failed, true)]
    [InlineData(GraphStatus.Skipped, true)]
    [InlineData(GraphStatus.Cycle, true)]
    [InlineData(GraphStatus.Queued, false)]
    [InlineData(GraphStatus.Building, false)]
    [InlineData(GraphStatus.Discovered, false)]
    public void Only_a_result_status_counts_as_settled(GraphStatus status, bool settled) =>
        Assert.Equal(settled, GraphNodeOpacity.IsSettled(status));

    /// <summary>
    /// AYIRT EDİCİ — atlanan düğüm de amber yörüngeyi alır, ama TEK ATIMLIK: girer, düğüm parlak dururken
    /// döner, düğüm soluklaşmaya başlarken söner.
    /// </summary>
    [StaFact]
    public void A_skipped_project_gets_the_same_amber_orbit_for_one_beat()
    {
        var view = Running();

        view.UpdateStatuses(Nodes(GraphStatus.Skipped));

        Assert.NotNull(view.NodeVisuals["OSYS.Data"].Beads);
        var flash = Assert.IsType<DoubleAnimationUsingKeyFrames>(view.BeadsAnimationOf("OSYS.Data"));

        Assert.Equal(3, flash.KeyFrames.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(GraphBeads.FadeInMs), flash.KeyFrames[0].KeyTime.TimeSpan);
        Assert.Equal(1.0, flash.KeyFrames[0].Value, 6);
        Assert.Equal(TimeSpan.FromMilliseconds(GraphBeads.SkipFlashHoldMs), flash.KeyFrames[1].KeyTime.TimeSpan);
        Assert.Equal(1.0, flash.KeyFrames[1].Value, 6);
        Assert.Equal(TimeSpan.FromMilliseconds(GraphBeads.SkipFlashTotalMs), flash.KeyFrames[2].KeyTime.TimeSpan);
        Assert.Equal(0.0, flash.KeyFrames[2].Value, 6);

        // Yörünge, derlenen düğümlerle AYNI paylaşımlı saatte döner — noktalar faz-kilitlidir.
        Assert.NotNull(view.BeadsClock);
    }

    /// <summary>Çakışın ömrü, düğümün parlak beklemesiyle KİLİTLİDİR: yörünge tam kare soluklaşmaya
    /// başlarken sönmeye başlar. İkisi ayrı sayılar olsaydı zamanla ayrışırlardı (kopya YASAK).</summary>
    [Fact]
    public void The_flash_is_locked_to_the_nodes_bright_hold()
    {
        Assert.Equal(GraphNodeOpacity.SkipHoldMs, GraphBeads.SkipFlashHoldMs, 6);
        Assert.Equal(GraphBeads.SkipFlashHoldMs + GraphBeads.FadeOutMs, GraphBeads.SkipFlashTotalMs, 6);
        // Paylaşımlı saat çakıştan ÖNCE bırakılamaz: normal çıkış penceresi (700ms) çakışı yarıda keserdi.
        Assert.True(GraphBeads.SkipFlashTotalMs > GraphBeads.SpinAfterStopMs);
    }

    /// <summary>Koşu DIŞINDA atlanma bir olay değildir (ör. sync sonrası ilk besleme): çakış yalnız koşarken
    /// oynar, yoksa graf açılışta kendiliğinden kıpırdardı.</summary>
    [StaFact]
    public void A_skip_outside_a_run_never_flashes()
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph(Nodes(GraphStatus.Queued), []);

        view.UpdateStatuses(Nodes(GraphStatus.Skipped));

        Assert.Null(view.NodeVisuals["OSYS.Data"].Beads);
    }

    /// <summary>Reduced-motion'da yörünge HİÇ doğmaz — çakış da bir dekoratif animasyondur.</summary>
    [StaFact]
    public void Reduced_motion_never_creates_the_skip_orbit()
    {
        var view = Running(animations: false);

        view.UpdateStatuses(Nodes(GraphStatus.Skipped));

        Assert.Null(view.NodeVisuals["OSYS.Data"].Beads);
        Assert.Equal(GraphNodeOpacity.Finished, view.NodeVisuals["OSYS.Data"].Body.Opacity, 6);
    }

    /// <summary>
    /// RİSK ÖLÇÜMÜ — hiçbir şeyin değişmediği bir koşuda projelerin TAMAMI tek tick'te atlanır: 177 düğüm ×
    /// (yörünge kurulumu + çakış animasyonu + hold-fade). Bu, çakışın en pahalı hâlidir.
    ///
    /// <para><b>Hangi bütçe ve NEDEN:</b> <see cref="UiResponsivenessBudgetTests.BudgetMs"/> (tek UI bloğu
    /// tavanı) — gerekçe kardeş ölçümde yazılıdır
    /// (<see cref="GraphRunLifecycleTests.A_whole_workspace_finishing_in_one_tick_stays_inside_the_ui_budget"/>):
    /// senaryo bir koşuda en fazla BİR kez olur, tekrarlayan bir event değildir.</para>
    /// </summary>
    [StaFact]
    public void A_whole_workspace_being_skipped_in_one_tick_stays_inside_the_ui_budget()
    {
        var (nodes, _) = SyntheticGraph.Build(177, 6, 2.2);
        var queued = nodes.Select(n => n with { Status = GraphStatus.Queued }).ToList();
        var skipped = nodes.Select(n => n with { Status = GraphStatus.Skipped }).ToList();

        double median = PerfMeasure.MedianOf(
            () =>
            {
                var view = GraphTestView.Realized(new Size(900, 520), () => true);
                view.SetGraph(queued, []);
                view.RunPhase = GraphRunPhase.Running;

                var sw = Stopwatch.StartNew();
                view.UpdateStatuses(skipped);
                sw.Stop();
                return sw.Elapsed.TotalMilliseconds;
            },
            warmups: 2, samples: 5);

        output.WriteLine($"[quiet] 177 düğüm × (yörünge + çakış + hold-fade) medyanı = {median:F1} ms " +
            $"(tek-blok bütçesi {UiResponsivenessBudgetTests.BudgetMs:N0} ms)");
        Assert.True(median < UiResponsivenessBudgetTests.BudgetMs,
            $"toplu atlanma bloğu bütçeyi aştı: {median:F1} ms");
    }
}
