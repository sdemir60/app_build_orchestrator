using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.App;

/// <summary>
/// [kaynak şeridi · design v1.25.0 §2.1, §3.6 · plan R5, R6] Title bar'ın ortasındaki <b>kaynak şeridinin</b> pencereye kablajı: (1) CPU/RAM
/// örnekleyicisinin ne zaman çalıştığı, (2) şeridin caption davranışını kendisinin iletmesi.
///
/// <para><b>Örnekleyici.</b> Üretim kurucusu (<c>Services/ResourceSampler.cs</c>) outer job'u OKUR (<see cref="EngineHost.OuterJob"/> — yazmaz) ve
/// motorun PID'ini her okumada sorar; sonuç <see cref="ViewModels.RunViewModel.ApplyResourceSample"/>'a gider. Çalışma kuralı TEK yerdedir
/// (<see cref="ShouldSampleResources"/>): pencere yüklü, yüzey gizli değil (tepsideki uygulama kaynak ölçmez) ve küçültülmüş değil. Üç sinyal
/// yeniden sorar — <c>Loaded</c>, <see cref="SetSurfaceHidden"/> (gizli yüzey sinyalinin TEK yazarı) ve <c>WindowState</c> — ve aynı değeri
/// yinelemek örnekleyicide bir şey yapmaz. Başsız süitte pencere gösterilmez ve <c>Loaded</c> gelmez: örnekleyici kurulur ama hiç açılmaz, ilgisiz
/// testler Win32 okuması yapmaz. İlk açılışta taban okuması alınır ve ilk örnek 0,5 sn sonra gelir; gizli/küçültülmüşten dönüşte ANINDA bir örnek
/// ve 3 sn ritmi (R5 — örnekleyicinin işi). <see cref="StopResourceStrip"/> pencere kapanırken durdurur ve bir daha açılmaz.</para>
///
/// <para><b>WindowState neden <c>StateChanged</c> değil, DP izleyicisi:</b> pencere XAML'de doğuştan Maximized açılır ve WPF HWND'den ÖNCE kurulmuş bir
/// durum için <c>StateChanged</c>'i hiç ateşlemez (bkz. <c>MaximizeFix.Bind</c>); küçültme/geri alma ise HWND'den sonra gelir. DP
/// izleyicisi her iki değişimi — OS kaynaklı da programatik de — yakalar ve başsız süitte sürülebilir. Abonelik güçlü referans tutar; tek ve uygulama
/// ömrü boyunca yaşayan ana pencere için kabul edilir.</para>
///
/// <para><b>R6 — caption iletimi.</b> Hücrelerin üstünde pencerenin caption davranışını bu dosya yürütür: sol basış yerel caption'a devredilir
/// (<see cref="ForwardCaptionDrag"/>), çift tık büyütür/geri alır, sağ tuş bırakışı sistem menüsünü açar. Kuralın nedeni ve sınırı (tıklama
/// alanı yalnız hücreler): <c>Views/ResourceStrip.xaml</c> başlığı — tek otorite. Üç eylem birer test dikişinden geçer (<c>…ForTest</c>): gerçek
/// yerel basış ve sistem menüsü bir HWND ve fare ister, başsız süitte koşamaz.</para>
/// </summary>
public partial class MainWindow
{
    private ResourceSampler? _resourceSampler;

    /// <summary>Pencerenin <c>Loaded</c> olayı geldi mi. <see cref="FrameworkElement.IsLoaded"/> okunmaz: başsız süitte <c>Loaded</c> olayı yükseltilerek
    /// sürülebilir (üretimdeki yol), <c>IsLoaded</c> ise gerçek bir HWND olmadan hiç true olmaz.</summary>
    private bool _resourceStripLoaded;

    /// <summary>Pencere kapandı (<see cref="StopResourceStrip"/>) — geri dönüşü yok; bundan sonra hiçbir sinyal örneklemeyi açmaz.</summary>
    private bool _resourceStripClosed;

    /// <summary>[test yüzeyi] Örnekleyici: etkinlik (<see cref="ResourceSampler.IsActive"/>) ve örnek sayısı (<see cref="ResourceSampler.SampleCount"/>) gözlemi.</summary>
    internal ResourceSampler? StripSampler => _resourceSampler;

