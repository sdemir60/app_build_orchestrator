using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using BuildOrchestrator.App.Graph;
using Xunit.Abstractions;
using IoPath = System.IO.Path;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [G1/It-5] <see cref="GraphView"/> ölçek ölçümü — grafın 500-1000 düğüme çıkarılması için ÖLÇÜM ZEMİNİ.
/// <see cref="ListRealizationPerfTests"/> ile AYNI ölçüm iskeletini (<see cref="PerfMeasure"/>: warmup + GC +
/// medyan) kullanır; tek farkı grafın görsel ağacının <see cref="GraphView.SetGraph"/> içinde HEVESLE
/// kurulmasıdır — bu yüzden ölçüm İKİ ayrı pencereye bölünür:
///
/// <list type="bullet">
///   <item><b>compute</b>: saf <see cref="GraphLayout.Compute"/> (WPF'siz aritmetik).</item>
///   <item><b>build</b>: <c>SetGraph</c> — compute + düğüm/kenar UIElement ağacının kurulması + stil/seçim/kamera
///     ilk uygulaması. (compute BU SÜRENİN İÇİNDEDİR, ayrıca da raporlanır ki payı görülebilsin.)</item>
///   <item><b>layout</b>: <c>Measure/Arrange/UpdateLayout</c> — WPF'in kurulmuş ağacı ölçüp yerleştirmesi.</item>
/// </list>
///
/// <para><b>Neden SERT duvar-saati eşiği yok:</b> duvar saati makineye/ısınmaya göre oynaktır. 36 düğümün
/// bütçesi <see cref="ListRealizationPerfTests"/>'teki gibi ERTELENEBİLİR bir bütçedir: aşılırsa suite KIRILMAZ,
/// gerçek sayı çıktıya yazılır ve yalnız felaket bir regresyon (gevşek tavan) suite'i patlatır. 500/1000 için
/// bütçe hiç yoktur (kayıt + felaket tavanı — G2'nin hedefi). Sadeleştirmenin kalıcılığını koruyan asıl metrik
/// deterministiktir: düğüm başına kurulan NESNE SAYISI
/// (<see cref="A_graph_node_builds_no_more_than_the_per_node_object_ceiling"/>) — G2'nin düşürmesi gereken sayı da
/// odur.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class GraphRealizationPerfTests(ITestOutputHelper output)
{
    /// <summary>design-v1 §2.3 referans grafı: 36 düğüm / 6 katman (prototipteki boyut).</summary>
    private const int DesignNodes = 36;
    private const int Layers = 6;
    private const double AvgFanIn = 1.6;

    private const double BudgetMs36 = 400;            // bugünkü tasarım boyutu — ERTELENEBİLİR bütçe
    private const double SanityCeilingMs36 = 3000;    // bütçe aşılsa bile suite'i patlatma (ListRealizationPerfTests ile aynı)
    private const double SanityCeilingMs500 = 6000;   // felaket regresyon tavanı (bütçe DEĞİL)
    private const double SanityCeilingMs1000 = 15000; // felaket regresyon tavanı (bütçe DEĞİL)

    /// <summary>[G1→G2] Bir graf düğümünün kurduğu nesne tavanı (<see cref="DsResources.RealizedObjects"/> +
    /// hücrenin kendisi). G1'de <b>17</b>'ydi (tavan 18); G2 üç kalemle <b>9</b>'a indirdi:
    /// <list type="bullet">
    ///   <item>dep-hata rozeti alt-ağacı (6 nesne) artık yalnız <c>HasDepIssue</c> olan düğümde kurulur — sentetik
    ///     profilde düğümlerin ~%97'sinde ÖLÜYDÜ;</item>
    ///   <item>ikonu 24 → 13px indiren <c>Viewbox</c> (+ iç <c>ContainerVisual</c>'ı, 2 nesne) yerine PAYLAŞILAN
    ///     donmuş bir <c>ScaleTransform</c> — geometrik sonuç birebir aynı (bkz. GraphRenderTests karakterizasyonu);</item>
    ///   <item>etiket, katmanın aralığı etiket hücresinin altına düştüğünde hiç kurulmaz (LOD) — bu boyutta
    ///     kurulduğu için aşağıdaki sayıya DAHİLDİR.</item>
    /// </list>
    /// Tavan DAR tutulur ki eager bir alt-ağaç geri sızarsa test kırılsın.</summary>
    private const int NodeObjectCeiling = 10;

    [StaFact]
    public void Realizing_the_design_sized_graph_stays_under_the_budget_and_500_and_1000_are_recorded()
    {
        var design = MeasureRealization(DesignNodes, warmups: 2, samples: 5);
        var five = MeasureRealization(500, warmups: 1, samples: 3);
        var thousand = MeasureRealization(1000, warmups: 1, samples: 3);

        output.WriteLine(Format(DesignNodes, design) + $"  (budget < {BudgetMs36:N0} ms)");
        output.WriteLine(Format(500, five) + "  (record only — G2 target)");
        output.WriteLine(Format(1000, thousand) + "  (record only — G2 target)");
        foreach (int n in new[] { DesignNodes, 500, 1000 })
        {
            var (nodes, edges) = SyntheticGraph.Build(n, Layers, AvgFanIn);
            output.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[G1 canvas] {0,4} nodes → canvas {1,8:N0} px (base {2:N0})",
                n, GraphLayout.Compute(nodes).Width, GraphLayout.CanvasWidth));

            // [G2] Cull'un GERÇEK kazancı: kaç düğüm/kenarın ağacı hiç kurulmadı.
            var view = NewHeadlessView(ViewportSize);
            view.SetGraph(nodes, edges);
            Layout(view, ViewportSize);
            output.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[G2 cull]   {0,4} nodes → cull {1,-3} · materialised {2,4}/{3,4} nodes ({4,5:P1}) · {5,4}/{6,4} edges",
                n, view.IsCullEnabled ? "ON" : "OFF", view.NodeVisuals.Count, view.NodeCount,
                (double)view.NodeVisuals.Count / view.NodeCount, view.EdgeVisuals.Count, view.EdgeCount));

            // [G2 fix round 1 · C1] Başlık rakamı YALNIZ ilk görünür alanı ölçer. Kullanıcı grafın TAMAMINI
            // gezerse her düğüm er geç materyalize olur — o senaryonun maliyeti de ölçülür ve raporlanır.
            output.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[G2 full]   {0,4} nodes → whole-graph materialisation median = {1,8:N1} ms",
                n, PerfMeasure.MedianOf(() => MeasureFullMaterializationMs(nodes, edges), warmups: 1, samples: 3)));
        }

        // [G1 review round 1] Sert ms eşiği makineye bağlı kırılganlıktır — kardeş testin (ListRealizationPerfTests
        // :48-54) ERTELENEBİLİR bütçe deseni AYNEN uygulanır: bütçe aşılırsa gerçek sayı çıktıya yazılır ve iş
        // ertelenir, suite yalnız felaket bir regresyonda (gevşek tavan) kırılır.
        if (design.TotalMs < BudgetMs36)
            Assert.True(design.TotalMs < BudgetMs36,
                $"36-düğüm graf realize {design.TotalMs:N1} ms — bütçe {BudgetMs36:N0} ms.");
        else
            Assert.True(design.TotalMs < SanityCeilingMs36,
                $"36-düğüm graf realize {design.TotalMs:N1} ms — {BudgetMs36:N0} ms bütçeyi aştı (ertelendi) VE {SanityCeilingMs36:N0} ms gevşek tavanı da aştı (felaket regresyon).");
        // 500/1000 için bütçe YOK (G2'nin işi); yalnız felaket bir regresyon suite'i patlatır.
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

        output.WriteLine($"[G1] graf düğümü nesne sayısı = {objects} (tavan {NodeObjectCeiling}) :: " +
            string.Join(", ", realized.GroupBy(o => o.GetType().Name)
                .OrderByDescending(g => g.Count()).Select(g => $"{g.Key}×{g.Count()}")));
        Assert.True(objects <= NodeObjectCeiling,
            $"graf düğümü {objects} nesne kuruyor — tavan {NodeObjectCeiling}. Eager bir alt-ağaç geri sızmış olabilir.");
    }

    /// <summary>[G1 · REALIZE TESTİ — It-4b dersi, commit <c>c6e9a21</c>] Headless Measure/Arrange, XAML runtime
    /// çözümlemesini GÖRMEZ: bir token yanlış tipte verildiğinde 1198 test yeşilken uygulama hiç açılmıyordu. Bu
    /// test grafı uygulamanın GERÇEK merge zinciriyle (<see cref="DsResources.NewHost"/>) ve gerçek bir HWND
    /// içinde ölçek altında ayağa kaldırır — büyüyen tuvalin de bu yolda çözüldüğünü kanıtlar.</summary>
    [StaFact]
    public void A_500_node_graph_realizes_in_a_real_window_through_the_app_resource_chain()
    {
        var (nodes, edges) = SyntheticGraph.Build(500, Layers, AvgFanIn);
        var host = DsResources.NewHost();
        var view = new GraphView { AnimationsEnabledProvider = () => false };
        var window = DsResources.Realize(host, view);

        view.SetGraph(nodes, edges);
        view.UpdateLayout();

        Assert.Equal(nodes.Count, view.NodeCount);
        Assert.Equal(edges.Count, view.EdgeCount);
        Assert.False(view.IsEmptyStateVisible);
        Assert.Equal(
            string.Format(CultureInfo.InvariantCulture, "{0} projects · {1} dependencies", nodes.Count, edges.Count),
            view.HeaderCountsText);
        // Tuval ARTIK 880'e pinli değil: 500 düğüm 6 katmana dağıldığında en kalabalık katman 880'i taşırır.
        Assert.True(view.GraphSize.Width > GraphLayout.CanvasWidth,
            $"500 düğümlü grafın tuvali büyümedi: {view.GraphSize.Width} px.");
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- ölçüm altyapısı

    private static Size ViewportSize => new(600, 400);

    /// <summary>[G2 fix round 1 · C1] Grafın TAMAMININ görünür olduğu panel ölçüsü — 1000 düğümde tuval
    /// 8.553px ve kamera tabanı <c>MinScale</c> 0,68 olduğu için 5.816px'ten geniş her panel yeter; 12.000
    /// rahat bir paydır.</summary>
    private static Size FullViewportSize => new(12000, 4000);

    /// <summary>[G2 fix round 1 · C1] "Kullanıcı grafın tamamını gezdi" senaryosunun duvar saati: <c>SetGraph</c>
    /// + ilk yerleşim + panel grafın tamamını gösterecek kadar büyüdüğünde KALAN her düğüm/kenarın
    /// materyalizasyonu ve yeniden yerleşimi. Cull'suz koda kıyaslanırken bu, cull'un ÜST sınır maliyetidir
    /// (ikinci bir tam <c>Measure/Arrange</c> geçişini de içerir — yani ölçüm G2'nin ALEYHİNE muhafazakârdır).</summary>
    private static double MeasureFullMaterializationMs(
        IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges)
    {
        var view = NewHeadlessView(ViewportSize);

        var sw = Stopwatch.StartNew();
        view.SetGraph(nodes, edges);
        Layout(view, ViewportSize);
        Layout(view, FullViewportSize); // tüm graf görünür → cull edilmiş ne varsa şimdi kurulur
        sw.Stop();

        Assert.Equal(nodes.Count, view.NodeVisuals.Count);
        Assert.Equal(edges.Count, view.EdgeVisuals.Count);
        return sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>Bir realizasyonun üç fazının duvar saati (ms). <see cref="ComputeMs"/>, <see cref="BuildMs"/>'in
    /// İÇİNDEDİR — toplam bu yüzden build + layout'tur.</summary>
    private readonly record struct GraphRealizeSample(double ComputeMs, double BuildMs, double LayoutMs)
    {
        public double TotalMs => BuildMs + LayoutMs;
    }

    private static string Format(int nodeCount, GraphRealizeSample s) => string.Format(
        CultureInfo.InvariantCulture,
        "[G1 perf] {0,4}-node graph realize median = {1,8:N1} ms  (compute {2,6:N2} · build {3,8:N1} · layout {4,8:N1})",
        nodeCount, s.TotalMs, s.ComputeMs, s.BuildMs, s.LayoutMs);

    /// <summary>Aynı grafı <paramref name="warmups"/> kez ısıtır, sonra <paramref name="samples"/> ölçümün
    /// FAZ BAZINDA medyanını döndürür. Her ölçüm TAZE bir view ile sıfırdan realize eder. Warmup/GC/medyan
    /// iskeleti <see cref="PerfMeasure"/>'dadır (liste perf testiyle ORTAK); burada yalnız "medyan faz bazında
    /// alınır" kararı yaşar — üç fazın medyanı ayrı ayrı alınır, tek bir örneğin fazları birlikte seçilmez.</summary>
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
        var layout = GraphLayout.Compute(nodes);
        sw.Stop();
        double computeMs = sw.Elapsed.TotalMilliseconds;
        GC.KeepAlive(layout);

        sw.Restart();
        view.SetGraph(nodes, edges); // düğüm/kenar UIElement ağacı BURADA kurulur (heves)
        sw.Stop();
        double buildMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        Layout(view, ViewportSize);
        sw.Stop();

        // [G2] Cull tembel materyalizasyondur: TOPLAM düğüm/kenar sayısı her zaman tamdır, MATERYALİZE olan küme
        // ise görünür alanla sınırlıdır (küçük grafta ikisi eşittir — cull hiç devreye girmez).
        Assert.Equal(nodes.Count, view.NodeCount);
        Assert.Equal(edges.Count, view.EdgeCount);
        Assert.InRange(view.NodeVisuals.Count, 1, nodes.Count);
        if (!view.IsCullEnabled)
        {
            Assert.Equal(nodes.Count, view.NodeVisuals.Count);
            Assert.Equal(edges.Count, view.EdgeVisuals.Count);
        }
        return new GraphRealizeSample(computeMs, buildMs, sw.Elapsed.TotalMilliseconds);
    }

    private static void Layout(FrameworkElement view, Size size)
    {
        view.Measure(size);
        view.Arrange(new Rect(new Point(0, 0), size));
        view.UpdateLayout();
    }

    /// <summary>HWND'siz ölçüm host'u — animasyon KAPALI olduğu için compositor saati gerekmez (aynı gerekçe:
    /// <see cref="ListRealizationPerfTests"/>). Token/ikon sözlükleri <c>TestAssets</c>'ten merge edilir ki
    /// <c>SetResourceReference</c> ile bağlanan fırçalar GERÇEKTEN çözülsün (aksi halde ölçüm gerçekçi olmazdı).</summary>
    private static GraphView NewHeadlessView(Size size)
    {
        var view = new GraphView { AnimationsEnabledProvider = () => false };
        foreach (string name in new[] { "Motion.xaml", "Tokens.xaml", "Icons.xaml" })
        {
            using var stream = File.OpenRead(IoPath.Combine(AppContext.BaseDirectory, "TestAssets", "Resources", name));
            view.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
        }
        Layout(view, size); // ViewportSize > 0 olmalı — aksi halde ApplyCamera erken döner
        return view;
    }
}
