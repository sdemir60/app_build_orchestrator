using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.ProcessControl;

namespace BuildOrchestrator.Supervisor;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var stdout = Console.OpenStandardOutput();
        var stdin = Console.OpenStandardInput();
        Console.SetOut(Console.Error); // [D4] guard: kaçak Console.WriteLine stderr'e — stdout YALNIZ NDJSON

        string logsRoot = GetArg(args, "--logs") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BuildOrchestrator", "logs");
        Directory.CreateDirectory(logsRoot);

        using var innerJob = JobObject.CreateKillOnClose(); // §3: inner Job — MSBuild child'ları burada yaşayacak
        var host = new SupervisorHost(new NdjsonWriter(stdout), new NdjsonReader(stdin), innerJob, logsRoot);
        return await host.RunAsync();
    }

    private static string? GetArg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
