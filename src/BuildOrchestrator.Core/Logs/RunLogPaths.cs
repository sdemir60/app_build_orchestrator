using System.Globalization;

namespace BuildOrchestrator.Core.Logs;

/// <summary>[D4] Per-run disk log konumları. Bellek ring buffer YOKTUR — tek kaynak disktir.</summary>
public static class RunLogPaths
{
    public static string DefaultLogsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BuildOrchestrator", "logs");

    public static string RunDirName(DateTimeOffset ts) =>
        "run-" + ts.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
}
