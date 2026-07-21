using System.IO;
using System.Xml.Linq;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [It-4a Foundation, Task 1 fix wave Important #1] MotionResourcesTests/TokenBrushesTests yalnız
/// Resources/Motion.xaml + Resources/Tokens.xaml'in İÇERİĞİNİ doğrular (TestAssets kopyasından, XamlReader ile) —
/// App.xaml'in bu iki dosyayı GERÇEKTEN merge ettiğini asla test etmezler. App.xaml:9-10'daki
/// <c>&lt;ResourceDictionary Source="Resources/Motion.xaml" /&gt;</c> satırı silinse ya da typo'lansa: build
/// kırılmaz (Source, derleme zamanı doğrulanmayan bir string), ve yukarıdaki testler de YEŞİL kalır (App.xaml'e
/// hiç bakmazlar) — regresyon yalnız kullanıcının ekranında, runtime'da bir XamlParseException / eksik kaynak
/// olarak ortaya çıkar. Bu test App.xaml'in KENDİSİNİ (TestAssets'e kopyalanan güncel kaynağı) okuyup
/// MergedDictionaries'in iki kaynağı da referans ettiğini yapısal (XML) olarak doğrulayarak bu boşluğu kapatır.
/// </summary>
public class AppResourcesMergeTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static XDocument LoadAppXaml()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestAssets", "App.xaml");
        return XDocument.Load(path);
    }

    [Fact]
    public void App_xaml_merges_the_Motion_resource_dictionary()
    {
        var doc = LoadAppXaml();

        var sources = doc.Descendants(Presentation + "ResourceDictionary")
            .Attributes("Source")
            .Select(a => a.Value)
            .ToList();

        Assert.Contains("Resources/Motion.xaml", sources);
    }

    [Fact]
    public void App_xaml_merges_the_Tokens_resource_dictionary()
    {
        var doc = LoadAppXaml();

        var sources = doc.Descendants(Presentation + "ResourceDictionary")
            .Attributes("Source")
            .Select(a => a.Value)
            .ToList();

        Assert.Contains("Resources/Tokens.xaml", sources);
    }

    [Fact]
    public void App_xaml_merges_the_Icons_resource_dictionary()
    {
        var doc = LoadAppXaml();

        var sources = doc.Descendants(Presentation + "ResourceDictionary")
            .Attributes("Source")
            .Select(a => a.Value)
            .ToList();

        Assert.Contains("Resources/Icons.xaml", sources);
    }

    /// <summary>
    /// Beklenen liste [T64] ile İKİDEN ÜÇE çıktı: <c>Resources/Icons.xaml</c> uygulamanın TEK ikon kaynağı
    /// olarak merge zincirine katıldı (Segoe MDL2 ikon fontu ve koda gömülü <c>Geometry.Parse</c> path'leri
    /// yerine). İddia bilerek TAM EŞİTLİK'tir ("en az N" DEĞİL): bu testin tüm değeri, merge zincirine
    /// düşünmeden bir sözlük eklenmesinin derlemeyi kırmasıdır — yükleme sırası (token'lar ikonlardan önce)
    /// ve zincirin kısalığı bilinçli kararlardır.
    /// </summary>
    [Fact]
    public void App_xaml_merges_exactly_the_three_foundation_dictionaries_no_more_no_less()
    {
        var doc = LoadAppXaml();

        var mergedDictionaries = doc.Descendants(Presentation + "ResourceDictionary.MergedDictionaries")
            .Single();
        var sources = mergedDictionaries.Elements(Presentation + "ResourceDictionary")
            .Select(e => e.Attribute("Source")?.Value)
            .ToList();

        Assert.Equal(["Resources/Motion.xaml", "Resources/Tokens.xaml", "Resources/Icons.xaml"], sources);
    }
}
