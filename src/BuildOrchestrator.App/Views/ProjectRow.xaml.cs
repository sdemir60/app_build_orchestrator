using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Core.Formatting;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [T53/T54-UI] design-v1 proje kartı (BuildApp.jsx:355-416). 7 slot: statü şeridi · WillBuildDot · ad+sln ·
/// sağ blok (sha↔hover ikonları) · statü glyph'i · dep rozet slotu · süre. DataContext bir
/// <see cref="ProjectRowViewModel"/>'dir; kart onun INotifyPropertyChanged'ini dinleyip yalnız DEĞİŞEN slotu
/// tazeler (statü tikleri satır VM'inden akar — koleksiyon reset YOK).
///
/// <para><b>Motion (bağlayıcı sözleşme):</b> tüm animasyonlar kod-tarafı (<see cref="MotionTokens"/>) — süre/eğri
/// ve <c>App.Motion?.AnimationsEnabled</c> BAŞLATMA ANINDA taze okunur; template-trigger Storyboard İMKANSIZ
/// (MotionTokens.cs). 120ms hover zemini · 80ms şerit genişliği · 120ms iç-sarmalayıcı TranslateX · 3.8s nefes
/// (yalnız building, 30fps) · 360ms shake (yalnız hata anında bir kez). Nefes/pulse yeniden-başlatma guard'ları
/// StatusGlyph/GraphView deseniyle AYNI (dönen bir animasyon her tikte baştan almaz).</para>
/// </summary>
public partial class ProjectRow : UserControl
{
    // design-v1 kaynak sabitleri (inline magic number YASAK — StatusGlyph.PulseMs / BuildingSpinner.RotationMs deseni).
    private const double BreathMs = 3800;          // BuildApp.jsx:22 `bo-breath 3.8s`
    private const double BreathPeakOpacity = 0.32; // BuildApp.jsx:24 amber-soft katman tepe opaklığı
    private const int DecorativeFrameRate = 30;    // brief: DesiredFrameRate=30
    private const double ShakeMs = 360;            // BuildApp.jsx:27 `bo-shake 360ms`
    private const double SelectedTranslateX = 4;   // BuildApp.jsx:379 seçili iç-sarmalayıcı translateX
    private const double StripeWidthNormal = 2;    // BuildApp.jsx:373
    private const double StripeWidthSelected = 3;

    private readonly SolidColorBrush _bgBrush = new(Colors.Transparent);
    private ProjectRowViewModel? _vm;
    private bool _hover;
    private bool _isBreathing;
    private ProjectRowState? _prevState;

