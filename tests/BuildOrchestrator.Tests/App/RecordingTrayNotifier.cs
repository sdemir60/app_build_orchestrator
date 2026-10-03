using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [perf Faz B · son toparlama · F-O3] <see cref="ITrayRunNotifier"/>'ın TEK ortak test sahtesi (kopya YASAK: üç test sınıfı
/// ayrı ayrı bir sahte yazmıştı ve arayüzün her yeni üyesi üçüne de dokunduruyordu). Tepsi bildirim yüzeyine giden HER şeyi
/// kaydeder: biten koşunun satırlarını sırayla (<see cref="Count"/>, <see cref="LastLine"/>) ve yok sayılan Build
/// kısayolunun nedenlerini (<see cref="IgnoredReasons"/>). Her test yalnız ilgilendiğini okur.
/// </summary>
internal sealed class RecordingTrayNotifier(Action<string>? log = null) : ITrayRunNotifier
{
    private readonly List<RibbonLine> _finished = [];

    /// <summary>Yok sayılan kısayolun nedenleri, geliş sırasıyla.</summary>
    public readonly List<string> IgnoredReasons = [];

    /// <summary>Teslim edilen koşu-bitişi satırı sayısı.</summary>
    public int Count => _finished.Count;

    /// <summary>Son teslim edilen koşu-bitişi satırı; hiç gelmediyse <c>null</c>.</summary>
    public RibbonLine? LastLine => _finished.Count == 0 ? null : _finished[^1];

    /// <summary><paramref name="log"/> verilmişse her teslim <c>Notify:{satır}</c> olarak ona da yazılır — gösterge sahtesiyle
    /// ortak sıralı günlük, bildirimin ne ÖNCE ne SONRA düştüğünü okuyabilsin diye.</summary>
    public void ShowRunFinished(RibbonLine line)
    {
        _finished.Add(line);
        log?.Invoke($"Notify:{line.Text}");
    }

    public void ShowBuildIgnored(string reason) => IgnoredReasons.Add(reason);
}
