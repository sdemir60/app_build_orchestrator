using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using BuildOrchestrator.App.Shell;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [tray menu position] Tepsi menüsü, ekran ölçeği (DPI) uygulama açıkken değiştikten SONRA da sağ tıkın olduğu yerde açılır.
///
/// <para><b>Kusur:</b> menüyü eskiden tepsi kütüphanesi (<c>H.NotifyIcon</c>) açıyordu. Kütüphane kabuğun verdiği tık noktasını
/// (fiziksel piksel) WPF birimine kendi SAKLADIĞI bir DPI çarpanıyla böler; çarpanı ikona İLK tıklandığında okur (uygulamayı tepsiden
/// açan sol tık dahil) ve yalnız gizli mesaj penceresine <c>WM_DPICHANGED</c> gelirse tazeler. WPF ise menünün popup penceresini her
/// açılışta yeniden kurar ve WPF birimini O ANKİ ölçekle piksele geri çevirir. Ekran ölçüsü sonradan değişince iki çarpan ayrışır ve menü
/// tıktan kayar — ölçüldü: ×1,5 bayat çarpanla 623 px, ÷1,5 ile 857 px; taze çarpanla 0 px.</para>
///
/// <para><b>Sonda ne kurar:</b> ekran değişikliğinin kendisi bir testte yapılamaz, geride bıraktığı durum yapılabilir — kütüphanenin
/// saklı çarpanı ekranla artık tutmaz (<see cref="LibraryDpiFactor"/>). Ardından kabuğun <c>NOTIFYICON_VERSION_4</c> sağ tık mesajını
/// gerçek tepsi ikonunun mesaj penceresine gönderir (wParam = imlecin yeri, lParam = <c>WM_RBUTTONUP</c>) ve açılan menünün popup
/// penceresinin ekrandaki yerini ölçer: köşelerinden biri tık noktasında olmalıdır (hangisi olduğu ekran kenarına ve Windows'un el
/// tercihine göre değişir).</para>
///
/// <para>Varsayılan koşuda ÇALIŞMAZ (<c>BO_PROBE_TRAY=1</c> yoksa <c>Skip</c>): gerçek bir tepsi ikonu kurar, imlecin yanında bir menü
/// açar ve onu öne getirir.</para>
/// </summary>
[Trait("Category", "Measurement")]
[Collection("Console UI (serial)")]
public sealed class TrayMenuPositionProbeTests(ITestOutputHelper output)
{
    /// <summary>Menü köşesi ile tık noktası arasında kabul edilen en büyük uzaklık (fiziksel px). Doğru açılan menünün köşesi tam
    /// tık noktasındadır (ölçüldü: 0 px); pay yalnız yuvarlamaya aittir — bayat çarpanın kaydırması yüzlerce pikseldir.</summary>
    private const double Tolerance = 2;

    private const int WM_RBUTTONUP = 0x0205;

