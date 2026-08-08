using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.App.Graph;

/// <summary>[quiet] Grafın koşu fazı — opaklık sisteminin tek girdisi (§2.3 "Koşu yaşam döngüsü").</summary>
public enum GraphRunPhase
{
    /// <summary>idle/boot/sync ve koşu bittikten sonra (done/stopped): TÜMÜ tam opak.</summary>
    Idle,
    /// <summary>Koşu sürüyor: graf soluklaşır, yalnız derlenenler parlaktır.</summary>
    Running,
}

/// <summary>
/// [quiet] design v1.3.0 §2.3 "Koşu yaşam döngüsü — soluk/parlak sistemi" — prototype/app/BuildApp.jsx
/// satır 421-429'un SAF portu.
///
/// <para><b>Sıra bağlayıcıdır:</b> seçim &gt; koşu &gt; hover. Seçim varken koşu kararı hiç sorulmaz (odak
/// kümesi tam opak, geri kalan 0.1); hover ise en sonda gelir ve her şeyi ezer — soluk moddayken bile
/// imlecin altındaki düğüm okunur.</para>
///
/// <para><b>Hold-fade burada DEĞİL:</b> "biten proje 2400ms parlak kalır, sonra 700ms'de söner" bir
/// ZAMANLAMA kuralıdır, bir değer kuralı değil. Bu sınıf yalnız NİHAİ değeri verir
/// (<see cref="Finished"/>); bekleme ve sönme süreleri <see cref="HoldMs"/>/<see cref="FadeMs"/> olarak
/// burada durur ve görsel tarafta <c>BeginTime</c>'lı tek atımlık bir animasyona çevrilir (CSS'teki
/// <c>transition: opacity 700ms ease-standard 2400ms</c> hilesinin WPF karşılığı).</para>
/// </summary>
public static class GraphNodeOpacity
{
    /// <summary>Koşarken henüz sırası gelmemiş düğüm (queued/discovered) — §2.3.</summary>
    public const double RunDim = 0.13;
    /// <summary>Koşarken bitmiş düğümün SÖNME sonrası değeri — §2.3.</summary>
    public const double Finished = 0.2;
    /// <summary>Seçim varken odak kümesi DIŞINDA kalan her şey — §2.3.</summary>
    public const double Unfocused = 0.1;
    public const double Full = 1.0;

    /// <summary>Biten düğümün sonuç renginde PARLAK kaldığı süre.
    ///
    /// <para><b>§2.3'ün sayısı 2400ms'ti; kullanıcı kararıyla 1400ms.</b> Gerçek koşuda 2.4 saniye "fazla
    /// kalıyor" hissi verdi. Sönme süresi (<see cref="FadeMs"/>) DEĞİŞMEDİ — geçiş hâlâ yumuşak, yalnız
    /// bekleme kısaldı.</para></summary>
    public const double HoldMs = 1400.0;
    /// <summary>Bekleme bitince <see cref="Finished"/>'a sönme süresi (§2.3).</summary>
    public const double FadeMs = 700.0;

    /// <summary>Normal opaklık geçişi (§2.3: "opaklık geçişi 280ms — hold-fade hariç").</summary>
    public const double GlideMs = 280.0;
    // [quiet · ÖLÇÜLMÜŞ SAPMA] §2.3'ün "renk geçişi 380ms ease-standard" kuralı UYGULANMADI ve bu yüzden
    // burada bir TintMs sabiti de yok. Gerekçe GraphView.ApplyNodeStatus'ta ölçümüyle birlikte yazılıdır:
    // WPF'te fırça geçişi düğüm başına üç yerel SolidColorBrush + üç ColorAnimation demek ve 177 projenin
    // aynı tick'te statü değiştirdiği durumda tick 11 ms'den 51 ms'ye çıkıp UI olay bütçesini aşıyor.

    /// <summary>
    /// Düğüm bir İŞ sonucuna oturdu mu — hold-fade'i (parlak bekle, sonra sön) doğuran geçişin hedef kümesi.
    ///
    /// <para><b>Eski kural "building'den çıkış"tı.</b> Yeterli değil: statü itişi 200ms'de bir gelir ve hızlı
    /// bir proje <c>queued → succeeded</c> diye tek tick'te görünebilir; o düğüm parlak anını hiç almazdı.
    /// Kural artık nasıl gelindiğine değil NEREYE gelindiğine bakar.</para>
    ///
    /// <para><b><see cref="GraphStatus.Skipped"/> BURADA DEĞİLDİR ve bu bilinçlidir.</b> Bir ara sürümde
    /// atlanan düğüme de parlak bekleme + amber yörünge çakışı verilmişti; gerekçe "atlanmak da bir karardır,
    /// işlem gördüğü belli olsun"du. Gözle bakıldığında yanlış olduğu görüldü: sessiz grafın anlattığı şey
    /// "ne oldu" değil "ne DEĞİŞTİ"dir ve atlanmak tam olarak hiçbir şeyin değişmemesidir. Üstelik bu araçta
    /// en sık koşu "hiçbir şey değişmedi" koşusudur — o durumda grafın tamamı her Derle'de saniyelerce
    /// kıpırdardı. Atlanan proje artık sonuç renginde sessizce yerine oturur (düz 280ms geçiş); bilgi zaten
    /// liste satırındaki will-build noktasında, şerit sayacında ve konsolda vardır.</para>
    /// </summary>
    public static bool IsSettled(GraphStatus status) => status
        is GraphStatus.Succeeded or GraphStatus.Failed or GraphStatus.Cycle;

    /// <summary>
    /// Bir düğümün NİHAİ opaklığı. Sıra prototiple birebirdir (BuildApp.jsx:421-429).
    /// </summary>
    /// <param name="status">Düğümün statüsü.</param>
    /// <param name="phase">Koşu sürüyor mu.</param>
    /// <param name="hasSelection">Grafta herhangi bir seçim var mı.</param>
    /// <param name="inFocus">Bu düğüm odak kümesinde mi (seçili + doğrudan komşuları).</param>
    /// <param name="hovered">İmleç bu düğümün üstünde mi.</param>
    public static double Resolve(
        GraphStatus status, GraphRunPhase phase, bool hasSelection, bool inFocus, bool hovered)
    {
        double opacity = Full;
        if (hasSelection)
        {
            opacity = inFocus ? Full : Unfocused;
        }
        else if (phase == GraphRunPhase.Running)
        {
            opacity = status switch
            {
                GraphStatus.Building => Full,
                GraphStatus.Queued or GraphStatus.Discovered => RunDim,
                _ => Finished,
            };
        }
        return hovered ? Full : opacity;
    }
}
