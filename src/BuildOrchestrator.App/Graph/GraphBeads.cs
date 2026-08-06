using System.Windows.Media;

namespace BuildOrchestrator.App.Graph;

/// <summary>[quiet] Beads yörüngesinin geometrisi — hepsi düğüm kenarından türer.</summary>
/// <param name="Side">Yörünge karesinin kenarı (düğümün 2.8px dışında).</param>
/// <param name="CornerRadius">Yuvarlatılmış karenin köşe yarıçapı.</param>
/// <param name="Perimeter">Yörüngenin ÇEVRESİ — dash deseninin tam bölünmesi ve bir turun yolu buradan.</param>
/// <param name="DashStep">İki nokta arasındaki adım; çevreyi TAM bölecek biçimde seçilir.</param>
public readonly record struct BeadsGeometry(
    double Side,
    double CornerRadius,
    double Perimeter,
    double DashStep);

/// <summary>
/// [quiet] design v1.3.0 §2.3 "Building animasyonu — beads" — prototype/app/BuildApp.jsx satır 379-384'ün
/// SAF portu.
///
/// <para>Derlenen düğümün <b>2.8px dışında</b>, düğümle eş-merkezli yuvarlatılmış-kare bir yörüngede dolanan
/// sık amber noktalar. Nokta deseni <c>0.01 / (adım − 0.01)</c>'dir ve adım çevreyi TAM böler — ek yerinde
/// bindirme olmaz.</para>
///
/// <para><b>WPF birim notu:</b> <c>StrokeDashArray</c> ve <c>StrokeDashOffset</c> px DEĞİL
/// <c>StrokeThickness</c> ÇARPANI cinsindendir. Yörünge kalemi tam 1px olduğu için SVG'nin mutlak değerleri
/// birebir taşınır — bu, kalınlığın bir tercih değil bir SÖZLEŞME olduğu anlamına gelir
/// (<see cref="StrokeThickness"/>).</para>
/// </summary>
public static class GraphBeads
{
    /// <summary>Yörünge kutusunun düğüme eklediği pay (JSX:380 <c>pad6</c>).</summary>
    public const double PadPx = 6.0;
    /// <summary>Yörüngenin düğüm kenarından DIŞARI mesafesi (§2.3: "node'un 2.8px dışında").</summary>
    public const double OrbitGapPx = 2.8;
    /// <summary>Köşe yarıçapı tavanı (JSX:381).</summary>
    public const double MaxCornerRadius = 6.8;
    /// <summary>İki nokta arasındaki HEDEF aralık; gerçek adım çevreye tam bölünecek biçimde yuvarlanır.</summary>
    public const double BeadSpacingPx = 3.4;
    /// <summary>Yörüngedeki asgari nokta sayısı — altına inilirse desen "noktalar" değil "çizgiler" olur.</summary>
    public const int MinBeadCount = 8;
    /// <summary>Yörünge kalemi. 1'den farklı olamaz: dash birimi kalınlık çarpanıdır (bkz. tip dokümanı).</summary>
    public const double StrokeThickness = 1.0;

    /// <summary>Bir turun süresi (§2.3: "4200ms linear infinite — yavaş, sabit hız").</summary>
    public const double CycleMs = 4200.0;
    /// <summary>Building'e girişte opaklığın 1'e çıkma süresi (§2.3).</summary>
    public const double FadeInMs = 420.0;
    /// <summary>Bitişte opaklığın 0'a inme süresi (§2.3).</summary>
    public const double FadeOutMs = 640.0;
    /// <summary>§2.3: "Animasyon sınıfı bitişten sonra 700ms daha kalır → noktalar DÖNERKEN söner, donup
    /// kaybolmaz." Paylaşımlı saat son building düğüm bittikten bu kadar sonra bırakılır.</summary>
    public const double SpinAfterStopMs = 700.0;

    /// <summary>Verilen düğüm kenarı için yörünge geometrisi.</summary>
    public static BeadsGeometry For(double nodeSize)
    {
        double extent = nodeSize + PadPx * 2;
        double inset = PadPx - OrbitGapPx;
        double side = extent - inset * 2;
        double radius = Math.Min(side / 2, MaxCornerRadius);
        // Yuvarlatılmış karenin gerçek çevresi: dört kenar eksi köşelerin kestiği düzlükler artı dört çeyrek yay.
        double perimeter = 4 * side - 8 * radius + 2 * Math.PI * radius;
        // Adım çevreyi TAM bölmeli — aksi halde desen ek yerinde bindirir. JS Math.round paritesi için
        // MidpointRounding.AwayFromZero (.NET'in varsayılanı banker's rounding'dir).
        double step = perimeter /
            Math.Max(MinBeadCount, Math.Round(perimeter / BeadSpacingPx, MidpointRounding.AwayFromZero));
        return new BeadsGeometry(side, radius, perimeter, step);
    }

    /// <summary>Nokta deseni: iğne ucu kadar dolu, geri kalanı boş (JSX:384). DONMUŞ döner — desen düğümler
    /// arasında paylaşılır.</summary>
    public static DoubleCollection DashArrayFor(BeadsGeometry geometry)
    {
        var dash = new DoubleCollection([0.01, geometry.DashStep - 0.01]);
        dash.Freeze();
        return dash;
    }
}
