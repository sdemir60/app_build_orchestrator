using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T58] LayoutMetrics — SAF (WPF'siz) kümülatif offset servisi. Sticky overlay (bu task) VE follow-mode
/// (T59) AYNI instance'ı tüketir. Aritmetik: satır 36px + katman başlığı 24px kümülatif tablosu; her satırın/
/// başlığın mutlak Y'si; belirli VerticalOffset'te yapışık başlık kümesi (birikimli i×24, prototip
/// BuildApp.jsx:489 `top: stick*24` + README §2.4 "üsttekiler asılı kalır" = accumulation, push-off YOK).
/// </summary>
public class LayoutMetricsTests
{
    // design-v1 §2.4: satır 36px, katman başlığı 24px.
    private static LayoutMetrics Mixed() =>
        new([new LayerSpec("L0", 3), new LayerSpec("L1", 5), new LayerSpec("L2", 6)]);

    [Fact]
    public void CumulativeTable_gives_absolute_Y_of_each_header_and_row_for_a_mixed_36_24_structure()
    {
        var m = Mixed();

        // Header 0 en üstte; rows 24/60/96; header 1 132'de başlar (24 + 3*36); ...
        Assert.Equal(0, m.OffsetOfHeader(0));
        Assert.Equal(24, m.OffsetOfRow(0));
        Assert.Equal(60, m.OffsetOfRow(1));
        Assert.Equal(96, m.OffsetOfRow(2));
        Assert.Equal(132, m.OffsetOfHeader(1));
        Assert.Equal(156, m.OffsetOfRow(3));
        Assert.Equal(300, m.OffsetOfRow(7));
        Assert.Equal(336, m.OffsetOfHeader(2));
        Assert.Equal(360, m.OffsetOfRow(8));
        Assert.Equal(540, m.OffsetOfRow(13));

        Assert.Equal(14, m.RowCount);
        Assert.Equal(3, m.Headers.Count);
        // Toplam yükseklik = 3 başlık*24 + 14 satır*36 = 72 + 504 = 576.
        Assert.Equal(576, m.TotalHeight);
    }

    [Fact]
    public void Headers_expose_slot_index_name_rowcount_and_first_row_index()
    {
        var m = Mixed();

        Assert.Equal("L1", m.Headers[1].Name);
        Assert.Equal(1, m.Headers[1].SlotIndex);
        Assert.Equal(5, m.Headers[1].RowCount);
        Assert.Equal(3, m.Headers[1].FirstRowIndex); // L0'ın 3 satırından sonra
        Assert.Equal(132, m.Headers[1].ContentTop);
    }

    [Fact]
    public void StickyHeadersAt_pins_the_i_th_visible_header_at_i_times_24()
    {
        var m = Mixed();

        // v=0: yalnız header 0 yapışık, slot 0 → Y=0.
        var at0 = m.StickyHeadersAt(0);
        Assert.Single(at0);
        Assert.Equal(0, at0[0].SlotIndex);
        Assert.Equal(0, at0[0].PinnedY);
        Assert.Equal("L0", at0[0].Name);

        // Header 1 eşiği τ1 = 132 - 24 = 108. Tam eşikte header 1 slot 1'e (Y=24) yapışır; header 0 hâlâ Y=0.
        var at108 = m.StickyHeadersAt(108);
        Assert.Equal(2, at108.Count);
        Assert.Equal(new[] { 0.0, 24.0 }, at108.Select(h => h.PinnedY).ToArray());
        Assert.Equal(new[] { "L0", "L1" }, at108.Select(h => h.Name).ToArray());

        // Header 2 eşiği τ2 = 336 - 48 = 288 → üç başlık da yapışık, 0/24/48.
        var at288 = m.StickyHeadersAt(288);
        Assert.Equal(3, at288.Count);
        Assert.Equal(new[] { 0.0, 24.0, 48.0 }, at288.Select(h => h.PinnedY).ToArray());
    }

    [Fact]
    public void StickyHeadersAt_just_below_a_threshold_does_not_yet_stick_the_next_header()
    {
        var m = Mixed();

        // τ1 = 108; hemen altında (107) yalnız header 0 yapışık (header 1 hâlâ in-flow, aşağıdan yukarı kayıyor).
        var at107 = m.StickyHeadersAt(107);
        Assert.Single(at107);
        Assert.Equal(0, at107[0].SlotIndex);
    }

    [Fact]
    public void StickyHeadersAt_when_next_header_reaches_the_stack_it_stacks_below_and_upper_stays_pinned()
    {
        // "push-off": alttaki başlık yukarı gelip yığına ulaşınca ÜSTTEKİ asılı kalır (README §2.4 /
        // prototip: birikimli — üstteki itilip düşmez, yığın büyür). Header 1 slot 1'e ulaştığında (v=108)
        // header 0 hâlâ Y=0'da; bir alt satır kaydırmada (v=200) da öyle.
        var m = Mixed();

        var before = m.StickyHeadersAt(107);
        Assert.Single(before);                 // yalnız header 0

        var reached = m.StickyHeadersAt(108);  // header 1 yığına ulaştı
        Assert.Equal(2, reached.Count);
        Assert.Equal(0, reached[0].PinnedY);   // header 0 İTİLMEDİ — hâlâ 0
        Assert.Equal(24, reached[1].PinnedY);  // header 1 altına yığıldı

        var deeper = m.StickyHeadersAt(200);   // L1 satırları içinde daha da aşağı
        Assert.Equal(2, deeper.Count);
        Assert.Equal(0, deeper[0].PinnedY);    // header 0 hâlâ asılı (accumulation)
        Assert.Equal(24, deeper[1].PinnedY);
    }

    [Fact]
    public void StickyHeadersAt_at_the_bottom_keeps_all_headers_stacked()
    {
        var m = Mixed();

        var atBottom = m.StickyHeadersAt(m.TotalHeight);
        Assert.Equal(3, atBottom.Count);
        Assert.Equal(new[] { 0.0, 24.0, 48.0 }, atBottom.Select(h => h.PinnedY).ToArray());
    }

    [Fact]
    public void ScrollTargetForRow_subtracts_the_top_margin_and_floors_at_zero()
    {
        var m = Mixed();

        // Follow/selection hedefi = satırın offsetTop'u - üst boşluk (prototip: max(150, viewport*0.3)),
        // 0'ın altına inmez. Row 8 offsetTop=360, margin 150 → 210.
        Assert.Equal(210, m.ScrollTargetForRow(8, topMargin: 150));
        // Row 0 offsetTop=24, margin 150 → clamp 0.
        Assert.Equal(0, m.ScrollTargetForRow(0, topMargin: 150));
        // margin yoksa offsetTop'un kendisi.
        Assert.Equal(360, m.ScrollTargetForRow(8));
    }

    [Fact]
    public void Default_no_layers_is_a_uniform_36px_headerless_list_with_no_sticky()
    {
        var m = LayoutMetrics.Flat(191); // OSYS ~191 bandı — tek başlıksız liste, uniform 36px

        Assert.Empty(m.Headers);
        Assert.Equal(191, m.RowCount);
        Assert.Equal(0, m.OffsetOfRow(0));
        Assert.Equal(36, m.OffsetOfRow(1));
        Assert.Equal(190 * 36, m.OffsetOfRow(190));
        Assert.Equal(191 * 36, m.TotalHeight);

        // Sticky DEVREDE DEĞİL — hangi offset olursa olsun yapışık başlık yok.
        Assert.Empty(m.StickyHeadersAt(0));
        Assert.Empty(m.StickyHeadersAt(5000));
    }

    [Fact]
    public void Empty_layer_zero_rows_still_emits_its_header_and_stacks()
    {
        // Filtre/config bir katmanı boş bırakırsa (0 satır) başlık yine de yer alır ve yığına katılır.
        var m = new LayoutMetrics([new LayerSpec("A", 0), new LayerSpec("B", 2)]);

        Assert.Equal(2, m.Headers.Count);
        Assert.Equal(0, m.OffsetOfHeader(0));
        Assert.Equal(24, m.OffsetOfHeader(1)); // A boş → B başlığı hemen altında
        Assert.Equal(48, m.OffsetOfRow(0));    // B'nin ilk satırı iki başlıktan sonra

        // Her iki başlık aynı offset'te yapışır (τA=0, τB=24-24=0) → 0 ve 24.
        var stuck = m.StickyHeadersAt(0);
        Assert.Equal(new[] { 0.0, 24.0 }, stuck.Select(h => h.PinnedY).ToArray());
    }
}
