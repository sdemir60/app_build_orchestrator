using System.Windows;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [It-4a Foundation / Global Constraints — reduced-motion] Gerçek OS sinyali: SystemParameters.ClientAreaAnimation
/// + StaticPropertyChanged canlı takip (uygulama-içi toggle YOK). Uygulama ömrü boyunca tek instance — statik
/// WPF event'ine abone olduğundan GC'den etkilenmez, ayrıca App tarafından referans tutulur.
/// </summary>
public sealed class SystemParametersMotionSignal : IMotionSignal
{
    public SystemParametersMotionSignal()
    {
        SystemParameters.StaticPropertyChanged += OnStaticPropertyChanged;
    }

    public bool AnimationsEnabled => SystemParameters.ClientAreaAnimation;

    public event EventHandler? Changed;

    private void OnStaticPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
            Changed?.Invoke(this, EventArgs.Empty);
    }
}
