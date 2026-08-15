using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T58] Birikimli yapışan katman başlıkları — liste ScrollViewer + overlay Canvas (feasibility §3.3).
/// Layout aritmetiği <see cref="LayoutMetrics"/>'te (SAF, testli, T59 follow-mode ile ORTAK instance);
/// bu control yalnız WPF kablajı: grupları in-flow entry akışına çevirir, ScrollChanged'de yapışık başlık
/// kümesini overlay'e sürer. Virtualization KAPALI (§4.1 — aritmetik tablo yalnız o zaman birebir).
/// </summary>
public partial class StickyLayerList : UserControl
{
    /// <summary>Sıralı bir katman: adı (boş → başlıksız, sticky devrede değil) + satır nesneleri (RowTemplate
    /// <c>{Binding Name}</c>'e bağlanır — <see cref="ViewModels.ProjectRowViewModel"/> gibi bir <c>Name</c>
    /// taşıyan her nesne).</summary>
    public sealed record LayerGroup(string Name, IReadOnlyList<object> Rows);

    /// <summary>In-flow başlık entry'si — <see cref="LayoutMetrics.HeaderInfo"/>'nun WPF-binding karşılığı;
    /// overlay'in StuckHeader'ı ile AYNI <c>Name</c>/<c>RowCount</c> alanlarını taşır ki tek şablon ikisine de
    /// bağlansın.</summary>
    public sealed record HeaderEntry(string Name, int RowCount);

    private static readonly IReadOnlyList<StuckHeader> NoHeaders = [];

    /// <summary>[T59 ile ORTAK] Gruplardan kurulan kümülatif offset servisi — follow-mode/selection scroll
    /// hedefleri AYNI instance'tan üretilir. <see cref="SetGroups"/>'tan önce null.</summary>
    public LayoutMetrics? Metrics { get; private set; }

    // [T59] Follow-mode/seçili-karta-kaydırma orkestratörü — Metrics her SetGroups'ta yenilendiğinden burada da yenilenir.
    private FollowScrollController? _follow;

    // [E4/T48 · E3 fold · W2] Liste-frontier reveal hero (DD9: graf+liste AYNI hero) — hero/kuşak/release muhasebesi
    // GraphView ile ORTAK tek yerde (RevealStagger); burada yalnız liste kademelemesi (10ms/satır, tavan 380) kalır.
    private readonly RevealStagger _reveal = new();
    private bool _revealPending;             // SetGroups reveal'i "kurar"; container üretimi tamamlanınca oynar

    /// <summary>[W2] Provider seam'i TEK yerde (<see cref="MotionGate"/>). Bu sahip yalnız TAZE OKUR — canlı
    /// <c>AnimationsEnabledChanged</c> aboneliği bugün de YOKtur (davranış birebir korunur).</summary>
    private readonly MotionGate _motion = new();

    public StickyLayerList()
    {
        InitializeComponent();
        Flow.ItemTemplateSelector = new EntrySelector(this);
        // Sanallaştırılmış panelin kümülatif tablosu ile LayoutMetrics'inki AYNI iki sabitten türer (kopya YASAK):
        // başlık 24px, satır 36px. Bu bağ olmadan scroll ekseni yapışık-başlık aritmetiğinden kayardı.
        FixedHeightVirtualizingPanel.SetEntryHeightSelector(Flow,
            entry => entry is HeaderEntry ? LayoutMetrics.DefaultHeaderHeight : LayoutMetrics.DefaultRowHeight);
        Overlay.ItemsSource = NoHeaders;
        // Salt aritmetik overlay recompute: kaydırmada yapışık küme değişir (ScrollUnit=Pixel → VerticalOffset px).
        // [E4 fix] AYNI ScrollChanged'de frontier follow "near-bottom'a dönüş → takip sürsün" resume tetiği çalışır
        // (Console/Stream'in BottomAnchor.IsStuck→Arbiter.Resume simetriği — tek asimetrik panel frontier'di).
        Scroll.ScrollChanged += (_, _) => { UpdateOverlay(Scroll.VerticalOffset); ResumeFrontierIfNearBottom(); };
        // [T59] Kullanıcı tekerleği çevirdiği anda uçuştaki follow/seçim-scroll animasyonu iptal olur + suppress
        // bayrağı kalkar (feasibility §3.3 — WPF'te wheel'in animasyonu otomatik iptal etmesi YOK, tarayıcının aksine).
        ScrollAnimator.EnableUserCancellation(Scroll);
        // [E4/T48] Kullanıcı frontier'i kaydırınca merkezi arbiter'a haber ver (bölgesel suppress — yalnız bu panel
        // duraklar, konsol/stream akmaya devam). Arbiter null ise (izole test) no-op.
        // [frontier resume] Tekerlek ANI ayrıca damgalanır: boşta-geri-açılma penceresi (FrontierIdleResumeMs)
        // buradan ölçülür ve her yeni notch onu SIFIRLAR — kullanıcı aktif kaydırırken takip devreye giremez.
        Scroll.PreviewMouseWheel += (_, _) =>
        {
            _lastUserScrollAtMs = NowMs();
            Arbiter?.NotifyUserScroll(ScrollPanel.Frontier);
        };
        // [E4/T48 · E3 fold] Flow container üretimi (ItemContainerGenerator) TAMAMLANINCA + bir SetGroups reveal'i
        // beklerken satırlar KADEMELİ belirsin (bo-reveal). Bkz. OnGeneratorStatusChanged (deferred).
        Flow.ItemContainerGenerator.StatusChanged += OnGeneratorStatusChanged;
        // Reveal ortasında unload olursa hero'yu bırak (aksi halde bir sonraki hero sonsuza dek bloke olurdu).
        Unloaded += (_, _) => _reveal.Release();
    }

