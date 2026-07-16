using System.Diagnostics;
using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.Logs;

namespace BuildOrchestrator.Tests.Supervisor;

public static class TestPaths
{
    public static string SupervisorExe => Path.Combine(AppContext.BaseDirectory, "BuildOrchestrator.Supervisor.exe");
}

public class SupervisorIpcTests
{
    private static ProcessStartInfo Psi(string? logsDir = null)
    {
        var psi = new ProcessStartInfo(TestPaths.SupervisorExe)
        { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (logsDir is not null) { psi.ArgumentList.Add("--logs"); psi.ArgumentList.Add(logsDir); }
        return psi;
    }

    [Fact]
    public async Task Stdout_is_ndjson_only_even_after_garbage_command() // [D4 — It-0 kabul maddesi]
    {
        using var p = Process.Start(Psi())!;
        await p.StandardInput.WriteLineAsync("""{"type":"ping","seq":1}""");
        await p.StandardInput.WriteLineAsync("bu bir NDJSON degil");
        await p.StandardInput.WriteLineAsync("""{"type":"shutdown"}""");
        string all = await p.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await p.WaitForExitAsync(new CancellationTokenSource(2000).Token);
        var lines = all.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.NotEmpty(lines);
        foreach (var line in lines) // parse edilemeyen tek satır = D4 ihlali = FAIL
            Assert.NotNull(System.Text.Json.JsonSerializer.Deserialize<IpcEvent>(line, IpcJson.Options));
        Assert.Contains(lines, l => l.Contains("\"engineReady\""));
        Assert.Contains(lines, l => l.Contains("\"pong\""));
        Assert.Contains(lines, l => l.Contains("\"badCommand\""));
        Assert.Equal(0, p.ExitCode);
    }

    [Fact]
    public async Task GetProjectLog_streams_chunks_and_missing_log_errors()
    {
        string logs = Directory.CreateTempSubdirectory("bo-logs").FullName;
        string projectId = @"d:\repo\a\a.csproj";
        await File.WriteAllTextAsync(Path.Combine(logs, ProjectLogNaming.FileNameFor(projectId)),
            string.Concat(Enumerable.Repeat(new string('x', 100) + "\n", 2000))); // ~200KB → ≥3 chunk
        using var p = Process.Start(Psi(logs))!;
        var writer = new NdjsonWriter(p.StandardInput.BaseStream);
        var reader = new NdjsonReader(p.StandardOutput.BaseStream);
        Assert.IsType<EngineReadyEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(5)));

        await writer.WriteAsync(new GetProjectLogCommand(projectId));
        var chunks = new List<ProjectLogChunkEvent>();
        while (true)
        {
            var e = Assert.IsType<ProjectLogChunkEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(5)));
            chunks.Add(e);
            if (e.IsLast) break;
        }
        Assert.True(chunks.Count >= 3);
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.Sequence));

        await writer.WriteAsync(new GetProjectLogCommand(@"d:\yok\yok.csproj"));
        var err = Assert.IsType<ErrorEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("logNotFound", err.Code);
        await writer.WriteAsync(new ShutdownCommand());
        await p.WaitForExitAsync(new CancellationTokenSource(2000).Token);
    }

    [Fact]
    public async Task StopRun_hard_terminates_inner_job_children_and_acks()
    {
        using var p = Process.Start(Psi())!;
        var writer = new NdjsonWriter(p.StandardInput.BaseStream);
        var reader = new NdjsonReader(p.StandardOutput.BaseStream);
        Assert.IsType<EngineReadyEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(5)));
        await writer.WriteAsync(new DebugSpawnChildrenCommand(Count: 1, Breakaway: false));
        var spawned = Assert.IsType<DebugChildrenSpawnedEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(10)));
        await writer.WriteAsync(new StopRunCommand("r1", StopKind.Hard)); // T4 base: hard = TerminateJobObject(inner)
        var stopped = Assert.IsType<RunStoppedEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(stopped.WasHard);
        foreach (int pid in spawned.Pids)
        {
            try { await Process.GetProcessById(pid).WaitForExitAsync(new CancellationTokenSource(2000).Token); }
            catch (ArgumentException) { /* zaten öldü */ }
        }
        await writer.WriteAsync(new ShutdownCommand());
        await p.WaitForExitAsync(new CancellationTokenSource(2000).Token);
    }
}
