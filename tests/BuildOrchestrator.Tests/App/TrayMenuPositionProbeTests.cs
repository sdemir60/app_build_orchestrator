using System.Globalization;
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
/// tercihine göre değişir). Görev çubuğundaki tık da aynı kuralla açılır ve görev çubuğunun önünde çizilir
/// (<see cref="The_tray_menu_opens_at_the_click_on_the_taskbar_in_front_of_it"/>).</para>
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

    /// <summary>Fare ölçüm sırasında kıpırdarsa deneme tekrarlanır: menü imlecin AÇILIŞ ANINDAKİ yerinde açılır (WPF imleci menünün ilk
    /// yerleşim turunda okur), kıpırdayan bir imleçle tık noktası belirsizdir — ölçüldü: koşu sırasında oynayan fare 34 px'lik sahte bir
    /// kırmızı üretti. Her denemede kıpırdarsa sonda atlanır; sonuç uydurulmaz.</summary>
    private const int Attempts = 5;

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

        var probe = StaThread.RunAsync(() => RightClickAfterScaleChange(staleRatio), "tray-menu-position").GetAwaiter().GetResult();
        output.WriteLine(probe.Report);

        Skip.If(probe.MouseKeptMoving, "The mouse moved during every attempt — keep it still while the probe runs.");
        Assert.True(probe.Distance <= Tolerance, probe.Report);
    }

    /// <summary>
    /// [tray menu at the click] Tepsi ikonuna sağ tıkta imleç görev çubuğunun ÜSTÜNDEDİR; menü yine tık noktasında açılır — bir köşesi tık
    /// noktasındadır — ve görev çubuğuyla çakışan kısmı görev çubuğunun ÖNÜNDE çizilir.
    ///
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-11-09]</b> ESKİ İDDİA (v1.12.1): görev çubuğundaki tıkta menü görev çubuğuna hiç
    /// binmez — açılış noktası çalışma alanına çekilir, menünün alt kenarı görev çubuğunun üst kenarında açılır. GEREKÇE: kullanıcı menünün tık
    /// noktasında açılmasını istedi ("tıkladığım noktadan çıksın, yalnız arkasında kalmasın"). Gerçek bir sağ tıkla ölçüldü (%125, tek ekran,
    /// görünür tepsi ikonu): tık noktasında açılan menü görev çubuğunun önünde çiziliyor, çakışan noktada en üstteki pencere menü ve Exit
    /// görev çubuğunun üstünde tam görünüyor (ekran görüntüsü).</para>
    ///
    /// <para><b>Sonda ne kurar:</b> imleci birincil ekranın görev çubuğuna (dikeyde ortası, yatayda tepsi tarafı) taşır, kabuğun sağ tık
    /// mesajını gerçek tepsi ikonuna gönderir ve açılan menünün penceresini ölçer. Menü çizilince — katmanlı pencere açılış geçişinde saydamdır
    /// ve o sürede tıklama altına geçer, bu yüzden önce menünün görev çubuğu dışındaki kısmının isabet aldığı beklenir — görev çubuğuyla
    /// çakışan bir noktadaki en üst pencereye bakar ve imleci eski yerine koyar. Görev çubuğu birincil ekranın altında değilse (gizlenen
    /// görev çubuğu, başka kenar) sonda atlanır.</para>
    /// </summary>
    [SkippableFact]
    public void The_tray_menu_opens_at_the_click_on_the_taskbar_in_front_of_it()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("BO_PROBE_TRAY") == "1",
            "Creates a real tray icon, moves the mouse onto the taskbar and opens the menu there — opt in with BO_PROBE_TRAY=1.");

        var probe = StaThread.RunAsync(() => RightClickOnTheTaskbar(), "tray-menu-taskbar").GetAwaiter().GetResult();
        output.WriteLine(probe.Report);

        Skip.If(probe.SkipReason is not null, probe.SkipReason);
        Skip.If(probe.MouseKeptMoving, "The mouse moved during every attempt — keep it still while the probe runs.");
        Assert.True(probe.Ok, probe.Report);
    }

    private sealed record Probe(double Distance, bool MouseKeptMoving, string Report);

    private sealed record TaskbarProbe(bool Ok, bool MouseKeptMoving, string? SkipReason, string Report)
    {
        public static TaskbarProbe Skipped(string reason) => new(false, false, reason, reason);
    }

    private static TaskbarProbe RightClickOnTheTaskbar()
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack; // bkz. RightClickAfterScaleChange

        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(Win32.MonitorFromPoint(default, Win32.MONITOR_DEFAULTTOPRIMARY), ref info))
            return TaskbarProbe.Skipped("The primary screen could not be read.");
        var (screen, work) = (info.rcMonitor, info.rcWork);
        if (work.Bottom >= screen.Bottom)
            return TaskbarProbe.Skipped("The taskbar is not docked at the bottom of the primary screen (hidden or on another edge).");
        var click = new Win32.POINT { X = work.Right - 160, Y = (work.Bottom + screen.Bottom) / 2 };

        using var tray = new AppTrayIcon(AppTrayIcon.NoRunToStop);
        TaskbarIcon icon = tray.IconForTest;
        ContextMenu menu = icon.ContextMenu ?? throw new InvalidOperationException("The tray icon has no menu.");
        GetCursorPos(out Win32.POINT before);
        Opened? opened;
        try
        {
            int attempt = 0;
            do
            {
                SetCursorPos(click.X, click.Y);
                opened = OpenOnce(icon, menu, (popup, r) => InFrontOfTheTaskbar(popup, r, work));
            }
            while (opened is { MouseMoved: true } && ++attempt < Attempts);
        }
        finally
        {
            SetCursorPos(before.X, before.Y); // imleç kullanıcınındır: sonda onu bulduğu yere bırakır
        }

        string where = string.Create(CultureInfo.InvariantCulture,
            $"screen ({screen.Left},{screen.Top})-({screen.Right},{screen.Bottom}), work area ({work.Left},{work.Top})-({work.Right},{work.Bottom}), " +
            $"click ({click.X},{click.Y})");
        if (opened is null) return new TaskbarProbe(false, false, null, $"The tray menu did not open ({where}).");

        var m = opened.Menu;
        double corner = NearestCorner(m, click);
        bool inFront = opened.WhileOpen == InFront;
        return new TaskbarProbe(corner <= Tolerance && inFront, opened.MouseMoved, null, string.Create(CultureInfo.InvariantCulture,
            $"{where}, menu ({m.Left},{m.Top})-({m.Right},{m.Bottom}): nearest corner {corner:F0} px from the click, " +
            $"{opened.WhileOpen}{(opened.MouseMoved ? $", the mouse moved to ({opened.AfterOpen.X},{opened.AfterOpen.Y})" : "")}"));
    }

    private const string InFront = "in front of the taskbar where they overlap";

    /// <summary>Menü çizilince görev çubuğuyla çakışan bir noktada en üstteki pencere menü mü. Menü görev çubuğuna hiç binmiyorsa ya da
    /// hiç çizilmiyorsa bunu söyler.</summary>
    private static string InFrontOfTheTaskbar(nint popup, Win32.RECT m, Win32.RECT work)
    {
        if (m.Bottom <= work.Bottom) return "not over the taskbar at all";
        var clear = new Win32.POINT { X = (m.Left + m.Right) / 2, Y = Math.Max(m.Top + 4, work.Bottom - 8) };
        var overlap = new Win32.POINT { X = clear.X, Y = (work.Bottom + m.Bottom) / 2 };
        DispatcherPump.PumpUntil(() => TopAt(clear) == popup, TimeSpan.FromSeconds(2));
        if (TopAt(clear) != popup) return "never drawn (no hit on its part above the taskbar)";
        nint top = TopAt(overlap);
        return top == popup ? InFront : $"BEHIND window 0x{top:X} where they overlap";
    }

    private static nint TopAt(Win32.POINT p) => GetAncestor(WindowFromPoint(p), GA_ROOT);

    private static Probe RightClickAfterScaleChange(double staleRatio)
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
            var probe = RightClickOnce(icon, menu, fresh, staleRatio);
            for (int attempt = 1; attempt < Attempts && probe.MouseKeptMoving; attempt++)
                probe = RightClickOnce(icon, menu, fresh, staleRatio);
            return probe;
        }
        finally
        {
            factor.Value = fresh; // süreç geneli statik durum: sonraki testlere bayat çarpan kalmaz
        }
    }

    /// <summary>Kabuğun sağ tık mesajını bir kez gönderir, açılan menünün yerini açılış noktasına göre ölçer. İmleç mesajdan ölçüme kadar
    /// <see cref="Tolerance"/>'tan fazla kıpırdadıysa sonuç "fare kıpırdadı" diye işaretlenir.</summary>
    private static Probe RightClickOnce(TaskbarIcon icon, ContextMenu menu, double fresh, double staleRatio)
    {
        string scale = string.Create(CultureInfo.InvariantCulture, $"screen factor {fresh:F3}, cached factor {fresh * staleRatio:F3}");
        if (OpenOnce(icon, menu) is not { } opened) return new Probe(double.PositiveInfinity, false, $"The tray menu did not open ({scale}).");

        var (r, click) = (opened.Menu, opened.Click);
        double distance = NearestCorner(r, click);
        return new Probe(distance, opened.MouseMoved, string.Create(CultureInfo.InvariantCulture,
            $"{scale}, click ({click.X},{click.Y}), menu ({r.Left},{r.Top})-({r.Right},{r.Bottom}), nearest corner {distance:F0} px" +
            $"{(opened.MouseMoved ? $", the mouse moved to ({opened.AfterOpen.X},{opened.AfterOpen.Y})" : "")}"));
    }

    /// <summary>Açılan menünün penceresi (fiziksel px), mesajın taşıdığı tık noktası, ölçüm anındaki imleç, imlecin arada kıpırdayıp
    /// kıpırdamadığı ve menü açıkken yapılan ek gözlemin sonucu.</summary>
    private sealed record Opened(Win32.RECT Menu, Win32.POINT Click, Win32.POINT AfterOpen, bool MouseMoved, string? WhileOpen);

    /// <summary>Kabuğun sağ tık mesajını imlecin o anki yeriyle bir kez gönderir, açılan menünün penceresini ölçer, menü açıkken
    /// <paramref name="whileOpen"/>'ı (varsa) çağırır ve menüyü kapatır; menü açılmazsa <c>null</c>. İki sondanın ortak adımı.</summary>
    private static Opened? OpenOnce(TaskbarIcon icon, ContextMenu menu, Func<nint, Win32.RECT, string>? whileOpen = null)
    {
        try
        {
            GetCursorPos(out var click);
            Win32.SendMessage(icon.TrayIcon.WindowHandle, MessageWindow.CallbackMessageId,
                (nint)((click.Y << 16) | (click.X & 0xFFFF)), WM_RBUTTONUP);

            DispatcherPump.PumpUntil(() => PopupHandle(menu) is nint h && Win32.IsWindowVisible(h), TimeSpan.FromSeconds(2));
            DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(200)); // yerleşim bir layout turunda oturur
            GetCursorPos(out var afterOpen);

            if (PopupHandle(menu) is not nint popup || !Win32.IsWindowVisible(popup)) return null;
            Win32.GetWindowRect(popup, out var r);
            return new Opened(r, click, afterOpen, Distance(click, afterOpen) > Tolerance, whileOpen?.Invoke(popup, r));
        }
        finally
        {
            menu.IsOpen = false;
            DispatcherPump.PumpFor(TimeSpan.FromMilliseconds(100)); // popup penceresi kapanışta bir zamanlayıcıyla yıkılır
        }
    }

    private static double Distance(Win32.POINT a, Win32.POINT b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static nint? PopupHandle(ContextMenu menu) =>
        PresentationSource.FromVisual(menu) is HwndSource { Handle: not 0 } source ? source.Handle : null;

    private static double NearestCorner(Win32.RECT r, Win32.POINT p)
    {
        double best = double.PositiveInfinity;
        foreach (int x in new[] { r.Left, r.Right })
            foreach (int y in new[] { r.Top, r.Bottom })
                best = Math.Min(best, Distance(new Win32.POINT { X = x, Y = y }, p));
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Win32.POINT point);

    /// <summary>İmleci taşır — yalnız sondanın işidir (görev çubuğuna tık); uygulama imleci hiçbir zaman taşımaz, bu yüzden
    /// <see cref="Win32"/>'ye girmez.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    /// <summary>Noktadaki en üst pencere — z-sırası ve katmanlı pencerenin piksel saydamlığı dahil (fare isabetinin kendisi).</summary>
    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Win32.POINT point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    private const uint GA_ROOT = 2;
}
