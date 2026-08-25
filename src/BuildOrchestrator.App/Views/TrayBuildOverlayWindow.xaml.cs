using System.Windows;
using System.Windows.Interop;
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
/// devredilir — burada animasyon ya da sayaç mantığı YOKTUR.</para>
///
/// <para><b>Bilinçli sınır:</b> tepsi birincil görev çubuğundadır, bu yüzden overlay de birincil ekranda
/// kalır (<see cref="SystemParameters.WorkArea"/> birincil ekranın çalışma alanıdır). Çok monitörlü bir
/// masaüstünde kullanıcı başka bir ekranda çalışıyor olabilir; gösterge yine tepsinin yanında belirir —
/// istenen davranış budur, gösterge tepsinin görsel karşılığıdır.</para>
/// </summary>
public partial class TrayBuildOverlayWindow : Window, ITrayBuildIndicatorView
{
    /// <summary>
    /// Sahne oranı (430×286) korunarak seçilmiş overlay ölçüleri ve ekran kenarına bırakılan pay — DIP.
    ///
    /// <para>Tasarım token'ı DEĞİLDİR: bu bileşenin kendi ölçüleridir (Controls.xaml'in "bileşenin KENDİ
    /// ölçüleri" istisnasıyla aynı statü). Yeterince küçük ki başka bir şeyi örtmesin, yeterince büyük ki
    /// üç haneli sayaç okunsun. DPI hesabı YAPILMAZ: bunlar DIP'tir, PerMonitorV2 altında dönüşümü WPF
    /// yapar.</para></summary>
    internal const double OverlayWidth = 144;
    internal const double OverlayHeight = 96;
    internal const double EdgeMargin = 12;

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

    internal TrayBuildIndicator Indicator => IndicatorControl;

    /// <summary>
    /// Verilen çalışma alanının sağ alt köşesi, kenar payıyla.
    ///
    /// <para>Saf ve parametreli: görev çubuğu solda ya da üstte olabilir, o zaman çalışma alanı (0,0)'dan
    /// başlamaz — köşe ekranın kendisinden değil ÇALIŞMA ALANINDAN türemeli, yoksa overlay görev çubuğunun
    /// altına kayar. Alan dışarıdan verildiği için test edilebilir.</para></summary>
    internal static (double Left, double Top) Place(Rect workArea, double width, double height, double margin)
        => (workArea.Right - width - margin, workArea.Bottom - height - margin);

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

    public void UpdateCounter(int done, int total) => IndicatorControl.SetCounter(done, total);

    public void BeginExit(Action onFinished) => IndicatorControl.RequestFinish(onFinished);

    public void HideNow()
    {
        Hide();
        // Gizlenme göstergenin kendi görünürlük kapısını da tetikler; bu çağrı bekleyen bir bitiş taahhüdünün
        // (çıkış evresi yarıda kaldıysa) yine de bildirilmesini garanti eder.
        IndicatorControl.StopNow();
    }

    /// <summary>Her gösterimde YENİDEN konumlanır — çalışma alanı bu arada değişmiş olabilir (görev çubuğu
    /// taşınmış, çözünürlük değişmiş, bir ekran eklenmiş).</summary>
    private void Reveal()
    {
        var (left, top) = Place(SystemParameters.WorkArea, OverlayWidth, OverlayHeight, EdgeMargin);
        Left = left;
        Top = top;
        Show();
    }
}
