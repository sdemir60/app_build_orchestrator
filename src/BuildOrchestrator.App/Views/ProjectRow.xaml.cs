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
using BuildOrchestrator.Contracts.Model;
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
    private const double BreathPeakOpacity = 0.32; // [A13/T4 fix-1 · D10] BuildApp.jsx:34 amber-soft katman tepe opaklığı (bayat satır referansı düzeltildi, eskiden :24)
    private const int DecorativeFrameRate = 30;    // brief: DesiredFrameRate=30
    private const double ShakeMs = 360;            // [A13/T4 fix-1 · D10] BuildApp.jsx:18 `bo-shake .36s` (bayat satır referansı düzeltildi, eskiden :27; keyframe'ler :30'da)
    private const double SelectedTranslateX = 4;   // BuildApp.jsx:379 seçili iç-sarmalayıcı translateX
    private const double StripeWidthNormal = 2;    // BuildApp.jsx:373
    private const double StripeWidthSelected = 3;

    // [cycle rounds/Task 9] Dep-slot tooltip metinleri — TEK doğruluk kaynağı burası (CLAUDE.md kopya YASAK).
    // "Failed dependency: …" metni ApplyDep içinde kalır (adlar interpolasyonlu, tek kullanım yeri zaten oradaydı).
    private const string CycleUnsettledTooltip =
        "Cycle did not fully settle — output may be one generation stale";
    private const string CycleUnconvergedTooltip =
        "Cycle did not build — not retried until the source changes";
    // [cycles] Sıradan üyelik: satır bu koşuda GERÇEK bir sonuç aldığı için statü glyph'i artık döngüyü değil
    // sonucu gösterir; yapısal olgu bu rozete taşınır. Yukarıdaki iki metinden farkı, hiçbir şey İDDİA
    // ETMEMESİDİR — ne çıktının bayat olduğunu ne bir daha denenmeyeceğini söyler, yalnız yeri tarif eder.
    private const string CycleMembershipTooltip = "In a dependency cycle";

    // [E3/T42] design-v1 bo-reveal (BuildApp.jsx:15/:27): opacity 0→1 + translateY(-5px)→0, .3s, ease-out —
    // GraphView katman reveal'iyle AYNI animasyon ailesi (GraphView.RevealMs/RevealRisePx). Liste satırı gecikmesi
    // graf'tan FARKLI formül: 10ms/satır, 380ms tavan (BuildApp.jsx:367 `Math.min(revealIndex*10, 380)`).
    // [W2 fix-1] İkisi de RevealStagger'daki TEK tanımın derleme-zamanı ALIAS'ıdır (GraphView ile ASLA sürüklenemez).
    internal const double RevealMs = RevealStagger.RevealMs;      // `bo-reveal .3s` — [E4] StickyLayerList release penceresi de kullanır
    private const double RevealRisePx = RevealStagger.RevealRisePx; // translateY(-5px)
    internal const double RowStaggerMs = 10;       // BuildApp.jsx:367 revealIndex*10
    internal const double RowStaggerCapMs = 380;   // BuildApp.jsx:367 tavan 380

    private readonly SolidColorBrush _bgBrush = new(Colors.Transparent);
    private ProjectRowViewModel? _vm;
    private ProjectRowActions? _actions; // [L1] ilk hover'da kurulur (bkz. EnsureActions)
    private bool _applied;               // [L1] ApplyAll bu DataContext için koştu mu (çift koşum guard'ı)
    private bool _hover;
    private bool _isBreathing;
    private ProjectRowState? _prevState;
    /// <summary>[W2] Provider + <c>MotionSettings</c> seam'i + subscribe-once kablajı TEK yerde
    /// (<see cref="MotionGate"/>) — latch'siz kip: her <c>Loaded</c>'da kaynak yeniden okunur.</summary>
    private readonly MotionGate _motion;

    /// <summary>[Fix wave 1 · D1 review Finding 2] Motion sinyalinin TAZE okunduğu kapı (GraphView deseni, D8) —
    /// sınıf statik <c>App.Motion</c>'a doğrudan bağlanmaz; testler gerçek bir 30fps saatini (nefes) sürebilmek
    /// için bunu <c>() =&gt; true</c> ile enjekte eder (headless'ta <c>App.Motion</c> null → hiç saat başlamazdı).</summary>
    public Func<bool> AnimationsEnabledProvider
    {
        get => _motion.AnimationsEnabledProvider;
        set => _motion.AnimationsEnabledProvider = value;
    }

    /// <summary>[Fix wave 1 · D1 review Finding 2] <c>AnimationsEnabledChanged</c>'e abone olunacak kaynak; null
    /// ise <c>App.Motion</c> (GraphView.MotionSettings deseni).</summary>
    public BuildOrchestrator.App.Services.IMotionSettings? MotionSettings
    {
        get => _motion.MotionSettings;
        set => _motion.MotionSettings = value;
    }

    public ProjectRow()
    {
        _motion = new MotionGate(this);
        InitializeComponent();
        PART_Root.Background = _bgBrush; // template-lokal, donmamış brush (A13.2) — 120ms renk geçişi bunu animate eder
        DataContextChanged += OnDataContextChanged;
        MouseEnter += (_, _) => SetHover(true);
        MouseLeave += (_, _) => SetHover(false);
        MouseLeftButtonUp += OnRowClicked;
        KeyDown += OnRowKeyDown;
        _motion.Changed += OnAnimationsEnabledChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        // [L1] Hover ikonlarının kablajı ctor'dan EnsureActions'a taşındı — ikonlar artık ilk hover'da doğuyor.
    }

    /// <summary>[L1/It-5 perf] Hover eylem bloğunu (folder + VS ikonları, VS-chooser popover'ı) İLK HOVER'da bir
    /// kez kurar ve sağ bloğa ekler. Öncesinde bu 16 nesne her satırda hevesle kuruluyordu (191 satırda ~3056),
    /// hiç hover edilmese bile. Kurulduktan sonra satır ömrü boyunca kalır (hover-out yalnız Collapse eder) —
    /// böylece hover/leave döngüsü tekrar tekrar inşa etmez. Kablaj (Click/Opened) burada, çünkü öğeler ancak
    /// burada var olur; DAVRANIŞ (OnRevealClick/OnVsClick/PopIn) satırda kalır.</summary>
    private ProjectRowActions EnsureActions()
    {
        if (_actions is { } existing) return existing;

        var actions = new ProjectRowActions();
        // [E1/T67] Hover ikonları → OS eylemleri (VM üzerinden). Chooser popover'ı D6 deseni: açılışta PopIn.
        actions.RevealButton.Click += OnRevealClick;
        actions.VsButton.Click += OnVsClick;
        actions.VsChooser.Opened += (_, _) => PopIn.Play(actions.VsChooserContent);
        PART_RightBlock.Children.Add(actions); // sha ile AYNI blok (üstünde) — eski XAML sırasıyla birebir
        _actions = actions;
        return actions;
    }

    // ---------------------------------------------------------------- test yüzeyi
    internal Rectangle Stripe => PART_Stripe;
    internal WillBuildDot Dot => PART_Dot;
    internal TextBlock DurationText => PART_Duration;
    internal TextBlock ShaText => PART_Sha;
    /// <summary>[L1] Hover eylem bloğu — İLK HOVER'a kadar <c>null</c> (hiç kurulmaz).</summary>
    internal FrameworkElement? HoverIcons => _actions?.HoverIcons;
    internal ProjectRowActions? Actions => _actions;
    /// <summary>[L1] <see cref="ApplyAll"/> çağrı sayacı — satır başına BİR kez koştuğunu pinleyen test seam'i.</summary>
    internal int ApplyAllCount { get; private set; }
    internal FrameworkElement DepSlot => PART_DepSlot;
    internal FrameworkElement DepIcon => PART_DepIcon;
    /// <summary>[design v1.7.0 §2.4] Uyarı slotundaki TEK üçgen — rengi nedeni söyler (turuncu = yapısal
    /// döngü, amber = geçici dep-issue).</summary>
    internal Path DepTriangle => PART_DepTriangle;
    internal FrameworkElement BreathLayer => PART_Breath;
    internal void SimulateHover(bool hover) => SetHover(hover);
    internal TranslateTransform InnerTranslate => PART_InnerTranslate;
    internal Border Root => PART_Root;                              // [T42] reveal opacity taşıyıcısı
    internal TranslateTransform ShakeTranslate => PART_ShakeTranslate; // [T42] reveal kayması Y'de akar (shake X)
    internal StatusGlyph Glyph => PART_Glyph;
    internal string? DepTooltip => PART_DepTip.Content as string;   // [Fix wave 1, Finding 3] birebir metin testi
    internal string? GlyphTooltip => PART_GlyphTip.Content as string;

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
        // [Fix wave 1 · D1 review Minor 5 · W2] İdempotent abonelik (her Loaded'da -= sonra +=) MotionGate'te;
        // gate'in kablajı ctor'da kurulduğu için bu handler'dan ÖNCE koşar (eski sıra birebir).

        // [L1/It-5 perf] ApplyAll BURADA TEKRAR koşmaz. Üretimde satır, DataContext'i miras aldığı anda ZATEN
        // ağaçtadır (ItemsControl önce container'ı ağaca ekler, sonra şablonu uygular) → ilk ApplyAll eksiksizdir
        // ve Loaded'daki ikinci koşum satır başına ~10 SetResourceReference + 3 animasyon kurulumunu boşuna
        // tekrarlıyordu. Geriye yalnız Loaded'ın GERÇEKTEN değiştirebildiği iki şey kalır:
        //   · sağ blok — hover/görünürlük durumu (sha ARTIK buna bağlı DEĞİL: [W1] ile hem cur hem target satır
        //     VM'inden gelir, yani ağaç dışında kurulmuş bir satırda bile eksiksizdir),
        //   · nefes — Unloaded StopBreathing çağırır, yeniden yüklenen satırda saat geri kurulmalı.
        if (!_applied) { ApplyAll(); return; }
        ApplyRightBlock();
        ApplyBreathing();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // [W2] Motion aboneliğini MotionGate bırakır (bu handler'dan ÖNCE — kablaj sırası eskisiyle birebir).
        StopBreathing(); // GraphView deseni: durum building'i terk edince / unload'da clock serbest
    }

    private void OnAnimationsEnabledChanged(object? sender, EventArgs e) => ApplyBreathing();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = e.NewValue as ProjectRowViewModel;
        _prevState = null;
        _applied = false; // yeni VM → tam tazeleme yeniden gerekir (container yeniden kullanımı dahil)
        if (_vm is not null) _vm.PropertyChanged += OnVmPropertyChanged;
        ApplyAll();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ProjectRowViewModel.State):
                // [Fix wave 1, Finding 1] Statü-türevi görseller (glyph/şerit/tooltip) ARTIK Status case'inde;
                // State setter'ı NotifyPropertyChangedFor(Status) ile onu hemen ardından tetikler. Burada yalnız
                // State'e özel yan etkiler kalır: shake/nefes geçişi + süre + sağ blok (building geçişinde sha).
                ApplyStateTransition();
                ApplyDuration();
                ApplyRightBlock();
                break;
            case nameof(ProjectRowViewModel.Status):
                ApplyStatusVisuals(); // [Fix wave 1, Finding 1] cycle/queued dahil TEK eşleme yolundan gelir
                ApplyDep();           // [cycles] üyelik rozetinin kapısı Status'tur — onunla birlikte tazelenir
                // [cycles] CycleWaiting setter'ı Status'u da tetikler (RunViewModel.cs) — sıra kardeşe geçtiği ANDA
                // nefes/süre burada da tazelenmeli. State case'i zaten çağırıyor; çift çağrı zararsız, iki metod
                // da idempotent.
                ApplyBreathing();
                ApplyDuration();
                break;
            case nameof(ProjectRowViewModel.InCycle):
                ApplyDep();           // [cycles] topoloji üyeliği değiştirmiş olabilir
                break;
            case nameof(ProjectRowViewModel.WillBuild):
                PART_Dot.State = _vm?.WillBuild;
        PART_Dot.InCycle = _vm?.InCycle ?? false;
                PART_Dot.InCycle = _vm?.InCycle ?? false;
                ApplyRightBlock();
                // Not: WillBuild, Status'u (queued) da tetikler → şerit/glyph Status case'inde tazelenir.
                break;
            case nameof(ProjectRowViewModel.DepIssues):
            case nameof(ProjectRowViewModel.HasDepIssue):
            case nameof(ProjectRowViewModel.NamePrefix): // [D5] önek sonradan değişirse dep-tooltip'i tazele
            case nameof(ProjectRowViewModel.CycleUnsettled):   // [cycle rounds/Task 9] üçgen tooltip dalı
            case nameof(ProjectRowViewModel.CycleUnconverged): // [cycle rounds/Task 9] dep-slot rozeti
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
            case nameof(ProjectRowViewModel.TargetSha): // [W1] syncCompleted buildPreview'dan SONRA gelse de tazelenir
                ApplySha();
                break;
        }
    }

    // ---------------------------------------------------------------- toplu tazeleme
    private void ApplyAll()
    {
        _applied = true;
        ApplyAllCount++;
        _prevState = _vm?.State;
        PART_Name.Text = _vm?.Name;
        // [E5/T47] Kart klavye ile odaklanınca ekran okuyucu proje ADINI okusun (ikon/şerit/glyph görselleri SR'a
        // bir şey söylemez). Ad, satır VM'inden gelir (İngilizce proje adı).
        System.Windows.Automation.AutomationProperties.SetName(this, _vm?.Name ?? "");
        PART_Sln.Text = _vm?.SolutionName;
        PART_Dot.State = _vm?.WillBuild;
        PART_Dot.InCycle = _vm?.InCycle ?? false;
        ApplyStatusVisuals(); // glyph/ad-rengi/şerit/tooltip (Status'tan)
        ApplyBreathing();     // building nabzı (State'ten) — ilk kurulumda shake YOK (_prevState taze)
        ApplyDep();
        ApplyDuration();
        ApplySelection();  // şerit genişliği/renk + translateX + zemin
        ApplyRightBlock(); // sha/hover ikonları
    }

    /// <summary>[Fix wave 1, Finding 1] Statü-türevi görseller: glyph, ad soluk/parlak, şerit rengi, glyph
    /// tooltip. Statü kaynağı <see cref="ProjectRowViewModel.Status"/> (cycle/queued dahil TEK eşleme yeri) —
    /// kart artık kendi eşlemesini yapmaz.</summary>
    private void ApplyStatusVisuals()
    {
        GraphStatus status = _vm?.Status ?? GraphStatus.Discovered;
        var state = _vm?.State ?? ProjectRowState.Pending;

        PART_Glyph.Status = status;

        // [design v1.7.0 §2.4] Ad TEK kurala bağlıdır: bu koşuda İŞİ OLAN satır (dirty · queued · building ·
        // failed) primary beyaz, güncel/atlanacak satır secondary gri. Eski kural alt-duruma bakıyordu ve
        // "derlenecek ama henüz sırası gelmemiş" bir satırı da soluk gösteriyordu — oysa onun işi var.
        // Kalınlık HER ZAMAN 500'dür (XAML); bold satır ritmini bozuyordu.
        bool hasWork = state is ProjectRowState.Started or ProjectRowState.Failed
                       || (state == ProjectRowState.Pending && _vm?.WillBuild == true);
        PART_Name.SetResourceReference(TextBlock.ForegroundProperty,
            hasWork ? "Brush.TextPrimary" : "Brush.TextSecondary");

        SetStripeFill();
        UpdateGlyphTooltip();
    }

    /// <summary>State'e özel geçiş yan etkileri: hata ANINDA bir kez shake + building nefes geçişi.</summary>
    private void ApplyStateTransition()
    {
        var state = _vm?.State ?? ProjectRowState.Pending;
        // Shake yalnız hata ANINDA (Pending/Started/... → Failed geçişinde), bir kez.
        if (state == ProjectRowState.Failed && _prevState is not null && _prevState != ProjectRowState.Failed)
            PlayShake();
        _prevState = state;
        ApplyBreathing();
    }

    /// <summary>
    /// [design v1.7.0 §2.4 — A kanalı] Sol şerit "bu koşuda ne oldu" der ve HER SATIRDA vardır: workspace
    /// açıldığı andan itibaren gri, koşuda amber, bitişte sonuç rengi.
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> <c>discovered</c> eskiden ŞERİTSİZDİ (transparent) ve <c>skipped</c>'ten
    /// farklı bir griye sahipti. İkisi de düzeltildi: şerit hiç kaybolmaz (Sync şeridi getirmez, zaten
    /// oradadır — Sync yalnız plan kanalını tazeler) ve iki gri TEK griye indi; "bazıları koyu bazıları açık"
    /// iki ayrı gri, aralarında bir anlam varmış izlenimi veriyordu. Zincir: gri → açık gri (queued) → amber
    /// (building) → yeşil/kırmızı.</para>
    /// </summary>
    private void SetStripeFill()
    {
        string key = (_vm?.Status ?? GraphStatus.Discovered) switch
        {
            GraphStatus.Queued => "Brush.StatusQueued",
            GraphStatus.Building => "Brush.Amber",
            GraphStatus.Succeeded => "Brush.StatusSuccess",
            GraphStatus.Failed => "Brush.StatusFail",
            _ => "Brush.StatusSkippedBorder", // discovered ve skipped AYNI gri
        };
        PART_Stripe.SetResourceReference(Shape.FillProperty, key);
    }

    private void ApplyDuration()
    {
        var state = _vm?.State ?? ProjectRowState.Pending;
        long ms = _vm?.DurationMs ?? 0;
        // Canlı elapsed yalnız GERÇEKTEN derlenen satırda; grubunun sırasını bekleyen üye (Started ama
        // IsCompiling değil) "—" gösterir — sayacı her turda sıfırlanıp yeniden koşan bir bekleme süresi
        // bilgi değil gürültüydü. Terminal satır kesin süresini (turların toplamı) gösterir.
        PART_Duration.Text = _vm?.IsCompiling ?? false
            ? DurationFormat.Elapsed(ms)
            : state == ProjectRowState.Started
                ? DurationFormat.Duration(null)
                : DurationFormat.Duration(ms == 0 ? null : ms);
        PART_Duration.SetResourceReference(TextBlock.ForegroundProperty,
            state == ProjectRowState.Failed ? "Brush.StatusFailText" : "Brush.TextDim");
    }

    /// <summary>
    /// [design v1.7.0 §2.4] Uyarı slotu: TEK üçgen, rengi EN AĞIR nedeni söyler ve tooltip nedenleri alt alta
    /// listeler.
    /// <list type="bullet">
    /// <item><b>Döngü üyeliği → turuncu</b> (<c>Brush.StatusCycle</c>). Yapısal ve KALICIDIR: satırın dep-issue'su
    /// da olsa turuncu kazanır, çünkü geçici olan diğeridir.</item>
    /// <item><b>Yalnız dep-issue → amber</b> (<c>Brush.AmberText</c>). Geçicidir: bağımlılık düzelince bir
    /// sonraki koşu temizler. Kırmızı KULLANILMAZ — kırmızı sonuç kanalınındır ("derlendi ve patladı"),
    /// oysa bu satır kendi işini yapmış olabilir.</item>
    /// <item><b>Satır building iken slot GİZLİDİR</b> — dönen spinner'la yarışmaz.</item>
    /// </list>
    /// Statü glyph'i bundan ETKİLENMEZ: o daima gerçek statüyü gösterir, uyarı onun yerine asla geçmez.
    /// </summary>
    private void ApplyDep()
    {
        bool building = _vm?.IsCompiling ?? false;
        bool inCycle = _vm?.InCycle ?? false;
        bool hasDepIssue = _vm?.HasDepIssue ?? false;
        bool cycleUnsettled = _vm?.CycleUnsettled ?? false;
        bool cycleUnconverged = _vm?.CycleUnconverged ?? false;
        bool show = !building && (inCycle || hasDepIssue || cycleUnsettled || cycleUnconverged);

        PART_DepIcon.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) { PART_DepTip.Content = null; UpdateGlyphTooltip(); return; }

        bool structural = inCycle || cycleUnsettled || cycleUnconverged;
        PART_DepTriangle.SetResourceReference(Shape.StrokeProperty,
            structural ? "Brush.StatusCycle" : "Brush.AmberText");

        // Nedenler alt alta: en ağırdan (yapısal, kalıcı) en hafife (geçici dep-issue).
        var reasons = new List<string>(3);
        if (cycleUnconverged) reasons.Add(CycleUnconvergedTooltip);
        else if (cycleUnsettled) reasons.Add(CycleUnsettledTooltip);
        else if (inCycle) reasons.Add(CycleMembershipTooltip);
        if (hasDepIssue && _vm?.DepIssues is { } issues)
        {
            // Kısa adlar (veri-türevli ortak önek atılmış — D5); önek satıra RunViewModel'den itilir.
            string prefix = _vm?.NamePrefix ?? "";
            string names = string.Join(", ", issues.Select(n => GraphNode.ShortLabel(n, prefix)));
            reasons.Add($"Dependency issue: {names} — last successful output referenced");
        }
        PART_DepTip.Content = string.Join(Environment.NewLine, reasons);
        UpdateGlyphTooltip();
    }

    private void ApplySha()
    {
        // [W1] "{cur7} → {target7}" (design-v1 README §kart slot 4 + "SHA 7 hane a3f81c2"). İKİ YARI DA burada
        // kısaltılır: kaynaklar HAM 40-hex'tir (cur = BuildState.BuiltCommit, target = remote-tracking ref) ve
        // 118px'lik slota ham hâlleri sığmaz. Kısaltma tek yerden (RunViewModel.Short7 — branch popover'ı da onu
        // kullanır) gelir; ikinci bir kırpma yardımcısı yazılmaz.
        //
        // İKİSİ DE SATIR VM'inden okunur: target artık ata ağaçtan ÇEKİLMİYOR (RunViewModel her satıra itiyor),
        // böylece syncCompleted buildPreview'dan SONRA gelse bile satır kendi PropertyChanged'iyle tazelenir.
        //
        // HİÇ DERLENMEMİŞ proje (BuiltCommit yok) ⇒ sol yarı boştur: çift yerine YALNIZ hedef basılır — yalın-ok
        // pürüzü (" → b7e91d4") üretilmez. Görünürlük ApplyRightBlock'ta.
        // [design v1.7.0 §2.4] SHA HER satırda görünür ve iki biçimi vardır: derlenecek satırda çift
        // ("cur → target", secondary), güncel satırda TEK sha (faint). Eskiden yalnız dirty satırlarda
        // gösteriliyordu ve hover'dan çıkıldığında satırlar arasında layout sıçraması oluyordu.
        string cur = Short7(_vm?.CurrentSha);
        string target = Short7(_vm?.TargetSha);
        bool dirty = _vm?.WillBuild == true;
        PART_Sha.Text = !dirty || cur.Length == 0 || cur == target ? target : $"{cur} → {target}";
        PART_Sha.SetResourceReference(TextBlock.ForegroundProperty,
            dirty ? "Brush.TextSecondary" : "Brush.TextFaint");
    }

    private static string Short7(string? sha) => sha is null ? "" : ViewModels.RunViewModel.Short7(sha);

    /// <summary>Sağ blok: hover'da aç-ikonları, değilse (will==dirty) sha çifti (BuildApp.jsx:387-403).
    /// [L1] İkon bloğu hover'da TALEP ÜZERİNE kurulur; hover yokken kurulmamışsa dokunulacak bir şey de yoktur.</summary>
    private void ApplyRightBlock()
    {
        bool showIcons = _hover;
        bool showSha = !_hover; // [design v1.7.0 §2.4] SHA her satırda — yalnız hover ikonları onu örter
        if (showIcons) EnsureActions().HoverIcons.Visibility = Visibility.Visible;
        else if (_actions is { } actions) actions.HoverIcons.Visibility = Visibility.Collapsed;
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
        // [L1/It-5 perf] Zemin zaten hedef renkteyse geçiş kurma (ilk uygulamada HER satırda Transparent→Transparent
        // idi → satır başına iki kaynak-zinciri yürüyüşü + bir renk saati). Uçuşta saat varsa atlanmaz (bkz. AnimateDouble).
        if (!_bgBrush.HasAnimatedProperties && _bgBrush.Color == target) return;
        MotionTokens.TransitionColor(this, _bgBrush, target);
    }

    /// <summary>[review fix 2] <c>PART_Glyph</c> satırın TEK her-zaman-görünür yüzeyidir — dep-slot boşken sıfır
    /// yükseklikte çöker (<c>DepSlot</c>), bu yüzden döngü durumları BURADA da duyurulmalı, yalnız dep-slot'un
    /// kendi tooltip'inde değil. Sıra <see cref="ApplyDep"/>'in 4-yollu önceliğiyle AYNI (CycleUnconverged —
    /// yalnız Skipped'te, RunCounters kapısıyla aynı — &gt; dep-issue &gt; CycleUnsettled &gt; sıradan döngü
    /// üyeliği); metinler TEKRAR YAZILMAZ, dep-slot'un KENDİ sabitleri (<see cref="CycleUnconvergedTooltip"/>/
    /// <see cref="CycleUnsettledTooltip"/>/<see cref="CycleMembershipTooltip"/>) reuse edilir.</summary>
    private void UpdateGlyphTooltip()
    {
        var state = _vm?.State ?? ProjectRowState.Pending;
        GraphStatus status = _vm?.Status ?? GraphStatus.Discovered;
        // [A13/T5] design-v1 EN_STATUS eşlemesi artık STATUS_META'nın yanında (StatusGlyph.LabelFor) — graf
        // düğümünün ekran-okuyucu adı ikinci tüketicisidir, kopya YASAK.
        string text = StatusGlyph.LabelFor(status);
        if (state == ProjectRowState.Started)
            text += " — " + DurationFormat.Elapsed(_vm?.DurationMs ?? 0);
        else if (state == ProjectRowState.Skipped && (_vm?.CycleUnconverged ?? false))
            text += " — " + CycleUnconvergedTooltip;
        else if (_vm?.HasDepIssue ?? false)
            text += " — dependency issue";
        else if (_vm?.CycleUnsettled ?? false)
            text += " — " + CycleUnsettledTooltip;
        else if ((_vm?.InCycle ?? false) && status != GraphStatus.Cycle)
            text += " — " + CycleMembershipTooltip;
        PART_GlyphTip.Content = text;
    }

    // ---------------------------------------------------------------- nefes / shake
    private void ApplyBreathing()
    {
        bool building = _vm?.IsCompiling ?? false;
        // Katman "yalnız building'de var": görünürlük motion'dan BAĞIMSIZ (reduced-motion'da da building satırda
        // katman durur ama opaklık 0 kalır = görünmez). Animasyon yalnız motion açıkken döner.
        PART_Breath.Visibility = building ? Visibility.Visible : Visibility.Collapsed;

        bool shouldBreathe = building && AnimationsEnabledProvider();
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

    // ---------------------------------------------------------------- [E3/T42] liste mount reveal (bo-reveal)

    /// <summary>[T42] Liste satırı reveal gecikmesi — 10ms/satır, 380ms'de tavan (BuildApp.jsx:367). Saf/pinli;
    /// graf katman stagger'ı (<see cref="Graph.GraphView.RevealDelayMs"/>, 55ms/330ms) ile AYNI aile, FARKLI formül.</summary>
    internal static double RevealDelayMs(int index) => Math.Min(Math.Max(index, 0) * RowStaggerMs, RowStaggerCapMs);

    /// <summary>[T42/bo-reveal] Satırı KADEMELİ belirt: opacity 0→1 + translateY(-5→0), 300ms ease-out, gecikme =
    /// <see cref="RevealDelayMs"/>(index). Reduced-motion (AnimationsEnabled false) iken ANİ — opacity 1, kayma yok.
    /// Gecikme boyunca opacity 0 TUTULUR (flash yok) — <see cref="Graph.GraphView"/> per-node reveal deseni. Kayma
    /// PART_ShakeTranslate'in Y ekseninde akar (shake X'i kullanır — çakışma yok).
    ///
    /// <para>[E4/T48] <paramref name="animate"/> verilirse satırın kendi <see cref="AnimationsEnabledProvider"/>'ı
    /// YERİNE onu kullanır — StickyLayerList reveal-hero wiring'i (bir hero bloke ederse ani sonuç) tüm satırların
    /// AYNI kararla oynamasını böyle garanti eder. null (varsayılan) → satır kendi sinyalini okur (mevcut davranış).</para></summary>
    internal void PlayReveal(int index, bool? animate = null)
    {
        PART_Root.BeginAnimation(OpacityProperty, null);
        PART_ShakeTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        if (!(animate ?? AnimationsEnabledProvider()))
        {
            PART_Root.Opacity = 1.0;
            PART_ShakeTranslate.Y = 0;
            return;
        }

        var spline = MotionTokens.ResolveKeySpline(this, "KeySpline.EaseOut", new KeySpline(0.22, 1, 0.36, 1));
        var begin = TimeSpan.FromMilliseconds(RevealDelayMs(index));
        var duration = TimeSpan.FromMilliseconds(RevealMs);

        // CSS `both` fill paritesi: gecikme boyunca 0 tutulur (Discrete 0 @ t=0), sonra hedefe ramp.
        PART_Root.Opacity = 0.0;
        var fade = MotionTokens.SplineTo(1.0, duration, spline);
        fade.BeginTime = begin;
        fade.KeyFrames.Insert(0, new DiscreteDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        PART_Root.BeginAnimation(OpacityProperty, fade);

        PART_ShakeTranslate.Y = -RevealRisePx;
        var slide = MotionTokens.SplineTo(0.0, duration, spline);
        slide.BeginTime = begin;
        slide.KeyFrames.Insert(0, new DiscreteDoubleKeyFrame(-RevealRisePx, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        PART_ShakeTranslate.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void PlayShake()
    {
        if (!AnimationsEnabledProvider()) return;
        PART_ShakeTranslate.BeginAnimation(TranslateTransform.XProperty, BuildShakeAnimation(this), HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>[A13/T4 · m1 test seam] Shake animasyonunu üreten TEK yer — kontrol ve test AYNI fabrikayı
    /// kullanır (<see cref="BuildBreathingAnimation"/> deseni): 360ms süre + BuildApp.jsx:30 keyframe'leri
    /// (10%,90%→∓2 · 25%,75%→±3 · 50%→∓3 · 100%→0) burada pinlenir (inline magic number YOK).</summary>
    internal static DoubleAnimationUsingKeyFrames BuildShakeAnimation(FrameworkElement host)
    {
        var spline = MotionTokens.ResolveKeySpline(host, "KeySpline.EaseStandard", new KeySpline(0.4, 0, 0.2, 1));
        // [Fix wave 1 · D1 review Minor 4] FillBehavior.Stop: keyframe'ler zaten 0'da biter → görsel aynı, ama
        // varsayılan HoldEnd'in aksine clock BİTİNCE serbest kalır (her shake'lenmiş satırda takılı saat kalmaz).
        var anim = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        void Frame(double v, double pct) =>
            anim.KeyFrames.Add(new SplineDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ShakeMs * pct)), spline));
        // BuildApp.jsx:30 keyframe'leri: 10%,90% → -2 · 25%,75% → +3 · 50% → -3.
        Frame(-2, 0.10); Frame(3, 0.25); Frame(-3, 0.50); Frame(3, 0.75); Frame(-2, 0.90); Frame(0, 1.0);
        return anim;
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
        // [L1/It-5 perf] Hedef zaten sağlanmışsa hiçbir şey yapma. İlk uygulamada (ApplyAll) satır seçili DEĞİLDİR:
        // şerit zaten 2, iç-sarmalayıcı zaten 0 — yine de iki animasyon kurulumu ve dört kaynak-zinciri yürüyüşü
        // (ResolveDuration + ResolveKeySpline) satır başına ödeniyordu. Uçuşta bir saat varsa ATLANMAZ: o durumda
        // okunan değer animasyonun ANLIK değeridir, hedefe eşit görünse bile devam ediyor olabilir.
        if (!target.HasAnimatedProperties && ((DependencyObject)target).GetValue(prop) is double current && current == to)
            return;

        bool enabled = AnimationsEnabledProvider();
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

    // ---------------------------------------------------------------- [E1/T67] hover ikon eylemleri
    /// <summary>Klasör ikonu → dosyayı Explorer'da seçili aç (satır Id'si = csproj yolu). Buton mouse event'i
    /// handled ettiğinden satır seçimi (OnRowClicked) tetiklenmez.</summary>
    private void OnRevealClick(object sender, RoutedEventArgs e)
    {
        if (_vm is { } vm) FindRunViewModel()?.RevealProjectInExplorer(vm.Id);
    }

    /// <summary>VS ikonu → bağlı solution'ı VS'de aç. Birden çok solution varsa VM chooser adaylarını döndürür →
    /// küçük seçim popover'ı açılır (D6 deseni). Tek/sıfır solution'da (chooser null/boş) hiçbir şey açılmaz —
    /// eylem zaten VM içinde tamamlandı (opened / no-sln / VS-not-found).</summary>
    private async void OnVsClick(object sender, RoutedEventArgs e)
    {
        // [L1] Tıklama ancak KURULMUŞ bloktan gelebilir (buton onun içinde doğar) — burada yeniden inşa YOK.
        if (_vm is not { } vm || _actions is not { } actions) return;
        // await ZORUNLU: devenv çözümü (vswhere) UI thread'inde beklenirse pencere saniyelerce ölür.
        if (FindRunViewModel() is not { } run) return;
        var chooser = await run.OpenProjectInVisualStudioAsync(vm.Id);
        if (chooser is not { Count: > 0 }) return;
        BuildVsChooserRows(actions, chooser);
        actions.VsChooser.IsOpen = true;
    }

    private void BuildVsChooserRows(ProjectRowActions actions, IReadOnlyList<SolutionRef> candidates)
    {
        actions.VsChooserRows.Children.Clear(); // minik non-virtualized liste (BranchPopover deseni)
        foreach (var sln in candidates) actions.VsChooserRows.Children.Add(BuildVsRow(actions, sln));
    }

    private Border BuildVsRow(ProjectRowActions actions, SolutionRef sln)
    {
        var name = new TextBlock
        {
            Text = sln.Name,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = AppFonts.Mono,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        name.SetResourceReference(FontSizeProperty, "FontSize.Xs");
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");

        var row = new Border
        {
            Height = 28,
            Padding = new Thickness(6, 0, 6, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = name,
        };
        row.SetResourceReference(Border.CornerRadiusProperty, "Radius.Sm");
        HoverBackground.Attach(row);
        row.MouseLeftButtonUp += async (_, _) =>
        {
            actions.VsChooser.IsOpen = false; // seçince kapan (BranchPopover.Pick deseni)
            if (_vm is { } vm && FindRunViewModel() is { } run)
                await run.OpenSolutionInVisualStudioAsync(vm.Id, sln); // bkz. OnVsClick: vswhere UI'da beklenmez
        };
        return row;
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

    private Color ResolveColor(string key, Color fallback) =>
        TryFindResource(key) is SolidColorBrush b ? b.Color : fallback;
}
