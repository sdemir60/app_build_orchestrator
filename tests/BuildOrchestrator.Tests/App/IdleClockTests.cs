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
/// <para>Bulunan kusur: <see cref="StatusGlyph"/>'in nabzı yalnız <c>Status</c>'a bakıyordu, GÖRÜNÜRLÜĞE
/// bakmıyordu. Kardeşi <see cref="BuildingSpinner"/> bunu baştan doğru yapıyor
/// (<c>IsVisible &amp;&amp; motion</c>) — iki kontrol aynı soruyu farklı soruyordu.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class IdleClockTests
{
    /// <summary>
    /// Gizlenen bir glyph nabzını BIRAKIR. Üretimdeki senaryo: şeridin faz glyph'i bir Resolve koşusunda
    /// <c>Building</c>'e alınır, koşu bitince <c>Collapsed</c> edilir ama <c>Status</c> hiç sıfırlanmaz —
    /// nabız görünmeyen bir kontrolün üzerinde sonsuza dek döner ve uygulama bir daha hiç boşa düşmez.
    /// </summary>
    [StaFact]
    public void A_hidden_status_glyph_stops_pulsing()
    {
        var glyph = new StatusGlyph { Status = GraphStatus.Building, AnimationsEnabledProvider = () => true };
        var window = DsResources.Realize(DsResources.NewHost(), glyph);
        Assert.True(glyph.IsPulsing, "ön-koşul: görünür building glyph GERÇEKTEN nabız atmalı");

        glyph.Visibility = Visibility.Collapsed;
        window.UpdateLayout();

        Assert.False(glyph.IsPulsing);
        GC.KeepAlive(window);
    }

    /// <summary>Simetrik yön: yeniden görünür olunca nabız geri gelir (tek yönlü bir kapı burada kırılır).</summary>
    [StaFact]
    public void A_glyph_that_becomes_visible_again_resumes_its_pulse()
    {
        var glyph = new StatusGlyph { Status = GraphStatus.Building, AnimationsEnabledProvider = () => true };
        var window = DsResources.Realize(DsResources.NewHost(), glyph);
        glyph.Visibility = Visibility.Collapsed;
        window.UpdateLayout();
        Assert.False(glyph.IsPulsing); // non-vacuous

        glyph.Visibility = Visibility.Visible;
        window.UpdateLayout();

        Assert.True(glyph.IsPulsing);
        GC.KeepAlive(window);
    }
}
