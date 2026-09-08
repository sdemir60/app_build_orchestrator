namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [design v1.12.0 §1.1 · §2.4] <b>Başlangıç modunun (<see cref="VisualStatus.Fresh"/>) liste satırındaki
/// çizim sabitleri</b> — TEK yer. Şeridin soluklugu ve noktanın halkası aynı kuralın iki yüzüdür ve
/// birbirinden ayrı değişemezler (kopya YASAK, CLAUDE.md).
///
/// <para><b>[DEĞİŞEN KURAL]</b> v1.11.0 başlangıç modunu KESİKLİ çiziyordu: şerit tile'lanmış bir
/// <c>DrawingBrush</c> (3px dolu / 4px boş), nokta 1.5px kesikli bir çember. Ölçülen kusur: 2px'lik bir
/// şeritte kesikli desen piksel ızgarasına oturmuyor, 8px'lik çemberin kesikleri tırtıklı çiziliyordu.
/// Çözüm "orta yol": şerit DÜZ ama SOLUK, nokta ise dört EŞİT yaydan oluşan bir halka. Grafın node
/// çerçevesi kesikli KALDI (kullanıcı kararı) — orada tırtık yoktu.</para>
/// </summary>
public static class StartMode
{
    /// <summary>Şeridin başlangıç modundaki opaklığı; işlem başlayınca <see cref="CrossFadeMs"/>'de 1'e çıkar.</summary>
    public const double FaintOpacity = 0.5;

    /// <summary>Halkanın opaklığı — dolu noktadan bir tık geride durur, çünkü henüz bir şey söylemiyor.</summary>
    public const double RingOpacity = 0.85;

    /// <summary>Halkanın çapı (prototip <c>r=3.2</c>): dolu noktadan (8px) KÜÇÜKTÜR, stroke'uyla birlikte
    /// bile onun içinde kalır — çapraz-sönümde dışa taşan bir kenar görünmez.</summary>
    public const double RingDiameter = 6.4;

    /// <summary>Halkanın kalınlığı (prototip <c>stroke-width 1.1</c>).</summary>
    public const double RingThickness = 1.1;

    /// <summary>Yay ve boşluğun PİKSEL uzunluğu (prototip <c>stroke-dasharray "2.93 2.1"</c>): çevre
    /// (2π·3.2 ≈ 20.1px) bu periyoda (5.03px) tam DÖRT kez sığar — halka dört eşit yaydır.
    /// <para>WPF'te <c>StrokeDashArray</c> birimi <c>StrokeThickness</c> ÇARPANIDIR, bu yüzden değerler
    /// kalınlığa bölünerek verilir (<see cref="RingDash"/>).</para></summary>
    public const double DashOnPx = 2.93, DashOffPx = 2.1;

    /// <summary>Başlangıç modundan işleme geçişin süresi (prototip <c>380ms ease-standard</c>): şeridin
    /// soluklugu ile noktanın çapraz-sönümü AYNI anda, AYNI sürede olur — ikisi tek hareketin parçasıdır.</summary>
    public const double CrossFadeMs = 380.0;

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
