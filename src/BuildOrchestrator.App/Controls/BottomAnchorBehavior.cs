using System.Windows.Threading;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T59] <see cref="BottomAnchorDecision"/>'ın (saf) kararını gerçek bir scroll host'a (ConsoleView'ın AvalonEdit
/// TextEditor'ı ve EventStreamView'ın ScrollViewer'ı) bağlayan ORTAK orkestratör — "bir bottom-anchor mekanizması"
/// (task-5 talimatı: konsolun mevcut <c>StickToBottom</c>'ıyla ÇAKIŞMAZ, ONUN üstüne kurulur — bkz. ConsoleView.xaml.cs).
///
/// <para><b>Neden delegate-tabanlı (WPF türü YOK):</b> ScrollViewer ve AvalonEdit TextEditor FARKLI tiplerdir; bu
/// sınıf ikisine de duck-typing'siz, WPF'e dokunmadan hizmet eder — bu yüzden testleri <c>[Fact]</c> (STA
/// gerekmez), yalnız gerçek kablajlar (ConsoleView, EventStreamView) <c>[StaFact]</c>'tir.</para>
/// </summary>
public sealed class BottomAnchorBehavior
{
    private readonly Func<double> _getOffset;
    private readonly Func<double> _getExtent;
    private readonly Func<double> _getViewport;
    private readonly Action<double> _scrollInstant;
    private readonly Func<double, bool> _scrollSmooth; // (target) -> animasyon BAŞLADI mı (false = anında atlandı)
    private readonly Action<TimeSpan, Action> _scheduleOnce;
    private readonly double _thresholdPx;
    private readonly Func<bool>? _autoResumeAllowed;
    private readonly Func<bool>? _followAllowed;

    private BottomAnchorState _state = BottomAnchorState.Initial;
    private int _jumpGeneration;
    private int _idleGeneration;
    private bool _steering; // kullanıcı kaydırdı ve bekleme henüz dolmadı — o sürece panel ondadır
    private bool _lastShowPill; // son bildirilen pill görünürlüğü (bkz. Notify)

    /// <summary>IsStuck/IsJumping/ShowPill değiştiğinde ateşlenir — host (ConsoleView) buna göre pill Visibility'sini
    /// / StickToBottom'ı günceller. Bekleme panel dipteyken dolduğunda da ateşlenir: durum aynı kalır ama takip geri gelir
    /// (<see cref="ShouldFollow"/> açılır) ve takibe bağlı işi olan host (konsolun render dilimi kırpması) onu bu bildirimde
    /// okur.</summary>
    public event EventHandler? Changed;

    /// <param name="getOffset">Anlık VerticalOffset (px).</param>
    /// <param name="getExtent">Anlık ExtentHeight (px, toplam içerik yüksekliği).</param>
    /// <param name="getViewport">Anlık ViewportHeight (px).</param>
    /// <param name="scrollInstant">İçerik-büyümesi yakalaması ve bekleme dönüşü — verilen ofsete (dibin kendisi,
    /// <see cref="BottomOffset"/>) ANINDA (animasyonsuz) kaydırır.</param>
    /// <param name="scrollSmooth">Pill tıklaması — yumuşak (ya da reduced-motion'da anında) dibe kaydırır; bir
    /// animasyon BAŞLATILDIYSA true döner (host tipik olarak <see cref="ScrollAnimator.AnimateTo"/>'ya sarar).</param>
    /// <param name="scheduleOnce">560ms "jumping" penceresini zamanlar — testte enjekte edilebilir (D8); üretim
    /// varsayılanı bir <see cref="DispatcherTimer"/>.</param>
    /// <param name="autoResumeAllowed">Dibe KENDİLİĞİNDEN dönmek serbest mi (<c>null</c> = hiç dönme).
    /// Kullanıcı kaydırdıktan sonra <see cref="BottomAnchorDecision.IdleResumeMs"/> boyunca panele hiç
    /// dokunulmazsa dibe dönülür — panel akışı yeniden izlemeye başlar, tıpkı listenin frontier takibinin
    /// aynı süre sonunda geri açılması gibi. Dönüş takibe dönüştür: takip yasakken (<paramref name="followAllowed"/>) bu izin
    /// olsa da dönülmez (<see cref="AutoResumeAllowed"/>).</param>
    /// <param name="followAllowed">[idle-memory · dalga 3 · karar 45] Host şu an akışı İZLEYEBİLİR mi (<c>null</c> = her zaman). İzin
    /// yokken <see cref="ShouldFollow"/> hiç açılmaz: ne zorlanan dip (<see cref="ForceStuck"/>), ne dibe atlama
    /// (<see cref="JumpToBottom"/> — görünüm dibe BİR KEZ gider), ne kullanıcının kendi eliyle dibe inmesi takibi başlatır; bekleme
    /// dönüşü de kurulmaz. Durum (<see cref="IsStuck"/>, pill) her zamanki gibi işler — yalnız akan içerik görünümü çekmez. Konsolun proje
    /// sayfası böyledir: okunan bir logdur, izlenen bir akış değil.</param>
    public BottomAnchorBehavior(
        Func<double> getOffset, Func<double> getExtent, Func<double> getViewport,
        Action<double> scrollInstant, Func<double, bool> scrollSmooth,
        Action<TimeSpan, Action>? scheduleOnce = null, double thresholdPx = BottomAnchorDecision.DefaultThresholdPx,
        Func<bool>? autoResumeAllowed = null, Func<bool>? followAllowed = null)
    {
        _autoResumeAllowed = autoResumeAllowed;
        _followAllowed = followAllowed;
        _getOffset = getOffset;
        _getExtent = getExtent;
        _getViewport = getViewport;
        _scrollInstant = scrollInstant;
        _scrollSmooth = scrollSmooth;
        _scheduleOnce = scheduleOnce ?? DefaultScheduleOnce;
        _thresholdPx = thresholdPx;
    }

    public bool IsStuck => _state.IsStuck;
    public bool IsJumping => _state.IsJumping;

    /// <summary>
    /// <b>Şu anda dibe yapışmak serbest mi</b> — "yeni içerik geldi, görünümü dibe çekeyim mi" sorusunun TEK
    /// yetkili cevabı. Dört koşul birden: takip açık, uçuşta bir atlama yok, direksiyon kullanıcıda değil ve host takibe izin
    /// veriyor (<see cref="FollowAllowed"/>).
    ///
    /// <para>Host'lar bunu okur (konsolun <c>AppendBatch</c>'i dahil) — kendi <see cref="IsStuck"/>
    /// yorumlarını yapmazlar. Konsol tam da bunu yapıyordu: <c>AppendBatch</c> salt <c>IsStuck</c>'a bakıp
    /// her batch'te dibe kaydırıyor, kullanıcının kaydırmasını görmüyordu — derleme sürerken konsol
    /// kaydırılamaz hâle geliyordu.</para>
    /// </summary>
    public bool ShouldFollow => _state.IsStuck && !_state.IsJumping && !_steering && FollowAllowed;

    /// <summary>[idle-memory · dalga 3 · karar 45] Host şu an takibe izin veriyor mu — yapıcının <c>followAllowed</c>'ı, verilmemişse
    /// her zaman. Takibin (<see cref="ShouldFollow"/>) ve bekleme dönüşünün (<see cref="AutoResumeAllowed"/>) ortak kapısı.</summary>
    private bool FollowAllowed => _followAllowed?.Invoke() ?? true;

    /// <summary>Dibe kendiliğinden dönüş şu an serbest mi: host dönüşe izin veriyor (<c>autoResumeAllowed</c>) VE takibe izin veriyor
    /// (<see cref="FollowAllowed"/>). Dönüş takibe dönüştür; takip yasakken görünümü dibe taşımak okuyucunun yerini boşuna değiştirirdi.</summary>
    private bool AutoResumeAllowed => _autoResumeAllowed is not null && _autoResumeAllowed() && FollowAllowed;
    public double DistanceFromBottom => Math.Max(0, BottomOffset - _getOffset());

    /// <summary>Dibin ofseti — "dip"in TEK tanımı: içerik eksi görünür alan (sığan içerikte 0). Uzaklık ondan ölçülür, dibe her gidiş
    /// (atlama, içerik büyümesi yakalaması, bekleme dönüşü) onu hedefler. Host da oturmuş bir kaydırma konumunun üst sınırı olarak okur
    /// (konsol: yükleyicinin okuduğu konum buna kenetlenir — +∞ ya da dibin ötesindeki geçici bir istek dipte sayılır). İçerik
    /// yüksekliği yalnız yerleşimde güncellenen bir host (ScrollViewer'ın <c>ExtentHeight</c>'ı — event stream) eşzamanlı bir yakalamada
    /// henüz eski dibi hedefler; yeni dibe, yerleşimin ardından gelen kendi kaydırma olayıyla (içerik büyümesi) iner.</summary>
    public double BottomOffset => Math.Max(0, _getExtent() - _getViewport());
    public bool ShowPill => BottomAnchorDecision.ShouldShowPill(_state, DistanceFromBottom, _thresholdPx);

    /// <summary>
    /// [B2 fix 2 · madde 1] <b>Son DUYURULAN pill görünürlüğü</b> (<see cref="Changed"/> anında kaydedilen <see cref="ShowPill"/>) — host'un
    /// pill'i ve pill'e bağlı her şey (konsolun prompt imleci) bunu okur, canlı <see cref="ShowPill"/>'i değil. Canlı değer o anki uzaklıktır:
    /// izlenen belgede ekleme ile dibe kaydırmanın uygulanması arasında bir an eşiği aşar ve onu okuyan her şey yanıp sönerdi (pill'in kendi
    /// kuralı da bu yüzden kaydırma olaylarından okunur — <see cref="RefreshPill"/>). Duyurulan değer her duyuruda ilerler
    /// (<see cref="RaiseChanged"/>): <see cref="Notify"/>'ın gördüğü değişimler, zorlama (<see cref="ForceStuck"/>), atlamanın başı ve sonu,
    /// bekleme dönüşü — içerik iki duyuru arasında büyüse de değişmez.
    /// </summary>
    public bool PillShown => _lastShowPill;

    /// <summary>
    /// [B2 fix 3 · madde 9] <b>Okuyucu CANLI dipte mi</b> — takip durumu dipte (<see cref="IsStuck"/>), pill duyurulmamış (<see cref="PillShown"/>)
    /// ve uçuşta bir atlama yok (<see cref="IsJumping"/>). Dibe bağlı bir göstergenin (konsolun prompt imleci) TEK tanımı: pill görünürken okuyucu
    /// canlı dipten uzaktadır; atlama sürerken görünüm henüz dipte değildir (durum iyimser biçimde "dipte"dir — <see cref="BottomAnchorDecision.BeginJump"/>).
    /// Pill'le aynı duyurulan değeri okur, canlı uzaklığı değil (gerekçe: <see cref="PillShown"/>).
    /// </summary>
    public bool AtLiveBottom => IsStuck && !PillShown && !IsJumping;

    /// <summary>Elle üzerine yaz (ör. ConsoleView.StickToBottom setter'ı ile geriye dönük uyum) — dipten uzaklığı
    /// YENİDEN HESAPLAMADAN doğrudan durumu değiştirir.</summary>
    public void ForceStuck(bool stuck)
    {
        if (stuck) _steering = false; // mod değişimi gibi açık bir "dibe sabitlen" isteği direksiyonu geri alır
        _state = new BottomAnchorState(stuck, IsJumping: false);
        RaiseChanged();
    }

    /// <summary>Her scroll-konum/extent değişiminde çağrılır (ScrollViewer.ScrollChanged.ExtentHeightChange için
    /// doğrudan, AvalonEdit için host'un elle izlediği extent farkı için).</summary>
    public void OnScrollChanged(double extentHeightChange)
    {
        var prev = _state;
        _state = BottomAnchorDecision.OnScrollChanged(
            _state, extentHeightChange, DistanceFromBottom, userDriven: _steering, _thresholdPx);

        if (extentHeightChange > 0 && ShouldFollow)
            _scrollInstant(BottomOffset); // içerik büyümesi yakalaması — ANINDA, dibin kendisine
        Notify(prev);
        // Bekleme sayacı BURADA kurulmaz: bu olayı akan içerik de doğurabilir ve o zaman sayaç hiç dolmazdı
        // (gerekçe: ArmIdleResume doc'u). Yalnız ham girdi onu ileri iter.
    }

    /// <summary>
    /// [B2 fix 1 · F1] <b>Yalnız pill'i yeniden okur</b> — içerik kaydırmasız büyüdüğünde (host'un kaydırma olayı yokken) çağrılır: dipten
    /// uzaklık değiştiyse ve pill bu yüzden görünür/gizli olacaksa host'u uyarır (<see cref="Notify"/>), aksi hâlde hiçbir şey yapmaz. Karar
    /// VERMEZ (<see cref="IsStuck"/>, takip, direksiyon aynı kalır) ve kaydırmaz — "pill canlı uzaklığı izler" kuralının içerik büyümesi kolu.
    /// Takip edilirken çağrılmamalıdır: ekleme ile dibe kaydırmanın uygulanması arasında uzaklık bir an eşiği aşar ve pill yanıp sönerdi (host
    /// bunu <see cref="ShouldFollow"/> ile ayırır).
    /// </summary>
    public void RefreshPill() => Notify(_state);

    /// <summary>
    /// Host'u uyarır. Durum değişmese bile <see cref="ShowPill"/> değiştiyse haber verilir: pill görünürlüğü
    /// durumdan değil ANLIK UZAKLIKTAN türer (design-v1 §2.5 — "dipten &gt;48px ise görünür"), ve takip
    /// kararı artık kullanıcıya bağlı olduğundan uzaklık, durum sabitken de değişebilir. Yalnız durum
    /// geçişlerinde haber verilseydi pill o hâllerde hiç güncellenmezdi.
    /// </summary>
    private void Notify(BottomAnchorState prev)
    {
        if (_state == prev && ShowPill == _lastShowPill) return;
        RaiseChanged();
    }

    /// <summary>
    /// Host'a duyurur (<see cref="Changed"/>) ve duyurulan pill görünürlüğünü KAYDEDER (<see cref="_lastShowPill"/>): sonraki
    /// <see cref="Notify"/> "pill değişti mi" sorusunu buna karşı sorar. Kayıt ile duyuru ayrı yazılırsa biri unutulur ve pill
    /// ya hiç yenilenmez ya da her olayda yenilenir — ikisi TEK yerde durur; durumun değiştiği her yol (zorlama, kullanıcı
    /// kaydırması, bekleme sonu, atlama başı ve sonu) buradan duyurur.
    /// </summary>
    private void RaiseChanged()
    {
        _lastShowPill = ShowPill;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// <b>Kullanıcı GERÇEKTEN kaydırdı</b> — host bunu ham girdiden (tekerlek, kaydırma çubuğu, dikey kaydırma tuşları —
    /// <see cref="UserScrollSignal"/>) bildirir. O andan itibaren bekleme dolana kadar direksiyon kullanıcıdadır: akan içerik görünümü
    /// dibe çekmez.
    ///
    /// <para>Sinyal neden hesaplanmıyor: bir ara sürüm "extent değişmeden gelen scroll olayı = kullanıcı"
    /// diye çıkarım yapıyordu. Yanlıştı — <c>ScrollChanged</c> yeniden yerleşimde, viewport değişiminde ve
    /// programatik kaydırmada da extent farkı olmadan ateşlenir; panel kendini kullanıcı kaydırmış sanıp
    /// takibi bırakıyor, kimse dokunmadan <c>⌄ latest</c> pill'i çıkıyordu.</para>
    /// </summary>
    /// <param name="stoppedJump">[B2 fix 2 · madde 2b] Jest uçuştaki bir dibe atlamayı DURDURDU mu (host'un kaydırma animatörü söyler —
    /// <see cref="ScrollAnimator.CancelForUser"/>). Durdurduysa atlama penceresi hemen kapanır (<see cref="EndJumpForUser"/>).</param>
    public void NotifyUserScroll(bool stoppedJump = false)
    {
        _steering = true; // önce: pencere kapanırken duyurulan durumda takip okuyucuya verilmiş olmalı
        if (stoppedJump) EndJumpForUser();
        ArmIdleResume();
    }

    /// <summary>
    /// [son temizlik · madde 1] <b>Kullanıcı YANA kaydırdı</b> — host bunu ham girdinin yana kanalından bildirir (←/→, yatay tekerlek, yatay
    /// kaydırma çubuğu — <see cref="UserScrollSignal"/>). Yana jest direksiyonu ALMAZ: izlenen akış izlenmeye devam eder ve uçuştaki atlama
    /// sürer. Direksiyon ZATEN kullanıcıdaysa (bir dikey jest aldı ve bekleme dolmadı) bekleme dönüşünü baştan kurar
    /// (<see cref="ArmIdleResume"/> — koşulu orada): yukarıda uzun bir satırı yana kaydırarak okuyan okuyucunun eli hâlâ paneldedir.
    /// <para>[DEĞİŞEN KURAL] Eskiden yana jest beklemeyi hiç kurmuyordu: yukarıda okuyup uzun bir satırı yana kaydıran okuyucu, son DİKEY
    /// jestinden beş saniye sonra dibe çekiliyordu. Daha önce yatay çubuk dikey jestlerle aynı kanaldaydı — beklemeyi kuruyor ama direksiyonu
    /// da alıyordu (izlenen akışta takibi beş saniye durduruyordu). Kural ikisinin arasıdır: yana jest direksiyonu almaz, yalnız tutulan
    /// direksiyonun beklemesini uzatır.</para>
    /// </summary>
    public void NotifySidewaysScroll() => ArmIdleResume();

    /// <summary>
    /// [B2 fix 2 · madde 2b] <b>Kullanıcının jesti uçuştaki atlamayı durdurdu: atlama penceresi ŞİMDİ kapanır</b> ve panelin durduğu yer bir
    /// kullanıcı kaydırması olarak yargılanır — pill, takip ve (konsolda) prompt imleci jestten hemen sonra okuyucuya göredir; bekleyen
    /// pencere sonu artık hiçbir şey yapmaz (kuşak sayacı). Jestin kendi kaydırması ardından gelir ve her kullanıcı kaydırması gibi
    /// yargılanır (<see cref="OnScrollChanged"/>).
    /// <para>[DEĞİŞEN KURAL] Eskiden pencere jestten bağımsız olarak 560 ms açık kalıyordu: anchor jestin kaydırmasını yok sayıyor, durum
    /// atlamanın iyimser "dipte"sinde kalıyor, pill pencere dolana dek gizli kalıyordu. Kural dardır — yalnız GERÇEKTEN uçuşan bir hareketi
    /// durduran jest pencereyi kapatır. Atlama inmiş, pencere hâlâ açıkken gelen jestin kaydırması YARGILANMAZ: pencere sonunda yalnız pill
    /// yeniden okunur (<see cref="BottomAnchorDecision.EndJump"/> durumu iyimser "dipte"de bırakır). Okuyucu yine güvendedir: direksiyon
    /// takibi tutar (akan içerik görünümü çekmez) ve beş saniyelik dönüş görünümü dibe indirir (<see cref="ArmIdleResume"/>).</para>
    /// </summary>
    private void EndJumpForUser()
    {
        if (!_state.IsJumping) return;
        ++_jumpGeneration; // bekleyen pencere sonu bayatladı
        var prev = _state;
        _state = BottomAnchorDecision.OnScrollChanged(
            BottomAnchorDecision.EndJump(_state), extentHeightChange: 0, DistanceFromBottom, userDriven: true, _thresholdPx);
        Notify(prev);
    }

    /// <summary>
    /// "Kullanıcı elini çekti" saatini BAŞTAN kurar — yalnız direksiyon kullanıcıdayken. Dolduğunda dibe dönülür ve akış yeniden izlenir.
    ///
    /// <para><b>Yalnız ham girdi çağırır:</b> dikey jest (<see cref="NotifyUserScroll"/> — direksiyonu alıp kurar) ve yana jest
    /// (<see cref="NotifySidewaysScroll"/> — direksiyonu almaz; tutulan direksiyonun saatini baştan kurar). Bir ara sürümde her scroll OLAYINDA
    /// kuruluyordu; derleme sürerken o olayları üreten kullanıcı değil akan içerikti, yani sayaç her satırla
    /// sıfırlanıyor ve beş saniye HİÇ dolmuyordu — sahada "kaydırdım, bekliyorum, dibe dönmüyor" diye
    /// görülen buydu. Sayaç scroll olaylarını değil, kullanıcının elini ölçer.</para>
    ///
    /// <para>Host izin vermiyorken (<see cref="AutoResumeAllowed"/> — konsolun proje sayfası takibe izin vermez) saat kurulmaz.
    /// Kuşak sayacı, arka arkaya gelen girdilerin biriktirdiği eski saatlerin dolduğunda iş yapmasını engeller.</para>
    /// </summary>
    private void ArmIdleResume()
    {
        if (!_steering || !AutoResumeAllowed) return;

        int generation = ++_idleGeneration;
        _scheduleOnce(TimeSpan.FromMilliseconds(BottomAnchorDecision.IdleResumeMs), () =>
        {
            if (generation != _idleGeneration) return;   // araya yeni bir scroll girdi
            _steering = false;                            // kullanıcı elini çekti — direksiyon geri alınır
            if (!AutoResumeAllowed) return;
            if (_state.IsStuck)
            {
                // Zaten dipteydi (kullanıcı kendi eliyle dibe indi): takip sürer. Durum değişmedi ama takip GERİ GELDİ —
                // host'lar duyar [idle-memory · final review m1]: konsol takibe bağlı kırpmasını sonraki batch'i beklemeden yapar.
                _scrollInstant(BottomOffset);
                RaiseChanged();
                return;
            }
            JumpToBottom();
        });
    }

    /// <summary>`⌄ latest` pill tıklaması (ya da başka bir "şimdi dibe git" tetikleyicisi). Takip yeniden devralır — host takibe izin
    /// veriyorsa (<see cref="FollowAllowed"/>); izin yoksa görünüm dibe BİR KEZ gider ve orada kalır (konsolun proje sayfası).</summary>
    public void JumpToBottom()
    {
        _steering = false; // açık "şimdi dibe git" (pill ya da bekleme sonu) — takip yeniden devralır (izin varsa)
        bool animated = _scrollSmooth(BottomOffset);
        if (!animated) { ForceStuck(true); return; } // reduced-motion/instant — jumping penceresine gerek yok

        _state = BottomAnchorDecision.BeginJump(_state);
        RaiseChanged();
        int generation = ++_jumpGeneration;
        _scheduleOnce(TimeSpan.FromMilliseconds(BottomAnchorDecision.JumpingWindowMs), () =>
        {
            if (generation != _jumpGeneration) return; // yeni bir JumpToBottom bunu geçersiz kıldı
            _state = BottomAnchorDecision.EndJump(_state);
            RaiseChanged();
        });
    }

    /// <summary>Üretimin tek seferlik zamanlayıcısının TEK tanımı: bir <see cref="DispatcherTimer"/>, bir kez. Zamanlayıcısı enjekte
    /// edilebilen her kullanıcı varsayılan olarak bunu alır — bu sınıf, <see cref="FollowScrollController"/> ve konsolun seam'i
    /// (<c>ConsoleView.AnchorScheduleOnce</c>); ikinci bir tanım yazılmaz.</summary>
    internal static void DefaultScheduleOnce(TimeSpan delay, Action callback)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) => { timer.Stop(); callback(); };
        timer.Start();
    }
}
