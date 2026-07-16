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
}
