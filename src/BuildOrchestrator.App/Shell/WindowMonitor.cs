using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BuildOrchestrator.App.Shell;

/// <summary>[pencere monitörü] Bağlı bir monitör: kimliği (<see cref="WindowMonitor"/>), çalışma alanı (fiziksel px) ve ölçeği (%150 → 1,5).</summary>
internal readonly record struct DisplayMonitor(string Key, Int32Rect WorkArea, double Scale);

/// <summary>
/// [pencere monitörü] Ana pencere en son hangi monitörde kapandıysa uygulama yeniden açılınca (Exit, Restart to update, Windows açılışı)
/// orada, tam ekran açılır; o monitör bağlı değilse yeri Windows seçer — ana ekran. Yalnız monitör hatırlanır, boyut değil: pencere hep tam
/// ekran açıldığı için o monitörün o anki çözünürlüğünü kaplar.
///
/// <para><b>Kimlik</b> monitörün aygıt arayüzü yoludur (<c>\\?\DISPLAY#ACM4200#4&amp;…&amp;UID4165#{…}</c>: model kodu + bağlantının örnek
/// yolu); çözünürlük ve ölçek değişse de aynı kalır. Aygıt yolu okunamayan bir ekranın (sanal ekran, uzak masaüstü) kimliği kaynağının adıdır
/// (<c>\\.\DISPLAY1</c>). Bağlantı kısmı monitör başka bir porta takılınca değişir: kimlik bulunamazsa model kodu yeter — yalnız o modelden
/// TEK monitör bağlıysa; iki eş monitör arasında tahmin yapılmaz.</para>
///
/// <para><b>Yerleştirme — ÖLÇÜLDÜ</b> (PerMonitorV2 manifestli harness; birincil 4K %100 + laptop %150): pencere XAML'de doğuştan maximized'dır
/// ve maximized doğan HWND Windows'un seçtiği ekranda kalır. <c>SetWindowPlacement</c> pencereyi önce birincil ekranda gösterip sonra kaydırdı
/// ve restore dikdörtgenini bozdu; HWND kurulduktan sonra Normal'e çekmek restore'u birincil ekranda bıraktı (tam ekrandan çıkan pencere geri
/// atladı). Tutan yol (<see cref="OpenOn"/>): pencere NORMAL doğar; kaynağı kurulunca — henüz görünmeden — önce sol üstü hedefin çalışma
/// alanına taşınır (boyuta dokunulmaz: ölçek değişimini <c>WM_DPICHANGED</c> ile Windows ve WPF kendisi uygular, boyut da verilirse iki kez
/// büyür), sonra hedef ölçekte ortalanmış restore dikdörtgeni verilir ve tam ekran istenir. WPF tam ekranı gösterirken uygular: ilk görünüş
/// hedef monitörde, tam ekran ve hedefin ölçeğinde; tam ekrandan çıkınca pencere aynı monitörde kalır. Tepside başlama (önce gizli kur, sonra
/// göster) aynı yoldan geçer. Gizli kurulumda tam ekran kenar düzeltmesinin (<see cref="MaximizeFix"/>) ölçeği gösterilene dek eskidir,
/// gösterilirken WPF'in DPI bildirimiyle tazelenir — ekrana eski hâli hiç çıkmaz.</para>
/// </summary>
internal static class WindowMonitor
{
    /// <summary>Hatırlanan monitör bağlıysa o; değilse <c>null</c> — yer Windows'a kalır. Monitörler yalnız bir kayıt varsa okunur.</summary>
    public static DisplayMonitor? Remembered(string? key) => string.IsNullOrEmpty(key) ? null : Find(key, Connected());

    /// <summary>Kimliği <paramref name="key"/> olan monitör: önce bütünüyle eşleşen, yoksa aynı model kodlu — yalnız o modelden tek monitör
    /// bağlıysa. Kayıt yoksa ya da monitör bağlı değilse <c>null</c>.</summary>
    public static DisplayMonitor? Find(string? key, IReadOnlyList<DisplayMonitor> monitors)
    {
        if (string.IsNullOrEmpty(key)) return null;
        foreach (var monitor in monitors)
            if (string.Equals(monitor.Key, key, StringComparison.OrdinalIgnoreCase)) return monitor;
        if (ModelOf(key) is not { } model) return null;
        var sameModel = monitors.Where(m => string.Equals(ModelOf(m.Key), model, StringComparison.OrdinalIgnoreCase)).ToList();
        return sameModel.Count == 1 ? sameModel[0] : null;
    }

