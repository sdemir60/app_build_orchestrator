using System.Windows;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [It-4a Foundation / Global Constraints — reduced-motion] IMotionSignal'i (SystemParameters.ClientAreaAnimation
/// soyutlaması) tüketir, AnimationsEnabled'ı canlı yayar ve Attach edilen ResourceDictionary'deki Duration.*
/// kaynaklarını uygulama düzeyinde TOPLU olarak 0'a çevirir / token değerine geri yükler (App/Resources/Motion.xaml
/// ile birebir eşleşen anahtarlar). Downstream task'lar isterse doğrudan {StaticResource Duration.*} kullanır
/// (reduced iken zaten 0), isterse Effective(TimeSpan) ile saf kod tarafında sorgular.
/// </summary>
public sealed class MotionSettings : IMotionSettings
{
    // App/Resources/Motion.xaml'deki token süreleriyle BİREBİR — reduced-motion kapanınca bu değerlere döner.
    private static readonly (string Key, TimeSpan Token)[] DurationKeys =
    [
        ("Duration.Instant", TimeSpan.FromMilliseconds(80)),
        ("Duration.Fast", TimeSpan.FromMilliseconds(120)),
        ("Duration.Base", TimeSpan.FromMilliseconds(180)),
        ("Duration.Slow", TimeSpan.FromMilliseconds(280)),
    ];

    private readonly IMotionSignal _signal;
    private ResourceDictionary? _resources;

    public MotionSettings(IMotionSignal signal)
    {
        _signal = signal;
        _signal.Changed += OnSignalChanged;
    }

    public bool AnimationsEnabled => _signal.AnimationsEnabled;

    public event EventHandler? AnimationsEnabledChanged;

    public TimeSpan Effective(TimeSpan token) => AnimationsEnabled ? token : TimeSpan.Zero;

    /// <summary>Verilen ResourceDictionary'nin Duration.* girdilerini şu anki sinyale göre uygular (ilk çağrıda)
    /// ve her sinyal değişiminde günceller. Aynı instance'a birden fazla kez Attach edilirse son çağrı geçerli olur.</summary>
    public void Attach(ResourceDictionary resources)
    {
        _resources = resources;
        Apply();
    }

    private void OnSignalChanged(object? sender, EventArgs e)
    {
        Apply();
        AnimationsEnabledChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Apply()
    {
        if (_resources is null) return;
        bool enabled = AnimationsEnabled;
        foreach (var (key, token) in DurationKeys)
            _resources[key] = new Duration(enabled ? token : TimeSpan.Zero);
    }
}
