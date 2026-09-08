namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [design v1.13.2 §1.1 · §2.4] <b>Başlangıç modunun (<see cref="VisualStatus.Fresh"/>) liste satırındaki
/// çizim sabitleri</b> — TEK yer. Şeridin opaklığı ve noktanın halkası aynı kuralın iki yüzüdür ve
/// birbirinden ayrı değişemezler (kopya YASAK, CLAUDE.md).
///
/// <para><b>[DEĞİŞEN KURAL — v1.12.0]</b> v1.11.0 başlangıç modunu KESİKLİ çiziyordu: şerit tile'lanmış bir
/// <c>DrawingBrush</c> (3px dolu / 4px boş), nokta 1.5px kesikli bir çember. Ölçülen kusur: 2px'lik bir
/// şeritte kesikli desen piksel ızgarasına oturmuyor, 8px'lik çemberin kesikleri tırtıklı çiziliyordu.
/// Çözüm "orta yol": şerit DÜZ ama SOLUK, nokta ise dört EŞİT yaydan oluşan bir halka. Grafın node
/// çerçevesi kesikli KALDI (kullanıcı kararı) — orada tırtık yoktu.</para>
///
/// <para><b>[DEĞİŞEN KURAL — v1.13.2, ölçüm]</b> "Sync sonrası liste silik görünüyordu." Şeridin SOLUK
/// (<see cref="FaintOpacity"/>, eski değeri 0.5) ve halkanın dolu noktadan bir tık geride (<see cref="RingOpacity"/>,
/// eski değeri 0.85) durması kaldırıldı: ikisi de artık <c>1.0</c> — başlangıç modu satırda TAM OPAKTIR.
/// İşlem başlayınca renk geçişi olmaz — yalnız kesikli halka dolu noktaya çapraz-söner (kesikli glyph
/// başlangıç moduyla uyumlu kalır); çapraz-sönümün KENDİSİ (<see cref="CrossFadeMs"/>, geometrisi)
/// DEĞİŞMEDİ.</para>
/// </summary>
public static class StartMode
{
    /// <summary>Şeridin başlangıç modundaki opaklığı.
    /// <para><b>[DEĞİŞEN KURAL — v1.13.2, ölçüm]</b> Eski değer <c>0.5</c>'ti (soluk); gerekçe: "Sync sonrası
    /// liste silik görünüyordu." Artık <c>1.0</c> — satır Sync sonrası da tam opak. Başlangıç modu ile
    /// başlangıç-dışı hâl arasında artık bir opaklık FARKI yok, bu yüzden <see cref="CrossFadeMs"/>'lik geçiş
    /// görünmez hâle gelir; kural yine de TEK yerde (burada) durur — <c>ProjectRow.SetStripeFill</c> bu
    /// sabiti OKUR, kendi değerini TAŞIMAZ.</para></summary>
    public const double FaintOpacity = 1.0;

    /// <summary>Halkanın opaklığı.
    /// <para><b>[DEĞİŞEN KURAL — v1.13.2, ölçüm]</b> Eski değer <c>0.85</c>'ti ("dolu noktadan bir tık geride
    /// durur, çünkü henüz bir şey söylemiyor"); gerekçe: "Sync sonrası liste silik görünüyordu." Artık
    /// <c>1.0</c> — tam opak.</para></summary>
    public const double RingOpacity = 1.0;

    /// <summary>Halkanın TASARIMDAKİ yarıçapı (prototip <c>r=3.2</c>) — stroke'un MERKEZ çizgisi. Dolu
    /// noktadan (8px) küçüktür: dış kenarı 3.2 + 1.1/2 = 3.75 &lt; 4, yani çapraz-sönümde dışa taşan bir kenar
    /// görünmez.</summary>
    public const double DesignRadius = 3.2;

