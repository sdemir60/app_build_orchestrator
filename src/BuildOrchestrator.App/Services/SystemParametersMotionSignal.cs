using System.Windows;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [It-4a Foundation / Global Constraints — reduced-motion] Gerçek OS sinyali: SystemParameters.ClientAreaAnimation
/// + StaticPropertyChanged canlı takip (uygulama-içi toggle YOK). Uygulama ömrü boyunca tek instance — statik
/// WPF event'ine abone olduğundan GC'den etkilenmez, ayrıca App tarafından referans tutulur.
/// </summary>
public sealed class SystemParametersMotionSignal : IMotionSignal
{
    public SystemParametersMotionSignal()
    {
        SystemParameters.StaticPropertyChanged += OnStaticPropertyChanged;
    }

    /// <summary>OS'un "animasyonları göster" ayarı — SALT OKUMA, hiçbir şeyi değiştirmez.</summary>
    public bool AnimationsEnabled => SystemParameters.ClientAreaAnimation;

    public event EventHandler? Changed;

    /// <summary>
    /// [A13/B3 · E6] "Bu <see cref="SystemParameters"/> static-property bildirimi BİZİM sinyalimize mi ait" kararı —
    /// SAF, yan etkisiz ve OS'a DOKUNMAZ (<c>Shell.StartupArgs.Decide</c> deseni: headless kurulamayan bir
    /// kabuğun İÇİNDEKİ kararı dışarı alıp test edilebilir kılmak).
    ///
    /// <para><b>Neden filtre şart:</b> <see cref="SystemParameters"/> ONLARCA static property için AYNI
    /// <c>StaticPropertyChanged</c> event'ini yayar (WorkArea, PrimaryScreenWidth, HighContrast, MenuShowDelay…).
    /// Filtre olmasaydı her ekran/tema/DPI değişimi bir motion tazelemesi tetiklerdi.</para>
    ///
    /// <para><b>Neden AYRI bir üye:</b> karar önce <see cref="OnStaticPropertyChanged"/>'in içindeydi ve dışarıdan
    /// sürülemiyordu — sınamanın TEK yolu makine-global erişilebilirlik ayarını çevirmekti, o ise YASAK
    /// (task-B3-brief.md kural 4). Bu, sınanabilirlik için gereken MİNİMUM seam'dir; üretim yolu (aşağıdaki
    /// handler) bunu kullanır, ikinci bir kopya YOKTUR.</para>
    /// </summary>
    internal static bool IsMotionProperty(string? propertyName) =>
        propertyName == nameof(SystemParameters.ClientAreaAnimation);

    private void OnStaticPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (IsMotionProperty(e.PropertyName))
            Changed?.Invoke(this, EventArgs.Empty);
    }
}
