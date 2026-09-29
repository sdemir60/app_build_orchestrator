using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using BuildOrchestrator.App.Shell;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı kararı 2026-09-29] SONDA — gerçek Windows dosya seçicisi <see cref="CenteredDialog"/> kapsamında
/// sahibinin üzerinde ortalanıyor mu. Konum hesabı <see cref="DialogPlacementTests"/>'te pinlidir; burada sınanan
/// şey kancanın GERÇEK seçicide tuttuğudur: seçici son konumunu kendisi hatırlar ve açılırken oraya dönebilir, yani
/// kanca yanlış anda koşarsa hesap doğru olsa bile seçici ortalanmaz. Kontrol ölçümü olarak aynı seçici kapsamsız da
/// açılır (Windows'un kendi seçtiği yer).
///
/// <para>Varsayılan koşuda ÇALIŞMAZ (<c>BO_PROBE_FILE_DIALOG=1</c> yoksa <c>Skip</c>): ekranda gerçek bir pencere ve
/// iki kez gerçek bir dosya seçici açar; seçiciyi yarım saniye sonra kendisi kapatır.</para>
/// </summary>
[Trait("Category", "Measurement")]
[Collection("Console UI (serial)")]
public sealed class FileDialogCenteringProbeTests(ITestOutputHelper output)
{
    /// <summary>Kabul edilen sapma — tamsayı bölmesinin yarım pikseli ve pencere kenarlığının yuvarlaması.</summary>
    private const int TolerancePx = 2;

    [SkippableFact]
    public void The_file_picker_opens_centred_over_its_owner()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("BO_PROBE_FILE_DIALOG") == "1",
            "Opens a real window and a real file dialog — opt in with BO_PROBE_FILE_DIALOG=1.");

        var (log, offsetX, offsetY) = StaThread.RunAsync(Probe, "file-dialog-centering-probe").GetAwaiter().GetResult();
        foreach (string line in log) output.WriteLine(line);

        Assert.True(Math.Abs(offsetX) <= TolerancePx && Math.Abs(offsetY) <= TolerancePx,
            $"seçici sahibinin merkezinden ({offsetX}, {offsetY}) px sapık açıldı");
    }

    private static (List<string> Log, int OffsetX, int OffsetY) Probe()
    {
        var log = new List<string>();
        // Pencere çalışma alanının büyük kısmını kaplar (seçici sığsın, kelepçe ölçümü bulandırmasın) ama sol üste
        // yaslıdır: Windows'un varsayılan yeri (sahibin sol üstü) ile ortalanmış yer birbirinden ayrılsın.
        var area = SystemParameters.WorkArea;
        var window = new Window
        {
            Left = area.Left + 20, Top = area.Top + 10, Width = area.Width * 0.85, Height = area.Height * 0.94,
            Title = "file dialog probe",
        };
        window.Show();
        nint owner = new WindowInteropHelper(window).Handle;
        try
        {
            Win32.GetWindowRect(owner, out var o);
            log.Add(Describe("pencere", o));

            var free = OpenAndMeasure(window, owner, centred: false);
            log.Add(Describe("kapsamsız seçici (Windows'un yeri)", free));

            var centred = OpenAndMeasure(window, owner, centred: true);
            log.Add(Describe("CenteredDialog kapsamında seçici", centred));

            int dx = (centred.Left + centred.Right) / 2 - (o.Left + o.Right) / 2;
            int dy = (centred.Top + centred.Bottom) / 2 - (o.Top + o.Bottom) / 2;
            log.Add(string.Format(CultureInfo.InvariantCulture, "  merkez sapması: ({0}, {1}) px", dx, dy));
            return (log, dx, dy);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Seçiciyi açar; yarım saniye sonra (seçici tamamen yerleşmişken) konumunu okur ve kapatır. Zamanlayıcı
    /// seçicinin kendi modal döngüsünde koşar — WPF dispatcher'ı o döngüde de mesaj alır.</summary>
    private static Win32.RECT OpenAndMeasure(Window window, nint owner, bool centred)
    {
        var measured = default(Win32.RECT);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Normal, (s, _) =>
        {
            nint dialog = FindOwnedWindow(owner);
            if (dialog == 0) return; // henüz yok — bir sonraki tikte
            ((DispatcherTimer)s!).Stop();
            Win32.GetWindowRect(dialog, out measured);
            PostMessage(dialog, WmClose, 0, 0);
        }, window.Dispatcher);
        timer.Start();

        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Settings (*.json)|*.json" };
        if (centred)
            using (CenteredDialog.Over(window)) picker.ShowDialog(window);
        else
            picker.ShowDialog(window);
        timer.Stop();
        return measured;
    }

    private static nint FindOwnedWindow(nint owner)
    {
        nint found = 0;
        EnumThreadWindows(Win32.GetCurrentThreadId(), (hwnd, _) =>
        {
            if (!Win32.IsWindowVisible(hwnd) || Win32.GetWindow(hwnd, Win32.GW_OWNER) != owner) return true;
            found = hwnd;
            return false;
        }, 0);
        return found;
    }

    private static string Describe(string what, Win32.RECT r) => string.Format(CultureInfo.InvariantCulture,
        "  {0}: ({1}, {2}) {3}×{4}", what, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    // --- yalnız sondanın ihtiyacı (üretim yüzeyi Win32.cs'e girmez)
    private const uint WmClose = 0x0010;

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumThreadWindows(uint dwThreadId, EnumWindowsProc lpfn, nint lParam);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);
}
