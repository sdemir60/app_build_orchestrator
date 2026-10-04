using System.Globalization;

namespace BuildOrchestrator.Core.Logs;

/// <summary>[D4] Per-run disk log konumları. Bellek ring buffer YOKTUR — tek kaynak disktir.</summary>
public static class RunLogPaths
{
    private const string RunDirPrefix = "run-";
    private const string RunDirStampFormat = "yyyyMMdd-HHmmss-fff";

    /// <summary>Dizin listelemede koşu klasörlerini ön-süzen arama deseni (<c>run-*</c>). Ön ekten TÜRER — kalıp tek yerde
    /// durur; asıl karar yine <see cref="TryParseRunDirName"/>'dedir, bu yalnız listeyi daraltır.</summary>
    internal const string RunDirSearchPattern = RunDirPrefix + "*";

    public static string DefaultLogsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BuildOrchestrator", "logs");

    public static string RunDirName(DateTimeOffset ts) =>
        RunDirPrefix + ts.ToString(RunDirStampFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// [PERF Faz C/C2] <see cref="RunDirName"/>'in tersi: bir klasör ADININ (yol değil) koşu klasörü kalıbına TAM uyup
    /// uymadığını söyler, uyuyorsa koşunun başladığı anı verir. Kalıp tek yerde durur (yukarıdaki sabitler) — saklama
    /// budaması neyin koşu klasörü olduğunu buradan öğrenir ve başka hiçbir klasöre dokunmaz.
    /// Damga, yazıcıya verilen anın DUVAR SAATİDİR; <c>RunCoordinator</c> onu yerel saatle (<c>DateTimeOffset.Now</c>)
    /// verdiği için yerel saat dilimi olarak yorumlanır.
    /// </summary>
    public static bool TryParseRunDirName(string name, out DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(name);
        startedAt = default;
        if (!name.StartsWith(RunDirPrefix, StringComparison.Ordinal)) return false;
        if (!DateTime.TryParseExact(name.AsSpan(RunDirPrefix.Length), RunDirStampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var wall)) return false;
        try
        {
            startedAt = new DateTimeOffset(wall, TimeZoneInfo.Local.GetUtcOffset(wall));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false; // DateTime uçlarındaki (0001/9999) bir damga yerel ofsetle DateTimeOffset aralığından taşar
        }
    }
}
