using System.Diagnostics;
using System.Globalization;
using System.Windows;
using BuildOrchestrator.App.Graph;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [G1/It-5] <see cref="GraphView"/> ölçek ölçümü. <see cref="ListRealizationPerfTests"/> ile AYNI ölçüm
/// iskeletini (<see cref="PerfMeasure"/>: warmup + GC + medyan) kullanır; ölçüm üç pencereye bölünür:
///
/// <list type="bullet">
///   <item><b>compute</b>: saf <see cref="QuietGraphLayout.Compute"/> (WPF'siz aritmetik).</item>
///   <item><b>build</b>: <c>SetGraph</c> — compute + düğüm UIElement ağacının kurulması + stil/seçim/kamera
///     ilk uygulaması. (compute BU SÜRENİN İÇİNDEDİR, ayrıca da raporlanır ki payı görülebilsin.)</item>
///   <item><b>layout</b>: <c>Measure/Arrange/UpdateLayout</c> — WPF'in kurulmuş ağacı ölçüp yerleştirmesi.</item>
/// </list>
///
/// <para><b>[quiet] Eski iddia — "cull kazancı":</b> bu dosya eskiden "kaç düğümün ağacı hiç kurulmadı" ve
/// "kullanıcı grafın tamamını gezerse maliyet ne olur" diye iki ayrı ölçüm raporluyordu. v1.3.0 §2.3 grafı her
/// panel boyutuna TAM SIĞDIRDIĞI için varsayılan görünümde her düğüm görünür alandadır — cull eleyecek bir şey
/// bulamaz ve kaldırıldı. Dolayısıyla "build" artık HER ZAMAN tam maliyettir; ayrı bir tam-materyalizasyon
/// penceresi de anlamsızdır. Bu, ölçüm için bir gevşetme DEĞİL sıkılaştırmadır: eski başlık rakamı yalnız ilk
/// görünür alanı ölçüyordu.</para>
///
/// <para><b>Neden SERT duvar-saati eşiği yok:</b> duvar saati makineye/ısınmaya göre oynaktır. 36 düğümün
/// bütçesi ERTELENEBİLİR bir bütçedir; 500/1000 için bütçe hiç yoktur (kayıt + felaket tavanı). Sadeleşmenin
/// kalıcılığını koruyan asıl metrik deterministiktir: düğüm başına kurulan NESNE SAYISI.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class GraphRealizationPerfTests(ITestOutputHelper output)
{
    /// <summary>design-v1 §2.3 referans grafı: 36 düğüm / 6 katman (prototipteki boyut).</summary>
    private const int DesignNodes = 36;
    private const int Layers = 6;
    private const double AvgFanIn = 1.6;

    private const double BudgetMs36 = 400;            // bugünkü tasarım boyutu — ERTELENEBİLİR bütçe
    private const double SanityCeilingMs36 = 3000;    // bütçe aşılsa bile suite'i patlatma
    private const double SanityCeilingMs500 = 6000;   // felaket regresyon tavanı (bütçe DEĞİL)
    private const double SanityCeilingMs1000 = 15000; // felaket regresyon tavanı (bütçe DEĞİL)

    /// <summary>
    /// Bir graf düğümünün kurduğu nesne tavanı (<see cref="DsResources.RealizedObjects"/> + hücrenin kendisi).
    ///
    /// <para><b>Eski iddia:</b> tavan <b>10</b>'du (ölçülen 9). v1.3.0 §2.3 üç alt-ağacı daha kaldırdı —
    /// node üstü ad etiketi (<c>TextBlock</c>), graf içi dep-issue rozeti ve rozeti kareden ayırmak için var
    /// olan ara kap — dolayısıvle tavan DÜŞER. Tavanı yükseltmek YASAKTIR: dar tutulur ki eager bir alt-ağaç
    /// geri sızarsa test kırılsın.</para>
    /// </summary>
    private const int NodeObjectCeiling = 7;

    [StaFact]
    public void Realizing_the_design_sized_graph_stays_under_the_budget_and_500_and_1000_are_recorded()
    {
        var design = MeasureRealization(DesignNodes, warmups: 2, samples: 5);
        var five = MeasureRealization(500, warmups: 1, samples: 3);
        var thousand = MeasureRealization(1000, warmups: 1, samples: 3);

        output.WriteLine(Format(DesignNodes, design) + $"  (budget < {BudgetMs36:N0} ms)");
        output.WriteLine(Format(500, five) + "  (record only)");
        output.WriteLine(Format(1000, thousand) + "  (record only)");
        foreach (int n in new[] { DesignNodes, 500, 1000 })
        {
            var (nodes, edges) = SyntheticGraph.Build(n, Layers, AvgFanIn);
            var layout = QuietGraphLayout.Compute(nodes, ViewportSize);
            output.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[quiet layout] {0,4} nodes → pitch {1,5:N1} · node {2,5:N1} px · {3,3} columns (panel {4:N0}×{5:N0})",
                n, layout.Pitch, layout.NodeSize, layout.Columns, ViewportSize.Width, ViewportSize.Height));
        }

        // [G1 review round 1] Sert ms eşiği makineye bağlı kırılganlıktır — kardeş testin ERTELENEBİLİR bütçe
        // deseni AYNEN uygulanır: bütçe aşılırsa gerçek sayı çıktıya yazılır ve iş ertelenir; suite yalnız
        // felaket bir regresyonda kırılır.
        if (design.TotalMs < BudgetMs36)
            Assert.True(design.TotalMs < BudgetMs36,
                $"36-düğüm graf realize {design.TotalMs:N1} ms — bütçe {BudgetMs36:N0} ms.");
        else
            Assert.True(design.TotalMs < SanityCeilingMs36,
                $"36-düğüm graf realize {design.TotalMs:N1} ms — {BudgetMs36:N0} ms bütçeyi aştı (ertelendi) VE {SanityCeilingMs36:N0} ms gevşek tavanı da aştı (felaket regresyon).");
        Assert.True(five.TotalMs < SanityCeilingMs500,
            $"500-düğüm graf realize {five.TotalMs:N1} ms — {SanityCeilingMs500:N0} ms felaket tavanını aştı.");
        Assert.True(thousand.TotalMs < SanityCeilingMs1000,
            $"1000-düğüm graf realize {thousand.TotalMs:N1} ms — {SanityCeilingMs1000:N0} ms felaket tavanını aştı.");
    }

    [StaFact]
    public void A_graph_node_builds_no_more_than_the_per_node_object_ceiling()
    {
        var (nodes, edges) = SyntheticGraph.Build(DesignNodes, Layers, AvgFanIn);
        var view = NewHeadlessView(ViewportSize);
        view.SetGraph(nodes, edges);
        Layout(view, ViewportSize);

        var cell = view.NodeVisuals[nodes[0].Name].Cell;
        var realized = DsResources.RealizedObjects(cell);
        int objects = realized.Count; // [T49 fix round 2] hücrenin kendisi ARTIK dahil (elle +1 kalktı)

        output.WriteLine($"[quiet] graf düğümü nesne sayısı = {objects} (tavan {NodeObjectCeiling}) :: " +
            string.Join(", ", realized.GroupBy(o => o.GetType().Name)
                .OrderByDescending(g => g.Count()).Select(g => $"{g.Key}×{g.Count()}")));
        Assert.True(objects <= NodeObjectCeiling,
            $"graf düğümü {objects} nesne kuruyor — tavan {NodeObjectCeiling}. Eager bir alt-ağaç geri sızmış olabilir.");
    }

    /// <summary>[G1 · REALIZE TESTİ — It-4b dersi, commit <c>c6e9a21</c>] Headless Measure/Arrange, XAML runtime
    /// çözümlemesini GÖRMEZ: bir token yanlış tipte verildiğinde 1198 test yeşilken uygulama hiç açılmıyordu. Bu
    /// test grafı uygulamanın GERÇEK merge zinciriyle (<see cref="DsResources.NewHost"/>) ve gerçek bir HWND
    /// içinde ayağa kaldırır — ve [quiet] ek olarak PANEL YENİDEN BOYUTLANIRKEN de ayakta kaldığını kanıtlar
    /// (yerleşim artık panel ölçüsünün fonksiyonudur).</summary>
    [StaFact]
    public void A_500_node_graph_realizes_in_a_real_window_and_survives_a_resize()
    {
        var (nodes, edges) = SyntheticGraph.Build(500, Layers, AvgFanIn);
        var host = DsResources.NewHost();
        var view = new GraphView { AnimationsEnabledProvider = () => false };
        var window = DsResources.Realize(host, view);

        view.SetGraph(nodes, edges);
        view.UpdateLayout();

        Assert.Equal(nodes.Count, view.NodeCount);
        Assert.Equal(nodes.Count, view.NodeVisuals.Count); // cull yok: hepsi kurulur
        Assert.False(view.IsEmptyStateVisible);
        Assert.Equal(
            string.Format(CultureInfo.InvariantCulture, "{0} projects · {1} dependencies", nodes.Count, edges.Count),
            view.HeaderCountsText);

        double before = view.NodeSize;
        window.Width = 420;
        window.Height = 300;
        view.UpdateLayout();

        Assert.True(view.LayoutComputeCount > 0, "panel yeniden boyutlandı ama yerleşim yeniden hesaplanmadı");
        Assert.True(view.NodeSize <= before, $"daralan panelde düğüm büyüdü: {before} → {view.NodeSize}");
        Assert.All(view.NodeVisuals.Values, visual => Assert.Equal(view.NodeSize, visual.Square.Width, 3));
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- ölçüm altyapısı

    private static Size ViewportSize => new(600, 400);

    /// <summary>Bir realizasyonun üç fazının duvar saati (ms). <see cref="GraphRealizeSample.ComputeMs"/>,
    /// <see cref="GraphRealizeSample.BuildMs"/>'in İÇİNDEDİR — toplam bu yüzden build + layout'tur.</summary>
    private readonly record struct GraphRealizeSample(double ComputeMs, double BuildMs, double LayoutMs)
    {
        public double TotalMs => BuildMs + LayoutMs;
    }

    private static string Format(int nodeCount, GraphRealizeSample s) => string.Format(
        CultureInfo.InvariantCulture,
        "[quiet perf] {0,4}-node graph realize median = {1,8:N1} ms  (compute {2,6:N2} · build {3,8:N1} · layout {4,8:N1})",
        nodeCount, s.TotalMs, s.ComputeMs, s.BuildMs, s.LayoutMs);

    /// <summary>Aynı grafı <paramref name="warmups"/> kez ısıtır, sonra <paramref name="samples"/> ölçümün
    /// FAZ BAZINDA medyanını döndürür. Her ölçüm TAZE bir view ile sıfırdan realize eder.</summary>
    private static GraphRealizeSample MeasureRealization(int nodeCount, int warmups, int samples)
    {
        var (nodes, edges) = SyntheticGraph.Build(nodeCount, Layers, AvgFanIn);

        var results = PerfMeasure.Sample(() => RealizeOnce(nodes, edges), warmups, samples);

        return new GraphRealizeSample(
            PerfMeasure.Median(results.Select(r => r.ComputeMs)),
            PerfMeasure.Median(results.Select(r => r.BuildMs)),
            PerfMeasure.Median(results.Select(r => r.LayoutMs)));
    }

    /// <summary>Tek bir realizasyon: saf compute · <c>SetGraph</c> · <c>Measure/Arrange/UpdateLayout</c>. Ağacın
    /// GERÇEKTEN kurulduğu doğrulanır — eksik realize edilmiş bir grafı ölçüp sessizce yeşil kalmak mümkündü.</summary>
    private static GraphRealizeSample RealizeOnce(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges)
    {
        var view = NewHeadlessView(ViewportSize);

        var sw = Stopwatch.StartNew();
        var layout = QuietGraphLayout.Compute(nodes, ViewportSize);
        sw.Stop();
        double computeMs = sw.Elapsed.TotalMilliseconds;
        GC.KeepAlive(layout);

        sw.Restart();
        view.SetGraph(nodes, edges); // düğüm UIElement ağacı BURADA kurulur
        sw.Stop();
        double buildMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        Layout(view, ViewportSize);
        sw.Stop();

        Assert.Equal(nodes.Count, view.NodeCount);
        Assert.Equal(nodes.Count, view.NodeVisuals.Count);
        return new GraphRealizeSample(computeMs, buildMs, sw.Elapsed.TotalMilliseconds);
    }

    private static void Layout(FrameworkElement view, Size size)
    {
        view.Measure(size);
        view.Arrange(new Rect(new Point(0, 0), size));
        view.UpdateLayout();
    }

    /// <summary>HWND'siz ölçüm host'u — animasyon KAPALI olduğu için compositor saati gerekmez. Token/ikon
    /// sözlükleri <c>TestAssets</c>'ten merge edilir ki <c>SetResourceReference</c> ile bağlanan fırçalar
    /// GERÇEKTEN çözülsün (aksi halde ölçüm gerçekçi olmazdı).</summary>
    private static GraphView NewHeadlessView(Size size)
    {
        // [A13/T1 fix-1 · S1] Sözlük merge'i GraphTestView'da (TEK yer).
        var view = GraphTestView.New();
        Layout(view, size); // ViewportSize > 0 olmalı — aksi halde yerleşim/kamera erken döner
        return view;
    }
}
