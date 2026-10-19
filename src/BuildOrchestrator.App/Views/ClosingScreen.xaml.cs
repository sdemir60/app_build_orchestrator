using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [design v1.23.0 §2.12 · §9 "Restart ekranı" · plan U4 · Exit] Pencerenin <b>kapanış ekranı</b>: pencere kapanana dek gösterilen kısa
/// ekran. İki kipi vardır ve ikisi aynı katmanı, aynı 232px'lik kolonu, aynı kare döngüsünü paylaşır — aynı anda yalnız biri oynar:
/// <list type="bullet">
/// <item><b>Restart</b> (<see cref="PlayRestart"/>, prototip <c>UpdateRestart</c>, BuildApp.jsx:1745-1786): <c>Restart to update</c>'ten
/// sonra. Orta satır kurulu → gelen sürüm geçişidir; tek adımı (<c>Closing &lt;ürün&gt;…</c>) ve süresi
/// <see cref="UpdateRestartTimeline"/>'dandır, metinler <see cref="UpdateText"/>'ten. Çubuk dolunca kabuk uygulamayı tam çıkış yoluna sokar
/// (<c>MainWindow.UpdateRestart.cs</c>); pencere kapanınca kurulumu Update.exe penceresiz yapar.</item>
/// <item><b>Exit</b> (<see cref="PlayExit"/>): onay kartının "Stop and exit"inden sonra. Orta satır işin adı ve ilerlemesidir (mono
/// <c>text-dim</c>, "Build · 14/38"); iki adımı ("Stopping build…", "Ending 5 processes…") ve süreleri <see cref="ExitClosingTimeline"/>'dandır,
/// metinler <see cref="ExitText"/>'ten. İkinci adım ancak kabuk çıkışın hazır olduğunu bildirince başlar (<see cref="MarkStopped"/> — VM'in
/// <c>ExitReady</c>'si); çubuk dolunca kabuk uygulamayı kapatır (<c>MainWindow.Exit.cs</c>).</item>
/// </list>
///
/// <para><b>[motor · Task 11 · K6] Ekran pencere kapanana dek kalır; sönüş yoktur.</b> Çubuk dolunca zamanlayıcı durur ve ekran bunu
/// BİR kez bildirir (<see cref="BarFilled"/>); ekranı kaldıran şey kendi hareketi değil, pencerenin kapanışıdır. Kapanış ancak o anda
/// istenir: ekranla aynı turda istenseydi Shutdown dispatcher'da Normal öncelikle koşar, ekranın ilk çizimi (Render önceliği) ondan sonra
/// gelirdi — pencere ekran hiç görünmeden kapanırdı.</para>
///
/// <para><b>Tek zaman dikişi (D8):</b> bir kare zamanlayıcısı (<see cref="Timer"/>, <see cref="FrameMs"/> aralıkla) ve
/// bir saat (<see cref="NowMs"/>). Her karede saat okunur ve kipin çizelgesinin o anı yazılır — prototipin rAF +
/// <c>Date.now()</c> döngüsünün karşılığı. Uyku/bekleme yoktur; testler sahte zamanlayıcıyla kare atar.</para>
///
/// <para><b>Hareket:</b> giriş <c>Duration.Base</c> sönümü (<see cref="PopIn.PlayFadeIn"/>, ortak giriş gövdesi);
/// azaltılmış harekette anında son duruma atlar. Çıkış hareketi yoktur. İlerleme çubuğu süs değil bilgidir, hareket
/// ayarından bağımsız ilerler.</para>
///
/// <para><b>Klavye:</b> ekran görünürken (<see cref="IsShowing"/>) pencere klavyeyi ve global kısayolları yok sayar —
/// kararı pencere verir (<c>MainWindow.UpdateRestart.cs</c>), bu kontrol yalnız durumunu söyler.</para>
/// </summary>
public partial class ClosingScreen : UserControl
{
    /// <summary>UIA rolü — gerekçe ve ölçüm <see cref="UserControlRolePeer"/>'de.</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new UserControlRolePeer(this, AutomationControlType.Pane);

