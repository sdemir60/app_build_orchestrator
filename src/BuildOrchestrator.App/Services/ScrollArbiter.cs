namespace BuildOrchestrator.App.Services;

/// <summary>Hakem edilen üç scroll paneli. Enum DEĞERİ = paneller-arası ÖNCELİK (küçük = yüksek): frontier &gt;
/// console &gt; stream (brief §1.4 — ortak/çekişen frame'de karar sırası). Değerler <see cref="ScrollArbiter"/>'ın
/// per-panel dizilerinde indeks olarak da kullanılır.</summary>
public enum ScrollPanel { Frontier = 0, Console = 1, Stream = 2 }

/// <summary>Bir panelin bir frame'de yükselttiği auto-scroll niyeti. Enum DEĞERİ = PANEL-İÇİ öncelik (küçük =
/// yüksek): bir frame'de aynı panele birden çok niyet gelirse en küçük değerli KABUL edilen kazanır, gerisi düşer
/// (yo-yo YOK). <see cref="Selection"/>/<see cref="Jump"/> AÇIK (kullanıcı-tetikli) niyetlerdir — bir paneli yeniden
/// devreye alır (suppress temizler); <see cref="Follow"/>/<see cref="BottomAnchor"/> OTOMATİK niyetlerdir (regional
/// suppress'e ve —follow için— seçime tabi).</summary>
public enum ScrollKind { Selection = 0, Jump = 1, Follow = 2, BottomAnchor = 3 }

/// <summary>Bir <see cref="ScrollArbiter.Request"/>/<see cref="ScrollArbiter.Arbitrate"/> kararı. Kabul edilirse
/// <see cref="Epoch"/> o panelin YENİ (artırılmış) devridir — çağıran uçuştaki animasyonunu bununla damgalar; yeni
/// bir grant devri artırınca eski animasyonun tamamlanması BAYAT sayılır (generation-guard, reveal deseniyle aynı) →
/// aynı viewport'u iki yöne çeken çakışma olmaz.</summary>
public readonly record struct ScrollGrant(bool Granted, ScrollPanel Panel, ScrollKind Kind, int Epoch);

