using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T64] <c>Resources/Icons.xaml</c>'i headless test host'unda yükler. <c>pack://</c> URI'ler gerçek bir
/// <see cref="Application"/> olmadan çözülmez (bkz. TokenBrushesTests/MotionResourcesTests aynı desen) —
/// bu yüzden sözlük, csproj'un <c>TestAssets\Resources</c>'a kopyaladığı dosyadan <see cref="XamlReader"/>
/// ile okunur. Üç test sınıfı (IconGeometryTests, RestoreGlyphTests, CopyLogTests) aynı yükleyiciyi
/// paylaşır — kopya YASAK (CLAUDE.md).
/// </summary>
internal static class IconResources
{
    public static ResourceDictionary Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestAssets", "Resources", "Icons.xaml");
        using var stream = File.OpenRead(path);
        return (ResourceDictionary)XamlReader.Load(stream);
    }
}
