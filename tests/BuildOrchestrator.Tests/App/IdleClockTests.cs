using System.Linq;
using System.Windows;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Boşta hiçbir SONSUZ animasyon saati dönmemelidir.
///
/// <para><b>Neden bu dosya var:</b> uygulama tamamen boştayken (koşu yok, build bitmiş) bir CPU çekirdeğinin
/// %133'ünü yakarken ölçüldü; thread başına dökümde tek bir thread %92'deydi. WPF'in zamanlayıcısı, ETKİN tek
/// bir saat kaldığı sürece boş kareye HİÇ inmez — yani unutulmuş bir <c>RepeatBehavior.Forever</c> yalnız
/// kendi maliyetini değil, tüm render döngüsünü ayakta tutar.</para>
///
/// <para><b>[DEĞİŞEN KURAL] Eski iddia:</b> testler <see cref="StatusGlyph"/>'in kendi NABZINI
/// (<c>IsPulsing</c>) sürüyordu — bulunan kusur, nabzın yalnız <c>Status</c>'a bakıp GÖRÜNÜRLÜĞE
/// bakmamasıydı. <b>Neden değişti:</b> o nabız kaldırıldı (tasarımda building glyph'inin tek animasyonu
/// dönüştür — bkz. <c>BuildingSpinnerTests</c>), dolayısıyla glyph'in içindeki tek sonsuz saatin sahibi artık
/// <see cref="BuildingSpinner"/>'dır. Korunan ÜRETİM ÖZELLİĞİ aynıdır ve senaryo da aynı: şeridin faz glyph'i
/// bir Resolve koşusunda <c>Building</c>'e alınır, koşu bitince <c>Collapsed</c> edilir ama <c>Status</c> hiç
/// sıfırlanmaz — görünmeyen bir kontrolün üzerinde saat dönmeye devam etmemelidir.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class IdleClockTests
{
    /// <summary>
    /// Dönen halkayı gerçekten döndürerek bir building glyph'i kurar. Sinyal statik seam üzerinden verilir:
    /// glyph artık motion sahibi DEĞİLDİR ve enjekte edecek kendi kapısı yoktur — saatin sahibi şablonun
    /// içindeki spinner'dır ve o <c>App.Motion</c>'ı okur.
    /// </summary>
    private static (BuildingSpinner Spinner, Window Window) RealizeSpinningGlyph(out StatusGlyph glyph)
    {
        glyph = new StatusGlyph { Status = GraphStatus.Building };
        var window = DsResources.Realize(DsResources.NewHost(), glyph);
        var spinner = DsResources.Descendants(glyph).OfType<BuildingSpinner>().Single();
        return (spinner, window);
    }

    [StaFact]
    public void A_hidden_building_glyph_stops_its_rings_clock()
    {
        using var _ = MotionScope.Enable(new FakeMotionSettings { AnimationsEnabled = true });
        var (spinner, window) = RealizeSpinningGlyph(out var glyph);
        Assert.True(spinner.IsRotating, "ön-koşul: görünür building glyph'inin halkası GERÇEKTEN dönmeli");

        glyph.Visibility = Visibility.Collapsed;
        window.UpdateLayout();

        Assert.False(spinner.IsRotating);
        GC.KeepAlive(window);
    }

    /// <summary>Simetrik yön: yeniden görünür olunca saat geri gelir (tek yönlü bir kapı burada kırılır).</summary>
    [StaFact]
    public void A_building_glyph_that_becomes_visible_again_resumes_its_rings_clock()
    {
        using var _ = MotionScope.Enable(new FakeMotionSettings { AnimationsEnabled = true });
        var (spinner, window) = RealizeSpinningGlyph(out var glyph);

        glyph.Visibility = Visibility.Collapsed;
        window.UpdateLayout();
        Assert.False(spinner.IsRotating); // non-vacuous

        glyph.Visibility = Visibility.Visible;
        window.UpdateLayout();

        Assert.True(spinner.IsRotating);
        GC.KeepAlive(window);
    }
}
