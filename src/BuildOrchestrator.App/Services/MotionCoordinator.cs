namespace BuildOrchestrator.App.Services;

/// <summary>
/// [T41/DD9] Motion budget "1 hero" kapısı — AYNI ANDA EN FAZLA BİR hero motion oynar (plan v7 §DD9:
/// "1 hero (graf+liste frontier AYNI hero)"). Saf, WPF'siz, test edilebilir: çağıran taraf bir hero'ya
/// GİRMEK isterse <see cref="TryBeginHero"/>/<see cref="Hero"/> çağırır; başka bir hero sürüyorsa istek
/// REDDEDİLİR (false/null) ve çağıran dekoratif yolu atlayıp ani sonuca gider.
///
/// <para><b>"Graf + liste frontier AYNI hero":</b> aynı ANAHTAR yeniden istenirse (iki co-tetiklenen sahip —
/// ör. graf reveal + liste reveal — aynı key'i kullanır) bu, TEK bir hero sayılır ve İZİN verilir (ref-count'lu):
/// birlikte oynarlar. FARKLI bir anahtar ise (başka bir hero motion) reddedilir. Böylece koordine olması
/// gereken sahipler birbirini boğmaz, koordine olmaması gerekenler ise tek-hero bütçesini aşamaz.</para>
///
/// <para>Kullanım UI thread'ine bağlıdır (tüm motion UI thread'inde tick'ler); yine de iç durum bir
/// <c>lock</c> ile korunur — ucuz ve çağrı yeri sayısı azdır.</para>
/// </summary>
public sealed class MotionCoordinator
{
    private readonly object _gate = new();
    private string? _currentKey;
    private int _depth; // aynı key'in re-entrant girişleri (graf+liste frontier AYNI hero)

    /// <summary>O an bir hero oynuyorsa anahtarı, aksi halde null. (Test/wiring görünürlüğü.)</summary>
    public string? CurrentHeroKey
    {
        get { lock (_gate) return _currentKey; }
    }

    /// <summary>Şu anda bir hero motion aktif mi.</summary>
    public bool IsHeroActive
    {
        get { lock (_gate) return _currentKey is not null; }
    }

    /// <summary>Verilen anahtarla bir hero'ya girmeye çalışır. Hero yoksa girer (true). Aynı anahtar zaten
    /// aktifse (AYNI hero — ör. graf+liste frontier) re-entrant girer (true). FARKLI bir hero aktifse REDDEDER
    /// (false) — çağıran dekoratif yolu atlar. Her başarılı <see cref="TryBeginHero"/> tam bir
    /// <see cref="EndHero"/> ile dengelenmelidir (ya da <see cref="Hero"/>'nun disposable'ını kullan).</summary>
    public bool TryBeginHero(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
        {
            if (_currentKey is null)
            {
                _currentKey = key;
                _depth = 1;
                return true;
            }
            if (string.Equals(_currentKey, key, StringComparison.Ordinal))
            {
                _depth++;
                return true;
            }
            return false; // başka bir hero sürüyor
        }
    }

    /// <summary>Bir <see cref="TryBeginHero"/> girişini geri alır. Anahtar mevcut hero'yla eşleşmiyorsa (ya da
    /// hiç hero yoksa) NO-OP — savunmacı (çift-Dispose / yanlış-anahtar birikmez). Son giriş de çıkınca hero
    /// serbest kalır ve yeni (farklı) bir hero başlayabilir.</summary>
    public void EndHero(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
        {
            if (_currentKey is null || !string.Equals(_currentKey, key, StringComparison.Ordinal)) return;
            if (--_depth <= 0)
            {
                _currentKey = null;
                _depth = 0;
            }
        }
    }

    /// <summary>Scoped hero: kabul edilirse Dispose'da (bir kez) <see cref="EndHero"/> çağıran bir
    /// <see cref="IDisposable"/> döner; reddedilirse NULL — çağıran <c>if (coordinator.Hero(key) is not { } h)
    /// { ani-sonuç; return; }</c> deseniyle dekoratif yolu atlar.</summary>
    public IDisposable? Hero(string key)
        => TryBeginHero(key) ? new HeroScope(this, key) : null;

    private sealed class HeroScope(MotionCoordinator owner, string key) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            // Idempotent: yalnız ilk Dispose EndHero'yu çağırır (double-Dispose ref-count'u bozmasın).
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.EndHero(key);
        }
    }
}
