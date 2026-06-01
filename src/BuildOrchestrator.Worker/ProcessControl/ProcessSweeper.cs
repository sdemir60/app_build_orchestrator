using System.Diagnostics;

namespace BuildOrchestrator.Worker.ProcessControl;

/// <summary>
/// Secondary safety net (Section 6.1, rule 4): explicitly sweep build helper processes
/// (MSBuild worker nodes, VBCSCompiler) that descend from this worker. The Job Object is the
/// primary guarantee; this catches anything that might have broken away.
/// </summary>
public static class ProcessSweeper
{
    private static readonly string[] HelperNames =
    {
        "MSBuild", "VBCSCompiler", "csc", "dotnet"
    };

    public static void SweepDescendants()
    {
        int selfId;
        using (var self = Process.GetCurrentProcess())
            selfId = self.Id;

        foreach (var name in HelperNames)
        {
            Process[] candidates;
            try
            {
                candidates = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var p in candidates)
            {
                try
                {
                    if (p.Id == selfId)
                        continue;
                    if (IsDescendantOf(p.Id, selfId))
                    {
                        p.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Process may have already exited; ignore.
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
    }

    private static bool IsDescendantOf(int childId, int ancestorId)
    {
        // Walk up the parent chain (bounded) using WMI-free Win32 via ParentProcessId.
        int current = childId;
        for (int depth = 0; depth < 16; depth++)
        {
            int parent = ParentProcessUtil.GetParentProcessId(current);
            if (parent <= 0)
                return false;
            if (parent == ancestorId)
                return true;
            current = parent;
        }
        return false;
    }
}
