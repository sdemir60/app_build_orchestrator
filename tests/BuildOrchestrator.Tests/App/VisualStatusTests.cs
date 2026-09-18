using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.20.0 §2.3 · §5] <b>Görsel durum = çıktının durumu + koşu bindirmesi.</b> Satır ve node aynı
/// eşlemeden (<see cref="VisualStatuses.For"/>) beslenir: koşu bir şey söylediyse o kazanır, atlanmak bir renk
/// değildir (çıktı durumuna düşer), işaretleme dalgası her durumu ezer. Döngü küpü durumdan bağımsızdır.
/// </summary>
public class VisualStatusTests
{
    public static TheoryData<StandingStatus, VisualStatus> StandingVisuals => new()
    {
        { StandingStatus.Unknown, VisualStatus.Unknown },
        { StandingStatus.Current, VisualStatus.Current },
        { StandingStatus.Stale, VisualStatus.Stale },
        { StandingStatus.Failed, VisualStatus.Failed },
    };

    /// <summary>Atlanan proje kendi çıktı durumunu taşır: güncelse yeşil kalır, — glyph'i/grisi yoktur.</summary>
    [Theory]
    [MemberData(nameof(StandingVisuals))]
    public void Skipped_falls_to_the_standing_colour(StandingStatus standing, VisualStatus expected)
        => Assert.Equal(expected, VisualStatuses.For(GraphStatus.Skipped, standing, marked: false));

    /// <summary>Motorun hakkında konuşmadığı proje çıktı durumunu gösterir; işaretliyse dalganın amberini.</summary>
    [Theory]
    [MemberData(nameof(StandingVisuals))]
    public void Discovered_shows_the_standing_colour_unless_marked(StandingStatus standing, VisualStatus expected)
    {
        Assert.Equal(expected, VisualStatuses.For(GraphStatus.Discovered, standing, marked: false));
        Assert.Equal(VisualStatus.Marked, VisualStatuses.For(GraphStatus.Discovered, standing, marked: true));
    }

    /// <summary>Dalga her çıktı durumunu ezer — kırmızı ya da yeşil bir kapsam üyesi de TAM amberdir.</summary>
    [Theory]
    [InlineData(StandingStatus.Unknown)]
    [InlineData(StandingStatus.Current)]
    [InlineData(StandingStatus.Stale)]
    [InlineData(StandingStatus.Failed)]
    public void Marked_wins_over_every_standing(StandingStatus standing)
        => Assert.Equal(VisualStatus.Marked, VisualStatuses.For(GraphStatus.Discovered, standing, marked: true));

    /// <summary>Koşu bir şey söylediyse (kuyruk, derleme, sonuç) çıktı durumunu ve işaretliliği ezer.
    /// <para><b>[DEĞİŞEN KURAL — R-M4 · design v1.20.0 §5]</b> Eski iddia: <c>Failed</c> da her çıktı durumunu
    /// ezer (Stale üstünde de kırmızı). Değişme gerekçesi: kırmızı KANITTIR — timeout/Stop kanıt sayılmaz ve
    /// satırı kırmızıya çevirmez; kanıtsız hata bayat (Stale) çıktı durumunu bırakır ve satır onu gösterir
    /// (<see cref="A_failure_that_is_not_evidence_reads_as_its_stale_standing"/>). Kanıtlı hatanın çıktı
    /// durumu zaten Failed'dır; Stale DIŞINDAKİ her durumda <c>Failed</c> hâlâ ezer.</para></summary>
    [Theory]
    [InlineData(GraphStatus.Queued, VisualStatus.Queued)]
    [InlineData(GraphStatus.Building, VisualStatus.Building)]
    [InlineData(GraphStatus.Succeeded, VisualStatus.Succeeded)]
    public void The_run_overlays_the_standing(GraphStatus status, VisualStatus expected)
    {
        Assert.Equal(expected, VisualStatuses.For(status, StandingStatus.Current, marked: false));
        Assert.Equal(expected, VisualStatuses.For(status, StandingStatus.Stale, marked: true));
    }

    /// <summary>[R-M4 · design v1.20.0 §5 "bozuk (kanıtlı)"] Kanıt olmayan bir koşu hatası (timeout, Stop,
    /// invoke hatası) satırı kırmızıya ÇEVİRMEZ: satır bayat çıktı durumunu (gri) gösterir. Kanıtlı hata
    /// çıktı durumunu zaten <see cref="StandingStatus.Failed"/>'a yazar ve kırmızıdır.</summary>
    [Fact]
    public void A_failure_that_is_not_evidence_reads_as_its_stale_standing()
    {
        Assert.Equal(VisualStatus.Stale, VisualStatuses.For(GraphStatus.Failed, StandingStatus.Stale, marked: false));
        Assert.Equal(VisualStatus.Stale, VisualStatuses.For(GraphStatus.Failed, StandingStatus.Stale, marked: true));
        Assert.Equal(VisualStatus.Failed, VisualStatuses.For(GraphStatus.Failed, StandingStatus.Failed, marked: false));
        Assert.Equal(VisualStatus.Failed, VisualStatuses.For(GraphStatus.Failed, StandingStatus.Current, marked: true));
    }

