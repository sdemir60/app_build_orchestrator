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

    /// <summary>[quiet] ATLANAN projenin parlak bekleme süresi — derlenenin çok altında.
    ///
    /// <para>Kullanıcı kararı: "atlandığı için çok hafif, görülüp geçilecek seviyede kısa tutabiliriz."
    /// Atlanma bir işlemdir ama bir DERLEME değildir; anlatımı da o kadar yer kaplamamalı.</para></summary>
    public const double SkipHoldMs = 520.0;
    /// <summary>Aynı tick'te atlanan iki proje arasındaki gecikme.
    ///
    /// <para><b>Neden var:</b> atlanan projeler derleme kuyruğuna hiç girmez — hiçbir şeyin değişmediği bir
    /// koşuda planlayıcı hepsini TEK tick'te işaretler. Gecikmesiz hâlde yüzlerce düğüm aynı anda yanıp aynı
    /// anda sönüyor ve "sırası geldi, bakıldı, geçildi" yerine tek bir flaş gibi okunuyordu. Adım, dalgayı
    /// build-order boyunca yürütür.</para></summary>
    public const double SkipStepMs = 45.0;
    /// <summary>Atlanma dalgasının tavanı — 177 projelik bir koşuda dalga bu süreye sığar, sonrakiler
    /// birlikte gelir (açılış dalgasının <c>RevealDelayCapMs</c> ile aynı gerekçesi).</summary>
    public const double SkipStaggerCapMs = 900.0;
    /// <summary>Normal opaklık geçişi (§2.3: "opaklık geçişi 280ms — hold-fade hariç").</summary>
    public const double GlideMs = 280.0;
    // [quiet · ÖLÇÜLMÜŞ SAPMA] §2.3'ün "renk geçişi 380ms ease-standard" kuralı UYGULANMADI ve bu yüzden
    // burada bir TintMs sabiti de yok. Gerekçe GraphView.ApplyNodeStatus'ta ölçümüyle birlikte yazılıdır:
    // WPF'te fırça geçişi düğüm başına üç yerel SolidColorBrush + üç ColorAnimation demek ve 177 projenin
    // aynı tick'te statü değiştirdiği durumda tick 11 ms'den 51 ms'ye çıkıp UI olay bütçesini aşıyor.

    /// <summary>
    /// Düğüm bir SONUCA oturdu mu — hold-fade'i (parlak bekle, sonra sön) doğuran geçişin hedef kümesi.
    ///
    /// <para><b>Eski kural "building'den çıkış"tı.</b> Atlanan proje hiç building olmaz: incremental kontrol
    /// onu güncel bulur ve doğrudan <see cref="GraphStatus.Skipped"/>'a geçer. Eski kuralla o düğüm tek bir
    /// parlak an bile almıyor, 0.13'ten 0.2'ye sessizce kayıyordu — koşu sonunda "bunlar hiç işlem görmedi"
    /// hissi buradan geliyordu. Kural artık statünün KENDİSİNE bakar: nasıl gelindiği değil, bir sonuca
    /// gelinmiş olması önemlidir.</para>
    /// </summary>
    public static bool IsSettled(GraphStatus status) => status
        is GraphStatus.Succeeded or GraphStatus.Failed or GraphStatus.Skipped or GraphStatus.Cycle;

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
