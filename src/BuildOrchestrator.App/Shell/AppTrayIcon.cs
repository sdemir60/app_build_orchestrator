using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [T62/K5 · A13.2] Sistem tepsisi ikonu — WPF'te <c>NotifyIcon</c> yoktur, onaylı paket <c>H.NotifyIcon.Wpf</c>
/// (feasibility §3.2). Bu sınıf yalnız KABUKTUR: ikon + menü + balloon; ne yapılacağına karar veren yok.
/// <see cref="RestoreRequested"/>/<see cref="ExitRequested"/> olaydır; Stop maddesi verilen komuta bağlıdır ve
/// etkinliği CanExecute'tan gelir (ActionBar'daki Stop düğmesiyle AYNI kaynak — ikinci bir kapı YAZILMAZ).
///
/// <para><b>İkon:</b> 16px ELLE ayarlanmış raster (<c>Assets/tray-icon-16.ico</c>) — 64px SVG'nin otomatik
/// küçültülmesi amber "D"yi bozar (feasibility §3.2). [T64] Çok boyutlu <c>app-icon.ico</c> (pencere/taskbar)
/// artık var ama tepsi BİLEREK 16px varyantında kalır: tepsi zaten 16px ister ve elle ayarlanmış kare
/// rasterlestirilmiş olandan nettir.</para>
///
/// <para><b>Balloon ikonu ayrıdır:</b> koşu sonucu bildirimi <c>app-icon.ico</c>'nun BÜYÜK karesini taşır
/// (<see cref="LargeIconPx"/>). Bildirim penceresi tepsi kutucuğundan çok daha geniştir; oraya 16px raster
/// koymak ikonu bulanıklaştırır.</para>
/// </summary>
internal sealed class AppTrayIcon : IDisposable, ITrayRunNotifier
{
    private const string IconUri = "pack://application:,,,/BuildOrchestrator.App;component/Assets/tray-icon-16.ico";

    /// <summary>Windows'un "large icon" balloon'unda gösterdiği kare. <c>app-icon.ico</c> bu kareyi gerçekten
    /// taşır (<c>IconGeometryTests</c> 16/24/32/48/256'yı pinler), yani ölçekleme yapılmaz.</summary>
    /// <summary>
    /// Bildirimin özel ikonunun kenarı. Bir tercih DEĞİL, API'nin ŞARTI: <c>H.NotifyIcon</c> özel balloon
    /// ikonunu çalışma anında ölçer ve 32×32 dışındaki her şeyi <see cref="InvalidOperationException"/> ile
    /// reddeder. Yanlış ölçü derlemede görünmez; bildirim gösterilirken patlar ve üretimde o çağrı
    /// beklenmeyen bir <c>Task</c>'in içinde olduğu için istisna YUTULUR — koşu bildirimsiz kapanır.
    /// Pin: <c>TrayBalloonIconTests</c>.
    /// </summary>
    private const int BalloonIconPx = 32;

    private readonly TaskbarIcon _icon;

    /// <summary>Bildirimin büyük ikonu — bir KEZ yüklenir ve handle'ı her balloon'a verilir; her bildirimde
    /// yeniden çözmek gereksiz GDI nesnesi üretirdi. <see cref="Dispose"/> bırakır.</summary>
    private readonly System.Drawing.Icon _largeIcon;

    /// <summary>Hiçbir zaman çalıştırılamayan Stop komutu — arkasında RunViewModel/engine olmayan bir tepsi
    /// içindir (ör. ikinci instance'ın geçici balloon tray'i). Kopya YASAK: aynı <c>RelayCommand(() => { },
    /// () => false)</c> eskiden App.xaml.cs'te ve test tarafında ayrı ayrı yazılıyordu; artık TEK örnek
    /// buradadır. Statik/paylaşılan TEK örnek güvenlidir: WPF'in <c>MenuItem</c>'ı <c>ICommand.CanExecuteChanged</c>'e
    /// zayıf bir event manager üzerinden abone olur.</summary>
    internal static readonly ICommand NoRunToStop = new RelayCommand(() => { }, () => false);