    /// <summary>[design v1.20.0 §1.4] — yalnız run-story'dedir: durum yüzeyleri onu hiçbir girdide almaz.</summary>
    [Fact]
    public void A_state_surface_never_receives_the_skipped_dash()
    {
        foreach (var status in Enum.GetValues<GraphStatus>())
            foreach (var standing in Enum.GetValues<StandingStatus>())
                foreach (bool marked in new[] { false, true })
                    Assert.NotEqual(VisualStatus.Skipped, VisualStatuses.For(status, standing, marked));

        Assert.Equal(VisualStatus.Skipped, VisualStatuses.OfRun(GraphStatus.Skipped)); // run-story'de durur
    }

    [Fact]
    public void Only_unknown_is_the_start_mode()
    {
        foreach (var state in Enum.GetValues<VisualStatus>())
            Assert.Equal(state == VisualStatus.Unknown, VisualStatuses.IsStartMode(state));
    }

    /// <summary>Kümülatif renk: güncel ve az önce derlenmiş AYNI yeşil; derlenecek bugünkü nötr gri.</summary>
    [Fact]
    public void Current_is_the_success_colour_and_stale_is_the_neutral_grey()
    {
        foreach (var state in new[] { VisualStatus.Current, VisualStatus.Succeeded })
        {
            Assert.Equal("Brush.StatusSuccess", VisualStatuses.StripeBrushKey(state));
            Assert.Equal("Brush.StatusSuccess", VisualStatuses.NodeBorderBrushKey(state));
            Assert.Equal("Brush.StatusSuccessSoft", VisualStatuses.NodeBackgroundBrushKey(state));
            Assert.Equal("Brush.StatusSuccessText", VisualStatuses.NodeCoreBrushKey(state, inCycle: false));
        }
        Assert.Equal("Brush.StatusSkippedBorder", VisualStatuses.StripeBrushKey(VisualStatus.Stale));
        Assert.Equal("Brush.BorderStrong", VisualStatuses.NodeBorderBrushKey(VisualStatus.Stale));
        Assert.Equal("Brush.SurfaceRaised", VisualStatuses.NodeBackgroundBrushKey(VisualStatus.Stale));
        Assert.Equal("Brush.TextFaint", VisualStatuses.NodeCoreBrushKey(VisualStatus.Stale, inCycle: false));
    }

    /// <summary>[design v1.20.0 §2.3] Döngü üyesinde küp HER durumda amber — çerçeve kendi durumunu taşır.</summary>
    [Theory]
    [InlineData(VisualStatus.Unknown)]
    [InlineData(VisualStatus.Current)]
    [InlineData(VisualStatus.Stale)]
    [InlineData(VisualStatus.Failed)]
    [InlineData(VisualStatus.Succeeded)]
    [InlineData(VisualStatus.Queued)]
    [InlineData(VisualStatus.Building)]
    public void The_cube_is_amber_for_every_cycle_member_state(VisualStatus state)
        => Assert.Equal("Brush.AmberText", VisualStatuses.NodeCoreBrushKey(state, inCycle: true));

    /// <summary>[design v1.20.0 §1.4 · §2.4-5] Satır glyph'i durumu çizer: güncel ✓, bozuk ✗, derlenecek /
    /// bilinmiyor / işaretli kesikli daire. — yalnız run-story'nin Skipped'ındadır.</summary>
    [Fact]
    public void The_status_glyph_draws_the_standing()
    {
        Assert.Equal("Icon.StatusCheck", StatusGlyph.InnerIconKeyFor(VisualStatus.Current));
        Assert.Equal("Icon.StatusCross", StatusGlyph.InnerIconKeyFor(VisualStatus.Failed));
        foreach (var dashed in new[] { VisualStatus.Unknown, VisualStatus.Stale, VisualStatus.Marked })
        {
            Assert.Null(StatusGlyph.InnerIconKeyFor(dashed));
            Assert.True(StatusGlyph.IsDashedRing(dashed));
        }
        Assert.False(StatusGlyph.IsDashedRing(VisualStatus.Current));
        Assert.Equal("Brush.StatusSuccessText", StatusGlyph.BrushKeyFor(VisualStatus.Current));

        foreach (var state in Enum.GetValues<VisualStatus>())
            Assert.Equal(state == VisualStatus.Skipped, StatusGlyph.InnerIconKeyFor(state) == "Icon.StatusDash");
    }
}
