using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Core.Incremental;

/// <summary>
/// [indexing ilerlemesi] <see cref="SourceHashCache.Prefill"/>'in dışarıya haber verdiği üç an. Gözlemci HER ZAMAN
/// <c>Prefill</c>'i çağıran thread'de çağrılır (hash işi başka thread'lerde koşar ama gözlemci oraya çıkmaz) —
/// gerçekleştirimlerin kilide ihtiyacı yoktur.
/// </summary>
public interface IPrefillObserver
{
    /// <summary>Okuma başlamadan; yalnız eksik dosya sayısı 0'dan büyükse.</summary>
    void Started(int total);

    /// <summary>İş sürerken her tikte: o ana kadar özetlenen dosya sayısı ve başlangıçtan beri geçen süre.</summary>
    void Progress(int done, int total, long elapsedMs);

    /// <summary>İş bitince. Hata ya da iptalde ÇAĞRILMAZ.</summary>
    void Finished(int total, long elapsedMs);
}

/// <summary>
/// [indexing ilerlemesi] İlk indekslemenin raporlama POLİTİKASI — Sync ve run yolu aynı kuralı kullanır, kopya YASAK.
/// Soğuk ilk indeksleme (ölçülen gerçek repoda 23 bin dosya, ~51 sn) eskiden tek satırdan sonra sessiz kalıyordu;
/// şimdi üç çıktı verir: açılış satırı (<see cref="PlanProgressLines.IndexingSources"/>), yaklaşık her
/// <see cref="HeartbeatIntervalMs"/>'de bir sayılı "hâlâ çalışıyor" satırı ve süreyi söyleyen kapanış satırı;
/// ayrıca sayı her değiştiğinde yapısal <c>progress</c> geri çağrısı (şerit/adım bunu tüketir).
///
/// <para><b>Eşik:</b> <see cref="SourceHashCache.NoisyPrefillThreshold"/>'ın altındaki indeksleme tümüyle sessizdir
/// (<see cref="Reported"/> <c>false</c>) — her koşuda değişen birkaç dosya için satır yazmak gürültü olurdu. Tüketici
/// <see cref="Reported"/>'a bakarak "kullanıcıya indeksleme gösterildi mi" sorusunu cevaplar.</para>
///
/// <para><b>Kilit yok:</b> gözlemci <c>Prefill</c>'in çağıran thread'inde çağrılır (bkz. <see cref="IPrefillObserver"/>).</para>
/// </summary>
public sealed class IndexingReport(Action<string> line, Action<int, int>? progress = null) : IPrefillObserver
{
    /// <summary>Kalp atışı satırlarının aralığı (ms) — ilk sınır da budur.</summary>
    public const long HeartbeatIntervalMs = 10_000;

    private long _nextHeartbeatMs = HeartbeatIntervalMs;
    private int _reportedDone;

    /// <summary><see cref="Started"/> eşiği geçti: kullanıcıya indeksleme gösterildi (kapanış satırı da yazılacak).</summary>
    public bool Reported { get; private set; }

    public void Started(int total)
    {
        if (total < SourceHashCache.NoisyPrefillThreshold) return;

        Reported = true;
        line(PlanProgressLines.IndexingSources(total));
        progress?.Invoke(0, total);
    }

    public void Progress(int done, int total, long elapsedMs)
    {
        if (!Reported) return;

        if (done != _reportedDone)
        {
            _reportedDone = done;
            progress?.Invoke(done, total);
        }

        // Tik 500 ms'de bir gelir ama bir tik uzun sürebilir (ör. ara kayıt): sınır birden fazla atlanmış olsa da TEK
        // satır yazılır ve sınır elapsed'in üstündeki ilk katına taşınır — satırlar birikmez.
        if (elapsedMs >= _nextHeartbeatMs)
        {
            line(PlanProgressLines.IndexingHeartbeat(done, total, elapsedMs));
            _nextHeartbeatMs = (elapsedMs / HeartbeatIntervalMs + 1) * HeartbeatIntervalMs;
        }
    }

    public void Finished(int total, long elapsedMs)
    {
        if (!Reported) return;

        // Son tik işin bitmesinden önce geldiyse sayaç total'e ulaşmamış olabilir: bitişte tamamlanır.
        if (_reportedDone != total)
        {
            _reportedDone = total;
            progress?.Invoke(total, total);
        }
        line(PlanProgressLines.IndexedSources(total, elapsedMs));
    }
}
