using System.Globalization;
using System.Text;

namespace BuildOrchestrator.App.Services;

/// <summary>[About] Tanı tablosunun bir satırı: etiket + gösterilecek değer.</summary>
public readonly record struct DiagnosticsLine(string Label, string Value);

/// <summary>
/// [About] Tanı raporunun ham girdisi — TOPLAMAYI çağıran yapar (<see cref="AppIdentity"/>,
/// <c>RuntimeInformation</c>, <c>MsBuildResolver</c>, uygulamanın kendi yol static'leri), bu tip yalnız taşır.
/// Böylece rapor mantığı WPF'siz ve process'siz test edilir.
/// </summary>
public sealed record DiagnosticsInput(
    string Product,
    string Version,
    string Copyright,
    string? EngineVersion,
    int? EnginePid,
    string Runtime,
    string Os,
    string MsBuild,
    string RepositoryRoot,
    string StateFile,
    string LogsRoot);

/// <summary>
/// [design v1.19.0 §2.10] Tanı modelinin gruplu hâli. About sekmesi <see cref="Identity"/>'yi, Environment sekmesi
/// <see cref="Runtime"/> ile <see cref="Paths"/>'i çizer; "Copy diagnostics" <see cref="DiagnosticsReport.ToText"/>
/// ile AYNI nesneden metin üretir. Bir değer iki grupta geçmez.
/// </summary>
public sealed record DiagnosticsSnapshot(
    string Product,
    DiagnosticsLine Version,
    DiagnosticsLine Engine,
    DiagnosticsLine Copyright,
    IReadOnlyList<DiagnosticsLine> Runtime,
    IReadOnlyList<DiagnosticsLine> Paths)
{
    /// <summary>About sekmesinin satırları: Version · Engine · Copyright.</summary>
    public IReadOnlyList<DiagnosticsLine> Identity => [Version, Engine, Copyright];
}

/// <summary>
/// [About] About'un kimlik satırları, Environment sekmesinin iki grubu ve "Copy diagnostics"in panoya yazdığı
/// metin — TEK modelden. Ayrı listelerden üretilselerdi biri güncellenip diğeri unutulurdu.
///
/// <para>SAF: hiçbir şey okumaz, hiçbir process başlatmaz. Etiket ve grup başlığı metinleri BURADA tanımlanır;
/// XAML onları tekrar yazmaz (satırlar <c>ItemsControl</c>'lerle çizilir).</para>
///
/// <para><b>[DEĞİŞEN KURAL — design v1.19.0 §2.10]</b> ESKİ MODEL tek düz listeydi (<c>App version</c>,
/// <c>Engine version</c>, <c>Engine PID</c> … <c>Worktree pool</c>) ve Environment sekmesi onun tamamını çizerdi.
/// Sürümler About sekmesine taşındı (Version · Engine · Copyright); Environment RUNTIME ve PATHS gruplarıdır.</para>
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

    /// <summary>Environment sekmesinin grup başlıkları — caps olarak çizilir.</summary>
    public const string RuntimeTitle = "Runtime";
    public const string PathsTitle = "Paths";

    /// <summary>Etiket ile değer arasındaki boşluk (pano metni hizalaması).</summary>
    private const string Gutter = "  ";

    public static DiagnosticsSnapshot Compose(DiagnosticsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new DiagnosticsSnapshot(
            input.Product,
            Version: new("Version", input.Version),
            Engine: new("Engine", Or(input.EngineVersion, NotStarted)),
            Copyright: new("Copyright", input.Copyright),
            Runtime:
            [
                new("Engine PID", input.EnginePid is { } pid ? pid.ToString(CultureInfo.InvariantCulture) : Unknown),
                new(".NET runtime", input.Runtime),
                new("OS", input.Os),
            ],
            Paths:
            [
                new("MSBuild", input.MsBuild),
                new("Repository root", Or(input.RepositoryRoot, NoRepository)),
                new("State file", input.StateFile),
                new("Logs", input.LogsRoot),
            ]);
    }

    /// <summary>
    /// Panoya gidecek düz metin (bir destek talebine yapıştırılır): ilk satır <c>{ürün} {sürüm}</c> — çıktının
    /// nereden geldiğini söyler — ardından <c>Engine</c> + Runtime + Paths satırları, değerler TEK kolonda hizalı.
    /// Telif panoya GİTMEZ: bir destek talebinde bilgi taşımaz. Satır ayracı
    /// <see cref="StringBuilder.AppendLine()"/>'ın platform ayracıdır.
    /// </summary>
    public static string ToText(DiagnosticsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        List<DiagnosticsLine> lines = [snapshot.Engine, .. snapshot.Runtime, .. snapshot.Paths];
        int width = lines.Max(l => l.Label.Length);

        var text = new StringBuilder()
            .Append(snapshot.Product).Append(' ').AppendLine(snapshot.Version.Value);
        foreach (var line in lines)
            text.Append(line.Label.PadRight(width)).Append(Gutter).AppendLine(line.Value);
        return text.ToString();
    }

    private static string Or(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
