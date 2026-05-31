using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Configuration;
using BuildOrchestrator.Core.Storage;
using BuildOrchestrator.Core.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>Status-label filters that can be toggled from the bottom-left stats (Section 7).</summary>
public enum CardFilter
{
    All,
    Built,
    Succeeded,
    Failed,
    Skipped
}

/// <summary>
/// Root view model for the single window (Section 7). Owns the project cards, console, branch list,
/// run lifecycle (Build/Rebuild/Stop morph), and stats; it brokers all Worker communication and
/// marshals incoming events onto the UI thread.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly WorkerClient _worker;
    private readonly ConfigStore _configStore;
    private readonly Action<Action> _uiInvoke;
    private readonly Dictionary<string, ProjectCardViewModel> _byId = new(StringComparer.Ordinal);

    private string? _currentRunId;
    private CardFilter _filter = CardFilter.All;

    public MainViewModel(WorkerClient worker, ConfigStore configStore, AppConfig config, Action<Action> uiInvoke)
    {
        _worker = worker;
        _configStore = configStore;
        _uiInvoke = uiInvoke;
        Config = config;
        ReducedMotion = config.ReducedMotion;

        Console = new ConsoleViewModel(config.ConsoleMaxLines);
        ProjectsView = CollectionViewSource.GetDefaultView(Projects);
        ProjectsView.Filter = FilterPredicate;

        HookWorker();
    }

    public AppConfig Config { get; }
    public ConsoleViewModel Console { get; }

    public ObservableCollection<ProjectCardViewModel> Projects { get; } = new();
    public ICollectionView ProjectsView { get; }

    public ObservableCollection<string> Branches { get; } = new();

    [ObservableProperty]
    private string? _selectedBranch;

    [ObservableProperty]
    private bool _reducedMotion;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string? _activeProjectId;

    [ObservableProperty]
    private string _statusText = "Ready";

    // ---- Stats (Section 7 bottom-left) ----
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _builtCount;
    [ObservableProperty] private int _succeededCount;
    [ObservableProperty] private int _failedCount;
    [ObservableProperty] private int _skippedCount;

    partial void OnSelectedBranchChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _worker.SelectBranch(value);
        }
    }

    partial void OnReducedMotionChanged(bool value)
    {
        Config.ReducedMotion = value;
        _configStore.Save(Config);
    }

    // ---- Commands ----

    [RelayCommand]
    private void Sync()
    {
        StatusText = "Syncing…";
        Projects.Clear();
        _byId.Clear();
        if (string.IsNullOrWhiteSpace(Config.RootPath))
        {
            StatusText = "Set a root path in Settings first.";
            return;
        }
        _worker.SyncWorkspace(Config.RootPath);
        _worker.ListBranches();
    }

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private void Build() => StartRun(BuildMode.Build);

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private void Rebuild() => StartRun(BuildMode.Rebuild);

    private bool CanBuild() => !IsRunning && Projects.Count > 0;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        if (_currentRunId is not null)
        {
            StatusText = "Stopping…";
            _worker.StopRun(_currentRunId);
        }
    }

    private bool CanStop() => IsRunning && _currentRunId is not null;

    [RelayCommand]
    private void Filter(string? filterName)
    {
        _filter = Enum.TryParse<CardFilter>(filterName, ignoreCase: true, out var f) ? f : CardFilter.All;
        ProjectsView.Refresh();
    }

    [RelayCommand]
    private void OpenPath(ProjectCardViewModel? card)
    {
        if (card is not null)
        {
            _worker.OpenPath(card.Id);
        }
    }

    [RelayCommand]
    private void OpenInVs(ProjectCardViewModel? card)
    {
        if (card is not null)
        {
            _worker.OpenInVs(card.Id);
        }
    }

    [RelayCommand]
    private void CardClicked(ProjectCardViewModel? card)
    {
        // Clicking a failed card focuses its output in the console; clicking again clears (Section 7).
        if (card is { IsFailed: true })
        {
            Console.ToggleFocus(card.Id);
        }
    }

    private void StartRun(BuildMode mode)
    {
        foreach (var c in Projects)
        {
            c.Reset();
        }
        Console.Clear();
        BuiltCount = SucceededCount = FailedCount = SkippedCount = 0;

        var request = new RunRequest
        {
            Mode = mode,
            Branch = SelectedBranch ?? string.Empty,
            Config = Config.Configuration,
            DependentMode = Config.DependentMode,
            Performance = Config.Performance
        };

        IsRunning = true;
        UpdateCommandStates();
        StatusText = mode == BuildMode.Rebuild ? "Rebuilding…" : "Building…";
        _worker.StartRun(request);
    }

    // ---- Worker event wiring (marshalled to UI thread) ----

    private void HookWorker()
    {
        _worker.SyncCompleted += p => _uiInvoke(() => OnSyncCompleted(p));
        _worker.SyncProgress += p => _uiInvoke(() => StatusText = $"Scanning… {p.Scanned}/{p.Total}");
        _worker.BranchList += p => _uiInvoke(() => OnBranchList(p));
        _worker.RunStarted += p => _uiInvoke(() => OnRunStarted(p));
        _worker.ProjectStarted += p => _uiInvoke(() => OnProjectStarted(p));
        _worker.ProjectLog += p => _uiInvoke(() => Console.Append(new ConsoleEntry(p.ProjectId, p.Line, p.IsError)));
        _worker.ProjectSucceeded += p => _uiInvoke(() => OnProjectSucceeded(p));
        _worker.ProjectFailed += p => _uiInvoke(() => OnProjectFailed(p));
        _worker.ProjectSkipped += p => _uiInvoke(() => OnProjectSkipped(p));
        _worker.RunCompleted += p => _uiInvoke(() => OnRunCompleted(p));
        _worker.RunCancelled += p => _uiInvoke(() => OnRunCancelled(p));
        _worker.Error += p => _uiInvoke(() => StatusText = "Error: " + p.Message);
        _worker.WorkerExited += () => _uiInvoke(OnWorkerExited);
    }

    private void OnSyncCompleted(SyncCompletedPayload p)
    {
        LoadProjects(p.Projects, p.HasCycles);
        StatusText = p.HasCycles ? $"Synced {TotalCount} projects (cycles detected)" : $"Synced {TotalCount} projects";
    }

    /// <summary>Populate the card list from a project set (used by Sync results and the cached graph).</summary>
    public void LoadProjects(IReadOnlyList<ProjectNode> nodes, bool hasCycles)
    {
        Projects.Clear();
        _byId.Clear();
        foreach (var node in nodes)
        {
            var card = new ProjectCardViewModel(node);
            Projects.Add(card);
            _byId[card.Id] = card;
        }
        TotalCount = Projects.Count;
        UpdateCommandStates();
    }

    /// <summary>Thread-safe-ish status setter for early startup before the window exists.</summary>
    public void StatusTextSafe(string text) => StatusText = text;

    private void OnBranchList(BranchListPayload p)
    {
        Branches.Clear();
        foreach (var b in p.Branches)
        {
            Branches.Add(b);
        }
        // Section 6: the user's active branch comes pre-selected.
        SelectedBranch = !string.IsNullOrEmpty(p.Current) ? p.Current : Branches.FirstOrDefault();
    }

    private void OnRunStarted(RunStartedPayload p)
    {
        _currentRunId = p.RunId;
        var planned = new HashSet<string>(p.PlannedProjectIds, StringComparer.Ordinal);
        foreach (var card in Projects)
        {
            if (planned.Contains(card.Id))
            {
                card.Status = ProjectStatus.Queued;
            }
        }
        UpdateCommandStates();
    }

    private void OnProjectStarted(ProjectStartedPayload p)
    {
        if (_byId.TryGetValue(p.ProjectId, out var card))
        {
            card.Status = ProjectStatus.Building;
            card.IsActive = true;
            ActiveProjectId = p.ProjectId; // drives auto-focus scroll
        }
    }

    private void OnProjectSucceeded(ProjectSucceededPayload p)
    {
        if (_byId.TryGetValue(p.ProjectId, out var card))
        {
            card.Status = ProjectStatus.Succeeded;
            card.ElapsedMs = p.ElapsedMs;
            card.IsActive = false;
        }
        SucceededCount++;
        BuiltCount++;
    }

    private void OnProjectFailed(ProjectFailedPayload p)
    {
        if (_byId.TryGetValue(p.ProjectId, out var card))
        {
            card.Status = ProjectStatus.Failed;
            card.FailureReason = p.Reason;
            card.ElapsedMs = p.ElapsedMs;
            card.IsActive = false;
        }
        FailedCount++;
        BuiltCount++;
    }

    private void OnProjectSkipped(ProjectSkippedPayload p)
    {
        if (_byId.TryGetValue(p.ProjectId, out var card))
        {
            card.Status = ProjectStatus.Skipped;
        }
        SkippedCount++;
    }

    private void OnRunCompleted(RunCompletedPayload p)
    {
        IsRunning = false;
        _currentRunId = null;
        ActiveProjectId = null;
        StatusText = $"Done — {p.Succeeded} ok, {p.Failed} failed, {p.Skipped} skipped in {p.ElapsedMs} ms";
        UpdateCommandStates();
    }

    private void OnRunCancelled(RunCancelledPayload p)
    {
        IsRunning = false;
        _currentRunId = null;
        ActiveProjectId = null;
        StatusText = "Stopped";
        foreach (var card in Projects.Where(c => c.Status is ProjectStatus.Building or ProjectStatus.Queued))
        {
            card.Reset();
        }
        UpdateCommandStates();
    }

    private void OnWorkerExited()
    {
        // Section 2: UI survives a Worker crash. Reset run state and offer recovery.
        IsRunning = false;
        _currentRunId = null;
        ActiveProjectId = null;
        StatusText = "Worker stopped unexpectedly. Restarting…";
        try
        {
            _worker.Start();
        }
        catch
        {
            StatusText = "Worker unavailable.";
        }
        UpdateCommandStates();
    }

    private bool FilterPredicate(object obj)
    {
        if (obj is not ProjectCardViewModel card)
        {
            return true;
        }
        return _filter switch
        {
            CardFilter.All => true,
            CardFilter.Built => card.Status is ProjectStatus.Succeeded or ProjectStatus.Failed,
            CardFilter.Succeeded => card.Status == ProjectStatus.Succeeded,
            CardFilter.Failed => card.Status == ProjectStatus.Failed,
            CardFilter.Skipped => card.Status == ProjectStatus.Skipped,
            _ => true
        };
    }

    private void UpdateCommandStates()
    {
        BuildCommand.NotifyCanExecuteChanged();
        RebuildCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }
}
