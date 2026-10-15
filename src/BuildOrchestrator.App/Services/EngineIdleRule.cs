namespace BuildOrchestrator.App.Services;

/// <summary>
/// [idle-memory · dalga 3 / spec §6.4 · karar 13-14] Motorun VM'in gözünden durumu — <see cref="EngineIdleRule"/>'ın motor girdisi. VM onu
/// TEK alanda tutar (<c>RunViewModel.EngineState</c>) ve yalnız UI thread'inde, durumu değiştiren olayla SENKRON yazar. Şeridin bayrakları
/// (<c>EngineDiedMessage</c>, <c>IsEngineUnavailable</c>) UI'ın bayrakları olarak kalır, aynı yerlerde yazılır ve onunla aynı şeyi söyler.
/// Yalnız <see cref="Ready"/> motor uyutulabilir: geçişler (<see cref="Starting"/>, <see cref="Sleeping"/>) ve konuşacak süreci olmayan
/// durumlar (<see cref="Asleep"/>, <see cref="Dead"/>, <see cref="Unavailable"/>) uyutulmaz.
/// </summary>
public enum EngineState
{
    /// <summary>Motor başlıyor, hazır bildirimi bekleniyor — geçiş. VM bu durumda doğar (kabuk motoru <c>Loaded</c>'da başlatır; tik daha
    /// önce, pencerenin ctor'unda kurulur) ve "Restart engine" başlarken yeniden girer.</summary>
    Starting,

    /// <summary>Motor hazır ve uyanık — hazır yolundan sonra (<c>RunViewModel.OnEngineReady</c>). Uyutulabilen TEK durum.</summary>
    Ready,

    /// <summary>Uyku sürüyor: konağın uykusu (<see cref="EngineHost.SleepAsync"/>) bitmedi — geçiş. Tik ikinci bir uyku başlatmaz.</summary>
    Sleeping,

    /// <summary>Motor kasıtlı durduruldu: süreci yok, konuşulacak motor yok; sonraki iş onu yeniden başlatır (uyandırma).</summary>
    Asleep,

    /// <summary>Motor kaybedildi — kendiliğinden öldü (<see cref="EngineHost.EngineExited"/>) ya da uykusu durdurmanın ortasında düştü (ölüm
    /// sinyali gelmez, konak ne uyuyor ne canlı): şerit ölüm satırını ve "Restart engine"i gösterir; toparlamak yeniden başlatmanın işidir.</summary>
    Dead,

    /// <summary>Motor hiç doğamadı (kurulum eksik ya da bozuk — <see cref="EngineUnavailableException"/>): komutlar kapalıdır
    /// (<c>RunViewModel.IsEngineUnavailable</c>).</summary>
    Unavailable,
}

/// <summary>
/// [idle-memory · dalga 3 / spec §6.4] Boştaki motorun durdurulması (uyku) kararının SAF kuralı — WPF'siz ve saatsiz (zaman girdidir);
/// <see cref="MemoryTrimRule"/> ile aynı yerleşim. Kural yalnız karar verir: soru <c>RunViewModel.TickElapsed</c>'te, motor sessizlik bekçisinin
/// (<c>EvaluateEngineSilence</c>) yanında, tikin pencere gizliyken de koşan bölümünden sorulur; <c>MainWindow</c> yalnız 200 ms'lik
/// zamanlayıcıyı sürer. Uyutmanın kendisi konağındır (<see cref="EngineHost.SleepAsync"/>); uyku sürerken tek uçuş VM'in motor durumundadır
/// (<see cref="EngineState.Sleeping"/>).
/// </summary>
public static class EngineIdleRule
{
    /// <summary>Son motor etkinliğinden bu kadar sonra ve o arada hiçbir iş yokken boştaki motor durdurulur (spec §6.4: beş dakika).
    /// Süre TEK yerdedir: onu kullanan kural ve tik kendi sayısını yazmaz, buradan okur. Kullanıcıya görünen iki metin (Settings satırının
    /// açıklaması ve "on" notu) süreyi sözcükle yazar — onların bu sabitle ayrışması testle yakalanır
    /// (<c>StopEngineWhenIdleSettingsTests</c>); değerin literal pini <c>EngineIdleRuleTests</c>'tedir.</summary>
    public static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(5);

    /// <summary>Motor şimdi uyutulsun mu: ayar açık, uçuşta işlem yok, cevabı beklenen proje log yüklemesi yok, motor hazır (bir geçişin
    /// ortasında değil) ve son motor etkinliğinden bu yana en az <see cref="IdleAfter"/> geçti.</summary>
    /// <param name="nowMs">Şimdi — VM'in monotonik saati (ms).</param>
    /// <param name="lastActivityMs">Son motor etkinliği: motordan beklenen bir cevabın başı (komut gönderimi), motordan gelen bir olay ya da
    /// hazır bildirimi — VM'in TEK damgası.</param>
    /// <param name="settingOn">Settings → General → ENGINE "Stop the engine when idle".</param>
    /// <param name="operationInFlight">Koşu, Sync, Clean, Optimize, checkout ya da pull uçuşta mı (VM'in tek tanımı).</param>
    /// <param name="pendingLogLoad">Cevabı beklenen bir proje log yüklemesi var mı.</param>
    /// <param name="engine">VM'in motor durumu: yalnız <see cref="EngineState.Ready"/> uyutulur.</param>
    public static bool ShouldSleep(long nowMs, long lastActivityMs, bool settingOn, bool operationInFlight, bool pendingLogLoad,
        EngineState engine)
    {
        if (!settingOn || operationInFlight || pendingLogLoad || engine != EngineState.Ready) return false;
        return TimeSpan.FromMilliseconds(nowMs - lastActivityMs) >= IdleAfter;
    }
}