    /// <summary>[E4/T48] Motion sinyalinin TAZE okunduğu kapı (GraphView deseni, D8) — testler enjekte eder;
    /// null-güvenli headless varsayılan <c>App.Motion</c>. [W2] Depo <see cref="MotionGate"/>.</summary>
    public Func<bool> AnimationsEnabledProvider
    {
        get => _motion.AnimationsEnabledProvider;
        set => _motion.AnimationsEnabledProvider = value;
    }

    /// <summary>[E4/T48/DD9] Liste reveal'inin girdiği hero-mutex; null ise <c>App.HeroMotion</c> (TAZE). Graf reveal
    /// ile AYNI key (<see cref="RevealHeroKey"/>) — co-tetiklenir, birlikte oynar (re-entrant). Testler enjekte eder.</summary>
    public MotionCoordinator? HeroCoordinator { get; set; }

    /// <summary>[E4/T48] Frontier'in auto-scroll'unu (follow/seçim) hakem eden merkezi arbiter; null ise izole
    /// (bölgesel suppress bildirimi no-op). MainWindow enjekte eder.</summary>
    public ScrollArbiter? Arbiter { get; set; }

    /// <summary>Graf reveal + liste reveal ORTAK hero anahtarı — <c>Graph.GraphView.RevealHeroKey</c> ile BİREBİR
    /// AYNI (DD9 "graf+liste frontier AYNI hero"); derleme-zamanı sabiti olduğundan ikisi ASLA sürüklenemez.</summary>
    internal const string RevealHeroKey = Graph.GraphView.RevealHeroKey;

    private MotionCoordinator? ActiveHeroCoordinator => HeroCoordinator ?? App.HeroMotion;

    /// <summary>In-flow ve overlay başlıklarının paylaştığı TEK DataTemplate (geçişin görünmezliği bunu gerektirir).</summary>
    public DataTemplate HeaderTemplate => (DataTemplate)Resources["HeaderTemplate"];
    public DataTemplate RowTemplate => (DataTemplate)Resources["RowTemplate"];

    /// <summary>Flow'un seçici üzerinden başlıklar için kullandığı şablon — <see cref="HeaderTemplate"/> ile
    /// AYNI nesne olmalı (test kanıtlar).</summary>
    internal DataTemplate HeaderTemplateForFlow() => ((EntrySelector)Flow.ItemTemplateSelector).Header;

    /// <summary>Grupları kur: kümülatif metrics + in-flow entry akışı (başlık + satırlar) + overlay ilk hesap.
    /// Adı boş grup → başlıksız (varsayılan tek liste); sticky devrede değil.
    /// <para>Varsayılan reveal OYNAR — bu, topoloji/Sync yoludur (<see cref="StickyRevealTriggerTests"/> pinler).</para></summary>
    public void SetGroups(IReadOnlyList<LayerGroup> groups) => SetGroups(groups, reveal: true);