    /// <summary>Aygıt yolunun model kodu (<c>\\?\DISPLAY#ACM4200#…</c> → <c>ACM4200</c>); kaynak adında (<c>\\.\DISPLAY1</c>) yoktur.</summary>
    internal static string? ModelOf(string key)
    {
        string[] parts = key.Split('#');
        return parts.Length >= 3 && parts[0].EndsWith("DISPLAY", StringComparison.OrdinalIgnoreCase) && parts[1].Length > 0 ? parts[1] : null;
    }

    /// <summary>Tam ekrandan çıkan pencerenin dikdörtgeni (fiziksel px): <paramref name="width"/>×<paramref name="height"/> DIP o monitörün
    /// ölçeğinde, çalışma alanında ortalı; alandan büyükse alana kırpılır.</summary>
    public static Int32Rect RestoreBounds(Int32Rect workArea, double scale, double width, double height)
    {
        int w = Math.Min((int)Math.Round(width * scale), workArea.Width);
        int h = Math.Min((int)Math.Round(height * scale), workArea.Height);
        return new Int32Rect(workArea.X + (workArea.Width - w) / 2, workArea.Y + (workArea.Height - h) / 2, w, h);
    }

    /// <summary>Masaüstüne bağlı monitörler.</summary>
    public static IReadOnlyList<DisplayMonitor> Connected()
    {
        var monitors = new List<DisplayMonitor>();
        Win32.EnumDisplayMonitors(0, 0, (nint handle, nint hdc, ref Win32.RECT bounds, nint data) =>
        {
            if (Of(handle) is { } monitor) monitors.Add(monitor);
            return true;
        }, 0);
        return monitors;
    }

    /// <summary>Bir monitör tanıtıcısının kimliği, çalışma alanı ve ölçeği; okunamazsa <c>null</c>.</summary>
    internal static DisplayMonitor? Of(nint monitor)
    {
        var info = new Win32.MONITORINFOEX { cbSize = Marshal.SizeOf<Win32.MONITORINFOEX>() };
        if (monitor == 0 || !Win32.GetMonitorInfo(monitor, ref info)) return null;
        uint dpi = Win32.GetDpiForMonitor(monitor, Win32.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 ? dpiX : 96;
        var work = info.rcWork;
        return new DisplayMonitor(KeyOfSource(info.szDevice),
            new Int32Rect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top), dpi / 96.0);
    }

    /// <summary>Pencerenin şu an bulunduğu monitörün kimliği — simge durumundaysa küçültülmeden önceki yeri; HWND yoksa <c>null</c>.</summary>
    public static string? KeyOf(Window window)
    {
        nint hwnd = new WindowInteropHelper(window).Handle;
        return hwnd == 0 ? null : Of(Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST))?.Key;
    }

    /// <summary>Pencereyi <paramref name="monitor"/>'de tam ekran açar. HWND kurulmadan ÖNCE çağrılır (ana pencerenin ctor'u): pencere normal
    /// doğar, kaynağı kurulunca — görünmeden — monitöre taşınır ve tam ekran istenir. Restore boyu pencerenin <c>Width</c>×<c>Height</c>'ıdır.</summary>
    public static void OpenOn(Window window, DisplayMonitor monitor)
    {
        double width = window.Width, height = window.Height;
        window.WindowState = WindowState.Normal;
        window.SourceInitialized += PlaceOnMonitor;

        void PlaceOnMonitor(object? sender, EventArgs e)
        {
            window.SourceInitialized -= PlaceOnMonitor;
            nint hwnd = new WindowInteropHelper(window).Handle;
            const uint flags = Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE;
            Win32.SetWindowPos(hwnd, 0, monitor.WorkArea.X, monitor.WorkArea.Y, 0, 0, flags | Win32.SWP_NOSIZE);
            var bounds = RestoreBounds(monitor.WorkArea, monitor.Scale, width, height);
            Win32.SetWindowPos(hwnd, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height, flags);
            window.WindowState = WindowState.Maximized;
        }
    }

    /// <summary>Ekran kaynağına bağlı etkin monitörün aygıt yolu; okunamazsa kaynağın kendi adı.</summary>
    private static string KeyOfSource(string source)
    {
        for (int i = 0; ; i++)
        {
            var device = new Win32.DISPLAY_DEVICE { cb = Marshal.SizeOf<Win32.DISPLAY_DEVICE>() };
            if (!Win32.EnumDisplayDevices(source, i, ref device, Win32.EDD_GET_DEVICE_INTERFACE_NAME)) return source;
            if ((device.StateFlags & Win32.DISPLAY_DEVICE_ACTIVE) != 0 && device.DeviceID is { Length: > 0 } id) return id;
        }
    }
}