    /// <param name="staleRatio">Kütüphanenin saklı çarpanının ekranın gerçek çarpanına oranı: 1,5 = ölçek KÜÇÜLDÜ (önbellek %150'de
    /// dolmuş, ekran şimdi %100), 1/1,5 = ölçek BÜYÜDÜ, 1 = ekran değişmedi — KONTROL: ölçüm doğruysa eski kod da burada geçer, yani
    /// diğer iki durumun kırmızısı ölçümden değil bayat çarpandan gelir.</param>
    [SkippableTheory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(1 / 1.5)]
    public void The_tray_menu_opens_at_the_click_after_the_display_scale_changed(double staleRatio)
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("BO_PROBE_TRAY") == "1",
            "Creates a real tray icon and opens its menu — opt in with BO_PROBE_TRAY=1.");

        var (distance, report) = StaThread.RunAsync(() => RightClickAfterScaleChange(staleRatio), "tray-menu-position")
            .GetAwaiter().GetResult();
        output.WriteLine(report);

        Assert.True(distance <= Tolerance, report);
    }

    private static (double Distance, string Report) RightClickAfterScaleChange(double staleRatio)
    {
        // `pack://` şemasını WPF `PackUriHelper`'ın statik ctor'unda kaydeder; headless süitte Application yoktur, şemaya ilk dokunan
        // sonda olmalıdır — yoksa tepsi ikonunun adresi reddedilir (TrayBalloonProbeTests ile aynı).
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;

        var factor = new LibraryDpiFactor();
        using var tray = new AppTrayIcon(AppTrayIcon.NoRunToStop);
        TaskbarIcon icon = tray.IconForTest;
        ContextMenu menu = icon.ContextMenu ?? throw new InvalidOperationException("The tray icon has no menu.");
        double fresh = factor.Value; // ilk okuma kütüphanenin önbelleğini O ANKİ ekranla doldurur
        try
        {
            factor.Value = fresh * staleRatio; // ekran değişti, kütüphanenin önbelleği değişmedi

            GetCursorPos(out var click);
            Win32.SendMessage(icon.TrayIcon.WindowHandle, MessageWindow.CallbackMessageId,
                (nint)((click.Y << 16) | (click.X & 0xFFFF)), WM_RBUTTONUP);
            GetCursorPos(out var afterClick); // menü mesajın içinde açılır; fare o arada kıpırdadıysa iki nokta da kabul

            DispatcherPump.PumpUntil(() => PopupHandle(menu) is nint h && Win32.IsWindowVisible(h), TimeSpan.FromSeconds(2));
            DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(200)); // yerleşim bir layout turunda oturur

            if (PopupHandle(menu) is not nint popup || !Win32.IsWindowVisible(popup))
                return (double.PositiveInfinity, $"The tray menu did not open (cached factor {fresh * staleRatio:F3}, screen {fresh:F3}).");

            Win32.GetWindowRect(popup, out var r);
            double distance = Math.Min(NearestCorner(r, click), NearestCorner(r, afterClick));
            return (distance, string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"screen factor {fresh:F3}, cached factor {fresh * staleRatio:F3}, click ({click.X},{click.Y}), menu " +
                $"({r.Left},{r.Top})-({r.Right},{r.Bottom}), nearest corner {distance:F0} px"));
        }
        finally
        {
            menu.IsOpen = false;
            factor.Value = fresh; // süreç geneli statik durum: sonraki testlere bayat çarpan kalmaz
        }
    }

    private static nint? PopupHandle(ContextMenu menu) =>
        PresentationSource.FromVisual(menu) is HwndSource { Handle: not 0 } source ? source.Handle : null;

    private static double NearestCorner(Win32.RECT r, POINT p)
    {
        double best = double.PositiveInfinity;
        foreach (int x in new[] { r.Left, r.Right })
            foreach (int y in new[] { r.Top, r.Bottom })
                best = Math.Min(best, Math.Sqrt(Math.Pow(x - p.X, 2) + Math.Pow(y - p.Y, 2)));
        return best;
    }

    /// <summary>
    /// <c>H.NotifyIcon</c>'un saklı DPI çarpanı (internal <c>H.NotifyIcon.DpiUtilities.DpiFactorX/Y</c>) — ekran değişikliğinin geride
    /// bıraktığı durumu kurmanın tek yolu. Kütüphane yükseltilip bu adlar değişirse sonda burada açık bir mesajla düşer.
    /// </summary>
    private sealed class LibraryDpiFactor
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly PropertyInfo _x;
        private readonly PropertyInfo _y;

        public LibraryDpiFactor()
        {
            var type = typeof(TaskbarIcon).Assembly.GetType("H.NotifyIcon.DpiUtilities")
                ?? throw new InvalidOperationException("H.NotifyIcon.DpiUtilities is gone — update the probe to the library's new DPI cache.");
            _x = type.GetProperty("DpiFactorX", Static) ?? throw new InvalidOperationException("DpiUtilities.DpiFactorX is gone.");
            _y = type.GetProperty("DpiFactorY", Static) ?? throw new InvalidOperationException("DpiUtilities.DpiFactorY is gone.");
        }

        public double Value
        {
            get => (double)_x.GetValue(null)!;
            set
            {
                _x.SetValue(null, value);
                _y.SetValue(null, value);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);
}