/// <summary>
/// [E4/T48] Üç panelin (frontier/console/stream) auto-scroll niyetlerini HAKEM eden SAF (WPF'siz, testli) karar
/// çekirdeği — mevcut <see cref="BuildOrchestrator.App.Controls.FollowScrollController"/>/<see cref="BuildOrchestrator.App.Controls.BottomAnchorBehavior"/>/<see cref="BuildOrchestrator.App.Controls.ScrollAnimator"/>'ı
/// TÜKETİR (yeniden yazmaz); "SAF karar + ince wiring" deseni (FollowScrollDecision/BottomAnchorDecision kardeşi).
/// Semantik otoritesi prototip <c>BuildApp.jsx</c>: paneller BAĞIMSIZ scroll kutularıdır (her biri kendi
/// <c>stick</c>/<c>jumping</c> ref'i) — bir kutuda kaydırmak yalnız o kutunun follow'unu duraklatır; follow
/// yalnız <c>running &amp;&amp; !selected</c> iken (<c>BuildApp.jsx:1388</c>) çalışır → seçim follow'u yener.
/// Bu sınıf o bağımsızlığı ve kuralları MERKEZİ bir otoriteye toplar (paneller onu tüketir).
///
/// <para><b>Kurallar (brief §1):</b>
/// <list type="number">
/// <item><b>Bölgesel suppress:</b> <see cref="NotifyUserScroll"/> YALNIZ verilen paneli duraklatır (diğerleri akar).</item>
/// <item><b>Yo-yo YASAK:</b> bir frame'de bir panel EN FAZLA bir grant alır (<see cref="Arbitrate"/>); her grant
/// panelin <see cref="ScrollGrant.Epoch"/>'unu artırır → eski uçuştaki hareket geçersiz kılınır (follow ile
/// bottom-anchor aynı frame'de viewport'u ters çekemez).</item>
/// <item><b>Seçim &gt; follow:</b> <see cref="Follow"/>, <see cref="SetSelection"/>(true) iken REDDEDİLİR;
/// <see cref="ScrollKind.Selection"/> her zaman geçer.</item>
/// <item><b>Öncelik frontier &gt; console &gt; stream:</b> <see cref="Arbitrate"/> kazananları panel önceliği
/// sırasında döner.</item>
/// </list></para>
///
/// <para>Kullanım UI thread'ine bağlıdır (tüm scroll UI thread'inde tetiklenir); durum kilit gerektirmez.</para>
///
/// <para><b>[E4 fix] SPEC-yüzeyi vs CANLI yol (dürüstlük notu):</b> <see cref="Arbitrate"/>, <see cref="ScrollGrant.Epoch"/>
/// (devir) ve panel öncelik-sıralaması, paneller-arası VE panel-içi (follow-vs-bottom-anchor) çekişmenin
/// SÖZLEŞME-OTORİTE yüzeyidir — ama mevcut bağımsız-panel wiring'i bu çekişmeyi ÜRETMEZ: frontier yalnız
/// follow/selection, console/stream yalnız bottom-anchor yükseltir; ortak/çekişen bir viewport yoktur. Dolayısıyla
/// <see cref="Arbitrate"/>/epoch/öncelik'i doğrulayan SAF testler CANLI bir yolu değil SPEC'i belgeler (ileriye
/// dönük: merkezi bir Arbitrate route açılırsa hazır). CANLI tüketilen yol ise <see cref="CanFollowFrontier"/>
/// (frontier follow gate; <c>FollowFrontier</c> her tick okur) + <see cref="IsSuppressed"/>/<see cref="NotifyUserScroll"/>/
/// <see cref="Resume"/> (frontier regional wheel-suppress; liste wheel set eder, near-bottom'a dönüş temizler) +
/// <see cref="SetSelection"/>/<see cref="HasSelection"/> (seçim &gt; follow). Bu API brief §1/§4 zorunluluğudur —
/// silinmez; <see cref="ScrollGrant.Epoch"/> şu an hiçbir yerde TÜKETİLMEZ (spec-forward, bu notla belgeli).</para>
/// </summary>
public sealed class ScrollArbiter
{
    private const int PanelCount = 3;

    private readonly bool[] _suppressed = new bool[PanelCount]; // regional: bir panelde kullanıcı scroll'u aktif mi
    private readonly int[] _epoch = new int[PanelCount];        // per-panel animasyon devri (yo-yo guard)
    private bool _hasSelection;                                 // frontier seçimi aktif mi (seçim > follow)

    /// <summary>Frontier'de bir kart seçili mi — <see cref="ScrollKind.Follow"/> bu true iken reddedilir (seçim
    /// &gt; follow, <c>BuildApp.jsx:1388</c> <c>follow = running &amp;&amp; !selected</c>).</summary>
    public bool HasSelection => _hasSelection;

    /// <summary>[E4 fix] Frontier follow ŞU AN devreye girebilir mi — bir kart seçili DEĞİL <b>ve</b> frontier
    /// bölgesel wheel-suppress altında DEĞİL. <c>MainWindow.FollowFrontier</c> bunu HER tick'te CANLI tüketir; böylece
    /// <see cref="IsSuppressed"/>(Frontier) yalnız YAZILAN değil OKUNAN bir bit olur: kullanıcı listeyi kaydırınca
    /// (<see cref="NotifyUserScroll"/>) follow duraklar, near-bottom'a dönünce (<see cref="Resume"/>) sürer; seçim de
    /// (<see cref="SetSelection"/>) duraklatır. <c>BuildApp.jsx:1388</c> <c>follow = running &amp;&amp; !selected</c>'ın
    /// regional-suppress'le genişletilmiş CANLI hâli.</summary>
    public bool CanFollowFrontier => !_hasSelection && !_suppressed[(int)ScrollPanel.Frontier];

    /// <summary>Seçim durumunu kurar (SelectedProjectId değişiminde). true iken frontier follow duraklatılır;
    /// false olunca follow kaldığı yerden sürebilir.</summary>
    public void SetSelection(bool active) => _hasSelection = active;

