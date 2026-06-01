using BuildOrchestrator.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>One project card in the left list (Section 7).</summary>
public sealed partial class ProjectCardViewModel : ObservableObject
{
    public ProjectCardViewModel(ProjectNode node)
    {
        Id = node.Id;
        Name = node.Name;
        SolutionName = node.SolutionName;
        ProjectPath = node.ProjectPath;
        InCycle = node.InCycle;
        CycleTooltip = node.InCycle
            ? "Cycle: " + string.Join(" \u2192 ", node.CycleMembers)
            : string.Empty;
        BuildOrder = node.BuildOrder;
        Status = node.InCycle ? ProjectStatus.CycleDetected : ProjectStatus.Discovered;
    }

    public string Id { get; }
    public string Name { get; }
    public string SolutionName { get; }
    public string ProjectPath { get; }
    public bool InCycle { get; }
    public string CycleTooltip { get; }
    public int BuildOrder { get; }

    /// <summary>Lifecycle status; drives the card colour and animation triggers.</summary>
    [ObservableProperty]
    private ProjectStatus _status;

    /// <summary>True while this project is actively building (pulse + scale + bring-to-front).</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>Whether the card is shown after a status filter is applied.</summary>
    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>True when the user clicked this card to scope the console to it.</summary>
    [ObservableProperty]
    private bool _isConsoleFocused;

    public void Reset()
    {
        IsActive = false;
        Status = InCycle ? ProjectStatus.CycleDetected : ProjectStatus.Discovered;
    }
}
