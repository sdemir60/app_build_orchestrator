using System.Globalization;
using System.Threading;
using BuildOrchestrator.Core.ProcessControl;

namespace BuildOrchestrator.Core.MsBuild;

/// <summary>
/// [T8] IMsBuildInvoker'ı saran ince bir decorator: yalnız <see cref="CopyContention"/> satırı GÖRÜLMÜŞ VE
/// invoke başarısız olmuş denemeleri, enjekte edilmiş backoff ile yeniden dener (177 proje paralel post-build
/// copy event'i çarpışabilir — bkz. brief). Gerçek bir derleme hatası ("error CS0103" gibi) TEK denemede
/// kalır; retry etmek 177 proje ölçeğinde build süresini anlamsızca katlardı. <c>delay</c> ENJEKTE edilir
/// [D8] — üretimde <c>Task.Delay</c>, testte gerçek zaman beklemeyen sahte bir callback. Bu sınıf dosya
/// sistemine DOKUNMAZ/kopyalamaz [§4]; retry yalnız projenin KENDİ build/copy'sini yeniden çalıştırır ve
/// isteği (<see cref="MsBuildInvokeRequest"/>) HİÇBİR şekilde değiştirmez [§3.4].
/// <para>[T20-b/P3] Bir contention penceresi AYNI ZAMANDA copy fazının CPU cap altında starve olduğu tek
/// gözlemlenebilir andır (copy, child'ın içindedir — "başlıyor" sinyali YOKTUR). Bu yüzden opsiyonel bir
/// <see cref="ICopyPhaseCpuFloor"/> verilirse: (1) retry'lar boyunca cap geçici olarak tabana yükseltilir,
/// (2) backoff <see cref="CappedBackoffFactor"/> ile uzar. Seam verilmezse davranış P3 öncesiyle aynıdır.</para>
/// </summary>
public sealed class RetryingMsBuildInvoker(
    IMsBuildInvoker inner,
    IReadOnlyList<TimeSpan> backoff,
    Func<TimeSpan, CancellationToken, Task> delay,
    Action<string>? onRetry = null,
    ICopyPhaseCpuFloor? cpuFloor = null) : IMsBuildInvoker
{
    private readonly IMsBuildInvoker _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IReadOnlyList<TimeSpan> _backoff = backoff ?? throw new ArgumentNullException(nameof(backoff));
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    private readonly Action<string>? _onRetry = onRetry;
    // [T20-b/P3] OPSİYONEL: verilmezse (null) bu sınıf CPU cap'ten tamamen habersizdir ve davranışı P3
    // öncesiyle BİREBİR aynıdır — mevcut çağıranlar (ve testleri) hiçbir şekilde etkilenmez.
    private readonly ICopyPhaseCpuFloor? _floor = cpuFloor;

    /// <summary>200ms, 600ms — toplam 3 deneme (ilk deneme + iki retry).</summary>
    public static readonly IReadOnlyList<TimeSpan> DefaultBackoff = [TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(600)];

    /// <summary>
    /// [T20-b/P3] CPU cap YÜRÜRLÜKTEYKEN backoff bu çarpanla uzar (200/600 → 300/900); cap yokken dizi AYNEN
    /// korunur. Gerekçe: cap altında sıkışan bir post-build copy'nin tamamlanma penceresi uzar — sabit backoff,
    /// MSBuild'in KENDİ copy-retry'ı (MSB3026) daha bitmeden yeniden denemeye başlar ve MSB3027'yi kalıcı bir
    /// Failed'a çevirir. Çarpan, denemelerin SAYISINI değil yalnız aralarını değiştirir.
    /// </summary>
    public const double CappedBackoffFactor = 1.5;

    public async Task<MsBuildInvokeResult> InvokeAsync(MsBuildInvokeRequest req, Action<string> onLine, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        ArgumentNullException.ThrowIfNull(onLine);

        // [T20-b/P3] Copy-contention penceresi: İLK retry kararında açılır ve invoke hangi yoldan dönerse
        // dönsün (başarı, retry'ların tükenmesi, iptal/exception) aşağıdaki finally'de KAPANIR. Kapatmayı bir
        // yolda bile atlamak, tek bir contention'ın Light bir run'ı kalıcı olarak tabana çıkarması demektir.
        IDisposable? floorWindow = null;
        try
        {
            int totalAttempts = _backoff.Count + 1;
            for (int attempt = 1; ; attempt++)
            {
                // contentionFlag: iç invoker satırları KENDİ reader thread'lerinden (stdout/stderr pump'ları)
                // paralel çağırabilir (bkz. MsBuildInvoker.RunChildAsync) — Interlocked ile yazılıp okunur, her
                // deneme başında SIFIRDAN başlar (önceki denemenin contention bulgusu bir sonrakine SIZMAZ).
                int contentionFlag = 0;
                void TeeOnLine(string line)
                {
                    // Her denemenin TÜM satırları çağırana akar (contention görülse de görülmese de) — proje log'u
                    // gerçek başarısızlık metnini içersin diye. Tespit, akışı hiç ENGELLEMEZ.
                    onLine(line);
                    if (CopyContention.IsContention(line))
                        Interlocked.Exchange(ref contentionFlag, 1);
                }

                var result = await _inner.InvokeAsync(req, TeeOnLine, ct);

                bool isLastAttempt = attempt >= totalAttempts;
                // TimedOut/Killed: teardown ya da caller iptali — bu yollardan dönen bir sonuç, satırlarda tesadüfen
                // bir contention kodu geçse bile RETRY EDİLMEZ (retry, devam eden teardown/iptal ile YARIŞMAMALI).
                bool contentionSeen = Volatile.Read(ref contentionFlag) == 1;
                bool shouldRetry = !isLastAttempt && result.ExitCode != 0 && !result.TimedOut && !result.Killed && contentionSeen;

                if (!shouldRetry)
                    return result;

                // Taban, retry'dan (ve backoff'tan) ÖNCE uygulanır: sıkışan copy YENİDEN denenirken gevşemiş
                // cap'i görsün. `??=` ile invoke başına TEK pencere açılır — üç deneme, üç ref-count DEĞİL.
                floorWindow ??= _floor?.Enter();

                var wait = _backoff[attempt - 1];
                if (_floor?.IsCapActive == true) wait *= CappedBackoffFactor; // [P3] cap-farkındalı backoff
                _onRetry?.Invoke(string.Format(CultureInfo.InvariantCulture,
                    "Copy contention algılandı ({0}), deneme {1}/{2} başarısız — {3}ms sonra yeniden denenecek.{4}",
                    req.ProjectId, attempt, totalAttempts, wait.TotalMilliseconds,
                    floorWindow is null ? "" : " " + PerfNoteText.CopyFloorNote));

                await _delay(wait, ct); // ct mid-backoff iptal edilirse delay OperationCanceledException fırlatır — döngü burada aynen yukarı fırlatır.
            }
        }
        finally
        {
            floorWindow?.Dispose(); // cap HER yolda run profiline döner
        }
    }
}
