using System.Windows;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [quiet · overlay kablajı] Tooltip ve ad etiketinin GERÇEK görünümdeki konumu. Saf aritmetik
/// <see cref="GraphOverlayTests"/>'te; burada onu besleyen iki girdi yaşar: kutunun ÖLÇÜSÜ ve düğümün
/// BOYANMIŞ yarım yüksekliği.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphOverlayPlacementTests
{
    private static readonly Size Panel = new(560, 420);

    private const string Short = "A";
    private const string Long = "OSYS.Orchestration.Service.WorkOrder.Reporting";

    private static GraphView Built()
    {
        var view = GraphTestView.Realized(Panel, () => false);
        // Tooltip/etiket kutusunun GERÇEK ölçüsü (padding + kenarlık) yalnız bu sözlükle gelir; ölçü
        // iddiaları onsuz anlamsız olurdu.
        view.Resources.MergedDictionaries.Add(DsResources.Load("Controls.xaml"));
        view.SetGraph(
            [new(Short, 0, GraphStatus.Succeeded), new(Long, 0, GraphStatus.Succeeded)], []);
        view.UpdateLayout();
        return view;
    }

    /// <summary>
    /// AYIRT EDİCİ — kutu HER adda yeniden ölçülür.
    ///
    /// <para><b>Kusur (ölçüldü):</b> hangi projeye gelinirse gelinsin kutu genişliği İLK ölçümdeki değerde
    /// (24.6px) takılı kalıyordu, dolayısıyla uzun adlar düğümün epey soluna kayıyordu — "tooltip'ler proje
    /// adına göre her zaman ortalı değil".</para>
    ///
    /// <para><b>Kök neden:</b> metin değiştiğinde WPF yalnız <c>TextBlock</c>'u kirli işaretler; ata zinciri
    /// LAYOUT TURUNDA yürütülür. Tur dışından çağrılan <c>Measure</c>, Border'ı temiz görüp erken çıkıyor ve
    /// <c>DesiredSize</c> bir önceki adın genişliğinde kalıyordu. Çözüm: ölçmeden önce <b>açıkça</b>
    /// geçersiz kıl.</para>
    /// </summary>
    [StaFact]
    public void The_tooltip_is_re_measured_for_every_name_so_it_stays_centred()
    {
        var view = Built();

        view.SetHoverForTest(Short);
        double shortWidth = view.TooltipBoxSize.Width;
        AssertCentredOnNode(view, Short);

        view.SetHoverForTest(Long);
        Assert.True(view.TooltipBoxSize.Width > shortWidth * 2,
            $"uzun ad kutuyu genişletmedi ({view.TooltipBoxSize.Width} vs {shortWidth}) — ölçü bayat");
        AssertCentredOnNode(view, Long);

        // Geriye dönüş de ölçülmeli: kutu daralmazsa bu sefer kısa ad sola kayardı.
        view.SetHoverForTest(Short);
        Assert.Equal(shortWidth, view.TooltipBoxSize.Width, 3);
        AssertCentredOnNode(view, Short);
    }

    private static void AssertCentredOnNode(GraphView view, string name)
    {
        var screen = GraphOverlay.Project(view.NodeCenter(name), view.CurrentCamera);
        Assert.Equal(screen.X, view.TooltipTopLeft.X + view.TooltipBoxSize.Width / 2, 3);
    }

    /// <summary>Aynı kusur ad etiketinde de vardı — iki yüzey TEK yoldan geçer (kopya YASAK), bu yüzden
    /// düzeltme ikisini birden kapatır.</summary>
    [StaFact]
    public void The_selection_label_is_re_measured_for_every_name_too()
    {
        var view = Built();

        view.SelectedNode = Short;
        double shortWidth = view.SelectionLabelBoxSize.Width;

        view.SelectedNode = Long;

        Assert.True(view.SelectionLabelBoxSize.Width > shortWidth * 2,
            $"uzun ad etiketi genişletmedi ({view.SelectionLabelBoxSize.Width} vs {shortWidth})");
        var screen = GraphOverlay.Project(view.NodeCenter(Long), view.CurrentCamera);
        Assert.Equal(screen.X, view.SelectionLabelTopLeft.X + view.SelectionLabelBoxSize.Width / 2, 3);
    }

    /// <summary>
    /// AYIRT EDİCİ — <b>hangi düğüm seçilirse seçilsin</b> ad etiketi düğümün ALTINDA ve panelin iç payının
    /// İÇİNDE durur. Yeri kamera açar: seçim zaten kamerayı hareket ettirir, dolayısıyla yer açmanın doğru
    /// yeri orasıdır.
    ///
    /// <para><b>Kusur:</b> en alttaki bantta etiket düğümün ÜSTÜNE geçiyordu (takla), kenardaki sütunlarda
    /// ise panelin dışına taşıyordu. İkisi de kullanıcı gözlemi.</para>
    /// </summary>
    [StaFact]
    public void Every_selection_keeps_its_label_below_the_node_and_inside_the_inset()
    {
        var (nodes, edges) = SyntheticGraph.Build(96, 5, 2.2);
        var view = GraphTestView.Realized(Panel, () => false);
        view.Resources.MergedDictionaries.Add(DsResources.Load("Controls.xaml"));
        view.SetGraph(nodes, edges);

        double inset = QuietGraphLayout.ContentInset;
        foreach (var node in nodes)
        {
            view.SelectedNode = node.Name;
            var screen = GraphOverlay.Project(view.NodeCenter(node.Name), view.CurrentCamera);
            var topLeft = view.SelectionLabelTopLeft;
            var box = view.SelectionLabelBoxSize;

            Assert.True(topLeft.Y > screen.Y, $"{node.Name}: etiket düğümün ÜSTÜNDE ({topLeft.Y} <= {screen.Y})");
            // Konumlar ZEMİNE (Ground) göredir — panel başlığı bu ölçünün dışındadır.
            Assert.InRange(topLeft.X, inset - 0.51, view.ViewportSize.Width - inset - box.Width + 0.51);
            Assert.InRange(topLeft.Y, inset - 0.51, view.ViewportSize.Height - inset - box.Height + 0.51);
        }
    }

    /// <summary>
    /// Tooltip'in KAREYE mesafesi, ad etiketinin HALKAYA mesafesiyle aynıdır (kullanıcı kararı). İki yüzey
    /// aynı sayıyı kullanır ama farklı kenardan ölçer: hover edilen düğümün halkası yoktur.
    ///
    /// <para>Bu eşitlik aynı zamanda "etiket halkaya değmez" iddiasını da taşır — mesafe halkanın DIŞINDAN
    /// ölçülür. Kusur şuydu: konum prototipin <c>0.95 × düğüm kenarı</c> katsayısından geliyordu, o katsayı
    /// ise büyümeyen ve halkası CSS outline olan bir düğüm için kalibreliydi; bizimkinde etiket halkanın
    /// İÇİNE düşüyordu ("amber border'a neredeyse bitişik").</para>
    /// </summary>
    [StaFact]
    public void The_tooltip_clears_the_square_by_the_same_gap_the_label_clears_the_ring()
    {
        var view = Built();
        double scale = GraphView.HoverScale;

        view.SetHoverForTest(Short);
        var hovered = GraphOverlay.Project(view.NodeCenter(Short), view.CurrentCamera);
        double squareTop = hovered.Y - view.NodeSize / 2 * scale;
        double tooltipGap = squareTop - (view.TooltipTopLeft.Y + view.TooltipBoxSize.Height);

        view.SetHoverForTest(null);
        view.SelectedNode = Short;
        var selected = GraphOverlay.Project(view.NodeCenter(Short), view.CurrentCamera);
        double ringBottom = selected.Y
            + (view.NodeSize / 2 + GraphView.SelectionRingInset) * scale * view.CurrentCamera.Scale;
        double labelGap = view.SelectionLabelTopLeft.Y - ringBottom;

        Assert.Equal(GraphOverlay.OverlayGapPx, tooltipGap, 3);
        Assert.Equal(GraphOverlay.OverlayGapPx, labelGap, 3);
    }

    // [kopya YASAK] "Etiket halkaya değmez" iddiası artık yukarıdaki eşitlik testinin İÇİNDEDİR: mesafe
    // halkanın dışından ölçülür ve tam OverlayGapPx'tir. Ayrı bir ">= " testi onun zayıf kopyası olurdu.
    // Eski hâli (The_selection_label_clears_the_ring_of_the_enlarged_selected_node) buradan geldi; kusuru —
    // prototipin 0.95 × düğüm-kenarı katsayısı yüzünden etiketin halkanın içine düşmesi — orada yazılıdır.

    /// <summary>
    /// Hover büyütmesi komşusuna YAPIŞMAZ.
    ///
    /// <para><b>Eski iddia:</b> §2.3'ün <c>scale(1.7)</c>'si. <b>Değişme gerekçesi:</b> düğüm kenarı
    /// pitch'in 0.6'sıdır, dolayısıyla 1.7× büyüyen düğüm <c>1.7 × 0.6 = 1.02 pitch</c> kaplar — hücresini
    /// tam doldurur ve yanındakine değer. Kullanıcı gözlemi de buydu ("aktif bir node varsa bitişik gibi
    /// duruyor"). İddia bir sayı değil ORAN'dır: büyümüş düğüm adımın içinde kalmalı.</para>
    /// </summary>
    [Fact]
    public void An_enlarged_node_still_leaves_a_gap_to_its_neighbour()
    {
        double occupied = GraphView.HoverScale * QuietGraphLayout.NodeSizeFactor;

        Assert.True(occupied < 1.0,
            $"hover'da düğüm adımın {occupied:P0}'ini kaplıyor — komşusuna yapışır");
        // Halkanın da yeri olmalı, ama pratikte pay pitch'in %10'u kadar: tavan gevşek tutuldu ki
        // düğümü gereksizce küçültmeyelim.
        Assert.True(occupied <= 0.92, $"pay çok ince ({occupied:P0})");
    }
}