    /// <summary>[test dikişi] Şeridin pencerenin yerel caption'ına gönderdiği basış — (mesaj, isabet kodu, <c>lParam</c>) (<c>null</c> ⇒ üretim: gerçek
    /// <c>ReleaseCapture</c> + <c>SendMessage</c>, bkz. <see cref="ForwardCaptionDrag"/>). Dikiş çağrının KENDİSİNİN üstündedir, "bir sürükleme istendi"
    /// bayrağının değil.</summary>
    internal Action<uint, nint, nint>? CaptionDragForTest { get; set; }

    /// <summary>[test dikişi] Şeridin ilettiği çift tık eylemi (<c>null</c> ⇒ üretim: <see cref="ToggleMaximizeRestore"/>).</summary>
    internal Action? CaptionToggleMaximizeForTest { get; set; }

    /// <summary>[test dikişi] Şeridin ilettiği sistem menüsü eylemi (<c>null</c> ⇒ üretim: imlecin olduğu yerde <c>SystemCommands.ShowSystemMenu</c>).</summary>
    internal Action? CaptionSystemMenuForTest { get; set; }

    /// <summary>Ctor'dan bir kez: örnekleyiciyi kurar, etkinlik sinyallerine bağlar ve şeridin caption iletimini kablolar.</summary>
    private void SetupResourceStrip()
    {
        _resourceSampler = new ResourceSampler(
            new JobResourceProbe(_engine.OuterJob, () => _engine.EnginePid),
            new DispatcherPollTimer(Dispatcher), ResourceSampler.MonotonicNow, Environment.ProcessorCount, _vm.ApplyResourceSample);

        Loaded += (_, _) =>
        {
            _resourceStripLoaded = true;
            RefreshResourceSampler();
        };
        DependencyPropertyDescriptor.FromProperty(WindowStateProperty, typeof(Window))
            .AddValueChanged(this, (_, _) => RefreshResourceSampler());

        ResourceStripView.MouseLeftButtonDown += OnResourceStripMouseLeftButtonDown;
        ResourceStripView.MouseRightButtonUp += OnResourceStripMouseRightButtonUp;
    }

    /// <summary>Örnekleyicinin çalışma kuralı — saf: yalnız yüklü, görünür (yüzey gizli değil) ve küçültülmemiş pencere kaynak ölçer. Gizliyken ya da
    /// simge durumundayken kimsenin görmediği bir sayı için zamanlayıcı da Win32 okuması da çalışmaz.</summary>
    internal static bool ShouldSampleResources(bool loaded, bool surfaceHidden, WindowState state) =>
        loaded && !surfaceHidden && state != WindowState.Minimized;

    /// <summary>Kuralı pencerenin güncel durumuyla yeniden sorar ve sonucu örnekleyiciye bildirir (idempotent — örnekleyici aynı değeri yok sayar).
    /// <see cref="SetSurfaceHidden"/>, <c>Loaded</c> ve <c>WindowState</c> değişimi çağırır; ctor biterken örnekleyici henüz yoktur (<c>null</c> ⇒ sessiz).</summary>
    private void RefreshResourceSampler() =>
        _resourceSampler?.SetActive(!_resourceStripClosed && ShouldSampleResources(_resourceStripLoaded, IsSurfaceHidden, WindowState));

    /// <summary>Pencere gerçekten kapandı (<c>OnClosed</c>): zamanlayıcı durur ve örnekleme bir daha açılmaz. Tepsiye gizlenen kapatmalar buraya GELMEZ —
    /// onlar gizli yüzey sinyaliyle <see cref="RefreshResourceSampler"/> üzerinden durur ve geri gelir.</summary>
    internal void StopResourceStrip()
    {
        _resourceStripClosed = true;
        _resourceSampler?.SetActive(false);
    }

    // ==================================== R6: şerit caption davranışını kendisi iletir ====================================

