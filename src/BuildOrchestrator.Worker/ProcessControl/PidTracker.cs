using System.Collections.Concurrent;
using System.Diagnostics;

namespace BuildOrchestrator.Worker.ProcessControl;

/// <summary>
/// Second safety net (Section 6.1 rule 4): tracks PIDs of build-related processes the Worker is
/// responsible for and sweeps them at the end of each run and on exit. The Job Object is the primary
/// guarantee; this catches anything that somehow escaped assignment.
/// </summary>
public sealed class PidTracker
{
    private static readonly string[] BuildProcessNames =
    {
        "MSBuild", "VBCSCompiler", "dotnet", "csc", "vbc"
    };

    private readonly ConcurrentDictionary<int, byte> _tracked = new();
    private readonly DateTime _startedAt = DateTime.UtcNow;

    public void Track(int pid) => _tracked.TryAdd(pid, 0);

    public IReadOnlyCollection<int> Tracked => _tracked.Keys.ToList();

    /// <summary>Kills explicitly-tracked PIDs and their process trees.</summary>
    public void SweepTracked()
    {
        foreach (var pid in _tracked.Keys.ToList())
        {
            KillTree(pid);
            _tracked.TryRemove(pid, out _);
        }
    }

    /// <summary>
    /// Best-effort sweep of build-helper processes (notably VBCSCompiler) started after the Worker
    /// began. Only used as a fallback when Job Objects are unavailable.
    /// </summary>
    public void SweepStragglers()
    {
        if (OperatingSystem.IsWindows() is false)
        {
            return;
        }

        foreach (var name in BuildProcessNames)
        {
            Process[] procs;
            try
            {
                procs = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var p in procs)
            {
                try
                {
                    if (p.StartTime.ToUniversalTime() >= _startedAt)
                    {
                        KillTree(p.Id);
                    }
                }
                catch
                {
                    // process may have exited or be inaccessible
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
    }

    private static void KillTree(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
        }
        catch
        {
            // already gone
        }
    }
}
