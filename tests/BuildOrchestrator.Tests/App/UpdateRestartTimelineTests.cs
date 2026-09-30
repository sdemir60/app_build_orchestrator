using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.23.0 §2.12 · §9 "Restart ekranı" · motor · Task 11 · K6] Restart ekranının zaman çizelgesi — saf çekirdek.
/// TEK adım: kapanış (<c>Closing &lt;ürün&gt;…</c>); çubuk 800ms'de doğrusal olarak %100'e dolar ve orada kalır.
///
/// <para><b>Eski iddia</b> (motor yokken tasarımın önizlemesi): üç adım — kapanış 800ms → %20, kurulum 1100ms → %78,
/// açılış 800ms → %100 (<c>UPDATE_STEPS</c>, BuildApp.jsx:1660); toplam 2700ms, ekran toplamdan 120ms sonra sönmeye
/// başlardı (BuildApp.jsx:2661) ve uygulama aynen kalırdı. <b>Değişti</b> (K6, kullanıcı kararı 2026-09-30): Windows
/// çalışan bir programın dosyalarını değiştirmeye izin vermez — kurulum ancak uygulama kapandıktan sonra Update.exe
/// tarafından, penceresiz yapılır. Uygulamanın kendi penceresinde gösterebileceği tek adım kapanıştır; kurulum ve
/// açılış bu ekranda oynatılsaydı gerçekte olmayan bir şeyi anlatırdı. Sönüş de yoktur: ekran pencere kapanana dek
/// kalır.</para>
/// </summary>
public class UpdateRestartTimelineTests
{
    /// <summary>Tek adım kapanıştır, adlandırılmış sabitlerle: 800ms'de %100. Çizelgenin toplamı o adımın süresidir;
    /// toplamın ötesi %100'de kalır — çıkış gecikirse çubuk dolu durur.</summary>
    [Fact]
    public void The_only_step_is_closing_and_the_bar_fills_over_800ms()
    {
        Assert.Equal(800.0, UpdateRestartTimeline.ClosingMs);
        Assert.Equal(100.0, UpdateRestartTimeline.ClosingEndPercent);

        var stage = Assert.Single(UpdateRestartTimeline.Stages);
        Assert.Equal(UpdateRestartStep.Closing, stage.Step);
        Assert.Equal(800, stage.DurationMs);
        Assert.Equal(100, stage.EndPercent);
        Assert.Equal(800, UpdateRestartTimeline.TotalMs);
        Assert.Equal(new UpdateRestartFrame(UpdateRestartStep.Closing, 50), UpdateRestartTimeline.At(400));
        Assert.Equal(100, UpdateRestartTimeline.At(5000).Percent); // çıkış gecikirse çubuk dolu kalır
    }

    /// <summary>Her an kapanış adımını ve doğrusal yüzdeyi okur: sıfırın öncesi (saat geri giderse) başlangıçtır, adımın
    /// ortası ara değerdir, bitişi ve ötesi %100'dür.</summary>
    [Theory]
    [InlineData(-50, 0)]
    [InlineData(0, 0)]
    [InlineData(400, 50)]
    [InlineData(799, 99.875)]
    [InlineData(800, 100)]
    [InlineData(9000, 100)]
    public void Each_moment_reads_the_closing_step_and_a_linear_percentage(double elapsedMs, double percent)
    {
        var frame = UpdateRestartTimeline.At(elapsedMs);

        Assert.Equal(UpdateRestartStep.Closing, frame.Step);
        Assert.Equal(percent, frame.Percent, precision: 6);
    }

    /// <summary>Metinler (tek kaynak <see cref="UpdateText"/>): başlık <c>Updating &lt;ürün&gt;</c>; adım etiketi
    /// <c>Closing &lt;ürün&gt;</c>, sonunda U+2026. Ürün adı yazılmaz, <see cref="AppIdentity.Product"/>'tan okunur.
    /// <para>Eski iddia: <c>Installing &lt;gelen&gt;…</c> ve <c>Starting &lt;gelen&gt;…</c> etiketleri de vardı — iki adım
    /// kalktı (K6), etiket artık gelen sürümü anmaz.</para></summary>
    [Fact]
    public void The_step_label_names_the_product()
    {
        Assert.Equal("Updating " + AppIdentity.Product, UpdateText.RestartHeading);
        Assert.Equal("Closing " + AppIdentity.Product + "…", UpdateText.RestartStepLabel(UpdateRestartStep.Closing));
    }
}
