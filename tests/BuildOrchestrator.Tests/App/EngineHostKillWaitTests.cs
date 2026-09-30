using System.Diagnostics;
using System.IO;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>[motor · Task 11 · fix-1 · final review #14] <see cref="EngineHost.KillAndAwaitExit"/>'in bekleme ilkesi.
/// <para><b>Neden ayrı sınıf, neden seri collection:</b> bu test gerçek zamana bağlıdır — 100 ms gecikmeli bir öldürme,
/// 1 s'lik <see cref="EngineHost.KillExitWait"/> bütçesi. Yön deterministiktir (bekleyen ilke dönüşte process'in
/// sonlandığını görür, beklemeyen görmez) ve pay 10×'tir; ama tam süitin paralel yükünde pay erir ve gecikmeli
/// öldürme ürün kusuru olmadan bütçenin dışına düşebilir (zamanlama testlerinin bilinen deseni — eşik gevşetilmez).
/// <c>Console UI (serial)</c> collection'ı (<c>DisableParallelization</c>) başka hiçbir collection ile eşzamanlı
/// koşmaz; xUnit'te <c>[Collection]</c> sınıf düzeyinde olduğundan test <see cref="EngineHostTests"/>'ten ayrıldı
/// (oradaki testler gerçek motor başlatır ve paralel kalır).</para></summary>
[Collection("Console UI (serial)")] // gerçek zamana bağlı — bkz. ConsoleUiSerialCollection
public class EngineHostKillWaitTests
{
    /// <summary>[motor · Task 11 · fix-1] Öldürme + bekleme ilkeli (<see cref="EngineHost.KillAndAwaitExit"/>) ancak
    /// process GERÇEKTEN sonlandıktan sonra döner — öldürmenin etkisi ne kadar geç inerse insin (bütçe
    /// <see cref="EngineHost.KillExitWait"/>). Beklemeyi pinleyen test budur;
    /// <see cref="EngineHostTests.Dispose_waits_for_the_supervisor_process_to_exit"/> üretim yolunu uçtan uca koşar ama
    /// ağaç öldürmenin taraması yarışı örttüğü için beklemesiz kodda da yeşildir.
    /// <para><b>Neden geç inen öldürme:</b> <c>TerminateProcess</c> asenkrondur, gerçek yarış birkaç ms'dir ve
    /// makineye bağlıdır. Öldürme stratejisi dikişin parametresidir; test gerçek bir child'ı (<c>PING.EXE</c>) 100 ms
    /// SONRA öldüren bir strateji verir — yarış deterministik olur: ilke beklemeseydi dönüşte process kesin yaşar.</para></summary>
    [Fact]
    public async Task Kill_and_await_exit_returns_only_after_the_process_has_ended()
    {
        using var child = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "PING.EXE"), "-n 30 127.0.0.1")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        var lateKill = Task.CompletedTask;
        try
        {
            EngineHost.KillAndAwaitExit(child, p => lateKill = KillAfterAsync(p, TimeSpan.FromMilliseconds(100)));

            Assert.True(child.WaitForExit(TimeSpan.Zero), "KillAndAwaitExit döndüğünde process hâlâ sonlanmamıştı");
        }
        finally
        {
            await lateKill; // Process nesnesi dispose edilmeden önce geç öldürme insin
            child.Kill();   // çıkmışsa no-op
            child.WaitForExit();
        }
    }

    /// <summary>Etkisi <paramref name="delay"/> sonra inen bir öldürme — asenkron sonlanmanın abartılmış hâli.</summary>
    private static async Task KillAfterAsync(Process process, TimeSpan delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        process.Kill();
    }
}
