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
    public double DistanceFromBottom => Math.Max(0, _getExtent() - _getOffset() - _getViewport());
    public bool ShowPill => BottomAnchorDecision.ShouldShowPill(_state, DistanceFromBottom, _thresholdPx);

    /// <summary>Elle üzerine yaz (ör. ConsoleView.StickToBottom setter'ı ile geriye dönük uyum) — dipten uzaklığı
    /// YENİDEN HESAPLAMADAN doğrudan durumu değiştirir.</summary>
    public void ForceStuck(bool stuck)
    {
        _state = new BottomAnchorState(stuck, IsJumping: false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Her scroll-konum/extent değişiminde çağrılır (ScrollViewer.ScrollChanged.ExtentHeightChange için
    /// doğrudan, AvalonEdit için host'un elle izlediği extent farkı için).</summary>
    public void OnScrollChanged(double extentHeightChange)
    {
        var prev = _state;
        _state = BottomAnchorDecision.OnScrollChanged(_state, extentHeightChange, DistanceFromBottom, _thresholdPx);
        if (_state.IsStuck && extentHeightChange > 0 && !_state.IsJumping)
            _scrollInstant(_getExtent()); // içerik büyümesi yakalaması — ANINDA, AppendBatch/ScrollToEnd ile aynı desen
        if (_state != prev) Changed?.Invoke(this, EventArgs.Empty);
        ArmIdleResume();
    }

    /// <summary>
    /// Dipten uzaktaysak "boşta kalma" saatini BAŞTAN kurar: her scroll onu ileri iter, yani sayaç ancak
    /// kullanıcı elini çektiğinde dolar. Dolduğunda dibe dönülür.
    ///
    /// <para>Zaten dipteyken ya da host izin vermiyorken saat kurulmaz. Kuşak sayacı, arka arkaya gelen
    /// scroll olaylarının biriktirdiği eski saatlerin dolduğunda iş yapmasını engeller.</para>
    /// </summary>
    private void ArmIdleResume()
    {
        if (_autoResumeAllowed is null || _state.IsStuck || _state.IsJumping) return;
        if (!_autoResumeAllowed()) return;

        int generation = ++_idleGeneration;
        _scheduleOnce(TimeSpan.FromMilliseconds(BottomAnchorDecision.IdleResumeMs), () =>
        {
            if (generation != _idleGeneration) return;   // araya yeni bir scroll girdi
            if (_state.IsStuck || _autoResumeAllowed is null || !_autoResumeAllowed()) return;
            JumpToBottom();
        });
    }

    /// <summary>`⌄ latest` pill tıklaması (ya da başka bir "şimdi dibe git" tetikleyicisi).</summary>
    public void JumpToBottom()
    {
        double target = Math.Max(0, _getExtent() - _getViewport());
        bool animated = _scrollSmooth(target);
        if (!animated) { ForceStuck(true); return; } // reduced-motion/instant — jumping penceresine gerek yok

        _state = BottomAnchorDecision.BeginJump(_state);
        Changed?.Invoke(this, EventArgs.Empty);
        int generation = ++_jumpGeneration;
        _scheduleOnce(TimeSpan.FromMilliseconds(BottomAnchorDecision.JumpingWindowMs), () =>
        {
            if (generation != _jumpGeneration) return; // yeni bir JumpToBottom bunu geçersiz kıldı
            _state = BottomAnchorDecision.EndJump(_state);
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
