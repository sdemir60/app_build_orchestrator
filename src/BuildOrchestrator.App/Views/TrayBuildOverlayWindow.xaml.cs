using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [tray indicator/K-1..K-3] Tepsideki derleme göstergesini taşıyan penceresiz overlay — controller'ın
/// gördüğü <see cref="ITrayBuildIndicatorView"/>'un gerçek uygulaması.
///
/// <para>Sınıfın kendi kararları üçtür: <b>kabuk</b> (XAML'de, "olmama" listesi), <b>ex-style</b> (Alt-Tab'da
/// görünmez + kendisi aktive olmaz) ve <b>konum</b> (çalışma alanının sağ alt köşesi). Gerisi göstergeye
/// devredilir — burada animasyon mantığı YOKTUR.</para>
///
/// <para><b>Bilinçli sınır:</b> tepsi birincil görev çubuğundadır, bu yüzden overlay de birincil ekranda
/// kalır (<see cref="PrimaryScreen"/>). Çok monitörlü bir masaüstünde kullanıcı başka bir ekranda çalışıyor
/// olabilir; gösterge yine tepsinin yanında belirir — istenen davranış budur, gösterge tepsinin görsel
/// karşılığıdır.</para>
/// </summary>
public partial class TrayBuildOverlayWindow : Window, ITrayBuildIndicatorView
{
    /// <summary>
    /// Overlay ölçüsü göstergenin BANDINDAN (<see cref="TrayBuildIndicator.StageWidth"/> /
    /// <see cref="TrayBuildIndicator.StageHeight"/>) tek bir ölçekle türer; ekran kenarına bırakılan pay — DIP.
    ///
    /// <para><b>Tek kaynak, tek ölçek:</b> büyütüp küçültmek istendiğinde dokunulacak TEK sayı
    /// <see cref="Scale"/>'dir — 144/96 gibi bağımsız literaller yoktur, ikisi de bant ölçüsünün
    /// <see cref="Scale"/> katıdır (kopya YASAK, CLAUDE.md). Bant, logonun üstünde/altında kalan boş göğü
    /// zaten kırptığı için (bkz. <c>TrayBuildIndicator.xaml</c> başlığı) mark, taşbarın hemen üstüne, fazladan
    /// boşluk bırakmadan oturur.</para>
    ///
    /// <para>Tasarım token'ı DEĞİLDİR: bu bileşenin kendi ölçüleridir (Controls.xaml'in "bileşenin KENDİ
    /// ölçüleri" istisnasıyla aynı statü). Ölçüler DIP'tir ve ekran ölçeğine göre ayrıca büyütülmez: pencerenin boyunu
    /// PerMonitorV2 altında WPF çevirir. Ekranın ölçeğini yalnız köşe hesabı okur (<see cref="Place"/>).</para></summary>
    internal const double Scale = 0.5;
    internal const double OverlayWidth = TrayBuildIndicator.StageWidth * Scale;
    internal const double OverlayHeight = TrayBuildIndicator.StageHeight * Scale;
    /// <summary>
    /// Ekran kenarına bırakılan paylar — sağ ve alt AYRIDIR. Duruş karesinde logo bandın solunda durur, sağında
    /// şevronun çıkış yolu için boşluk kalır; bu yüzden gösterge sağa alta göre daha yakın oturur. Sağ pay
    /// sıfıra İNMEZ: bant şevronun en uç çıkış karesini (gölgesiyle) içinde taşır, küçük bir pay onun ekran
    /// kenarına yapışıp kesilmiş görünmesini önler. Alt pay görev çubuğuna mesafedir.
    /// </summary>
    internal const double RightMargin = 4;
    internal const double BottomMargin = 12;

    public TrayBuildOverlayWindow(ResourceDictionary? resourceScope = null)
    {
        InitializeComponent();
        // Bu pencerenin ana pencerede olduğu gibi bir ebeveyni yoktur — token/geometri kapsamı doğrudan
        // enjekte edilir (MainWindow'un `resourceScope` parametresiyle aynı gerekçe; üretimde Application
        // kaynakları zaten kapsar, testte sözlük elden verilir).
        if (resourceScope is not null) Resources.MergedDictionaries.Add(resourceScope);

        Width = OverlayWidth;
        Height = OverlayHeight;

        // Tıklama tek yerden akar: çizili piksellere basmak ana pencereyi geri getirir. Şeffaf alanın
        // tıklamayı geçirmesini işletim sistemi halleder (bkz. XAML başlığı).
        IndicatorControl.MouseLeftButtonUp += (_, _) => RestoreRequested?.Invoke();
    }

    /// <summary>Logonun çizili piksellerine sol tık — davranış tepsi ikonunun sol tıkıyla AYNIDIR ve aynı
    /// geri getirme yoluna bağlanır (ikinci bir restore yolu yazılmaz).</summary>
    public event Action? RestoreRequested;

