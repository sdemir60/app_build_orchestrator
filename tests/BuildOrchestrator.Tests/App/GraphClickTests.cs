using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using IoPath = System.IO.Path;

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
/// <para><b>Tetikleme nasıl GERÇEK:</b> WPF'te <c>UIElement.MouseLeftButtonDown</c> <b>Direct</b> yönlendirmelidir;
/// "kabarma" görüntüsünü <see cref="Mouse.MouseDownEvent"/> (bubbling) üzerindeki sınıf handler'ı üretir —
/// kabarma yolundaki her öğede tek tek yeniden yükseltir ve <c>Handled</c>'ı geri kopyalar. Bu yüzden testler
/// gövdeye <see cref="Mouse.MouseDownEvent"/> yükseltir: düğümün handler'ı da, zeminin handler'ı da üretimde
/// hangi sırayla/koşulla koşuyorsa burada da öyle koşar (doğrudan handler çağrısı DEĞİL).</para>
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

    /// <summary>GraphRenderTests.NewView ile AYNI headless kurulum (token sözlükleri + ölçüm/yerleşim).</summary>
    private static GraphView NewView()
    {
        var view = new GraphView { AnimationsEnabledProvider = () => false };
        foreach (string name in new[] { "Tokens.xaml", "Motion.xaml", "Icons.xaml" })
        {
            using var stream = File.OpenRead(IoPath.Combine(AppContext.BaseDirectory, "TestAssets", "Resources", name));
            view.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
        }
        view.Measure(new Size(600, 400));
        view.Arrange(new Rect(0, 0, 600, 400));
        return view;
    }

    /// <summary>Gerçek sol-tuş basışı: <see cref="Mouse.MouseDownEvent"/> kabarır ve WPF'in kendi sınıf
    /// handler'ı yol üstündeki her öğede <c>MouseLeftButtonDown</c>'ı yükseltir (bkz. sınıf özeti).</summary>
    private static MouseButtonEventArgs PressLeft(UIElement target)
    {
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = Mouse.MouseDownEvent,
        };
        target.RaiseEvent(args);
        return args;
    }

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
        view.SelectedNode = "OSYS.Base"; // ön-koşul (bu testin iddiası zemin tıklaması, seçimin kurulması değil)

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
