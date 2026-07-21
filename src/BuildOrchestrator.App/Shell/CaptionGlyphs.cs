using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [T62/K8] Title bar buton glyph'leri. Tasarımda maximize butonu tek karedir ve maximize DURUMU tanımsız
/// bırakılmıştı; v7 kararı <b>K8</b>: <c>WindowState=Maximized</c> iken glyph "restore" (iç içe/kaydırılmış iki
/// kare) olur, normale dönünce tek kareye geri döner.
/// </summary>
public static class CaptionGlyphs
{
    /// <summary>□ (U+25A1) — tek kare; MainWindow.xaml'deki ilk içerikle aynı karakter.</summary>
    public const string Maximize = "□";
    /// <summary>❐ (U+2750) — iki (kaydırılmış/iç içe) kare; Windows'un klasik restore glyph'i. Segoe MDL2 ikon
    /// fontuna bağımlı OLMAMAK için Unicode karakter kullanılır (□ ve ✕ ile aynı yol/aynı font fallback'i).
    /// Min/close glyph'leri XAML'de kalır — K8 YALNIZ bu çifti sahiplenir.</summary>
    public const string Restore = "❐";

    public static string MaxButtonGlyph(WindowState state)
        => state == WindowState.Maximized ? Restore : Maximize;

    /// <summary>
    /// Pencerenin <see cref="Window.WindowState"/>'ini izleyip max/restore butonunun içeriğini süren TEK kablaj
    /// (MainWindow ve testi aynı yolu kullanır — kopya YASAK, CLAUDE.md).
    ///
    /// <para><b>Neden <c>DependencyPropertyDescriptor</c>, <c>StateChanged</c> DEĞİL:</b> <c>StateChanged</c>
    /// yalnız pencere gösterildikten sonra (WM_SIZE üzerinden) tetiklenir; DP izleyicisi hem OS kaynaklı
    /// (maximize/snap/çift-tık) hem de programatik (ilk kurulum dahil) her değişimi yakalar. Abonelik güçlü
    /// referans tutar — tek ve uygulama ömrü boyunca yaşayan ana pencere için kabul edilir.</para>
    /// </summary>
    public static void BindMaxButton(Window window, ContentControl maxButton)
    {
        void Update() => maxButton.Content = MaxButtonGlyph(window.WindowState);

        Update();
        DependencyPropertyDescriptor.FromProperty(Window.WindowStateProperty, typeof(Window))
            .AddValueChanged(window, (_, _) => Update());
    }
}