    /// <summary>Verilen panelin auto-scroll'u kullanıcı scroll'uyla duraklatıldı mı (regional).</summary>
    public bool IsSuppressed(ScrollPanel panel) => _suppressed[(int)panel];

    /// <summary>Verilen panelin güncel animasyon devri (uçuştaki hareketi damgalamak / bayat-guard için).</summary>
    public int EpochOf(ScrollPanel panel) => _epoch[(int)panel];

    /// <summary>Kullanıcı bir paneli (tekerlek/sürükleme) kaydırdı → YALNIZ o panelin auto-scroll'u duraklar
    /// (diğer paneller etkilenmez — regional). ScrollAnimator'ın per-target IsUserSuppressed'ının merkezi karşılığı.</summary>
    public void NotifyUserScroll(ScrollPanel panel) => _suppressed[(int)panel] = true;

    /// <summary>Kullanıcı panelin auto-scroll'una geri döndü (ör. dibe indi / frontier'e döndü) → suppress kalkar,
    /// o panelin otomatik takibi/yapışması sürebilir.</summary>
    public void Resume(ScrollPanel panel) => _suppressed[(int)panel] = false;

    /// <summary>Bir niyet AÇIK (kullanıcı-tetikli) mi — seçim-scroll ve pill-jump paneli yeniden devreye alır.</summary>
    private static bool IsExplicit(ScrollKind kind) => kind is ScrollKind.Selection or ScrollKind.Jump;

    private bool IsAllowed(ScrollPanel panel, ScrollKind kind) => kind switch
    {
        ScrollKind.Selection or ScrollKind.Jump => true,                        // açık kullanıcı eylemi — her zaman
        ScrollKind.Follow => !_suppressed[(int)panel] && !_hasSelection,        // regional suppress + seçim > follow
        ScrollKind.BottomAnchor => !_suppressed[(int)panel],                    // regional suppress
        _ => false,
    };

    /// <summary>Tek bir auto-scroll niyetini hakemler. Kabul edilirse panelin devri artırılır (yo-yo guard) ve
    /// AÇIK niyet ise (<see cref="ScrollKind.Selection"/>/<see cref="ScrollKind.Jump"/>) o panelin suppress'i
    /// temizlenir — <c>ScrollAnimator.AnimateTo</c> paritesi (yeni programatik hareket, kullanıcı iptali artık
    /// geçersiz). Reddedilirse devir DEĞİŞMEZ.</summary>
    public ScrollGrant Request(ScrollPanel panel, ScrollKind kind)
    {
        if (!IsAllowed(panel, kind)) return new ScrollGrant(false, panel, kind, _epoch[(int)panel]);
        if (IsExplicit(kind)) _suppressed[(int)panel] = false;
        return new ScrollGrant(true, panel, kind, ++_epoch[(int)panel]);
    }

    /// <summary>Bir frame'de gelen niyetleri hakemler: her panel EN FAZLA bir grant alır (yo-yo YOK — bir panel
    /// içindeki en yüksek öncelikli KABUL edilen niyet kazanır, gerisi düşer), kazananlar panel önceliği sırasında
    /// (frontier→console→stream) döner. Reddedilen paneller listede yer almaz.</summary>
    public IReadOnlyList<ScrollGrant> Arbitrate(IReadOnlyList<(ScrollPanel Panel, ScrollKind Kind)> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var winners = new List<ScrollGrant>(PanelCount);
        for (int p = 0; p < PanelCount; p++) // frontier(0) → console(1) → stream(2)
        {
            var panel = (ScrollPanel)p;
            // Bu paneldeki niyetler panel-içi önceliğe (küçük ScrollKind) göre denenir; İLK kabul edilen kazanır ve
            // döngü kırılır → panel başına tek devir artışı (bir panel aynı frame'de iki yöne çekilemez).
            foreach (var kind in frame.Where(r => r.Panel == panel).Select(r => r.Kind).Distinct().OrderBy(k => (int)k))
            {
                var grant = Request(panel, kind);
                if (grant.Granted) { winners.Add(grant); break; }
            }
        }
        return winners;
    }
}
