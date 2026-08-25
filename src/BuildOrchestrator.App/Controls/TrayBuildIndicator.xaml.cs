using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [tray indicator/T3] Tepside koşan derlemeyi gösteren animasyonlu marka işareti — beyaz şeridinde canlı
/// proje sayacı taşır. Çizimi ve zaman çizelgesi <c>TrayBuildIndicator.xaml</c>'dedir (kaynak sanat, verbatim);
/// burada yalnız döngünün SÜRÜŞÜ vardır.
///
/// <para><b>Döngü sonsuz DEĞİLDİR</b> (K-10). Storyboard tek geçiş koşar; her bitişte bu sınıf "devam mı,
/// bitiş mi" sorusunu sorar. Sebebi tek bir gereksinimdir: koşu bittiğinde gösterge YARIDA KESİLMEMELİ, içinde
/// bulunduğu turun çıkış evresini tamamlamalıdır — parçalar sağa süzülür, son şerit yok olur, overlay tam o
/// karede kapanır. Sonsuz bir storyboard'un böyle bir sınırı yoktur; onu durdurmanın tek yolu yarıda kesmek
/// (ya da seek/hız oyunları) olurdu. Bu yüzden <see cref="RequestFinish"/> yalnız bir BAYRAKTIR.</para>
///
/// <para><b>Kod tarafında TEK bir ms literali yoktur.</b> Bekleme ve koşullar storyboard'un <c>Completed</c>
/// olayından akar; sayacın yumuşak takası süresini <c>Duration.Fast</c> token'ından, animasyon BAŞLANGICINDA
/// taze okur (<see cref="MotionTokens.ResolveFast"/> — DS'in tüm 120 ms geçişleriyle aynı kapı).</para>
/// </summary>
public partial class TrayBuildIndicator : UserControl
{
    private readonly Storyboard _loop;

    private bool _running;
    private bool _finishing;
    private Action? _onFinished;

    private int _done = -1;
    private int _total = -1;
    private bool _hasCounter;
    private string? _pendingCounterText;

    public TrayBuildIndicator()
    {
        InitializeComponent();

        _loop = (Storyboard)Resources["TrayLoop"];
        // Dekoratif animasyon: tam kare hızı harcanmaz. Değer paylaşılan sabitten gelir — XAML'e ikinci kez
        // yazılsaydı sayı iki yerde yaşardı.
        Timeline.SetDesiredFrameRate(_loop, MotionTokens.DecorativeFrameRate);
        _loop.Completed += OnIterationCompleted;

        // [§14.5] Görünmeyen bir gösterge saat döndürmez. Overlay penceresi gizlendiğinde (ya da kontrol
        // ağaçtan düştüğünde) döngü sökülür — aksi halde tepsideki koşu bittikten sonra bile arka planda
        // dönmeye devam ederdi.
        IsVisibleChanged += (_, _) => { if (!IsVisible) StopNow(); };

        Loaded += (_, _) => PlaceCounterSlot();
    }

    /// <summary>Kaç tur BAŞLATILDI — testlerin "kendini yeniden başlattı mı / bitişte durdu mu" sorusunu
    /// sorabildiği tek gözlem noktası (<c>BuildingSpinner.IsRotating</c> deseni).</summary>
    internal int IterationCount { get; private set; }

    /// <summary>Sayaç metnine kaç kez GERÇEKTEN yazıldı — "değişmeyen değer yazılmaz" kuralının kanıtı.</summary>
    internal int CounterWrites { get; private set; }

    internal Storyboard Loop => _loop;
    internal TextBlock Counter => CountText;
    internal Grid CounterSlot => this.CounterSlotHost;
    internal System.Windows.Shapes.Path ChevronFigure => Chevron;
    internal TranslateTransform ChevronShiftTransform => ChevronShift;
    internal TranslateTransform SweepShiftTransform => SweepShift;

    /// <summary>Döngüyü baştan başlatır.</summary>
    public void BeginLoop()
    {
        _finishing = false;
        StartIteration();
    }

    /// <summary>
    /// Reduced-motion karesi: hiçbir saat kurulmaz, her parça yerinde durur.
    ///
    /// <para>Ayrı bir "statik kompozisyon" YOKTUR ve olmamalıdır — öğelerin TABAN değerleri zaten duruş
    /// karesidir (bkz. XAML başlığı). <c>Stop()</c> animasyonları söker ve taban değerlere döner, yani duruş
    /// karesi bedavaya gelir. İkinci bir kompozisyon yazmak, animasyonun duruş evresiyle sessizce
    /// ayrışabilecek bir kopya olurdu.</para></summary>
    public void ShowStaticFrame() => StopNow();

    /// <summary>[K-10] Yeni tur BAŞLATMA; içinde bulunduğun turu bitişine kadar koş, sonra
    /// <paramref name="onFinished"/>'ı çağır. Çağrı anında hiçbir şeyi kesmez.</summary>
    public void RequestFinish(Action onFinished)
    {
        _onFinished = onFinished;
        _finishing = true;
    }