    /// <summary>
    /// [A13/T2 · 2.5] <paramref name="reveal"/> = bu tazeleme kademeli belirişi (bo-reveal) OYNATSIN mı.
    ///
    /// <para><b>Neden ayrım ZORUNLU (A12 sınıfı, ÖLÇÜLDÜ):</b> liste 2.5'te filtreye bağlandı ve
    /// <c>_revealPending</c> koşulsuz kurulduğu sürece <b>her tuş vuruşu stagger'ı baştan oynatıyordu</b> —
    /// üç harflik bir sorgu <c>RevealGeneration</c>'ı 3'ten 6'ya çıkarıyordu. Prototip otoritesi bu ayrımı
    /// destekler — ama <b>tam olarak şu ölçüldü (A13/B3 fix round 1):</b> <c>revealKey</c> yalnız iki yerde
    /// artar, <c>doSync()</c> (<c>BuildApp.jsx:1186-1193</c>, artış <c>:1190</c>) ve <c>pickFolder()</c>
    /// (<c>BuildApp.jsx:1378</c>); <b>filtre yolu ona HİÇ dokunmaz</b>. Dikkat — otorite "yalnız topoloji
    /// değişince artar" DEMEZ: <c>doSync()</c> topolojiye BAKMADAN her Sync'te artırır. Üretim orada bilerek
    /// ayrılır; gerekçesi <c>RunViewModel._lastTopologySignature</c>'ın XML doc'undadır.</para>
    ///
    /// <para><b>Reset semantiği BİLEREK KORUNDU</b> (filtre tazelemesi de <c>ItemsSource</c> ataması yapar,
    /// yani tam reset). A13.2'nin "koleksiyon reset'i YASAK" kuralı burada ihlal edilmez, çünkü kuralın
    /// koruduğu iki şey de zarar görmez: <b>(a) seçim</b> satır VM'lerinin kendi <c>IsSelected</c>'ında yaşar
    /// ve satır nesneleri <c>Projects</c>'ten gelen AYNI örneklerdir → reset seçimi düşürmez; <b>(b) gereksiz
    /// churn</b> çağıran tarafta kapatılır (<c>MainWindow</c> görünür-satır imzası değişmedikçe buraya HİÇ
    /// gelmez). Alternatif (entry akışını yerinde uzlaştırmak) <see cref="Metrics"/>/overlay/reveal
    /// muhasebesinin İKİNCİ bir kopyasını gerektirirdi — "tek yer" kuralına aykırı.</para>
    ///
    /// <para><b>[T2 fix-2 · m9 — ÖLÇÜLDÜ, düzeltildi]</b> Önceki sürüm burada "görünür küme değiştiğinde
    /// listenin başa dönmesi doğru davranıştır" diyordu — bu iddia hiç ÖLÇÜLMEMİŞTİ ve YANLIŞTI.
    /// <see cref="ProjectListFilterTests.Filtering_the_list_preserves_the_scroll_offset_instead_of_snapping_to_the_top"/>
    /// üretim yolundan ölçer: WPF <c>ScrollViewer</c>, <c>ItemsSource</c> tam reset yese bile
    /// <c>VerticalOffset</c>'i KORUR (yeni extent'e clamp eder) — liste BAŞA DÖNMEZ. Yani reset semantiğinin
    /// zararsızlığı yalnız (a) ve (b)'ye dayanır; scroll konumu zaten hiç tehlikede değildi.</para>
    /// </summary>
    public void SetGroups(IReadOnlyList<LayerGroup> groups, bool reveal)
    {
        ArgumentNullException.ThrowIfNull(groups);
        Metrics = new LayoutMetrics(groups.Select(g => new LayerSpec(g.Name ?? "", g.Rows.Count)).ToList());
        // [T59] Metrics tazelendi — follow/seçim controller'ı AYNI (yeni) instance'ı paylaşmalı.
        // [T2 fix-1 · I-D] ...ama controller YENİDEN YARATILMAZ, yalnız REBIND edilir: 2.5'ten sonra SetGroups
        // görünür küme her değiştiğinde koşuyor ve her yeni controller throttle saatini (550ms, design-v1 §3.3)
        // ve seçim durumunu sıfırlıyordu. Bkz. FollowScrollController.Rebind.
        if (_follow is null)
            _follow = new FollowScrollController(Metrics, () => Scroll.ViewportHeight, () => Scroll.VerticalOffset, AnimateScrollTo);
        else
            _follow.Rebind(Metrics);

        var entries = new List<object>();
        foreach (var g in groups)
        {
            if (!string.IsNullOrEmpty(g.Name))
                entries.Add(new HeaderEntry(g.Name, g.Rows.Count));
            entries.AddRange(g.Rows);
        }
        // [E4/T48 · E3 fold] Yeni topoloji = yeni reveal (prototip revealKey artışı, BuildApp.jsx:1378 vb.). Container
        // üretimi tamamlanınca satırlar kademeli belirir (OnGeneratorStatusChanged).
        //
        // [A12] BAYRAK, `ItemsSource` ATAMASINDAN ÖNCE KURULUR — sıra KRİTİKTİR. `ItemsSource` ataması
        // container üretimini SENKRON olarak tamamlayabilir (ölçüldü: liste ZATEN realize edilmişken —
        // yani üretimdeki sıra: kabuk realize, gruplar sonra akar — `StatusChanged`/`ContainersGenerated`
        // bu satırın İÇİNDE ateşlenir). Bayrak sonra kurulursa handler onu `false` görüp döner ve BİR DAHA
        // status değişimi gelmez → reveal SESSİZCE hiç oynamaz, kartlar tam opaklıkta "pat" diye belirir.
        // [A13/T2 · 2.5] Filtre tazelemesinde (reveal:false) bayrak KURULMAZ.
        // [T2 fix-1 · m1] Sınırı doğru yazalım: bu, HENÜZ TÜKETİLMEMİŞ bir bayrağı (container üretimi
        // tamamlanmadan gelen ikinci bir SetGroups) düşürür. Bayrak zaten tüketilip
        // <see cref="PlayRevealStagger"/> Dispatcher(Loaded) kuyruğuna alındıysa O ÇAĞRI İPTAL EDİLMEZ —
        // reveal'in kendisi generation-guard'lı (<see cref="RevealStagger"/>) olduğundan zararsızdır:
        // en fazla taze listeyi bir kez kademeli gösterir, yanlış satırlara dokunamaz.
        _revealPending = reveal;
        Flow.ItemsSource = entries;
        UpdateOverlay(Scroll.VerticalOffset);
    }

