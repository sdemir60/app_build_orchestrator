using System.Windows.Media;

namespace BuildOrchestrator.App.Graph;

/// <summary>[quiet] Beads yörüngesinin geometrisi — düğüm kenarı VE hücre pitch'inden türer.</summary>
/// <param name="Side">Yörünge YOLUNUN (kalem merkez çizgisinin) kenarı — düğümün <c>bgap</c> kadar dışında.</param>
/// <param name="CornerRadius">Yuvarlatılmış karenin köşe yarıçapı.</param>
/// <param name="Perimeter">Yörüngenin ÇEVRESİ — dash deseninin tam bölünmesi ve bir turun yolu buradan.</param>
/// <param name="DashStep">İki nokta arasındaki adım; çevreyi TAM bölecek biçimde seçilir.</param>
public readonly record struct BeadsGeometry(
    double Side,
    double CornerRadius,
    double Perimeter,
    double DashStep);

/// <summary>
/// [quiet] design v1.18.0 §9 "Beads bir tık kalın + hücreye kelepçeli yörünge" — prototype/app/BuildApp.jsx
/// satır ~517-525/622-631'in SAF portu.
///
/// <para>Derlenen düğümün, düğümle eş-merkezli yuvarlatılmış-kare bir yörüngede dolanan sık amber noktaları.
/// Yörüngenin düğümden DIŞARI mesafesi (<c>bgap</c>) SABİT DEĞİLDİR — hücrenin <b>pitch</b>'inden geriye
/// çözülür ve <see cref="MinOrbitGapPx"/>/<see cref="MaxOrbitGapPx"/>'e kelepçelenir: dar pitch'te (yoğun
/// graf) yörünge düğüme yaklaşır, bol pitch'te eski sabit değerde (2.8) tavanlanır. <b>Eski kural sabit
/// 2.8px'ti</b> — v1.18.0 yörüngeyi hücreye KELEPÇELEDİ ki dar pitch'te komşu düğümlerin mürekkepleri
/// üst üste binmesin. Nokta deseni <c>0.01 / (adım − 0.01)</c>'dir ve adım çevreyi TAM böler — ek yerinde
/// bindirme olmaz.</para>
///
/// <para><b>WPF birim notu:</b> <c>StrokeDashArray</c> ve <c>StrokeDashOffset</c> px DEĞİL
/// <see cref="StrokeThickness"/> ÇARPANI cinsindendir. <b>Eski kalınlık 1'di</b> ("1'den farklı olamaz"
/// sözleşmesi geçerliydi, SVG'nin mutlak değerleri birebir taşınıyordu); v1.18.0 kalınlığı 1.6'ya çıkardı —
/// artık desen VE saat kalınlığa BÖLÜNEREK uygulanır (<see cref="DashArrayFor"/>,
/// <c>GraphView.EnsureBeadsClock</c>).</para>
/// </summary>
public static class GraphBeads
{
    /// <summary>Yörüngenin düğüm kenarından DIŞARI mesafesinin TABANI (v1.18.0).</summary>
    public const double MinOrbitGapPx = 0.8;
    /// <summary>...TAVANI (v1.18.0). <b>Eski değer sabitti</b> (§2.3: "node'un 2.8px dışında") — artık yalnız
    /// bol pitch'te ulaşılan bir tavan.</summary>
    public const double MaxOrbitGapPx = 2.8;
    /// <summary>Komşu hücrelerin yörünge mürekkepleri arasında HEDEFLENEN asgari boşluk — <c>bgap</c> formülü
    /// bunu (kalem kalınlığıyla birlikte) pitch'ten geriye çözer (bkz. <see cref="For"/>).</summary>
    public const double NeighborGapPx = 2.0;
    /// <summary>Köşe yarıçapı tavanı (JSX:381).</summary>
    public const double MaxCornerRadius = 6.8;
    /// <summary>İki nokta arasındaki HEDEF aralık; gerçek adım çevreye tam bölünecek biçimde yuvarlanır.</summary>
    public const double BeadSpacingPx = 3.2;
    /// <summary>Yörüngedeki asgari nokta sayısı — altına inilirse desen "noktalar" değil "çizgiler" olur.</summary>
    public const int MinBeadCount = 8;
    /// <summary>Yörünge kalemi. <b>Eski değer 1'di</b> ("1'den farklı olamaz" sözleşmesi geçerliydi) —
    /// v1.18.0 "bir tık kalın" istedi. Dash birimi hâlâ bu kalınlığın ÇARPANIdır, yalnız artık 1 olmadığı
    /// için desen/saat BUNA BÖLÜNEREK uygulanır (bkz. <see cref="DashArrayFor"/>).</summary>
    public const double StrokeThickness = 1.6;

