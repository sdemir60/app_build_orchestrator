using System.Windows;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.11.0 §9-4 · §9-5 · §2.3] <b>İki koreografi.</b>
///
/// <para><b>Açılış (işaretleme):</b> her işlem — Build · Rebuild · Clean · satır aksiyonu · Resolve — AYNI
/// sırayı oynar: <i>nötr an → RANDOM dalga → sarı-gri an → örtüşen veda → nefes</i>. "Örtüşen veda"nın
/// gerekçesi ölçülmüş: gri büyük bir opaklık düşüşü yaptığı için yolun ortasında "gitti" okunur, bu yüzden
/// sarının süresi kısa tutulur ve ikisi ALGIDA aynı anda biter.</para>
///
/// <para><b>Bitiş ("neon tutuşma"):</b> yalnız grafta — hepsi soluk bekler, derlenenler random sırayla
/// düzensiz titreyerek tutuşur, sonra kalan griler birlikte belirginleşir. Proje listesi bitişte SABİT kalır
/// (kullanıcı kararı).</para>
///
/// <para>Zamanlama saf çekirdeklerde (<see cref="MarkingChoreography"/>/<see cref="EndFinale"/>); bu dosya
/// hem onları hem de sürücünün gerçek satır/graf üzerindeki etkisini pinler.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ChoreographyTests
{
    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), new ConsoleBatcher(_ => Task.Delay(Timeout.Infinite)),
            () => "r1") { RootPath = @"D:\repo" };

    private static ProjectNode Node(string name, int order, bool inCycle = false) =>
        new($@"C:\p\{name}.csproj", name, $@"C:\p\{name}.csproj", ["Osys"], [], order, null, null, inCycle, null);

    // ================================================================ saf çekirdek: açılış

    /// <summary>Dalga temposu: 110ms/node, ama zincir toplamı 1.1s'yi AŞMAZ — 36 projede de kısa kalır.</summary>
    [Theory]
    [InlineData(1, 0)]      // tek üyeli kapsamda gecikme yok
    [InlineData(2, 110)]
    [InlineData(11, 110)]   // 10 aralık × 110 = 1100 → tam sınırda
    [InlineData(36, 31)]    // 35 aralık → 1100/35 ≈ 31ms
    public void The_wave_tempo_is_110ms_per_node_but_the_chain_never_exceeds_1100ms(int count, double expected)
    {
        Assert.Equal(expected, MarkingChoreography.StaggerMs(count));
        Assert.True(MarkingChoreography.StaggerMs(count) * (count - 1) <= MarkingChoreography.MaxWaveMs);
    }

    /// <summary>Adımların sırası ve aralıkları (build-data.js:361-363): nötr 440 → dalga → sarı-gri an →
    /// veda → nefes → koşu.</summary>
    [Fact]
    public void The_steps_run_in_order_and_the_run_starts_after_the_last_one()
    {
        const int n = 4;
        double w = MarkingChoreography.WaveSpanMs(n);

        Assert.Equal(0, MarkingChoreography.StepAtMs(MarkStep.Neutral, n));
        Assert.Equal(440, MarkingChoreography.StepAtMs(MarkStep.Wave, n));
        Assert.Equal(440 + w, MarkingChoreography.StepAtMs(MarkStep.Hold, n));
        Assert.Equal(740 + w, MarkingChoreography.StepAtMs(MarkStep.DimEnv, n));
        Assert.Equal(1300 + w, MarkingChoreography.StepAtMs(MarkStep.Settle, n));
        Assert.Equal(1860 + w, MarkingChoreography.StepAtMs(MarkStep.Wait, n));
        Assert.Equal(2280 + w, MarkingChoreography.StepAtMs(MarkStep.Wait2, n));
        Assert.Equal(2520 + w, MarkingChoreography.TotalMs(n));

        double previous = -1;
        foreach (var step in MarkingChoreography.Steps)
        {
            double at = MarkingChoreography.StepAtMs(step, n);
            Assert.True(at > previous, $"{step} sırayı bozdu");
            previous = at;
        }
    }

    /// <summary>
    /// <b>Örtüşen veda:</b> griler ÖNCE (1120ms) başlar, sarılar 560ms sonra (440ms) katılır ve ALGIDA aynı
    /// anda biterler (sarı 120ms önce tamamlanır). Sarının süresinin kısa olması bir gözden kaçma değil,
    /// ölçülmüş bir karardır.
    /// </summary>
    [Fact]
    public void The_farewell_overlaps_so_the_greys_and_the_ambers_land_together()
    {
        const int n = 4;
        double greyStart = MarkingChoreography.StepAtMs(MarkStep.DimEnv, n);
        double amberStart = MarkingChoreography.StepAtMs(MarkStep.Settle, n);

        Assert.Equal(560, amberStart - greyStart);                       // sarılar 560ms geç başlar
        // ...ve ALGIDA aynı anda biterler: sarı 120ms ÖNCE tamamlanır. Fark bir gözden kaçma değil, ölçülmüş
        // bir karardır — gri büyük bir opaklık düşüşü yaptığı için yolun ortasında "gitti" okunur
        // (BuildApp.jsx:500 "sarılar ~120ms önce biter — algıda eşzamanlı").
        double greyEnds = greyStart + MarkingChoreography.EnvGlideMs;
        double amberEnds = amberStart + MarkingChoreography.MarkedGlideMs;
        Assert.Equal(120, greyEnds - amberEnds);
        Assert.True(MarkingChoreography.MarkedGlideMs < MarkingChoreography.EnvGlideMs);
    }

    /// <summary>Nötr anda kapsam da DÜZ GRİDİR — amber dalgayla gelir (BuildApp.jsx:296).</summary>
    [Fact]
    public void The_scope_stays_grey_through_the_neutral_moment()
    {
        Assert.Equal(1.0, MarkingChoreography.Opacity(MarkStep.Neutral, marked: true, MarkingChoreography.RowEnvOpacity));
        Assert.Equal(1.0, MarkingChoreography.Opacity(MarkStep.Neutral, marked: false, MarkingChoreography.RowEnvOpacity));
        Assert.Equal(1.0, MarkingChoreography.Opacity(MarkStep.Wave, marked: true, MarkingChoreography.RowEnvOpacity));
    }

    /// <summary>Dalga RANDOM akar — derleme sırasıyla DEĞİL (kullanıcı kararı). Sıra deterministiktir:
    /// aynı koşu numarası aynı sırayı verir, farklı koşu farklı.</summary>
    [Fact]
    public void The_wave_order_is_random_but_deterministic_per_run()
    {
        var first = MarkingChoreography.Order(12, runCount: 1);
        var again = MarkingChoreography.Order(12, runCount: 1);
        var next = MarkingChoreography.Order(12, runCount: 2);

        Assert.Equal(first, again);                                   // aynı koşu → aynı sıra
        Assert.NotEqual(first, next);                                 // farklı koşu → farklı sıra
        Assert.Equal(Enumerable.Range(0, 12), first.Order());         // permütasyon: her sıra bir kez
        Assert.NotEqual(Enumerable.Range(0, 12), first);              // ...ve derleme sırası DEĞİL
    }

    // ================================================================ saf çekirdek: bitiş

    /// <summary>Neon zinciri node başına ≤150ms ve toplamda ≤1.5s'tir.</summary>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 150)]
    [InlineData(11, 150)]
    [InlineData(36, 43)]
    public void The_neon_chain_is_at_most_150ms_per_node_and_15s_in_total(int count, double expected)
    {
        Assert.Equal(expected, EndFinale.StaggerMs(count));
        // Tavan node BAŞINA uygulanır ve tempo yuvarlanır → zincir tavanı en çok BİR adım aşabilir.
        Assert.True(EndFinale.ChainMs(count) - EndFinale.MaxChainMs < EndFinale.StaggerMs(count) + 1);
    }

    /// <summary>Adım sırası: hold (hepsi soluk) → neon (yalnız derlenenler) → nefes → grey (kalanlar birlikte).</summary>
    [Fact]
    public void Only_the_built_nodes_light_up_and_the_greys_come_back_last()
    {
        // hold: HERKES soluk.
        Assert.Equal(EndFinale.DimOpacity, EndFinale.Opacity(EndStep.Hold, built: true));
        Assert.Equal(EndFinale.DimOpacity, EndFinale.Opacity(EndStep.Hold, built: false));
        // neon: yalnız DERLENENLER yanar.
        Assert.Equal(1.0, EndFinale.Opacity(EndStep.Neon, built: true));
        Assert.Equal(EndFinale.DimOpacity, EndFinale.Opacity(EndStep.Neon, built: false));
        // grey: kalanlar da gelir.
        Assert.Equal(1.0, EndFinale.Opacity(EndStep.Grey, built: false));
        // ...ve griler BİRLİKTE, uzun bir geçişle gelir.
        Assert.Equal(EndFinale.GreyMs, EndFinale.GlideMs(built: false));
        Assert.Equal(EndFinale.LitGlideMs, EndFinale.GlideMs(built: true));

        double previous = -1;
        foreach (var step in EndFinale.Steps)
        {
            double at = EndFinale.StepAtMs(step, 4);
            Assert.True(at > previous, $"{step} sırayı bozdu");
            previous = at;
        }
    }

    /// <summary>Neon bir floresan lambadır: DÜZENSİZ titrer (monoton bir rampa DEĞİL) ve tam parlaklıkta biter.</summary>
    [Fact]
    public void The_neon_keyframes_flicker_irregularly_and_settle_at_full_brightness()
    {
        var frames = EndFinale.NeonKeyframes;

        Assert.Equal(0, frames[0].Percent);
        Assert.Equal(1, frames[^1].Percent);
        Assert.Equal(1.0, frames[^1].Opacity);
        // En az üç kez YÜKSELİP düşer — titreme budur.
        int drops = frames.Zip(frames.Skip(1), (a, b) => b.Opacity < a.Opacity).Count(dropped => dropped);
        Assert.True(drops >= 3, $"neon monoton yükseliyor (yalnız {drops} düşüş) — titreme yok");
    }

    // ================================================================ sürücü: satırlar + graf

    /// <summary>
    /// [§9-4 "Satırlar node'larla senkron söner/yerleşir"] Dalga, her üyeyi işaretlediğinde grafa haber
    /// verir — yalnız ADIM değiştikçe değil. Satır listesi binding'le anında boyanır; graf itilen bir
    /// kanaldır ve haber gelmezse ancak koşu tikinin insafıyla tazelenir. Bu, iki yüzeyin aynı anda
    /// değişmesinin ÖN KOŞULUdur (kablonun kendisi <see cref="MainWindow"/>'dadır).
    /// </summary>
    [Fact]
    public void The_wave_tells_the_graph_about_every_single_mark_not_just_every_step()
    {
        var (vm, driver) = Driven();
        int pushes = 0;
        driver.PushToGraph = (_, _) => pushes++;
        var scope = vm.ScopeFor(RunMode.Build);
        Assert.Equal(2, scope.Count); // ön-koşul: A ve B

        driver.Play(vm.Projects, scope);
        int atStart = pushes;
        DispatcherPump.PumpUntil(() => scope.All(r => r.Marked), TimeSpan.FromSeconds(4));

        Assert.True(pushes - atStart >= scope.Count,
            $"dalga {scope.Count} işaretleme için yalnız {pushes - atStart} haber verdi");
    }

    private static (RunViewModel vm, OperationChoreographer driver) Driven(bool animations = true)
    {
        var vm = NewVm();
        vm.OnEvent(new WorkspaceTopologyEvent([Node("A", 0), Node("B", 1), Node("C", 2)], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 2, 1));
        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(@"C:\p\A.csproj", "A", true),
            new BuildPreviewItem(@"C:\p\B.csproj", "B", true),
            new BuildPreviewItem(@"C:\p\C.csproj", "C", false),
        ]));
        return (vm, new OperationChoreographer(() => animations));
    }

    /// <summary>Kapsam moddan gelir: Build stale set'i, Rebuild döngü dışı HER ŞEYİ, Resolve döngü üyelerini
    /// işaretler.</summary>
    [Fact]
    public void The_scope_of_an_operation_comes_from_its_run_mode()
    {
        var vm = NewVm();
        vm.OnEvent(new WorkspaceTopologyEvent([Node("A", 0), Node("B", 1), Node("Cyc", 2, inCycle: true)], [], [], []));
        vm.OnEvent(new SyncCompletedEvent("main", "sha1234", false, 1, 1));
        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(@"C:\p\A.csproj", "A", true),
            new BuildPreviewItem(@"C:\p\B.csproj", "B", false),
        ]));

        Assert.Equal(["A"], vm.ScopeFor(RunMode.Build).Select(r => r.Name));
        Assert.Equal(["A", "B"], vm.ScopeFor(RunMode.Rebuild).Select(r => r.Name));   // döngü dışı HERKES
        Assert.Equal(["Cyc"], vm.ScopeFor(RunMode.Cycles).Select(r => r.Name));
    }

    /// <summary>Reduced-motion: koreografi HİÇ oynamaz (§1.3 "tüm süreler 0") — kapsam yalnız işaretlenir ve
    /// satırlar tam opak kalır.</summary>
    [Fact]
    public void Reduced_motion_skips_the_choreography_and_marks_the_scope_at_once()
    {
        var (vm, driver) = Driven(animations: false);

        driver.Play(vm.Projects, vm.ScopeFor(RunMode.Build));

        Assert.False(driver.IsPlaying);
        Assert.Equal(MarkStep.None, driver.Step);
        Assert.Equal(["A", "B"], vm.Projects.Where(r => r.Marked).Select(r => r.Name));
        Assert.All(vm.Projects, r => Assert.Equal(RowFade.None, r.Fade));
    }

    /// <summary>Kapsam BOŞSA koreografi oynamaz — işaretlenecek bir şey yoktur (hızlı kontrol koşusu).</summary>
    [Fact]
    public void An_empty_scope_plays_nothing()
    {
        var (vm, driver) = Driven();

        driver.Play(vm.Projects, []);

        Assert.False(driver.IsPlaying);
        Assert.Equal(MarkStep.None, driver.Step);
        Assert.All(vm.Projects, r => Assert.False(r.Marked));
    }

    /// <summary>[§9-4] Koreografi GERÇEK bir saatte oynar ve adımları sırayla geçer; işaretlenen satır
    /// <c>marked</c> görsel durumuna (amber) düşer, kapsam dışı satır <c>discovered</c> kalır.</summary>
    [StaFact]
    public void The_wave_marks_the_scope_and_the_steps_advance_on_a_real_clock()
    {
        var (vm, driver) = Driven();

        // Üretim sırası: önce  (başlangıç modu düşer — RunViewModel.BeginRunAsync), sonra .
        foreach (var row in vm.Projects) row.Fresh = false;

        driver.Play(vm.Projects, vm.ScopeFor(RunMode.Build));
        Assert.True(driver.IsPlaying);

        // Dalga: kapsam tek tek amber'a yanar.
        DispatcherPump.PumpUntil(() => vm.Projects.Count(r => r.Marked) == 2, TimeSpan.FromSeconds(3));
        Assert.Equal(VisualStatus.Marked, vm.Projects.Single(r => r.Name == "A").VisualStatus);
        Assert.Equal(VisualStatus.Discovered, vm.Projects.Single(r => r.Name == "C").VisualStatus);

        // Veda: kapsam dışı satır ÖNCE (uzun geçişle) söner.
        DispatcherPump.PumpUntil(() => driver.Step == MarkStep.DimEnv, TimeSpan.FromSeconds(4));
        Assert.Equal(MarkingChoreography.RowEnvOpacity, vm.Projects.Single(r => r.Name == "C").Fade.Opacity);
        Assert.Equal(MarkingChoreography.EnvGlideMs, vm.Projects.Single(r => r.Name == "C").Fade.DurationMs);
        Assert.Equal(1.0, vm.Projects.Single(r => r.Name == "A").Fade.Opacity); // sarılar HENÜZ katılmadı

        // ...sarılar 560ms sonra, DAHA KISA bir geçişle katılır.
        DispatcherPump.PumpUntil(() => driver.Step == MarkStep.Settle, TimeSpan.FromSeconds(4));
        Assert.Equal(MarkingChoreography.MarkedOpacity, vm.Projects.Single(r => r.Name == "A").Fade.Opacity);
        Assert.Equal(MarkingChoreography.MarkedGlideMs, vm.Projects.Single(r => r.Name == "A").Fade.DurationMs);
    }

    /// <summary>[§9-4] Koşu başlayınca koreografi biter: satırlar tam opaklığa döner ve İŞARETLİLİK SİLİNİR —
    /// amberi bundan sonra statü kanalı (queued/building) taşır.</summary>
    [StaFact]
    public void Cancelling_restores_full_opacity_and_clearing_drops_the_marks()
    {
        var (vm, driver) = Driven();
        driver.Play(vm.Projects, vm.ScopeFor(RunMode.Build));
        DispatcherPump.PumpUntil(() => driver.Step == MarkStep.DimEnv, TimeSpan.FromSeconds(4));

        driver.Cancel(vm.Projects);

        Assert.False(driver.IsPlaying);
        Assert.Equal(MarkStep.None, driver.Step);
        Assert.All(vm.Projects, r => Assert.Equal(RowFade.None, r.Fade));
        Assert.Equal(2, vm.Projects.Count(r => r.Marked)); // Cancel işaretliliği KORUR

        driver.ClearMarks(vm.Projects);
        Assert.All(vm.Projects, r => Assert.False(r.Marked));
    }

    // ================================================================ bitiş koreografisi (graf)

    private static GraphView Graph(params GraphNode[] nodes)
    {
        var view = GraphTestView.Realized(new Size(640, 400), () => true);
        view.SetGraph(nodes, []);
        return view;
    }

    /// <summary>[§9-5] Derlenen yoksa koreografi HİÇ oynamaz — final görünüme doğrudan gidilir.</summary>
    [StaFact]
    public void With_nothing_built_the_end_finale_does_not_play()
    {
        var view = Graph(new GraphNode("a", 0, GraphStatus.Skipped, VisualStatus.Skipped));

        view.PlayEndFinale([], runCount: 1);

        Assert.Equal(EndStep.None, view.EndStep);
    }

    /// <summary>[§9-5] Koreografi grafta oynar: hepsi soluk bekler, sonra derlenenler yanar; kalan griler EN
    /// SON gelir.</summary>
    [StaFact]
    public void The_end_finale_holds_everything_dim_then_lights_only_what_was_built()
    {
        var view = Graph(
            new GraphNode("built", 0, GraphStatus.Succeeded, VisualStatus.Succeeded),
            new GraphNode("skipped", 1, GraphStatus.Skipped, VisualStatus.Skipped));

        view.PlayEndFinale(["built"], runCount: 1);

        DispatcherPump.PumpUntil(() => view.EndStep == EndStep.Hold, TimeSpan.FromSeconds(2));
        Assert.Equal(EndFinale.DimOpacity, view.NodeVisuals["skipped"].OpacityTarget, 6);

        DispatcherPump.PumpUntil(() => view.EndStep == EndStep.Grey, TimeSpan.FromSeconds(6));
        Assert.Equal(1.0, view.NodeVisuals["skipped"].OpacityTarget, 6); // griler EN SON birlikte gelir
    }

    /// <summary>[§9-5] Yeni bir işlem (açılış koreografisi) bitiş koreografisini ANINDA keser.</summary>
    [StaFact]
    public void A_new_operation_cuts_the_end_finale_immediately()
    {
        var view = Graph(new GraphNode("built", 0, GraphStatus.Succeeded, VisualStatus.Succeeded));
        view.PlayEndFinale(["built"], runCount: 1);
        DispatcherPump.PumpUntil(() => view.EndStep == EndStep.Hold, TimeSpan.FromSeconds(2));

        view.BeginOperation();
        view.SetMarking(MarkStep.Neutral, new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(EndStep.None, view.EndStep);
        Assert.Equal(MarkStep.Neutral, view.MarkStep);
    }

    /// <summary>
    /// [§9-4/§9-5] Kesme, işaretleme DALGASINA bağlı değildir. Kapsamı boş bir işlem (ör. Sync'in hemen
    /// ardından "Everything up to date" ile biten Build) hiç dalga oynatmaz — sürücü grafa yalnız
    /// <see cref="MarkStep.None"/> iter. Bitiş koreografisi yine de ANINDA kesilmelidir: ekranda bir önceki
    /// koşunun neon'u yanarken yeni işlem başlamış olamaz.
    /// </summary>
    [StaFact]
    public void An_operation_with_an_empty_scope_still_cuts_the_end_finale()
    {
        var view = Graph(new GraphNode("built", 0, GraphStatus.Succeeded, VisualStatus.Succeeded));
        view.PlayEndFinale(["built"], runCount: 1);
        DispatcherPump.PumpUntil(() => view.EndStep == EndStep.Hold, TimeSpan.FromSeconds(2));

        view.BeginOperation();
        view.SetMarking(MarkStep.None, new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(EndStep.None, view.EndStep);
    }
}
