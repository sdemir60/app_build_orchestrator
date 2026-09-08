using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.12.0 §1.1 · §2.4 · §5] <b>Başlangıç modu satırda KESİKSİZ çizilir.</b>
///
/// <para><b>Ölçülen kusur:</b> kesikli şerit (<c>repeating-linear-gradient</c> / WPF'te tile'lanmış
/// <c>DrawingBrush</c>) 1px hairline'da piksel ızgarasına oturmuyor, 8px'lik kesikli nokta çemberi tırtıklı
/// çiziliyordu.</para>
///
/// <para><b>[DEĞİŞEN KURAL]</b> Eski kural: "kesikli çizilen TEK durum başlangıç modudur — şerit, nokta ve
/// node çerçevesi orada kesiklidir" (v1.11.0 §2.3). Yeni kural: <b>şerit DÜZ ve SOLUK</b> (opaklık 0.5,
/// işlem başlayınca 380ms'de 1) · <b>nokta 4 yaylı bir HALKA</b> (kesikli çember değil) ve işlem başlayınca
/// dolu noktaya <b>çapraz-söner</b> — eleman ve boyut sabit kalır, hiza kaymaz. <b>Node'un kesikli çerçevesi
/// KORUNDU</b> (kullanıcı kararı): tırtık şikâyeti yalnız şerit ve noktaya aitti, SVG stroke ile çizilen
/// çember tırtık yapmıyor.</para>
///
/// <para>Statü glyph'inin kesikli çemberi de DEĞİŞMEDİ — o da SVG stroke'tur ve building spinner'ı onun dönen
/// hâlidir.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class StartModeContinuousTests
{
    // Motion KAPALI: opaklık hedefe ANINDA oturur, böylece ölçülen şey animasyonun ara karesi değil KURALDIR.
    private static (ProjectRow row, ProjectRowViewModel vm, Window window) Realize(bool fresh)
    {
        var host = DsResources.NewHost();
        var vm = new ProjectRowViewModel("a", "A", ProjectRowState.Pending) { Fresh = fresh };
        var row = new ProjectRow { DataContext = vm, AnimationsEnabledProvider = () => false };
        return (row, vm, DsResources.Realize(host, row));
    }

    private static StatusDot RealizeDot(VisualStatus state, out Window window)
    {
        var dot = new StatusDot { State = state, AnimationsEnabledProvider = () => false };
        window = DsResources.Realize(DsResources.NewHost(), dot);
        return dot;
    }

    // ------------------------------------------------------------------ şerit

    /// <summary>Şerit her durumda DÜZ bir token fırçasıyla dolar; başlangıç modunu SOLUKLUK anlatır.</summary>
    [StaFact]
    public void The_stripe_is_solid_and_only_faint_in_the_start_mode()
    {
        var (row, vm, window) = Realize(fresh: true);

        Assert.IsType<SolidColorBrush>(row.Stripe.Fill);            // tile'lanmış kesikli fırça YOK
        Assert.Equal(StartMode.FaintOpacity, row.Stripe.Opacity);

        vm.Fresh = false;                                           // bir işlem başladı (_neutralize)
        Assert.IsType<SolidColorBrush>(row.Stripe.Fill);
        Assert.Equal(1.0, row.Stripe.Opacity);
        GC.KeepAlive(window);
    }

    // ------------------------------------------------------------------ nokta

    /// <summary>Nokta TEK bir elemandır: halka ve dolu daire üst üste durur, aralarında yalnız opaklık
    /// değişir. Boyutlar sabittir — bu, "hiza kaymaz, titreme yok" iddiasının ta kendisidir.</summary>
    [StaFact]
    public void The_dot_cross_fades_between_a_four_arc_ring_and_a_filled_circle()
    {
        var dot = RealizeDot(VisualStatus.Fresh, out var window);

        double ringWidth = dot.Ring.Width, fillWidth = dot.Fill.Width;

        // Başlangıç modu: halka görünür, dolu daire yok.
        Assert.Equal(StartMode.RingOpacity, dot.Ring.Opacity);
        Assert.Equal(0.0, dot.Fill.Opacity);

        dot.State = VisualStatus.Discovered;
        dot.UpdateLayout();

        // İşlem başladı: çapraz-sönüm — halka gitti, dolu daire geldi. Ölçüler DEĞİŞMEDİ.
        Assert.Equal(0.0, dot.Ring.Opacity);
        Assert.Equal(1.0, dot.Fill.Opacity);
        Assert.Equal(ringWidth, dot.Ring.Width);
        Assert.Equal(fillWidth, dot.Fill.Width);
        GC.KeepAlive(window);
    }

    /// <summary>Halka 4 EŞİT yaydan oluşur: r=3.2 çemberin çevresi (≈20.1px) dash periyoduna (2.93+2.1=5.03px)
    /// tam dört kez sığar. Kesikli bir CSS border değil, stroke — tırtık yapmayan çizim budur.
    ///
    /// <para><b>Ölçüm ÇİZİLEN geometriden yapılır, layout kutusundan DEĞİL.</b> WPF bir <see cref="Ellipse"/>'in
    /// stroke'unu kutunun İÇİNE çeker (merkez yarıçapı = (Width − StrokeThickness) / 2), yani kutu genişliğinden
    /// hesaplanan bir çevre gerçekte çizilenle uyuşmaz. Testin ilk hâli tam bu yüzden VACUOUS'tu: 6.4px'lik
    /// kutuda 4 yay ölçüyor, ekranda ise 2.65 yarıçaplı ve 3.31 yaylı bir halka duruyordu.</para></summary>
    [StaFact]
    public void The_ring_is_drawn_as_four_equal_arcs()
    {
        var dot = RealizeDot(VisualStatus.Fresh, out var window);

        double drawnRadius = dot.Ring.RenderedGeometry.Bounds.Width / 2;   // stroke'un MERKEZ çizgisi
        double circumference = Math.PI * drawnRadius * 2;
        double period = (dot.Ring.StrokeDashArray[0] + dot.Ring.StrokeDashArray[1]) * dot.Ring.StrokeThickness;

        Assert.Equal(StartMode.DesignRadius, drawnRadius, 3);
        Assert.Equal(4.0, circumference / period, 1);
        Assert.Equal(StartMode.RingThickness, dot.Ring.StrokeThickness);
        // Halka dolu noktanın İÇİNDE kalır: dış kenarı (r + kalınlığın yarısı) 4px'i geçmez.
        Assert.True(drawnRadius + dot.Ring.StrokeThickness / 2 <= dot.Fill.Width / 2);
        GC.KeepAlive(window);
    }

    // ------------------------------------------------------------------ geçiş NE ZAMAN oynar

    /// <summary>
    /// <b>Geri dönüştürülen bir satır YENİ verisinin hâline ANINDA oturur.</b> Liste sanallaştırılmıştır ve
    /// container'lar yeniden kullanılır (<see cref="FixedHeightVirtualizingPanel"/>,
    /// <c>VirtualizationMode.Recycling</c>): kaydırırken aynı <see cref="ProjectRow"/> kontrolü sırayla farklı
    /// projelere bağlanır. Çapraz-sönüm bir DURUM DEĞİŞİMİNİ anlatır ("işlem başladı"); veri değişimini
    /// anlatmaz — orada oynarsa liste kaydırıldıkça satırlar birbirine dönüşüyor gibi görünür.
    /// </summary>
    [StaFact]
    public void A_recycled_row_lands_on_its_new_data_without_animating()
    {
        var (row, _, window) = Realize(fresh: true);
        var stripe = (System.Windows.Shapes.Shape)row.FindName("PART_Stripe");
        row.AnimationsEnabledProvider = () => true;    // ön-koşul: motion AÇIK, yine de oynamamalı

        row.DataContext = new ProjectRowViewModel("b", "B", ProjectRowState.Succeeded) { Fresh = false };
        row.UpdateLayout();

        Assert.False(stripe.HasAnimatedProperties, "geri dönüştürülen satırda şerit animasyonu kurulmamalı");
        Assert.False(row.Dot.Ring.HasAnimatedProperties, "geri dönüştürülen satırda halka animasyonu kurulmamalı");
        Assert.False(row.Dot.Fill.HasAnimatedProperties, "geri dönüştürülen satırda nokta animasyonu kurulmamalı");
        Assert.Equal(1.0, stripe.Opacity);              // yeni veri başlangıç modunda DEĞİL
        Assert.Equal(0.0, row.Dot.Ring.Opacity);
        Assert.Equal(1.0, row.Dot.Fill.Opacity);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// <b>Statü tikleri çapraz-sönümü YENİDEN kurmaz.</b> Koşarken statü saniyede birkaç kez itilir; başlangıç
    /// modu bu tiklerde değişmez, dolayısıyla oynatacak bir geçiş de yoktur. Hedefi değişmeyen bir animasyonu
    /// her tikte yeniden arm etmek hem boşunadır hem de opaklığı kalıcı olarak bir saatin altında bırakır —
    /// bir sonraki gerçek geçiş o zaman anında oturamaz.
    /// </summary>
    [StaFact]
    public void A_status_tick_never_re_arms_the_cross_fade()
    {
        var (row, vm, window) = Realize(fresh: false);
        var stripe = (System.Windows.Shapes.Shape)row.FindName("PART_Stripe");
        row.AnimationsEnabledProvider = () => true;

        vm.State = ProjectRowState.Started;
        vm.State = ProjectRowState.Succeeded;
        row.UpdateLayout();

        Assert.False(row.Dot.Ring.HasAnimatedProperties, "statü tiki halkada animasyon kurmamalı");
        Assert.False(row.Dot.Fill.HasAnimatedProperties, "statü tiki noktada animasyon kurmamalı");
        Assert.False(stripe.HasAnimatedProperties, "statü tiki şeritte animasyon kurmamalı");
        GC.KeepAlive(window);
    }

    /// <summary>Buna karşılık GERÇEK geçiş — başlangıç modunun düşmesi — motion açıkken oynar. Yukarıdaki iki
    /// testin vacuous olmadığının kanıtı budur.</summary>
    [StaFact]
    public void Leaving_the_start_mode_really_does_cross_fade()
    {
        var (row, vm, window) = Realize(fresh: true);
        var stripe = (System.Windows.Shapes.Shape)row.FindName("PART_Stripe");
        row.AnimationsEnabledProvider = () => true;

        vm.Fresh = false;                               // bir işlem başladı
        row.UpdateLayout();

        Assert.True(row.Dot.Ring.HasAnimatedProperties, "başlangıç modundan çıkarken halka SÖNMELİ");
        Assert.True(row.Dot.Fill.HasAnimatedProperties, "başlangıç modundan çıkarken nokta YANMALI");
        Assert.True(stripe.HasAnimatedProperties, "başlangıç modundan çıkarken şerit tam opaklığa ÇIKMALI");
        GC.KeepAlive(window);
    }

    /// <summary>Dolu daire statü rengini taşır (şeritle AYNI tablo) — halka her zaman nötr gridir, çünkü
    /// başlangıç modunun rengi yoktur.</summary>
    [StaFact]
    public void The_filled_circle_carries_the_status_colour_while_the_ring_stays_neutral()
    {
        var dot = RealizeDot(VisualStatus.Succeeded, out var window);

        Assert.Equal(DsResources.TokenColor(dot, "Brush.StatusSuccess"), DsResources.ColorOf(dot.Fill.Fill));
        Assert.Equal(DsResources.TokenColor(dot, "Brush.StatusSkippedBorder"), DsResources.ColorOf(dot.Ring.Stroke));
        GC.KeepAlive(window);
    }
}
