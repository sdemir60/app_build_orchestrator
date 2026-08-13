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
/// ikonu kurar ve global kısayol kaydeder; <c>Loaded</c> ise supervisor'ı başlatır.
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

    /// <summary>[A13/T3c · c2b → T3 fix-1 · P1 KAPATILDI] Otorite (design-v1 DS <c>components/shell/TitleBar.jsx</c>,
    /// <c>_ds_bundle.js:1189</c>) title bar zeminini <c>background: 'var(--surface-base)'</c> (#0e0e10) olarak kurar.
    /// Üretim <c>Brush.Surface</c>'a (#141417) bağlıydı — SAPMA; kullanıcı kararıyla (2026-07-31) düzeltildi ve bu
    /// test <c>Skip</c>'ten çıkarıldı. Kırmızı kanıtı: <c>Assert.Same() Failure — Expected #FF0E0E10, Actual
    /// #FF141417</c> (düzeltmeden ÖNCE koşuldu, bkz. task-T3-fix1-report.md).</summary>
    [StaFact]
    public void The_title_bar_background_is_live_bound_to_surface_base_per_authority()
    {
        using var temp = new TempDir();
        var window = NewMainWindow(temp);
        Realize(window);

        var expected = Assert.IsType<SolidColorBrush>(window.FindResource("Brush.SurfaceBase"));
        Assert.Same(expected, window.TitleBarRow.Background);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#0e0e10")!, ((SolidColorBrush)window.TitleBarRow.Background).Color);
        GC.KeepAlive(window); // [fix-1 · lens1 D7] kardeşlerindeki KeepAlive eksikti — skip kalkınca GC riski gerçek olurdu
    }

    /// <summary>[A13/T3 fix-1 · P2] Otoritenin AYNI öğesi (design-v1 DS <c>TitleBar</c>,
    /// <c>_ds_bundle.js:1184-1196</c>) zeminle BİRLİKTE bir alt çizgi de kurar:
    /// <c>borderBottom: '1px solid var(--border-subtle)'</c>. Üretimde title bar ile gövde arasında HİÇBİR ayırıcı
    /// yoktu (<c>MainWindow.xaml</c> genelinde <c>BorderBrush</c>/<c>BorderThickness</c> sıfır eşleşme) — lens1 ve
    /// lens2 bağımsız ölçtü, kullanıcı kararıyla (2026-07-31) kapatıldı.
    ///
    /// <para>Otorite zemini ve alt çizgiyi TEK stil bloğunda aynı öğeye verdiği için üretimde de tek öğe taşır:
    /// <c>TitleBarRow</c> artık bir <see cref="Border"/>'dır (tek çocuğu eski <c>DockPanel</c>). Renk eşitliği tek
    /// başına yetmez — fırçanın Tokens.xaml'deki nesnenin TA KENDİSİ olduğu (<see cref="Assert.Same"/>) ve
    /// kalınlığın <b>yalnız altta</b> 1px olduğu ayrı ayrı pinlenir.</para></summary>
    [StaFact]
    public void The_title_bar_carries_the_authoritys_one_pixel_border_subtle_hairline_along_its_bottom_edge()
    {
        using var temp = new TempDir();
        var window = NewMainWindow(temp);
        Realize(window);

        // Otoritede zemin ve alt çizgi AYNI stil bloğundadır → üretimde de aynı öğe taşımalı (Grid taşıyamaz).
        var titleBar = Assert.IsType<Border>((object)window.TitleBarRow);
        Assert.Same(window.FindResource("Brush.BorderSubtle"), titleBar.BorderBrush);
        Assert.Equal(new Thickness(0, 0, 0, 1), titleBar.BorderThickness);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [A13/T3 fix-2 · 3] <b>P2'nin ölçülen bedeli — artık sessiz değil.</b> Eklenen 1px hairline dıştan telafi
    /// EDİLMEDİ: bant 40'ta kaldı, dolayısıyla iç kutu hairline kadar daraldı. Bu bilinçli bir karardır ve
    /// ölçülebilir ÜÇ sonucuyla buraya pinlenmiştir: bant BÜYÜMEZ (41 olmaz), iç kutu bir kenarlıktan fazla
    /// daralmaz, ve içerik <c>inner</c>'ın merkezinden yuvarlama bütçesini (bugün bir cihaz pikseli)
    /// aşmayacak biçimde dikey ortalı kalır. Bu üçü sırasıyla aşağıdaki üç assert öbeğidir.
    ///
    /// <para><b>Otorite ve karar:</b> design-v1 README §2 bant şeması (<c>:84</c>) <c>TITLE BAR (40px)</c> ve
    /// §2.1 başlığı (<c>:105</c>) <c>## 2.1 Title bar (40px)</c> — görünen bant 40px'tir. DS
    /// <c>TitleBar</c> (<c>_ds_bundle.js:1188-1190</c>) CSS'te <c>height: 'var(--titlebar-height)'</c> +
    /// <c>borderBottom: '1px …'</c> yazar; CSS varsayılanı <c>content-box</c> olduğu için orada TOPLAM 41'dir.
    /// İki okuma çatışır ve <b>40 bandı seçilmiştir</b>: <c>Size.TitleBarHeight</c> aynı anda
    /// <c>WindowChrome.CaptionHeight</c>'ı besler (bkz.
    /// <see cref="The_title_bar_height_cast_really_runs_and_feeds_both_the_row_and_the_window_chrome"/>), yani
    /// bandı 41'e çıkarmak caption/hit-test sözleşmesini bozardı. Seçimin doğrudan aritmetik sonucu: hairline
    /// bandı BÜYÜTMEZ, bandın son piksel satırını YER.</para>
    ///
    /// <para><b>DPI: hiçbir iddia dip cinsinden sabit OLAMAZ</b> [A13/B0'da yeniden ölçüldü].
    /// <c>UseLayoutRounding</c> (<c>MainWindow.xaml:12</c>) yalnız boyları değil her hizalama offset'ini de
    /// cihaz pikseli ızgarasına oturtur; dolayısıyla dip değerleri ekran ölçeğiyle kayar — hairline 1 dip
    /// yazıldığı hâlde 150%'de 2 piksel (1,333 dip) yer kaplar ve iç kutuyu <c>38,667</c>'ye, 125%'te 1 piksel
    /// (0,8 dip) kaplar ve iç kutuyu <c>39,2</c>'ye indirir. Bu yüzden iddialar farkın KENDİSİ üzerinden ve
    /// <b>cihaz pikseli cinsinden</b> kurulur (bkz. <see cref="InDevicePixels"/>).</para>
    ///
    /// <para><b>Merkez iddiası neden ±0 olamaz — B0'ın kırmızısı:</b> iddia
    /// <c>Assert.Equal(inner.ActualHeight / 2, logoCenter, precision: 1)</c> idi, yani tolerans ±0,05 dip:
    /// 125%'te bir cihaz pikselinin 1/16'sı. 150%'de şansla yeşildi, 125%'te ÖLÇÜLDÜ ve kırıldı — logo merkezi
    /// <c>20,4</c>, iç kutu merkezi <c>19,6</c>. Fark TAM bir cihaz pikselidir ve tamamı yuvarlamadan gelir:
    /// logo ile iç kutu arasında İKİ hizalama adımı vardır (Viewbox → StackPanel → DockPanel) ve ikisinde de
    /// ideal offset yarım piksele denk düşüp yukarı yuvarlanır — <c>(39,2−16)/2 = 11,6 → 12</c> ve
    /// <c>(16−15,2)/2 = 0,4 → 0,8</c>. Model doğrulanır: aynı bantta TEK adım uzaktaki gear butonu yalnız
    /// 0,4 dip (yarım piksel) sapar. Yani <c>inner</c> merkezine göre ölçülen sapmanın tamamı yuvarlamayla
    /// açıklanır, artık bırakan yapısal bir kayma YOKTUR; kırılgan olan iddiaydı.</para>
    ///
    /// <para><b>SINIR (bilerek):</b> "iç kutuda ortalı" ile "40 bandında ortalı" arasındaki fark yarım
    /// hairline'dır (125%'te 0,4 dip) ve yuvarlama zemininin (bir cihaz pikseli) ALTINDA kalır — bu ikisini
    /// hiçbir DPI-bağımsız assert ayırt edemez. Buradaki iddia ortalamanın KENDİSİNİ pinler (içerik üst/alt
    /// hizaya kaçarsa kırmızı); hangi kutunun ortası olduğu YAPISAL olarak okunur: ölçü <c>inner</c>
    /// koordinatında alınır.</para></summary>
    [StaFact]
    public void The_hairline_eats_the_bands_last_pixel_row_instead_of_growing_the_forty_pixel_title_bar()
    {
        using var temp = new TempDir();
        var window = NewMainWindow(temp);
        Realize(window);

        double band = (double)window.FindResource("Size.TitleBarHeight");
        Assert.Equal(40.0, band); // otorite bandı (README §2 :84 / §2.1 :105)

        var titleBar = (Border)window.TitleBarRow;
        Assert.Equal(band, titleBar.ActualHeight);              // DIŞ bant BÜYÜMEDİ (41 olmadı)
        Assert.Equal(band, System.Windows.Shell.WindowChrome.GetWindowChrome(window).CaptionHeight);

        var dpi = VisualTreeHelper.GetDpi(window);

        // İÇ kutu hairline kadar daraldı — hairline bandın İÇİNDEN yer alır. Üst sınır DPI'dan türer: 1 dip'lik
        // bir çizgi ızgarada en çok Ceiling(ölçek) piksel kaplayabilir (125%'te 2, 100%'de 1).
        var inner = (FrameworkElement)titleBar.Child;
        Assert.True(inner.ActualHeight < titleBar.ActualHeight,
            $"hairline iç kutuyu hiç daraltmadı (iç {inner.ActualHeight}, bant {titleBar.ActualHeight}) — çizgi kayıp olabilir");
        double shrink = InDevicePixels(titleBar.ActualHeight - inner.ActualHeight, dpi);
        Assert.True(shrink <= Math.Ceiling(dpi.DpiScaleY),
            $"iç kutu bir kenarlıktan FAZLA daraldı: {shrink} cihaz pikseli (bir kenarlık en çok {Math.Ceiling(dpi.DpiScaleY)})");

        // Görünür sonuç: içerik KALAN kutuda dikey ortalı kalır. Tolerans yuvarlama bütçesidir: her hizalama
        // adımı merkezi en çok YARIM cihaz pikseli kaydırır (bugün 2 adım → TAM bir cihaz pikseli).
        var logo = window.TitleBarLogo;
        double logoCenter = logo.TranslatePoint(new Point(0, logo.ActualHeight / 2), inner).Y;
        double drift = InDevicePixels(logoCenter - inner.ActualHeight / 2, dpi);
        double budget = AlignmentSteps(logo, inner) / 2.0;
        Assert.True(drift <= budget,
            $"logo iç kutuda ortalı DEĞİL: {drift} cihaz pikseli sapma (yuvarlama bütçesi {budget} piksel)");
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [A13/B0] Bir dip farkını CİHAZ PİKSELİNE çevirir ve yarım-piksel ızgarasına oturtur.
    ///
    /// <para><c>UseLayoutRounding</c> altında her boy ve her hizalama offset'i tam piksele oturduğundan, iki
    /// geometri değerinin farkı TAM ARİTMETİKTE yarım cihaz pikselinin katıdır; ölçülende görülen artık
    /// (<c>20,4 − 19,6 = 0,7999999999999972</c>) yalnız double gösteriminden gelir. Sınır iddiaları — fark tam
    /// bütçe kadar olduğunda — bu artık yüzünden yazı-turaya döner, bu yüzden ızgaraya oturtmak ZORUNLUDUR.
    /// Gevşetme değildir: ara değerler zaten oluşamaz.</para>
    /// </summary>
    private static double InDevicePixels(double dips, DpiScale dpi) =>
        Math.Round(Math.Abs(dips) * dpi.DpiScaleY * 2) / 2;

    /// <summary>
    /// [A13/B0] <paramref name="child"/> ile <paramref name="ancestor"/> arasındaki hizalama adımı sayısı —
    /// yuvarlama bütçesinin çarpanı (adım × yarım cihaz pikseli). Elle YAZILMAZ, ağaçtan sayılır: araya bir
    /// sarmalayıcı girerse bütçe de gerçek fizikle birlikte büyür, iddia sessizce kırılmaz. Title bar logosu
    /// için bugün 2 döner (Viewbox → StackPanel → DockPanel) ⇒ bütçe TAM bir cihaz pikselidir.
    /// </summary>
    private static int AlignmentSteps(FrameworkElement child, FrameworkElement ancestor)
    {
        int steps = 0;
        for (DependencyObject? node = child; node is not null && !ReferenceEquals(node, ancestor);
             node = VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement) steps++;
        }
        return steps;
    }

    // [KALDIRILDI — design v1.7.0 §2.5] Konsolda duvar saati yok; idle prompt satırı yalnız imleç +
    // "ready" taşıdığı için bağlanacak bir saat kablosu da kalmadı.

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