    /// <summary>
    /// Verilen çalışma alanının sağ alt köşesi, kenar payıyla — pencerenin <see cref="Window.Left"/>/<see cref="Window.Top"/>'u olarak.
    ///
    /// <para>Saf ve parametreli: görev çubuğu solda ya da üstte olabilir, o zaman çalışma alanı (0,0)'dan
    /// başlamaz — köşe ekranın kendisinden değil ÇALIŞMA ALANINDAN türemeli, yoksa overlay görev çubuğunun
    /// altına kayar. Alan dışarıdan verildiği için test edilebilir.</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL] İki ölçek ayrıdır.</b> Alan FİZİKSEL pikseldir; ölçü ve paylar DIP'tir ve köşenin bulunduğu ekranın
    /// ölçeğiyle (<paramref name="screenScale"/>) piksele çevrilir. Sonuç WPF birimine PENCERENİN o anki ölçeğiyle
    /// (<paramref name="windowScale"/>) geri bölünür, çünkü WPF <c>Left</c>/<c>Top</c>'u o ölçekle piksele çevirir. Eski hâl alanı
    /// <see cref="SystemParameters.WorkArea"/>'dan, yani DIP olarak alıyordu: WPF o değeri sürecin AÇILIŞTA okuduğu sistem DPI'ıyla
    /// böler ve tazelemez — ekran ölçüsü uygulama açıkken değişince (laptop %150 ↔ 4K %100) gösterge köşeden yüzlerce piksel kayıyor,
    /// ölçek büyüdüyse ekranın dışına düşüyordu (ölçüm ve pin: <c>TrayOverlayPositionProbeTests</c>).</para></summary>
    internal static (double Left, double Top) Place(
        Rect workAreaPx, double screenScale, double windowScale,
        double width, double height, double rightMargin, double bottomMargin)
        => ((workAreaPx.Right - (width + rightMargin) * screenScale) / windowScale,
            (workAreaPx.Bottom - (height + bottomMargin) * screenScale) / windowScale);

    /// <summary>Birincil ekranın çalışma alanı FİZİKSEL pikselde ve o ekranın ölçeği — her çağrıda Win32'den TAZE okunur
    /// (<c>GetMonitorInfo</c>, <c>GetDpiForMonitor</c>); WPF'in saklı değerlerine bakılmaz. Okunamazsa <c>null</c>.</summary>
    private static (Rect WorkAreaPx, double Scale)? PrimaryScreen()
    {
        nint monitor = Win32.MonitorFromPoint(default, Win32.MONITOR_DEFAULTTOPRIMARY);
        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(monitor, ref info)
            || Win32.GetDpiForMonitor(monitor, Win32.MDT_EFFECTIVE_DPI, out uint dpi, out _) != 0)
            return null;
        var work = info.rcWork;
        return (new Rect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top), dpi / 96.0);
    }

    /// <summary>Pencereyi birincil ekranın köşesine koyar (<see cref="Place"/>). Ekran okunamazsa pencere son yerinde kalır — bir
    /// göstergenin yeri yüzünden gösterim düşmez.</summary>
    private void PlaceInCorner()
    {
        if (PrimaryScreen() is not var (workAreaPx, screenScale)) return;
        var (left, top) = Place(workAreaPx, screenScale, VisualTreeHelper.GetDpi(this).DpiScaleX,
            OverlayWidth, OverlayHeight, RightMargin, BottomMargin);
        Left = left;
        Top = top;
    }

    /// <summary>
    /// Pencerenin ölçeği değişti — en son başka ölçekli bir ekrandaydı ya da ekranın ölçeği değişti. <see cref="Place"/> pencerenin
    /// ESKİ ölçeğiyle hesaplanmışsa sol üst köşe doğru yerdedir ama boy eski ölçektedir; WPF yeni ölçeğe geçince Windows'un önerdiği
    /// dikdörtgeni uygular. Köşe bu yüzden yeni ölçekle yeniden hesaplanır — kuyruğa bırakılarak, ki WPF'in kendi yerleşiminden SONRA
    /// koşsun.
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(PlaceInCorner);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint hwnd = new WindowInteropHelper(this).Handle;
        int current = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, current | Win32.OverlayExStyle);
    }

    // ---------------------------------------------------------------- ITrayBuildIndicatorView

    public void ShowLoop()
    {
        Reveal();
        IndicatorControl.BeginLoop();
    }

    public void ShowStatic()
    {
        Reveal();
        IndicatorControl.ShowStaticFrame();
    }

    public void BeginExit(Action onFinished) => IndicatorControl.RequestFinish(onFinished);

    public void HideNow()
    {
        Hide();
        // Gizlenme göstergenin kendi görünürlük kapısını da tetikler; bu çağrı bekleyen bir bitiş taahhüdünün
        // (çıkış evresi yarıda kaldıysa) yine de bildirilmesini garanti eder.
        IndicatorControl.StopNow();
    }

    /// <summary>Her gösterimde YENİDEN konumlanır — çalışma alanı bu arada değişmiş olabilir (görev çubuğu
    /// taşınmış, çözünürlük ya da ölçek değişmiş, bir ekran eklenmiş). Konum pencerenin ölçeğine bağlı olduğu için önce pencerenin
    /// kendisi kurulur (<see cref="WindowInteropHelper.EnsureHandle"/>): ilk gösterimde ölçeği ancak o zaman bilinir.</summary>
    private void Reveal()
    {
        new WindowInteropHelper(this).EnsureHandle();
        PlaceInCorner();
        Show();
    }
}
