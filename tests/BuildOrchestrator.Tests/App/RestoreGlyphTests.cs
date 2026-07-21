using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T62/K8 · T64] Maximize butonunun glyph'i tasarımda tanımsızdı; v7 kararı K8: <c>WindowState=Maximized</c>
/// iken "restore" (iç içe/kaydırılmış iki kare), normalde tek kare. Kablaj
/// <see cref="CaptionGlyphs.BindMaxButton"/> içinde TEK yerdedir — MainWindow ile bu test BİREBİR aynı yolu
/// kullanır.
///
/// <para>[T64] Karşılaştırma artık KARAKTER değil GEOMETRİ üzerindedir: iddialar
/// <c>Resources/Icons.xaml</c>'deki gerçek geometri nesnelerine referans eşitliğiyle bağlanır, üstelik her
/// durumda ÖTEKİ geometrinin gelmediği de ayrıca doğrulanır — "null değil" gibi ayırt etmeyen bir kontrol
/// önceki karakter karşılaştırmasından zayıf olurdu.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class RestoreGlyphTests
{
    /// <summary>Icons.xaml'i, kaynak kapsamı KENDİSİ olan bir <see cref="Path"/>'e bağlar (TryFindResource
    /// önce elemanın kendi <c>Resources</c>'una bakar) — headless test host'unda merge zinciri yoktur.</summary>
    private static (Path Glyph, ResourceDictionary Icons) NewGlyphInIconScope()
    {
        var icons = IconResources.Load();
        var glyph = new Path();
        glyph.Resources.MergedDictionaries.Add(icons);
        return (glyph, icons);
    }

    [StaFact]
    public void Pure_glyph_choice_resolves_to_the_drawn_geometry_for_the_window_state()
    {
        var (scope, icons) = NewGlyphInIconScope();
        var maximize = (Geometry)icons["Icon.CaptionMaximize"];
        var restore = (Geometry)icons["Icon.CaptionRestore"];

        Assert.Same(maximize, CaptionGlyphs.MaxButtonGlyph(scope, WindowState.Normal));
        Assert.Same(maximize, CaptionGlyphs.MaxButtonGlyph(scope, WindowState.Minimized));
        Assert.Same(restore, CaptionGlyphs.MaxButtonGlyph(scope, WindowState.Maximized));
        Assert.NotSame(maximize, restore);
    }

    [StaFact]
    public void Bound_button_starts_with_the_state_it_is_bound_in()
    {
        var window = new Window { WindowState = WindowState.Maximized };
        var (glyph, icons) = NewGlyphInIconScope();

        CaptionGlyphs.BindMaxButton(window, glyph);

        Assert.Same(icons["Icon.CaptionRestore"], glyph.Data);
        Assert.NotSame(icons["Icon.CaptionMaximize"], glyph.Data);
    }

    [StaFact]
    public void Glyph_swaps_both_ways_when_the_window_state_changes()
    {
        var window = new Window();
        var (glyph, icons) = NewGlyphInIconScope();
        CaptionGlyphs.BindMaxButton(window, glyph);
        Assert.Same(icons["Icon.CaptionMaximize"], glyph.Data);

        window.WindowState = WindowState.Maximized;
        Assert.Same(icons["Icon.CaptionRestore"], glyph.Data);

        window.WindowState = WindowState.Normal;
        Assert.Same(icons["Icon.CaptionMaximize"], glyph.Data);
    }
}
