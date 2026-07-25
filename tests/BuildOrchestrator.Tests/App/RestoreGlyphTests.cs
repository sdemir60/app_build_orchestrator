using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App.Shell;
using IoFile = System.IO.File;
using IoPath = System.IO.Path;

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
///
/// <para>[T64 review · fix wave 1] Aynı kablaj butonun <b>UIA adını</b> da sürer: içerik artık bir karakter
/// değil çizilmiş bir <see cref="Shape"/> olduğundan UIA ad TÜRETEMEZ ve ad duruma bağlı olduğundan statik
/// bir XAML attribute'ü maximize'da bayatlardı. Durum taşımayan min/close adları XAML'de sabittir — o üçlünün
/// hiçbirinin adsız kalmadığı ayrıca pinlenir.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class RestoreGlyphTests
{
    /// <summary>Icons.xaml'i, kaynak kapsamı KENDİSİ olan bir <see cref="Path"/>'e bağlar (TryFindResource
    /// önce elemanın kendi <c>Resources</c>'una bakar) — headless test host'unda merge zinciri yoktur.
    /// Buton, MainWindow'daki gibi glyph'i içerik olarak taşır (kablajın ikinci hedefi: UIA adı).</summary>
    private static (Button Button, Path Glyph, ResourceDictionary Icons) NewCaptionButtonInIconScope()
    {
        var icons = IconResources.Load();
        var glyph = new Path();
        glyph.Resources.MergedDictionaries.Add(icons);
        return (new Button { Content = glyph }, glyph, icons);
    }

    [StaFact]
    public void Pure_glyph_choice_resolves_to_the_drawn_geometry_for_the_window_state()
    {
        var (_, scope, icons) = NewCaptionButtonInIconScope();
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
        var (button, glyph, icons) = NewCaptionButtonInIconScope();

        CaptionGlyphs.BindMaxButton(window, button, glyph);

        Assert.Same(icons["Icon.CaptionRestore"], glyph.Data);
        Assert.NotSame(icons["Icon.CaptionMaximize"], glyph.Data);
    }

    [StaFact]
    public void Glyph_swaps_both_ways_when_the_window_state_changes()
    {
        var window = new Window();
        var (button, glyph, icons) = NewCaptionButtonInIconScope();
        CaptionGlyphs.BindMaxButton(window, button, glyph);
        Assert.Same(icons["Icon.CaptionMaximize"], glyph.Data);

        window.WindowState = WindowState.Maximized;
        Assert.Same(icons["Icon.CaptionRestore"], glyph.Data);

        window.WindowState = WindowState.Normal;
        Assert.Same(icons["Icon.CaptionMaximize"], glyph.Data);
    }

    // ---------------------------------------------------------------- erişilebilirlik adları

    [StaFact]
    public void The_max_button_announces_maximize_or_restore_following_the_window_state()
    {
        // Ad glyph ile AYNI anda döner: statik bir XAML attribute'ü maximize edilmiş pencerede hâlâ
        // "Maximize" derdi (yanlış ad, ADSIZ olmaktan da kötü).
        var window = new Window();
        var (button, glyph, _) = NewCaptionButtonInIconScope();

        CaptionGlyphs.BindMaxButton(window, button, glyph);
        Assert.Equal("Maximize", AutomationProperties.GetName(button));

        window.WindowState = WindowState.Maximized;
        Assert.Equal("Restore", AutomationProperties.GetName(button));

        window.WindowState = WindowState.Normal;
        Assert.Equal("Maximize", AutomationProperties.GetName(button));
    }

    [StaFact]
    public void The_minimize_and_close_buttons_carry_a_static_automation_name_in_the_markup()
    {
        // Bu ikisinin adı duruma bağlı DEĞİLDİR → XAML'de sabit. İçerikleri Shape olduğu için ad düşerse
        // ekran okuyucu ikisini de yalnız "button" diye okur; MainWindow DI olmadan kurulamadığından
        // (engine/vm/console) guard işaretlemenin KENDİSİNİ okur — NoHardcodedColorTests ile aynı desen.
        string markup = IoFile.ReadAllText(IoPath.Combine(RepoPaths.AppSrcRoot, "MainWindow.xaml"));

        Assert.Contains("AutomationProperties.Name=\"Minimize\"", markup, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Close\"", markup, StringComparison.Ordinal);
    }
}