    public ProjectRow()
    {
        InitializeComponent();
        PART_Root.Background = _bgBrush; // template-lokal, donmamış brush (A13.2) — 120ms renk geçişi bunu animate eder
        DataContextChanged += OnDataContextChanged;
        MouseEnter += (_, _) => SetHover(true);
        MouseLeave += (_, _) => SetHover(false);
        MouseLeftButtonUp += OnRowClicked;
        KeyDown += OnRowKeyDown;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ---------------------------------------------------------------- test yüzeyi
    internal Rectangle Stripe => PART_Stripe;
    internal WillBuildDot Dot => PART_Dot;
    internal TextBlock DurationText => PART_Duration;
    internal TextBlock ShaText => PART_Sha;
    internal FrameworkElement HoverIcons => PART_HoverIcons;
    internal FrameworkElement DepSlot => PART_DepSlot;
    internal FrameworkElement DepIcon => PART_DepIcon;
    internal FrameworkElement BreathLayer => PART_Breath;
    internal void SimulateHover(bool hover) => SetHover(hover);
    internal TranslateTransform InnerTranslate => PART_InnerTranslate;
    internal StatusGlyph Glyph => PART_Glyph;

    /// <summary>[T54-UI test] Nefes animasyonunu üreten TEK yer — kontrol ve test AYNI fabrikayı kullanır;
    /// 30fps sınırı ve 3.8s süre burada pinlenir (inline magic number YOK).</summary>
    internal static DoubleAnimationUsingKeyFrames BuildBreathingAnimation(FrameworkElement host)
    {
        var spline = MotionTokens.ResolveKeySpline(host, "KeySpline.EaseInOut", new KeySpline(0.65, 0, 0.35, 1));
        var anim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        anim.KeyFrames.Add(new SplineDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero), spline));
        anim.KeyFrames.Add(new SplineDoubleKeyFrame(BreathPeakOpacity, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(BreathMs / 2)), spline));
        anim.KeyFrames.Add(new SplineDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(BreathMs)), spline));
        Timeline.SetDesiredFrameRate(anim, DecorativeFrameRate);
        return anim;
    }

    // ---------------------------------------------------------------- lifecycle
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (App.Motion is { } motion) motion.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
        ApplyAll();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (App.Motion is { } motion) motion.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
        StopBreathing(); // GraphView deseni: durum building'i terk edince / unload'da clock serbest
    }

    private void OnAnimationsEnabledChanged(object? sender, EventArgs e) => ApplyBreathing();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = e.NewValue as ProjectRowViewModel;
        _prevState = null;
        if (_vm is not null) _vm.PropertyChanged += OnVmPropertyChanged;
        ApplyAll();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ProjectRowViewModel.State):
                ApplyStateVisuals();
                ApplyDuration();
                ApplyRightBlock(); // sha/ikon görünürlüğü sha WillBuild'e bağlı ama building geçişinde de tazelensin
                break;
            case nameof(ProjectRowViewModel.WillBuild):
                PART_Dot.State = _vm?.WillBuild;
                ApplyRightBlock();
                break;
            case nameof(ProjectRowViewModel.DepIssues):
            case nameof(ProjectRowViewModel.HasDepIssue):
                ApplyDep();
                break;
            case nameof(ProjectRowViewModel.DurationMs):
                ApplyDuration();
                UpdateGlyphTooltip(); // building'de canlı "Building — Ns"
                break;
            case nameof(ProjectRowViewModel.IsSelected):
                ApplySelection();
                break;
            case nameof(ProjectRowViewModel.SolutionName):
                PART_Sln.Text = _vm?.SolutionName;
                break;
            case nameof(ProjectRowViewModel.CurrentSha):
                ApplySha();
                break;
        }
    }

    // ---------------------------------------------------------------- toplu tazeleme
    private void ApplyAll()
    {
        _prevState = _vm?.State;
        PART_Name.Text = _vm?.Name;
        PART_Sln.Text = _vm?.SolutionName;
        PART_Dot.State = _vm?.WillBuild;
        ApplyStateVisuals();
        ApplyDep();
        ApplyDuration();
        ApplySelection();  // şerit genişliği/renk + translateX + zemin
        ApplyRightBlock(); // sha/hover ikonları
    }

    /// <summary>Statüye bağlı görseller: glyph, ad soluk/parlak, şerit rengi, nefes, shake, glyph tooltip.</summary>
    private void ApplyStateVisuals()
    {
        var state = _vm?.State ?? ProjectRowState.Pending;
        GraphStatus status = MapStatus(state);

        PART_Glyph.Status = status;

        // Ad rengi: skipped | discovered(pending) → dim (BuildApp.jsx:348).
        bool dim = state is ProjectRowState.Skipped or ProjectRowState.Pending;
        PART_Name.SetResourceReference(TextBlock.ForegroundProperty, dim ? "Brush.TextDim" : "Brush.TextPrimary");

        SetStripeFill();
        UpdateGlyphTooltip();

        // Shake yalnız hata ANINDA (Pending/Started/... → Failed geçişinde), bir kez.
        if (state == ProjectRowState.Failed && _prevState is not null && _prevState != ProjectRowState.Failed)
            PlayShake();
        _prevState = state;

        ApplyBreathing();
    }

    private void SetStripeFill()
    {
        var state = _vm?.State ?? ProjectRowState.Pending;
        string? key = MapStatus(state) switch
        {
            GraphStatus.Queued => "Brush.StatusQueued",
            GraphStatus.Building => "Brush.Amber",
            GraphStatus.Succeeded => "Brush.StatusSuccess",
            GraphStatus.Failed => "Brush.StatusFail",
            GraphStatus.Skipped => "Brush.StatusSkipped",
            GraphStatus.Cycle => "Brush.StatusCycle",
            _ => null, // discovered → transparent
        };
        // Seçili + discovered → amber (BuildApp.jsx:374).
        if (key is null && (_vm?.IsSelected ?? false)) key = "Brush.Amber";

        if (key is null) PART_Stripe.Fill = Brushes.Transparent;
        else PART_Stripe.SetResourceReference(Shape.FillProperty, key);
    }

    private void ApplyDuration()
    {
        var state = _vm?.State ?? ProjectRowState.Pending;
        long ms = _vm?.DurationMs ?? 0;
        // building → canlı Elapsed; bitmiş → Duration; yoksa "—" (Duration(null)).
        PART_Duration.Text = state == ProjectRowState.Started
            ? DurationFormat.Elapsed(ms)
            : DurationFormat.Duration(ms == 0 ? null : ms);
        PART_Duration.SetResourceReference(TextBlock.ForegroundProperty,
            state == ProjectRowState.Failed ? "Brush.StatusFailText" : "Brush.TextDim");
    }

    private void ApplyDep()
    {
        bool has = _vm?.HasDepIssue ?? false;
        PART_DepIcon.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        if (has && _vm?.DepIssues is { } issues)
        {
            // Kısa adlar (OSYS. atılmış), virgülle — tooltip BİREBİR (brief slot 6).
            string names = string.Join(", ", issues.Select(GraphNode.ShortLabel));
            PART_DepTip.Content = $"Failed dependency: {names} — last successful output referenced";
        }
        UpdateGlyphTooltip(); // depIssue eki glyph tooltip'ini de değiştirir
    }

    private void ApplySha()
    {
        // {CurrentSha} → {TargetSha}. TargetSha run-geneli (RunViewModel) — atalardan çözülür; CurrentSha per-proje
        // (henüz IPC'de yok — bkz. ProjectRowViewModel.CurrentSha). Görünürlük ApplyRightBlock'ta.
        string cur = _vm?.CurrentSha ?? "";
        string target = FindRunViewModel()?.TargetSha ?? "";
        PART_Sha.Text = $"{cur} → {target}";
    }

    /// <summary>Sağ blok: hover'da aç-ikonları, değilse (will==dirty) sha çifti (BuildApp.jsx:387-403).</summary>
    private void ApplyRightBlock()
    {
        bool showIcons = _hover;
        bool showSha = !_hover && _vm?.WillBuild == true;
        PART_HoverIcons.Visibility = showIcons ? Visibility.Visible : Visibility.Collapsed;
        PART_Sha.Visibility = showSha ? Visibility.Visible : Visibility.Collapsed;
        if (showSha) ApplySha();
    }

    /// <summary>Seçim: şerit 2→3 (80ms), iç-sarmalayıcı TranslateX (120ms EaseOut), zemin (120ms). Şerit rengi
    /// de seçilime bağlıdır (selected+discovered→amber).</summary>
    private void ApplySelection()
    {
        bool selected = _vm?.IsSelected ?? false;
        AnimateStripeWidth(selected ? StripeWidthSelected : StripeWidthNormal);
        AnimateInnerTranslate(selected ? SelectedTranslateX : 0);
        SetStripeFill();
        ApplyBackground();
    }

    private void SetHover(bool hover)
    {
        if (_hover == hover) return;
        _hover = hover;
        ApplyBackground();
        ApplyRightBlock();
    }

    private void ApplyBackground()
    {
        bool selected = _vm?.IsSelected ?? false;
        Color target = selected ? ResolveColor("Brush.SurfaceRaised", Colors.Transparent)
            : _hover ? ResolveColor("Brush.SurfaceHover", Colors.Transparent)
            : Colors.Transparent;
        MotionTokens.TransitionColor(this, _bgBrush, target);
    }

    private void UpdateGlyphTooltip()
    {
        var state = _vm?.State ?? ProjectRowState.Pending;
        GraphStatus status = MapStatus(state);
        string text = StatusLabel(status);
        if (state == ProjectRowState.Started)
            text += " — " + DurationFormat.Elapsed(_vm?.DurationMs ?? 0);
        else if (_vm?.HasDepIssue ?? false)
            text += " — dependency issue";
        PART_GlyphTip.Content = text;
    }

    // ---------------------------------------------------------------- nefes / shake
    private void ApplyBreathing()
    {
        bool building = (_vm?.State ?? ProjectRowState.Pending) == ProjectRowState.Started;
        // Katman "yalnız building'de var": görünürlük motion'dan BAĞIMSIZ (reduced-motion'da da building satırda
        // katman durur ama opaklık 0 kalır = görünmez). Animasyon yalnız motion açıkken döner.
        PART_Breath.Visibility = building ? Visibility.Visible : Visibility.Collapsed;

        bool shouldBreathe = building && (App.Motion?.AnimationsEnabled ?? false);
        if (shouldBreathe == _isBreathing) return; // zaten dönen nabız baştan almaz (StatusGlyph deseni)
        _isBreathing = shouldBreathe;
        if (!shouldBreathe) { StopBreathing(); return; }
        PART_Breath.BeginAnimation(OpacityProperty, BuildBreathingAnimation(this));
    }

    private void StopBreathing()
    {
        _isBreathing = false;
        PART_Breath.BeginAnimation(OpacityProperty, null);
        PART_Breath.Opacity = 0;
    }

    private void PlayShake()
    {
        if (!(App.Motion?.AnimationsEnabled ?? false)) return;
        var spline = MotionTokens.ResolveKeySpline(this, "KeySpline.EaseStandard", new KeySpline(0.4, 0, 0.2, 1));
        var anim = new DoubleAnimationUsingKeyFrames(); // bir kez (RepeatBehavior default 1x), sonunda 0'a döner
        void Frame(double v, double pct) =>
            anim.KeyFrames.Add(new SplineDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ShakeMs * pct)), spline));
        // BuildApp.jsx:27 keyframe'leri: 10%,90% → -2 · 25%,75% → +3 · 50% → -3.
        Frame(-2, 0.10); Frame(3, 0.25); Frame(-3, 0.50); Frame(3, 0.75); Frame(-2, 0.90); Frame(0, 1.0);
        PART_ShakeTranslate.BeginAnimation(TranslateTransform.XProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    // ---------------------------------------------------------------- animasyon yardımcıları
    private void AnimateStripeWidth(double to) =>
        AnimateDouble(PART_Stripe, FrameworkElement.WidthProperty, to,
            "Duration.Instant", 80, "KeySpline.EaseStandard", new KeySpline(0.4, 0, 0.2, 1));

    private void AnimateInnerTranslate(double to) =>
        AnimateDouble(PART_InnerTranslate, TranslateTransform.XProperty, to,
            "Duration.Fast", 120, "KeySpline.EaseOut", new KeySpline(0.22, 1, 0.36, 1));

    private void AnimateDouble(IAnimatable target, DependencyProperty prop, double to,
        string durKey, double durFallback, string splineKey, KeySpline splineFallback)
    {
        bool enabled = App.Motion?.AnimationsEnabled ?? false;
        var duration = MotionTokens.ResolveDuration(this, durKey, durFallback);
        var spline = MotionTokens.ResolveKeySpline(this, splineKey, splineFallback);
        if (!enabled || duration.TimeSpan <= TimeSpan.Zero)
        {
            target.BeginAnimation(prop, null);
            ((DependencyObject)target).SetValue(prop, to);
            return;
        }
        target.BeginAnimation(prop, MotionTokens.SplineTo(to, duration.TimeSpan, spline), HandoffBehavior.SnapshotAndReplace);
    }

    // ---------------------------------------------------------------- etkileşim
    private void OnRowClicked(object sender, MouseButtonEventArgs e)
    {
        if (_vm is { } vm) FindRunViewModel()?.SelectProject(vm.Id);
    }

    private void OnRowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && _vm is { } vm)
        {
            FindRunViewModel()?.SelectProject(vm.Id);
            e.Handled = true;
        }
    }

    /// <summary>[C1 debt] Seçim RunViewModel'de yaşar; kartın DataContext'i satır VM'idir → ata ağaçta
    /// DataContext'i RunViewModel olan ilk öğeye (StickyLayerList/ShellRoot) çıkılır.</summary>
    private ViewModels.RunViewModel? FindRunViewModel()
    {
        DependencyObject? d = this;
        while (d is not null)
        {
            if (d is FrameworkElement fe && fe.DataContext is ViewModels.RunViewModel run) return run;
            d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    // ---------------------------------------------------------------- eşleme / metin
    /// <summary>ProjectRowState → görsel <see cref="GraphStatus"/>. VM'de <c>queued</c>/<c>cycle</c> sinyali YOK
    /// (IPC bunları taşımaz); <c>Pending</c> nötr <c>Discovered</c>'a eşlenir (dinlenme görünümü).</summary>
    private static GraphStatus MapStatus(ProjectRowState state) => state switch
    {
        ProjectRowState.Started => GraphStatus.Building,
        ProjectRowState.Succeeded => GraphStatus.Succeeded,
        ProjectRowState.Failed => GraphStatus.Failed,
        ProjectRowState.Skipped => GraphStatus.Skipped,
        _ => GraphStatus.Discovered,
    };

    /// <summary>design-v1 EN_STATUS (BuildApp.jsx:342) — glyph tooltip'inin İngilizce statü etiketi.</summary>
    private static string StatusLabel(GraphStatus status) => status switch
    {
        GraphStatus.Queued => "Queued",
        GraphStatus.Building => "Building",
        GraphStatus.Succeeded => "Succeeded",
        GraphStatus.Failed => "Failed",
        GraphStatus.Skipped => "Skipped",
        GraphStatus.Cycle => "Cycle",
        _ => "Discovered",
    };

    private Color ResolveColor(string key, Color fallback) =>
        TryFindResource(key) is SolidColorBrush b ? b.Color : fallback;
}
