using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// design v1.3.0 §2.3 "Koşu yaşam döngüsü — soluk/parlak sistemi" — prototype/app/BuildApp.jsx satır
/// 421-429'un SAF portu. Sıra bağlayıcıdır: seçim &gt; koşu &gt; hover.
/// </summary>
public class GraphNodeOpacityTests
{
    private static double Op(
        GraphStatus status, GraphRunPhase phase,
        bool selection = false, bool focus = false, bool hover = false)
        => GraphNodeOpacity.Resolve(status, phase, selection, focus, hover);

    /// <summary>idle/boot/sync ve koşu bittikten sonra: TÜMÜ tam opak (§2.3).</summary>
    [Theory]
    [InlineData(GraphStatus.Discovered)]
    [InlineData(GraphStatus.Queued)]
    [InlineData(GraphStatus.Building)]
    [InlineData(GraphStatus.Succeeded)]
    [InlineData(GraphStatus.Failed)]
    [InlineData(GraphStatus.Skipped)]
    [InlineData(GraphStatus.Cycle)]
    public void Everything_is_fully_opaque_while_the_graph_is_idle(GraphStatus status)
        => Assert.Equal(1.0, Op(status, GraphRunPhase.Idle), 6);

    /// <summary>Koşu başlayınca graf soluklaşır: queued/discovered 0.13, yalnız derlenenler tam opak (§2.3).</summary>
    [Fact]
    public void A_running_graph_fades_the_untouched_nodes_to_thirteen_percent_and_keeps_the_building_ones_bright()
    {
        Assert.Equal(0.13, Op(GraphStatus.Queued, GraphRunPhase.Running), 6);
        Assert.Equal(0.13, Op(GraphStatus.Discovered, GraphRunPhase.Running), 6);
        Assert.Equal(1.0, Op(GraphStatus.Building, GraphRunPhase.Running), 6);
    }

    /// <summary>Biten proje sonuç rengine döner ve (bekleme + sönmeden sonra) 0.2'de kalır (§2.3).</summary>
    [Theory]
    [InlineData(GraphStatus.Succeeded)]
    [InlineData(GraphStatus.Failed)]
    [InlineData(GraphStatus.Cycle)]
    public void A_finished_node_settles_at_twenty_percent_while_the_run_continues(GraphStatus status)
        => Assert.Equal(0.2, Op(status, GraphRunPhase.Running), 6);

    /// <summary>
    /// [DEĞİŞEN KURAL] Atlanan düğüm "biten" değildir: koşarken kuyruktakiyle AYNI soluklukta (0.13) kalır.
    ///
    /// <para>Eski kural onu 0.2'ye koyuyordu (§2.3 atlananı da sonuç sayar). Ama düğüm kuyrukta zaten
    /// 0.13'tedir ve 0.2'ye gitmek inmek değil %54 parlamaktır: Resolve koşusu başlarken kapsam dışı kalan
    /// projeler önce 1.0'dan 0.13'e sönüp hemen ardından pre-skip ile 0.2'ye geri parlıyor, ekranda titreme
    /// üretiyordu. Aynı gerekçe daha önce atlanandan parlak beklemeyi de kaldırmıştı
    /// (<c>GraphNodeOpacity.IsSettled</c>) — bu, o kararın tamamlanmasıdır.</para>
    /// </summary>
    [Fact]
    public void A_skipped_node_stays_as_dim_as_the_queue_while_the_run_continues()
        => Assert.Equal(0.13, Op(GraphStatus.Skipped, GraphRunPhase.Running), 6);

    /// <summary>AYIRT EDİCİ: seçim koşu kararını EZER — odak kümesi tam opak, geri kalan HER ŞEY 0.1 (§2.3).
    /// Sıra ters olsaydı koşarken seçilen bir queued düğüm 0.13'te kalırdı.
    ///
    /// <para><b>[DEĞİŞTİ — v1.13.2]</b> Eski iddia burada ikinci satırda <c>Building, focus:false → 0.1</c>
    /// örneğiyle "building de dahil, odak dışı HER statü söner" diyordu. Tasarım v1.13.2 building'e bir
    /// İSTİSNA getirdi (bkz. <see cref="A_live_building_node_stays_fully_opaque_outside_focus_but_only_while_running"/>)
    /// — bu testteki örnek istisnaya TAKILMAYAN bir statüyle (Queued) değiştirildi; "odak dışı 0.1'e iner"
    /// genel iddiası geçerliliğini KORUYOR, yalnız artık building bir muafiyet taşıyor.</para>
    /// </summary>
    [Fact]
    public void A_selection_overrides_the_run_system_entirely()
    {
        Assert.Equal(1.0, Op(GraphStatus.Queued, GraphRunPhase.Running, selection: true, focus: true), 6);
        Assert.Equal(0.1, Op(GraphStatus.Queued, GraphRunPhase.Running, selection: true, focus: false), 6);
        Assert.Equal(0.1, Op(GraphStatus.Succeeded, GraphRunPhase.Idle, selection: true, focus: false), 6);
    }

