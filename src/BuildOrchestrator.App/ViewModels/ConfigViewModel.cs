using BuildOrchestrator.App.Services;
using System.IO;
using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Configuration;
using BuildOrchestrator.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// Backs the configuration screen (Section 3): root dir, Debug/Release, performance mode, branch
/// mode, log level, dependent mode, cache location, reduced motion, autostart.
/// </summary>
public sealed partial class ConfigViewModel : ObservableObject
{
    private readonly ConfigStore _store;
    private readonly AppConfig _config;

    public ConfigViewModel(ConfigStore store, AppConfig config)
    {
        _store = store;
        _config = config;

        RootPath = config.RootPath;
        Configuration = config.Configuration;
        Performance = config.Performance;
        BranchMode = config.BranchMode;
        LogLevel = config.LogLevel;
        DependentMode = config.DependentMode;
        CacheLocation = config.CacheLocation ?? string.Empty;
        ReducedMotion = config.ReducedMotion;
        AutoStart = AutoStartService.IsEnabled();
    }

    public Array Configurations => Enum.GetValues(typeof(BuildConfiguration));
    public Array Performances => Enum.GetValues(typeof(PerformanceMode));
    public Array BranchModes => Enum.GetValues(typeof(BranchMode));
    public Array LogLevels => Enum.GetValues(typeof(LogLevel));
    public Array DependentModes => Enum.GetValues(typeof(DependentMode));

    [ObservableProperty] private string _rootPath = string.Empty;
    [ObservableProperty] private BuildConfiguration _configuration;
    [ObservableProperty] private PerformanceMode _performance;
    [ObservableProperty] private BranchMode _branchMode;
    [ObservableProperty] private LogLevel _logLevel;
    [ObservableProperty] private DependentMode _dependentMode;
    [ObservableProperty] private string _cacheLocation = string.Empty;
    [ObservableProperty] private bool _reducedMotion;
    [ObservableProperty] private bool _autoStart;

    /// <summary>True after Save so the caller can apply changes (e.g. reduced motion).</summary>
    public bool Saved { get; private set; }

    [RelayCommand]
    private void BrowseRoot()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog();
        if (!string.IsNullOrWhiteSpace(RootPath) && Directory.Exists(RootPath))
        {
            dialog.SelectedPath = RootPath;
        }
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            RootPath = dialog.SelectedPath;
        }
    }

    [RelayCommand]
    private void Save()
    {
        _config.RootPath = RootPath;
        _config.Configuration = Configuration;
        _config.Performance = Performance;
        _config.BranchMode = BranchMode;
        _config.LogLevel = LogLevel;
        _config.DependentMode = DependentMode;
        _config.CacheLocation = string.IsNullOrWhiteSpace(CacheLocation) ? null : CacheLocation;
        _config.ReducedMotion = ReducedMotion;
        _store.Save(_config);
        AutoStartService.SetEnabled(AutoStart);
        Saved = true;
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    public event Action<bool>? CloseRequested;
}
