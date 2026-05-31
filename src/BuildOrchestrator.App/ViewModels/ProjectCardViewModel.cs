using BuildOrchestrator.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// View model for a single project card (Section 7). Status drives card color and which animation
/// plays (pulse while building, glow on success, shake on failure, fade+desaturate when skipped).
/// </summary>
public sealed partial class ProjectCardViewModel : ObservableObject
{
    public ProjectCardViewModel(ProjectNode node)
    {
        Id = node.Id;
        Name = node.Name;
        SolutionName = node.SolutionName;
        ProjectPath = node.ProjectPath;
        BuildOrder = node.BuildOrder;
        IsInCycle = node.IsInCycle;
        CycleMembersTooltip = node.IsInCycle
            ? "Cycle: " + string.Join(" → ", node.CycleMembers.Select(System.IO.Path.GetFileNameWithoutExtension))
            : string.Empty;
        Status = node.IsInCycle ? ProjectStatus.CycleDetected : ProjectStatus.Discovered;
    }

    public string Id { get; }
    public string Name { get; }
    public string SolutionName { get; }
    public string ProjectPath { get; }
    public int BuildOrder { get; }
    public bool IsInCycle { get; }
    public string CycleMembersTooltip { get; }

    [ObservableProperty]
    private ProjectStatus _status;

    [ObservableProperty]
    private long _elapsedMs;

    [ObservableProperty]
    private string? _failureReason;

    /// <summary>True when this card is the active/focused build (drives carousel emphasis).</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>True when filtered out; the UI collapses it with an opacity animation.</summary>
    [ObservableProperty]
    private bool _isFilteredOut;

    public bool IsSkipped => Status == ProjectStatus.Skipped;
    public bool IsFailed => Status == ProjectStatus.Failed;

    partial void OnStatusChanged(ProjectStatus value)
    {
        OnPropertyChanged(nameof(IsSkipped));
        OnPropertyChanged(nameof(IsFailed));
    }

    public void Reset()
    {
        FailureReason = null;
        ElapsedMs = 0;
        IsActive = false;
        Status = IsInCycle ? ProjectStatus.CycleDetected : ProjectStatus.Discovered;
    }
}
