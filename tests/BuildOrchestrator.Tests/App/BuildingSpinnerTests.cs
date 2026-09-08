using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// <b>Building spinner ayrı bir çizim DEĞİLDİR</b> — başlangıç modundaki KESİKLİ halkanın amber, dönen
/// hâlidir. Tasarımın üç kaynağı da bunu söyler: prototip kodu (<c>BuildApp.jsx:162-171</c> <c>BuildingSpin</c>
/// — <c>r=6.7</c> daire, <c>strokeDasharray "2.3 2.5"</c>, <c>strokeWidth 1.5</c>, opaklık 0.9, renk
/// <c>--amber-text</c>, <c>bo-rot</c> = 1.4 s lineer sonsuz), design README:69 ve ARCHITECTURE.md §14.4.
///
/// <para><b>[DEĞİŞEN KURAL] Eski iddia:</b> kontrol DS bundle'ının GENEL <c>Spinner</c>'ını çiziyordu —
/// 270°'lik bir YAY (<c>Icon.Spinner</c>), 900 ms'de dönen, <c>Brush.TextSecondary</c> renginde. Kaynağın
/// kendi yorumu bunu "SAPMA — hakemlik bekliyor" diye kaydetmişti: bundle'ın genel spinner'ı ile uygulamanın
/// KENDİ building spinner'ı farklı çizimlerdi ve "kod kazanır" kuralıyla bundle seçilmişti. <b>Neden
/// değişti:</b> uygulamanın çizdiği şey bundle'ın genel spinner'ı değil, prototip uygulamasının
/// <c>BuildingSpin</c>'idir; README ve ARCHITECTURE zaten kesikli halkayı tarif ediyordu — sapan taraf
/// koddu. Hakemliği kullanıcı verdi: tasarım kazanır.</para>
///
/// <para>Kesik deseni <b>kullanıcı birimindedir</b> (SVG kuralı), WPF'te ise <c>StrokeDashArray</c>
/// <c>StrokeThickness</c>'ın ÇARPANIDIR. Glyph'in halkası 1 kalınlıkta olduğu için sayılar orada birebir
/// geçer; spinner 1.5 kalınlıkta olduğu için AYNI desen ölçeklenerek türetilmelidir — ikinci bir sayı
/// tablosu yazmak (kopya YASAK, CLAUDE.md) desenin iki yerde ayrışmasına kapı açardı.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class BuildingSpinnerTests
{
    private static (BuildingSpinner spinner, Path ring, Border host, Window window) Realize()
    {
        var host = DsResources.NewHost();
        var spinner = new BuildingSpinner();
        var window = DsResources.Realize(host, spinner);
        var ring = DsResources.Descendants(spinner).OfType<Path>().Single();
        return (spinner, ring, host, window);
    }

    [StaFact]
    public void The_building_spinner_draws_the_start_modes_dashed_ring_not_a_separate_arc()
    {
        var (_, ring, host, window) = Realize();

        Assert.Same(host.FindResource("Icon.StatusRing"), ring.Data);
        Assert.Null(host.TryFindResource("Icon.Spinner")); // ayrı yay çizimi ARTIK YOK — sahipsiz kalmaz
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Its_dash_pattern_is_the_glyph_rings_pattern_expressed_in_the_same_user_units()
    {
        var (_, ring, host, window) = Realize();

        var design = (DoubleCollection)host.FindResource("Icon.StatusRing.DashArray");
        var userUnits = ring.StrokeDashArray.Select(d => d * ring.StrokeThickness).ToList();

        Assert.Equal(design.Count, userUnits.Count);
        for (int i = 0; i < design.Count; i++)
            Assert.Equal(design[i], userUnits[i], 6);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void It_is_amber_at_nine_tenths_opacity()
    {
        var (spinner, ring, host, window) = Realize();

        // BuildApp.jsx:165 `color: 'var(--amber-text)'` — spinner AKSİYON değil DURUM taşır ve building'in
        // rengi ambırdır; ribbon, sayaç chip'i ve satır glyph'i AYNI rengi gösterir.
        Assert.Equal(DsResources.TokenColor(host, "Brush.AmberText"), DsResources.ColorOf(spinner.Foreground));
        Assert.Equal(BuildingSpinner.DashedRingOpacity, ring.Opacity);
        GC.KeepAlive(window);
    }
}
