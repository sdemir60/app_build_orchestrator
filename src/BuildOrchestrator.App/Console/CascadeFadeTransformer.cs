using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace BuildOrchestrator.App.Console;

/// <summary>
/// [T56/3b] Proje-log kaskatının satır-bazlı opacity-fade katmanı. AvalonEdit satır translateY+scale desteklemez
/// (feasibility §3.6/A13.1) → her satırın foreground brush'ı, <see cref="CascadeScheduler"/>'ın o satır için
/// verdiği opacity ile <b>alpha-ölçeklenir</b> (design'ın pop-in'inin en yakın eşdeğeri). ConsoleColorizer'dan
/// SONRA <c>LineTransformers</c>'a eklenir: her elemanın (colorizer'ın koyduğu) rengini okur, alpha'sını modüle
/// eder — belge DÜZ metin kalır. Kaskat bitince <see cref="ConsoleView"/> bu transformer'ı KALDIRIR (tam opak).
///
/// <para>Flash yok: açığa çıkmamış satır opacity 0 (görünmez); ConsoleView her tick'te <see cref="Elapsed"/>'i
/// günceller ve TextView'ı redraw eder (Stopwatch-bazlı — motion sözleşmesi).</para>
/// </summary>
public sealed class CascadeFadeTransformer : DocumentColorizingTransformer
{
    private readonly CascadeScheduler _scheduler;
    private readonly Dictionary<(Color Color, int AlphaBucket), Brush> _cache = new(); // donmuş, tekrar kullanılır

    public CascadeFadeTransformer(CascadeScheduler scheduler) => _scheduler = scheduler;

    /// <summary>Geçerli kaskat elapsed'i — ConsoleView her tick'te ayarlar.</summary>
    public TimeSpan Elapsed { get; set; }

    /// <summary>Kaskat verilen elapsed'te tamamlandı mı (tüm satırlar tam opak) — ConsoleView bunu izleyip
    /// transformer'ı kaldırır.</summary>
    public bool IsComplete(TimeSpan elapsed) => _scheduler.IsComplete(elapsed);

    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.Length == 0) return;
        double opacity = _scheduler.OpacityOf(line.LineNumber - 1, Elapsed);
        if (opacity >= 1.0) return; // tam opak — dokunma (colorizer'ın rengi kalır)
        ChangeLinePart(line.Offset, line.Offset + line.Length, element =>
        {
            if (element.TextRunProperties.ForegroundBrush is SolidColorBrush src)
                element.TextRunProperties.SetForegroundBrush(Faded(src.Color, opacity));
        });
    }

    private Brush Faded(Color color, double opacity)
    {
        int bucket = (int)Math.Clamp(opacity * 255.0, 0, 255); // alpha kovası → tick başına yeni allocation'ı önler
        var key = (color, bucket);
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var brush = new SolidColorBrush(Color.FromArgb((byte)(color.A * bucket / 255), color.R, color.G, color.B));
        brush.Freeze();
        _cache[key] = brush;
        return brush;
    }
}
