using System.Windows;
using System.Windows.Input;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T1 · madde 1.1] Grafın FARE seçimi — <b>tetikleyicinin kendisi</b>.
///
/// <para><b>Neden bu dosya var (ölçülmüş boşluk):</b> graf süitindeki TÜM seçim testleri seçimi
/// <c>view.SelectedNode = "…"</c> ile <b>programatik</b> kuruyordu; <c>MouseLeftButtonDown</c> hiçbir testte
/// yükseltilmiyordu. Yani "seçim değişince halka/kamera doğru mu" pinliydi ama "fare tıklaması seçimi
/// GERÇEKTEN değiştiriyor mu" hiç sorulmamıştı — A12'nin (reveal stagger) tam olarak düştüğü kör nokta.</para>
///
/// <para><b>Üretim yolu (değişmez kod):</b> <c>GraphView.xaml.cs:671-675</c> düğüm gövdesine
/// <c>MouseLeftButtonDown</c> bağlar (<c>e.Handled = true</c> + aynı düğümde toggle) ve <c>:172</c>
/// <c>Ground.MouseLeftButtonDown</c> ile seçimi kaldırır.</para>
///
/// <para><b>Tetikleme nasıl GERÇEK:</b> basış <see cref="MouseInput.PressLeft"/> ile yükseltilir (doğrudan
/// handler çağrısı DEĞİL); gerekçesi ve WPF mekaniği orada anlatılır. Aynı yardımcıyı <c>GraphPanZoomTests</c>
/// de kullanır — kopya YASAK.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class GraphClickTests
{
    private static IReadOnlyList<GraphNode> Nodes() =>
    [
        new("OSYS.Base", 0, GraphStatus.Discovered, Prefix: "OSYS."),
        new("OSYS.Data.Core", 1, GraphStatus.Discovered, Prefix: "OSYS."),
    ];

    private static IReadOnlyList<GraphEdge> Edges() => [new("OSYS.Base", "OSYS.Data.Core")];

    /// <summary>[fix-1 · S1] Headless GraphView kurulumu ORTAK yerde (<see cref="GraphTestView"/>) — bu dosya
    /// o bloğun ALTINCI inline kopyasını taşıyordu.</summary>
    private static GraphView NewView() => GraphTestView.Sized(new Size(600, 400));

    private static MouseButtonEventArgs PressLeft(UIElement target) => MouseInput.PressLeft(target);

    [StaFact]
    public void Clicking_a_node_selects_it_and_clicking_the_same_node_again_clears_the_selection()
    {
        var view = NewView();
        view.SetGraph(Nodes(), Edges());
        var body = view.NodeVisuals["OSYS.Base"].Body;

        PressLeft(body);
        Assert.Equal("OSYS.Base", view.SelectedNode);

        PressLeft(body); // aynı düğüm → toggle
        Assert.Null(view.SelectedNode);
    }

    [StaFact]
    public void Clicking_a_different_node_moves_the_selection_instead_of_clearing_it()
    {
        var view = NewView();
        view.SetGraph(Nodes(), Edges());

        PressLeft(view.NodeVisuals["OSYS.Base"].Body);
        PressLeft(view.NodeVisuals["OSYS.Data.Core"].Body);

        Assert.Equal("OSYS.Data.Core", view.SelectedNode);
    }

    [StaFact]
    public void Clicking_the_empty_ground_clears_the_selection()
    {
        var view = NewView();
        view.SetGraph(Nodes(), Edges());

        // [fix-1 · I-E] Ön-koşul da ÜRETİM TETİĞİYLE kurulur (programatik setter DEĞİL) ve AÇIKÇA assert edilir:
        // setter'a bir kapı eklense (bilinmeyen düğüm yok say / materyalizasyon başarısızsa geri al) bu test
        // sessizce vakum-yeşile düşer ve zemin kablosu koptuğunda kırmızı VERMEZDİ.
        PressLeft(view.NodeVisuals["OSYS.Base"].Body);
        Assert.Equal("OSYS.Base", view.SelectedNode);

        PressLeft(view.Ground);

        Assert.Null(view.SelectedNode);
    }

    /// <summary>
    /// AYIRT EDİCİ: düğüm tıklaması zemine ULAŞMAMALI. <c>e.Handled = true</c> silinirse basış zemine kabarır,
    /// <c>Ground.MouseLeftButtonDown</c> AYNI basışta seçimi hemen kaldırır ve düğüm seçimi hiç görünmez —
    /// bu test o senaryoda KIRMIZI verir (SelectedNode null gelir).
    /// </summary>
    [StaFact]
    public void A_click_on_a_node_is_handled_so_it_never_reaches_the_ground_and_undoes_itself()
    {
        var view = NewView();
        view.SetGraph(Nodes(), Edges());

        var args = PressLeft(view.NodeVisuals["OSYS.Base"].Body);

        Assert.True(args.Handled, "düğüm basışı Handled edilmedi — zemine sızar ve seçimi anında kaldırır");
        Assert.Equal("OSYS.Base", view.SelectedNode);
    }
}
