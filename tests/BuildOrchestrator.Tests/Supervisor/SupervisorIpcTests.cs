using System.Diagnostics;
using System.IO;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.Supervisor;

public static class TestPaths
{
    public static string SupervisorExe => Path.Combine(AppContext.BaseDirectory, "BuildOrchestrator.Supervisor.exe");

    /// <summary>Gerçek Supervisor process'ini stdio yönlendirmeli başlatır (RunCoordinatorTests da kullanır).</summary>
    public static ProcessStartInfo Psi(string? logsDir = null)
    {
        var psi = new ProcessStartInfo(SupervisorExe)
        { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (logsDir is not null) { psi.ArgumentList.Add("--logs"); psi.ArgumentList.Add(logsDir); }
        return psi;
    }
}

public class SupervisorIpcTests
{
    private static ProcessStartInfo Psi(string? logsDir = null) => TestPaths.Psi(logsDir);

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

    // [T28] getProjectLog artık AKTİF run'ın dizininden okur (bkz. RunCoordinator.TryGetProjectLogSnapshot) —
    // gerçek run/chunk/dikiş davranışı ProjectLogStreamTests.cs'te (in-process, sahte invoker, gerçek writer)
    // test edilir. Burada yalnız gerçek Supervisor process'i üzerinden "hiç run koşmadıysa bilinmeyen proje
    // logNotFound döner + stdout NDJSON kalır" wiring'i doğrulanır (D4).
    [Fact]
    public async Task GetProjectLog_of_unknown_project_before_any_run_errors_and_stdout_stays_ndjson()
    {
        using var p = Process.Start(Psi())!;
        var writer = new NdjsonWriter(p.StandardInput.BaseStream);
        var reader = new NdjsonReader(p.StandardOutput.BaseStream);
        Assert.IsType<EngineReadyEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(5)));

        await writer.WriteAsync(new GetProjectLogCommand(@"d:\yok\yok.csproj"));
        var err = Assert.IsType<ErrorEvent>(await reader.ReadAsync<IpcEvent>().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("logNotFound", err.Code);

        await writer.WriteAsync(new ShutdownCommand());
        string rest = await p.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(5));
        foreach (var line in rest.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.NotNull(System.Text.Json.JsonSerializer.Deserialize<IpcEvent>(line, IpcJson.Options)); // D4: kalan satırlar da NDJSON
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
