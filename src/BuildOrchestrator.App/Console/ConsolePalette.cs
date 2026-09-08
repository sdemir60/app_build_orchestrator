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
    /// <summary>
    /// Satır renklerinin <b>token anahtarları</b> — TEK doğruluk kaynağı. Paletin kendisi
    /// (<see cref="FromLookup"/>) ve imleç renk sıçraması (<see cref="Controls.CursorHop"/>, design v1.12.1
    /// §2.5) ikisi de buradan okur: imleç "bir konsol satırının taşıyamayacağı hiçbir rengi" almamalıdır ve
    /// bunu ancak aynı anahtar listesinden beslenerek garanti edebilir (kopya YASAK, CLAUDE.md).
    /// </summary>
    public static class Keys
    {
        public const string Clock = "Brush.TextFaint";
        public const string Icon = "Brush.AmberText";
        public const string Cmd = "Brush.TextPrimary";
        public const string Info = "Brush.TextSecondary";
        public const string Dim = "Brush.TextFaint";

        /// <summary>Başarı tonu. <see cref="ConsoleLineType"/>'ta karşılığı YOKTUR (onu döndüren tek yol metin
        /// tahminiydi, kaldırıldı) ama satır paletinin bir rengidir: koşu sonu satırları ve statü glyph'i onu
        /// taşır, imleç turu da (design v1.12.1) bu tonu içerir.</summary>
        public const string Success = "Brush.StatusSuccessText";

        /// <summary>[design v1.11.0 §1.1] warn satırları AMBER. <b>[DEĞİŞEN KURAL]</b> Eskiden cycle
        /// turuncusuydu (<c>Brush.StatusCycleText</c>); v1.11.0 turuncuyu UI'dan tamamen çıkardı —
        /// <c>--status-cycle*</c> token'ları dosyada durur ama bir statü kanalı olarak kullanılmaz.</summary>
        public const string Warn = "Brush.AmberText";

        public const string Error = "Brush.StatusFailText";
    }

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
        Clock = Resolve(find, Keys.Clock),
        Icon = Resolve(find, Keys.Icon),
        Cmd = Resolve(find, Keys.Cmd),
        Info = Resolve(find, Keys.Info),
        Dim = Resolve(find, Keys.Dim),
        Warn = Resolve(find, Keys.Warn),
        Error = Resolve(find, Keys.Error),
    };

    private static Brush Resolve(Func<string, object?> find, string key) =>
        find(key) as Brush ?? throw new InvalidOperationException($"Console palette: brush resource '{key}' was not found.");
}
