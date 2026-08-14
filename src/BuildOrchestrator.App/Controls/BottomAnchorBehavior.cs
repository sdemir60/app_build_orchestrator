using System.Windows.Threading;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T59] <see cref="BottomAnchorDecision"/>'ın (saf) kararını gerçek bir scroll host'a (ConsoleView'ın AvalonEdit
/// TextEditor'ı, ileride Event Stream'in ScrollViewer'ı) bağlayan ORTAK orkestratör — "bir bottom-anchor mekanizması"
/// (task-5 talimatı: konsolun mevcut <c>StickToBottom</c>'ıyla ÇAKIŞMAZ, ONUN üstüne kurulur — bkz. ConsoleView.xaml.cs).
///
/// <para><b>Neden delegate-tabanlı (WPF türü YOK):</b> ScrollViewer ve AvalonEdit TextEditor FARKLI tiplerdir; bu
/// sınıf ikisine de (ve ileride Event Stream'in host'una da) duck-typing'siz, WPF'e dokunmadan hizmet eder — bu
/// yüzden testleri <c>[Fact]</c> (STA gerekmez), yalnız gerçek kablaj (ConsoleView) <c>[StaFact]</c>'tir.</para>
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

    private BottomAnchorState _state = BottomAnchorState.Initial;
    private int _jumpGeneration;
    private int _idleGeneration;
    private bool _steering; // kullanıcı kaydırdı ve bekleme henüz dolmadı — o sürece panel ondadır
    private bool _lastShowPill; // son bildirilen pill görünürlüğü (bkz. Notify)

    /// <summary>IsStuck/IsJumping/ShowPill değiştiğinde ateşlenir — host (ConsoleView) buna göre pill Visibility'sini
    /// / StickToBottom'ı günceller.</summary>
    public event EventHandler? Changed;

    /// <param name="getOffset">Anlık VerticalOffset (px).</param>
    /// <param name="getExtent">Anlık ExtentHeight (px, toplam içerik yüksekliği).</param>
    /// <param name="getViewport">Anlık ViewportHeight (px).</param>
    /// <param name="scrollInstant">İçerik-büyümesi yakalaması — ANINDA (animasyonsuz) dibe kaydırır (AppendBatch/
    /// ScrollToEnd desenine eşdeğer).</param>
    /// <param name="scrollSmooth">Pill tıklaması — yumuşak (ya da reduced-motion'da anında) dibe kaydırır; bir
    /// animasyon BAŞLATILDIYSA true döner (host tipik olarak <see cref="ScrollAnimator.AnimateTo"/>'ya sarar).</param>
    /// <param name="scheduleOnce">560ms "jumping" penceresini zamanlar — testte enjekte edilebilir (D8); üretim
    /// varsayılanı bir <see cref="DispatcherTimer"/>.</param>
    /// <param name="autoResumeAllowed">Dibe KENDİLİĞİNDEN dönmek serbest mi (<c>null</c> = hiç dönme).
    /// Kullanıcı kaydırdıktan sonra <see cref="BottomAnchorDecision.IdleResumeMs"/> boyunca panele hiç
    /// dokunulmazsa dibe dönülür — panel akışı yeniden izlemeye başlar, tıpkı listenin frontier takibinin
    /// aynı süre sonunda geri açılması gibi. Host bunu kapatabilir: konsol proje-log modunda dönmez, orada
    /// izlenecek bir akış (ve imleç) yoktur.</param>
    public BottomAnchorBehavior(
        Func<double> getOffset, Func<double> getExtent, Func<double> getViewport,
        Action<double> scrollInstant, Func<double, bool> scrollSmooth,
        Action<TimeSpan, Action>? scheduleOnce = null, double thresholdPx = BottomAnchorDecision.DefaultThresholdPx,
        Func<bool>? autoResumeAllowed = null)
    {
        _autoResumeAllowed = autoResumeAllowed;
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
    /// yetkili cevabı. Üç koşul birden: takip açık, uçuşta bir atlama yok ve direksiyon kullanıcıda değil.
    ///
    /// <para>Host'lar bunu okur (konsolun <c>AppendBatch</c>'i dahil) — kendi <see cref="IsStuck"/>
    /// yorumlarını yapmazlar. Konsol tam da bunu yapıyordu: <c>AppendBatch</c> salt <c>IsStuck</c>'a bakıp
    /// her batch'te dibe kaydırıyor, kullanıcının kaydırmasını görmüyordu — derleme sürerken konsol
    /// kaydırılamaz hâle geliyordu.</para>
    /// </summary>
    public bool ShouldFollow => _state.IsStuck && !_state.IsJumping && !_steering;
    public double DistanceFromBottom => Math.Max(0, _getExtent() - _getOffset() - _getViewport());
    public bool ShowPill => BottomAnchorDecision.ShouldShowPill(_state, DistanceFromBottom, _thresholdPx);

    /// <summary>Elle üzerine yaz (ör. ConsoleView.StickToBottom setter'ı ile geriye dönük uyum) — dipten uzaklığı
    /// YENİDEN HESAPLAMADAN doğrudan durumu değiştirir.</summary>
    public void ForceStuck(bool stuck)
    {
        if (stuck) _steering = false; // mod değişimi gibi açık bir "dibe sabitlen" isteği direksiyonu geri alır
        _state = new BottomAnchorState(stuck, IsJumping: false);
        _lastShowPill = ShowPill;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Her scroll-konum/extent değişiminde çağrılır (ScrollViewer.ScrollChanged.ExtentHeightChange için
    /// doğrudan, AvalonEdit için host'un elle izlediği extent farkı için).</summary>
    public void OnScrollChanged(double extentHeightChange)
    {
        var prev = _state;
        _state = BottomAnchorDecision.OnScrollChanged(
            _state, extentHeightChange, DistanceFromBottom, userDriven: _steering, _thresholdPx);

        if (extentHeightChange > 0 && ShouldFollow)
            _scrollInstant(_getExtent()); // içerik büyümesi yakalaması — ANINDA, AppendBatch/ScrollToEnd ile aynı desen
        Notify(prev);
        // Bekleme sayacı BURADA kurulmaz: bu olayı akan içerik de doğurabilir ve o zaman sayaç hiç dolmazdı
        // (gerekçe: ArmIdleResume doc'u). Yalnız ham girdi onu ileri iter.
    }

    /// <summary>
    /// Host'u uyarır. Durum değişmese bile <see cref="ShowPill"/> değiştiyse haber verilir: pill görünürlüğü
    /// durumdan değil ANLIK UZAKLIKTAN türer (design-v1 §2.5 — "dipten &gt;48px ise görünür"), ve takip
    /// kararı artık kullanıcıya bağlı olduğundan uzaklık, durum sabitken de değişebilir. Yalnız durum
    /// geçişlerinde haber verilseydi pill o hâllerde hiç güncellenmezdi.
    /// </summary>
    private void Notify(BottomAnchorState prev)
    {
        bool pill = ShowPill;
        if (_state == prev && pill == _lastShowPill) return;
        _lastShowPill = pill;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// <b>Kullanıcı GERÇEKTEN kaydırdı</b> — host bunu ham girdiden (tekerlek) bildirir. O andan itibaren
    /// bekleme dolana kadar direksiyon kullanıcıdadır: akan içerik görünümü dibe çekmez.
    ///
    /// <para>Sinyal neden hesaplanmıyor: bir ara sürüm "extent değişmeden gelen scroll olayı = kullanıcı"
    /// diye çıkarım yapıyordu. Yanlıştı — <c>ScrollChanged</c> yeniden yerleşimde, viewport değişiminde ve
    /// programatik kaydırmada da extent farkı olmadan ateşlenir; panel kendini kullanıcı kaydırmış sanıp
    /// takibi bırakıyor, kimse dokunmadan <c>⌄ latest</c> pill'i çıkıyordu.</para>
    /// </summary>
    public void NotifyUserScroll()
    {
        _steering = true;
        ArmIdleResume();
    }

    /// <summary>
    /// "Kullanıcı elini çekti" saatini BAŞTAN kurar. Dolduğunda dibe dönülür ve akış yeniden izlenir.
    ///
    /// <para><b>Yalnız <see cref="NotifyUserScroll"/> çağırır.</b> Bir ara sürümde her scroll OLAYINDA
    /// kuruluyordu; derleme sürerken o olayları üreten kullanıcı değil akan içerikti, yani sayaç her satırla
    /// sıfırlanıyor ve beş saniye HİÇ dolmuyordu — sahada "kaydırdım, bekliyorum, dibe dönmüyor" diye
    /// görülen buydu. Sayaç scroll olaylarını değil, kullanıcının elini ölçer.</para>
    ///
    /// <para>Host izin vermiyorken (konsolun proje-log modu) saat kurulmaz. Kuşak sayacı, arka arkaya gelen
    /// girdilerin biriktirdiği eski saatlerin dolduğunda iş yapmasını engeller.</para>
    /// </summary>
    private void ArmIdleResume()
    {
        if (_autoResumeAllowed is null || !_steering) return;
        if (!_autoResumeAllowed()) return;

        int generation = ++_idleGeneration;
        _scheduleOnce(TimeSpan.FromMilliseconds(BottomAnchorDecision.IdleResumeMs), () =>
        {
            if (generation != _idleGeneration) return;   // araya yeni bir scroll girdi
            _steering = false;                            // kullanıcı elini çekti — direksiyon geri alınır
            if (_autoResumeAllowed is null || !_autoResumeAllowed()) return;
            if (_state.IsStuck) { _scrollInstant(_getExtent()); return; } // zaten dipteydi: takibi sürdür
            JumpToBottom();
        });
    }

    /// <summary>`⌄ latest` pill tıklaması (ya da başka bir "şimdi dibe git" tetikleyicisi).</summary>
    public void JumpToBottom()
    {
        _steering = false; // açık "şimdi dibe git" (pill ya da bekleme sonu) — takip yeniden devralır
        double target = Math.Max(0, _getExtent() - _getViewport());
        bool animated = _scrollSmooth(target);
        if (!animated) { ForceStuck(true); return; } // reduced-motion/instant — jumping penceresine gerek yok

        _state = BottomAnchorDecision.BeginJump(_state);
        _lastShowPill = ShowPill;
        Changed?.Invoke(this, EventArgs.Empty);
        int generation = ++_jumpGeneration;
        _scheduleOnce(TimeSpan.FromMilliseconds(BottomAnchorDecision.JumpingWindowMs), () =>
        {
            if (generation != _jumpGeneration) return; // yeni bir JumpToBottom bunu geçersiz kıldı
            _state = BottomAnchorDecision.EndJump(_state);
            _lastShowPill = ShowPill;
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    private static void DefaultScheduleOnce(TimeSpan delay, Action callback)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) => { timer.Stop(); callback(); };
        timer.Start();
    }
}
