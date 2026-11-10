using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [pencere çerçevesi] Tam ekran pencerenin Windows 11 çerçevesi komşu monitöre düşmez (<see cref="WindowFrame"/>).
///
/// <para><b>Kusur (kullanıcı):</b> çift monitörde sağdaki ekranda tam ekran yapılan pencerenin sol kenarı soldaki monitörde hafifçe
/// görünüyordu; pencereye her tıklandığında oradaki pencerenin üstüne çıkıyordu.</para>
///
/// <para><b>Ölçüldü</b> (solda birincil 4K %100, sağda laptop %150; PerMonitorV2 deneme penceresi ve kurulu v1.14.0 — ikisi piksel piksel
/// aynı): tam ekran pencerenin dikdörtgeni <c>(3829,168)-(6731,1918)</c>, çalışma alanı <c>(3840,179)-(6720,1907)</c> — her kenarda 11 px
/// taşma. İçeriği WPF'in pencere bölgesi alana birebir kırpıyordu; taşan, DWM'in çizdiği Windows 11 çerçevesiydi: yuvarlak köşe tercihi
/// açıkken DWM yuvarlak köşeyi, 1 px kenarlığı ve gölgeyi bölgeye değil dikdörtgene (<c>DWMWA_EXTENDED_FRAME_BOUNDS</c> = dikdörtgenin
/// kendisi) çiziyor. Soldaki monitöre konan yeşil bir pencerenin üstünde, tam ekran pencere öne gelince: tercih yuvarlakken 13 577 px,
/// tam ekranda kare köşeyle 0 px. Kenarlık rengini kapatmak (<c>DWMWA_COLOR_NONE</c>) yetmedi — gölge kaldı (13 520 px).</para>
///
/// <para>Köşe tercihi gerçek DWM'den geri okunur. Pencere gösterilmez: DWM özniteliği tanıtıcıya bağlıdır ve tanıtıcı
/// <see cref="WindowInteropHelper.EnsureHandle"/> ile kurulur — ana pencerenin kaynağının kurulduğu an, ilk karesinden önce.</para>
/// </summary>
[Collection("Console UI (serial)")] // gerçek HWND kuran STA testleri — bkz. ConsoleUiSerialCollection
public sealed class WindowFrameTests
{
    // dwmapi.h: DWMWA_WINDOW_CORNER_PREFERENCE ve DWM_WINDOW_CORNER_PREFERENCE değerleri.
    private const int CornerPreference = 33, DoNotRound = 1, Round = 2;

    /// <summary>Ana pencere doğuştan tam ekrandır; monitörüne açılırken de normal doğup kaynağında tam ekran olur. İki yolda da ilk karede
    /// köşe kare olmalı, pencere küçülünce yuvarlak, yeniden büyüyünce yine kare.</summary>
    [StaFact]
    public void Corners_are_square_while_the_window_is_maximized_and_round_otherwise()
    {
        var window = new Window { ShowInTaskbar = false, Width = 400, Height = 300, WindowState = WindowState.Maximized };
        WindowFrame.Bind(window, borderColor: 0x00302a2a);
        try
        {
            nint hwnd = new WindowInteropHelper(window).EnsureHandle();
            Assert.Equal(DoNotRound, CornersOf(hwnd));

            window.WindowState = WindowState.Normal;
            Assert.Equal(Round, CornersOf(hwnd));

            window.WindowState = WindowState.Maximized;
            Assert.Equal(DoNotRound, CornersOf(hwnd));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Kusurun görüldüğü yol: uygulama en son kapandığı monitörde açılır — pencere normal doğar, kaynağı kurulunca o monitörde tam
    /// ekran olur (<see cref="WindowMonitor.OpenOn"/>). İlk karesinden önce köşesi kare olmalı. Sıra ana pencereninkiyle aynıdır: çerçeve
    /// önce bağlanır, monitör sonra — kaynakta çerçeve pencereyi henüz normal görür.</summary>
    [StaFact]
    public void A_window_opened_on_its_last_monitor_is_square_before_its_first_frame()
    {
        var window = new Window { ShowInTaskbar = false, Width = 600, Height = 400, WindowState = WindowState.Maximized };
        WindowFrame.Bind(window, borderColor: 0x00302a2a);
        WindowMonitor.OpenOn(window, WindowMonitorTests.Primary());
        try
        {
            nint hwnd = new WindowInteropHelper(window).EnsureHandle();

            Assert.Equal(WindowState.Maximized, window.WindowState); // ön koşul: monitöre açılış kaynakta tam ekran yaptı
            Assert.Equal(DoNotRound, CornersOf(hwnd));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>DWM öznitelikleri yalnız pencerenin durumunu bilen tek yerde yazılır — kusur, durumdan habersiz bir yazımdı (ana pencerenin
    /// kaynağında köşe tercihi koşulsuz yuvarlaktı). <c>Dwm.cs</c> yalnız bildirimi taşır.</summary>
    [Fact]
    public void Only_the_window_frame_writes_dwm_window_attributes()
        => Assert.Empty(SourceGuard.ScanApp("*.cs", DwmWrite,
            allowedFiles: [Path.Combine("Shell", "WindowFrame.cs"), Path.Combine("Shell", "Dwm.cs")], skipCommentLines: true));

    private static readonly Regex DwmWrite = new(@"\bDwmSetWindowAttribute\s*\(", RegexOptions.Compiled);

    private static int CornersOf(nint hwnd)
    {
        int hr = DwmGetWindowAttribute(hwnd, CornerPreference, out int value, sizeof(int));
        Assert.True(hr == 0, $"DwmGetWindowAttribute failed: 0x{hr:X8}");
        return value;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
}
