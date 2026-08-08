using System.Windows;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// design v1.3.0 §2.3 "Yerleşim — katman bantları" — prototype/app/BuildApp.jsx <c>graphLayout</c>
/// (satır 259-289) SAF portu. Graf HER panel boyutunda tam sığar; scrollbar yoktur.
///
/// <para><b>Eski iddia (<c>GraphLayoutTests</c>, artık geçersiz):</b> tuval 880px tabanlıydı, satır aralığı
/// 96px sabitti, düğüm 26px'ti ve yerleşim panel boyutundan BAĞIMSIZDI (graf sığmadığında kamera
/// uzaklaşırdı). v1.3.0 §2.3 bunu ezdi: "Pitch (node adımı) otomatik: 44px'ten 5'e 0.5 adımla taranır;
/// tüm bantlar + bant boşlukları (0.7×pitch) panel yüksekliğine sığan İLK değer seçilir."</para>
/// </summary>
public class QuietGraphLayoutTests
{
    /// <summary>Verilen katman boyutlarına göre <c>L{katman}.P{sıra}</c> adlı düğümler — bant içi sıra
    /// giriş (build-order) sırasıdır.</summary>
    private static IReadOnlyList<GraphNode> Bands(params int[] counts)
    {
        var nodes = new List<GraphNode>();
        for (int layer = 0; layer < counts.Length; layer++)
            for (int i = 0; i < counts[layer]; i++)
                nodes.Add(new GraphNode($"L{layer}.P{i:D3}", layer, GraphStatus.Discovered));
        return nodes;
    }

    /// <summary>
    /// Hesap alanı = panel − 2×28 kenar payı, DÖRT YANDA SİMETRİK.
    ///
    /// <para><b>Eski iddia:</b> pay 12px'ti ve yükseklikte ayrıca 18px'lik mono ipucu rezervi vardı
    /// (JSX:339-341: <c>graphLayout(W - 24, H - 24 - 18)</c>). İkisi de kullanıcı kararıyla değişti:
    /// gerçek koşuda graf panele yapışık ve "ferah değil" duruyordu (pay 12 → 28), ipucu satırı da
    /// kaldırıldı (rezerv → yok).</para>
    /// </summary>
    [Fact]
    public void The_content_box_is_the_panel_minus_a_symmetric_28px_inset()
    {
        var content = QuietGraphLayout.ContentSize(new Size(640, 360));

        Assert.Equal(640 - 56, content.Width, 3);
        Assert.Equal(360 - 56, content.Height, 3);
    }

    /// <summary>Panel çok küçükse hesap 240×160 tabanına oturur (JSX:339
    /// <c>Math.max(240, dim.w)</c> / <c>Math.max(160, dim.h)</c>) — aksi halde negatif bir hesap alanı
    /// tarama döngüsünü anlamsız kılardı.</summary>
    [Fact]
    public void A_tiny_panel_floors_at_the_240_by_160_minimum()
    {
        var content = QuietGraphLayout.ContentSize(new Size(100, 80));

        Assert.Equal(240 - 56, content.Width, 3);
        Assert.Equal(160 - 56, content.Height, 3);
    }

    /// <summary>Bol yer varsa tarama İLK adımda (44) durur; sütun sayısı en kalabalık bandı ASLA aşmaz
    /// (JSX:269) — aşsaydı satır-içi ortalama ile blok ortalaması çakışırdı.</summary>
    [Fact]
    public void A_roomy_panel_keeps_the_44px_ceiling_and_never_exceeds_the_widest_band()
    {
        var result = QuietGraphLayout.Compute(Bands(10, 10, 10), new Size(640, 360));

        Assert.Equal(QuietGraphLayout.MaxPitch, result.Pitch, 3);
        Assert.Equal(10, result.Columns);
    }

    /// <summary>
    /// AYIRT EDİCİ: tarama 0.5 adımlıdır ve SIĞAN İLK değeri alır — tam sayıya yuvarlamaz.
    ///
    /// <para>6 bant × 40 düğüm, 640×360 panel (hesap alanı 584×304): 20 ve üstü sığmaz
    /// (15.5 satır-birimi × 20 = 310 &gt; 304), 19.5 sığar (15.5 × 19.5 = 302.25 ≤ 304). Adımı 1px'e
    /// yuvarlayan bir uygulama burada 19 verir ve KIRMIZI olur.</para>
    /// </summary>
    [Fact]
    public void The_pitch_scan_walks_down_in_half_pixel_steps_and_takes_the_FIRST_value_that_fits()
    {
        var result = QuietGraphLayout.Compute(Bands(40, 40, 40, 40, 40, 40), new Size(640, 360));

        Assert.Equal(19.5, result.Pitch, 3);
        Assert.Equal(29, result.Columns);
    }

