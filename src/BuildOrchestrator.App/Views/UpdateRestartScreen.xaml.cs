using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [design v1.23.0 §2.12 · §9 "Restart ekranı" · plan U4] <c>Restart to update</c>'in ekranı: pencere kapanıp yeni
/// sürümle açılana dek gösterilen kısa ekran (prototip <c>UpdateRestart</c>, BuildApp.jsx:1745-1786). Adımlar ve
/// yüzdeler <see cref="UpdateRestartTimeline"/>'dan, metinler <see cref="UpdateText"/>'ten gelir.
///
/// <para><b>Güncelleme motoru henüz YOK.</b> Ekran tasarımın önizlemesidir: üç adımı oynatır, bitişten 120ms sonra
/// söner ve kalkar; arkasındaki uygulama hiç değişmemiştir (Sync yok, sıfırlama yok, hap yerinde — plan U4). Motor
/// yazıldığında kurulumun kendisi bu ekranın arkasında koşar.</para>
///
/// <para><b>Tek zaman dikişi (D8):</b> bir kare zamanlayıcısı (<see cref="Timer"/>, <see cref="FrameMs"/> aralıkla) ve
/// bir saat (<see cref="NowMs"/>). Her karede saat okunur ve çizelgenin o anı yazılır — prototipin rAF +
/// <c>Date.now()</c> döngüsünün karşılığı. Uyku/bekleme yoktur; testler sahte zamanlayıcıyla kare atar.</para>
///
/// <para><b>Hareket:</b> giriş <c>Duration.Base</c> sönümü (<see cref="PopIn.PlayFadeIn"/>, ortak giriş gövdesi), çıkış
/// <c>Duration.Slow</c> sönümü (ease-out); ikisi de azaltılmış harekette anında son duruma atlar. İlerleme çubuğu
/// süs değil bilgidir, hareket ayarından bağımsız ilerler. Sönerken ekran tıklamaları geçirir
/// (prototip <c>pointerEvents: 'none'</c>).</para>
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

    /// <summary>Etiketlerin andığı gelen sürüm.</summary>
    private string _incoming = "";

    /// <summary>Son yazılan (ve duyurulan) adım — etiket yalnız adım değişince yazılır.</summary>
    private UpdateRestartStep? _shownStep;

    /// <summary>Sönüşün sahibi — yeni bir oynatma, uçuştaki eski sönüşün bitişini geçersiz kılar.</summary>
    private int _generation;

    /// <summary>Ekran görünür mü (sönüş dahil) — pencere klavyeyi bu sürece yok sayar.</summary>
    public bool IsShowing => Visibility == Visibility.Visible;

    /// <summary>[test yüzeyi] Adım etiketinin canlı bölge duyurusu kaç kez yükseldi — peer'in olayı dinleyicisiz
    /// gözlemlenemez.</summary>
    internal int StepAnnouncements { get; private set; }

    /// <summary>Çıkış sönümünün süresi — <c>Duration.Slow</c> token'ından taze çözülür (tasarım 280ms; reduced-motion
    /// token'ı sıfırlar).</summary>
    internal static TimeSpan FadeOutDuration(FrameworkElement element) => MotionTokens.ResolveSlow(element).TimeSpan;

    /// <summary>Ekranı açar ve çizelgeyi baştan oynatır: kurulu → gelen sürüm, ilk adım, boş çubuk. Ekran zaten
    /// görünürken gelen istek yok sayılır — oynayan çizelge baştan başlamaz.</summary>
    public void Play(string installed, string incoming)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(incoming);
        if (IsShowing) return;

        _generation++;
        _incoming = incoming;
        _shownStep = null;
        PART_Installed.Text = installed;
        PART_Incoming.Text = incoming;
        IsHitTestVisible = true;
        Visibility = Visibility.Visible;
        _startedAtMs = NowMs();
        Render(0);
        PopIn.PlayFadeIn(this);
        Timer.Start(TimeSpan.FromMilliseconds(FrameMs), OnFrame);
    }

    /// <summary>Bir kare: saati oku, çizelgenin o anını yaz; sönüş anı geldiyse çık.</summary>
    private void OnFrame()
    {
        double elapsedMs = NowMs() - _startedAtMs;
        Render(elapsedMs);
        if (elapsedMs >= UpdateRestartTimeline.FadeOutAtMs) Leave();
    }

    /// <summary>Çubuğu her karede, etiketi yalnız adım değişince yazar; yeni adım ekran okuyucuya BİR KEZ duyurulur
    /// (<c>StickyRibbon.AnnouncePhaseIfChanged</c> deseni).</summary>
    private void Render(double elapsedMs)
    {
        var frame = UpdateRestartTimeline.At(elapsedMs);
        PART_Progress.Value = frame.Percent;
        if (_shownStep == frame.Step) return;
        _shownStep = frame.Step;
        PART_Step.Text = UpdateText.RestartStepLabel(frame.Step, _incoming);
        StepAnnouncements++;
        var peer = UIElementAutomationPeer.FromElement(PART_Step) ?? UIElementAutomationPeer.CreatePeerForElement(PART_Step);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    /// <summary>Zamanlayıcıyı durdurur ve söner; sönüş bitince (azaltılmış harekette hemen) kalkar. Sönerken tıklamalar
    /// arkadaki uygulamaya geçer, klavye ise ekran kalkana dek yok sayılmaya devam eder.
    /// <para><b>Sönüş görünen değerden başlar:</b> giriş (<see cref="PopIn.PlayFadeIn"/>) yerel tabanı 0 yazar ve görünen
    /// 1'i yalnız son değerini tutan animasyonu taşır. O animasyon silinince opaklık tabana (0) düşer ve başlangıcı
    /// olmayan sönüm 0 → 0 oynardı — ekran tek karede kaybolurdu (ölçüldü). Bu yüzden silmeden önce okunan görünür değer
    /// taban olarak geri yazılır, sönüm oradan başlar.</para></summary>
    private void Leave()
    {
        Timer.Stop();
        IsHitTestVisible = false;
        double visible = Opacity;
        BeginAnimation(OpacityProperty, null);
        if (!MotionGate.StaticAnimationsEnabled) { Close(); return; } // [W2 fix-1] statik sinyalin TEK kapısı

        Opacity = visible;
        int generation = _generation;
        var fade = MotionTokens.SplineTo(0.0, FadeOutDuration(this), MotionTokens.ResolveEaseOut(this));
        fade.Completed += (_, _) =>
        {
            if (generation == _generation) Close();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Ekranı kaldırır ve bir sonraki oynatma için opaklığı ve tıklanabilirliği geri koyar.</summary>
    private void Close()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 1.0;
        IsHitTestVisible = true;
        Visibility = Visibility.Collapsed;
    }
}
