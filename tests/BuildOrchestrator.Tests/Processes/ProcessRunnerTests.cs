using System.Diagnostics;
using BuildOrchestrator.Core.Processes;
using Xunit;

namespace BuildOrchestrator.Tests.Processes;

public class ProcessRunnerTests
{
    private readonly ProcessRunner _runner = new();

    [Fact]
    public async Task Captures_exit_code()
    {
        var r = await _runner.RunAsync(new ProcessSpec("cmd.exe", ["/c", "exit 3"]));
        Assert.Equal(3, r.ExitCode); Assert.False(r.Success); Assert.False(r.TimedOut);
    }

    [Fact]
    public async Task Captures_stdout_and_stderr()
    {
        var r = await _runner.RunAsync(new ProcessSpec("cmd.exe", ["/c", "echo out& echo err 1>&2"]));
        Assert.Contains("out", r.StandardOutput); Assert.Contains("err", r.StandardError); Assert.True(r.Success);
    }

    [Fact]
    public async Task Timeout_kills_process_tree_and_reports()
    {
        var r = await _runner.RunAsync(new ProcessSpec("powershell.exe",
            ["-NoProfile", "-Command", "Start-Sleep -Seconds 60"], Timeout: TimeSpan.FromMilliseconds(500)));
        Assert.True(r.TimedOut); Assert.False(r.Success);
        Assert.True(r.Elapsed < TimeSpan.FromSeconds(10), $"timeout gecikti: {r.Elapsed}");
    }

    [Fact]
    public async Task Caller_cancellation_kills_child_and_throws_OperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(300));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _runner.RunAsync(new ProcessSpec("powershell.exe",
                ["-NoProfile", "-Command", "Start-Sleep -Seconds 60"]), cts.Token));
        sw.Stop();

        // Cancellation surfaces promptly and the 60s child is not waited out (proves kill, not natural exit).
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"iptal gecikti: {sw.Elapsed}");
    }

    [Fact]
    public async Task RunAsync_timeout_kills_and_returns_bounded_TimedOut()
    {
        var runner = new ProcessRunner();
        // 30sn uyuyan child'ı 500ms timeout ile öldür; test 30sn beklememeli
        var spec = new ProcessSpec("cmd.exe",
            ["/c", "powershell -NoProfile -Command Start-Sleep -Seconds 30"],
            Timeout: TimeSpan.FromMilliseconds(500));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await runner.RunAsync(spec).WaitAsync(TimeSpan.FromSeconds(10)); // aşım = FAIL
        sw.Stop();
        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);
        Assert.True(sw.ElapsedMilliseconds < 10_000, $"kill-path {sw.ElapsedMilliseconds}ms — bounded olmalı");
    }

    /// <summary>Kusur (CI, 2026-10-13, windows-2025, .NET 10.0.12): <c>RunAsync</c>, <c>WaitForExitAsync</c> normal döndükten hemen
    /// sonra <c>ExitCode</c>'u okurken "Process must exit before requested information can be determined" attı — <c>HasExited</c> o an
    /// false'tu (SyncWorkspaceServiceTests'te bir git commit'i; aynı commit'in yeniden koşusu yeşildi). .NET'in Exited olayı ile
    /// <c>HasExited</c>'in sorduğu süreç tanıtıcısı runtime içinde nadiren ayrışıyor; yerelde 2000 süreçlik sondada üretilemedi
    /// (<see cref="Under_load_the_exit_code_is_always_readable_after_the_wait"/>). Çıkış kodu artık sürecin KENDİ tanıtıcısı çıkışı
    /// onaylayana kadar beklenerek okunur: erken dönen bir bekleyiş istisna değil, birkaç milisaniyelik ek bekleme olur. Test erken
    /// dönmüş bir bekleyişin ardını kurar — yardımcı, henüz koşan bir süreçte çağrılır.</summary>
    [Fact]
    public async Task The_exit_code_waits_for_the_process_to_confirm_its_exit_instead_of_throwing()
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 2 127.0.0.1 >nul & exit 4")
        {
            UseShellExecute = false, CreateNoWindow = true,
        })!;
        Assert.False(process.HasExited); // ~1 sn koşar: bekleyişin erken döndüğü an

        int code = await ProcessExit.ExitCodeOnceExitedAsync(process, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(4, code);
    }

    /// <summary>Onay beklemesi iptale (ve <c>RunAsync</c>'te zaman aşımına) uyar: çıkmayan bir süreçte sonsuza dek beklemez,
    /// <see cref="OperationCanceledException"/> ile çıkar — zaman aşımı yolu (kill) bu yüzden onu da kapsar.</summary>
    [Fact]
    public async Task Confirming_the_exit_honours_cancellation()
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 30 127.0.0.1 >nul")
        {
            UseShellExecute = false, CreateNoWindow = true,
        })!;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProcessExit.ExitCodeOnceExitedAsync(process, cts.Token));
        }
        finally
        {
            process.Kill(entireProcessTree: true);
        }
    }

    /// <summary>[sonda · <c>BO_PROBE_PROCESS_EXIT=1</c>] CI'da (windows-2025, .NET 10.0.12) bir kez görüldü: <c>RunAsync</c>,
    /// <c>WaitForExitAsync</c> normal döndükten hemen sonra <c>process.ExitCode</c>'u okurken "Process must exit before requested
    /// information can be determined" attı (SyncWorkspaceServiceTests'te bir git commit'i). Sonda aynı yolu yük altında çok sayıda
    /// kısa ömürlü süreçle zorlar ve düşüşleri sayar; adet <c>BO_PROBE_PROCESS_EXIT_COUNT</c> ile verilir (varsayılan 2000).
    /// Varsayılan koşuda ÇALIŞMAZ: yüzlerce süreç doğurur, CPU yakar.</summary>
    [SkippableFact]
    [Trait("Category", "Measurement")]
    public async Task Under_load_the_exit_code_is_always_readable_after_the_wait()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("BO_PROBE_PROCESS_EXIT") == "1",
            "Spawns hundreds of processes — opt in with BO_PROBE_PROCESS_EXIT=1.");
        int total = int.TryParse(Environment.GetEnvironmentVariable("BO_PROBE_PROCESS_EXIT_COUNT"), out int n) ? n : 2000;
        int failures = 0, wrongCode = 0;
        string? first = null;
        await Parallel.ForEachAsync(Enumerable.Range(0, total), new ParallelOptions { MaxDegreeOfParallelism = 32 }, async (_, _) =>
        {
            try
            {
                var r = await _runner.RunAsync(new ProcessSpec("cmd.exe", ["/c", "exit 3"]));
                if (r.ExitCode != 3) Interlocked.Increment(ref wrongCode);
            }
            catch (InvalidOperationException ex)
            {
                Interlocked.Increment(ref failures);
                Interlocked.CompareExchange(ref first, ex.ToString(), null);
            }
        });
        Assert.True(failures == 0 && wrongCode == 0,
            $"{failures}/{total} InvalidOperationException, {wrongCode} wrong exit code. First: {first}");
    }
}
