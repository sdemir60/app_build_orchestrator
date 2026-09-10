using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
/// [T53/T54-UI] design-v1 proje kartı (BuildApp.jsx:355-416). 7 slot: statü şeridi · StatusDot · ad+sln ·
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

    // [design v1.11.0 §2.4-6] Uyarı üçgeninin metni SAF bir çekirdekten gelir (ViewModels/RowWarning) — kart
    // kendi cümlelerini KURMAZ. Metinler eskiden burada üç sabit olarak duruyordu ve tooltip onları alt alta
    // diziyordu; v1.11.0 tooltip'i TEK SATIRA indirdi.

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
        // [design v1.11.0 §9-6] Satıra SAĞ TIK, ⋯ düğmesiyle AYNI menüyü açar (VS Solution Explorer
        // alışkanlığı). Menü ⋯'in altında konumlanır: imlecin altında değil, satırın kendi çapasında —
        // böylece iki yol da AYNI yerde aynı menüyü gösterir.
        MouseRightButtonUp += OnRowRightClicked;
        KeyDown += OnRowKeyDown;
        _motion.Changed += OnAnimationsEnabledChanged;
        // [design v1.11.0 §2.3] Nokta da satırın motion kapısını kullanır — iki yüzey aynı sinyali okur.
        PART_Dot.AnimationsEnabledProvider = () => _motion.Enabled;
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
        // [design v1.11.0 §9-6] Menünün AÇIK/KAPALI kapısı ⋯ düğmesinin kendisidir (popup'ın IsOpen'ı ona
        // iki-yönlü bağlıdır). Kablaj popup'a DEĞİL düğmeye takılır: sağ tık da bu düğmeyi işaretler ve
        // sağ blok kuralı (menü açıkken ikonlar görünür kalır) popup'ın gerçekten açılmasını beklemeden işler.
        actions.MoreButton.Checked += (_, _) =>
        {
            actions.RowMenuContent.Title = ShortName();
            // [design §3.8] Menü açılırken Build/Rebuild kapısı koşu kapısından okunur (prototip `busy`).
            actions.RowMenuContent.SetRunActionsEnabled(CanRunProject());
            ApplyRightBlock();
        };
        actions.MoreButton.Unchecked += (_, _) => ApplyRightBlock();
        // [tek proje · design §3.8] Play → YALNIZ bu projeyi derleyen kapsamlı komut (parametre satırın kimliği,
        // ApplyActionState yazar); Stop → ana Stop komutunun TA KENDİSİ (ikinci bir durdurma yolu yok). Komutlar
        // RunViewModel'de yaşar, düğmeler WPF'in CanExecute → IsEnabled kablosunu kullanır: koşu uçuştayken play
        // kendiliğinden pasifleşir. Menü maddeleri komutu satır üzerinden çalıştırır — menü projesini bilmez.
        var run = FindRunViewModel();
        actions.BuildButton.Command = run?.BuildProjectCommand;
        actions.StopButton.Command = run?.StopCommand;
        actions.RowMenuContent.ItemInvoked += OnRowMenuItem;
        // [design v1.11.0 §9-6] Menünün çapası SATIRIN KENDİSİDİR, ⋯ düğmesi değil (BuildApp.jsx:609
        // `right: 8`). Yerleşim Custom'dır: WPF geri çağrıyı menü ÖLÇÜLDÜKTEN sonra çağırır, yani menünün
        // gerçek genişliği/yüksekliği hesaba girer — sabit bir offset yazmak (eski `-118`) ikon sayısı ya da
        // menü genişliği değişince sessizce bozulurdu.
        actions.RowMenu.PlacementTarget = PART_Root;
        actions.RowMenu.CustomPopupPlacementCallback = PlaceRowMenu;
        PART_RightBlock.Children.Add(actions); // sha ile AYNI blok (üstünde) — eski XAML sırasıyla birebir
        _actions = actions;
        return actions;
    }

    // ---------------------------------------------------------------- test yüzeyi
    internal Rectangle Stripe => PART_Stripe;
    internal StatusDot Dot => PART_Dot;
    internal TextBlock DurationText => PART_Duration;
    internal TextBlock DecisionText => PART_Decision;
    /// <summary>[L1] Hover eylem bloğu — İLK HOVER'a kadar <c>null</c> (hiç kurulmaz).</summary>
    internal FrameworkElement? HoverIcons => _actions?.HoverIcons;
    internal ProjectRowActions? Actions => _actions;
    /// <summary>[L1] <see cref="ApplyAll"/> çağrı sayacı — satır başına BİR kez koştuğunu pinleyen test seam'i.</summary>
    internal int ApplyAllCount { get; private set; }
    internal FrameworkElement DepSlot => PART_DepSlot;
    internal FrameworkElement DepIcon => PART_DepIcon;
    /// <summary>[design v1.11.0 §2.4-6] Uyarı slotundaki TEK üçgen — HER ZAMAN amber.</summary>
    internal Path DepTriangle => PART_DepTriangle;
    internal FrameworkElement BreathLayer => PART_Breath;
    internal void SimulateHover(bool hover) => SetHover(hover);
    internal TranslateTransform InnerTranslate => PART_InnerTranslate;
    internal Border Root => PART_Root;                              // [T42] reveal opacity taşıyıcısı
    internal TranslateTransform ShakeTranslate => PART_ShakeTranslate; // [T42] reveal kayması Y'de akar (shake X)
    internal StatusGlyph Glyph => PART_Glyph;
    internal string? DepTooltip => PART_DepTip.Content as string;   // [Fix wave 1, Finding 3] birebir metin testi
    internal TextBlock NameText => PART_Name;

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
        // [design v1.11.0 §2.4-1] Başlangıç modunun KESİKLİ şerit fırçası bir DrawingBrush'tır ve rengini
        // ANINDA çözer (SetResourceReference gibi geç bağlanamaz). Satır ağaca girmeden ApplyAll koştuysa
        // (DataContext, Loaded'dan ÖNCE gelir) o çözüm boşa düşer — burada bir kez tazelenir. Düz dolgu
        // yolunda no-op'tur (SetResourceReference zaten geç bağlıdır).
        SetStripeFill();
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
        // [design v1.12.0] Geri dönüştürülen container YENİ verisinin hâline ANINDA oturur: çapraz-sönüm bir
        // durum değişimini anlatır, veri değişimini değil (gerekçe StartMode.ShouldCrossFade'de).
        _stripeWasStartMode = null;
        PART_Dot.ResetTransitionLatch();
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
                ApplyStatusVisuals(); // [Fix wave 1, Finding 1] queued dahil TEK eşleme yolundan gelir
                ApplyDep();           // [cycles] üyelik rozetinin kapısı Status'tur — onunla birlikte tazelenir
                // [cycles] CycleWaiting setter'ı Status'u da tetikler (RunViewModel.cs) — sıra kardeşe geçtiği ANDA
                // nefes/süre burada da tazelenmeli. State case'i zaten çağırıyor; çift çağrı zararsız, iki metod
                // da idempotent.
                ApplyBreathing();
                ApplyDuration();
                break;
            // [design v1.11.0 §9-2] Görsel durumun İKİ ek girdisi: başlangıç modu ve işaretlilik. Statü
            // değişimi zaten yukarıdan geçer (VisualStatus onunla birlikte tazelenir); bu iki bayrak statüyü
            // DEĞİŞTİRMEDEN de görünümü çevirir.
            case nameof(ProjectRowViewModel.Fresh):
            case nameof(ProjectRowViewModel.Marked):
                // [design v1.11.0 §2.3] İşaretlilik = işaretleme DALGASI. Bu tek kanalda renk AKAR (200ms),
                // çakmaz — satır node'la senkron yanmalıdır. Diğer tüm yollarda renk anında oturur.
                ApplyStatusVisuals(lighting: true);
                break;
            case nameof(ProjectRowViewModel.InCycle):
                ApplyDep();           // [cycles] topoloji üyeliği değiştirmiş olabilir
                break;
            case nameof(ProjectRowViewModel.WillBuild):
                // [design v1.11.0 §9-1] Plan kanalının TEK görünür kalıntısı çift SHA metnidir — nokta ARTIK
                // planı taşımaz (statü rengini taşır). Bu yüzden burada yalnız sağ blok tazelenir.
                ApplyRightBlock();
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
                break;
            case nameof(ProjectRowViewModel.IsSelected):
                ApplySelection();
                break;
            case nameof(ProjectRowViewModel.IsRunTarget): // [§3.8] hedef satır: play ↔ Stop, hover'sız görünürlük
            case nameof(ProjectRowViewModel.IsRunLocked): // [§3.8] play tooltip'i: boşta / koşarken
                ApplyRightBlock();
                break;
            case nameof(ProjectRowViewModel.Fade):
                ApplyFade();
                break;
            case nameof(ProjectRowViewModel.SolutionName):
                PART_Sln.Text = _vm?.SolutionName;
                break;
            case nameof(ProjectRowViewModel.LastBuiltAt):
            case nameof(ProjectRowViewModel.OwnFilesChanged):
            case nameof(ProjectRowViewModel.WillBuildReason):
                ApplyDecision();
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
        ApplyStatusVisuals(); // glyph/ad-rengi/şerit/nokta (TEK görsel durumdan)
        ApplyBreathing();     // building nabzı (State'ten) — ilk kurulumda shake YOK (_prevState taze)
        ApplyDep();
        ApplyDuration();
        ApplySelection();  // şerit genişliği/renk + translateX + zemin
        ApplyRightBlock(); // sha/hover ikonları
    }

    /// <summary>[design v1.11.0 §9-2] Statü-türevi görsellerin TEK yazıcısı: glyph, ad vurgusu, sol şerit ve
    /// nokta. Hepsi <see cref="ProjectRowViewModel.VisualStatus"/>'ten beslenir — kart kendi eşlemesini YAPMAZ
    /// (tablo <see cref="VisualStatuses"/>'tedir; graf de aynı tablodan okur).</summary>
    /// <param name="lighting">[design v1.11.0 §2.3] Renk geçişle mi otursun — yalnız işaretleme dalgası
    /// (<see cref="ProjectRowViewModel.Marked"/>/<see cref="ProjectRowViewModel.Fresh"/> kanalı) true verir.</param>
    private void ApplyStatusVisuals(bool lighting = false)
    {
        GraphStatus status = _vm?.Status ?? GraphStatus.Discovered;
        var visual = _vm?.VisualStatus ?? VisualStatus.Discovered;

        PART_Glyph.Status = status;
        // [design v1.11.0 §2.4-5] Glyph TOOLTIP TAŞIMAZ; ekran okuyucunun duyacağı statü metni UIA adına
        // yazılır (eşleme StatusGlyph.LabelFor — kopya YASAK).
        System.Windows.Automation.AutomationProperties.SetName(PART_Glyph, StatusGlyph.LabelFor(status));

        // [design v1.11.0 §2.4-3] Ad TEK kurala bağlıdır: bu İŞLEMDE işi olan satır (marked · queued ·
        // building · succeeded · failed) primary beyaz, geri kalanı secondary gri.
        // [DEĞİŞEN KURAL] Eski kural planı (WillBuild) okuyordu; plan kanalı kalktığı için vurgu da görsel
        // duruma bağlandı. Somut fark: SUCCEEDED satır artık PRIMARY'dir (eskiden secondary'ydi) — bu koşuda
        // gerçekten iş yapmış bir satırın adı, hiç dokunulmamış bir satırla aynı tonda okunamaz.
        // Kalınlık HER ZAMAN 500'dür (XAML); bold satır ritmini bozuyordu.
        //
        // [DEĞİŞEN KURAL — v1.13.2, ölçüm] Renk eskiden SetResourceReference ile ANINDA oturuyordu: dalganın
        // başında bütün adlar birden beyazlıyor, şerit ve nokta ise sırayla amber'a dönüyordu — üç yüzey aynı
        // hareketi anlatmıyordu. Artık ad da AYNI yoldan (TransitionTokenBrush) boyanır: dalgada (lighting=true)
        // 200ms'de akar. Gecikme AYRI bir sabit DEĞİLDİR (kopya YASAK) — bu çağrının kendisi zaten satırın
        // dalga gecikmesi kadar geç gelir, çünkü OperationChoreographer her satırın Marked'ını KENDİ sırasında
        // (NeutralMs + order[i]*stagger) gerçek zamanda değiştirir; şerit (SetStripeFill) ve nokta
        // (PART_Dot.SetState) gecikmelerini de AYNI şekilde, çağrı anından alır — ad üçüncü bir kanal açmaz.
        string nameKey = VisualStatuses.NameIsEmphasised(visual) ? "Brush.TextPrimary" : "Brush.TextSecondary";
        Controls.MotionTokens.TransitionTokenBrush(this, PART_Name, TextBlock.ForegroundProperty, nameKey,
            lighting && _motion.Enabled, Controls.MarkingChoreography.LightMs);

        PART_Dot.SetState(visual, lighting);
        SetStripeFill(lighting);
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
    /// [design v1.13.2 §2.4-1] Sol şerit HER SATIRDA vardır ve <b>noktayla AYNI</b> rengi taşır: başlangıç
    /// modunda da TAM OPAK nötr gri, işaretlenince amber, bitişte sonuç rengi.
    ///
    /// <para><b>[DEĞİŞEN KURAL — v1.12.0]</b> Başlangıç modu KESİKLİ çiziliyordu (tile'lanmış bir
    /// <c>DrawingBrush</c>: 3px dolu / 4px boş). Ölçülen kusur: 2px'lik bir şeritte kesikli desen piksel
    /// ızgarasına oturmuyor, tırtıklı görünüyordu. Şerit artık HER durumda DÜZ bir token fırçasıyla dolar ve
    /// başlangıç modunu OPAKLIK anlatır (<see cref="Controls.StartMode.FaintOpacity"/>, geçiş
    /// <see cref="Controls.StartMode.CrossFadeMs"/>). Noktanın çapraz-sönümüyle AYNI anda, AYNI sürede olur.</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL — v1.13.2, ölçüm]</b> "Sync sonrası liste silik görünüyordu." Başlangıç modunun
    /// <see cref="Controls.StartMode.FaintOpacity"/>'si (eski değeri 0.5) kaldırıldı — artık <c>1.0</c>, yani
    /// başlangıç modu ile başlangıç-dışı hâl arasında opaklık FARKI yok. Kod burada DEĞİŞMEDİ (geçiş köprüsü
    /// hâlâ kurulur, bkz. <see cref="Controls.StartMode.CrossFadeMs"/>'in doc'u) — TEK doğruluk kaynağı
    /// <see cref="Controls.StartMode"/>'daki sabittir.</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL — v1.11.0]</b> <c>Queued</c> eskiden kendi grisini (<c>Brush.StatusQueued</c>)
    /// taşıyordu; artık kuyruk da işlemin kapsamıdır ve amber KALIR — işaretleme dalgasıyla yanan renk koşu
    /// başlayınca sönmez.</para>
    ///
    /// <para><b>[KORUNAN SAPMA]</b> §2.4 şeridin 1px dikey iç boşluklu olmasını ister; burada şerit satırın
    /// tam yüksekliğince uzanır (kullanıcı kararı — ayrımı satırın alt çizgisi yapar). Bkz. ProjectRow.xaml.</para>
    /// </summary>
    private void SetStripeFill(bool lighting = false)
    {
        var visual = _vm?.VisualStatus ?? VisualStatus.Discovered;
        string key = VisualStatuses.StripeBrushKey(visual);
        // Renk geçişinin TEK yolu (kopya YASAK): dalgada akar, diğer her yolda token referansına oturur.
        Controls.MotionTokens.TransitionTokenBrush(this, PART_Stripe, Shape.FillProperty, key,
            lighting && _motion.Enabled, Controls.MarkingChoreography.LightMs);

        // Soluktan tama geçiş: kural noktanınkiyle AYNI yerdedir (StartMode.ShouldCrossFade) — ikisi tek
        // hareketin parçasıdır ve ayrı ayrı karar veremezler.
        bool start = VisualStatuses.IsStartMode(visual);
        bool animate = Controls.StartMode.ShouldCrossFade(_stripeWasStartMode, start) && _motion.Enabled;
        _stripeWasStartMode = start;
        double target = start ? Controls.StartMode.FaintOpacity : 1.0;
        if (!animate)
        {
            PART_Stripe.BeginAnimation(OpacityProperty, null);
            PART_Stripe.Opacity = target;
            return;
        }
        var spline = Controls.MotionTokens.ResolveKeySpline(this, "KeySpline.EaseStandard", new KeySpline(0.4, 0, 0.2, 1));
        PART_Stripe.BeginAnimation(OpacityProperty,
            Controls.MotionTokens.SplineTo(target, TimeSpan.FromMilliseconds(Controls.StartMode.CrossFadeMs), spline),
            System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Şerit bir ÖNCEKİ çizimde başlangıç modunda mıydı — geçişin kapısı; <c>null</c> = bu veri için
    /// henüz çizilmedi (<see cref="Controls.StartMode.ShouldCrossFade"/>).</summary>
    private bool? _stripeWasStartMode;

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
    /// [design v1.11.0 §2.4-6 · §9-8] Uyarı slotu: statüden bağımsız, sabit 14px, TEK üçgen ve
    /// <b>HER ZAMAN AMBER</b>. Tooltip <b>TEK SATIRDIR</b> ve metni saf çekirdek üretir
    /// (<see cref="RowWarning.For"/>); döngü yolu, üye listesi ve gerekçe proje LOGUNDADIR.
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> Renk eskiden nedeni söylüyordu (yapısal döngü → turuncu, geçici dep-issue
    /// → amber) ve tooltip nedenleri alt alta diziyordu. v1.11.0 turuncuyu UI'dan çıkardı: iki uyarı tek
    /// amber üçgende birleşti ve ayrım tooltip'in TEK cümlesinde kaldı.</para>
    ///
    /// <para>Satır building iken slot GİZLİDİR — dönen spinner'la yarışmaz. Statü glyph'i bundan
    /// ETKİLENMEZ: o daima gerçek statüyü gösterir.</para>
    /// </summary>
    private void ApplyDep()
    {
        bool building = _vm?.IsCompiling ?? false;
        string? warn = building ? null : RowWarning.For(
            _vm?.InCycle ?? false, _vm?.CycleUnsettled ?? false, _vm?.CycleUnconverged ?? false,
            _vm?.DepIssues, _vm?.NamePrefix ?? "");

        PART_DepIcon.Visibility = warn is null ? Visibility.Collapsed : Visibility.Visible;
        PART_DepTip.Content = warn;
    }

    /// <summary>
    /// [design v1.16.0 §2.4] Sağ yuvanın metni: bir sonraki koşuda bu projeye NE OLACAĞI ve NEDEN.
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> Yuvada eskiden commit çifti (<c>a3f81c2 → b7e91d4</c>) dururdu. O çift
    /// kararı anlatmıyordu ve yanıltıyordu: sağ yarı kullanıcının PULL ETMEDİĞİ bir uzak commit'ti, sol yarı
    /// ise projeye değil REPOYA aitti — "commit aynı ama neden derlenecek?" sorusu tam da oradan doğuyordu.
    /// Motor kararı artık diskteki içerikten verdiği için satır da o kararı söyler.</para>
    ///
    /// <para>Sözcük seçimi <see cref="DecisionLabel"/>'de (saf, WPF'siz test edilir); burada yalnız iki Run'a
    /// yazılır ve renklendirilir: asıl sözcük derlenecek satırda <c>text-secondary</c>, güncel satırda
    /// <c>text-faint</c>; "·" sonrası kuyruk HER ZAMAN faint — asıl sözcük önde okunsun diye.</para>
    /// </summary>
    private void ApplyDecision()
    {
        var decision = _vm is null
            ? RowDecision.None
            : DecisionLabel.For(_vm.WillBuild, _vm.WillBuildReason, _vm.OwnFilesChanged, _vm.LastBuiltAt, DateTimeOffset.Now);

        PART_DecisionWord.Text = decision.Word;
        PART_DecisionTail.Text = decision.Tail is null ? "" : " · " + decision.Tail;
        PART_Decision.ToolTip = decision.IsEmpty ? null : decision.Title;
        PART_DecisionWord.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,
            decision.Stale ? "Brush.TextSecondary" : "Brush.TextFaint");
    }

    /// <summary>Sağ blok: hover'da aç-ikonları, değilse karar etiketi (design v1.16.0 §2.4).
    /// [L1] İkon bloğu hover'da TALEP ÜZERİNE kurulur; hover yokken kurulmamışsa dokunulacak bir şey de yoktur.</summary>
    private void ApplyRightBlock()
    {
        // [design v1.11.0 §9-6] Menü AÇIKKEN ikonlar görünür kalır: menü satırın çapasına bağlıdır ve
        // çapa kaybolursa menü havada asılı kalırdı (prototipte de `hover || menuOpen`).
        bool menuOpen = _actions?.MoreButton.IsChecked == true;
        // [design §3.8] Koşunun HEDEFİ olan satırda Stop hover OLMADAN da görünür (prototip `hover || isTarget || menuOpen`).
        bool target = _vm?.IsRunTarget == true;
        bool showIcons = _hover || menuOpen || target;
        bool showDecision = !showIcons; // [design v1.7.0 §2.4] Etiket her satırda — yalnız hover ikonları onu örter
        if (showIcons) EnsureActions().HoverIcons.Visibility = Visibility.Visible;
        else if (_actions is { } hidden) hidden.HoverIcons.Visibility = Visibility.Collapsed;
        // Kurulmuş blok gizliyken de tazelenir: hedef bırakıldığında play, Stop'un yerine geri dönmüş olmalı —
        // bir sonraki hover'da satır Stop göstermemeli.
        if (_actions is { } actions) ApplyActionState(actions);
        PART_Decision.Visibility = showDecision ? Visibility.Visible : Visibility.Collapsed;
        if (showDecision) ApplyDecision();
    }

    /// <summary>[tek proje · design §3.8] Hover bloğunun koşuya bağlı hâli: play'in hedefi (satırın kimliği —
    /// geri dönüştürülen container yeni VM'inin kimliğini alır), play/Stop yuvası (hedef satırda Stop) ve
    /// play'in tooltip'i (kilitliyken <see cref="AccessibilityNames.BuildBusyTooltip"/>). Pasiflik burada
    /// YAZILMAZ: komutun CanExecute'u düğmeyi zaten kapatır.</summary>
    private void ApplyActionState(ProjectRowActions actions)
    {
        bool target = _vm?.IsRunTarget == true;
        actions.BuildButton.CommandParameter = _vm?.Id;
        actions.BuildButton.Visibility = target ? Visibility.Collapsed : Visibility.Visible;
        actions.StopButton.Visibility = target ? Visibility.Visible : Visibility.Collapsed;
        actions.BuildButton.ToolTip = _vm?.IsRunLocked == true
            ? AccessibilityNames.BuildBusyTooltip
            : AccessibilityNames.BuildThisProject;
    }

    /// <summary>Satırın projesi ŞU AN satırdan derlenebilir mi — menünün Build/Rebuild kapısı, play ile AYNI
    /// komuttan okunur (ikinci bir kural yazılmaz).</summary>
    private bool CanRunProject() =>
        _vm is { } vm && FindRunViewModel()?.BuildProjectCommand.CanExecute(vm.Id) == true;

    /// <summary>[design §9-6] Menü maddesi seçildi: menü kapanır (BuildMenu deseni), komut satırın projesiyle
    /// çalışır. Satırdan tetiklemek satıra tıklamak DEĞİLDİR — seçim burada değişmez (komut kendi kuralıyla
    /// seçimi ve filtreyi düşürür).</summary>
    private void OnRowMenuItem(string kind)
    {
        if (_actions is { } actions) actions.MoreButton.IsChecked = false;
        if (_vm is not { } vm || FindRunViewModel() is not { } run) return;
        System.Windows.Input.ICommand? command = kind switch
        {
            "build" => run.BuildProjectCommand,
            "rebuild" => run.RebuildProjectCommand,
            "clean" => run.CleanProjectCommand,
            _ => null,
        };
        if (command is not null && command.CanExecute(vm.Id)) command.Execute(vm.Id);
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

    // [design v1.11.0 §2.4-5 · §9-13] Statü glyph'inin TOOLTIP'i KALDIRILDI. Eski hâlinde glyph, statü
    // etiketine ek olarak canlı süreyi ve döngü/dep gerekçelerini de söylüyordu — üçü de satırda ZATEN vardı
    // (süre kolonu, uyarı üçgeni). Ekran okuyucu için statü metni glyph'in UIA adına yazılır
    // (ApplyStatusVisuals); listede tooltip taşıyan TEK öğe uyarı üçgenidir.

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

    /// <summary>
    /// [design v1.11.0 §9-4 · §2.4] Açılış koreografisinin satır payı: satırlar graf node'larıyla SENKRON
    /// söner. Hedef ve süre satır VM'inden gelir (<see cref="ProjectRowViewModel.Fade"/>) — karar
    /// <see cref="MarkingChoreography"/>'de, burada YALNIZ uygulanır.
    ///
    /// <para><b>Devri <c>HandoffBehavior.SnapshotAndReplace</c> yapar</b> — animasyon ÖNCEDEN SÖKÜLMEZ.
    /// <see cref="PlayReveal"/> opaklığı <c>HoldEnd</c> ile tutar ama TABAN değeri <b>0</b>'dır (beliriş
    /// oradan başlar); animasyonu sökmek opaklığı o tabana düşürür ve yeni solma sıfırdan başlar.
    /// <b>Ölçülen kusur:</b> koreografi yedi adımdır ve her adım <c>Fade</c>'i yeniden yazar, yani satır
    /// koşu başlarken yedi kez bir an kaybolup geri geliyordu (kullanıcı: "proje listesinde bazı satırlarda
    /// yanıp sönmeler"). Snapshot uçuştaki (ya da tutulan) değeri alır ve oradan hedefe gider — CSS'in
    /// <c>transition</c> davranışının ta kendisi.</para>
    /// </summary>
    private void ApplyFade()
    {
        var fade = _vm?.Fade ?? RowFade.None;

        if (!AnimationsEnabledProvider())
        {
            // Reduced-motion: değer YEREL yazılır, o yüzden uçuştaki saat burada sökülmelidir.
            PART_Root.BeginAnimation(OpacityProperty, null);
            PART_Root.Opacity = fade.Opacity;
            return;
        }

        var spline = MotionTokens.ResolveKeySpline(this, "KeySpline.EaseInOut", new KeySpline(0.65, 0, 0.35, 1));
        PART_Root.BeginAnimation(OpacityProperty,
            MotionTokens.SplineTo(fade.Opacity, TimeSpan.FromMilliseconds(fade.DurationMs), spline),
            HandoffBehavior.SnapshotAndReplace);
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

    /// <summary>
    /// Satıra tıklamak projeyi seçer — <b>ama satırın KENDİ eylem bloğundan gelen tık bir satır tıklaması
    /// değildir</b> (ikonlar ve onların popup'ları: ⋯ menüsü, VS seçici).
    ///
    /// <para><b>Neden bir kapı gerekiyor (ölçüldü).</b> Bir <see cref="System.Windows.Controls.Primitives.Popup"/>
    /// içindeki fare olayının yolu <c>PopupRoot</c>'tan Popup'ın MANTIKSAL ebeveynine — yani bu satıra — devam
    /// eder, ve <see cref="UIElement.MouseLeftButtonUpEvent"/> <b>Direct</b> bir olaydır: girdi sistemi onu yol
    /// üstündeki HER öğede ayrıca yükseltir. Menüden <i>Build</i> seçmek bu yüzden satırı da seçiyordu ve
    /// koşu seçimi düşürdükten hemen SONRA satır yeniden seçildiği için graf fit görünüme dönmek yerine o
    /// düğüme odaklanıyor, konsol da koşu anlatısı yerine proje loguna geçiyordu. İkon <i>düğmeleri</i> bunu
    /// kendi <c>Handled</c>'larıyla zaten kesiyordu; menü satırları düz <see cref="Border"/>'dır ve kesmiyordu.</para>
    ///
    /// <para>Kapı kaynağa bakar, tekil öğelere değil: eylem bloğunun İÇİNDEN doğan her tık dışarıda kalır
    /// (ikonlar arasındaki boşluk dahil — orası da satırın gövdesi değil, eylem bloğudur).</para>
    /// </summary>
    private void OnRowClicked(object sender, MouseButtonEventArgs e)
    {
        if (IsFromRowActions(e.OriginalSource as DependencyObject ?? e.Source as DependencyObject)) return;
        if (_vm is { } vm) FindRunViewModel()?.SelectProject(vm.Id);
    }

    /// <summary>Kaynak, satırın eylem bloğunun içinde mi. Yürüyüş MANTIKSAL ebeveyni önceler: popup'ın
    /// çocuğundan çıkışın TEK yolu odur (görsel ebeveyn <c>PopupRoot</c>'ta biter); şablon içi parçalar için
    /// görsel ebeveyne düşer.</summary>
    private bool IsFromRowActions(DependencyObject? source)
    {
        if (_actions is not { } actions) return false;
        for (var node = source; node is not null; node = ParentOf(node))
            if (ReferenceEquals(node, actions)) return true;
        return false;

        static DependencyObject? ParentOf(DependencyObject node) =>
            LogicalTreeHelper.GetParent(node) ?? (node is Visual visual ? VisualTreeHelper.GetParent(visual) : null);
    }

    /// <summary>[design v1.11.0 §9-6] Sağ tık satır menüsünü açar. Hover bloğu talep üzerine kurulduğu için
    /// (L1) önce o kurulur ve GÖRÜNÜR yapılır — menü kapandığında hover kuralı onu yeniden gizler.</summary>
    private void OnRowRightClicked(object sender, MouseButtonEventArgs e)
    {
        var actions = EnsureActions();
        actions.MoreButton.IsChecked = true; // Checked kablajı başlığı yazar ve sağ bloğu açar
        e.Handled = true; // satır seçimi tetiklenmesin — sağ tık bir SEÇİM jesti değildir
    }

    /// <summary>
    /// Satır menüsünün yerleşimi. Karar <see cref="RowMenuPlacement"/>'tadır; burada yalnız WPF'in ölçüleri
    /// ona verilir ve sonuç çapaya (satırın kökü) göre offset'e çevrilir.
    ///
    /// <para>Dikey kelepçe için listenin GÖRÜNÜR alanı gerekir; üstteki <see cref="ScrollViewer"/> bulunamazsa
    /// (tek başına realize edilmiş bir satır) kelepçe atlanır ve menü satırın altına oturur — prototipin
    /// <c>cont</c> yokken yaptığının aynısı (BuildApp.jsx:660-665).</para>
    /// </summary>
    private CustomPopupPlacement[] PlaceRowMenu(Size popupSize, Size targetSize, Point offset)
    {
        double x = RowMenuPlacement.LeftInRow(targetSize.Width, popupSize.Width);
        double y = targetSize.Height - RowMenuPlacement.RowOverlap;

        if (FindScrollViewer() is { } viewport && PART_Root.IsDescendantOf(viewport))
        {
            double rowTop = PART_Root.TransformToAncestor(viewport).Transform(default).Y;
            y = RowMenuPlacement.TopInViewport(rowTop, targetSize.Height, popupSize.Height, viewport.ViewportHeight)
                - rowTop;
        }
        return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.None)];
    }

    private ScrollViewer? FindScrollViewer()
    {
        for (DependencyObject? d = this; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is ScrollViewer scroll) return scroll;
        return null;
    }

    /// <summary>Menü başlığındaki kısa ad — önek satır VM'inden gelir (D5, tek otorite).</summary>
    private string ShortName() =>
        _vm is { } vm ? GraphNode.ShortLabel(vm.Name, vm.NamePrefix) : "";

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
