using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using BuildOrchestrator.App.Services;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [T62/K5 · A13.2] Sistem tepsisi ikonu — WPF'te <c>NotifyIcon</c> yoktur, onaylı paket <c>H.NotifyIcon.Wpf</c>
/// (feasibility §3.2). Bu sınıf yalnız KABUKTUR: ikon + menü + balloon; ne yapılacağına karar veren yok, olayları
/// dışarı verir (<see cref="RestoreRequested"/>/<see cref="StopRequested"/>/<see cref="ExitRequested"/>).
///
/// <para><b>İkon:</b> 16px ELLE ayarlanmış raster (<c>Assets/tray-icon-16.ico</c>) — 64px SVG'nin otomatik
/// küçültülmesi amber "D"yi bozar (feasibility §3.2). [T64] Çok boyutlu <c>app-icon.ico</c> (pencere/taskbar)
/// artık var ama tepsi BİLEREK 16px varyantında kalır: tepsi zaten 16px ister ve elle ayarlanmış kare
/// rasterlestirilmiş olandan nettir.</para>
/// </summary>
internal sealed class AppTrayIcon : IDisposable
{
    private const string IconUri = "pack://application:,,,/BuildOrchestrator.App;component/Assets/tray-icon-16.ico";

    private readonly TaskbarIcon _icon;

    public AppTrayIcon()
    {
        var stop = new MenuItem { Header = "Stop" };
        stop.Click += (_, _) => StopRequested?.Invoke();
        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => ExitRequested?.Invoke();
        var menu = new ContextMenu();
        menu.Items.Add(stop);
        menu.Items.Add(exit);

        _icon = new TaskbarIcon
        {
            ToolTipText = AppIdentity.Product, // [About] ürün adı tek kaynaktan (kopya YASAK)
            IconSource = new BitmapImage(new Uri(IconUri)),
            ContextMenu = menu,
            Visibility = Visibility.Visible,
        };
        _icon.TrayLeftMouseUp += (_, _) => RestoreRequested?.Invoke();
        _icon.TrayMouseDoubleClick += (_, _) => RestoreRequested?.Invoke();
        _icon.ForceCreate(false); // efficiency mode KAPALI: process askıya alınırsa derleme takibi durur
    }

    /// <summary>Tepsi ikonuna sol tık / çift tık — pencereyi geri getir.</summary>
    public event Action? RestoreRequested;
    /// <summary>Tepsi menüsü → Stop (koşan derlemeyi graceful durdur).</summary>
    public event Action? StopRequested;
    /// <summary>Tepsi menüsü → Exit (GERÇEK çıkış → kaskat kill).</summary>
    public event Action? ExitRequested;

    /// <summary>
    /// [K5] YALNIZ ilk `X` kapatmasında: uygulamanın tepside çalışmaya devam ettiğini OS balloon'u ile bildirir.
    /// Uygulama İÇİ toast design §8'de yasaktır — bu bilinçli olarak işletim sisteminin bildirimidir.
    /// </summary>
    public void ShowClosedToTrayNotification() => _icon.ShowNotification(
        title: AppIdentity.Product,
        message: "Still running in the tray. Right-click the tray icon and choose Exit to quit.",
        icon: NotificationIcon.Info);

    /// <summary>[E2/triaj-f] Genel OS tray balloon'u — ikinci instance mevcut pencereyi öne getiremediğinde
    /// (SESSİZ kalmamak için) tek-satırlık bilgilendirme gösterir. Uygulama-içi toast değil (design §8 yasağı) —
    /// bilinçli olarak OS bildirimi.</summary>
    public void ShowNotification(string title, string message) =>
        _icon.ShowNotification(title: title, message: message, icon: NotificationIcon.Warning);

    public void Dispose() => _icon.Dispose();
}
