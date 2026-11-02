using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [pencere monitörü] Ana pencerenin kablosu: gerçek kapanışta bulunduğu monitörü kalıcı duruma yazar (<see cref="UiState.LastMonitor"/>);
/// açılışta o monitör bağlıysa pencere orada tam ekran açılmak üzere NORMAL doğar (<see cref="WindowMonitor.OpenOn"/>), bağlı değilse
/// bugünkü gibi doğuştan maximized kalır ve yeri Windows seçer. Kuralların kendisi <see cref="WindowMonitorTests"/>'tedir.
///
/// <para>Harness: pencere GÖSTERİLMEZ (<see cref="MainWindowHost"/>). HWND olmadığı için pencere bulunduğu monitörü kendisi okuyamaz; testler
/// okumayı dikişten verir (<see cref="MainWindow.CurrentMonitorKey"/>). Gerçek kapanış, tepsi → Exit'in ardından gelen kapanıştır
/// (<c>CloseToTrayTests.Once_the_exit_has_started_the_shutdowns_own_close_really_closes_the_window</c> ile aynı yol).</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public sealed class LastMonitorTests
{
    private static readonly string Laptop = WindowMonitorTests.Laptop.Key;

    [StaFact]
    public void A_window_whose_last_monitor_is_connected_is_born_normal_to_be_placed_there()
    {
        using var temp = new TempDir();
        string connected = WindowMonitorTests.Primary().Key;

        var (window, _) = MainWindowHost.New(temp, saved: new UiState { LastMonitor = connected });

        Assert.Equal(WindowState.Normal, window.WindowState);
    }

    [StaFact]
    public void A_window_whose_last_monitor_is_gone_is_born_maximized_and_left_to_windows()
    {
        using var temp = new TempDir();

        var (window, _) = MainWindowHost.New(temp, saved: new UiState { LastMonitor = Laptop });

        Assert.Equal(WindowState.Maximized, window.WindowState);
    }

    [StaFact]
    public void The_real_close_remembers_the_monitor_the_window_is_on()
    {
        using var temp = new TempDir();
        var window = CloseQueued(temp);
        window.CurrentMonitorKey = () => Laptop;

        window.Close();

        Assert.Equal(Laptop, MainWindowHost.UiStateStore(temp).Load().LastMonitor);
    }

    /// <summary>Pencerenin monitörü okunamıyorsa (bu oturumda hiç gösterilmedi — bkz. aşağıdaki kaynak pini) önceki kayıt korunur: tepside
    /// geçen bir oturum, kullanıcının en son gördüğü monitörü silmez.</summary>
    [StaFact]
    public void A_close_without_a_monitor_reading_keeps_the_last_monitor()
    {
        using var temp = new TempDir();
        var window = CloseQueued(temp, new UiState { LastMonitor = Laptop });
        window.CurrentMonitorKey = () => null;

        window.Close();

        Assert.Equal(Laptop, MainWindowHost.UiStateStore(temp).Load().LastMonitor);
    }

    [StaFact]
    public void Hiding_to_the_tray_remembers_nothing()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.New(temp);
        window.CurrentMonitorKey = () => Laptop;

        window.Close(); // Close to tray açık (varsayılan): × pencereyi tepsiye indirir

        Assert.Equal(Visibility.Hidden, window.Visibility); // ön-koşul: kapanış iptal edildi
        Assert.Null(MainWindowHost.UiStateStore(temp).Load().LastMonitor);
    }

    /// <summary>[kaynak] Üretimde monitör yalnız bu oturumda gösterilmiş pencereden okunur (<c>IsLoaded</c>: ilk gösterimden sonra doğrudur,
    /// tepsiye inmek onu düşürmez): tepside başlayıp hiç açılmadan kapanan bir oturum, Windows'un o gün gizli pencereye seçtiği ekranı
    /// yazmaz. Headless süit bunu göremez — pencere hiç yüklenmez, HWND'si de yoktur — bu yüzden kaynakta pinlenir
    /// (<c>CloseToTrayTests.MainWindow_wires_the_tray_exit_item_to_the_safe_exit</c> ile AYNI desen).</summary>
    [Fact]
    public void Only_a_window_shown_this_session_reads_its_monitor()
    {
        const string relative = "MainWindow.Monitor.cs";
        string source = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, relative));

        Assert.Single(SourceGuard.ScanText(relative, source,
            new Regex(@"CurrentMonitorKey = \(\) => IsLoaded \? WindowMonitor\.KeyOf\(this\) : null;"), skipCommentLines: true));
    }

    /// <summary>Tepsi → Exit (uçuşta iş yok): çıkış başladı ve uygulamanın kapanışı kuyrukta — bundan sonraki <c>Close()</c> pencereyi
    /// GERÇEKTEN kapatır. Uygulamayı kapatan çağrı sayaçlı bir sahtedir (<see cref="ExitSeamFakes"/>).</summary>
    private static MainWindow CloseQueued(TempDir temp, UiState? saved = null)
    {
        var (window, _) = MainWindowHost.New(temp, saved: saved);
        var fakes = new ExitSeamFakes(window);
        window.ExitFromTray();
        DispatcherPump.PumpUntil(() => fakes.Shutdowns > 0, TimeSpan.FromSeconds(2));
        Assert.Equal(1, fakes.Shutdowns); // ön-koşul: çıkış başladı
        return window;
    }
}
