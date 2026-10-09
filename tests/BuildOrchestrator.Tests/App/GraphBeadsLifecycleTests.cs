using System.Windows;
using System.Windows.Shapes;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Derlemesi biten düğümün beads yörüngesi söndükten sonra paylaşımlı saatten SÖKÜLÜR ve çizimden ÇIKAR (Collapsed);
/// düğüm yeniden derlenmeye başlarsa aynı yörünge geri gelir.
///
/// <para><b>Eski davranış:</b> yörünge düğüm ilk kez derlendiğinde kuruluyor, bir daha sökülmüyor, yalnız opaklığı 0'a
/// iniyordu — ve paylaşımlı saate bağlı kalıyordu. Saat koşu boyunca (herhangi bir düğüm derlenirken) döndüğü için, biten
/// her düğümün GÖRÜNMEZ yörüngesi saniyede 30 kez <c>StrokeDashOffset</c> alıyor ve yeniden çiziliyordu.
/// <b>Değişme gerekçesi (ÖLÇÜLDÜ, 2026-10-10):</b> görünür bir Rebuild'in ağır penceresinde (OSYS, 187 proje) App'in
/// tüm tahsislerinin %15'i (30 s'de 125 MB <c>EffectiveValueEntry[]</c>) <c>Shape.GetPen ← Rectangle.OnRender ←
/// Grid/Canvas.ArrangeOverride</c> yolundan geliyordu: dash offset'i değişen her yörünge her karede yeni bir
/// <c>Pen</c> + <c>DashStyle</c> kuruyor; render thread'i 1,07 G döngü/s, UI thread'i 0,72 G döngü/s harcıyordu. Maliyet
/// koşu ilerledikçe (derlenmiş düğüm sayısıyla) büyüyor, en kötü kare boşlukları (350–720 ms) koşunun sonundaki UI
/// döngü grubunda görülüyordu.</para>
///
/// <para>Söndürme sırasında yörünge DÖNMEYE devam eder (§2.3: "noktalar dönerken söner, donup kaybolmaz") — söküm
/// yalnız çıkış animasyonu bittiğinde olur; ikinci test bunu pinler.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphBeadsLifecycleTests
{
    private static readonly TimeSpan FadeBudget = TimeSpan.FromSeconds(3); // çıkış 640 ms; yük altında pay

    private static IReadOnlyList<GraphNode> Nodes(GraphStatus a, GraphStatus b) =>
    [
        new("OSYS.A", "OSYS.A", 0, a),
        new("OSYS.B", "OSYS.B", 0, b),
    ];

    private static GraphView Realize(out Window window)
    {
        var view = GraphTestView.Shown(new Size(600, 400), out window, () => true);
        view.SetGraph(Nodes(GraphStatus.Building, GraphStatus.Building), []);
        return view;
    }

    /// <summary>Yörünge GERÇEKTEN sürülüyor mu: kısa bir pompa boyunca dash offset'i değişiyor mu. Davranışı okur — saatin
    /// uygulanıp uygulanmadığını değil (<c>IsAnimated</c> saat ilk tikini atana dek false döner, ölçüldü).</summary>
    private static bool Spins(Rectangle orbit)
    {
        double before = orbit.StrokeDashOffset;
        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(400)); // dekoratif kare 30 fps: ~12 tik (yavaş runner payı)
        return orbit.StrokeDashOffset != before;
    }

    /// <summary>"Bu yörünge DURDU" iddiasını boşuna geçmekten korur: durduğu söylenen <paramref name="still"/> ile döndüğü bilinen
    /// <paramref name="spinning"/> AYNI pompa penceresinde ölçülür — <paramref name="spinning"/> ilerlediyse o pencerede tik geldi,
    /// yani <paramref name="still"/>'in kıpırdamaması gerçektir.</summary>
    private static (bool StillMoved, bool SpinningMoved) MovesTogether(Rectangle still, Rectangle spinning)
    {
        double s0 = still.StrokeDashOffset, m0 = spinning.StrokeDashOffset;
        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(400));
        return (still.StrokeDashOffset != s0, spinning.StrokeDashOffset != m0);
    }

    private static void PumpUntilCollapsed(Rectangle orbit) =>
        DispatcherPump.PumpUntil(() => orbit.Visibility == Visibility.Collapsed, FadeBudget);

    [StaFact]
    public void A_finished_nodes_orbit_stops_spinning_and_leaves_the_render_once_its_fade_out_ends()
    {
        var view = Realize(out var window);
        var a = view.NodeVisuals["OSYS.A"].Beads!;
        var b = view.NodeVisuals["OSYS.B"].Beads!;
        Assert.True(Spins(a) && Spins(b)); // ön-koşul: iki düğüm de derleniyor, iki yörünge de dönüyor

        // A bitti, B hâlâ derleniyor — saat bu yüzden dönmeye devam eder; eski kod A'nın görünmez yörüngesini de sürerdi.
        view.UpdateStatuses(Nodes(GraphStatus.Succeeded, GraphStatus.Building));
        PumpUntilCollapsed(a);

        Assert.Equal(Visibility.Collapsed, a.Visibility); // çizimden çıktı…
        var (aMoved, bMoved) = MovesTogether(a, b);
        Assert.True(bMoved);                              // (aynı pencerede tik geldi —)
        Assert.False(aMoved);                             // …ve saat onu artık sürmüyor
        Assert.Equal(Visibility.Visible, b.Visibility);
        Assert.NotNull(view.BeadsClock);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_orbit_keeps_spinning_while_it_fades_out()
    {
        var view = Realize(out var window);
        var a = view.NodeVisuals["OSYS.A"].Beads!;

        view.UpdateStatuses(Nodes(GraphStatus.Succeeded, GraphStatus.Building));

        Assert.True(Spins(a));
        Assert.Equal(Visibility.Visible, a.Visibility);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_retired_orbit_comes_back_spinning_when_its_node_builds_again()
    {
        var view = Realize(out var window);
        var a = view.NodeVisuals["OSYS.A"].Beads!;
        view.UpdateStatuses(Nodes(GraphStatus.Succeeded, GraphStatus.Building));
        PumpUntilCollapsed(a);
        Assert.Equal(Visibility.Collapsed, a.Visibility); // ön-koşul: yörünge gerçekten söküldü

        view.UpdateStatuses(Nodes(GraphStatus.Building, GraphStatus.Building));

        Assert.Same(a, view.NodeVisuals["OSYS.A"].Beads); // yeniden KURULMAZ — aynı yörünge geri gelir
        Assert.True(Spins(a));
        Assert.Equal(Visibility.Visible, a.Visibility);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Restarting_the_clock_spins_only_the_orbits_still_on_screen()
    {
        var view = Realize(out var window);
        var a = view.NodeVisuals["OSYS.A"].Beads!;
        var b = view.NodeVisuals["OSYS.B"].Beads!;
        view.UpdateStatuses(Nodes(GraphStatus.Succeeded, GraphStatus.Building));
        PumpUntilCollapsed(a);
        Assert.Equal(Visibility.Collapsed, a.Visibility); // ön-koşul

        window.Hide();   // saat bırakılır (görünürlük kapısı)…
        window.Show();   // …ve yeniden kurulur: yalnız ekrandaki yörüngeler bağlanmalı
        window.UpdateLayout();

        Assert.NotNull(view.BeadsClock);
        var (aMoved, bMoved) = MovesTogether(a, b);
        Assert.True(bMoved);
        Assert.False(aMoved, "yeniden kurulan saat sökülmüş yörüngeyi tekrar sürüyor");
        Assert.Equal(Visibility.Collapsed, a.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>Değiştirilen (SnapshotAndReplace) eski bir sönüşün saati zaman ağacında kalır ve süresi dolunca <c>Completed</c>
    /// yine ateşlenir. Bitir–başla–bitir bir sönüş süresinin içinde olursa o bayat tamamlanma ikinci sönüşü YARIDA kesmemeli:
    /// yörünge ancak SON sönüşü bitince emekliye ayrılır.</summary>
    [StaFact]
    public void A_stale_fade_out_does_not_cut_a_later_one_short()
    {
        var view = Realize(out var window);
        var a = view.NodeVisuals["OSYS.A"].Beads!;
        var since = System.Diagnostics.Stopwatch.StartNew();

        view.UpdateStatuses(Nodes(GraphStatus.Succeeded, GraphStatus.Building)); // 1. sönüş (t≈0)
        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(250));
        view.UpdateStatuses(Nodes(GraphStatus.Building, GraphStatus.Building));  // yeniden derleniyor (1. sönüş değişti)
        DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(250));
        view.UpdateStatuses(Nodes(GraphStatus.Succeeded, GraphStatus.Building)); // 2. sönüş (t≈500, biter t≈1140)
        DispatcherPump.PumpUntil(() => since.Elapsed.TotalMilliseconds >= GraphBeads.FadeOutMs + 150, FadeBudget);

        Assert.Equal(Visibility.Visible, a.Visibility); // 1. sönüşün süresi geçti, 2. hâlâ sürüyor
        PumpUntilCollapsed(a);
        Assert.Equal(Visibility.Collapsed, a.Visibility); // son sönüş bitince yine de emekliye ayrılır
        GC.KeepAlive(window);
    }

    /// <summary>Son derleme bittiğinde saat 700 ms'lik spin-down'dan sonra bırakılır. O pencerede panel boyutu değişip yörünge
    /// çevresi yeniden kurulursa (saat yeniden başlar) bırakma yine de olmalı — aksi halde boş bir 30 fps saati bir sonraki
    /// koşuya dek döner (bkz. <see cref="DecorativeClock"/>: aktif saat her karede tik ister).</summary>
    [StaFact]
    public void Resizing_during_the_spin_down_still_releases_the_clock()
    {
        // Kalabalık tek bant: panel daraldığında pitch gerçekten küçülür (iki düğümde panel tabanı kelepçesi pitch'i hep 44'te tutar).
        static IReadOnlyList<GraphNode> Crowd(GraphStatus a) =>
            [new("OSYS.A", "OSYS.A", 0, a), .. Enumerable.Range(0, 39).Select(i => new GraphNode($"OSYS.F{i}", $"OSYS.F{i}", 0, GraphStatus.Succeeded))];
        var view = GraphTestView.Shown(new Size(600, 400), out var window, () => true);
        view.SetGraph(Crowd(GraphStatus.Building), []);
        view.UpdateStatuses(Crowd(GraphStatus.Succeeded)); // son derleme bitti: spin-down kuruldu
        var before = view.BeadsClock;
        Assert.NotNull(before); // ön-koşul: saat henüz dönüyor (spin-down penceresi)

        window.Width = QuietGraphLayout.MinPanelWidth;   // dar panel → küçük pitch → yörünge çevresi değişir → saat yeniden kurulur
        window.Height = QuietGraphLayout.MinPanelHeight;
        window.UpdateLayout();
        Assert.False(ReferenceEquals(before, view.BeadsClock), "ön-koşul: boyut değişimi saati yeniden kurmadı");

        DispatcherPump.PumpUntil(() => view.BeadsClock is null, FadeBudget);

        Assert.Null(view.BeadsClock);
        GC.KeepAlive(window);
    }
}
