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
    /// AYIRT EDİCİ — etiket seçili düğümün HALKASINA değmez.
    ///
    /// <para><b>Kusur:</b> "proje adı amber border'a neredeyse bitişik". Konum prototipin
    /// <c>0.95 × düğüm kenarı</c> katsayısından geliyordu; o katsayı büyümeyen ve halkası CSS outline olan
    /// bir düğüm için kalibreliydi. Bizim seçili düğümümüz hover ölçeğinde durur ve halkası kareden taşar,
    /// dolayısıyla etiket halkanın İÇİNE düşüyordu.</para>
    /// </summary>
    [StaFact]
    public void The_selection_label_clears_the_ring_of_the_enlarged_selected_node()
    {
        var view = Built();
        view.SelectedNode = Short;

        var screen = GraphOverlay.Project(view.NodeCenter(Short), view.CurrentCamera);
        double ringBottom = screen.Y
            + (view.NodeSize / 2 + GraphView.CellOverhang) * GraphView.HoverScale * view.CurrentCamera.Scale;

        Assert.True(view.SelectionLabelTopLeft.Y >= ringBottom + GraphOverlay.LabelGapPx - 0.001,
            $"etiket {view.SelectionLabelTopLeft.Y}, halkanın altı {ringBottom} — halkaya biniyor");
    }

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