    /// <summary>[T59] Koşarken + seçim yokken frontier satırının (çağıranın belirlediği — ör. ilk
    /// <c>State==Started</c> proje) görünür kalması için çağrılır. Throttle(550ms)/dead-band(54px)/kullanıcı-
    /// suppress kararı <see cref="FollowScrollController"/>'a aittir.</summary>
    public void FollowRow(int rowIndex) => _follow?.FollowRow(rowIndex, ScrollAnimator.GetIsUserSuppressed(Scroll));

    /// <summary>[T59] Kullanıcı tekerleği çevirerek follow'u en son iptal etti mi — çağıranın (ör. bir "geri frontier'e
    /// dön" affordance'ı) bunu gösterip göstermeyeceğine karar vermesi için.</summary>
    public bool IsFollowSuppressedByUser => ScrollAnimator.GetIsUserSuppressed(Scroll);

    /// <summary>[T59] Kullanıcı-suppress bayrağını YOK SAYARAK satırı zorla görünür kılar ("frontier'e dön" tıklaması) —
    /// <see cref="ScrollAnimator.AnimateTo"/> ZATEN her çağrıda suppress'i temizler (yeni programatik hareket).</summary>
    public void ResumeFollow(int rowIndex) => _follow?.FollowRow(rowIndex, userSuppressed: false);

    /// <summary>[E4 fix] Frontier follow'un "dibe/frontier'e geri dön → takip sürsün" resume eşiği — <b>BottomAnchorBehavior
    /// ile AYNI</b> eşik (<see cref="BottomAnchorDecision.DefaultThresholdPx"/>, 48px) ki Console/Stream'in bottom-anchor
    /// resume'uyla frontier simetrik olsun.</summary>
    internal const double FrontierResumeThresholdPx = BottomAnchorDecision.DefaultThresholdPx;

    private double DistanceFromBottom() =>
        Math.Max(0, Scroll.ExtentHeight - Scroll.VerticalOffset - Scroll.ViewportHeight);