    /// <summary>Bir turun süresi. <b>Eski değer 4200ms'ti</b> — v1.18.0 yörüngeyi hızlandırdı.</summary>
    public const double CycleMs = 2400.0;
    /// <summary>Building'e girişte opaklığın 1'e çıkma süresi (§2.3).</summary>
    public const double FadeInMs = 420.0;
    /// <summary>Bitişte opaklığın 0'a inme süresi (§2.3).</summary>
    public const double FadeOutMs = 640.0;
    /// <summary>§2.3: "Animasyon sınıfı bitişten sonra 700ms daha kalır → noktalar DÖNERKEN söner, donup
    /// kaybolmaz." Paylaşımlı saat son building düğüm bittikten bu kadar sonra bırakılır.</summary>
    public const double SpinAfterStopMs = 700.0;

    /// <summary>Verilen düğüm kenarı VE hücre pitch'i için yörünge geometrisi.
    ///
    /// <para><c>bgap</c> pitch'ten GERİYE ÇÖZÜLÜR: <c>(pitch − size − StrokeThickness − NeighborGapPx) / 2</c>,
    /// <see cref="MinOrbitGapPx"/>/<see cref="MaxOrbitGapPx"/>'e kelepçeli. Kelepçe TABANA inmediği sürece
    /// (yani sonuç clamp'lenmediği sürece) komşu hücrelerin yörünge mürekkepleri arasında tam
    /// <see cref="NeighborGapPx"/> boşluk kalır — cebirsel özdeşlik:
    /// <c>pitch − size − 2·(bgap + StrokeThickness/2) = NeighborGapPx</c>.</para>
    /// </summary>
    public static BeadsGeometry For(double nodeSize, double pitch)
    {
        double gap = Math.Clamp(
            (pitch - nodeSize - StrokeThickness - NeighborGapPx) / 2, MinOrbitGapPx, MaxOrbitGapPx);
        double side = nodeSize + gap * 2;
        double radius = Math.Min(side / 2, MaxCornerRadius);
        // Yuvarlatılmış karenin gerçek çevresi: dört kenar eksi köşelerin kestiği düzlükler artı dört çeyrek yay.
        double perimeter = 4 * side - 8 * radius + 2 * Math.PI * radius;
        // Adım çevreyi TAM bölmeli — aksi halde desen ek yerinde bindirir. JS Math.round paritesi için
        // MidpointRounding.AwayFromZero (.NET'in varsayılanı banker's rounding'dir).
        double step = perimeter /
            Math.Max(MinBeadCount, Math.Round(perimeter / BeadSpacingPx, MidpointRounding.AwayFromZero));
        return new BeadsGeometry(side, radius, perimeter, step);
    }

    /// <summary>Nokta deseni: iğne ucu kadar dolu, geri kalanı boş (JSX:384), <see cref="StrokeThickness"/>'a
    /// BÖLÜNMÜŞ — WPF <c>StrokeDashArray</c> birimi px değil kalınlık ÇARPANIdır. <b>Eski kalınlık 1'di</b>,
    /// bu yüzden bölme eskiden etkisizdi; 1.6 olunca bölmeden desen 1.6× uzar. DONMUŞ döner — desen düğümler
    /// arasında paylaşılır.</summary>
    public static DoubleCollection DashArrayFor(BeadsGeometry geometry)
    {
        var dash = new DoubleCollection([0.01 / StrokeThickness, (geometry.DashStep - 0.01) / StrokeThickness]);
        dash.Freeze();
        return dash;
    }
}
