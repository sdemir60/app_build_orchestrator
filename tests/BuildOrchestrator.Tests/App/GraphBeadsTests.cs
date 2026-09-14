using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// design v1.18.0 §9 "Beads bir tık kalın + hücreye kelepçeli yörünge" — prototype/app/BuildApp.jsx satır
/// ~517-525/622-631'in SAF portu.
///
/// <para><b>Eski iddia (artık geçersiz):</b> yörünge düğümün SABİT <c>2.8px</c> dışında dolanıyordu
/// (<c>GraphBeads.OrbitGapPx</c>), kalem <c>1.0</c>'dı ("1'den farklı olamaz" sözleşmesi), hedef aralık
/// <c>3.4px</c>, tur <c>4200ms</c>. v1.18.0 yörüngeyi bir tık kalınlaştırdı (1.6) VE hücreye KELEPÇELEDİ:
/// dışarı mesafe artık sabit değil, hücre pitch'inden geriye çözülen <c>bgap ∈ [0.8, 2.8]</c>'dir — dar
/// pitch'te (yoğun graf) yörünge düğüme yaklaşır, bol pitch'te eski tavanda (2.8) kalır. Kalınlık 1
/// olmadığı için WPF dash birimi (kalınlık çarpanı) artık desene VE saate BÖLÜNEREK uygulanır.</para>
/// </summary>
public class GraphBeadsTests
{
    /// <summary>
    /// AYIRT EDİCİ — <c>bgap</c> artık sabit değil, <c>(pitch − size − 1.6 − 2) / 2</c>'den [0.8, 2.8]'e
    /// kelepçeli geriye çözülür. Üç senaryo: tipik pitch (kelepçelenmez), bol pitch (tavana kelepçelenir,
    /// "kelepçe kapanmaz" — yörünge sonsuza büyümez) ve yoğun graf (tabana kelepçelenir).
    /// </summary>
    [Theory]
    [InlineData(20.0, 12.0, 2.2)]  // tipik: (20-12-1.6-2)/2 = 2.2, kelepçelenmez
    [InlineData(44.0, 24.0, 2.8)]  // bol: unclamped 8.2 → tavan 2.8'de kelepçe kapanmaz
    [InlineData(8.0, 8.0, 0.8)]    // yoğun: unclamped -1.8 → taban 0.8
    public void The_orbit_gap_is_solved_backward_from_pitch_and_clamped_to_0_8_2_8(
        double pitch, double nodeSize, double expectedGap)
    {
        var g = GraphBeads.For(nodeSize, pitch);

        Assert.Equal(nodeSize + expectedGap * 2, g.Side, 6); // yol kenarı = size + 2·bgap
    }

    /// <summary>
    /// AYIRT EDİCİ — kelepçe TABANA inmediği sürece (bgap &gt; 0.8) komşu hücrelerin yörünge mürekkepleri
    /// arasında en az 2px boşluk kalır: <c>pitch − size − 2·(bgap + kalınlık/2) ≥ 2</c>. Formül unclamped
    /// kaldığı sürece bu cebirsel bir ÖZDEŞLİKTİR (tam 2'dir); tavana kelepçelendiğinde (bol pitch) boşluk
    /// 2'den daha da büyür. Tabana kelepçelenince (yoğun graf) garanti YOKTUR — mürekkepler dokunabilir; bu
    /// kabul edilen bir ödünleşimdir (bkz. dördüncü senaryo, negatif boşluk).
    /// </summary>
    [Theory]
    [InlineData(20.0, 12.0)]  // unclamped → boşluk tam 2
    [InlineData(44.0, 24.0)]  // tavana kelepçeli → boşluk 2'den büyük
    public void Neighboring_orbit_ink_stays_at_least_2px_apart_unless_the_clamp_hits_the_floor(
        double pitch, double nodeSize)
    {
        var g = GraphBeads.For(nodeSize, pitch);
        double bgap = (g.Side - nodeSize) / 2;
        Assert.True(bgap > GraphBeads.MinOrbitGapPx, "bu senaryo taban kelepçesinde olmamalı");

        double neighborGap = pitch - nodeSize - 2 * (bgap + GraphBeads.StrokeThickness / 2);
        Assert.True(neighborGap >= 2.0 - 1e-9, $"komşu mürekkepler arası boşluk {neighborGap}px — 2px altında");
    }

    /// <summary>Yoğun grafta (pitch tabana çarpar) mürekkepler ARTIK 2px garantisi TAŞIMAZ — kelepçenin
    /// tabana indiği durumun dokümante edilmiş istisnası.</summary>
    [Fact]
    public void The_2px_neighbor_guarantee_does_not_hold_once_the_gap_floors_out()
    {
        var g = GraphBeads.For(8.0, 8.0);
        double bgap = (g.Side - 8.0) / 2;
        Assert.Equal(GraphBeads.MinOrbitGapPx, bgap, 6); // tabana kelepçeli

        double neighborGap = 8.0 - 8.0 - 2 * (bgap + GraphBeads.StrokeThickness / 2);
        Assert.True(neighborGap < 2.0, "taban kelepçesinde mürekkepler dokunabilir — bu beklenen");
    }

