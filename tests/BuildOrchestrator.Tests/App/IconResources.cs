using System.Windows;

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
    // [T60] Yükleme mekaniği DsResources'a taşındı — B3 dördüncü bir kopyasını yazacaktı (kopya YASAK).
    public static ResourceDictionary Load() => DsResources.Load("Icons.xaml");
}
