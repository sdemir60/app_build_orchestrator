using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [tray indicator/3. tur] Bitiş bildiriminin ÖZEL İKONU.
///
/// <para><b>Neden ayrı bir pin:</b> <c>H.NotifyIcon</c> özel balloon ikonunun ölçüsünü çalışma anında
/// DOĞRULAR ve 32×32 dışındaki her ölçüyü <see cref="InvalidOperationException"/> ile reddeder
/// (<c>"Custom icon must be {Width=32, Height=32}"</c>). Bu kural hiçbir derleme hatası vermez: yanlış
/// ölçüdeki ikon sessizce kabul edilir, patlama ancak balloon GÖSTERİLİRKEN olur — ve üretimde o çağrı
/// beklenmeyen bir <c>Task</c>'in içindedir, yani istisna yutulur. Sonuç: <b>koşu biter, hiçbir bildirim
/// çıkmaz ve hiçbir yerde iz kalmaz.</b> Tam olarak bu yaşandı (48×48 yüklenmişti); bu test o yolu
/// headless kapatır.</para>
///
/// <para>Ölçünün ICO'da gerçekten bir kare olarak bulunduğunu <c>IconGeometryTests</c> ayrıca pinler —
/// burada yüklenen ikonun ta kendisi ölçülür, ikisi birlikte hem kaynağı hem tüketimi kapatır.</para>
/// </summary>
public class TrayBalloonIconTests
{
    [Fact]
    public void The_balloon_icon_is_the_only_size_the_notification_api_accepts()
    {
        // `pack://` şemasını WPF, `PackUriHelper`'ın statik ctor'unda kaydeder. Üretimde bunu `Application`
        // kurulurken biri yapar; headless süitte hiçbir Application yoktur, bu yüzden şemaya ilk dokunan
        // testin kendisi olmalıdır — yoksa adres "Invalid port specified" ile reddedilir ve test ölçmek
        // istediği şeyi değil, kendi ortamını raporlar.
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;

        using var icon = AppTrayIcon.LoadBalloonIcon();

        Assert.Equal(32, icon.Width);
        Assert.Equal(32, icon.Height);
    }
}
