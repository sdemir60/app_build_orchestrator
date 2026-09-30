using System.Windows;
using System.Windows.Controls;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [design v1.23.0 §2.12 · §9 "Restart ekranı" · plan U4] <c>Restart to update</c>'in ekranı: pencere kapanana dek
/// gösterilen kısa ekran (prototip <c>UpdateRestart</c>, BuildApp.jsx:1745-1786). Adım ve yüzde
/// <see cref="UpdateRestartTimeline"/>'dan, metinler <see cref="UpdateText"/>'ten gelir.
///
/// <para><b>[motor · Task 11 · K6] Ekran pencere kapanana dek kalır; sönüş yoktur.</b> Tek adımı (<c>Closing
/// &lt;ürün&gt;…</c>) gösterir; çubuk dolunca zamanlayıcı durur ve ekran bunu bildirir (<see cref="BarFilled"/>). Kabuk
/// o anda uygulamayı güvenli tam çıkış yoluna sokar (<c>MainWindow.UpdateRestart.cs</c>); pencere kapanınca kurulumu
/// Update.exe penceresiz yapar ve yeni sürüm normal açılır. Ekranı kaldıran şey kendi hareketi değil, pencerenin
/// kapanışıdır.</para>
///
/// <para><b>Tek zaman dikişi (D8):</b> bir kare zamanlayıcısı (<see cref="Timer"/>, <see cref="FrameMs"/> aralıkla) ve
/// bir saat (<see cref="NowMs"/>). Her karede saat okunur ve çizelgenin o anı yazılır — prototipin rAF +
/// <c>Date.now()</c> döngüsünün karşılığı. Uyku/bekleme yoktur; testler sahte zamanlayıcıyla kare atar.</para>
///
/// <para><b>Hareket:</b> giriş <c>Duration.Base</c> sönümü (<see cref="PopIn.PlayFadeIn"/>, ortak giriş gövdesi);
/// azaltılmış harekette anında son duruma atlar. Çıkış hareketi yoktur. İlerleme çubuğu süs değil bilgidir, hareket
/// ayarından bağımsız ilerler.</para>
///
/// <para><b>Klavye:</b> ekran görünürken (<see cref="IsShowing"/>) pencere klavyeyi ve global kısayolları yok sayar —
/// kararı pencere verir (<c>MainWindow.UpdateRestart.cs</c>), bu kontrol yalnız durumunu söyler.</para>
/// </summary>
public partial class UpdateRestartScreen : UserControl
{
    /// <summary>Kare aralığı (ms) — prototipin <c>requestAnimationFrame</c> döngüsünün ~60 Hz'i. Çubuğun adım içindeki
    /// doğrusal ilerlemesi her karede yazılır.</summary>
    internal const double FrameMs = 16;

    public UpdateRestartScreen() => InitializeComponent();

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

    /// <summary>Son yazılan (ve duyurulan) adım — etiket yalnız adım değişince yazılır.</summary>
    private UpdateRestartStep? _shownStep;

    /// <summary>[motor · Task 11 · fix-1] Çubuk doldu (<see cref="UpdateRestartTimeline.TotalMs"/>) — oynatma başına BİR
    /// kez, zamanlayıcı durduğu karede. Kabuk bu anda güvenli tam çıkışı ister (<c>MainWindow.UpdateRestart.cs</c>).</summary>
    public event Action? BarFilled;

    /// <summary>Ekran görünür mü — pencere klavyeyi bu sürece yok sayar.</summary>
    public bool IsShowing => Visibility == Visibility.Visible;

    /// <summary>[test yüzeyi] Adım etiketinin canlı bölge duyurusu kaç kez yükseldi — peer'in olayı dinleyicisiz
    /// gözlemlenemez.</summary>
    internal int StepAnnouncements { get; private set; }

    /// <summary>Ekranı açar ve çizelgeyi baştan oynatır: kurulu → gelen sürüm, ilk adım, boş çubuk. Ekran zaten
    /// görünürken gelen istek yok sayılır — oynayan (ya da dolmuş) çizelge baştan başlamaz.</summary>
    public void Play(string installed, string incoming)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(incoming);
        if (IsShowing) return;

        _shownStep = null;
        PART_Installed.Text = installed;
        PART_Incoming.Text = incoming;
        Visibility = Visibility.Visible;
        _startedAtMs = NowMs();
        Render(0);
        PopIn.PlayFadeIn(this);
        Timer.Start(TimeSpan.FromMilliseconds(FrameMs), OnFrame);
    }

    /// <summary>Bir kare: saati oku, çizelgenin o anını yaz; çubuk dolduysa zamanlayıcıyı durdur ve bunu bildir
    /// (<see cref="BarFilled"/>) — ekran kalır.</summary>
    private void OnFrame()
    {
        double elapsedMs = NowMs() - _startedAtMs;
        Render(elapsedMs);
        if (elapsedMs < UpdateRestartTimeline.TotalMs) return;
        Timer.Stop();
        BarFilled?.Invoke();
    }

    /// <summary>Çubuğu her karede, etiketi yalnız adım değişince yazar; yeni adım ekran okuyucuya BİR KEZ duyurulur
    /// (<see cref="LiveRegion.Announce"/>).</summary>
    private void Render(double elapsedMs)
    {
        var frame = UpdateRestartTimeline.At(elapsedMs);
        PART_Progress.Value = frame.Percent;
        if (_shownStep == frame.Step) return;
        _shownStep = frame.Step;
        PART_Step.Text = UpdateText.RestartStepLabel(frame.Step);
        StepAnnouncements++;
        LiveRegion.Announce(PART_Step);
    }
}
