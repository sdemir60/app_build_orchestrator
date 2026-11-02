using System.Windows.Media;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
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

        using var icon = AppTrayIcon.LoadBalloonIcon(BalloonBadge.Stopped);

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

        using var icon = AppTrayIcon.LoadBalloonIcon(size, BalloonBadge.Stopped, DsResources.NewScope());

        Assert.Equal(size, PixelSize(icon));
    }

    /// <summary>[yedek yol] Ürün ikonlu deneme HANGİ sebeple düşerse düşsün bildirim ikonsuz yine gösterilir — ikon süstür,
    /// bildirimin kendisi değildir. Denenen sebepler deneme yolunun gerçek hatalarıdır: kütüphanenin ölçü reddi ve
    /// <c>Shell_NotifyIcon</c> reddi (<see cref="InvalidOperationException"/>), HICON'un okunamaması (<c>GetIconInfo</c> →
    /// <see cref="System.Runtime.InteropServices.COMException"/>), ikon dosyasının çözülememesi
    /// (<see cref="System.IO.FileFormatException"/>).</summary>
    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(System.Runtime.InteropServices.COMException))]
    [InlineData(typeof(System.IO.FileFormatException))]
    public void A_failed_product_icon_still_shows_the_notification_without_it(Type failure)
    {
        int withoutIcon = 0;

        AppTrayIcon.ShowWithIconFallback(
            withProductIcon: () => throw (Exception)Activator.CreateInstance(failure)!,
            withoutIcon: () => withoutIcon++);

        Assert.Equal(1, withoutIcon);
    }

    /// <summary>Ürün ikonlu deneme geçerse ikinci bir balon YOKTUR.</summary>
    [Fact]
    public void An_accepted_product_icon_shows_the_notification_once()
    {
        int withIcon = 0, withoutIcon = 0;

        AppTrayIcon.ShowWithIconFallback(withProductIcon: () => withIcon++, withoutIcon: () => withoutIcon++);

        Assert.Equal((1, 0), (withIcon, withoutIcon));
    }

    /// <summary>Yedek yol hatayı YUTMAZ: ikonsuz gösterim de düşerse onun hatası çağırana ulaşır
    /// (<c>TrayBuildIndicatorController</c> onu gözlem noktasına yazar) — kalıcı bir arıza sessizce kaybolmaz.</summary>
    [Fact]
    public void A_notification_that_fails_without_the_icon_too_reaches_the_caller()
    {
        Assert.Throws<System.Runtime.InteropServices.COMException>(() => AppTrayIcon.ShowWithIconFallback(
            withProductIcon: () => throw new InvalidOperationException("Custom icon must be {Width=40, Height=40}."),
            withoutIcon: () => throw new System.Runtime.InteropServices.COMException("Show notification failed.")));
    }

    /// <summary>
    /// [kullanıcı kararı 2026-11-02] Bildirimin ikonu SONUCU taşır: ürün ikonunun sağ alt köşesinde dolu bir rozet — başarı yeşil, hata
    /// kırmızı, durdurulan koşu gri. Windows tepsi bildiriminin metni renklendirilemez; sonucu okumadan gösterebilen tek yüzey ikondur.
    /// Rozet şeridin terminal satırının glyph'inden okunur (başlığı ve gövdeyi veren AYNI satır).
    /// <para>Örnek nokta rozetin dolu alanıdır: dairenin merkezinin altında, işaretin dışında. Beklenen renk token sözlüğünden okunur,
    /// ürünün kendi hesabından değil. Ölçeğin istediği ölçüler (32 · 40 · 48) birlikte denenir. Ürün ikonu yerinde kalır: sol üst
    /// çeyrek düz ürün karesiyle aynıdır.</para>
    /// </summary>
    [StaTheory]
    [InlineData("succeeded", "Brush.StatusSuccess", 32)]
    [InlineData("failed", "Brush.StatusFail", 32)]
    [InlineData(null, "Brush.StatusSkipped", 32)]
    [InlineData("succeeded", "Brush.StatusSuccess", 48)]
    [InlineData("failed", "Brush.StatusFail", 40)]
    [InlineData(null, "Brush.StatusSkipped", 40)]
    public void The_run_result_icon_wears_the_result_colour_on_its_corner(string? glyph, string fillKey, int px)
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        var scope = DsResources.NewScope();
        var size = new System.Drawing.Size(px, px);
        var line = new RibbonLine("Completed — 3 succeeded · 0 skipped · 4s", "Brush.StatusSuccessText", glyph);

        using var icon = AppTrayIcon.LoadBalloonIcon(size, BalloonBadge.For(line), scope);
        using var bitmap = icon.ToBitmap();

        Assert.Equal(size, PixelSize(icon));
        var expected = ((SolidColorBrush)scope[fillKey]).Color;
        double badge = px / 2.0; // rozet ikonun yarısı; 16 birimlik kutusunda (8, 13.5) dolu alandadır
        var seen = bitmap.GetPixel((int)(px - badge + 8 * badge / 16), (int)(px - badge + 13.5 * badge / 16));
        Assert.True(Near(seen, expected, tolerance: 8), $"badge pixel {seen} is not {fillKey} {expected}");
        int quarter = px / 4;
        var product = PixelOf(AppTrayIcon.ProductFrame(size), quarter, quarter);
        var kept = bitmap.GetPixel(quarter, quarter);
        Assert.True(kept.A == product.A && Near(kept, product, tolerance: 2), $"product pixel {kept} is not {product}");
    }

    private static bool Near(System.Drawing.Color seen, Color expected, int tolerance) =>
        (seen.A == 255 || seen.A == expected.A)
        && Math.Abs(seen.R - expected.R) <= tolerance && Math.Abs(seen.G - expected.G) <= tolerance && Math.Abs(seen.B - expected.B) <= tolerance;

    /// <summary>Karenin bir pikseli, düz alfayla (BGRA).</summary>
    private static Color PixelOf(System.Windows.Media.Imaging.BitmapSource frame, int x, int y)
    {
        var straight = new System.Windows.Media.Imaging.FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var bgra = new byte[4];
        straight.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), bgra, 4, 0);
        return Color.FromArgb(bgra[3], bgra[2], bgra[1], bgra[0]);
    }

    /// <summary>HICON'un GERÇEK piksel ölçüsü. <c>FromHandle</c> ölçüyü handle'ın kendi bitmap'inden okur ve handle'ın
    /// sahibi olmaz (Dispose onu yok etmez).</summary>
    private static System.Drawing.Size PixelSize(System.Drawing.Icon icon)
    {
        using var view = System.Drawing.Icon.FromHandle(icon.Handle);
        return view.Size;
    }
}
