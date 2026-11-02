using System.Windows;
using System.Windows.Interop;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [pencere monitörü] Ana pencerenin kapandığı monitörü tanıyan ve pencereyi görünmeden oraya koyan kurallar (<see cref="WindowMonitor"/>).
///
/// <para><b>Kusur:</b> pencere yerini hiçbir yere yazmıyordu; uygulama her baştan açılışta (Exit, Restart to update, Windows açılışı) yeri
/// Windows'a bırakıyordu ve Windows ana ekranı seçiyordu — laptop ekranında güncellenen uygulama, ana ekran olan harici monitörde açılıyordu.</para>
///
/// <para><b>Kimlik:</b> monitörün aygıt arayüzü yolu (<c>\\?\DISPLAY#&lt;model&gt;#&lt;bağlantı&gt;#{…}</c>): çözünürlük ve ölçek değişse de aynı
/// kalır. Bağlantı kısmı monitör başka bir porta takılınca değişir; o zaman model kodu yeter — yalnız o modelden TEK monitör bağlıysa.</para>
///
/// <para><b>Yerleştirme ÖLÇÜLDÜ</b> (PerMonitorV2 manifestli geçici harness; birincil 4K %100 + laptop %150): doğuştan maximized pencere
/// birincil ekranda görünür (kusur). <c>SetWindowPlacement</c> pencereyi önce birincil ekranda gösterip sonra kaydırdı ve restore
/// dikdörtgenini bozdu; HWND'den sonra Normal'e çekmek restore'u birincil ekranda bıraktı (tam ekrandan çıkan pencere geri atladı). Tutan yol:
/// pencere NORMAL doğar, kaynağı kurulunca — görünmeden — önce sol üstü hedef çalışma alanına taşınır (ölçek değişimini Windows ile WPF kendisi
/// uygular; boyut da verilirse iki kez büyür), sonra hedef ölçekte ortalanmış restore dikdörtgeni verilir ve tam ekran yapılır. İlk görünüşü
/// hedef monitörde, tam ekran ve %150'de oldu; restore 2100×1200, aynı monitörde. Tepside başlama yolunda (önce gizli kur, sonra göster) da
/// aynı. Test süreci sistem ölçeğinde koştuğu için farklı ölçekli monitör burada kurulamaz; buradaki gerçek pencere testleri birincil ekranı —
/// sürecin kendi ölçeğindeki ekranı — kullanır ve hiçbir pencere gösterilmez.</para>
/// </summary>
[Collection("Console UI (serial)")] // gerçek HWND kuran STA testleri — bkz. ConsoleUiSerialCollection
public sealed class WindowMonitorTests
{
    /// <summary>Monitör aygıt arayüzünün sınıf kimliği (GUID_DEVINTERFACE_MONITOR) — her monitör yolunun son parçası.</summary>
    private const string MonitorInterface = "{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    internal static string MonitorKey(string model, string connection) => $@"\\?\DISPLAY#{model}#{connection}#{MonitorInterface}";

    internal static readonly DisplayMonitor Laptop =
        new(MonitorKey("ACM0A1F", "4&1f2e3d4c&0&UID8388688"), new Int32Rect(3840, 179, 2880, 1728), 1.5);

    private static readonly DisplayMonitor External =
        new(MonitorKey("ACM4200", "4&1f2e3d4c&0&UID4165"), new Int32Rect(0, 0, 3840, 2112), 1.0);

    [Fact]
    public void A_remembered_monitor_is_found_again_by_its_identity_whatever_its_resolution()
    {
        Assert.Equal(Laptop, WindowMonitor.Find(Laptop.Key, [External, Laptop]));

        var rescaled = Laptop with { WorkArea = new Int32Rect(3840, 0, 1920, 1152), Scale = 1.25 };
        Assert.Equal(rescaled, WindowMonitor.Find(Laptop.Key, [External, rescaled]));
    }

    [Fact]
    public void A_monitor_plugged_into_another_port_is_found_by_its_model()
    {
        var otherPort = External with { Key = MonitorKey("ACM4200", "4&1f2e3d4c&0&UID4170") };

        Assert.Equal(otherPort, WindowMonitor.Find(External.Key, [Laptop, otherPort]));
    }

    [Fact]
    public void Between_two_monitors_of_the_same_model_on_new_ports_nothing_is_guessed()
    {
        var left = External with { Key = MonitorKey("ACM4200", "4&1f2e3d4c&0&UID4170") };
        var right = External with { Key = MonitorKey("ACM4200", "4&1f2e3d4c&0&UID4171"), WorkArea = new Int32Rect(3840, 0, 3840, 2112) };

        Assert.Null(WindowMonitor.Find(External.Key, [left, right]));
    }

