using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>Editable copy of <see cref="AppConfig"/> for the config screen (Section 3).</summary>
public sealed partial class ConfigViewModel : ObservableObject
{
    public ConfigViewModel(AppConfig config)
    {
        _rootPath = config.RootPath ?? string.Empty;
        _buildConfiguration = config.BuildConfiguration;
        _performance = config.Performance;
        _branchWorkMode = config.BranchWorkMode;
        _dependentMode = config.DependentMode;
        _cacheLocation = config.CacheLocation ?? AppPaths.Root;
        _autoStart = config.AutoStart;
        _minimizeToTray = config.MinimizeToTray;
    }

    [ObservableProperty] private string _rootPath;
    [ObservableProperty] private BuildConfiguration _buildConfiguration;
    [ObservableProperty] private PerformanceMode _performance;
    [ObservableProperty] private BranchWorkMode _branchWorkMode;
    [ObservableProperty] private DependentMode _dependentMode;
    [ObservableProperty] private string _cacheLocation;
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private bool _minimizeToTray;

    public IReadOnlyList<BuildConfiguration> BuildConfigurations { get; } =
        Enum.GetValues<BuildConfiguration>();
    public IReadOnlyList<PerformanceMode> PerformanceModes { get; } =
        Enum.GetValues<PerformanceMode>();
    public IReadOnlyList<BranchWorkMode> BranchWorkModes { get; } =
        Enum.GetValues<BranchWorkMode>();
    public IReadOnlyList<DependentMode> DependentModes { get; } = Enum.GetValues<DependentMode>();

    public AppConfig ToConfig() => new()
    {
        RootPath = string.IsNullOrWhiteSpace(RootPath) ? null : RootPath,
        BuildConfiguration = BuildConfiguration,
        Performance = Performance,
        BranchWorkMode = BranchWorkMode,
        DependentMode = DependentMode,
        CacheLocation = string.IsNullOrWhiteSpace(CacheLocation) ? null : CacheLocation,
        AutoStart = AutoStart,
        MinimizeToTray = MinimizeToTray
    };
}