    /// <summary>
    /// [E4 fix — FIX 1/FIX 2.1] Frontier follow AUTO-RESUME tetiği (Console/Stream'in <c>BottomAnchor.IsStuck →
    /// Arbiter.Resume</c> simetriği; It-4a wheel-suppress PAUSE'u E4'te CANLI olunca eksik kalan tek yön buydu).
    /// Kullanıcı listeyi tekerlekle duraklattıysa (<see cref="ScrollAnimator"/> per-target suppress + arbiter'ın
    /// <see cref="ScrollArbiter.NotifyUserScroll"/> ile kurduğu regional bit) ve near-bottom frontier bölgesine
    /// döndüyse İKİ suppress'i de TEK yoldan temizler → bir sonraki <c>FollowFrontier</c> tick'i (arbiter
    /// <see cref="ScrollArbiter.CanFollowFrontier"/>) takibi sürdürür.
    ///
    /// <para><b>Yo-yo YOK (kritik):</b>
    /// <list type="bullet">
    /// <item><b>Edge-tetikli:</b> yalnız follow ŞU AN kullanıcı-tekerleğiyle DURAKLIYKEN
    /// (<see cref="ScrollAnimator.GetIsUserSuppressed"/>) davranır — takip zaten sürerken (unsuppressed) no-op; bu yüzden
    /// follow ANİMASYONUNUN kendi ScrollChanged'leri resume tetiklemez (follow ile ping-pong yok).</item>
    /// <item><b>Konum kapısı:</b> yalnız near-bottom'da (<see cref="FrontierResumeThresholdPx"/>) temizler; kullanıcı
    /// yukarı/uzağa kaydırdıysa suppress KALIR (follow duraklı kalır — doğru).</item>
    /// <item><b>Throttle'a saygı:</b> suppress'i temizlemek listeyi HAREKET ETTİRMEZ; gerçek re-engagement bir sonraki
    /// tick'te <see cref="FollowScrollController"/>'ın 550ms throttle + 54px dead-band'ine tabidir. Kullanıcı aktif
    /// kaydırırken her tekerlek notch'u suppress'i yeniden kurar (<c>ScrollAnimator.EnableUserCancellation</c>) ve
    /// FollowRow bunu TAZE okur → uçuştaki tekerlekle dövüşmez; takip pratikte ancak kullanıcı durunca oturur.</item>
    /// </list></para>
    /// </summary>
    /// <summary>[frontier resume] Boşta-geri-açılma penceresi: listeye bu kadar süre DOKUNULMAZSA takip
    /// kendiliğinden sürer. Follow throttle'ının (550 ms) birkaç katı — okumakta olan bir kullanıcıyı listenin
    /// altından çekecek kadar kısa DEĞİL, etkileşimi bırakmış bir kullanıcıyı takibi elle geri açmaya
    /// zorlayacak kadar uzun da değil.</summary>
    internal const long FrontierIdleResumeMs = BottomAnchorDecision.IdleResumeMs; // tek kaynak orada

    /// <summary>[D8] Boşta penceresinin saat kaynağı — testler deterministik sürer.</summary>
    internal Func<long> NowMs { get; set; } = () => Environment.TickCount64;

    private long _lastUserScrollAtMs = long.MinValue;

    /// <summary>
    /// [frontier resume] Tekerlekle duraklatılmış takibi, kullanıcı yeniden "izliyor" sayılabildiğinde geri açar.
    /// <see cref="ResumeFrontierIfNearBottom"/>'ın eşi ama TİK'ten (frontier satır indeksi bilinerek) çağrılır.
    ///
    /// <para>İki koşuldan biri yeter: (a) kullanıcı <b>frontier satırının yanına</b> geri döndü
    /// (<see cref="FrontierResumeThresholdPx"/>), (b) listeye <see cref="FrontierIdleResumeMs"/> boyunca hiç
    /// dokunmadı. (a) niyet okur, (b) unutulmuş bir duraklamayı kendiliğinden kapatır; ikisi de "kullanıcı
    /// scroll'u kazanır" kuralını bozmaz, yalnız duraklamanın SONSUZA DEK sürmesini engeller.</para>
    /// </summary>
    internal void ResumeFrontierIfReengaged(int frontierRow)
    {
        if (!ScrollAnimator.GetIsUserSuppressed(Scroll)) return; // yalnız kullanıcı-duraklattıysa (edge) — ping-pong yok
        if (!IsNearFrontier(frontierRow) && !IsIdle()) return;
        ClearFrontierSuppression();
    }

    private bool IsIdle() =>
        _lastUserScrollAtMs != long.MinValue && NowMs() - _lastUserScrollAtMs >= FrontierIdleResumeMs;

    /// <summary>Frontier satırı görünür pencereye (ya da ona <see cref="FrontierResumeThresholdPx"/> kadar yakın
    /// bir bantla) düşüyor mu. Metrics yoksa karar verilemez → hayır.</summary>
    private bool IsNearFrontier(int frontierRow)
    {
        if (Metrics is not { } metrics || frontierRow < 0 || frontierRow >= metrics.RowCount) return false;
        double rowTop = metrics.OffsetOfRow(frontierRow);
        double rowBottom = rowTop + metrics.RowHeight;
        double viewTop = Scroll.VerticalOffset - FrontierResumeThresholdPx;
        double viewBottom = Scroll.VerticalOffset + Scroll.ViewportHeight + FrontierResumeThresholdPx;
        return rowBottom > viewTop && rowTop < viewBottom;
    }

