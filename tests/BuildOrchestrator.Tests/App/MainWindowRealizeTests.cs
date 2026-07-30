using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T49 FINAL PASS · It-4b dersi (c6e9a21)] <c>MainWindow.xaml</c> GERÇEKTEN realize olabilmeli.
///
/// <para><b>Neden bu testin varlığı bu fazın en kritik kalemi:</b> headless suite XAML'in RUNTIME kaynak
/// çözümlemesini GÖRMEZ. <c>ShellRoot</c>'ta bir <b>Double</b> token (<c>Size.ActionBarHeight</c>) bir
/// <c>GridLength</c>'e verilmişti; 1198 test yeşilken uygulama HİÇ AÇILMIYORDU (XamlParseException). O sınıfın
/// (Double token → GridLength/Thickness, tanımsız <c>DynamicResource</c> anahtarı, <c>FindResource</c> cast'i)
/// testsiz kalan SON kökü MainWindow'du — en yoğun token tüketicisi ve tek <c>(double)FindResource(...)</c>
/// cast'inin sahibi (<c>MainWindow.xaml.cs</c>, title-bar yüksekliği).</para>
///
/// <para><b>Realize neden <c>Show()</c> İLE DEĞİL:</b> <c>MainWindow.OnSourceInitialized</c> gerçek bir tepsi
/// ikonu kurar, global kısayol kaydeder ve Snap Layouts hook'u takar; <c>Loaded</c> ise supervisor'ı başlatır.
/// Bunlar bir testin yan etkisi OLAMAZ (kapatma yolu da K5 gereği kalıcı kullanıcı durumunu yazar). Bu yüzden
/// pencere ŞABLONU uygulanıp ölçülür/yerleştirilir ve pencere seviyesindeki <c>DynamicResource</c>'lar
/// AÇIKÇA okunarak çözülmeye zorlanır — tip uyuşmazlığı tam da o anda patlar.</para>
/// </summary>
[Collection("Console UI (serial)")]
public class MainWindowRealizeTests
{
    /// <summary>[A13/T2] Kurulum + realize ARTIK <see cref="MainWindowHost"/>'ta (kopya YASAK — T2 üçüncü bir
    /// kopyasını yazacaktı). Store da oraya taşındığı için bu sınıf da temp'e yazar: bu testler bugün kalıcı
    /// duruma dokunmuyor, ama "MainWindow kuran HER test store enjekte eder" kuralının istisnası olmaz.</summary>
    private static MainWindow NewMainWindow(TempDir temp) => MainWindowHost.New(temp).window;

    private static FrameworkElement Realize(MainWindow window) => MainWindowHost.Realize(window);

    [StaFact]
    public void The_main_window_realizes_with_the_production_merge_chain_and_no_token_type_mismatch()
    {
        using var temp = new TempDir();
        var window = NewMainWindow(temp);

        // Pencere seviyesindeki DynamicResource'lar: değer ANCAK okununca çözülür — bir Double token'ı
        // Brush'a (ya da tersi) veren bir sapma tam burada InvalidCastException/XamlParseException verir.
        _ = window.Background;
        _ = window.Foreground;
        _ = window.MinWidth;
        _ = window.MinHeight;

        // Şablon + yerleşim + render: title bar / caption butonları / layout seçici / gövde (ShellRoot) ve
        // içindeki TÜM DynamicResource bağları burada çözülür (Arrange, bağlı olmayan öğede de OnRender'ı sürer
        // → Background/Fill gibi render-only özellikler GERÇEKTEN okunur).
        Realize(window);

        // [fix round 1 · A1 · fix round 2] İkinci ağ: ağaçta VAR OLUP render'ın okumadığı token bağları
        // (çizilmeyen bir özellik, ölçülmeyen bir Grid tanımı) için hedef DP tipine uyum AÇIKÇA denetlenir —
        // WPF okuma yolunda tip doğrulaması yapmaz. SINIR: Collapsed bir dalın ŞABLONLA doğacak içeriği hiç
        // kurulmaz, dolayısıyla ne render ne de bu denetim onu görür — orası kapsam DIŞIDIR.
        Assert.Empty(DsResources.DynamicResourceTypeMismatches(window));

        Assert.IsType<SolidColorBrush>(window.Background);
        Assert.IsType<SolidColorBrush>(window.Foreground);
        Assert.True(window.MinWidth > 0 && window.MinHeight > 0);
        GC.KeepAlive(window);
    }

