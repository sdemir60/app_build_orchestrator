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
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    /// <summary>Motor asla doğmasın: var olmayan bir supervisor yolu ile <c>StartAsync</c> zaten
    /// <c>EngineUnavailableException</c> verir — ama bu test pencereyi hiç <c>Show()</c> etmediğinden
    /// <c>Loaded</c> da tetiklenmez; yol iki kere kapalıdır.</summary>
    private static MainWindow NewMainWindow()
    {
        var engine = new EngineHost(Path.Combine(AppContext.BaseDirectory, "no-such-supervisor.exe"));
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        return new MainWindow(engine, vm, NeverTickingBatcher(), DsResources.NewScope());
    }

    /// <summary>
    /// [fix round 1 · A1] Pencerenin İÇERİĞİNİ realize eder — <b>ölçüldü:</b> <c>Window.Measure/Arrange</c>
    /// gerçek bir <c>PresentationSource</c> (HWND) olmadan içeriğe HİÇ İNMEZ; caption butonlarının şablonları
    /// bile genişlemez (<c>MinButton.ApplyTemplate()</c> sonradan hâlâ <c>true</c> döner). İçerik kökü doğrudan
    /// ölçülüp yerleştirildiğinde ise şablonlar genişler ve <c>OnRender</c> koşar — yani <c>Background</c> gibi
    /// RENDER-ONLY özellikler de gerçekten okunur ve yanlış tipli token orada patlar.
    /// </summary>
    private static FrameworkElement Realize(MainWindow window)
    {
        window.ApplyTemplate();
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1400, 800));
        content.Arrange(new Rect(0, 0, 1400, 800));
        content.UpdateLayout();
        return content;
    }

    [StaFact]
    public void The_main_window_realizes_with_the_production_merge_chain_and_no_token_type_mismatch()
    {
        var window = NewMainWindow();

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

        // [fix round 1 · A1] İkinci ağ: render'ın okumadığı (Collapsed dal, çizilmeyen özellik) token bağları
        // için hedef DP tipine uyum AÇIKÇA denetlenir — WPF okuma yolunda tip doğrulaması yapmaz.
        Assert.Empty(DsResources.DynamicResourceTypeMismatches(window));

        Assert.IsType<SolidColorBrush>(window.Background);
        Assert.IsType<SolidColorBrush>(window.Foreground);
        Assert.True(window.MinWidth > 0 && window.MinHeight > 0);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_title_bar_height_cast_really_runs_and_feeds_both_the_row_and_the_window_chrome()
    {
        var window = NewMainWindow();

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
        var window = NewMainWindow();
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
