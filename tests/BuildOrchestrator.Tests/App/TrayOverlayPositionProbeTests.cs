using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.Views;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [tray overlay position] Tepsi derleme göstergesi, ekranın ölçeği uygulama açıkken değiştikten SONRA da birincil ekranın çalışma
/// alanının sağ alt köşesinde çıkar.
///
/// <para><b>Kusur:</b> gösterge yerini WPF'in <c>SystemParameters.WorkArea</c>'sından alıyordu. O değer kabuğun verdiği fiziksel
/// pikselleri WPF birimine sürecin AÇILIŞTA okuduğu sistem DPI'ıyla böler ve o DPI süreç boyunca tazelenmez; pencerenin konumunu ise
/// WPF pencerenin O ANKİ ölçeğiyle piksele geri çevirir. Ekran ölçüsü sonradan değişince (laptop %150 ↔ 4K %100) iki çarpan ayrışır
/// ve gösterge köşeden kayar, ölçek büyüdüyse ekranın dışına düşer.</para>
///
/// <para><b>Sonda ne kurar:</b> ekran değişikliğinin kendisi bir testte yapılamaz, geride bıraktığı durum yapılabilir — WPF'in saklı
/// sistem DPI'ı ekranla artık tutmaz (<see cref="WpfSystemDpi"/>). Ardından göstergeyi ürünün kendi yoluyla gösterir
/// (<see cref="TrayBuildOverlayWindow.ShowStatic"/>) ve gerçek pencerenin ekrandaki yerini ölçer: sağ ve alt kenarı, birincil ekranın
/// çalışma alanından o ekranın ölçeğiyle büyütülmüş paylar kadar içeride, boyu o ölçekte olmalıdır. Ölçüldü (4K %100 birincil ekran):
/// eski kod önbellek %150'de kalmışken 1279 px içeride, %67'de kalmışken 1921 px dışarıda; düzeltilmiş kod ikisinde de köşede.</para>
///
/// <para><b>Test sürecinin DPI kipi:</b> uygulama PerMonitorV2 koşar (app.manifest), test süreci sistem ölçeğindedir. Bu sonda bundan
/// etkilenmez: birincil ekranın ölçeği sürecin ölçeğine eşitken iki kipte de pencere koordinatları fizikseldir ve kusuru doğuran bayat
/// çarpan iki kipte de aynı yoldan geçer. Kusurun ikinci yüzü — pencerenin kendi ölçeğinin bayatlaması (en son farklı ölçekli bir
/// ekranda gösterilmiş pencere) — yalnız uygulamanın kipinde oluşur ve test sürecinde kurulamaz: süreç kipi açılışta konmuş olur, yalnız
/// thread'i PerMonitorV2'ye almak da yetmez (WPF o karışık kipte pencerenin ölçeğini bilmez, düzeltilmiş kod da kırmızı görünür). O yüz
/// PerMonitorV2 manifestli geçici bir harness'le ölçüldü (laptop %150'de gösterilip gizlenen pencere): eski kod 1919 px ekran dışında,
/// düzeltilmiş kod köşede (1 px). Hesabın o yüzünü saf köşe testi pinler
/// (<c>TrayBuildOverlayWindowTests.Overlay_positions_into_the_bottom_right_of_a_given_work_area</c>, ekran ve pencere ölçeği ayrı).</para>
///
/// <para>Varsayılan koşuda ÇALIŞMAZ (<c>BO_MEASURE_OVERLAY=1</c> yoksa <c>Skip</c>): gerçek, Topmost bir overlay'i ekrana çıkarır.</para>
/// </summary>
[Trait("Category", "Measurement")]
[Collection("Console UI (serial)")]
public sealed class TrayOverlayPositionProbeTests(ITestOutputHelper output)
{
    /// <summary>Beklenen kenar/boy ile ölçülen arasındaki en büyük fark (fiziksel px). Pay yalnız yuvarlamaya aittir — bayat bir
    /// çarpanın kaydırması yüzlerce pikseldir.</summary>
    private const int Tolerance = 2;

    /// <summary>Gösterimden ölçüme kadar pompalanan süre: yerleşim bir iki turda oturur.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);