    /// <summary>Düğüm kenarı = pitch × 0.6, 8–24 kelepçesiyle (JSX:288). Bol yerde tavana çarpar
    /// (44 × 0.6 = 26.4 → 24), kalabalıkta banttan gelir (19.5 × 0.6 = 11.7).</summary>
    [Fact]
    public void The_node_edge_is_60_percent_of_the_pitch_clamped_to_8_and_24()
    {
        var roomy = QuietGraphLayout.Compute(Bands(10, 10, 10), new Size(640, 360));
        var crowded = QuietGraphLayout.Compute(Bands(40, 40, 40, 40, 40, 40), new Size(640, 360));

        Assert.Equal(QuietGraphLayout.MaxNodeSize, roomy.NodeSize, 3); // 26.4 tavana kelepçelendi
        Assert.Equal(11.7, crowded.NodeSize, 3);
        Assert.Equal(crowded.Pitch * QuietGraphLayout.NodeSizeFactor, crowded.NodeSize, 6);
    }

    /// <summary>Hiçbir pitch sığmazsa taban (5) kullanılır ve düğüm 8px tabanına iner — graf yine üretilir,
    /// hiçbir düğüm düşürülmez (JSX:267 <c>let pitch = 5, cols = 1</c>).</summary>
    [Fact]
    public void A_graph_that_fits_at_no_pitch_falls_back_to_the_5px_floor()
    {
        var result = QuietGraphLayout.Compute(Bands([.. Enumerable.Repeat(50, 40)]), new Size(300, 200));

        Assert.Equal(QuietGraphLayout.MinPitch, result.Pitch, 3);
        Assert.Equal(QuietGraphLayout.MinNodeSize, result.NodeSize, 3);
        Assert.Equal(40 * 50, result.Positions.Count);
    }

    /// <summary>Bantlar derlenme sırasına göre: layer 0 en ÜSTTE, bant içi build-order SOLDAN SAĞA (§2.3).</summary>
    [Fact]
    public void Bands_run_top_down_by_layer_and_left_to_right_in_build_order()
    {
        var p = QuietGraphLayout.Compute(Bands(4, 4), new Size(640, 360)).Positions;

        Assert.True(p["L0.P000"].X < p["L0.P001"].X);
        Assert.True(p["L0.P001"].X < p["L0.P002"].X);
        Assert.True(p["L0.P000"].Y < p["L1.P000"].Y);
        Assert.Equal(p["L0.P000"].Y, p["L0.P003"].Y, 6); // aynı satır
    }

    /// <summary>Bant boşluğu 0.7 × pitch: bir bandın satırı ile sonraki bandın ilk satırı arasında
    /// 1 + 0.7 = 1.7 pitch vardır (JSX:282 <c>rowCursor += rows + 0.7</c>, merkezler 0.5 offsetli).</summary>
    [Fact]
    public void The_gap_between_two_bands_is_zero_point_seven_pitches()
    {
        var result = QuietGraphLayout.Compute(Bands(4, 4), new Size(640, 360));

        double delta = result.Positions["L1.P000"].Y - result.Positions["L0.P000"].Y;
        Assert.Equal(1.7 * result.Pitch, delta, 3);
    }

    /// <summary>Bandın EKSİK kalan son satırı yatayda ORTALANIR (JSX:279
    /// <c>offX = ((cols - n) / 2) * pitch</c>) — kısa satırın orta noktası dolu satırınkiyle aynıdır.
    /// (Blok ortalaması ikisini de aynı miktarda kaydırır, dolayısıyla karşılaştırma geçerlidir.)</summary>
    [Fact]
    public void An_incomplete_last_row_is_centred_against_the_full_row_above_it()
    {
        // 640px panel (hesap alanı 584) / pitch 44 → 13 sütun; bant 17 düğümlü ⇒ satır 1 = 13, satır 2 = 4.
        var result = QuietGraphLayout.Compute(Bands(17), new Size(640, 360));
        var p = result.Positions;

        Assert.Equal(13, result.Columns);
        double fullRowMid = (p["L0.P000"].X + p["L0.P012"].X) / 2;
        double shortRowMid = (p["L0.P013"].X + p["L0.P016"].X) / 2;
        Assert.Equal(fullRowMid, shortRowMid, 3);
    }

