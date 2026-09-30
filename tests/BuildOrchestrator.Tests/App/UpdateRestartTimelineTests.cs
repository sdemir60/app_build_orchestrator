using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.23.0 §2.12 · §9 "Restart ekranı" · plan U4] Restart ekranının zaman çizelgesi — saf çekirdek. Üç adım
/// (<c>UPDATE_STEPS</c>, BuildApp.jsx:1660): kapanış 800ms → %20, kurulum 1100ms → %78, açılış 800ms → %100; ilerleme
/// adım İÇİNDE doğrusaldır (BuildApp.jsx:1757-1763). Toplam 2700ms; ekran toplamdan 120ms sonra sönmeye başlar
/// (BuildApp.jsx:2661). Güncelleme motoru henüz yok — çizelge tasarımın kendisidir, ekran onu oynatıp uygulamaya döner.
/// </summary>
public class UpdateRestartTimelineTests
{
    /// <summary>Adımlar adlandırılmış sabitlerdir ve tasarımın sırasıyla dizilir.</summary>
    [Fact]
    public void The_three_steps_are_closing_installing_and_starting_with_the_design_durations_and_ends()
    {
        Assert.Equal(800.0, UpdateRestartTimeline.ClosingMs);
        Assert.Equal(1100.0, UpdateRestartTimeline.InstallingMs);
        Assert.Equal(800.0, UpdateRestartTimeline.StartingMs);
        Assert.Equal(20.0, UpdateRestartTimeline.ClosingEndPercent);
        Assert.Equal(78.0, UpdateRestartTimeline.InstallingEndPercent);
        Assert.Equal(100.0, UpdateRestartTimeline.StartingEndPercent);

        Assert.Equal(
            new[]
            {
                new UpdateRestartStage(UpdateRestartStep.Closing, 800, 20),
                new UpdateRestartStage(UpdateRestartStep.Installing, 1100, 78),
                new UpdateRestartStage(UpdateRestartStep.Starting, 800, 100),
            },
            UpdateRestartTimeline.Stages);
    }

    /// <summary>Toplam 2700ms (<c>UPDATE_TOTAL</c>); sönüş toplamdan 120ms sonra başlar (<c>UPDATE_TOTAL + 120</c>).</summary>
    [Fact]
    public void The_screen_plays_2700ms_and_starts_fading_120ms_later()
    {
        Assert.Equal(2700.0, UpdateRestartTimeline.TotalMs);
        Assert.Equal(120.0, UpdateRestartTimeline.FadeOutDelayMs);
        Assert.Equal(2820.0, UpdateRestartTimeline.FadeOutAtMs);
    }

    /// <summary>Adım sınırlarında ve adım ortalarında hangi adımın okunduğu ve ilerleme yüzdesi: sınırda bir sonraki adım
    /// başlar (önceki adımın bitiş yüzdesiyle), ortada doğrusal ara değer; toplamın ötesi son adımda %100'de kalır,
    /// sıfırın öncesi (saat geri giderse) başlangıçtır.</summary>
    [Theory]
    [InlineData(-50, UpdateRestartStep.Closing, 0)]
    [InlineData(0, UpdateRestartStep.Closing, 0)]
    [InlineData(400, UpdateRestartStep.Closing, 10)]
    [InlineData(799, UpdateRestartStep.Closing, 19.975)]
    [InlineData(800, UpdateRestartStep.Installing, 20)]
    [InlineData(1350, UpdateRestartStep.Installing, 49)]
    [InlineData(1900, UpdateRestartStep.Starting, 78)]
    [InlineData(2300, UpdateRestartStep.Starting, 89)]
    [InlineData(2700, UpdateRestartStep.Starting, 100)]
    [InlineData(9000, UpdateRestartStep.Starting, 100)]
    public void Each_moment_reads_its_step_and_a_linear_percentage(double elapsedMs, UpdateRestartStep step, double percent)
    {
        var frame = UpdateRestartTimeline.At(elapsedMs);

        Assert.Equal(step, frame.Step);
        Assert.Equal(percent, frame.Percent, precision: 6);
    }

    /// <summary>Metinler (tek kaynak <see cref="UpdateText"/>): başlık <c>Updating &lt;ürün&gt;</c>; adım etiketi
    /// <c>Closing &lt;ürün&gt;</c> / <c>Installing &lt;gelen&gt;</c> / <c>Starting &lt;gelen&gt;</c>, sonunda U+2026. Ürün
    /// adı yazılmaz, <see cref="AppIdentity.Product"/>'tan okunur.</summary>
    [Fact]
    public void The_heading_and_the_step_labels_name_the_product_and_the_incoming_version()
    {
        Assert.Equal("Updating " + AppIdentity.Product, UpdateText.RestartHeading);
        Assert.Equal("Closing " + AppIdentity.Product + "…",
            UpdateText.RestartStepLabel(UpdateRestartStep.Closing, "1.8.0"));
        Assert.Equal("Installing 1.8.0…", UpdateText.RestartStepLabel(UpdateRestartStep.Installing, "1.8.0"));
        Assert.Equal("Starting 1.8.0…", UpdateText.RestartStepLabel(UpdateRestartStep.Starting, "1.8.0"));
    }
}
