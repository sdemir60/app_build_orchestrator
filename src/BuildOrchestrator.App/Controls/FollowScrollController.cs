using System.Windows.Threading;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T59] Follow-mode + seçili-karta-kaydırma orkestratörü — <see cref="FollowScrollDecision"/>'ın (saf) kararını
/// PAYLAŞILAN <see cref="LayoutMetrics"/> (T58 instance) + gerçek scroll host'a bağlar. Karta tıklama follow'u
/// durdurur; seçim kalkınca kaldığı yerden sürer (design-v1 §2.4/§3.3).
///
/// <para><b>Neden delegate-tabanlı:</b> <see cref="BottomAnchorBehavior"/> ile aynı gerekçe — WPF türüne
/// dokunmadan test edilir (<c>[Fact]</c>, STA gerekmez); gerçek kablaj (StickyLayerList) <c>ScrollAnimator</c>'a
/// sarar.</para>
/// </summary>
public sealed class FollowScrollController
{
    /// <summary>Ek A-11: seçili kart 90ms gecikmeyle (kaskat/reveal animasyonuyla çakışmasın diye) görünür kılınır.</summary>
    public const double SelectedCardDelayMs = 90.0;

    // [T2 fix-1 · I-D] Metrics ARTIK readonly DEĞİL: liste her filtre tazelemesinde yeni bir LayoutMetrics
    // üretir ve controller'ı yeniden yaratmak throttle/seçim state'ini SIFIRLARDI (bkz. Rebind).
    private LayoutMetrics _metrics;
    private readonly Func<double> _getViewportHeight;
    private readonly Func<double> _getCurrentOffset;
    private readonly Func<double, bool> _animateTo; // (targetOffset) -> animasyon başladı mı
    private readonly Func<long> _nowMs;
    private readonly Action<TimeSpan, Action> _scheduleOnce;

    private long _lastMoveAtMs = long.MinValue;
    private bool _hasSelection;
    private int _selectionGeneration;

    /// <param name="metrics">T58'in paylaşılan LayoutMetrics instance'ı — hedefler <c>ScrollTargetForRow</c>'dan.</param>
    /// <param name="getViewportHeight">Anlık ScrollViewer.ViewportHeight.</param>
    /// <param name="getCurrentOffset">Anlık ScrollViewer.VerticalOffset.</param>
    /// <param name="animateTo">Hedefe kaydır (host, tipik olarak <see cref="ScrollAnimator.AnimateTo"/>'ya sarar).</param>
    /// <param name="nowMs">D8: enjekte edilebilir saat (throttle testte deterministik).</param>
    /// <param name="scheduleOnce">D8: 90ms gecikmeyi zamanlar — testte enjekte edilebilir; üretim varsayılanı DispatcherTimer.</param>
    public FollowScrollController(LayoutMetrics metrics, Func<double> getViewportHeight, Func<double> getCurrentOffset,
        Func<double, bool> animateTo, Func<long>? nowMs = null, Action<TimeSpan, Action>? scheduleOnce = null)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _metrics = metrics;
        _getViewportHeight = getViewportHeight;
        _getCurrentOffset = getCurrentOffset;
        _animateTo = animateTo;
        _nowMs = nowMs ?? (() => Environment.TickCount64);
        _scheduleOnce = scheduleOnce ?? DefaultScheduleOnce;
    }

    /// <summary>Seçim yokken true — follow-mode aktif.</summary>
    public bool IsFollowing => !_hasSelection;

    /// <summary>
    /// [T2 fix-1 · I-D] Satır düzeni değişti (yeni <see cref="LayoutMetrics"/>) ama <b>oturum aynı</b>:
    /// throttle zamanlayıcısı (<see cref="_lastMoveAtMs"/>) ve seçim durumu KORUNUR.
    ///
    /// <para><b>Neden gerekli (ölçülen kusur):</b> <c>StickyLayerList.SetGroups</c> controller'ı her çağrıda
    /// YENİDEN yaratıyordu. Eskiden bu yalnız topoloji değişiminde olurdu; 2.5'ten sonra görünür küme her
    /// değiştiğinde oluyor — yani koşarken <c>Building</c>/<c>Failed</c> filtresi açıkken HER
    /// <c>projectStarted</c>/<c>projectSucceeded</c> taze bir controller üretiyordu. Taze controller'da
    /// <c>_lastMoveAtMs == long.MinValue</c> → <c>elapsed = double.MaxValue</c> → <see cref="FollowScrollDecision.ShouldMove"/>
    /// HEP true: design-v1 §3.3'ün 550ms throttle'ı filtre altında tamamen etkisizleşiyor ve takip 200ms
    /// tick'te zıplıyordu. Seçim durumu da (<c>_hasSelection</c>) sessizce düşüyordu.</para>
    /// </summary>
    public void Rebind(LayoutMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _metrics = metrics;
    }


    /// <summary>
    /// Koşarken + seçim yokken frontier satırının (ör. ilk <c>State==Started</c> proje) görünür kalması için
    /// çağrılır (durum her değiştiğinde — RunViewModel.Projects değişimi gibi). Seçim aktifse VEYA kullanıcı bu
    /// scroll'u yeni iptal ettiyse (<paramref name="userSuppressed"/> — bkz. <see cref="ScrollAnimator.GetIsUserSuppressed"/>)
    /// no-op. Throttle (550ms) + dead-band (54px) <see cref="FollowScrollDecision.ShouldMove"/>'dan.
    /// </summary>
    public void FollowRow(int rowIndex, bool userSuppressed = false)
    {
        if (_hasSelection || userSuppressed) return;

        double margin = FollowScrollDecision.TopMargin(_getViewportHeight(), FollowScrollDecision.FollowTopMarginFraction);
        double target = _metrics.ScrollTargetForRow(rowIndex, margin);

        long now = _nowMs();
        double elapsed = _lastMoveAtMs == long.MinValue ? double.MaxValue : now - _lastMoveAtMs;
        if (!FollowScrollDecision.ShouldMove(elapsed, _getCurrentOffset(), target)) return;

        _lastMoveAtMs = now;
        _animateTo(target);
    }

    /// <summary>Karta tıklama: follow durur; 90ms sonra satır %35 üst-marjla görünür kılınır (Ek A-11).</summary>
    public void SelectRow(int rowIndex)
    {
        _hasSelection = true;
        int generation = ++_selectionGeneration;
        _scheduleOnce(TimeSpan.FromMilliseconds(SelectedCardDelayMs), () =>
        {
            if (generation != _selectionGeneration) return; // yeni bir seçim/deselect bunu geçersiz kıldı
            double margin = FollowScrollDecision.TopMargin(_getViewportHeight(), FollowScrollDecision.SelectionTopMarginFraction);
            double target = _metrics.ScrollTargetForRow(rowIndex, margin);
            _animateTo(target);
        });
    }

    /// <summary>Seçim kalkar — follow kaldığı yerden sürer (bir sonraki <see cref="FollowRow"/> çağrısı throttle/
    /// dead-band'e tabi olarak yeniden hareket ettirebilir).</summary>
    public void ClearSelection()
    {
        _hasSelection = false;
        _selectionGeneration++; // bekleyen bir seçim-scroll'u geçersiz kıl
    }

    private static void DefaultScheduleOnce(TimeSpan delay, Action callback)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) => { timer.Stop(); callback(); };
        timer.Start();
    }
}
