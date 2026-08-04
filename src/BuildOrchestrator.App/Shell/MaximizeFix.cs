using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BuildOrchestrator.App.Shell;

public static class MaximizeFix
{
    /// dotnet/wpf#3887: WindowChrome + maximize'da içerik resize-border kadar ekran dışına taşar. [A13 ZORUNLU]
    public static Thickness PaddingFor(WindowState state, double frameWidthPx, double frameHeightPx, double paddedBorderPx, double dpiScale)
    {
        if (state != WindowState.Maximized) return new Thickness(0);
        double x = (frameWidthPx + paddedBorderPx) / dpiScale;
        double y = (frameHeightPx + paddedBorderPx) / dpiScale;
        return new Thickness(x, y, x, y);
    }

    /// <summary>
    /// <see cref="PaddingFor"/>'u <paramref name="target"/>'a süren TEK kablaj — hesap ile uygulama arasındaki
    /// yegâne köprü (MainWindow ve testi aynı yolu kullanır; kopya YASAK, CLAUDE.md).
    ///
    /// <para><b>Neden <see cref="DependencyPropertyDescriptor"/>, <c>StateChanged</c>/<c>OnStateChanged</c>
    /// DEĞİL — ÖLÇÜLDÜ:</b> pencere XAML'de DOĞUŞTAN maximized açılır (<c>MainWindow.xaml</c>,
    /// <c>WindowState="Maximized"</c>), WPF ise HWND'den ÖNCE kurulmuş bir <see cref="Window.WindowState"/>
    /// için <c>StateChanged</c>'i HİÇ tetiklemez: probe'ta <c>Loaded</c>/<c>ContentRendered</c> ve sonrasında
    /// tetiklenme sayısı <b>0</b> ölçüldü. Düzeltme yalnız o override'dan uygulandığı sürece padding ilk
    /// açılışta HİÇ yazılmıyordu ve pencere work area'nın her kenarından frame kalınlığı kadar (ölçümde 9 px)
    /// dışarı taşıyordu — alt kenar görev çubuğunun altında kalıyordu. Kullanıcı küçültüp yeniden büyütünce
    /// ilk gerçek durum GEÇİŞİ oluyor, override ancak o an koşuyordu. DP izleyicisi hem OS kaynaklı hem
    /// programatik değişimi, <b>ilk kurulum dahil</b>, yakalar — <see cref="CaptionGlyphs.BindMaxButton"/> ile
    /// birebir aynı gerekçe ve aynı desen. Abonelik güçlü referans tutar; tek ve uygulama ömrü boyunca yaşayan
    /// ana pencere için kabul edilir.</para>
    /// </summary>
    /// <param name="dpiSource">
    /// ÜRETİMDE null → <see cref="VisualTreeHelper.GetDpi(Visual)"/> (davranış birebir aynı). Ölçek her
    /// yeniden hesapta TAZE okunur — bir kez yakalanmaz. Testler bu dikişten sahte bir ölçek verip DPI dalını
    /// ölçebilir (gerçek bir DPI değişimi HWND ve <c>WM_DPICHANGED</c> ister, headless süitte üretilemez).
    /// </param>
    public static void Bind(Window window, Border target, Func<DpiScale>? dpiSource = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(target);
        var dpi = dpiSource ?? (() => VisualTreeHelper.GetDpi(window));

        void Update()
        {
            var scale = dpi();
            uint ppi = (uint)scale.PixelsPerInchX;
            target.Padding = PaddingFor(window.WindowState,
                Dwm.GetSystemMetricsForDpi(Dwm.SM_CXSIZEFRAME, ppi),
                Dwm.GetSystemMetricsForDpi(Dwm.SM_CYSIZEFRAME, ppi),
                Dwm.GetSystemMetricsForDpi(Dwm.SM_CXPADDEDBORDER, ppi),
                scale.DpiScaleX);
        }

        Update(); // ilk kurulum: doğuştan maximized pencereyi yakalayan tek nokta
        DependencyPropertyDescriptor.FromProperty(Window.WindowStateProperty, typeof(Window))
            .AddValueChanged(window, (_, _) => Update());
        // Düzeltmenin DIP karşılığı ölçeğe göre GERÇEKTEN değişir (ölçüldü: 96 dpi'de 8 DIP, 192 dpi'de 6.5
        // DIP) ve DPI değişimi WindowState'e DOKUNMAZ — yukarıdaki izleyici bu dalı görmez. Maximize haliyle
        // farklı ölçekli monitöre taşınan pencere (Win+Shift+←/→) aksi halde eski ölçekte donardı.
        window.DpiChanged += (_, _) => Update();
    }
}
