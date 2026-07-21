using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [T62/K8 · T64] Title bar max/restore glyph'i. Tasarımda maximize butonu tek karedir ve maximize DURUMU
/// tanımsız bırakılmıştı; v7 kararı <b>K8</b>: <c>WindowState=Maximized</c> iken glyph "restore" (iç içe/
/// kaydırılmış iki kare) olur, normale dönünce tek kareye geri döner.
///
/// <para>[T64] Glyph artık Unicode KARAKTER değil, <c>Resources/Icons.xaml</c>'deki ÇİZİLMİŞ geometridir —
/// min/max/close ile aynı 10x10 1px-stroke ızgarası. Bu sınıf path data TAŞIMAZ, yalnız <b>anahtar</b>
/// bilir; geometri, verilen elemanın kaynak kapsamından çözülür (App.xaml merge zinciri). Sözlük merge
/// edilmemiş bir bağlamda (headless) çözüm <c>null</c> döner ve <see cref="Path"/> hiçbir şey çizmez —
/// çökme yok.</para>
/// </summary>
public static class CaptionGlyphs
{
    /// <summary>Tek kare (Icons.xaml, <c>_ds_bundle.js:1164-1172</c> WinBtn 'max').</summary>
    public const string MaximizeKey = "Icon.CaptionMaximize";

    /// <summary>İki iç içe kare (Icons.xaml, K8 türetmesi).</summary>
    public const string RestoreKey = "Icon.CaptionRestore";

    /// <summary>Pencere durumuna karşılık gelen ikon anahtarı — saf fonksiyon.</summary>
    public static string MaxButtonGlyphKey(WindowState state)
        => state == WindowState.Maximized ? RestoreKey : MaximizeKey;

    /// <summary>
    /// Pencere durumunun geometrisini <paramref name="scope"/>'un kaynak kapsamında çözer. Sözlük o
    /// kapsamda yoksa <c>null</c> (bkz. sınıf doc'u).
    /// </summary>
    public static Geometry? MaxButtonGlyph(FrameworkElement scope, WindowState state)
        => scope.TryFindResource(MaxButtonGlyphKey(state)) as Geometry;

    /// <summary>
    /// Pencerenin <see cref="Window.WindowState"/>'ini izleyip max/restore butonunun glyph'ini süren TEK kablaj
    /// (MainWindow ve testi aynı yolu kullanır — kopya YASAK, CLAUDE.md). Butonun İÇERİĞİ burada kurulmaz:
    /// XAML'de duran <see cref="Path"/>'in yalnız <see cref="Path.Data"/>'sı değiştirilir (stroke/boyut
    /// stilini XAML sahiplenir).
    ///
    /// <para><b>Neden <c>DependencyPropertyDescriptor</c>, <c>StateChanged</c> DEĞİL:</b> <c>StateChanged</c>
    /// yalnız pencere gösterildikten sonra (WM_SIZE üzerinden) tetiklenir; DP izleyicisi hem OS kaynaklı
    /// (maximize/snap/çift-tık) hem de programatik (ilk kurulum dahil) her değişimi yakalar. Abonelik güçlü
    /// referans tutar — tek ve uygulama ömrü boyunca yaşayan ana pencere için kabul edilir.</para>
    /// </summary>
    public static void BindMaxButton(Window window, Path maxGlyph)
    {
        void Update() => maxGlyph.Data = MaxButtonGlyph(maxGlyph, window.WindowState);

        Update();
        DependencyPropertyDescriptor.FromProperty(Window.WindowStateProperty, typeof(Window))
            .AddValueChanged(window, (_, _) => Update());
    }
}
