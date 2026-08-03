using System.Diagnostics;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Processes;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// "Open in Visual Studio" ÇAĞIRAN THREAD'İ bloke etmez.
///
/// <para><b>Ölçülen kusur:</b> <c>OsActions.ResolveDevenv</c> <c>vswhere</c>'i
/// <c>Task.Run(...).GetAwaiter().GetResult()</c> ile SENKRON bekliyordu ve bu yol bir satırın hover ikonundan
/// (yani UI thread'inden) çağrılıyordu. <c>vswhere</c> soğuk makinede saniyeler sürebilir; spec'in kendi
/// timeout'u <b>30 saniyedir</b> — yani tek bir tıklama pencereyi 30 saniyeye kadar ölü bırakabilirdi.</para>
///
/// <para>Ayrıca <c>devenv</c> yolu oturum boyunca DEĞİŞMEZ: ikinci bir açılış <c>vswhere</c>'i yeniden
/// çalıştırmamalıdır.</para>
/// </summary>
public class OsActionsBlockingTests(ITestOutputHelper output)
{
    /// <summary>Çağıran thread'in bloke kalabileceği tavan. vswhere'in kendi süresi (burada 300 ms) buna
    /// GİRMEMELİDİR — iş arka planda koşar.</summary>
    private const double BudgetMs = 50;

    private const int VswhereDelayMs = 300;

    private sealed class SlowRunner(string stdout) : IProcessRunner
    {
        public int Calls;
        public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(VswhereDelayMs, ct);
            return new ProcessResult(0, stdout, "", TimeSpan.Zero, false);
        }
    }

    private sealed class NullLauncher : IProcessLauncher
    {
        public int Count;
        public void Launch(ProcessStartInfo startInfo) => Count++;
    }

    [Fact]
    public async Task Opening_a_solution_does_not_block_the_calling_thread_while_vswhere_runs()
    {
        var launcher = new NullLauncher();
        var runner = new SlowRunner(@"C:\VS\devenv.exe");
        var os = new OsActions(launcher, runner, vswherePath: FakeVswhere());

        var sw = Stopwatch.StartNew();
        var pending = os.OpenInVisualStudioAsync([new SolutionRef("Osys", @"C:\src\Osys.sln")]);
        double blocked = sw.Elapsed.TotalMilliseconds;

        var result = await pending;
        output.WriteLine($"[open-in-vs] çağıran thread {blocked:N1} ms bloke (vswhere {VswhereDelayMs} ms sürdü)");

        Assert.True(blocked < BudgetMs,
            $"çağıran thread {blocked:N1} ms bloke oldu — bütçe {BudgetMs:N0} ms (vswhere arka planda koşmalı).");
        Assert.Equal(OpenInVsOutcome.Opened, result.Outcome);
        Assert.Equal(1, launcher.Count);
    }

    [Fact]
    public async Task The_resolved_devenv_path_is_reused_instead_of_running_vswhere_again()
    {
        var runner = new SlowRunner(@"C:\VS\devenv.exe");
        var os = new OsActions(new NullLauncher(), runner, vswherePath: FakeVswhere());
        var sln = new SolutionRef("Osys", @"C:\src\Osys.sln");

        await os.OpenInVisualStudioAsync([sln]);
        await os.OpenInVisualStudioAsync([sln]);

        Assert.Equal(1, runner.Calls); // devenv yolu oturum boyunca sabittir — ikinci sorgu YOK
    }

    /// <summary>vswhere'in VAR olduğu bir yol gerekir (<c>OsActions</c> yoksa sorguyu hiç koşmaz) — bu derlemenin
    /// kendi dosyası kullanılır; gerçekten çalıştırılmaz, çünkü runner enjekte edilmiştir.</summary>
    private static string FakeVswhere() => typeof(OsActionsBlockingTests).Assembly.Location;
}