    /// <summary>
    /// Animasyonsuz, anında durdur — saat SÖKÜLÜR, parçalar duruş karesine döner.
    ///
    /// <para><b>Bekleyen bir bitiş varsa taahhüt DÜŞMEZ, hemen yerine getirilir.</b> Senaryo gerçektir:
    /// koşu bitti, gösterge çıkış evresini oynatıyor ve tam o sırada kullanıcı pencereyi geri getiriyor.
    /// Gösterge anında gizlenir (kullanıcı zaten ekrana döndü) ama "bitti" haberi terminal ANINDA tepsideyken
    /// doğmuştur ve verilmelidir. Sessizce yutulsaydı koşu bildirimsiz kapanırdı.</para></summary>
    public void StopNow()
    {
        var onFinished = _onFinished;
        _finishing = false;
        _onFinished = null;
        Teardown();
        onFinished?.Invoke();
    }

    /// <summary>
    /// Saati GERÇEKTEN söker. <c>Stop()</c> yetmez: clock'u durdurur ama animasyonlar öğelere BAĞLI kalır
    /// (<c>HasAnimatedProperties</c> true kalır) — ve dahası <c>Stop()</c>'un kendisi <c>Completed</c>
    /// tetikler, yani aşağıdaki handler döngüyü yeniden başlatırdı. <see cref="_running"/> bayrağı o
    /// yeniden-giriş kapısıdır.
    /// </summary>
    private void Teardown()
    {
        _running = false;
        _loop.Remove(this);
    }

    /// <summary>
    /// [K-6/K-14] Sayacı günceller. Değer DEĞİŞMEDEN metin yazılmaz (§14.5: aynı string'i atamak bile
    /// measure/draw'ı boşa kirletir ve bu sayaç koşu boyunca sık beslenir). Değişince rakamlar sert takas
    /// edilmez: metin kısılır, yeni değer yazılır, geri açılır — yalnız OPAKLIK oynar, yerleşim durur.
    /// </summary>
    public void SetCounter(int done, int total)
    {
        if (_done == done && _total == total) return;
        _done = done;
        _total = total;

        string text = string.Format(CultureInfo.InvariantCulture, "{0}/{1}", done, total);

        // İlk yazımda geçilecek bir değer yoktur — üstelik gösterge o sırada zaten kendi giriş evresini
        // oynatıyordur; üstüne bir de sayaç geçişi bindirmek çift giriş olurdu.
        var fast = MotionTokens.ResolveFast(this);
        if (!_hasCounter || !fast.Animate)
        {
            CountText.BeginAnimation(OpacityProperty, null);
            WriteCounter(text);
            return;
        }

        _pendingCounterText = text;
        var fadeOut = MotionTokens.SplineTo(0.0, fast.Duration, fast.Spline);
        fadeOut.Completed += OnCounterFadedOut;
        CountText.BeginAnimation(OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
    }

    private void WriteCounter(string text)
    {
        CountText.Text = text;
        CounterWrites++;
        _hasCounter = true;
    }

    private void OnCounterFadedOut(object? sender, EventArgs e)
    {
        if (_pendingCounterText is not { } text) return;
        _pendingCounterText = null;
        WriteCounter(text);

        // Süre BURADA yeniden taze okunur: kullanıcı OS ayarını geçişin ortasında kapatmış olabilir.
        var fast = MotionTokens.ResolveFast(this);
        if (!fast.Animate)
        {
            CountText.BeginAnimation(OpacityProperty, null);
            return;
        }

        // SnapshotAndReplace, sönmüş (0) olan O ANKİ değerden devralır — animasyonu temizleyip yeniden
        // başlatmak taban değere (1) atlar ve geri açılış hiç görünmezdi.
        CountText.BeginAnimation(
            OpacityProperty, MotionTokens.SplineTo(1.0, fast.Duration, fast.Spline), HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Sayaç yuvası, beyaz şeridin PAYLAŞILAN geometrisinden ölçülür — şeridin kutusu XAML'de ikinci
    /// kez yazılsaydı, şerit genişleyince (ör. dört haneli bir sayaç) yuva sessizce yerinde kalırdı.</summary>
    private void PlaceCounterSlot()
    {
        if (TryFindResource("Brand.Pill.WhiteCounter") is not RectangleGeometry pill) return;
        Canvas.SetLeft(CounterSlotHost, pill.Rect.X);
        Canvas.SetTop(CounterSlotHost, pill.Rect.Y);
        CounterSlotHost.Width = pill.Rect.Width;
        CounterSlotHost.Height = pill.Rect.Height;
    }

    private void StartIteration()
    {
        _running = true;
        IterationCount++;
        _loop.Begin(this, isControllable: true);
    }

    private void OnIterationCompleted(object? sender, EventArgs e)
    {
        // Sökme sırasında gelen Completed (bkz. Teardown) döngüyü diriltmesin.
        if (!_running) return;

        if (_finishing)
        {
            _finishing = false;
            var onFinished = _onFinished;
            _onFinished = null;
            Teardown();
            onFinished?.Invoke();
            return;
        }

        // Görünmez bir gösterge yeni tur açmaz (§14.5).
        if (!IsVisible)
        {
            Teardown();
            return;
        }

        // Dikiş görünmez: 3.000 s karesinde son şerit çoktan silinmiş, şevron da 2.800'de sönmüştür — yeni
        // turun ilk karesi de boştur (şevron 0.300'e kadar açılır). Yani tur sınırında göze çarpan bir
        // kesinti oluşmaz (deliverable v1.2 kararı: "boş kare yoktur").
        StartIteration();
    }
}
