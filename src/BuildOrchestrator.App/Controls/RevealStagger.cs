using System.Windows.Threading;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [W2/It-5] Açılış reveal stagger'ının hero + kuşak + release muhasebesi — TEK yer.
/// <see cref="Graph.GraphView"/> (katman stagger'ı) ve <see cref="StickyLayerList"/> (satır stagger'ı) bu üçlüyü
/// gövde olarak BİREBİR aynı yazmıştı: hero al / kuşak damgala / pencere sonunda generation-guarded bırak.
/// KADEMELEME'nin kendisi (hangi öğe ne kadar gecikmeyle belirir) burada DEĞİLDİR — o iki sahipte kasten farklıdır
/// (graf: 55ms/katman · tavan 330; liste: 10ms/satır · tavan 380).
///
/// <para><b>[E3 fix — kritik, korunur]</b> Release tetiği bir <see cref="DispatcherTimer"/>'dır.
/// <c>Completed</c>-after-<c>BeginAnimation</c> yolu gerçek-HWND WPF'te HİÇ ateşlenmez (ölü kod) — oraya
/// dönülürse hero SONSUZA DEK takılır. Timer generation-guarded'dır (<see cref="ReleaseIfCurrent"/>): reveal #1
/// sürerken hızlı bir ikinci reveal gelirse #1'in timer'ı ateşlense bile #2'nin TAZE hero'suna dokunmaz.</para>
///
/// <para><b>[DD9]</b> Hero anahtarı çağıranın verdiğidir; graf ve liste AYNI anahtarı (<c>"sync-reveal"</c>)
/// derleme-zamanı sabiti olarak paylaşır → re-entrant kabul edilir, birlikte oynarlar.</para>
/// </summary>
internal sealed class RevealStagger
{
    /// <summary>[W2 fix-1] design-v1 <c>bo-reveal</c> (BuildApp.jsx:15/:27) — bir öğenin beliriş SÜRESİ (.3s).
    /// Graf düğümü ve liste satırı AYNI animasyon ailesindendir; sabit önce iki yerde ayrı yazılıydı
    /// (<c>GraphView.RevealMs</c> ↔ <c>ProjectRow.RevealMs</c>) ve sessizce sürüklenebilirdi. TEK tanım burada;
    /// iki sahip de derleme-zamanı alias tutar (<c>StickyLayerList.RevealHeroKey</c> deseni).</summary>
    public const double RevealMs = 300.0;

    /// <summary>[W2 fix-1] Öğe bu kadar YUKARIDAN gelir (BuildApp.jsx:27 <c>translateY(-5px)</c>) — aynı gerekçe,
    /// bkz. <see cref="RevealMs"/>.</summary>
    public const double RevealRisePx = 5.0;

    private IDisposable? _hero;
    private int _generation;
    private DispatcherTimer? _releaseTimer;

    /// <summary>Aktif reveal kuşağının damgası — test, doğru kuşakla release'i tetiklemek için okur.</summary>
    public int Generation => _generation;

    /// <summary>Reveal tamamlandığında hero'yu bırakacak CANLI bir release zamanlandı mı — ölü <c>Completed</c>
    /// yolunun aksine gerçek bir tetik kuruldu mu (test ayırt edici olarak okur).</summary>
    public bool HasPendingRelease => _releaseTimer is { IsEnabled: true };

    /// <summary>
    /// Yeni bir reveal kuşağı başlatır: önceki hero + bekleyen release bırakılır, kuşak damgası artırılır ve
    /// (animasyon açıksa) hero alınmaya çalışılır.
    ///
    /// <para>Başka bir hero sürerken (<paramref name="coordinator"/> hero vermezse) dekoratif stagger ATLANIR —
    /// çağıran öğeleri ani yerleştirir. Coordinator yoksa (headless, enjekte edilmemiş) davranış eskisi gibidir:
    /// hero alınmaz ama <c>animate</c> düşürülmez.</para>
    /// </summary>
    /// <returns>Kademelenecek mi (<c>Animate</c>) ve bu reveal'in kuşak damgası (<c>Generation</c>).</returns>
    public (bool Animate, int Generation) Begin(bool animationsEnabled, MotionCoordinator? coordinator, string heroKey)
    {
        Release();
        int generation = ++_generation;

        bool animate = animationsEnabled;
        if (animate)
        {
            _hero = coordinator?.Hero(heroKey);
            if (coordinator is not null && _hero is null)
                animate = false; // başka bir hero aktif → ani sonuç
        }

        return (animate, generation);
    }

    /// <summary>
    /// Hero'yu reveal PENCERESİ (<paramref name="maxDelayMs"/> + <paramref name="revealMs"/>) dolunca bırakacak
    /// generation-guarded tetiği kurar. Hero tutulmuyorsa no-op; hero alınmış ama HİÇ öğe yoksa
    /// (<paramref name="maxDelayMs"/> &lt; 0, savunmacı dal) hero derhal bırakılır — bekletilmez.
    /// </summary>
    public void ScheduleRelease(double maxDelayMs, double revealMs, int generation)
    {
        if (_hero is null) return;                       // reduced/blocked → hero yok, release gerekmez
        if (maxDelayMs < 0) { Release(); return; }       // hero alındı ama öğe yok (savunmacı) — bekletme

        var releaseAfter = TimeSpan.FromMilliseconds(maxDelayMs + revealMs);
        _releaseTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = releaseAfter };
        _releaseTimer.Tick += (_, _) => ReleaseIfCurrent(generation);
        _releaseTimer.Start();
    }

    /// <summary>Reveal tamamlandığında hero'yu bırakan generation-guarded karar: YALNIZ tetikleyen reveal hâlâ
    /// geçerliyse (<paramref name="generation"/> == <see cref="Generation"/>) bırakır. Superseded bir reveal'in
    /// gecikmiş release'i mevcut reveal'in taze hero'sunu düşürmez. Testler bunu doğrudan çağırır (gerçek timer
    /// tick'ini beklemeden).</summary>
    public void ReleaseIfCurrent(int generation)
    {
        if (generation != _generation) return; // stale kuşak — mevcut hero'ya dokunma
        Release();
    }

    /// <summary>Hero'yu (varsa) ve bekleyen release timer'ını bırakır — yeni bir hero girebilir. Çift-bırakma
    /// güvenli (<c>HeroScope.Dispose</c> idempotent).</summary>
    public void Release()
    {
        if (_releaseTimer is not null)
        {
            _releaseTimer.Stop();
            _releaseTimer = null;
        }
        _hero?.Dispose();
        _hero = null;
    }
}