    /// <summary>Kare aralığı (ms) — prototipin <c>requestAnimationFrame</c> döngüsünün ~60 Hz'i. Çubuğun adım içindeki
    /// doğrusal ilerlemesi her karede yazılır.</summary>
    internal const double FrameMs = 16;

    /// <summary>Kipin çizelgesinin bir anı: adım etiketi, çubuğun yüzdesi ve çizelgenin bitip bitmediği.</summary>
    private readonly record struct Frame(string Step, double Percent, bool Done);

    public ClosingScreen()
    {
        InitializeComponent();
        _timeline = RestartFrame;
    }

    private IPollTimer? _timer;

    /// <summary>Kare zamanlayıcısı — üretimde UI thread'inde tık atan <see cref="DispatcherPollTimer"/> (ilk
    /// kullanımda kurulur), testte elle tıklatılan sahtesi.</summary>
    internal IPollTimer Timer
    {
        get => _timer ??= new DispatcherPollTimer(Dispatcher);
        set => _timer = value;
    }

    /// <summary>[test dikişi] Monoton saat (ms).</summary>
    internal Func<long> NowMs { get; set; } = () => Environment.TickCount64;

    /// <summary>Oynatmanın başladığı an (<see cref="NowMs"/>).</summary>
    private long _startedAtMs;

    /// <summary>Oynayan kipin çizelgesi — oynatmanın başından geçen süre → kare.</summary>
    private Func<double, Frame> _timeline;

    /// <summary>Son yazılan (ve duyurulan) adım etiketi — etiket yalnız adım değişince yazılır.</summary>
    private string? _shownStep;

    /// <summary>[Exit] Exit kipinin iki adım etiketi (<see cref="ExitText.StoppingStep"/>, <see cref="ExitText.EndingStep"/>).</summary>
    private string _stoppingStep = "", _endingStep = "";

    /// <summary>[Exit] Çıkışın hazır olduğu an — oynatmanın başından ms; henüz değilse <c>null</c> (<see cref="MarkStopped"/>).</summary>
    private double? _stoppedAtMs;

    /// <summary>[motor · Task 11 · fix-1] Çizelge bitti, çubuk doldu — oynatma başına BİR kez, zamanlayıcı durduğu karede. Kabuk bu anda
    /// kapanışı ister: restart kipinde tam çıkış yolunu (<c>MainWindow.UpdateRestart.cs</c>), exit kipinde kapanışın kendisini
    /// (<c>MainWindow.Exit.cs</c>).</summary>
    public event Action? BarFilled;

    /// <summary>Ekran görünür mü — pencere klavyeyi bu sürece yok sayar.</summary>
    public bool IsShowing => Visibility == Visibility.Visible;

    /// <summary>[Exit] Oynayan kip Exit'inki mi — kabuk <c>ExitReady</c>'yi ve <see cref="BarFilled"/>'ı buna göre yönlendirir.</summary>
    public bool IsPlayingExit { get; private set; }

    /// <summary>[test yüzeyi] Adım etiketinin canlı bölge duyurusu kaç kez yükseldi — peer'in olayı dinleyicisiz
    /// gözlemlenemez.</summary>
    internal int StepAnnouncements { get; private set; }

    /// <summary>Restart kipini açar ve çizelgeyi baştan oynatır: kurulu → gelen sürüm, ilk adım, boş çubuk. Ekran zaten
    /// görünürken gelen istek yok sayılır — oynayan (ya da dolmuş) çizelge baştan başlamaz.</summary>
    public void PlayRestart(string installed, string incoming)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(incoming);
        if (IsShowing) return;