    /// <summary>Tüm blok hesap alanında ortalanır (JSX:284-287): merkezlerin sınır kutusunun ortası =
    /// hesap alanının ortası.</summary>
    [Fact]
    public void The_whole_block_is_centred_inside_the_content_box()
    {
        var panel = new Size(640, 360);
        var content = QuietGraphLayout.ContentSize(panel);
        var p = QuietGraphLayout.Compute(Bands(7, 5, 9), panel).Positions;

        double x0 = p.Values.Min(v => v.X), x1 = p.Values.Max(v => v.X);
        double y0 = p.Values.Min(v => v.Y), y1 = p.Values.Max(v => v.Y);
        Assert.Equal(content.Width / 2, (x0 + x1) / 2, 3);
        Assert.Equal(content.Height / 2, (y0 + y1) / 2, 3);
    }

    /// <summary>
    /// AYIRT EDİCİ — bu planın en yüksek regresyon riski: <b>yerleşim artık panel boyutunun
    /// fonksiyonudur</b>. Aynı düğüm kümesi iki farklı panelde farklı pitch, farklı düğüm boyutu ve farklı
    /// konum üretir.
    ///
    /// <para><b>Eski iddia:</b> <c>GraphLayoutTests.Canvas_size_is_880_wide_and_covers_every_layer…</c>
    /// yerleşimin panelden bağımsız olduğunu pinliyordu. §2.3 ("graf HER panel boyutunda tam sığar,
    /// scrollbar yok") onu ezdi.</para>
    /// </summary>
    [Fact]
    public void The_same_graph_lays_out_differently_in_a_different_panel_because_layout_is_a_function_of_size()
    {
        var nodes = Bands(40, 40, 40, 40, 40, 40);
        var wide = QuietGraphLayout.Compute(nodes, new Size(1200, 700));
        var narrow = QuietGraphLayout.Compute(nodes, new Size(640, 360));

        Assert.True(wide.Pitch > narrow.Pitch, $"geniş panel daha büyük pitch vermeli ({wide.Pitch} > {narrow.Pitch})");
        Assert.True(wide.NodeSize > narrow.NodeSize);
        Assert.NotEqual(wide.Positions["L0.P000"], narrow.Positions["L0.P000"]);
    }

    /// <summary>Boş graf çökmez; sonuç boştur (<c>SetGraph</c>'ın 0 düğümlü yolu buradan geçer).</summary>
    [Fact]
    public void An_empty_graph_yields_an_empty_layout_instead_of_throwing()
    {
        var result = QuietGraphLayout.Compute([], new Size(640, 360));

        Assert.Empty(result.Positions);
    }

    /// <summary>Boş katman İNDEKSLERİ atlanır — bant listesi yalnız DOLU katmanlardan oluşur (JSX:264
    /// <c>groups = byLayer.filter(Boolean)</c>); aksi halde kullanılmayan bir katman indeksi grafın
    /// ortasında hayalet bir boşluk açardı.</summary>
    [Fact]
    public void An_empty_layer_index_produces_no_band_at_all()
    {
        IReadOnlyList<GraphNode> nodes =
        [
            new("A", 0, GraphStatus.Discovered),
            new("B", 5, GraphStatus.Discovered), // katman 1-4 hiç yok
        ];
        var result = QuietGraphLayout.Compute(nodes, new Size(640, 360));

        Assert.Equal(1.7 * result.Pitch, result.Positions["B"].Y - result.Positions["A"].Y, 3);
    }

    /// <summary>Katman indeksleri giriş sırasında artmasa bile bantlar KATMAN SIRASINA göre dizilir —
    /// besleme sırası bir bant sırası vaadi değildir.</summary>
    [Fact]
    public void Bands_are_ordered_by_layer_index_even_when_the_feed_is_not()
    {
        IReadOnlyList<GraphNode> nodes =
        [
            new("late", 2, GraphStatus.Discovered),
            new("early", 0, GraphStatus.Discovered),
            new("middle", 1, GraphStatus.Discovered),
        ];
        var p = QuietGraphLayout.Compute(nodes, new Size(640, 360)).Positions;

        Assert.True(p["early"].Y < p["middle"].Y);
        Assert.True(p["middle"].Y < p["late"].Y);
    }
}
