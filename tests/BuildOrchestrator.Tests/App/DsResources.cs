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

    /// <summary>[G2 fix round 1] Gömülü Geist Mono'nun test karşılığı: <c>pack://</c> aileler gerçek bir
    /// <see cref="Application"/> olmadan çözülmez, bu yüzden AYNI OTF dosyalarına <c>file://</c> tabanlı bir
    /// aile kurulur. Desen <c>TrackedTextBlockTests</c>'te (T57) kuruldu; oradaki sans karşılığıyla birlikte
    /// tek doğruluk kaynağı burasıdır (kopya YASAK, CLAUDE.md).</summary>
    public static FontFamily MonoFontFamily => new(
        new Uri(Path.Combine(AppContext.BaseDirectory, "TestAssets", "Fonts") + Path.DirectorySeparatorChar),
        "./#Geist Mono");

    public static ResourceDictionary Load(string fileName)
    {
        string xaml = File.ReadAllText(AssetPath(fileName))
            .Replace($"\"{LocalNamespace}\"", $"\"{QualifiedNamespace}\"", StringComparison.Ordinal);
        return (ResourceDictionary)XamlReader.Parse(xaml);
    }

    /// <summary>[A13/T3 fix-1 · B5] Bir token sözlüğünden konsol paleti — <c>ConsoleColorizerTests</c> ve
    /// <c>ConsoleViewTests</c> bu tek satırı ayrı ayrı taşıyordu (kopya YASAK, CLAUDE.md).</summary>
    public static BuildOrchestrator.App.Console.ConsolePalette ConsolePaletteFrom(ResourceDictionary tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return BuildOrchestrator.App.Console.ConsolePalette.FromLookup(k => tokens[k]);
    }

    /// <summary>Üretimdeki App.xaml merge sırası (AppResourcesMergeTests bunu ayrıca pinler).</summary>
    private static readonly string[] MergeChain = ["Motion.xaml", "Tokens.xaml", "Icons.xaml", "Controls.xaml"];

    /// <summary>
    /// Uygulamanın merge zincirinin AYNISINI (Motion → Tokens → Icons → Controls) taşıyan bir kaynak kapsamı.
    /// Sıra üretimdeki App.xaml ile birebir aynıdır — bir stil yanlış sırada çözülüyorsa test de görmelidir.
    /// </summary>
    public static Border NewHost()
    {
        var host = new Border();
        foreach (string name in MergeChain) host.Resources.MergedDictionaries.Add(Load(name));
        return host;
    }

    /// <summary>[T49 FINAL PASS] Aynı zincirin ÇIPLAK sözlük hâli — bir <see cref="Window"/>'un üstünde ebeveyn
    /// olmadığı için ona host verilemez; kaynak kapsamı doğrudan <c>Window.Resources</c>'a enjekte edilir
    /// (bkz. <c>MainWindow</c> ctor'ının <c>resourceScope</c> parametresi).</summary>
    public static ResourceDictionary NewScope()
    {
        var scope = new ResourceDictionary();
        foreach (string name in MergeChain) scope.MergedDictionaries.Add(Load(name));
        return scope;
    }

    /// <summary>Kontrolü host'a koyar, ekran dışı bir pencerede gösterir ve şablonunu uygular — DynamicResource
    /// setter'ları ancak öğe ağaca girince çözülür, bu yüzden ADIM SIRASI önemlidir. Dönen pencere canlı
    /// tutulmalıdır (çağıranın metodu bitene kadar scope'ta kalır).
    ///
    /// <para>[About] <b>Boyut ARTIK parametre</b> — varsayılan eski davranışla BİREBİR aynıdır (400×200).
    /// Gerekçe (ÖLÇÜLDÜ): 200px'lik pencerede 620px'lik bir modal DİKEYDE KIRPILIR ve
    /// <see cref="FrameworkElement.ActualHeight"/> içeriği ne olursa olsun aynı doymuş değeri döndürür — bu
    /// hâliyle <c>AboutDialogTests.Switching_tabs_never_resizes_the_dialog</c> içerik alanının sabit
    /// yüksekliği KALDIRILDIĞINDA BİLE yeşil kalıyordu, yani hiçbir şeyi ayırt etmiyordu. Yükseklik ölçen
    /// çağıranlar içeriği sığdıran bir pencere ister.</para></summary>
    public static Window Realize(Border host, FrameworkElement content, double width = 400, double height = 200)
    {
        host.Child = content;
        var window = AnimationHost.ShowOffscreen(host, width, height);
        content.ApplyTemplate();
        content.UpdateLayout();
        return window;
    }

    /// <summary>
    /// Tasarım token'ı tüketen özellikler. Bir DP'nin bir düğüm için geçerli olup olmadığı ELENMEZ: WPF her
    /// <see cref="DependencyObject"/> üzerinde her DP'yi okumaya izin verir ve ilgisiz olanlar varsayılanını
    /// (tipiyle uyumlu) döner — yani yanlış pozitif üretmez.
    ///
    /// <para>[fix round 2] <see cref="RowDefinition.HeightProperty"/> / <see cref="ColumnDefinition.WidthProperty"/>
    /// listenin BAŞINDA olmalıydı: <c>c6e9a21</c>'in TAM OLARAK patladığı özellikler bunlar
    /// (<c>Size.ActionBarHeight</c> Double token'ı bir <c>GridLength</c>'e veriliyordu). Bu ikisi
    /// <see cref="Grid"/>'in görsel/mantıksal çocuğu DEĞİLDİR — ağaç gezintisine hiç girmezler, bu yüzden
    /// <see cref="DynamicResourceTypeMismatches"/> her <see cref="Grid"/> için ayrıca ziyaret eder.</para>
    /// </summary>
    private static readonly DependencyProperty[] TokenProperties =
    [
        Control.BackgroundProperty, Control.BorderBrushProperty, Control.ForegroundProperty,
        Control.BorderThicknessProperty, Control.PaddingProperty, Control.FontSizeProperty,
        Control.FontFamilyProperty, Control.FontWeightProperty,
        Panel.BackgroundProperty,
        Border.BackgroundProperty, Border.BorderBrushProperty, Border.BorderThicknessProperty,
        Border.PaddingProperty, Border.CornerRadiusProperty,
        TextBlock.BackgroundProperty, TextBlock.ForegroundProperty, TextBlock.FontSizeProperty,
        TextBlock.FontFamilyProperty, TextBlock.FontWeightProperty,
        System.Windows.Shapes.Shape.FillProperty, System.Windows.Shapes.Shape.StrokeProperty,
        System.Windows.Shapes.Shape.StrokeThicknessProperty, System.Windows.Shapes.Path.DataProperty,
        UIElement.EffectProperty, UIElement.OpacityMaskProperty,
        FrameworkElement.WidthProperty, FrameworkElement.HeightProperty,
        FrameworkElement.MinWidthProperty, FrameworkElement.MinHeightProperty, FrameworkElement.MarginProperty,
        RowDefinition.HeightProperty, RowDefinition.MinHeightProperty,      // c6e9a21'in kendi özellikleri
        ColumnDefinition.WidthProperty, ColumnDefinition.MinWidthProperty,
    ];

    /// <summary>Denetlenen özellik kümesi — testler "bug'ın kendi özelliği gerçekten listede mi" sorusunu
    /// doğrudan sorabilsin diye açılır (bkz. <c>TokenRealizeCoverageTests</c>).</summary>
    public static IReadOnlyList<DependencyProperty> CheckedProperties => TokenProperties;

    /// <summary>
    /// [T49 fix round 1 · A1] Realize edilmiş bir ağaçtaki HER <c>DynamicResource</c> bağını çözer ve
    /// <b>hedef DP tipine uyup uymadığını</b> denetler; uymayanların listesini döner (boş = temiz).
    ///
    /// <para><b>Neden gerekli — ölçülen gerçek:</b> <c>Measure</c>/<c>Arrange</c> yalnız YERLEŞİM'e giren
    /// özellikleri okur (<c>Height</c>, <c>Margin</c>, <c>RowDefinition.Height</c>) — <c>c6e9a21</c>'in
    /// GridLength bug'ı bu yüzden yakalanır. Ama <c>Background</c>/<c>Foreground</c>/<c>Fill</c> RENDER-ONLY'dir:
    /// gerçek bir <c>PresentationSource</c> olmadan hiç okunmaz. Dahası WPF, <c>GetValue</c> OKUMA yolunda tip
    /// doğrulaması YAPMAZ (ölçüldü: bir <c>Double</c>, <c>Background</c>'dan sessizce geri gelir). Bu yüzden
    /// uyum burada AÇIKÇA denetlenir — "patladı mı" değil, "doğru tip mi" sorulur.</para>
    ///
    /// <para>Şablon içindeki (ApplyTemplate ile SONRADAN doğan) öğeler de kapsanır: gezinti görsel + mantıksal
    /// ağacın birleşimidir (<see cref="RealizedObjects"/>, kök DAHİL) ve şablon uygulandıktan SONRA
    /// çağrılmalıdır. <b>KAPSAM DIŞI:</b> bir <c>Style</c>/<c>Setter</c> üzerinden gelen değerler yerel değer
    /// DEĞİLDİR ve burada görünmez — onları uygulandıkları anda WPF'in kendi doğrulaması yakalar (setter'ın
    /// hedefi yanlış tipteyse <c>Style</c> uygulanırken fırlar).</para>
    /// </summary>
    public static IReadOnlyList<string> DynamicResourceTypeMismatches(DependencyObject root)
    {
        var offenders = new List<string>();
        foreach (var node in RealizedObjects(root))
        {
            Check(node);
            // Grid tanımları ağacın çocuğu DEĞİLDİR (ne görsel ne mantıksal) — c6e9a21 tam da oradaydı.
            if (node is Grid grid)
            {
                foreach (var row in grid.RowDefinitions) Check(row);
                foreach (var column in grid.ColumnDefinitions) Check(column);
            }
        }
        return offenders;

        void Check(DependencyObject node)
        {
            foreach (var property in TokenProperties)
            {
                // GetValue = değeri GERÇEKTEN talep et (DynamicResource ancak burada çözülür). WPF okuma
                // yolunda tip DOĞRULAMASI YAPMAZ — ölçüldü: bir şablon içinden Background'a bağlanan Double
                // token'ı `GetValue` sessizce 40 olarak geri verir. Uyum bu yüzden BURADA denetlenir.
                object? value = node.GetValue(property);
                if (value is not null && !property.PropertyType.IsInstanceOfType(value))
                    offenders.Add(
                        $"{node.GetType().Name}.{property.Name}: {property.PropertyType.Name} bekleniyordu, {value.GetType().Name} geldi");
            }
        }
    }

    public static Color TokenColor(FrameworkElement host, string key)
        => ((SolidColorBrush)host.FindResource(key)).Color;

    public static Color ColorOf(Brush? brush) => ((SolidColorBrush)brush!).Color;

    /// <summary>[L1/It-5 perf] Bir kökün GERÇEKTEN kurduğu nesneler — görsel VE mantıksal ağacın birleşimi
    /// (tekilleştirilmiş). Yalnız görsel ağacı saymak perf metriği olarak yanıltıcıdır: Collapsed bir dalın
    /// şablonu genişlemez, bu yüzden <c>Button.Content</c> (Viewbox/Canvas/Path) ve <c>Popup.Child</c> alt-ağacı
    /// görsel ağaca hiç girmez — ama nesne olarak kurulmuş ve satır başına ödenmiştir. Ölçüm bu yüzden mantıksal
    /// çocukları da gezer. (Tooltip'ler hiçbir ağaca girmez → bu sayıya dahil DEĞİLDİR.)
    ///
    /// <para>[fix round 2] <b>KÖK ARTIK SAYIYA DAHİL.</b> Önceden yalnız torunlar dönüyordu: perf çağıranları
    /// bunu elle <c>+ 1</c> ile telafi ediyordu (kopya bir düzeltme, üç yerde) ve daha kötüsü
    /// <see cref="DynamicResourceTypeMismatches"/> kök öğenin KENDİ token bağlarını hiç denetlemiyordu —
    /// bir kökün <c>Background</c>'ına verilmiş yanlış tipli token görünmez kalırdı.</para></summary>
    public static IReadOnlyCollection<DependencyObject> RealizedObjects(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject> { root };
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

    /// <summary>[A13/T3 fix-1 · C11] <see cref="Descendants"/>'ın simetrik karşılığı: görsel ata zinciri (düğümün
    /// KENDİSİ hariç). Süitte bu yürüyüş dört ayrı yerde elle yazılmıştı; hepsi buradan beslenir
    /// (kopya YASAK, CLAUDE.md).</summary>
    public static IEnumerable<DependencyObject> Ancestors(DependencyObject node) =>
        SelfAndAncestors(node).Skip(1);

    /// <summary>
    /// [A13/T3 fix-2 · 7] Ata zinciri, düğümün <b>KENDİSİ dahil</b>.
    ///
    /// <para><b><paramref name="includeLogical"/> farkı KORUNMUŞTUR</b> (sessizce birleştirilmedi): süitteki üç
    /// kopyadan ikisi salt GÖRSEL ağacı yürüyordu, biri (<c>SettingsDialogFocusTests.IsDescendantOf</c> — odak
    /// tuzağı) <b>görsel VE mantıksal</b> yürüyor. Fark gerçektir: <c>Keyboard.FocusedElement</c> bir
    /// <c>Popup</c>/<c>ContentElement</c> altında olabilir ve orada görsel zincir kopar, mantıksal zincir
    /// devam eder. Varsayılan (görsel) davranış eski iki çağıranla birebir aynıdır.</para>
    ///
    /// <para>Salt-görsel kipte <see cref="Visual"/> olmayan bir düğümde zincir <c>null</c> ile biter —
    /// <see cref="VisualTreeHelper.GetParent"/> orada fırlatırdı; eski çağıranların hepsi yalnız
    /// <see cref="Visual"/> zincirleri yürüdüğü için davranış değişmez.</para></summary>
    public static IEnumerable<DependencyObject> SelfAndAncestors(DependencyObject node, bool includeLogical = false)
    {
        ArgumentNullException.ThrowIfNull(node);
        for (DependencyObject? cur = node; cur is not null; cur = ParentOf(cur, includeLogical))
            yield return cur;
    }

    private static DependencyObject? ParentOf(DependencyObject node, bool includeLogical) =>
        node is Visual ? VisualTreeHelper.GetParent(node)
        : includeLogical ? LogicalTreeHelper.GetParent(node)
        : null;

    /// <summary>[A13/T3 fix-2 · 7] <paramref name="node"/>, <paramref name="ancestor"/>'ın kendisi ya da onun
    /// bir torunu mu. <paramref name="includeLogical"/> için bkz. <see cref="SelfAndAncestors"/>.</summary>
    public static bool IsSelfOrDescendantOf(DependencyObject node, DependencyObject ancestor, bool includeLogical = false) =>
        SelfAndAncestors(node, includeLogical).Any(n => ReferenceEquals(n, ancestor));

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
