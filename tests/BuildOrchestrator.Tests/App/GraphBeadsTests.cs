using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// design v1.3.0 §2.3 "Building animasyonu — beads" — prototype/app/BuildApp.jsx satır 379-384'ün SAF
/// portu. Nokta deseni ÇEVREYE TAM BÖLÜNÜR; ek yerinde bindirme olmaz.
///
/// <para><b>Eski iddia (<c>GraphRenderTests</c> nabız testleri, artık geçersiz):</b> building düğüm DS
/// <c>ds-node-pulse</c> ile 1.6s'de <c>1 → 0.5 → 1</c> nefes alıyordu. v1.3.0 §2.3 nabzı kaldırdı ve yerine
/// düğümün 2.8px dışında dolanan amber noktaları koydu.</para>
/// </summary>
public class GraphBeadsTests
{
    /// <summary>Yörünge düğümün 2.8px DIŞINDADIR: kutu = node + 12, iç pay = 6 − 2.8 = 3.2 ⇒ kenar =
    /// node + 5.6 (JSX:380-381).</summary>
    [Fact]
    public void The_orbit_sits_2_8px_outside_the_node()
    {
        var geometry = GraphBeads.For(24);

        Assert.Equal(29.6, geometry.Side, 6); // 24 + 5.6
        Assert.Equal((geometry.Side - 24) / 2, GraphBeads.OrbitGapPx, 6);
    }

    /// <summary>Köşe yarıçapı yarım kenardır, 6.8'de tavanlanır (JSX:381).</summary>
    [Theory]
    [InlineData(24.0, 6.8)]  // kenar 29.6 → yarısı 14.8, tavana kelepçelenir
    [InlineData(8.0, 6.8)]   // kenar 13.6 → yarısı 6.8, tam tavanda
    [InlineData(3.0, 4.3)]   // kenar 8.6 → yarısı 4.3, tavanın altında
    public void The_corner_radius_is_half_the_side_capped_at_6_8(double nodeSize, double expected)
        => Assert.Equal(expected, GraphBeads.For(nodeSize).CornerRadius, 6);

    /// <summary>Çevre = yuvarlatılmış karenin gerçek çevresi: <c>4·kenar − 8·r + 2πr</c> (JSX:382).</summary>
    [Fact]
    public void The_perimeter_is_the_rounded_square_perimeter()
    {
        var g = GraphBeads.For(24);

        Assert.Equal(4 * g.Side - 8 * g.CornerRadius + 2 * Math.PI * g.CornerRadius, g.Perimeter, 9);
    }

    /// <summary>
    /// AYIRT EDİCİ: adım çevreyi TAM böler — desen ek yerinde bindirmez (JSX:383). Adımı sabit 3.4 alan bir
    /// uygulama bu testi düşürür.
    /// </summary>
    [Theory]
    [InlineData(8.0)]
    [InlineData(12.3)]
    [InlineData(24.0)]
    public void The_dash_step_divides_the_perimeter_a_whole_number_of_times(double nodeSize)
    {
        var g = GraphBeads.For(nodeSize);

        double count = g.Perimeter / g.DashStep;
        Assert.Equal(Math.Round(count), count, 9);
        Assert.True(count >= GraphBeads.MinBeadCount, $"{nodeSize}px düğümde yalnız {count} nokta kaldı");
        // Adım hedef aralığın (3.4) yakınında kalır — yuvarlama onu başka bir mertebeye taşımaz.
        Assert.InRange(g.DashStep, GraphBeads.BeadSpacingPx * 0.6, GraphBeads.BeadSpacingPx * 1.6);
    }

    /// <summary>Dash deseni <c>0.01 / (adım − 0.01)</c>: iğne ucu kadar dolu, geri kalanı boş. 1px kalınlıkta
    /// WPF'in çarpan birimi mutlak px'e eşittir, dolayısıyla SVG değerleri BİREBİR taşınır (JSX:384).</summary>
    [Fact]
    public void The_dash_pattern_is_a_hairline_dot_followed_by_the_rest_of_the_step()
    {
        var g = GraphBeads.For(24);
        var dash = GraphBeads.DashArrayFor(g);

        Assert.Equal(2, dash.Count);
        Assert.Equal(0.01, dash[0], 9);
        Assert.Equal(g.DashStep - 0.01, dash[1], 9);
        Assert.True(dash.IsFrozen, "desen donmuş değil — düğümler arasında paylaşılamaz");
        Assert.Equal(1.0, GraphBeads.StrokeThickness, 9); // dash birimi = kalınlık çarpanı (sözleşme)
    }

    /// <summary>§2.3'ün sayıları — birinin sessizce kayması bu testi düşürür.</summary>
    [Fact]
    public void The_beads_numbers_are_pinned_to_their_spec_values()
    {
        Assert.Equal(2.8, GraphBeads.OrbitGapPx, 6);
        Assert.Equal(3.4, GraphBeads.BeadSpacingPx, 6);
        Assert.Equal(4200.0, GraphBeads.CycleMs, 6);
        Assert.Equal(420.0, GraphBeads.FadeInMs, 6);
        Assert.Equal(640.0, GraphBeads.FadeOutMs, 6);
        Assert.Equal(700.0, GraphBeads.SpinAfterStopMs, 6);
    }
}
