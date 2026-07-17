using System.ComponentModel;
using System.Text.Json;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.Logs;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Supervisor;

public sealed class SupervisorHost(NdjsonWriter writer, NdjsonReader reader, JobObject innerJob, string logsRoot,
    RunCoordinator coordinator)
{
    private bool _running = true;
    private bool _stopRequested; // aktif run YOKKEN gelen graceful stop (run koşarken sahibi koordinatördür)

    public bool StopRequested => _stopRequested;

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        string version = typeof(SupervisorHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        await writer.WriteAsync(new EngineReadyEvent(Environment.ProcessId, version), ct);
        while (_running)
        {
            IpcCommand? cmd;
            try { cmd = await reader.ReadAsync<IpcCommand>(ct); }
            catch (IpcFramingException ex)
            { await writer.WriteAsync(new ErrorEvent("framing", ex.Message), ct); return 2; } // kurtarılamaz
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            { await writer.WriteAsync(new ErrorEvent("badCommand", ex.Message), ct); continue; } // satır tüketildi, devam
            if (cmd is null) return 0; // stdin EOF → App kapandı (outer Job zaten süpürür; bu düzenli çıkış)
            await DispatchAsync(cmd, ct);
        }
        return 0;
    }

    private async Task DispatchAsync(IpcCommand cmd, CancellationToken ct)
    {
        switch (cmd)
        {
            case PingCommand p:
                await writer.WriteAsync(new PongEvent(p.Seq), ct); break;
            case ShutdownCommand:
                _running = false; break;
            case StartRunCommand s:
                await coordinator.StartAsync(s, ct); break; // hemen döner — run arka planda koşar, loop komut almaya devam eder
            case StopRunCommand s:
                await StopRunAsync(s, ct); break;
            case GetProjectLogCommand g:
                await SendProjectLogAsync(g, ct); break;
            case DebugSpawnChildrenCommand d:
                await SpawnDebugChildrenAsync(d, ct); break;
            default:
                await writer.WriteAsync(new ErrorEvent("unknownCommand", cmd.GetType().Name), ct); break;
        }
    }

    /// <summary>
    /// [I2-K1] Aktif bir run varsa Stop'un SAHİBİ koordinatördür: hard'da inner Job'ı O terminate eder ve
    /// <c>runStopped</c>'ı in-flight projelerin sonuçları raporlandıktan SONRA O yazar (kısıt: "öldürüldü" ≠
    /// "raporlandı"). Aktif run yoksa T4-base davranışı korunur: hard → job terminate + anında ack.
    /// TryRequestStop ATOMİKTİR — "aktif mi?" kontrolü ile sahiplenme arasında yarış penceresi yoktur, bu yüzden
    /// runStopped tam olarak bir kez (ya koordinatörden ya buradan) yazılır.
    /// </summary>
    private async Task StopRunAsync(StopRunCommand s, CancellationToken ct)
    {
        if (coordinator.TryRequestStop(s.Kind)) return;
        if (s.Kind == StopKind.Hard) innerJob.Terminate();
        else _stopRequested = true;
        await writer.WriteAsync(new RunStoppedEvent(s.RunId, WasHard: s.Kind == StopKind.Hard), ct);
    }

    private async Task SendProjectLogAsync(GetProjectLogCommand g, CancellationToken ct)
    {
        string path = Path.Combine(logsRoot, ProjectLogNaming.FileNameFor(g.ProjectId));
        if (!File.Exists(path))
        { await writer.WriteAsync(new ErrorEvent("logNotFound", g.ProjectId), ct); return; }
        string text = await File.ReadAllTextAsync(path, ct);
        foreach (var c in LogChunker.Chunk(text))
            await writer.WriteAsync(new ProjectLogChunkEvent(g.ProjectId, c.Sequence, c.Text, c.IsLast, 0), ct);
    }

    private async Task SpawnDebugChildrenAsync(DebugSpawnChildrenCommand d, CancellationToken ct)
    {
        var pids = new List<int>();
        for (int i = 0; i < d.Count; i++)
        {
            try
            {
                string cmdLine = WindowsCommandLine.Build(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    "/c", "powershell -NoProfile -Command Start-Sleep -Seconds 300");
                var child = JobProcessLauncher.Launch(innerJob, cmdLine, new LaunchOptions(Breakaway: d.Breakaway));
                pids.Add(child.Pid);
            }
            catch (Win32Exception ex) // breakaway probe: NativeErrorCode==5 (ERROR_ACCESS_DENIED) beklenir
            { await writer.WriteAsync(new ErrorEvent("spawnFailed", $"win32={ex.NativeErrorCode}"), ct); return; }
        }
        await writer.WriteAsync(new DebugChildrenSpawnedEvent(pids.ToArray()), ct);
    }
}
