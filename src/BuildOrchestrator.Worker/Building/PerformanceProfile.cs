using System.Diagnostics;
using BuildOrchestrator.Contracts;

namespace BuildOrchestrator.Worker.Building;

/// <summary>Maps a <see cref="PerformanceMode"/> to parallel degree + process priority (Section 3).</summary>
public sealed record PerformanceProfile(int MaxParallel, ProcessPriorityClass Priority)
{
    public static PerformanceProfile For(PerformanceMode mode)
    {
        int cpu = Math.Max(1, Environment.ProcessorCount);
        return mode switch
        {
            PerformanceMode.FullPower => new PerformanceProfile(cpu, ProcessPriorityClass.AboveNormal),
            PerformanceMode.Balanced => new PerformanceProfile(Math.Max(1, cpu / 2), ProcessPriorityClass.Normal),
            PerformanceMode.Light => new PerformanceProfile(Math.Min(2, cpu), ProcessPriorityClass.BelowNormal),
            _ => new PerformanceProfile(cpu, ProcessPriorityClass.Normal)
        };
    }

    public void ApplyToCurrentProcess()
    {
        try
        {
            using var p = Process.GetCurrentProcess();
            p.PriorityClass = Priority;
        }
        catch
        {
            // Priority is best-effort.
        }
    }
}
