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

    /// <summary>[kaynak şeridi] Önce stdin'in kapanmasını bekleyen (<c>more</c> girdiyi EOF'a dek okur; <c>LaunchOptions(RedirectStdio:
    /// true)</c> ister), sonra SINIRLI bir işlemci yükü yakıp kendiliğinden çıkan cmd. Bekleyiş çocuğu canlı bir job üyesi olarak
    /// tutar (uyku/poll yok: boru üzerinde bloklu okuma), stdin'i kapatmak yükü başlatır ve çocuk biter. Döngü sonsuz DEĞİLDİR:
    /// <see cref="BusyLoopChildCmdLine"/>'ın aksine çocuk kendi çıkar, yani "çıkmış üyenin işlemci zamanı job'da kalır" sorusu sorulabilir.
    /// Yük bu makinede ~0,1 sn işlemci zamanıdır (job muhasebesi ~15,6 ms'lik saat tikleriyle ilerler; yük bir tikten çok büyüktür) ve
    /// yavaş bir makinede yalnız uzar. <c>pause</c> ve <c>set /p</c> bekleyiş OLAMAZ: ölçüldü, yönlendirilmiş stdin'de beklemeden
    /// dönerler (<c>more</c>, <c>sort</c>, <c>findstr</c> bekler).</summary>
    public static string GatedBurnChildCmdLine() => WindowsCommandLine.Build(
        CmdExe, "/c", "more >nul & for /l %i in (1,1,500000) do @rem");
}
