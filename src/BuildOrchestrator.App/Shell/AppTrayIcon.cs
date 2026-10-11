using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using H.NotifyIcon.Interop;

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
/// <para><b>Balloon ikonu ayrıdır:</b> koşu sonucu bildirimi <c>app-icon.ico</c>'dan, kütüphanenin ekran ölçeğine
/// göre istediği BÜYÜK ölçüde yüklenen ikonu taşır (<see cref="LoadBalloonIcon()"/>). Bildirim penceresi tepsi
/// kutucuğundan çok daha geniştir; oraya 16px raster koymak ikonu bulanıklaştırır.</para>
/// </summary>
internal sealed class AppTrayIcon : IDisposable, ITrayRunNotifier
{
    private const string IconUri = "pack://application:,,,/BuildOrchestrator.App;component/Assets/tray-icon-16.ico";

    private readonly TaskbarIcon _icon;

    /// <summary>Bildirimin büyük ikonu — ilk bitiş bildiriminde bir KEZ yüklenir ve handle'ı her balloon'a verilir; her
    /// bildirimde yeniden çözmek gereksiz GDI nesnesi üretirdi. Kurulumda değil ilk bildirimde yüklenir: yükleme düşerse
    /// tepsi ikonu (ve uygulamanın açılışı) değil yalnız o bildirimin ikonu düşer (<see cref="ShowWithIconFallback"/>).
    /// <see cref="Dispose"/> bırakır.</summary>
    private System.Drawing.Icon? _largeIcon;

    /// <summary>Hiçbir zaman çalıştırılamayan Stop komutu — arkasında RunViewModel/engine olmayan bir tepsi
    /// içindir (ör. ikinci instance'ın geçici balloon tray'i). Kopya YASAK: aynı <c>RelayCommand(() => { },
    /// () => false)</c> eskiden App.xaml.cs'te ve test tarafında ayrı ayrı yazılıyordu; artık TEK örnek
    /// buradadır. Statik/paylaşılan TEK örnek güvenlidir: WPF'in <c>MenuItem</c>'ı <c>ICommand.CanExecuteChanged</c>'e
    /// zayıf bir event manager üzerinden abone olur.</summary>
    internal static readonly ICommand NoRunToStop = new RelayCommand(() => { }, () => false);