    /// <summary>[A13/T3c · c2a] design-v1 BuildApp.jsx:1429 kök konteyner <c>background: var(--surface-base)</c>
    /// — pencerenin zemini <b>DOĞRU token'a</b> bağlı olmalı, herhangi bir <see cref="SolidColorBrush"/> değil.
    /// İki ayrı iddia gerekir (brief c2 notu): (a) renk otoriteyle birebir, (b) bağ CANLI — uygulanan fırça
    /// Tokens.xaml sözlüğündeki nesnenin TA KENDİSİ (<see cref="Assert.Same"/>); yalnız renk eşitliği aynı rengi
    /// taşıyan BAŞKA bir fırçayı (kopya bir <c>SolidColorBrush</c>) ayırt edemezdi.</summary>
    [StaFact]
    public void The_window_background_is_live_bound_to_surface_base()
    {
        using var temp = new TempDir();
        var window = NewMainWindow(temp);
        Realize(window);

        var expected = Assert.IsType<SolidColorBrush>(window.FindResource("Brush.SurfaceBase"));
        Assert.Same(expected, window.Background);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#0e0e10")!, expected.Color); // colors.css --surface-base
        GC.KeepAlive(window);
    }

    /// <summary>[A13/T3c · c2b — ÜRETİM BULGUSU, bkz. task-T3c-report.md ## Concerns] Otorite (design-v1
    /// <c>components/shell/TitleBar.jsx</c>, BuildApp.jsx içinde STİLSİZ kullanılır) title bar zeminini
    /// <c>var(--surface-base)</c> (#0e0e10) olarak kurar. Üretim (<c>MainWindow.xaml:TitleBarRow</c>)
    /// <c>Brush.Surface</c>'a (#141417) bağlıdır — SAPMA. Düzeltmek bu task'ın kapsamı DIŞINDA (brief kural 3);
    /// bu test o yüzden BİLEREK <c>Skip</c>'lidir: otoriteye göre yazılmış assertion'ın gerçek üretime karşı
    /// KIRMIZI çıktığı, task sırasında bir kez çalıştırılıp raporda kanıtlandı (## Koşum çıktıları).</summary>
    [StaFact(Skip = "A13/T3c c2b: bilinen üretim≠otorite sapması (Brush.Surface #141417 vs otorite Brush.SurfaceBase #0e0e10) — düzeltme kapsam dışı, bkz. rapor Concerns. Kırmızı kanıt (skip'siz çalıştırıldığında): Assert.Same() Failure — Expected #FF0E0E10, Actual #FF141417.")]
    public void The_title_bar_background_is_live_bound_to_surface_base_per_authority()
    {
        using var temp = new TempDir();
        var window = NewMainWindow(temp);
        Realize(window);

        var expected = Assert.IsType<SolidColorBrush>(window.FindResource("Brush.SurfaceBase"));
        Assert.Same(expected, window.TitleBarRow.Background);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#0e0e10")!, ((SolidColorBrush)window.TitleBarRow.Background).Color);
    }

    [StaFact]
    public void The_title_bar_height_cast_really_runs_and_feeds_both_the_row_and_the_window_chrome()
    {
        using var temp = new TempDir();
        var window = NewMainWindow(temp);

        // `(double)FindResource("Size.TitleBarHeight")` ctor'da KOŞTU: token bir Double DEĞİLSE (ör. GridLength'e
        // ya da Thickness'a çevrilse) ctor InvalidCastException ile patlardı — buraya hiç gelinmezdi.
        double token = (double)window.FindResource("Size.TitleBarHeight");
        Assert.True(token > 0);

        // Tek kaynak iddiası: title-bar SATIRI ve WindowChrome'un CaptionHeight'ı AYNI token'dan türer.
        Assert.Equal(new GridLength(token), window.TitleRow.Height);
        Assert.Equal(token, System.Windows.Shell.WindowChrome.GetWindowChrome(window).CaptionHeight);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Every_dynamic_resource_key_used_by_the_title_bar_resolves_to_a_value_of_the_expected_type()
    {
        using var temp = new TempDir();
        var window = NewMainWindow(temp);
        Realize(window);

        // Caption butonları + layout seçici + gear: stil (DynamicResource Ds.IconButton*) ve ikon geometrileri
        // (DynamicResource Icon.* / Icon.*.StrokeThickness) GERÇEKTEN çözülmüş olmalı — çözülmemiş bir anahtar
        // sessizce null bırakır ve üretimde görünmez bir title bar üretirdi.
        Assert.NotNull(window.LayQuadButton.Style);
        Assert.NotNull(window.LayListButton.Style);
        Assert.NotNull(window.LayFocusButton.Style);
        Assert.NotNull(window.GearButton.Style);

        var glyphs = DsResources.Descendants(window.RootShell).OfType<System.Windows.Shapes.Path>().ToList();
        Assert.NotEmpty(glyphs);
        Assert.All(glyphs, p => Assert.True(p.Data is not null || p.Stroke is not null,
            "title bar Path'i ne geometri ne kontur çözebildi — DynamicResource anahtarı kayıp olabilir"));
        GC.KeepAlive(window);
    }
}
