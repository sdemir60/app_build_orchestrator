using System.Text;
using BuildOrchestrator.Worker;
using BuildOrchestrator.Worker.Building;
using BuildOrchestrator.Worker.Ipc;
using BuildOrchestrator.Worker.ProcessControl;
using Microsoft.Build.Locator;

// IMPORTANT: register MSBuild BEFORE any Microsoft.Build type is touched (Section 2).
if (!MSBuildLocator.IsRegistered)
{
    try
    {
        MSBuildLocator.RegisterDefaults();
    }
    catch
    {
        // If MSBuild cannot be located the worker still runs sync/git; builds will report errors.
    }
}

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

// Section 6.1 rule 1: bind this process (and all of its build children) to a Job Object that
// kills the whole tree when the worker exits/crashes. Orphan build processes become impossible.
using var job = new JobObject();
if (OperatingSystem.IsWindows() && job.IsValid)
    job.AssignCurrentProcess();

// Section 6.1 rules 4/5: sweep any stragglers on exit as a secondary safety net.
AppDomain.CurrentDomain.ProcessExit += (_, _) => ProcessSweeper.SweepDescendants();

// Crash resilience (Section 6.1 rule 5): if the UI passes its PID, exit when it dies so the
// Job Object closes and all build children are terminated automatically.
WatchParentProcess(args);

var channel = new IpcChannel(Console.In, Console.Out);
var host = new WorkerHost(channel);

GC.KeepAlive(typeof(MsBuildBuildEngine)); // ensure the type is available once MSBuild is registered

host.Run();

static void WatchParentProcess(string[] args)
{
    int parentPid = -1;
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i] == "--parent" && int.TryParse(args[i + 1], out var pid))
            parentPid = pid;
    if (parentPid <= 0)
        return;

    try
    {
        var parent = System.Diagnostics.Process.GetProcessById(parentPid);
        parent.EnableRaisingEvents = true;
        parent.Exited += (_, _) => Environment.Exit(0);
    }
    catch
    {
        // Parent already gone; nothing to watch.
    }
}
