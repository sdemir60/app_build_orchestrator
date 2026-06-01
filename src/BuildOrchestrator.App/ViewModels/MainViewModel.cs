using System.Collections.ObjectModel;
using System.Windows;
using BuildOrchestrator.App.Ipc;
using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Configuration;
using BuildOrchestrator.Core.Persistence;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>Root view model for the single window (Sections 7, 8).</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly WorkerClient _client;
    private readonly ConfigStore _configStore = new();
    private readonly Dictionary<string, ProjectCardViewModel> _byId = new();
    private readonly Dictionary<string, DateTime> _startTimes = new();

    private AppConfig _config;
    private string? _currentRunId;
    private int _runTotal;

    /// <summary>Raised when a project starts building so the view can scroll it into focus.</summary>
    public event Action<ProjectCardViewModel>? ScrollToCardRequested;

    public MainViewModel()
    {
        _config = _configStore.Load();
        _client = new WorkerClient();
        _client.EventReceived += OnWorkerEvent;
        _client.WorkerExited += OnWorkerExited;
        _client.Diagnostic += OnWorkerDiagnostic;

        Console = new ConsoleViewModel();
    }

    public ConsoleViewModel Console { get; }

    public ObservableCollection<ProjectCardViewModel> Projects { get; } = new();
    public ObservableCollection<BranchInfo> Branches { get; } = new();

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private string _syncStatus = "Ready";
    [ObservableProperty] private BranchInfo? _selectedBranch;

    [ObservableProperty] private ProjectStatus? _activeFilter;

    // Counts (Section 7 bottom-left labels)
    [ObservableProperty] private int _total;
    [ObservableProperty] private int _built;
    [ObservableProperty] private int _succeeded;
    [ObservableProperty] private int _failed;
    [ObservableProperty] private int _skipped;

    public void Initialize()
    {
        _client.Start();
        if (!string.IsNullOrWhiteSpace(_config.RootPath))
            _client.Send(new SyncWorkspaceCommand(_config.RootPath!));
        else
            _client.Send(new ReanalyzeCommand());
    }

    public AppConfig Config => _config;

    public void ApplyConfig(AppConfig config)
    {
        _config = config;
        _configStore.Save(config);
        // Re-sync against the (possibly new) root.
        if (!string.IsNullOrWhiteSpace(config.RootPath))
            _client.Send(new SyncWorkspaceCommand(config.RootPath!));
    }

    partial void OnSelectedBranchChanged(BranchInfo? value)
    {
        if (value != null)
            _client.Send(new SelectBranchCommand(value.Name));
    }

    partial void OnIsRunningChanged(bool value)
    {
        BuildCommand.NotifyCanExecuteChanged();
        RebuildCommand.NotifyCanExecuteChanged();
    }

    // ---- Commands ----

    [RelayCommand]
    private void Sync()
    {
        IsSyncing = true;
        SyncStatus = "Syncing...";
        _client.Send(new ReanalyzeCommand());
    }

    [RelayCommand(CanExecute = nameof(CanStartRun))]
    private void Build() => StartRun(BuildMode.Build);

    [RelayCommand(CanExecute = nameof(CanStartRun))]
    private void Rebuild() => StartRun(BuildMode.Rebuild);

    private bool CanStartRun() => !IsRunning && Projects.Count > 0;

    [RelayCommand]
    private void Stop()
    {
        if (_currentRunId != null)
        {
            SyncStatus = "Stopping\u2026";
            _client.Send(new StopRunCommand(_currentRunId));
        }
    }

    /// <summary>Stop any in-progress build before the application exits (tray "Exit"/close).</summary>
    public void StopForExit()
    {
        if (IsRunning && _currentRunId != null)
            _client.Send(new StopRunCommand(_currentRunId));
    }

    [RelayCommand]
    private void OpenPath(ProjectCardViewModel? card)
    {
        if (card != null)
            _client.Send(new OpenPathCommand(card.Id));
    }

    [RelayCommand]
    private void OpenInVS(ProjectCardViewModel? card)
    {
        if (card != null)
            _client.Send(new OpenInVSCommand(card.Id));
    }

    /// <summary>Click a card to scope the console to it; click again to clear (Section 7).</summary>
    [RelayCommand]
    private void FocusCard(ProjectCardViewModel? card)
    {
        if (card == null)
            return;
        if (Console.FocusedProjectId == card.Id)
        {
            Console.FocusedProjectId = null;
            card.IsConsoleFocused = false;
        }
        else
        {
            foreach (var c in Projects)
                c.IsConsoleFocused = false;
            card.IsConsoleFocused = true;
            Console.FocusedProjectId = card.Id;
        }
    }

    [RelayCommand]
    private void ToggleFilter(string? statusName)
    {
        ProjectStatus? status = statusName switch
        {
            "Succeeded" => ProjectStatus.Succeeded,
            "Failed" => ProjectStatus.Failed,
            "Skipped" => ProjectStatus.Skipped,
            "Built" => null, // handled below as composite
            _ => null
        };

        // Toggle off if same filter clicked again.
        if (ActiveFilter == status && statusName != "Total" && status != null)
            ActiveFilter = null;
        else
            ActiveFilter = status;

        ApplyFilter(statusName);
    }

    private void ApplyFilter(string? statusName)
    {
        foreach (var card in Projects)
        {
            bool visible = statusName switch
            {
                "Succeeded" => ActiveFilter == ProjectStatus.Succeeded ? card.Status == ProjectStatus.Succeeded : true,
                "Failed" => ActiveFilter == ProjectStatus.Failed ? card.Status == ProjectStatus.Failed : true,
                "Skipped" => ActiveFilter == ProjectStatus.Skipped ? card.Status == ProjectStatus.Skipped : true,
                "Built" => card.Status is ProjectStatus.Succeeded or ProjectStatus.Failed,
                _ => true
            };
            card.IsVisible = visible;
        }
    }

    private void StartRun(BuildMode mode)
    {
        if (IsRunning)
            return;

        foreach (var card in Projects)
            card.Reset();
        RecountStatuses();
        Console.Clear();
        _startTimes.Clear();

        _currentRunId = Guid.NewGuid().ToString("N");
        var request = new RunRequest
        {
            Mode = mode,
            Branch = SelectedBranch?.Name ?? string.Empty,
            Config = _config.BuildConfiguration,
            DependentMode = _config.DependentMode,
            Performance = _config.Performance,
            BranchWorkMode = _config.BranchWorkMode
        };
        IsRunning = true;
        _client.Send(new StartRunCommand(_currentRunId, request));
    }

    // ---- Worker events (marshalled to the UI thread) ----

    private void OnWorkerEvent(WorkerEvent evt)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
            return;
        dispatcher.BeginInvoke(() => HandleEvent(evt));
    }

    private void HandleEvent(WorkerEvent evt)
    {
        switch (evt)
        {
            case SyncProgressEvent sp:
                IsSyncing = true;
                SyncStatus = sp.Total > 0 ? $"{sp.Message} ({sp.Current}/{sp.Total})" : sp.Message;
                Console.Append(new ConsoleLine("", "sync", SyncStatus, false));
                break;

            case SyncCompletedEvent sc:
                LoadGraph(sc.Graph);
                IsSyncing = false;
                SyncStatus = $"{Projects.Count} projects";
                break;

            case BranchListEvent bl:
                LoadBranches(bl.Branches);
                break;

            case RunStartedEvent rs:
                IsRunning = true;
                _runTotal = rs.TotalProjects;
                UpdateRunProgress();
                break;

            case ProjectStartedEvent ps when _byId.TryGetValue(ps.ProjectId, out var startedCard):
                startedCard.Status = ProjectStatus.Building;
                startedCard.IsActive = true;
                _startTimes[ps.ProjectId] = DateTime.Now;
                Console.Append(new ConsoleLine(ps.ProjectId, startedCard.Name,
                    $"\u25B6 Building {startedCard.Name}", false, IsHeader: true));
                ScrollToCardRequested?.Invoke(startedCard);
                break;

            case ProjectLogEvent pl:
                _byId.TryGetValue(pl.ProjectId, out var logCard);
                Console.Append(new ConsoleLine(pl.ProjectId, logCard?.Name ?? pl.ProjectId, pl.Line, pl.IsError));
                break;

            case ProjectSucceededEvent pe when _byId.TryGetValue(pe.ProjectId, out var okCard):
                okCard.Status = ProjectStatus.Succeeded;
                okCard.IsActive = false;
                Console.Append(new ConsoleLine(pe.ProjectId, okCard.Name,
                    $"\u2713 {okCard.Name} succeeded{Elapsed(pe.ProjectId)}", false, IsHeader: true, IsSuccess: true));
                RecountStatuses();
                UpdateRunProgress();
                break;

            case ProjectFailedEvent pf when _byId.TryGetValue(pf.ProjectId, out var failCard):
                failCard.Status = ProjectStatus.Failed;
                failCard.IsActive = false;
                Console.Append(new ConsoleLine(pf.ProjectId, failCard.Name,
                    $"\u2717 {failCard.Name} FAILED{Elapsed(pf.ProjectId)}", true, IsHeader: true));
                RecountStatuses();
                UpdateRunProgress();
                break;

            case ProjectSkippedEvent psk when _byId.TryGetValue(psk.ProjectId, out var skipCard):
                skipCard.Status = ProjectStatus.Skipped;
                skipCard.IsActive = false;
                Console.Append(new ConsoleLine(psk.ProjectId, skipCard.Name,
                    $"\u29B8 {skipCard.Name} skipped — {psk.Reason}", false, IsHeader: true));
                RecountStatuses();
                UpdateRunProgress();
                break;

            case RunCompletedEvent rc:
                IsRunning = false;
                _currentRunId = null;
                SyncStatus = $"Done — {rc.Succeeded} ok, {rc.Failed} failed, {rc.Skipped} skipped";
                Console.Append(new ConsoleLine("", "run",
                    $"\u2756 Done — {rc.Succeeded} ok, {rc.Failed} failed, {rc.Skipped} skipped", false, IsHeader: true));
                RecountStatuses();
                break;

            case RunCancelledEvent:
                IsRunning = false;
                _currentRunId = null;
                SyncStatus = "Stopped";
                Console.Append(new ConsoleLine("", "run", "\u2756 Stopped", false, IsHeader: true));
                RecountStatuses();
                break;

            case WorkerErrorEvent we:
                Console.Append(new ConsoleLine("", "worker", we.Detail is null ? we.Message : $"{we.Message}: {we.Detail}", true));
                break;
        }
    }

    private void UpdateRunProgress()
    {
        if (IsRunning)
            SyncStatus = $"Building {Succeeded + Failed + Skipped}/{_runTotal}\u2026";
    }

    /// <summary>Formats the elapsed build time for a project (" in 1.2s" / " in 800ms").</summary>
    private string Elapsed(string projectId)
    {
        if (!_startTimes.TryGetValue(projectId, out var start))
            return string.Empty;
        var span = DateTime.Now - start;
        return span.TotalSeconds >= 1
            ? $" in {span.TotalSeconds:0.0}s"
            : $" in {span.TotalMilliseconds:0}ms";
    }

    private void LoadGraph(DependencyGraph graph)
    {
        Projects.Clear();
        _byId.Clear();
        foreach (var node in graph.Projects.OrderBy(n => n.BuildOrder))
        {
            var card = new ProjectCardViewModel(node);
            Projects.Add(card);
            _byId[node.Id] = card;
        }
        RecountStatuses();
        BuildCommand.NotifyCanExecuteChanged();
        RebuildCommand.NotifyCanExecuteChanged();
    }

    private void LoadBranches(List<BranchInfo> branches)
    {
        Branches.Clear();
        foreach (var b in branches)
            Branches.Add(b);
        SelectedBranch = branches.FirstOrDefault(b => b.IsCurrent) ?? branches.FirstOrDefault();
    }

    private void RecountStatuses()
    {
        Total = Projects.Count;
        Succeeded = Projects.Count(p => p.Status == ProjectStatus.Succeeded);
        Failed = Projects.Count(p => p.Status == ProjectStatus.Failed);
        Skipped = Projects.Count(p => p.Status == ProjectStatus.Skipped);
        Built = Succeeded + Failed;
    }

    private void OnWorkerExited()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            IsRunning = false;
            IsSyncing = false;
            SyncStatus = "Worker stopped";
        });
    }

    private void OnWorkerDiagnostic(string message, bool isError)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
            Console.Append(new ConsoleLine("", "worker", message, isError)));
    }

    public void Dispose()
    {
        _client.EventReceived -= OnWorkerEvent;
        _client.WorkerExited -= OnWorkerExited;
        _client.Diagnostic -= OnWorkerDiagnostic;
        _client.Dispose();
    }
}