    /// <summary>İki suppress'i TEK yoldan temizler (ScrollAnimator per-target flag + arbiter regional bit) —
    /// ayrışmaları imkânsız kalsın diye her geri-açılma yolu buradan geçer.</summary>
    private void ClearFrontierSuppression()
    {
        ScrollAnimator.ClearUserSuppressed(Scroll);
        Arbiter?.Resume(ScrollPanel.Frontier);
    }

    internal void ResumeFrontierIfNearBottom()
    {
        if (!ScrollAnimator.GetIsUserSuppressed(Scroll)) return;   // yalnız kullanıcı-duraklattıysa (edge) — ping-pong yok
        if (DistanceFromBottom() > FrontierResumeThresholdPx) return; // yalnız near-bottom'da — uzaktayken duraklı kal
        ClearFrontierSuppression();                                // iki suppress TEK yoldan (bkz. o metodun notu)
    }

    /// <summary>[T59] Karta tıklama — follow durur, satır 90ms sonra %35 üst-marjla görünür kılınır (Ek A-11).</summary>
    public void SelectRow(int rowIndex) => _follow?.SelectRow(rowIndex);

    /// <summary>[T59] Seçim kalkar — follow kaldığı yerden sürer.</summary>
    public void ClearSelection() => _follow?.ClearSelection();

    // [T59] ScrollAnimator'a sarar: süre/eğri Foundation'dan, motion sinyali ÇAĞRI ANINDA taze okunur (sözleşme).
    // design-v1 §1.3: "yer değiştirme" = ease-in-out. Scroll'un kendi bir süre token'ı YOK (yalnız throttle/
    // dead-band kadansı verilmiş) — 4 Foundation süresinden en yakını (Slow) gerekçeli seçim (bkz. ScrollAnimator
    // XML yorumu ve task-5-report.md). [M-1] ConsoleView.AnimateToBottom ile AYNI desen — MotionTokens.
    // AnimateSlowEaseInOut'a çıkarıldı (kopya YASAK, CLAUDE.md).
    private bool AnimateScrollTo(double target) =>
        MotionTokens.AnimateSlowEaseInOut(this, Scroll, Scroll.VerticalOffset, target);

    /// <summary>Verilen VerticalOffset'teki yapışık başlıkları overlay'e ver. ScrollChanged production'da bunu
    /// <c>Scroll.VerticalOffset</c> ile çağırır; testler deterministik olsun diye offset'i doğrudan enjekte eder
    /// (D8: gerçek scroll plumbing'e bağlı değil).
    ///
    /// <para><b>[Final review I-2 / A13.2 "koleksiyon reset YOK"]</b> ItemsSource'a atama ItemsControl için TAM
    /// reset'tir (container teardown + yeniden üretim). T59'un animasyonlu scroll'u burayı HER KAREDE çağırdığından
    /// atama yalnız yapışık küme GERÇEKTEN değiştiğinde yapılır: <see cref="LayoutMetrics.StickyHeadersAt"/> aynı
    /// adet için hep AYNI (önbelleklenmiş) instance'ı döndürür, burada da referans eşitliği kontrol edilir.</para>
    /// </summary>
    internal void UpdateOverlay(double verticalOffset)
    {
        var stuck = Metrics?.StickyHeadersAt(verticalOffset) ?? NoHeaders;
        if (ReferenceEquals(Overlay.ItemsSource, stuck)) return;
        Overlay.ItemsSource = stuck;
    }

    // ---------------------------------------------------------------- [E4/T48 · E3 fold] liste reveal hero (bo-reveal)