    /// <summary>Sol basış: çift tık (<c>ClickCount == 2</c>) büyütür/geri alır, tek basış yerel caption sürüklemesine devredilir. Çift tık sürüklemeden
    /// ÖNCE ayrılır: ikinci basış yerel taşıma döngüsüne hiç girmez. Windows'un caption'ında üçüncü art arda tık yeni bir tık dizisini başlatır;
    /// <c>== 2</c> bunu birebir yansıtır. Basış tüketilir (pencerenin geri kalanına kabarmaz).</summary>
    private void OnResourceStripMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) ForwardToggleMaximize();
        else ForwardCaptionDrag();
        e.Handled = true;
    }

    /// <summary>Sağ tuş BIRAKIŞI sistem menüsünü açar (Windows caption'ında da menü basışta değil bırakışta açılır).</summary>
    private void OnResourceStripMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        ForwardSystemMenu();
        e.Handled = true;
    }

    /// <summary>
    /// Basışı pencerenin YEREL caption'ına devreder — Windows'un kendi title bar sürüklemesi: <c>ReleaseCapture</c>, sonra <c>WM_NCLBUTTONDOWN</c> +
    /// <c>HTCAPTION</c> ve basışın ekran noktası. Çağrı sistemin taşıma döngüsü bitene (tuş bırakılana) dek döner; döngü büyütülmüş pencereyi önce
    /// geri alır, kenara yaklaşınca snap eder — pencere durumu burada hiç sorulmaz.
    ///
    /// <para><b>Neden <c>Window.DragMove</c> değil:</b> o yalnız pencere durumu Normal iken bir şey gönderir (ölçüldü: .NET 10.0.11'in IL'i durum Normal
    /// değilse hiçbir şey göndermeden döner); pencere ise doğuştan büyütülmüş açılır, yani hücreden sürükleme en sık durumda sessizce ölürdü.</para>
    ///
    /// <para>Döngü tuşun bırakılışını yutar. <c>Window.DragMove</c> da (aynı IL) ardından pencereye bir <c>WM_LBUTTONUP</c> verir; burada da aynısı
    /// yapılır ki WPF'in fare durumu gerçeğe eşit kalsın. Sol tuş artık basılı değilse (basışla işleyici arasında bırakılmışsa) taşıma döngüsü
    /// başlatılmaz. Ekran noktası <c>GetMessagePos</c>'tan gelir: işlenen fare mesajının imleç konumudur, <c>lParam</c>'ın istediği paketli biçimdedir ve
    /// döngüden önce okunur (döngü kuyruğu tüketir).</para>
    /// </summary>
    private void ForwardCaptionDrag()
    {
        nint lParam = (nint)Win32.GetMessagePos();
        if (CaptionDragForTest is { } seam)
        {
            seam(Win32.WM_NCLBUTTONDOWN, Win32.HTCAPTION, lParam);
            return;
        }

        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0 || Mouse.LeftButton != MouseButtonState.Pressed) return;

        Win32.ReleaseCapture();
        Win32.SendMessage(hwnd, Win32.WM_NCLBUTTONDOWN, Win32.HTCAPTION, lParam);
        Win32.SendMessage(hwnd, Win32.WM_LBUTTONUP, 0, 0);
    }

    private void ForwardToggleMaximize()
    {
        if (CaptionToggleMaximizeForTest is { } seam) seam();
        else ToggleMaximizeRestore();
    }

    private void ForwardSystemMenu()
    {
        if (CaptionSystemMenuForTest is { } seam) seam();
        else SystemCommands.ShowSystemMenu(this, SystemMenuAnchor(PointToScreen(Mouse.GetPosition(this)), VisualTreeHelper.GetDpi(this)));
    }

    /// <summary>Sistem menüsünün konumu. <see cref="Visual.PointToScreen"/> CİHAZ pikseli verir, <see cref="SystemCommands.ShowSystemMenu"/> ise
    /// MANTIKSAL (dip) ekran koordinatı ister — WPF onu pencerenin DPI'ıyla cihaza çevirir (ölçüldü: <c>Visual.GetDpi</c> +
    /// <c>DpiHelper.LogicalPixelsToDevice</c>). Çevrilmeden verilirse 125%'te menü imlecin 1,25 katı uzağında açılırdı.</summary>
    internal static Point SystemMenuAnchor(Point physicalScreen, DpiScale dpi) =>
        new(physicalScreen.X / dpi.DpiScaleX, physicalScreen.Y / dpi.DpiScaleY);
}