    /// <summary>
    /// [DEĞİŞEN KURAL — v1.13.2] Seçim dimlemesinde bir İSTİSNA var: o an DERLENEN (building, koşu
    /// sürerken) düğüm odak kümesinde olmasa da tam opak kalır.
    ///
    /// <para><b>Eski davranış:</b> odak dışı HER statü (building dahil) 0.1'e
    /// (<see cref="GraphNodeOpacity.Unfocused"/>) iniyordu. <b>Değişme gerekçesi</b> (tasarım v1.13.2):
    /// "Beads halkası amber dönerken gövdenin 0.1'de kalması 'derlenmiyor' gibi okunuyordu." Prototip:
    /// <c>const live = running &amp;&amp; s === 'building'; … focus.has(p.name) || live ? 1 : 0.1</c>
    /// (BuildApp.jsx:545,562).</para>
    /// </summary>
    [Fact]
    public void A_live_building_node_stays_fully_opaque_outside_focus_but_only_while_running()
        => Assert.Equal(1.0, Op(GraphStatus.Building, GraphRunPhase.Running, selection: true, focus: false), 6);

    /// <summary>Kontrol: istisna YALNIZ Building'edir — aynı koşulda başka bir statü (Queued) hâlâ odak
    /// dışında 0.1'e iner.</summary>
    [Fact]
    public void A_non_building_node_still_dims_outside_focus()
        => Assert.Equal(0.1, Op(GraphStatus.Queued, GraphRunPhase.Running, selection: true, focus: false), 6);

    /// <summary>Kontrol: istisna KOŞU SÜRERKEN'e özeldir (<c>running &amp;&amp; s === 'building'</c>,
    /// BuildApp.jsx:545) — run bitmişse (idle) building bir düğüm de odak dışında yine söner. Statik bir
    /// "statü building ise muaf" kuralı olsaydı bu test KIRMIZI verirdi.</summary>
    [Fact]
    public void The_exception_requires_the_run_to_still_be_active()
        => Assert.Equal(0.1, Op(GraphStatus.Building, GraphRunPhase.Idle, selection: true, focus: false), 6);

    /// <summary>Kontrol: istisna YALNIZ <c>hasSelection</c> dalına aittir — filtre dalında building için
    /// muafiyet YOK (prototipte filtre satırında <c>live</c> istisnası yok, BuildApp.jsx:563).</summary>
    [Fact]
    public void The_filter_branch_does_not_grant_the_live_building_exception()
        => Assert.Equal(0.1, GraphNodeOpacity.Resolve(
            GraphStatus.Building, GraphRunPhase.Running,
            hasSelection: false, inFocus: false, hovered: false,
            hasFilter: true, inFilter: false), 6);

    /// <summary>Hover her şeyi ezer — soluk moddayken bile opaklık 1 (§2.3 "Hover").</summary>
    [Fact]
    public void Hover_wins_over_everything_including_the_selection_dim()
    {
        Assert.Equal(1.0, Op(GraphStatus.Queued, GraphRunPhase.Running, hover: true), 6);
        Assert.Equal(1.0, Op(GraphStatus.Queued, GraphRunPhase.Running, selection: true, focus: false, hover: true), 6);
        Assert.Equal(1.0, Op(GraphStatus.Succeeded, GraphRunPhase.Running, hover: true), 6);
    }

    /// <summary>
    /// §2.3'ün sayıları — birinin sessizce kayması bu testi düşürür.
    ///
    /// <para><b>Eski iddia:</b> <see cref="GraphNodeOpacity.HoldMs"/> 2400ms'ti (§2.3'ün verdiği sayı).
    /// Gerçek koşuda "biraz fazla kalıyor" hissi verdiği için kullanıcı kararıyla 1400ms oldu. Sönme
    /// (<see cref="GraphNodeOpacity.FadeMs"/>) DEĞİŞMEDİ — geçiş hâlâ yumuşak, yalnız bekleme kısaldı.</para>
    /// </summary>
    [Fact]
    public void The_opacity_and_timing_numbers_are_pinned_to_their_spec_values()
    {
        Assert.Equal(0.13, GraphNodeOpacity.RunDim, 6);
        Assert.Equal(0.2, GraphNodeOpacity.Finished, 6);
        Assert.Equal(0.1, GraphNodeOpacity.Unfocused, 6);
        Assert.Equal(1400.0, GraphNodeOpacity.HoldMs, 6);
        Assert.Equal(700.0, GraphNodeOpacity.FadeMs, 6);
        Assert.Equal(280.0, GraphNodeOpacity.GlideMs, 6);
    }
}
