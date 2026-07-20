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

    private BottomAnchorState _state = BottomAnchorState.Initial;
    private int _jumpGeneration;

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
    public BottomAnchorBehavior(
        Func<double> getOffset, Func<double> getExtent, Func<double> getViewport,
        Action<double> scrollInstant, Func<double, bool> scrollSmooth,
        Action<TimeSpan, Action>? scheduleOnce = null, double thresholdPx = BottomAnchorDecision.DefaultThresholdPx)
    {
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
