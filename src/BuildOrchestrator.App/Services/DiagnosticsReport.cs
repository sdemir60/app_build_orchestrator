using System.Globalization;
using System.Text;

namespace BuildOrchestrator.App.Services;

/// <summary>[About] Tanı tablosunun bir satırı: etiket + gösterilecek değer.</summary>
public readonly record struct DiagnosticsLine(string Label, string Value);

/// <summary>
/// [About] Tanı raporunun ham girdisi — TOPLAMAYI çağıran yapar (<c>RuntimeInformation</c>, <c>MsBuildResolver</c>,
/// uygulamanın kendi yol static'leri), bu tip yalnız taşır. Böylece rapor mantığı WPF'siz ve process'siz
/// test edilir.
/// </summary>
public sealed record DiagnosticsInput(
    string AppVersion,
    string? EngineVersion,
    int? EnginePid,
    string Runtime,
    string Os,
    string MsBuild,
    string RepositoryRoot,
    string StateFile,
    string LogsRoot,
    string WorktreePool);

/// <summary>
/// [About] Environment sekmesinin çizdiği satırlar ve "Copy diagnostics"in panoya yazdığı metin — TEK
/// modelden. İkisi ayrı listelerden üretilseydi biri güncellenip diğeri unutulurdu.
///
/// <para>SAF: hiçbir şey okumaz, hiçbir process başlatmaz. Etiket metinleri BURADA tanımlanır; XAML onları
/// tekrar yazmaz (sekme bir <c>ItemsControl</c>'dür).</para>
/// </summary>
public static class DiagnosticsReport
{
    /// <summary>Motor henüz doğmadı.</summary>
    public const string NotStarted = "not started";
    /// <summary>Değer bu koşulda YOK (ör. motor doğmadığı için PID).</summary>
    public const string Unknown = "—";
    /// <summary>Henüz bir repo kökü seçilmemiş.</summary>
    public const string NoRepository = "no repository";
    /// <summary>MSBuild çözümü sürüyor (vswhere child process'i).</summary>
    public const string Resolving = "resolving…";

    /// <summary>Etiket ile değer arasındaki boşluk (pano metni hizalaması).</summary>
    private const string Gutter = "  ";

    public static IReadOnlyList<DiagnosticsLine> Compose(DiagnosticsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return
        [
            new("App version", input.AppVersion),
            new("Engine version", Or(input.EngineVersion, NotStarted)),
            new("Engine PID", input.EnginePid is { } pid ? pid.ToString(CultureInfo.InvariantCulture) : Unknown),
            new(".NET runtime", input.Runtime),
            new("OS", input.Os),
            new("MSBuild", input.MsBuild),
            new("Repository root", Or(input.RepositoryRoot, NoRepository)),
            new("State file", input.StateFile),
            new("Logs", input.LogsRoot),
            new("Worktree pool", input.WorktreePool),
        ];
    }

    /// <summary>Panoya gidecek düz metin — değerler TEK kolonda hizalanır (bir destek talebine yapıştırılır).
    /// Satır ayracı <see cref="StringBuilder.AppendLine()"/>'ın platform ayracıdır.</summary>
    public static string ToText(IReadOnlyList<DiagnosticsLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0) return "";

        int width = lines.Max(l => l.Label.Length);
        var text = new StringBuilder();
        foreach (var line in lines)
            text.Append(line.Label.PadRight(width)).Append(Gutter).AppendLine(line.Value);
        return text.ToString();
    }

    private static string Or(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
