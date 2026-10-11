using BuildOrchestrator.App.Shell;
using H.NotifyIcon.Interop;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [tray indicator/3. tur → ekran ölçeği] Bitiş bildiriminin ÖZEL İKONU.
///
/// <para><b>Neden ayrı bir pin:</b> <c>H.NotifyIcon</c> özel balloon ikonunun ölçüsünü gösterim anında DOĞRULAR ve
/// istediği ölçüden başka her şeyi <see cref="InvalidOperationException"/> ile reddeder
/// (<c>"Custom icon must be {Width=…, Height=…}"</c>). Bu kural hiçbir derleme hatası vermez: yanlış ölçüdeki ikon
/// sessizce kabul edilir, patlama ancak balloon GÖSTERİLİRKEN olur — ve üretimde o çağrı beklenmeyen bir <c>Task</c>'in
/// içindedir, yani istisna yutulur. Sonuç: <b>koşu biter, hiçbir bildirim çıkmaz ve hiçbir yerde iz kalmaz.</b></para>
///
/// <para><b>Eski iddia ve neden değişti:</b> bu sınıf eskiden "API yalnız 32×32 kabul eder" diye 32'yi pinliyordu
/// (48×48 yüklenip reddedildiği turda yazılmıştı). Ölçüm bunun yalnız %100 ekran ölçeğinde doğru olduğunu gösterdi:
/// istenen ölçü <c>GetSystemMetrics(SM_CXICON)</c>'dur ve ekran ölçeğiyle büyür. %125'te (AppliedDPI 120) gerçek
/// tepsiyle koşan sonda (<c>TrayBalloonProbeTests</c>) <c>Custom icon must be {Width=40, Height=40}. Current size:
/// {Width=32, Height=32}</c> ile patladı — kullanıcı tepsideyken biten koşuda bildirim görmedi. Sabit 32 pini %100'de
/// yeşil kalıp kusuru gizliyordu.</para>
///
/// <para><b>Ölçü nereden okunur:</b> beklenen ölçü kütüphanenin KENDİ public kuralıdır
/// (<see cref="IconUtilities.GetRequiredCustomIconSize"/>) — ikinci bir kural yazılmaz. Gerçek ölçü HICON'un kendi
/// bitmap'inden okunur (kütüphane de HICON'u ölçer), <c>System.Drawing.Icon</c>'a İSTENEN ölçüden değil.</para>
/// </summary>
public class TrayBalloonIconTests
{
    /// <summary>Bu makinenin ekran ölçeğinde yüklenen ikon, kütüphanenin istediği ölçüdedir. %100'de beklenen de 32
    /// olduğu için CI'da ayırt edici değildir; ölçeği %100 olmayan makinede (lokal süit kapıdır) kusuru yakalar.</summary>
    [StaFact]
    public void The_balloon_icon_is_the_size_the_notification_api_demands_on_this_screen()
    {
        // `pack://` şemasını WPF, `PackUriHelper`'ın statik ctor'unda kaydeder. Üretimde bunu `Application`
        // kurulurken biri yapar; headless süitte hiçbir Application yoktur, bu yüzden şemaya ilk dokunan
        // testin kendisi olmalıdır — yoksa adres "Invalid port specified" ile reddedilir ve test ölçmek
        // istediği şeyi değil, kendi ortamını raporlar.
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;

        using var icon = AppTrayIcon.LoadBalloonIcon();

        Assert.Equal(IconUtilities.GetRequiredCustomIconSize(largeIcon: true), PixelSize(icon));
    }

    /// <summary>Ekran ölçeğinden BAĞIMSIZ kapsam: kütüphanenin bir ölçekte isteyebileceği ölçüler (%100 → 32, %125 → 40,
    /// %150 → 48 … %300 → 96). ICO'da kare olarak bulunan (32, 48) ve bulunmayan ölçüler birlikte denenir — %100'de koşan
    /// CI da kullanıcının %125'ini sınar.
    /// <para>Ölçüldü: <c>System.Drawing.Icon</c>'a 40 istemek 32×32 bir HICON verir — ICO'daki en yakın kareyi olduğu gibi
    /// seçer, ölçeklemez. Yani istenen ölçüyü yalnız İLETMEK düzeltme değildir; kare o ölçüye örneklenmelidir.</para></summary>
    [StaTheory]
    [InlineData(32)]
    [InlineData(40)]
    [InlineData(48)]
    [InlineData(56)]
    [InlineData(64)]
    [InlineData(72)]
    [InlineData(80)]
    [InlineData(96)]
    public void The_balloon_icon_loads_at_every_size_a_screen_scale_can_demand(int px)
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        var size = new System.Drawing.Size(px, px);

        using var icon = AppTrayIcon.LoadBalloonIcon(size);

        Assert.Equal(size, PixelSize(icon));
    }

    /// <summary>HICON'un GERÇEK piksel ölçüsü. <c>FromHandle</c> ölçüyü handle'ın kendi bitmap'inden okur ve handle'ın
    /// sahibi olmaz (Dispose onu yok etmez).</summary>
    private static System.Drawing.Size PixelSize(System.Drawing.Icon icon)
    {
        using var view = System.Drawing.Icon.FromHandle(icon.Handle);
        return view.Size;
    }
}
