using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using IoFile = System.IO.File;
using IoPath = System.IO.Path;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Uygulama <b>büyütülmüş (maximized)</b> açılır: bu bir kullanıcı kararıdır — araç bir build panosudur, graf +
/// liste + konsol + stream dört panelini aynı anda taşır ve 1400×800'lik bir pencerede her açılışta elle
/// büyütülüyordu.
///
/// <para><b>Restore boyutu KORUNUR:</b> <c>Width</c>/<c>Height</c> (1400×800) hâlâ bildirilir — maximize
/// edilmiş bir pencerede bunlar "restore" ölçüsüdür, yani kullanıcı butona basıp küçülttüğünde pencere hâlâ
/// tasarlanan ölçüye döner. <c>WindowState="Maximized"</c> tek başına yazılıp boyutlar silinseydi restore
/// WPF'in varsayılanına düşerdi.</para>
///
/// <para>Pencere <c>Show()</c> EDİLMEZ (bkz. <see cref="MainWindowRealizeTests"/> sınıf özeti: gerçek tepsi
/// ikonu/kısayol/supervisor bir testin yan etkisi olamaz). <c>WindowState</c> HWND'siz de okunur/yazılır —
/// WPF yalnız görsel karşılığını (WM_SIZE) HWND'ye bağlar.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class StartupWindowStateTests
{
    [StaFact]
    public void The_window_opens_maximized()
    {
        using var temp = new TempDir();
        var window = MainWindowHost.New(temp).window;

        Assert.Equal(WindowState.Maximized, window.WindowState);
    }

    [StaFact]
    public void The_restore_size_stays_at_the_designed_1400_by_800()
    {
        using var temp = new TempDir();
        var window = MainWindowHost.New(temp).window;

        // Maximize edilmiş pencerede Width/Height = restore ölçüsü. Kaybolursa "küçült" WPF varsayılanına düşer.
        Assert.Equal(1400d, window.Width);
        Assert.Equal(800d, window.Height);
    }

    /// <summary>
    /// [Snap Layouts sökümü · regresyon] Maximize/restore butonu ARTIK sıradan bir WPF butonudur: tıklama
    /// <c>Button.Click</c> → <c>OnMaximizeRestore</c> yolundan gelir. T62'de bu bölge <c>HTMAXBUTTON</c> ile
    /// NON-CLIENT ilan edilmişti; orada WPF <c>Click</c>'i HİÇ DOĞMUYORDU ve toggle elle
    /// <c>WM_NCLBUTTONDOWN/UP</c> çiftinden üretiliyordu. Hook kalkınca tek çalışan yol <c>Click</c>'tir.
    ///
    /// <para><b>Sonucun kendisi (<c>WindowState</c> değişimi) burada ÖLÇÜLEMEZ — ölçüldü:</b>
    /// <c>SystemCommands.MaximizeWindow/RestoreWindow</c> durumu doğrudan yazmaz, pencereye
    /// <c>WM_SYSCOMMAND</c> <b>POST</b> eder ve HWND yokken/pencere görünmezken SESSİZCE çıkar. Bu süit
    /// pencereyi bilerek <c>Show()</c> etmez (sınıf özeti), yani orada durum hiç değişmez. Pinlenebilir olan,
    /// T62'de gerçekten kaybolmuş olan halka: butonun tıklamayı ALMASI ve üretim handler'ına bağlı olması.</para>
    /// </summary>
    [StaFact]
    public void The_max_button_takes_real_clicks_again_and_is_wired_to_the_maximize_restore_handler()
    {
        using var temp = new TempDir();
        var window = MainWindowHost.New(temp).window;
        MainWindowHost.Realize(window);

        int clicks = 0;
        window.MaxButton.Click += (_, _) => clicks++;
        var invoke = new ButtonAutomationPeer(window.MaxButton).GetPattern(PatternInterface.Invoke) as IInvokeProvider;
        Assert.NotNull(invoke); // rol "button" ise yetenek de gerçek olmalı — non-client bölgede değil

        invoke!.Invoke(); // dispatcher'a Input önceliğiyle post edilir → pompalanır (AccessibilityTests deseni)
        DispatcherPump.PumpUntil(() => clicks > 0, TimeSpan.FromSeconds(2));
        Assert.Equal(1, clicks);

        // Üretim handler'ının o Click'e bağlı olduğu markup'ta pinlenir: yukarıdaki abonelik testin KENDİ
        // handler'ıdır, XAML bağı düşse de sayardı.
        string markup = IoFile.ReadAllText(IoPath.Combine(RepoPaths.AppSrcRoot, "MainWindow.xaml"));
        Assert.Contains("x:Name=\"MaxButton\"", markup, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnMaximizeRestore\"", markup, StringComparison.Ordinal);
    }
}
