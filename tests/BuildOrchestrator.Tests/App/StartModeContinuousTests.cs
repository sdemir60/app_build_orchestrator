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
    /// tam dört kez sığar. Kesikli bir CSS border değil, stroke — tırtık yapmayan çizim budur.</summary>
    [StaFact]
    public void The_ring_is_drawn_as_four_equal_arcs()
    {
        var dot = RealizeDot(VisualStatus.Fresh, out var window);

        double circumference = Math.PI * dot.Ring.Width;   // Width = 2r
        double period = (dot.Ring.StrokeDashArray[0] + dot.Ring.StrokeDashArray[1]) * dot.Ring.StrokeThickness;

        Assert.Equal(4.0, circumference / period, 1);
        Assert.Equal(StartMode.RingThickness, dot.Ring.StrokeThickness);
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
