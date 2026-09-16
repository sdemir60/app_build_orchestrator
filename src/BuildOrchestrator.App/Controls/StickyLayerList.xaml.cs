using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
    /// overlay'in <see cref="StuckHeader"/>'ı ile AYNI <c>Name</c>/<c>RowCount</c>/<c>SlotIndex</c> alanlarını
    /// taşır ki tek şablon (<c>HeaderTemplate</c>) ve TEK tıklama kablosu (<see cref="HeaderRoot_MouseLeftButtonUp"/>)
    /// ikisine de bağlansın [v1.17.0 §2.4]. <paramref name="SlotIndex"/> varsayılanı (-1) yalnız derlemeyi
    /// geriye uyumlu tutar — <see cref="SetGroups(IReadOnlyList{LayerGroup}, bool)"/> HER ZAMAN gerçek slotu verir.</summary>
    public sealed record HeaderEntry(string Name, int RowCount, int SlotIndex = -1);

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
        // Burada BAŞKA bir iş yapılmaz: frontier follow'un geri açılması kaydırma KONUMUNA değil yalnız boşta
        // penceresine bağlıdır (bkz. ResumeFrontierIfIdle) — bu olay kullanıcının kendi sürüklemesinde de aktığı
        // için buraya bağlanan her resume, duraklamayı kullanıcının elinden alırdı.
        Scroll.ScrollChanged += (_, _) => UpdateOverlay(Scroll.VerticalOffset);
        // [T59 · review round 1 I-1] Kullanıcı listeyi kaydırdığı anda uçuştaki follow/seçim-scroll animasyonu
        // iptal olur (feasibility §3.3 — WPF'te girdinin animasyonu otomatik iptal etmesi YOK, tarayıcının
        // aksine) + suppress bayrağı damgalanır + merkezi arbiter'a haber verilir (bölgesel suppress — yalnız bu
        // panel duraklar, konsol/stream akmaya devam. Arbiter null ise izole test, no-op) + boşta-geri-açılma
        // penceresi (FrontierIdleResumeMs) sıfırlanır.
        // Sinyalin ÜÇ girdi kanalı (tekerlek · kaydırma çubuğu · gezinme tuşları) konsol ve event stream ile
        // AYNI kablodan gelir: UserScrollSignal. Kök (this) hem Scroll'u hem Overlay'i kapsadığı için tünelleyen
        // tekerlek ikisinde de burada yakalanır; çubuk olayı (ScrollBar.ScrollEvent) Scroll'un şablonundan
        // baloncuklanarak ulaşır. Yarım kablo YASAK: yalnız tekerleği dinlemek, çubuğu sürükleyen kullanıcıyı
        // takip eden listenin geri çekmesi demekti (bkz. UserScrollSignal doc'u, FrontierFollowPauseChannelsTests).
        UserScrollSignal.Wire(this, OnUserScroll);
        // [v1.17.0 §2.4] Overlay artık hit-test'e AÇIK (başlıklar tıklanabilir) — bu, imlecin yığılmış başlık
        // bandındayken tekerlek olayının Scroll'a hiç ULAŞMAMASI riskini doğurur (Overlay, ScrollViewer'ın
        // KARDEŞİDİR, ATASI DEĞİL — routed event orada durur, aşağı Scroll'a bubble ETMEZ). ForwardWheelToScroll
        // olayı Scroll'un KENDİ (bubbling) MouseWheel'ine yeniden yükseltir (standart WPF telafisi —
        // ScrollViewer'ın sınıf handler'ı bunu normal biçimde işler). Muhasebe orada TEKRARLANMAZ: kökteki
        // UserScrollSignal kablosu (yukarıdaki paragraf) Overlay'in de atasıdır, tünel ona ÖNCE uğramıştır.
        // Yeniden yükseltilen olay bubbling'dir, tünel DEĞİL — kökteki kablo ikinci kez ateşlenmez.
        Overlay.PreviewMouseWheel += ForwardWheelToScroll;
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

        // [review round 1 · M-2] Başlık var/yok kararı VE slot sırası TEK kaynaktan (Metrics.Headers) okunur —
        // "adı boş mu" kuralını (LayoutMetrics zaten LayerSpec.HasHeader'da uyguladı) ve slot sayacını burada
        // İKİNCİ KEZ yazmak (eski hâl: `!string.IsNullOrEmpty(g.Name)` + paralel `slotIndex++`) iki karar
        // yerinin AYNI KURALI iki farklı yerde tekrarlamasıydı — sürüklenebilirdi. Metrics.Headers, groups'la
        // AYNI sırada (yalnız başlıklı katmanlar, layer sırasında) kurulduğundan headerCursor'ı groups'la
        // LOCKSTEP yürütmek yeterli: her katmanda (headerCursor bir sonraki başlığa işaret ediyorsa VE o
        // başlığın LayerIndex'i şu anki katmanla eşleşiyorsa) o HeaderInfo'nun kendi Name/RowCount/SlotIndex'i
        // kullanılır — hiçbir alan groups'tan YENİDEN türetilmez.
        var entries = new List<object>();
        int headerCursor = 0;
        int layerIndex = 0;
        foreach (var g in groups)
        {
            if (headerCursor < Metrics.Headers.Count && Metrics.Headers[headerCursor].LayerIndex == layerIndex)
            {
                var h = Metrics.Headers[headerCursor++];
                entries.Add(new HeaderEntry(h.Name, h.RowCount, h.SlotIndex));
            }
            entries.AddRange(g.Rows);
            layerIndex++;
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
        // [ölçülen kusur] Satır yüzeyi beliriş boyunca KAPALI kalır. Reveal, container üretimi bitince
        // `DispatcherPriority.Loaded`(6) ile kuyruğa girer; render ise `Render`(7), yani DAHA YÜKSEK
        // önceliktedir — sıra bu yüzden "satırları tam opaklıkta çiz → 0'a indir → kademeli aç"tı ve arada
        // gözle görülür bir kare açılıyordu. (Liste zaten doluyken fark edilmiyordu: boyanan içerik bir
        // öncekine benziyordu. Boş listeye gelen bir topolojide ise "gelir, kaybolur, tekrar gelir" olarak
        // görülüyor.) Yüzeyi <see cref="PlayRevealStagger"/> açar — beliriş reddedilse de açar, aksi halde
        // liste kalıcı görünmez kalırdı. Sessiz tazeleme (reveal:false) yüzeye DOKUNMAZ: her tuş vuruşunda
        // bir kare kaybolmasın.
        // Graf bu işi düğüm başına ZATEN böyle yapar (GraphView düğüm görselini `Opacity = 0` ile doğurur ve
        // reveal'i SetGraph'tan SENKRON sürer); liste, container'ları WPF ürettiği için aynı şeyi yüzey
        // seviyesinde yapar — iki sahip artık aynı ilkede.
        Flow.Opacity = reveal ? 0 : 1;
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

    /// <summary>[frontier resume] Boşta-geri-açılma penceresi: listeye bu kadar süre DOKUNULMAZSA takip
    /// kendiliğinden sürer. Follow throttle'ının (550 ms) birkaç katı — okumakta olan bir kullanıcıyı listenin
    /// altından çekecek kadar kısa DEĞİL, etkileşimi bırakmış bir kullanıcıyı takibi elle geri açmaya
    /// zorlayacak kadar uzun da değil.</summary>
    internal const long FrontierIdleResumeMs = BottomAnchorDecision.IdleResumeMs; // tek kaynak orada

    /// <summary>[D8] Boşta penceresinin saat kaynağı — testler deterministik sürer.</summary>
    internal Func<long> NowMs { get; set; } = () => Environment.TickCount64;

    private long _lastUserScrollAtMs = long.MinValue;

    /// <summary>
    /// [frontier resume] Kullanıcı kaydırmasıyla duraklatılmış takibi geri açar — <b>TEK koşul</b>: listeye
    /// <see cref="FrontierIdleResumeMs"/> boyunca hiç dokunulmamış olması. Koşarken 200 ms'lik tick'ten çağrılır
    /// (<c>MainWindow.FollowFrontier</c>); temizlenen şey İKİ suppress'tir (<see cref="ScrollAnimator"/> per-target
    /// bayrağı + arbiter'ın regional bit'i) ve bir sonraki tick takibi sürdürür.
    ///
    /// <para><b>Kaydırma KONUMU karara girmez.</b> Eskiden iki konum yolu daha vardı ve ikisi de bu pencereyi
    /// atlıyordu: frontier satırı görünür pencereye 48 px yakınsa, ya da liste dibine 48 px kalmışsa duraklama
    /// ANINDA kalkardı (ikincisi her <c>ScrollChanged</c>'de, yani kullanıcının KENDİ sürüklemesinin ürettiği
    /// olaylarda da). Gerekçe "ilgi çekici yere dönen kullanıcı takibi geri istiyordur"dı; sahada ölçülen sonuç
    /// tersiydi — konum "geri döndüm" ile "burada okuyorum"u ayırt edemediği için derlenen satırla aynı ekranda
    /// olan kullanıcının duraklaması her tick'te siliniyor, takip viewport'u sürekli geri alıyordu. Aynı jest
    /// panelin neresinde yapıldığına göre farklı davranamaz: konum yolları kaldırıldı, kapı tektir.</para>
    ///
    /// <para><b>Yo-yo YOK:</b> edge-tetiklidir — yalnız follow ŞU AN kullanıcı tarafından duraklatılmışken
    /// davranır, zaten sürerken no-op; bu yüzden follow ANİMASYONUNUN kendi ScrollChanged'leri hiçbir şey
    /// tetiklemez. Suppress'i temizlemek listeyi HAREKET ETTİRMEZ; gerçek re-engagement bir sonraki tick'te
    /// <see cref="FollowScrollController"/>'ın 550 ms throttle + 54 px dead-band'ine tabidir.</para>
    /// </summary>
    internal void ResumeFrontierIfIdle()
    {
        if (!ScrollAnimator.GetIsUserSuppressed(Scroll)) return; // yalnız kullanıcı-duraklattıysa (edge) — ping-pong yok
        if (!IsIdle()) return;
        ClearFrontierSuppression();
    }

    private bool IsIdle() =>
        _lastUserScrollAtMs != long.MinValue && NowMs() - _lastUserScrollAtMs >= FrontierIdleResumeMs;

    /// <summary>İki suppress'i TEK yoldan temizler (ScrollAnimator per-target flag + arbiter regional bit) —
    /// ayrışmaları imkânsız kalsın diye her geri-açılma yolu buradan geçer.</summary>
    private void ClearFrontierSuppression()
    {
        ScrollAnimator.ClearUserSuppressed(Scroll);
        Arbiter?.Resume(ScrollPanel.Frontier);
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

    // ---------------------------------------------------------------- [v1.17.0 §2.4] katman başlığı = gezinme kontrolü

    /// <summary>
    /// "Kullanıcı listeyi kaydırdı" olayının TEK tüketicisi — üç girdi kanalını da (tekerlek, kaydırma çubuğu,
    /// gezinme tuşları) <see cref="UserScrollSignal"/> kökten buraya bağlar. Üç şeyi birlikte yapar: uçuştaki
    /// follow/seçim-scroll animasyonunu iptal eder + per-target suppress bayrağını kaldırır
    /// (<see cref="ScrollAnimator.CancelForUser"/>), boşta-geri-açılma damgasını
    /// (<see cref="_lastUserScrollAtMs"/>) tazeler, ve merkezi arbiter'a bölgesel suppress bildirir
    /// (<see cref="Arbiter"/>, null ise izole test no-op).
    ///
    /// <para><b>Ölçülen kusur — çubuk kanalı:</b> sinyal yalnız tekerlekten alınıyordu. Kaydırma çubuğunun
    /// başlığını sürüklemek ya da oluğa tıklamak hiç tekerlek olayı doğurmadığı için liste "kimse dokunmadı"
    /// sanıp derlenen satırı takip etmeye devam ediyor, kullanıcıyı sürüklediği yerden geri çekiyordu; panel
    /// ancak çubuk bırakılıp takip throttle'ı oturunca sakinleşiyordu. Konsol ve event stream aynı kusuru
    /// <see cref="UserScrollSignal"/> ile çözmüştü — liste o kablonun dışında kalmıştı.</para>
    ///
    /// <para><b>Ölçülen kusur — overlay tekerleği (review round 1 · I-1):</b> overlay hit-test'e açılınca
    /// (v1.17.0 §2.4) yığılmış başlık bandı üstündeki tekerlek Scroll'a hiç uğramaz oldu ve
    /// <see cref="ForwardWheelToScroll"/> yalnız asıl KAYDIRMAYI yeniden yükseltiyordu — bu üç bookkeeping'i
    /// ATLAYARAK. Kablo kökte (this) olduğu için o bant da artık aynı tünelden geçer.</para>
    /// </summary>
    private void OnUserScroll()
    {
        ScrollAnimator.CancelForUser(Scroll);
        MarkFollowPausedByUser();
    }

    /// <summary>"Kullanıcı listeyi taşıdı" muhasebesinin iptalden BAĞIMSIZ yarısı: boşta-geri-açılma damgası +
    /// arbiter'ın bölgesel suppress'i. Ham girdi (<see cref="OnUserScroll"/>) ve katman başlığı jump'ı
    /// (<see cref="ReleaseHeader"/>) ikisi de buradan geçer — biri uçuştaki hareketi iptal eder, öbürü kendi
    /// hareketini BAŞLATIR, ama takip açısından ikisi AYNI olaydır.</summary>
    private void MarkFollowPausedByUser()
    {
        _lastUserScrollAtMs = NowMs();
        Arbiter?.NotifyUserScroll(ScrollPanel.Frontier);
    }

    /// <summary>Overlay'e düşen bir tekerlek olayının asıl KAYDIRMASINI Scroll'un KENDİ (bubbling)
    /// <c>MouseWheel</c>'ine yeniden yükseltir. Overlay, ScrollViewer'ın görsel ATASI DEĞİL KARDEŞİDİR — routed
    /// event doğal olarak Overlay'in kendi ebeveynine (Grid) bubble eder, Scroll'a hiç uğramaz. <c>RaiseEvent</c>
    /// Scroll'un sınıf handler'ını (ScrollViewer'ın kendi <c>OnMouseWheel</c>'i) normal yoldan tetikler — üçüncü
    /// parti bir kütüphane olmadan nested-scroll telafisi için standart WPF deseni.
    ///
    /// <para>Bookkeeping BURADA DEĞİL: <see cref="OnUserScroll"/> kökten (this) tünelleyen
    /// <c>PreviewMouseWheel</c> ile ZATEN çağrılmıştır — kök, Overlay'in de atasıdır ve tünel ona ÖNCE uğrar.
    /// Burada ikinci kez çağırmak aynı muhasebenin kopyası olurdu. Yeniden yükseltilen olay bubbling
    /// <c>MouseWheel</c>'dir, tünel DEĞİL; dolayısıyla kökteki kablo ikinci kez ateşlenmez.</para></summary>
    private void ForwardWheelToScroll(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;
        Scroll.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = Mouse.MouseWheelEvent,
        });
        e.Handled = true;
    }

    /// <summary>[review round 1 · M-5] Başlığa BASILAN element'i yakalar — <see cref="HeaderRoot_MouseLeftButtonUp"/>
    /// yalnız AYNI element hâlâ yakalamayı tutuyorsa (basış BURADA başladı) tıklamayı jump'a çevirir. Yakalama
    /// olmadan bir satıra basıp başlığın üstüne sürükleyip bırakmak (satır kendi tıklamasını iptal ettiğinde) da
    /// jump tetiklerdi — WPF <c>MouseLeftButtonUp</c> yalnız BIRAKMA noktasındaki elementi bilir, BASMA noktasını
    /// değil.</summary>
    private void HeaderRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement header) return;
        _pressedHeaderSlot = HeaderSlot(header.DataContext); // [final review M-2] bırakmada AYNI slot aranır
        header.CaptureMouse();
    }

    // [Final review M-2] Basılan başlığın slotu — bırakmada AYNI slot beklenir. Yakalama Border'a bağlıdır,
    // veriye değil: basış ile bırakma arasında container geri dönüştürülürse (Recycling) yakalama başka bir
    // katmanın başlığına bağlanmış Border'da kalır. null = basılı başlık yok.
    private int? _pressedHeaderSlot;

    private static int HeaderSlot(object? dataContext) => dataContext switch
    {
        HeaderEntry h => h.SlotIndex,
        StuckHeader s => s.SlotIndex,
        _ => -1,
    };

    /// <summary>
    /// [v1.17.0 §2.4] Katman başlığına (in-flow VEYA yapışık overlay — AYNI <c>HeaderTemplate</c>, AYNI kablo)
    /// tıklama: o grubun ilk (görünür/filtrelenmiş) satırını yığılmış başlıkların hemen altına getirir. Aritmetik
    /// SAF <see cref="LayoutMetrics.JumpTargetForHeader"/>'da; burada yalnız hangi slotun tıklandığını okuyup
    /// mevcut smooth-scroll altyapısını (<see cref="AnimateScrollTo"/> — reduced-motion'da anında) çağırır.
    /// Seçim/filtre/konsol/graf'a HİÇ dokunmaz — yalnız scroll; ve her kullanıcı kaydırması gibi frontier
    /// takibini duraklatır.
    ///
    /// <para><b>[review round 1 · M-5]</b> Basış BU başlıkta başlamadıysa (bkz. <see cref="HeaderRoot_MouseLeftButtonDown"/>)
    /// jump tetiklenmez — bir satıra basıp başlığın üstüne sürükleyip bırakmak artık zararsızdır.</para>
    ///
    /// <para><b>[final review M-2]</b> Yakalama bırakmayı imleç nerede olursa olsun başlığa yönlendirir; bu yüzden
    /// bırakma konumu da başlığın sınırları İÇİNDE olmalıdır (basıp dışarı sürükleyip bırakmak native tıklamada
    /// olduğu gibi iptaldir) ve başlığın slotu basış anındakiyle AYNI olmalıdır (geri dönüştürülmüş container).</para>
    /// </summary>
    private void HeaderRoot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement header) ReleaseHeader(header, e.GetPosition(header));
    }

    /// <summary>[final review M-2] Bırakma kararı — <paramref name="positionInHeader"/> üretimde
    /// <c>e.GetPosition(header)</c>'dır. <c>internal</c>: gerçek imleç konumu headless'ta simüle edilemez, testler
    /// üretimin çağırdığı bu metodu doğrudan sürer (ConsoleView.UpdateHoverBand ile AYNI desen).</summary>
    internal void ReleaseHeader(FrameworkElement header, Point positionInHeader)
    {
        bool pressStartedHere = header.IsMouseCaptured;
        if (pressStartedHere) header.ReleaseMouseCapture();
        int? pressedSlot = _pressedHeaderSlot;
        _pressedHeaderSlot = null;
        if (!pressStartedHere || Metrics is null) return; // [M-5] basış BAŞKA bir elementte başladı — jump YOK.

        bool inside = positionInHeader.X >= 0 && positionInHeader.Y >= 0
            && positionInHeader.X < header.ActualWidth && positionInHeader.Y < header.ActualHeight;
        if (!inside) return; // basıp dışarı sürükleyip bıraktı — iptal

        int slotIndex = HeaderSlot(header.DataContext);
        if (slotIndex < 0 || slotIndex != pressedSlot) return; // basıştan beri container başka bir slota bağlandı
        AnimateScrollTo(Metrics.JumpTargetForHeader(slotIndex));
        // Jump bir KULLANICI kaydırmasıdır — takip duraklar, tekerlekte olduğu gibi. Sıra zorunlu: AnimateTo
        // "yeni programatik hareket" diyerek suppress bayrağını temizler, bu yüzden bayrak hareket başladıktan
        // SONRA kurulur. CancelForUser DEĞİL: o, az önce başlayan jump'ın kendisini iptal ederdi.
        ScrollAnimator.SuppressForUser(Scroll);
        MarkFollowPausedByUser();
    }

    /// <summary>Header ToolTip'i — <c>Name</c>'den <see cref="ViewModels.InteractionText.JumpToLayer"/> ile
    /// üretilir. Bir <see cref="Loaded"/>-bazlı TEK SEFERLİK atama DEĞİL, XAML <c>Binding</c>'dir: in-flow
    /// container'lar <see cref="FixedHeightVirtualizingPanel"/>'de GERİ DÖNÜŞTÜRÜLÜR (Recycling) — DataContext
    /// değişince <c>Loaded</c> yeniden ATEŞLENMEYEBİLİR ve tooltip eski katmanın adında asılı kalırdı; Binding
    /// her DataContext değişiminde taze kalır.</summary>
    public static readonly System.Windows.Data.IValueConverter JumpTooltipConverter = new JumpTooltipConverterImpl();

    private sealed class JumpTooltipConverterImpl : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            ViewModels.InteractionText.JumpToLayer((string)value);

        public object ConvertBack(object value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
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
        // [D3/T5 · design v1.13.2 §2.4/§9] Reveal GERÇEKTEN oynadığı an — bu metodun HER çağrılışı, yalnız
        // SetGroups(reveal:true) yolundan (Sync, workspace kaydı) gelir — VE seçim yokken liste scroll'u
        // yumuşak 0'a döner: graf da reveal'ini yeniden oynadığından ikisi birlikte "sıfırdan listelendi"
        // okunur. Build/Rebuild/Clean/Resolve ve satırdan tetiklenenler bu metodu HİÇ çağırmaz (reveal:false
        // ya da hiç SetGroups yok) — o işlemler scroll'a dokunmaz (kullanıcı kararı: imlecin altındaki satır
        // kaçmasın, o işlemlerde zaten işaretleme koreografisi anlatıyor).
        //
        // "Seçim yok" bilgisi TEK kaynaktan okunur: FollowScrollController.IsFollowing — yeni bir seçim kaynağı
        // İCAT EDİLMEZ (ClearSelection/SelectRow zaten burada, MainWindow.UpdateFrontierSelection besler).
        // _follow SetGroups'ta kurulur ve bu metot yalnız SetGroups sonrasında (senkron ya da testten doğrudan)
        // çağrıldığından pratikte hep dolu olsa da, null ise "seçim yok" varsayılır (0'a dönmek zararsız).
        //
        // Animasyon yolu ZATEN VAR (AnimateScrollTo, ~satır 291) — reduced-motion altında kayma ANINDA olur
        // (ScrollAnimator'ın mevcut davranışı), burada ayrı bir dal YAZILMAZ.
        if (_follow is null || _follow.IsFollowing) AnimateScrollTo(0);

        // [A13/B3 · E5] Container'lar ZORLA üretilir. Virtualization KAPALI olduğundan TEK bir senkron layout turu
        // tüm ContentPresenter'ları measure eder ve her birinin ProjectRow çocuğunu kurar (ölçüm:
        // ListRealizationPerfTests.RealizeOnce — UpdateLayout'tan sonra realize == N). Layout zaten temizse NO-OP'tur
        // (ölçüldü: n=500'de 0,225 ms, CollectRows yürüyüşü dâhil), yani normal (HWND) yolda ek maliyet yoktur.
        // NOT: UIElement.UpdateLayout() elemana kapsanmaz — ContextLayoutManager'ın tamamını sürer.
        Flow.UpdateLayout();
        var rows = CollectRows();

        // Yüzey AÇILIR: bundan sonra görünürlüğü satırların kendi opaklığı yönetir (aşağıdaki PlayReveal).
        // Beliriş reddedilse de (azaltılmış hareket / başka hero) burası koşar — yoksa liste kalıcı olarak
        // görünmez kalırdı. Tek karede olduğu için arada render YOKTUR: satırlar hiç tam opak boyanmaz.
        Flow.Opacity = 1;

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

    /// <summary>[test yüzeyi] Satır akışının opaklığı. Beliriş bekleyen bir liste 0'dadır: render, satırları tam
    /// opaklıkta gösteren bir kare ÇİZEMEZ (bkz. <see cref="SetGroups(IReadOnlyList{LayerGroup}, bool)"/>).</summary>
    internal double RowSurfaceOpacity => Flow.Opacity;
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