    [Fact]
    public void Without_its_monitor_the_window_is_left_to_windows()
    {
        Assert.Null(WindowMonitor.Find(null, [Laptop]));
        Assert.Null(WindowMonitor.Find("", [Laptop]));
        Assert.Null(WindowMonitor.Find(External.Key, [Laptop]));
        Assert.Null(WindowMonitor.Find(@"\\.\DISPLAY3", [Laptop]));
    }

    /// <summary>Aygıt yolu okunamayan bir ekranın (ör. CI'ın sanal ekranı) kimliği kaynağının adıdır; yalnız bütünüyle eşleşir.</summary>
    [Fact]
    public void A_display_known_only_by_its_source_name_is_matched_as_a_whole()
    {
        var virtualDisplay = new DisplayMonitor(@"\\.\DISPLAY1", new Int32Rect(0, 0, 1024, 728), 1.0);

        Assert.Equal(virtualDisplay, WindowMonitor.Find(@"\\.\DISPLAY1", [virtualDisplay]));
        Assert.Null(WindowMonitor.Find(@"\\.\DISPLAY2", [virtualDisplay]));
    }

    /// <summary>Ölçülen restore dikdörtgeni (harness, laptop %150): 1400×800 → 2100×1200, çalışma alanında ortalı.</summary>
    [Fact]
    public void The_restore_size_is_the_window_size_at_the_monitors_scale_centred_on_its_work_area()
    {
        Assert.Equal(new Int32Rect(4230, 443, 2100, 1200), WindowMonitor.RestoreBounds(Laptop.WorkArea, Laptop.Scale, 1400, 800));
    }

    [Fact]
    public void A_restore_size_larger_than_the_work_area_is_cut_to_it()
    {
        Assert.Equal(new Int32Rect(0, 0, 1024, 728), WindowMonitor.RestoreBounds(new Int32Rect(0, 0, 1024, 728), 1.0, 1400, 800));
    }

    // ---------------------------------------------------------------- gerçek monitörler (hiçbir pencere gösterilmez)

    [Fact]
    public void Every_connected_monitor_has_an_identity_a_work_area_and_a_scale()
    {
        var monitors = WindowMonitor.Connected();

        Assert.NotEmpty(monitors);
        Assert.All(monitors, m =>
        {
            Assert.False(string.IsNullOrEmpty(m.Key));
            Assert.True(m.WorkArea.Width > 0 && m.WorkArea.Height > 0, $"empty work area: {m}");
            Assert.True(m.Scale > 0, $"no scale: {m}");
        });
        Assert.Contains(Primary(), monitors);
    }

    [StaFact]
    public void A_window_belongs_to_the_monitor_it_sits_on_and_a_window_without_a_handle_to_none()
    {
        var window = new Window { ShowInTaskbar = false, Left = 0, Top = 0, Width = 400, Height = 300 };
        Assert.Null(WindowMonitor.KeyOf(window));
        try
        {
            new WindowInteropHelper(window).EnsureHandle();

            Assert.Equal(Primary().Key, WindowMonitor.KeyOf(window));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Ana pencere gibi doğuştan maximized bir pencere: monitöre açılınca normal doğar, kaynağı kurulunca — gösterilmeden — o
    /// monitörün restore dikdörtgenine oturur ve tam ekran olmayı bekler (WPF gösterirken uygular; harness'te ölçüldü).</summary>
    [StaFact]
    public void A_window_opened_on_a_monitor_is_born_normal_and_sits_on_it_before_it_is_shown()
    {
        var target = Primary();
        var window = new Window { ShowInTaskbar = false, Width = 600, Height = 400, WindowState = WindowState.Maximized };

        WindowMonitor.OpenOn(window, target);

        Assert.Equal(WindowState.Normal, window.WindowState); // maximized doğsa Windows'un seçtiği ekranda kalırdı
        try
        {
            nint hwnd = new WindowInteropHelper(window).EnsureHandle();

            Win32.GetWindowRect(hwnd, out var r);
            Assert.Equal(WindowMonitor.RestoreBounds(target.WorkArea, target.Scale, 600, 400),
                new Int32Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
            Assert.Equal(WindowState.Maximized, window.WindowState);
            Assert.False(Win32.IsWindowVisible(hwnd));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Birincil ekran: test sürecinin kendi ölçeğindeki ekran (süreç sistem ölçeğinde koşar). Gerçek monitörü kullanan testlerin
    /// TEK okuma yeri.</summary>
    internal static DisplayMonitor Primary()
    {
        var primary = WindowMonitor.Of(Win32.MonitorFromPoint(default, Win32.MONITOR_DEFAULTTOPRIMARY));
        Assert.True(primary is not null, "the primary monitor could not be read");
        return primary.Value;
    }
}