    /// <summary>
    /// Halkayı taşıyan <see cref="System.Windows.Shapes.Ellipse"/>'in KUTU ölçüsü — tasarım yarıçapı DEĞİL.
    ///
    /// <para><b>Neden farklı (ölçüldü):</b> SVG'de <c>stroke</c> <c>r</c> üstünde ORTALANIR ve dışa taşar; WPF
    /// bir <c>Ellipse</c>'in stroke'unu layout kutusunun İÇİNE çeker, yani çizilen merkez yarıçapı
    /// <c>(Width − StrokeThickness) / 2</c>'dir. Kutuya doğrudan 6.4 verildiğinde ekrana 2.65 yarıçaplı bir
    /// halka çıkıyordu (çevre 16.6px → dash periyodu 5.03px'e 3.3 kez sığıyor, yani yaylar EŞİTSİZ ve halka
    /// gözle görülür biçimde küçük). Kutu bu yüzden kalınlık kadar büyütülür.</para>
    /// </summary>
    public const double RingBoxSize = DesignRadius * 2 + RingThickness;

    /// <summary>Halkanın kalınlığı (prototip <c>stroke-width 1.1</c>).</summary>
    public const double RingThickness = 1.1;

    /// <summary>Yay ve boşluğun PİKSEL uzunluğu (prototip <c>stroke-dasharray "2.93 2.1"</c>): çevre
    /// (2π·3.2 ≈ 20.1px) bu periyoda (5.03px) tam DÖRT kez sığar — halka dört eşit yaydır.
    /// <para>WPF'te <c>StrokeDashArray</c> birimi <c>StrokeThickness</c> ÇARPANIDIR, bu yüzden değerler
    /// kalınlığa bölünerek verilir (<see cref="RingDash"/>).</para></summary>
    public const double DashOnPx = 2.93, DashOffPx = 2.1;

    /// <summary>Başlangıç modundan işleme geçişin süresi (prototip <c>380ms ease-standard</c>): şeridin
    /// opaklık köprüsü ile noktanın çapraz-sönümü AYNI anda, AYNI sürede olur — ikisi tek hareketin
    /// parçasıdır. <b>[v1.13.2]</b> Şeridin iki ucu artık AYNI değer (1.0) olsa da köprü kurulur (bkz.
    /// <see cref="FaintOpacity"/>) — yalnız noktanın halka↔dolu geçişi GÖRÜNÜR kalır.</summary>
    public const double CrossFadeMs = 380.0;

    /// <summary>
    /// <b>Çapraz-sönüm NE ZAMAN oynar.</b> Yalnız başlangıç modu GERÇEKTEN değiştiğinde — yani bir işlem
    /// başladığında ya da Sync başlangıç moduna geri döndüğünde. Şerit ve nokta bu tek kuralı paylaşır.
    ///
    /// <para><b>İki durum bilinçle DIŞARIDA:</b> (a) <paramref name="previous"/> <c>null</c> ise bu, kontrolün
    /// bu veri için İLK çizimidir — açılışta ya da geri dönüştürülmüş bir container'da (liste sanallaştırılmış
    /// ve <c>VirtualizationMode.Recycling</c> kullanır) sönüm oynatmak "az önce bir işlem oldu" derdi; oysa
    /// olan yalnızca satırın başka bir projeye bağlanmasıdır. (b) Mod DEĞİŞMEDİYSE oynatacak bir geçiş yoktur:
    /// koşarken statü saniyede birkaç kez itilir ve her tikte hedefi aynı olan bir animasyonu yeniden arm
    /// etmek hem boşunadır hem de opaklığı kalıcı olarak bir saatin altında bırakır — bir sonraki GERÇEK
    /// geçiş o zaman anında oturamaz.</para>
    /// </summary>
    public static bool ShouldCrossFade(bool? previous, bool current) => previous is { } was && was != current;

    /// <summary>Halkanın dash deseni, <c>StrokeThickness</c> çarpanı cinsinden.
    /// <para><b>DONDURULMUŞ</b> olmalı: paylaşılan bir <see cref="System.Windows.Freezable"/> dondurulmazsa
    /// onu ilk kuran thread'e bağlanır ve başka bir STA thread'inden okunduğunda
    /// <see cref="System.InvalidOperationException"/> fırlatır (GraphView.FrozenDash ile AYNI gerekçe).</para></summary>
    public static readonly System.Windows.Media.DoubleCollection RingDash = Frozen();

    private static System.Windows.Media.DoubleCollection Frozen()
    {
        var collection = new System.Windows.Media.DoubleCollection([DashOnPx / RingThickness, DashOffPx / RingThickness]);
        collection.Freeze();
        return collection;
    }
}