        IsPlayingExit = false;
        PART_Installed.Text = installed;
        PART_Incoming.Text = incoming;
        PART_Transition.Visibility = Visibility.Visible;
        PART_Context.Visibility = Visibility.Collapsed;
        Begin(UpdateText.RestartHeading, RestartFrame);
    }

    /// <summary>[Exit] Exit kipini açar ve çizelgeyi baştan oynatır: işin bağlamı (<paramref name="context"/>), ilk adım
    /// (<paramref name="stoppingStep"/>), boş çubuk. İkinci adımın etiketi (<paramref name="endingStep"/>) kartın saydığı süreçleri söyler.
    /// Ekran zaten görünürken gelen istek yok sayılır.</summary>
    public void PlayExit(string stoppingStep, string context, string endingStep)
    {
        ArgumentNullException.ThrowIfNull(stoppingStep);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(endingStep);
        if (IsShowing) return;

        IsPlayingExit = true;
        _stoppingStep = stoppingStep;
        _endingStep = endingStep;
        _stoppedAtMs = null;
        PART_Context.Text = context;
        PART_Context.Visibility = Visibility.Visible;
        PART_Transition.Visibility = Visibility.Collapsed;
        Begin(ExitText.ClosingHeading, ExitFrame);
    }

    /// <summary>[Exit] Çıkış hazır (VM'in <c>ExitReady</c>'si): ikinci adım ilk adımın en kısa süresi dolunca başlar. Bir kez geçerlidir;
    /// restart kipinde hiçbir şey yapmaz (restart'ın çizelgesi sabittir).</summary>
    public void MarkStopped()
    {
        if (!IsPlayingExit || _stoppedAtMs is not null) return;
        _stoppedAtMs = NowMs() - _startedAtMs;
    }

    /// <summary>İki kipin ortak açılışı: başlık (görünür metin ve ekran okuyucu adı), kipin çizelgesi, ilk kare, giriş ve kare döngüsü.</summary>
    private void Begin(string heading, Func<double, Frame> timeline)
    {
        _timeline = timeline;
        _shownStep = null;
        PART_Heading.Text = heading;
        AutomationProperties.SetName(this, heading);
        Visibility = Visibility.Visible;
        _startedAtMs = NowMs();
        Render(0);
        PopIn.PlayFadeIn(this);
        Timer.Start(TimeSpan.FromMilliseconds(FrameMs), OnFrame);
    }

    /// <summary>Restart kipinin çizelgesi — tek adım, sabit süre.</summary>
    private static Frame RestartFrame(double elapsedMs)
    {
        var frame = UpdateRestartTimeline.At(elapsedMs);
        return new(UpdateText.RestartStepLabel(frame.Step), frame.Percent, elapsedMs >= UpdateRestartTimeline.TotalMs);
    }

    /// <summary>[Exit] Exit kipinin çizelgesi — iki adım; ikincisi çıkış hazır olunca (<see cref="_stoppedAtMs"/>).</summary>
    private Frame ExitFrame(double elapsedMs)
    {
        var frame = ExitClosingTimeline.At(elapsedMs, _stoppedAtMs);
        return new(frame.Step == ExitClosingStep.Stopping ? _stoppingStep : _endingStep, frame.Percent, frame.Done);
    }

    /// <summary>Bir kare: saati oku, çizelgenin o anını yaz; çizelge bittiyse zamanlayıcıyı durdur ve bunu bildir
    /// (<see cref="BarFilled"/>) — ekran kalır.</summary>
    private void OnFrame()
    {
        if (!Render(NowMs() - _startedAtMs).Done) return;
        Timer.Stop();
        BarFilled?.Invoke();
    }

    /// <summary>Çubuğu her karede, etiketi yalnız adım değişince yazar; yeni adım ekran okuyucuya BİR KEZ duyurulur
    /// (<see cref="LiveRegion.Announce"/>).</summary>
    private Frame Render(double elapsedMs)
    {
        var frame = _timeline(elapsedMs);
        PART_Progress.Value = frame.Percent;
        if (string.Equals(_shownStep, frame.Step, StringComparison.Ordinal)) return frame;
        _shownStep = frame.Step;
        PART_Step.Text = frame.Step;
        StepAnnouncements++;
        LiveRegion.Announce(PART_Step);
        return frame;
    }
}
