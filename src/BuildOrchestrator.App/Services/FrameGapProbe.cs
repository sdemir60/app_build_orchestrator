using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [perf sondası] Kare aralığı kaydedicisi — yalnız <see cref="Variable"/> (<c>BO_PROBE_FRAMES</c>) bir dosya yolu taşıyorsa
/// kurulur; üretimde yoktur. Her çizilen kare için <c>CompositionTarget.Rendering</c> dinlenir ve her pencerede
/// (<see cref="WindowMs"/>) tek satır yazılır: kaç kare çizildi, en uzun aralık ve 33/50/100/250 ms üstündeki aralık sayıları.
/// Bir animasyonun "donması" tam olarak budur: ardışık iki kare arasında geçen süre. UI thread'i tıkanırsa da, render thread'i
/// derleyici süreçlerine karşı işlemci bulamazsa da aralık uzar — ikisi de aynı sayıda görünür.
///
/// <para>Sonda açıkken WPF her karede çizer (<c>Rendering</c> abonesi varken zamanlama ağacı kare hızını sınırlamaz); yani
/// sayılar dekoratif 30 fps saatlerden değil, arayüzün kare üretebilme yeteneğinden gelir. Dosyaya yalnız pencere kapanışında
/// bir satır yazılır — ölçüme girecek bir maliyet değildir.</para>
/// </summary>
internal sealed class FrameGapProbe
{
    internal const string Variable = "BO_PROBE_FRAMES";
    private const int WindowMs = 5000;

    private readonly StreamWriter _writer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly double[] _thresholdsMs = [33, 50, 100, 250];
    private readonly int[] _over = new int[4];
    private double _lastMs = double.NaN;
    private double _windowStartMs;
    private double _maxGapMs;
    private int _frames;

    private FrameGapProbe(string path)
    {
        _writer = new StreamWriter(path, append: true) { AutoFlush = true };
        _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} frames probe start (window {WindowMs} ms)");
        CompositionTarget.Rendering += OnRendering;
    }

    /// <summary>Ortam değişkeni bir yol taşıyorsa sondayı kurar; yoksa <c>null</c> — çağıran hiçbir şey yapmaz.</summary>
    internal static FrameGapProbe? StartIfRequested()
    {
        string? path = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return new FrameGapProbe(path); }
        catch (IOException) { return null; }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        if (double.IsNaN(_lastMs)) { _lastMs = now; _windowStartMs = now; return; }
        double gap = now - _lastMs;
        _lastMs = now;
        _frames++;
        if (gap > _maxGapMs) _maxGapMs = gap;
        for (int i = 0; i < _thresholdsMs.Length; i++)
            if (gap > _thresholdsMs[i]) _over[i]++;
        if (now - _windowStartMs < WindowMs) return;

        double seconds = (now - _windowStartMs) / 1000.0;
        _writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "{0:HH:mm:ss.fff} frames={1} ({2:0.0}/s) maxGap={3:0}ms over33={4} over50={5} over100={6} over250={7}",
            DateTime.Now, _frames, _frames / seconds, _maxGapMs, _over[0], _over[1], _over[2], _over[3]));
        _frames = 0;
        _maxGapMs = 0;
        Array.Clear(_over);
        _windowStartMs = now;
    }
}
