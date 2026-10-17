using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Services;

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
/// <para><b>R6 — caption iletimi.</b> Şerit <c>IsHitTestVisibleInChrome=True</c> taşır (tooltip için fare olayı almalı), bu yüzden o bölge artık
/// caption değildir ve Windows'un caption davranışı oradan GİTMİŞTİR. Şeridin üstünde: sol basış pencereyi sürükler, çift tık büyütür/geri alır
/// (<see cref="ToggleMaximizeRestore"/> — caption butonuyla AYNI yol), sağ tuş bırakışı sistem menüsünü açar. Şerit hover zemini almaz ve imleci
/// değiştirmez; bu iletim kullanıcıya görünmez. Üç pencere eylemi birer test dikişinden geçer (<c>…ForTest</c>): gerçek <c>DragMove</c> ve sistem
/// menüsü bir HWND ve fare ister, başsız süitte koşamaz.</para>
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

    /// <summary>[test dikişi] Şeridin ilettiği sürükleme eylemi (<c>null</c> ⇒ üretim: sol tuş basılıyken <see cref="Window.DragMove"/>).</summary>
    internal Action? CaptionDragMoveForTest { get; set; }

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

    /// <summary>Sol basış: çift tık (<c>ClickCount == 2</c>) büyütür/geri alır, tek basış sürüklemeyi başlatır. Windows'un caption'ında üçüncü art arda
    /// tık yeni bir tık dizisini başlatır; <c>== 2</c> bunu birebir yansıtır. Basış tüketilir (pencerenin geri kalanına kabarmaz).</summary>
    private void OnResourceStripMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) ForwardToggleMaximize();
        else ForwardDragMove();
        e.Handled = true;
    }

    /// <summary>Sağ tuş BIRAKIŞI sistem menüsünü açar (Windows caption'ında da menü basışta değil bırakışta açılır).</summary>
    private void OnResourceStripMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        ForwardSystemMenu();
        e.Handled = true;
    }

    private void ForwardDragMove()
    {
        if (CaptionDragMoveForTest is { } seam) seam();
        else if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove(); // tuş basılı değilse DragMove InvalidOperationException atar
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
