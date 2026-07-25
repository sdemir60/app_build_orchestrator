using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T60] App'in kaynak sözlüklerini headless test host'unda yükleyen TEK yardımcı. <c>pack://</c> URI'ler
/// gerçek bir <see cref="Application"/> olmadan çözülmez — sözlükler csproj'un <c>TestAssets\Resources</c>'a
/// kopyaladığı dosyalardan <see cref="XamlReader"/> ile okunur (aynı glob Controls.xaml'i de kopyalar).
///
/// <para>T60 öncesinde bu blok üç test sınıfında (MotionResourcesTests, TokenBrushesTests, IconResources)
/// AYRI AYRI duruyordu; DS kontrol testleri dördüncüsü olacaktı — tek yere toplandı (kopya YASAK, CLAUDE.md).</para>
/// </summary>
internal static class DsResources
{
    /// <summary>
    /// App'in KENDİ tiplerine yapılan XAML atıflarını gevşek (loose) yükleme için adreslenebilir kılar.
    /// <c>Resources/Controls.xaml</c> içinde eşleme <c>clr-namespace:…</c>'tır — assembly ADI OLMADAN, çünkü
    /// WPF markup compiler'ı derlenmekte olan assembly'nin tiplerini ancak böyle çözer (adı yazılırsa
    /// MC3072 verir). Gevşek XAML'de ise kural TERSİDİR: assembly adı yoksa tipler ÇAĞIRAN assembly'de
    /// (bu test projesinde) aranır ve bulunamaz. Bu yüzden metin, parse edilmeden önce tamamlanır.
    /// </summary>
    private const string LocalNamespace = "clr-namespace:BuildOrchestrator.App.Controls";
    private const string QualifiedNamespace = LocalNamespace + ";assembly=BuildOrchestrator.App";

    public static string AssetPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "TestAssets", "Resources", fileName);

    public static ResourceDictionary Load(string fileName)
    {
        string xaml = File.ReadAllText(AssetPath(fileName))
            .Replace($"\"{LocalNamespace}\"", $"\"{QualifiedNamespace}\"", StringComparison.Ordinal);
        return (ResourceDictionary)XamlReader.Parse(xaml);
    }

    /// <summary>
    /// Uygulamanın merge zincirinin AYNISINI (Motion → Tokens → Icons → Controls) taşıyan bir kaynak kapsamı.
    /// Sıra üretimdeki App.xaml ile birebir aynıdır — bir stil yanlış sırada çözülüyorsa test de görmelidir.
    /// </summary>
    public static Border NewHost()
    {
        var host = new Border();
        foreach (string name in new[] { "Motion.xaml", "Tokens.xaml", "Icons.xaml", "Controls.xaml" })
            host.Resources.MergedDictionaries.Add(Load(name));
        return host;
    }

    /// <summary>Kontrolü host'a koyar, ekran dışı bir pencerede gösterir ve şablonunu uygular — DynamicResource
    /// setter'ları ancak öğe ağaca girince çözülür, bu yüzden ADIM SIRASI önemlidir. Dönen pencere canlı
    /// tutulmalıdır (çağıranın metodu bitene kadar scope'ta kalır).</summary>
    public static Window Realize(Border host, FrameworkElement content)
    {
        host.Child = content;
        var window = AnimationHost.ShowOffscreen(host, width: 400, height: 200);
        content.ApplyTemplate();
        content.UpdateLayout();
        return window;
    }

    public static Color TokenColor(FrameworkElement host, string key)
        => ((SolidColorBrush)host.FindResource(key)).Color;

    public static Color ColorOf(Brush? brush) => ((SolidColorBrush)brush!).Color;

    /// <summary>[L1/It-5 perf] Bir kökün GERÇEKTEN kurduğu nesneler — görsel VE mantıksal ağacın birleşimi
    /// (tekilleştirilmiş). Yalnız görsel ağacı saymak perf metriği olarak yanıltıcıdır: Collapsed bir dalın
    /// şablonu genişlemez, bu yüzden <c>Button.Content</c> (Viewbox/Canvas/Path) ve <c>Popup.Child</c> alt-ağacı
    /// görsel ağaca hiç girmez — ama nesne olarak kurulmuş ve satır başına ödenmiştir. Ölçüm bu yüzden mantıksal
    /// çocukları da gezer. (Tooltip'ler hiçbir ağaca girmez → bu sayıya dahil DEĞİLDİR.)</summary>
    public static IReadOnlyCollection<DependencyObject> RealizedObjects(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is Visual)
            {
                int count = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(node, i);
                    if (seen.Add(child)) stack.Push(child);
                }
            }
            foreach (object child in LogicalTreeHelper.GetChildren(node))
                if (child is DependencyObject d && seen.Add(d)) stack.Push(d);
        }
        return seen;
    }

    /// <summary>Görsel ağacın tamamı — şablon içindeki şablonlara da iner (split button'ın yarımları gibi).</summary>
    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var grandChild in Descendants(child)) yield return grandChild;
        }
    }
}
