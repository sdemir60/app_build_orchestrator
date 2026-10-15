namespace BuildOrchestrator.App.Services;

/// <summary>
/// [idle-memory · dalga 3 / spec §6.4 · karar 13-14] Motorun VM'in gözünden durumu — <see cref="EngineIdleRule"/>'ın motor girdisi. VM onu
/// TEK alanda tutar (<c>RunViewModel.EngineState</c>) ve yalnız UI thread'inde, durumu değiştiren olayla SENKRON yazar. Şeridin bayrakları
/// (<c>EngineDiedMessage</c>, <c>IsEngineUnavailable</c>) UI'ın bayrakları olarak kalır, aynı yerlerde yazılır ve onunla aynı şeyi söyler.
/// Yalnız <see cref="Ready"/> motor uyutulabilir: geçişler (<see cref="Starting"/>, <see cref="Sleeping"/>, <see cref="Waking"/>) ve konuşacak
/// süreci olmayan durumlar (<see cref="Asleep"/>, <see cref="Dead"/>, <see cref="Unavailable"/>) uyutulmaz. Aynı alan VM'in motor kapısını
/// da sürer — gönderim kuralının tek ifadesi <c>RunViewModel.EnsureEngineAwakeAsync</c>'in özetindedir.
/// </summary>
public enum EngineState
{
    /// <summary>Motor başlıyor, hazır bildirimi bekleniyor — geçiş. VM bu durumda doğar (kabuk motoru <c>Loaded</c>'da başlatır; tik daha
    /// önce, pencerenin ctor'unda kurulur) ve "Restart engine" başlarken yeniden girer; ölüm sinyali gelmeden düşen bir yeniden başlatmada
    /// kalıcı kalabilir (<c>RunViewModel.EngineState</c>'in bilinen ikinci ayrışması).</summary>
    Starting,

    /// <summary>Motor hazır ve uyanık — hazır yolundan sonra (<c>RunViewModel.OnEngineReady</c>). Uyutulabilen TEK durum.</summary>
    Ready,

    /// <summary>Uyku sürüyor: konağın uykusu (<see cref="EngineHost.SleepAsync"/>) bitmedi — geçiş. Tik ikinci bir uyku başlatmaz.</summary>
    Sleeping,

    /// <summary>Motor kasıtlı durduruldu: süreci yok, konuşulacak motor yok; sonraki iş onu yeniden başlatır (uyandırma).</summary>
    Asleep,

    /// <summary>[dalga 3 · Task 21 · karar 32] Uyanma sürüyor: bir komut yolu uyuyan motoru başlatıyor (<see cref="EngineHost.WakeAsync"/>
    /// bitmedi) — geçiş; kural uyutmaz. Uyanma bitince <see cref="Ready"/>; düşerse kurulum hatasında <see cref="Unavailable"/>, doğup hazır
    /// olamayan motorda <see cref="Dead"/> (ölüm sinyaliyle; sinyal hiç gelmezse kapıya yeni giren ilk yolda — kural
    /// <c>RunViewModel.EnsureEngineAwakeAsync</c>'te).</summary>
    Waking,

    /// <summary>Motor kaybedildi — kendiliğinden öldü (<see cref="EngineHost.EngineExited"/>), uykusu durdurmanın ortasında düştü (ölüm sinyali
    /// gelmez, konak ne uyuyor ne canlı) ya da uyanması ölüm sinyali gelmeden düştü: şerit ölüm satırını ve "Restart engine"i gösterir;
    /// toparlamak yeniden başlatmanın işidir.</summary>
    Dead,

    /// <summary>Motor hiç doğamadı (kurulum eksik ya da bozuk — <see cref="EngineUnavailableException"/>): komutlar kapalıdır
    /// (<c>RunViewModel.IsEngineUnavailable</c>).</summary>
    Unavailable,
}