    /// <param name="staleRatio">WPF'in saklı sistem DPI'ının ekranın gerçek DPI'ına oranı: 1,5 = ölçek KÜÇÜLDÜ (süreç %150'de
    /// açılmış, ekran şimdi %100), 1/1,5 = ölçek BÜYÜDÜ, 1 = ekran değişmedi — KONTROL: ölçüm doğruysa eski kod da burada geçer, yani
    /// diğer iki durumun kırmızısı ölçümden değil bayat çarpandan gelir.</param>
    [SkippableTheory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(1 / 1.5)]
    public void The_overlay_lands_in_the_corner_after_the_display_scale_changed(double staleRatio)
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("BO_MEASURE_OVERLAY") == "1",
            "Shows a real Topmost overlay — opt in with BO_MEASURE_OVERLAY=1.");

        var probe = StaThread.RunAsync(() => RevealWithStaleSystemDpi(staleRatio), "tray-overlay-position").GetAwaiter().GetResult();
        output.WriteLine(probe.Report);
        Assert.True(probe.Distance <= Tolerance, probe.Report);
    }

    private sealed record Probe(int Distance, string Report);

    private static Probe RevealWithStaleSystemDpi(double staleRatio)
    {
        var dpi = new WpfSystemDpi();
        int fresh = dpi.Value; // ilk okuma WPF'in önbelleğini O ANKİ ekranla doldurur
        var overlay = new TrayBuildOverlayWindow(DsResources.NewScope());
        try
        {
            dpi.Value = (int)Math.Round(fresh * staleRatio); // ekran değişti, WPF'in önbelleği değişmedi
            overlay.ShowStatic();
            DispatcherPump.PumpFor(Settle);
            return Measure(overlay, Invariant($"WPF system DPI {fresh}, cached as {dpi.Value}"));
        }
        finally
        {
            dpi.Value = fresh; // süreç geneli statik durum: sonraki testlere bayat DPI kalmaz
            overlay.Close();
        }
    }

    private static Probe Measure(TrayBuildOverlayWindow overlay, string setup)
    {
        nint hwnd = new WindowInteropHelper(overlay).Handle;
        Win32.GetWindowRect(hwnd, out var r);
        var (work, dpi) = PrimaryScreen();
        double scale = dpi / 96.0;
        int right = work.Right - (int)Math.Round(TrayBuildOverlayWindow.RightMargin * scale);
        int bottom = work.Bottom - (int)Math.Round(TrayBuildOverlayWindow.BottomMargin * scale);
        int width = (int)Math.Round(TrayBuildOverlayWindow.OverlayWidth * scale);
        int height = (int)Math.Round(TrayBuildOverlayWindow.OverlayHeight * scale);
        int distance = new[]
        {
            Math.Abs(r.Right - right), Math.Abs(r.Bottom - bottom),
            Math.Abs(r.Right - r.Left - width), Math.Abs(r.Bottom - r.Top - height),
        }.Max();
        string area = Invariant($"primary work area ({work.Left},{work.Top})-({work.Right},{work.Bottom}) at {dpi} DPI");
        string seen = Invariant(
            $"overlay ({r.Left},{r.Top})-({r.Right},{r.Bottom}), expected right {right} bottom {bottom} size {width}x{height}, off by {distance} px");
        return new Probe(distance, $"{setup}: {area}, {seen}");
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>Birincil ekranın çalışma alanı ve ölçeği, doğrudan Win32'den (ürünün ince P/Invoke yüzeyi) — beklenen köşe ürünün
    /// yerleşim kodundan türemez.</summary>
    private static (Win32.RECT Work, uint Dpi) PrimaryScreen()
    {
        nint monitor = Win32.MonitorFromPoint(default, Win32.MONITOR_DEFAULTTOPRIMARY);
        var info = new Win32.MONITORINFO { cbSize = Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("GetMonitorInfo failed.");
        Marshal.ThrowExceptionForHR(Win32.GetDpiForMonitor(monitor, Win32.MDT_EFFECTIVE_DPI, out uint dpi, out _));
        return (info.rcWork, dpi);
    }

    /// <summary>
    /// WPF'in saklı sistem DPI'ı (internal <c>MS.Internal.FontCache.Util._dpi</c>; <c>SystemParameters</c> fiziksel pikseli onunla böler) —
    /// ekran değişikliğinin geride bıraktığı durumu kurmanın tek yolu. Yazım <c>SystemParameters</c>'ın ekrana bağlı önbelleğini de
    /// boşaltır (internal <c>InvalidateDisplayDependentCache</c>) ve <c>WorkArea</c>'nın gerçekten yeni çarpanla okunduğunu doğrular:
    /// kurulum tutmazsa sonda burada açık bir mesajla düşer, sessizce geçmez. WPF yükseltilip adlar değişirse de öyle.
    /// </summary>
    private sealed class WpfSystemDpi
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly FieldInfo _dpi;
        private readonly PropertyInfo _dpiProperty;
        private readonly MethodInfo _invalidate;

        public WpfSystemDpi()
        {
            var util = typeof(SystemParameters).Assembly.GetType("MS.Internal.FontCache.Util")
                ?? typeof(System.Windows.Media.Visual).Assembly.GetType("MS.Internal.FontCache.Util")
                ?? throw new InvalidOperationException("MS.Internal.FontCache.Util is gone — update the probe to WPF's new DPI cache.");
            _dpi = util.GetField("_dpi", Static) ?? throw new InvalidOperationException("Util._dpi is gone.");
            _dpiProperty = util.GetProperty("Dpi", Static) ?? throw new InvalidOperationException("Util.Dpi is gone.");
            _invalidate = typeof(SystemParameters).GetMethod("InvalidateDisplayDependentCache", Static)
                ?? throw new InvalidOperationException("SystemParameters.InvalidateDisplayDependentCache is gone.");
        }

        public int Value
        {
            get => (int)_dpiProperty.GetValue(null)!; // getter önbelleği gerekiyorsa doldurur
            set
            {
                _ = Value;
                _dpi.SetValue(null, value);
                _invalidate.Invoke(null, null);
                var (work, _) = PrimaryScreen();
                double expected = (work.Right - work.Left) * 96.0 / value;
                if (Math.Abs(SystemParameters.WorkArea.Width - expected) > 1)
                    throw new InvalidOperationException(Invariant(
                        $"SystemParameters.WorkArea did not follow the cached DPI {value}: width {SystemParameters.WorkArea.Width}, expected {expected}."));
            }
        }
    }
}