    public AppTrayIcon(ICommand stopCommand, RunViewModel? run = null)
    {
        _largeIcon = LoadBalloonIcon();

        var menu = CreateMenu(stopCommand, () => ExitRequested?.Invoke(), run);

        _icon = new TaskbarIcon
        {
            ToolTipText = AppIdentity.Product, // [About] ürün adı tek kaynaktan (kopya YASAK)
            IconSource = new BitmapImage(new Uri(IconUri)),
            ContextMenu = menu,
            Visibility = Visibility.Visible,
        };
        _icon.TrayLeftMouseUp += (_, _) => RestoreRequested?.Invoke();
        _icon.TrayMouseDoubleClick += (_, _) => RestoreRequested?.Invoke();
        // [Ö4/K-2] Balloon tıkı da AYNI yoldan geri getirir — bu ikonun gösterdiği HER balloon'a uygulanır
        // (ilk-kapanış, ikinci-instance uyarısı, koşu sonucu): ikinci bir restore yolu YAZILMAZ.
        _icon.TrayBalloonTipClicked += (_, _) => RestoreRequested?.Invoke();
        _icon.ForceCreate(false); // efficiency mode KAPALI: process askıya alınırsa derleme takibi durur
    }

    /// <summary>Menü TEK yerden kurulur: <c>TaskbarIcon</c> (dolayısıyla ctor) headless testte kurulamaz
    /// (gerçek bir tepsi ikonu ister), bu yüzden menü mantığı statik bir fabrikaya ayrılır ki gerçek bir
    /// tepsi kurmadan sınanabilsin. Stop maddesi <paramref name="stop"/>'a DOĞRUDAN bağlanır (<c>Command</c>) —
    /// etkinliği WPF'in kendi komut kapısından gelir, ikinci bir <c>CanExecute</c> sorgusu burada YAZILMAZ
    /// (eski hâl <c>MainWindow</c>'da ayrı bir kapı taşıyordu — bkz. bu sınıfın özeti). Uygulamada özel
    /// <c>MenuItem</c> şablonu yoktur; WPF'in varsayılan şablonu pasif maddeyi gri çizer.
    /// <para>[Stop now] Maddenin başlığı Stop'un üç aşamasını izler (<see cref="StopText.Label"/>: Stop → Stop now →
    /// Terminating…): Stopping'de ikinci basış hard stop olduğu için "Stop" demek basışın artık zararsız olmadığını gizlerdi.
    /// Durum <paramref name="run"/>'ın <see cref="RunViewModel.StopLabel"/>'ından gelir — Stop düğmesiyle ve satır ikonuyla
    /// AYNI kaynak; <c>null</c> ise (arkasında koşu olmayan tepsi) başlık ilk aşamada kalır. Abonelik menüyle aynı ömürdedir
    /// (menü uygulama boyunca yaşar), ayrıca çözülmez.</para></summary>
    internal static ContextMenu CreateMenu(ICommand stop, Action exit, RunViewModel? run = null)
    {
        var stopItem = new MenuItem { Header = StopText.Label(run?.StopStage ?? StopStage.Stop), Command = stop };
        if (run is not null)
            run.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RunViewModel.StopLabel)) stopItem.Header = run.StopLabel;
            };
        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => exit();

        var menu = new ContextMenu();
        menu.Items.Add(stopItem);
        menu.Items.Add(exitItem);
        return menu;
    }

    /// <summary>Gömülü uygulama ikonundan (<see cref="AppIdentity.AppIconUri"/> — adres TEK kaynaktan, pencere
    /// ikonu da onu okur) istenen kareyi çözer. <c>System.Drawing.Icon</c> veriyi ctor'da kendi içine kopyalar,
    /// bu yüzden akış hemen bırakılabilir.</summary>
    internal static System.Drawing.Icon LoadBalloonIcon()
    {
        var resource = Application.GetResourceStream(new Uri(AppIdentity.AppIconUri))
            ?? throw new InvalidOperationException(
                $"The application icon resource was not found: {AppIdentity.AppIconUri}");
        using var stream = resource.Stream;
        return new System.Drawing.Icon(stream, new System.Drawing.Size(BalloonIconPx, BalloonIconPx));
    }

    /// <summary>Tepsi ikonuna sol tık / çift tık / balloon tıkı — pencereyi geri getir.</summary>
    public event Action? RestoreRequested;
    /// <summary>Tepsi menüsü → Exit: güvenli tam çıkış — uçuştaki iş beklenir, sonra uygulama kapanır
    /// (<c>MainWindow.ExitFromTray</c> → <c>RunViewModel.RequestExit</c>).</summary>
    public event Action? ExitRequested;

    /// <summary>
    /// [K5] YALNIZ ilk `X` kapatmasında: uygulamanın tepside çalışmaya devam ettiğini OS balloon'u ile bildirir.
    /// Uygulama İÇİ toast design §8'de yasaktır — bu bilinçli olarak işletim sisteminin bildirimidir.
    /// <para>[P3 · Task 4] Kapı: <c>MainWindow.MinimizeToTray</c>, <c>FirstCloseBalloonGate.ClaimShow()</c> true
    /// dönünce buraya gelir — o kapı hem "ilk mi" hem Show notifications açık mı'yı birlikte sorar.</para>
    /// </summary>
    public void ShowClosedToTrayNotification() => _icon.ShowNotification(
        title: AppIdentity.Product,
        message: "Still running in the tray. Right-click the tray icon and choose Exit to quit.",
        icon: NotificationIcon.Info);

    /// <summary>[E2/triaj-f] Genel OS tray balloon'u — ikinci instance mevcut pencereyi öne getiremediğinde
    /// (SESSİZ kalmamak için) tek-satırlık bilgilendirme gösterir. Uygulama-içi toast değil (design §8 yasağı) —
    /// bilinçli olarak OS bildirimi.
    /// <para>[P3 · Task 4] Kapı: <c>App.OnStartup</c>, <c>SecondInstanceGate.Decide</c>'in <c>ShowBalloon</c>'u
    /// true dönünce buraya gelir — ayrışan çıkış kodu (3) balloon bastırılsa bile korunur, yalnız bu çağrı
    /// düşer.</para></summary>
    public void ShowNotification(string title, string message) =>
        _icon.ShowNotification(title: title, message: message, icon: NotificationIcon.Warning);

    /// <summary>
    /// [tray indicator/K-5] Uygulama TEPSİDEYKEN biten bir koşunun sonucu.
    ///
    /// <para><paramref name="line"/> yeniden derlenmez — şeridin o anki terminal satırının TA KENDİSİDİR
    /// (<c>RunViewModel.RibbonLine</c>). Kullanıcı pencereyi açtığında şeritte aynı cümleyi görür; iki yüzey
    /// aynı şeyi söylemek zorundadır. Bildirim o satırı yalnız İKİYE AYIRIR: başı başlık, geri kalanı gövde.</para>
    ///
    /// <para>Neden <see cref="ShowNotification"/> yeniden kullanılmıyor: o Warning ikonuna SABİTLENMİŞTİR ve
    /// kendi çağıranı (ikinci instance uyarısı) vardır; onu parametreleştirmek mevcut davranışı değiştirirdi.
    /// Burada ikon HER sonuçta ürünün kendi (büyük) ikonudur — bir OS glyph'i sonucu zaten taşımaz, sonuç
    /// başlıktaki sözcüktedir ("Completed" / "▸ Stopped" / "Run failed").</para>
    /// <para>[P3 · Task 4] Kapı: <c>TrayBuildIndicatorController</c>, balonun TAM gösterileceği anda
    /// <c>notificationsOn()</c>'ı TAZE okur ve yalnız açıksa buraya gelir.</para></summary>
    public void ShowRunFinished(RibbonLine line) => _icon.ShowNotification(
        title: RunFinishedTitle(line),
        message: RunFinishedBody(line),
        icon: NotificationIcon.None,           // yerini büyük ürün ikonu alır
        customIconHandle: _largeIcon.Handle,
        largeIcon: true);

    /// <summary>Balloon başlığı = satırın BAŞI (<c>"Completed"</c>, <c>"Run failed"</c>); ayırıcı taşımayan bir
    /// satırda ürün adı. Ayrı ve saf: gerçek bir tepsi ikonu kurmadan sınanabilsin diye (<c>TaskbarIcon</c>
    /// headless süitte kurulamaz).</summary>
    internal static string RunFinishedTitle(RibbonLine line) => line.Head ?? AppIdentity.Product;

    /// <summary>Balloon gövdesi = satırın GERİ KALANI; ayırıcı taşımayan bir satırda satırın tamamı (ürün
    /// adının altında). Bkz. <see cref="RunFinishedTitle"/>.
    /// <para>Son <c>?? ""</c> bir SAVUNMA TABANIdır, ölü dal değil: teslim kutusu (<c>SetTerminalLine</c>)
    /// artık bir <c>RibbonLine</c> ve varsayılanı <c>default</c>, yani hiç satır itilmemişken <c>Text</c>
    /// <c>null</c>'dır. Kutu eskiden <c>""</c> ile başlıyordu; imza değişirken taban düşmez.</para></summary>
    internal static string RunFinishedBody(RibbonLine line) => line.Detail ?? line.Text ?? "";

    /// <summary>
    /// [perf B2] Uygulama TEPSİDEYKEN basılan ama komutun kapısı kapalı olduğu için yok sayılan Build kısayolunun
    /// NEDENİ. İş sürerken koşu komutları kapalıdır ve kısayol kuyruğa girmez; pencere gizliyken ekran da yoktur,
    /// yani kısayol sessizce hiçbir şey yapmazsa kullanıcı neden başlamadığını bilemez — balon tek yüzeydir.
    /// Pencere görünürken balon YOKTUR (ekran zaten söylüyor: Sync düğmesi meşgul, şerit). İkon Info: hata değil,
    /// durum bilgisi; başlık ürünün adı.
    /// <para>Kapı: <c>MainWindow.OnGlobalHotkey</c>, balonun TAM gösterileceği anda Show notifications'ı TAZE okur
    /// ve yalnız açıksa buraya gelir (<see cref="ShowClosedToTrayNotification"/> ile aynı kural).</para></summary>
    public void ShowBuildIgnored(string reason) => _icon.ShowNotification(
        title: AppIdentity.Product,
        message: BuildIgnoredBody(reason),
        icon: NotificationIcon.Info);

    /// <summary>Yok sayılan Build kısayolunun balon gövdesi — metin TEK yerde (kopya YASAK; testler de buradan okur).
    /// <paramref name="reason"/> <c>RunViewModel.WhyRunCannotStart()</c>'ın kısa cümlesidir. Gövde nedeni söyler ve
    /// orada biter: "tekrar dene" ipucu YOKTUR — nedenlerin bir kısmı kendiliğinden bitmez (motor erişilemiyor, proje
    /// listesi yok) ve ipucu orada yanlış yönlendirirdi; bitişi söyleyen neden bunu kendi cümlesinde taşır. Ayrı ve saf:
    /// gerçek bir tepsi ikonu kurmadan sınanabilsin diye (<c>TaskbarIcon</c> headless süitte kurulamaz).</summary>
    internal static string BuildIgnoredBody(string reason) => $"Build not started — {reason}.";

    public void Dispose()
    {
        _icon.Dispose();
        _largeIcon.Dispose();
    }
}
