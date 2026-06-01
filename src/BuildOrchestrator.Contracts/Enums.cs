namespace BuildOrchestrator.Contracts;

/// <summary>Lifecycle/visual status of a project card.</summary>
public enum ProjectStatus
{
    Discovered,
    Queued,
    Building,
    Succeeded,
    Failed,
    Skipped,
    CycleDetected
}

public enum BuildMode
{
    Build,
    Rebuild
}

public enum BuildConfiguration
{
    Debug,
    Release
}

/// <summary>Downstream (dependent) propagation policy.</summary>
public enum DependentMode
{
    Safe,
    Fast
}

/// <summary>Parallelism + process priority profile.</summary>
public enum PerformanceMode
{
    FullPower,
    Balanced,
    Light
}

/// <summary>How a selected branch is materialized for building.</summary>
public enum BranchWorkMode
{
    Worktree,
    CheckoutInPlace
}

public enum LogLevel
{
    ErrorsOnly,
    Full
}