    public AppTrayIcon(ICommand stopCommand, RunViewModel? run = null)
    {
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
    /// Terminating…): Stopping'de basış hard stop olduğu için "Stop" demek basışın artık zararsız olmadığını gizlerdi.
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

    /// <summary>
    /// Bildirimin ürün ikonu, kütüphanenin O AN istediği ölçüde. Ölçü bir tercih DEĞİL, API'nin ŞARTIDIR:
    /// <c>H.NotifyIcon</c> özel balloon ikonunu gösterim anında ölçer ve <c>GetSystemMetrics(SM_CXICON)</c>'dan başka her
    /// ölçüyü <see cref="InvalidOperationException"/> ile reddeder — o ölçü ekran ölçeğiyle büyür (%100 → 32, %125 → 40,
    /// %150 → 48). Kural kütüphanenin KENDİ public metodundan okunur (<see cref="IconUtilities.GetRequiredCustomIconSize"/>);
    /// ikinci bir ölçü kuralı YAZILMAZ — eskiden burada sabit 32 vardı ve %125'te her bitiş bildirimi sessizce düşüyordu.
    /// Pin: <c>TrayBalloonIconTests</c>.
    /// </summary>
    internal static System.Drawing.Icon LoadBalloonIcon() =>
        LoadBalloonIcon(IconUtilities.GetRequiredCustomIconSize(largeIcon: true));

    /// <summary>
    /// Gömülü uygulama ikonunu (<see cref="AppIdentity.AppIconUri"/> — adres TEK kaynaktan, pencere ikonu da onu okur)
    /// TAM <paramref name="size"/> ölçüsünde bir HICON olarak yükler.
    ///
    /// <para><b>ICO o ölçüde bir kare taşıyorsa</b> (32, 48) kare olduğu gibi kullanılır: o ölçüde ayrıca üretilmiş kare,
    /// büyük karenin küçültülmüşünden nettir. <b>Taşımıyorsa</b> (ör. %125'in 40'ı) en büyük kare o ölçüye örneklenir.
    /// Ölçüm: <c>System.Drawing.Icon</c> ÖLÇEKLEMEZ — istenen ölçüye en yakın kareyi olduğu gibi seçer (40 isteyene 32
    /// verir), yani ölçüyü yalnız iletmek kütüphanenin reddini değiştirmez.</para>
    ///
    /// <para>Sonuç her iki yolda da bir ICO akışından <c>System.Drawing.Icon</c>'a verilir: HICON'un sahibi o olur
    /// (<see cref="System.Drawing.Icon.Dispose()"/> yok eder) ve veriyi ctor'da kendi içine kopyaladığı için akış hemen
    /// bırakılabilir.</para>
    /// </summary>
    internal static System.Drawing.Icon LoadBalloonIcon(System.Drawing.Size size)
    {
        byte[] ico = ReadAppIcon();
        using (var source = new MemoryStream(ico))
        {
            var frames = new IconBitmapDecoder(
                source, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames;
            if (!frames.Any(f => f.PixelWidth == size.Width && f.PixelHeight == size.Height))
                ico = SingleFrameIco(Resample(frames.MaxBy(f => f.PixelWidth)!, size));
        }

        using var stream = new MemoryStream(ico);
        return new System.Drawing.Icon(stream, size);
    }

    private static byte[] ReadAppIcon()
    {
        var resource = Application.GetResourceStream(new Uri(AppIdentity.AppIconUri))
            ?? throw new InvalidOperationException(
                $"The application icon resource was not found: {AppIdentity.AppIconUri}");
        using var stream = resource.Stream;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Kareyi <paramref name="size"/>'a örnekler. Örnekleme önceden çarpılmış (premultiplied) piksellerde
    /// yapılır: düz alfalı pikselleri ortalamak tam saydam piksellerin rengini kenarın yumuşatma piksellerine karıştırır.
    /// Sonuç ICO'nun beklediği düz alfaya geri çevrilir.</summary>
    private static BitmapSource Resample(BitmapSource frame, System.Drawing.Size size)
    {
        var premultiplied = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
        var scaled = new TransformedBitmap(premultiplied, new ScaleTransform(
            (double)size.Width / frame.PixelWidth, (double)size.Height / frame.PixelHeight));
        return new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
    }

    /// <summary>Tek kareli, PNG gövdeli bir ICO (Vista'dan beri ICO karesi PNG taşıyabilir). Dizin ICO biçiminin
    /// kendisidir: 6 baytlık başlık + 16 baytlık tek giriş, ardından gövde; sayılar little-endian (BinaryWriter da
    /// öyle yazar).</summary>
    private static byte[] SingleFrameIco(BitmapSource frame)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var png = new MemoryStream();
        encoder.Save(png);

        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0);                 // ayrılmış
            writer.Write((ushort)1);                 // tür: ikon
            writer.Write((ushort)1);                 // kare sayısı
            writer.Write((byte)frame.PixelWidth);    // ICO ölçüsü tek bayttır: 256 → 0 (balon ölçüleri çok altında)
            writer.Write((byte)frame.PixelHeight);
            writer.Write((byte)0);                   // palet yok
            writer.Write((byte)0);                   // ayrılmış
            writer.Write((ushort)1);                 // düzlem
            writer.Write((ushort)32);                // piksel başına bit
            writer.Write((uint)png.Length);          // gövde uzunluğu
            writer.Write((uint)(6 + 16));            // gövdenin konumu: başlık + tek giriş
        }
        png.WriteTo(ico);
        return ico.ToArray();
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
    /// <c>notificationsOn()</c>'ı TAZE okur ve yalnız açıksa buraya gelir.</para>
    /// <para>Ürün ikonu düşerse (yüklenemez ya da kütüphane reddeder) bildirim aynı metinle ikonsuz gösterilir
    /// (<see cref="ShowWithIconFallback"/>): koşu sonucu bir ikon yüzünden kaybolmaz.</para></summary>
    public void ShowRunFinished(RibbonLine line) => ShowWithIconFallback(
        withProductIcon: () => ShowRunFinishedWithProductIcon(line),
        withoutIcon: () => _icon.ShowNotification(
            title: RunFinishedTitle(line),
            message: RunFinishedBody(line),
            icon: NotificationIcon.None));

    /// <summary>Bitiş bildiriminin ürün ikonlu denemesi — YEDEKSİZ: ikon yüklenemezse ya da kütüphane reddederse fırlatır.
    /// Üretim onu yalnız <see cref="ShowRunFinished"/>'in yedek yolu içinden çağırır; sonda (<c>TrayBalloonProbeTests</c>)
    /// doğrudan çağırır ki ürün ikonunun reddi yedek yolun arkasında görünmez kalmasın.</summary>
    internal void ShowRunFinishedWithProductIcon(RibbonLine line) => _icon.ShowNotification(
        title: RunFinishedTitle(line),
        message: RunFinishedBody(line),
        icon: NotificationIcon.None,           // yerini büyük ürün ikonu alır
        customIconHandle: (_largeIcon ??= LoadBalloonIcon()).Handle,
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
    /// [yedek yol] Bildirimi önce ürün ikonuyla dener; o deneme HANGİ sebeple düşerse düşsün bildirimi ikonsuz gösterir.
    /// İkon süstür, bildirimin kendisi değildir: ürün ikonu yolu bildirimi iki kez sessizce yuttu (önce 48×48, sonra
    /// sabit 32'nin %125'te reddi) ve ikisinde de kullanıcı koşu sonucunu hiç görmedi.
    /// <para>Deneme yolunun gerçek hataları tek tipte değildir — kütüphanenin ölçü reddi ve <c>Shell_NotifyIcon</c> reddi
    /// <see cref="InvalidOperationException"/>, HICON'un okunamaması <c>COMException</c>, ikon dosyasının çözülememesi
    /// WIC'in kendi istisnaları — bu yüzden her hata yakalanır. İlk denemenin hatası gözlem noktasına yazılır
    /// ([console pump] deseni); ikonsuz gösterim de düşerse ONUN hatası çağırana (<c>TrayBuildIndicatorController</c>)
    /// ulaşır — kalıcı bir arıza yedek yolda kaybolmaz.</para>
    /// <para>Ayrı ve saf: gerçek bir tepsi ikonu kurmadan sınanabilsin diye (<c>TaskbarIcon</c> headless süitte
    /// kurulamaz). Pin: <c>TrayBalloonIconTests</c>.</para>
    /// </summary>
    internal static void ShowWithIconFallback(Action withProductIcon, Action withoutIcon)
    {
        try
        {
            withProductIcon();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[tray balloon] product icon failed, shown without it: {ex}");
            withoutIcon();
        }
    }

    /// <summary>
    /// [perf B2] Uygulama TEPSİDEYKEN basılan ama komutun kapısı kapalı olduğu için yok sayılan Build kısayolunun
    /// NEDENİ. İş sürerken koşu komutları kapalıdır ve kısayol kuyruğa girmez; pencere gizliyken ekran da yoktur,
    /// yani kısayol sessizce hiçbir şey yapmazsa kullanıcı neden başlamadığını bilemez — balon tek yüzeydir.
    /// Pencere görünürken balon YOKTUR (ekran zaten söylüyor: Sync düğmesi meşgul — sessiz Sync dahil; şeridi yalnız görünür
    /// bir Sync <c>Syncing</c> diye yazar, sessiz Sync şeride dokunmaz). İkon Info: hata değil,
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
        _largeIcon?.Dispose();
    }
}
