using System.IO;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Tests.ProcessControl;

/// <summary>
/// Job Object testlerinin ORTAK child komut satırları. Üç test sınıfı (<see cref="JobObjectTests"/>,
/// <see cref="HandleInheritanceTests"/>, <see cref="JobCpuRateTests"/>) aynı satırları kopyalıyordu;
/// tek kaynağa çekildi.
/// </summary>
internal static class JobTestChildren
{
    public static string CmdExe => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    /// <summary>cmd + powershell torunu: nested üyelik mirasını (2 doğum bildirimi) ve uzun ömrü sağlar.</summary>
    public static string SleepChildCmdLine() => WindowsCommandLine.Build(
        CmdExe, "/c", "powershell -NoProfile -Command Start-Sleep -Seconds 300");

    /// <summary>[T20-a] Tek process'te tek çekirdeği doyuran cmd döngüsü (step 0 ⇒ sonsuz). Torun süreç
    /// doğurmaz, bu yüzden <see cref="System.Diagnostics.Process.TotalProcessorTime"/> child'ın TÜM
    /// tüketimini kapsar (ölçülen: 1200ms duvar saatinde 1171ms CPU — tam bir çekirdek).</summary>
    public static string BusyLoopChildCmdLine() => WindowsCommandLine.Build(
        CmdExe, "/c", "for /l %i in (1,0,2) do @rem");
}
