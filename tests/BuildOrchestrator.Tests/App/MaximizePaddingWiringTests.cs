using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [dotnet/wpf#3887 · kablaj] <see cref="BuildOrchestrator.App.Shell.MaximizeFix"/>'in HESABI
/// (<c>PaddingFor</c>) <see cref="MaximizeFixTests"/>'te pinlidir; burada pinlenen onun <b>UYGULANMASI</b>dır.
///
/// <para><b>Ölçülen kusur:</b> pencere XAML'de DOĞUŞTAN maximized açılır (<c>MainWindow.xaml</c>,
/// <c>WindowState="Maximized"</c>) ama düzeltme yalnız <c>OnStateChanged</c> override'ından uygulanıyordu.
/// WPF, HWND'den ÖNCE kurulmuş bir <see cref="Window.WindowState"/> için <c>StateChanged</c>'i HİÇ
/// tetiklemez — probe ile ölçüldü: <c>Loaded</c>/<c>ContentRendered</c> ve sonrasında StateChanged sayısı
/// <b>0</b>, pencere dikdörtgeni ise work area'nın her kenarından <b>9 px</b> (o makinede
/// <c>CXSIZEFRAME 4 + CXPADDEDBORDER 5 @120dpi</c>) dışarı taşıyordu; alt kenar görev çubuğunun altında
/// kalıyordu. Kullanıcının "küçültüp tekrar büyütünce oturuyor" gözlemi bunun eşiydi: ilk gerçek durum
/// GEÇİŞİ orada oluyordu (StateChanged sayısı 0 → 2) ve padding ancak o an yazılıyordu.</para>
///
/// <para>Aynı tuzağın adı bu pencerede zaten konmuştu — <see cref="BuildOrchestrator.App.Shell.CaptionGlyphs"/>
/// glyph'i bilerek <c>DependencyPropertyDescriptor</c> ile izler ("StateChanged yalnız pencere
/// gösterildikten sonra tetiklenir; DP izleyicisi ilk kurulum dahil her değişimi yakalar"). Padding artık
/// aynı desendedir.</para>
///
/// <para>Pencere <c>Show()</c> EDİLMEZ (bkz. <see cref="MainWindowRealizeTests"/> sınıf özeti). Kusur tam da
/// HWND'den ÖNCEKİ durumda yaşadığı için bu bir kısıt değil, testin ta kendisidir: DP izleyicisi HWND'siz
/// çalışır, <c>OnSourceInitialized</c>'a dayanan bir çözüm ise bu süitte KIRMIZI GÖSTERİLEMEZDİ.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class MaximizePaddingWiringTests
{
    /// <summary>
    /// Kusurun kendisi: doğuştan maximized pencerede düzeltme ctor'dan çıkarken UYGULANMIŞ olmalı.
    ///
    /// <para><b>Beklenen değer burada YENİDEN HESAPLANMAZ</b> (formül kopyası YASAK — hesabın doğruluğu
    /// <see cref="MaximizeFixTests"/>'in işidir): pinlenen, padding'in sıfır OLMAMASI ve dört kenarın eşit
    /// olmasıdır. Böylece test koştuğu makinenin DPI'sinden bağımsız kalır (96 dpi'de 8 DIP, 120 dpi'de
    /// 7.2 DIP — ikisi de geçerli).</para>
    /// </summary>
    [StaFact]
    public void The_padding_is_applied_to_a_window_that_is_born_maximized()
    {
        using var temp = new TempDir();
        var window = MainWindowHost.New(temp).window;

        Assert.Equal(WindowState.Maximized, window.WindowState); // ön koşul: kusurun yaşadığı durum
        var padding = window.RootShell.Padding;

        Assert.NotEqual(new Thickness(0), padding);
        Assert.Equal(padding.Left, padding.Right);
        Assert.Equal(padding.Left, padding.Top);
        Assert.Equal(padding.Left, padding.Bottom);
    }

    /// <summary>
    /// [regresyon] Eski <c>OnStateChanged</c> override'ının kapsadığı davranış — durum geçişlerinde düzeltme
    /// açılıp kapanır — yeni kablajda AYNEN korunur. Override kaldırıldığı için (aynı hesabın iki uygulayıcısı
    /// olamaz, kopya YASAK) bu dal artık YALNIZ burada pinlidir.
    /// </summary>
    [StaFact]
    public void Restoring_clears_the_padding_and_maximizing_puts_it_back()
    {
        using var temp = new TempDir();
        var window = MainWindowHost.New(temp).window;
        var maximized = window.RootShell.Padding;

        window.WindowState = WindowState.Normal;
        Assert.Equal(new Thickness(0), window.RootShell.Padding); // normalde düzeltme YOK — içerik taşmıyor

        window.WindowState = WindowState.Maximized;
        Assert.Equal(maximized, window.RootShell.Padding);
    }

    /// <summary>
    /// DPI değişimi padding'i YENİDEN hesaplatır. <see cref="Window.WindowState"/> değişmediği için DP
    /// izleyicisi bu dalı görmez; kablajın DPI olayına da bağlı olması gerekir.
    ///
    /// <para><b>Neden önemli — ölçüldü:</b> düzeltmenin DIP karşılığı DPI'ye göre GERÇEKTEN değişir
    /// (<c>CXSIZEFRAME+CXPADDEDBORDER</c>: 96 dpi'de 8 px → 8 DIP, 192 dpi'de 13 px → 6.5 DIP). Yani maximize
    /// haliyle farklı ölçekli bir monitöre taşınan pencerede (Win+Shift+←/→, durum DEĞİŞMEZ) padding eski
    /// ölçekte donar; aynı şekilde sistem ölçeğinden farklı ölçekli bir monitörde açılan pencerenin ilk
    /// değeri de ancak DPI olayıyla düzelir.</para>
    ///
    /// <para><b>Beklenen değer testte YENİDEN HESAPLANMAZ:</b> aynı üretim yolundan (yeni ölçeğe doğrudan
    /// bağlanmış bir referans hedef) türetilir. Böylece formül tek yerde kalır ve test yalnız "taze ölçekle
    /// yeniden hesaplandı mı"yı ölçer.</para>
    ///
    /// <para>Gerçek bir DPI değişimi HWND + <c>WM_DPICHANGED</c> ister ve bu süitte üretilemez; olay elle
    /// yükseltilir. <see cref="DpiChangedEventArgs"/>'ın kurucusu <c>internal</c> olduğundan yansıma
    /// ZORUNLUDUR — bir <c>RoutedEventArgs</c> ile taklit etmek YANLIŞ olurdu: o tip
    /// <c>InvokeEventHandler</c>'ı <see cref="DpiChangedEventHandler"/>'a sert cast ile ezer, yani üretimde
    /// yükselen gerçek olayla uyuşmayan bir yol test edilmiş olurdu.</para>
    /// </summary>
    [StaFact]
    public void A_dpi_change_recomputes_the_padding_from_the_new_scale()
    {
        var window = new Window { WindowState = WindowState.Maximized };
        var target = new Border();
        var scale = new DpiScale(1.0, 1.0);
        MaximizeFix.Bind(window, target, () => scale);
        var atOldScale = target.Padding;

        scale = new DpiScale(2.0, 2.0);
        RaiseDpiChanged(window, new DpiScale(1.0, 1.0), scale);

        var reference = new Border();
        MaximizeFix.Bind(new Window { WindowState = WindowState.Maximized }, reference, () => new DpiScale(2.0, 2.0));
        Assert.NotEqual(atOldScale, reference.Padding); // ön koşul: iki ölçek gerçekten farklı padding üretir
        Assert.Equal(reference.Padding, target.Padding);
    }

    private static void RaiseDpiChanged(Window window, DpiScale oldDpi, DpiScale newDpi)
    {
        var args = (RoutedEventArgs)Activator.CreateInstance(typeof(DpiChangedEventArgs),
            BindingFlags.Instance | BindingFlags.NonPublic, binder: null,
            args: [oldDpi, newDpi, Window.DpiChangedEvent, window], culture: null)!;
        window.RaiseEvent(args);
    }
}