    private void OnGeneratorStatusChanged(object? sender, EventArgs e)
    {
        if (Flow.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated) return;
        if (!_revealPending) return;
        _revealPending = false;
        // StatusChanged bir layout pass'ının İÇİNDE ateşlenir; ContentPresenter'ların ProjectRow çocukları o an
        // henüz measure edilmemiş olabilir → layout tamamlanınca (Loaded önceliği) oyna. Testler PlayRevealStagger'ı
        // doğrudan çağırır (realize sonrası, deterministik).
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(PlayRevealStagger));
    }

    /// <summary>
    /// [E4/T48 · E3 fold — GraphView.PlayRevealStagger deseni] Liste satırlarını KADEMELİ belirtir (bo-reveal:
    /// 10ms/satır, 380ms tavan — <see cref="ProjectRow.RevealDelayMs"/>) ve bu pencere boyunca
    /// <see cref="RevealHeroKey"/> ("sync-reveal") hero'sunu TUTAR (DD9: graf+liste frontier AYNI hero — re-entrant,
    /// graf reveal ile birlikte oynar). Reduced-motion VEYA başka bir hero sürerken satırlar ANİ yerleşir (hero
    /// tutulmaz — GraphView deseni). Hero, en geç biten satırın reveal'i (<c>maxDelay + RevealMs</c>) tamamlanınca
    /// generation-guarded bir <see cref="DispatcherTimer"/> ile bırakılır.
    ///
    /// <para><b>[E3 fix — kritik]</b> Release tetiği bir <see cref="DispatcherTimer"/>'dır; <c>Completed</c>-after-
    /// <c>BeginAnimation</c> ÖLÜ yola GERİ DÖNÜLMEZ (o handler gerçek-HWND WPF'te HİÇ ateşlenmez → takılı hero).
    /// Timer generation-guarded (<see cref="ReleaseRevealHeroIfCurrent"/>): reveal #1 sürerken hızlı bir ikinci
    /// SetGroups gelirse #1'in timer'ı ateşlense bile #2'nin taze hero'suna DOKUNMAZ.</para>
    ///
    /// <para><b>[A13.2]</b> ItemsSource reset/Clear YOK (I-2 fix korunur) — yalnız satır Opacity/Y primitive'i
    /// animate edilir (virtualization zaten KAPALI, ScrollUnit=Pixel). Aşağıdaki layout zorlaması da bu kurala
    /// tabidir: <c>UpdateLayout</c> koleksiyona DOKUNMAZ (container teardown yok), yalnız var olan container'ları
    /// measure ettirir.</para>
    ///
    /// <para><b>[A13/B3 · E5]</b> Bu metot, <see cref="CollectRows"/>'u çağırmadan ÖNCE layout'u zorlar — böylece
    /// kapsamı (hangi satırların reveal aldığı) bir DISPATCHER ÖNCELİĞİ VARSAYIMINA bırakmaz. <b>Ölçülen sınır:</b>
    /// bugünkü tek üretim tetiği <see cref="OnGeneratorStatusChanged"/>'in <c>DispatcherPriority.Loaded</c>
    /// ertelemesidir ve <c>Loaded</c>(6) &lt; <c>Render</c>(7) olduğundan otomatik layout turu ZATEN önce koşar;
    /// yani düşme penceresi üretimde bugün AÇILMIYOR (bkz. task-B3-report.md "Fix round 1 / E5" ölçümü). Zorlama
    /// bu yüzden bir kusur düzeltmesi DEĞİL, o pinlenmemiş varsayıma olan bağımlılığı kaldıran bir sertleştirmedir:
    /// tetik bir gün <c>Background</c>'a kaysa ya da senkron çağrılsa satırlar sessizce düşerdi.
    /// <see cref="StickyRevealTests.A_reveal_driven_while_layout_is_dirty_still_reaches_every_row"/> tam olarak bunu
    /// pinler (layout kirliyken sürülen reveal).</para>
    /// </summary>
    internal void PlayRevealStagger()
    {
        // [A13/B3 · E5] Container'lar ZORLA üretilir. Virtualization KAPALI olduğundan TEK bir senkron layout turu
        // tüm ContentPresenter'ları measure eder ve her birinin ProjectRow çocuğunu kurar (ölçüm:
        // ListRealizationPerfTests.RealizeOnce — UpdateLayout'tan sonra realize == N). Layout zaten temizse NO-OP'tur
        // (ölçüldü: n=500'de 0,225 ms, CollectRows yürüyüşü dâhil), yani normal (HWND) yolda ek maliyet yoktur.
        // NOT: UIElement.UpdateLayout() elemana kapsanmaz — ContextLayoutManager'ın tamamını sürer.
        Flow.UpdateLayout();
        var rows = CollectRows();

        // [W2 fold] Önceki hero + bekleyen release'i bırak, yeni kuşağı damgala, hero'yu al (başka hero sürüyorsa
        // animate düşer → ani sonuç). Muhasebe GraphView ile ORTAK: bkz. RevealStagger.Begin.
        var (animate, gen) = _reveal.Begin(AnimationsEnabledProvider(), ActiveHeroCoordinator, RevealHeroKey);

        double maxDelay = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].PlayReveal(i, animate);  // index = kümülatif satır (başlık sayılmaz) — BuildApp.jsx:503 revealIndex++
            double delay = ProjectRow.RevealDelayMs(i);
            if (delay > maxDelay) maxDelay = delay;
        }

        _reveal.ScheduleRelease(maxDelay, ProjectRow.RevealMs, gen);
    }

    /// <summary>Flow'un ŞU AN realize olmuş <see cref="ProjectRow"/> container'ları, in-flow satır sırasında.
    /// <para>Başlıklar atlanır — <b>meşru</b>: reveal index'i yalnız satırları sayar (BuildApp.jsx:503).</para>
    /// <para><b>[A13/B3 · E5] Bu metot KISMİ bir sonuç DÖNEBİLİR</b> (container üretilmemiş ya da container'ın
    /// <see cref="ProjectRow"/> çocuğu henüz realize değilse o satır listeye girmez) — kendi başına bir kapsam
    /// garantisi VERMEZ. Garantiyi çağıran kurar: <see cref="PlayRevealStagger"/> ondan hemen önce layout'u zorlar
    /// ve virtualization KAPALI olduğu için tek bir tur tüm satırları realize eder.</para>
    /// <para>Eski doc comment burada "o satır atlanır (<b>bir sonraki reveal onu yakalar</b>)" diyordu — bu gerekçe
    /// YANLIŞTI ve kaldırıldı: <see cref="PlayRevealStagger"/> yalnız <see cref="SetGroups"/>'tan sürülür, o da
    /// yalnız TOPOLOJİ değiştiğinde koşar (<c>RunViewModel.OnWorkspaceTopology</c> imza guard'ı, A13/B3 · E4) —
    /// "bir sonraki reveal" hiç gelmeyebilir.</para>
    /// <para><b>İkinci çağıran uyarısı:</b> <see cref="RevealRows"/> (test yüzeyi) bu metodu layout zorlamadan
    /// çağırır ve bu BİLEREK böyledir — test yüzeyi kendini iyileştirirse ölçtüğü durumu maskeler. Üretime yeni bir
    /// çağıran eklenirse layout zorlamasını O DA yapmalıdır.</para></summary>
    private IReadOnlyList<ProjectRow> CollectRows()
    {
        var generator = Flow.ItemContainerGenerator;
        var rows = new List<ProjectRow>();
        for (int i = 0; i < Flow.Items.Count; i++)
        {
            if (Flow.Items[i] is HeaderEntry) continue;
            if (generator.ContainerFromIndex(i) is not DependencyObject container) continue;
            if (FindDescendant<ProjectRow>(container) is { } row) rows.Add(row);
        }
        return rows;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
            if (FindDescendant<T>(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }

    /// <summary>[E3 fix deseni] Reveal tamamlanınca hero'yu bırakan generation-guarded karar (bkz.
    /// <see cref="RevealStagger.ReleaseIfCurrent"/>). Test bunu doğrudan çağırır.</summary>
    internal void ReleaseRevealHeroIfCurrent(int gen) => _reveal.ReleaseIfCurrent(gen);

    // test yüzeyi (GraphView deseni)
    internal int RevealGeneration => _reveal.Generation;
    internal bool HasPendingRevealRelease => _reveal.HasPendingRelease;
    internal IReadOnlyList<ProjectRow> RevealRows => CollectRows();
    /// <summary>[T2 fix-1 · I-D test yüzeyi] Follow/seçim controller'ı — <c>SetGroups</c> boyunca AYNI nesne
    /// kalmalıdır (yeniden yaratmak throttle saatini ve seçim durumunu sıfırlar; bkz.
    /// <see cref="FollowScrollController.Rebind"/>).</summary>
    internal FollowScrollController? FollowController => _follow;
    /// <summary>[E5/T47 test yüzeyi] Satır akışı paneli — ok-tuşu gezinme modu (DirectionalNavigation) buradan pinlenir.</summary>
    internal ItemsControl RowFlow => Flow;

    private sealed class EntrySelector(StickyLayerList owner) : DataTemplateSelector
    {
        public DataTemplate Header { get; } = owner.HeaderTemplate;
        public DataTemplate Row { get; } = owner.RowTemplate;

        public override DataTemplate SelectTemplate(object item, System.Windows.DependencyObject container)
            => item is HeaderEntry ? Header : Row;
    }
}
