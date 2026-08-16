using System.Windows.Media;

namespace BuildOrchestrator.App.Console;

/// <summary>
/// [T56/3a] Konsol renk paleti — design-v1 §2.5 satır renkleri, TOKEN brush'larından (Tokens.xaml, hardcode YASAK):
/// cmd=text-primary, info=text-secondary, dim=text-faint, warn=status-cycle-text, error=status-fail-text.
/// <para><c>Success</c> KALDIRILDI: onu döndüren tek yol metin tahminiydi (bkz. ConsoleLineClassifier).</para>
///
/// <para>Brush'lar bir <c>Func&lt;string,object?&gt;</c> lookup ile çözülür — üretimde
/// <c>ConsoleView.TryFindResource</c>, testte dosyadan yüklenen Tokens.xaml sözlüğü (headless host, D8). Böylece
/// <see cref="ConsoleColorizer"/> saf/test edilebilir kalır ve tokenlar anahtarla tüketilir.</para>
/// </summary>
public sealed class ConsolePalette
{
    public required Brush Clock { get; init; }
    public required Brush Icon { get; init; }
    public required Brush Cmd { get; init; }
    public required Brush Info { get; init; }
    public required Brush Dim { get; init; }
    public required Brush Warn { get; init; }
    public required Brush Error { get; init; }

    public Brush ForType(ConsoleLineType type) => type switch
    {
        ConsoleLineType.Cmd => Cmd,
        ConsoleLineType.Dim => Dim,
        ConsoleLineType.Warn => Warn,
        ConsoleLineType.Error => Error,
        _ => Info,
    };

    /// <summary>Token brush anahtarlarını verilen lookup ile çözer. Eksik anahtarda anlaşılır bir hata fırlatır
    /// (sessiz yanlış-renk yerine) — anahtar adları Tokens.xaml ile birebir.</summary>
    public static ConsolePalette FromLookup(Func<string, object?> find) => new()
    {
        Clock = Resolve(find, "Brush.TextFaint"),
        Icon = Resolve(find, "Brush.AmberText"),
        Cmd = Resolve(find, "Brush.TextPrimary"),
        Info = Resolve(find, "Brush.TextSecondary"),
        Dim = Resolve(find, "Brush.TextFaint"),
        Warn = Resolve(find, "Brush.StatusCycleText"),
        Error = Resolve(find, "Brush.StatusFailText"),
    };

    private static Brush Resolve(Func<string, object?> find, string key) =>
        find(key) as Brush ?? throw new InvalidOperationException($"Console palette: brush resource '{key}' was not found.");
}