    /// <summary>Köşe yarıçapı yarım kenardır, 6.8'de tavanlanır — formül değişmedi, yalnız kenar artık
    /// pitch'ten türüyor.</summary>
    [Theory]
    [InlineData(44.0, 24.0, 6.8)]  // kenar 29.6 → yarısı 14.8, tavana kelepçelenir
    [InlineData(20.0, 12.0, 6.8)]  // kenar 16.4 → yarısı 8.2, tavana kelepçelenir
    [InlineData(8.0, 8.0, 4.8)]    // kenar 9.6 → yarısı 4.8, tavanın altında
    public void The_corner_radius_is_half_the_side_capped_at_6_8(
        double pitch, double nodeSize, double expected)
        => Assert.Equal(expected, GraphBeads.For(nodeSize, pitch).CornerRadius, 6);

    /// <summary>Çevre = yuvarlatılmış karenin gerçek çevresi: <c>4·kenar − 8·r + 2πr</c>.</summary>
    [Fact]
    public void The_perimeter_is_the_rounded_square_perimeter()
    {
        var g = GraphBeads.For(24.0, 44.0);

        Assert.Equal(4 * g.Side - 8 * g.CornerRadius + 2 * Math.PI * g.CornerRadius, g.Perimeter, 9);
    }

    /// <summary>
    /// AYIRT EDİCİ: adım çevreyi TAM böler — desen ek yerinde bindirmez. Hedef aralık artık 3.2 (eskiden
    /// 3.4). Üç pitch/size senaryosu (tipik, bol, yoğun) da kapsanır.
    /// </summary>
    [Theory]
    [InlineData(20.0, 12.0)]
    [InlineData(44.0, 24.0)]
    [InlineData(8.0, 8.0)]
    public void The_dash_step_divides_the_perimeter_a_whole_number_of_times(double pitch, double nodeSize)
    {
        var g = GraphBeads.For(nodeSize, pitch);

        double count = g.Perimeter / g.DashStep;
        Assert.Equal(Math.Round(count), count, 9);
        Assert.True(count >= GraphBeads.MinBeadCount, $"pitch {pitch}/size {nodeSize}'de yalnız {count} nokta kaldı");
        // Adım hedef aralığın (3.2) yakınında kalır — yuvarlama onu başka bir mertebeye taşımaz.
        Assert.InRange(g.DashStep, GraphBeads.BeadSpacingPx * 0.6, GraphBeads.BeadSpacingPx * 1.6);
    }

    /// <summary>
    /// AYIRT EDİCİ: dash deseni <c>0.01 / (adım − 0.01)</c>'in kalınlığa (1.6) BÖLÜNMÜŞ hâlidir — WPF
    /// <c>StrokeDashArray</c> birimi px değil kalınlık ÇARPANIdır. <b>Eski iddia:</b> kalınlık 1 olduğu için
    /// bölme etkisizdi ve SVG'nin mutlak değerleri birebir taşınıyordu; kalınlık 1.6 olunca bölme artık
    /// GÖZLENEBİLİR bir fark yaratır (bölmezsen desen 1.6× uzar).
    /// </summary>
    [Fact]
    public void The_dash_pattern_is_a_hairline_dot_divided_by_the_stroke_thickness()
    {
        var g = GraphBeads.For(24.0, 44.0);
        var dash = GraphBeads.DashArrayFor(g);

        Assert.Equal(2, dash.Count);
        Assert.Equal(0.01 / GraphBeads.StrokeThickness, dash[0], 9);
        Assert.Equal((g.DashStep - 0.01) / GraphBeads.StrokeThickness, dash[1], 9);
        Assert.True(dash.IsFrozen, "desen donmuş değil — düğümler arasında paylaşılamaz");
    }

    /// <summary>§9 v1.18.0'ın sayıları — birinin sessizce kayması bu testi düşürür.
    /// <b>Eski değerler (v1.3.0, artık geçersiz):</b> kalınlık 1.0, hedef aralık 3.4, tur 4200ms, sabit
    /// dışarı mesafe 2.8.</summary>
    [Fact]
    public void The_beads_numbers_are_pinned_to_their_v1_18_0_spec_values()
    {
        Assert.Equal(0.8, GraphBeads.MinOrbitGapPx, 6);
        Assert.Equal(2.8, GraphBeads.MaxOrbitGapPx, 6);
        Assert.Equal(2.0, GraphBeads.NeighborGapPx, 6);
        Assert.Equal(3.2, GraphBeads.BeadSpacingPx, 6);
        Assert.Equal(1.6, GraphBeads.StrokeThickness, 6);
        Assert.Equal(2400.0, GraphBeads.CycleMs, 6);
        Assert.Equal(420.0, GraphBeads.FadeInMs, 6);
        Assert.Equal(640.0, GraphBeads.FadeOutMs, 6);
        Assert.Equal(700.0, GraphBeads.SpinAfterStopMs, 6);
    }
}
