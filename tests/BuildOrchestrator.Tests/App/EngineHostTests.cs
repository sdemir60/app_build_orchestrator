using System.Diagnostics;
using System.IO;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

public class EngineHostTests
{
    [Fact]
    public async Task Start_receives_engineReady_and_ping_pong_works()
    {
        await using var host = new EngineHost(TestPaths.SupervisorExe);
        var ready = await host.StartAsync();
        Assert.Equal(host.EnginePid, ready.Pid);
        var pong = new TaskCompletionSource<PongEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.EventReceived += e => { if (e is PongEvent p) pong.TrySetResult(p); };
        await host.SendAsync(new PingCommand(42));
        Assert.Equal(42, (await pong.Task.WaitAsync(TimeSpan.FromSeconds(5))).Seq);
    }

    [Fact]
    public async Task Supervisor_kill_raises_EngineExited_and_restart_recovers() // T6
    {
        await using var host = new EngineHost(TestPaths.SupervisorExe);
        var ready1 = await host.StartAsync();
        var exited = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.EngineExited += code => exited.TrySetResult(code);
        Process.GetProcessById(ready1.Pid).Kill(); // crash simülasyonu
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(2)); // handle-wait ile deterministik tespit
        var ready2 = await host.RestartAsync();
        Assert.NotEqual(ready1.Pid, ready2.Pid);
        var pong = new TaskCompletionSource<PongEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.EventReceived += e => { if (e is PongEvent p) pong.TrySetResult(p); };
        await host.SendAsync(new PingCommand(1));
        Assert.Equal(1, (await pong.Task.WaitAsync(TimeSpan.FromSeconds(5))).Seq);
    }

    [Fact]
    public async Task StartAsync_timeout_disposes_child_and_no_leak()
    {
        // Var olmayan exe → child hızla ölür; StartAsync 5sn timeout'a düşmeden EngineExited ya da hata dönmeli.
        await using var host = new EngineHost(Path.Combine(AppContext.BaseDirectory, "does-not-exist.exe"));
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await host.StartAsync(new CancellationTokenSource(TimeSpan.FromSeconds(6)).Token));
        Assert.Null(host.EnginePid); // child referansı sızmadı/temizlendi
    }
}
