namespace BuildOrchestrator.App.Services;

/// <summary>
/// [It-4a Foundation] Downstream animasyon task'larının (typewriter/kaskat/scroll/graf) tükettiği reduced-motion
/// arayüzü: canlı AnimationsEnabled bayrağı + bir token süreyi etkin süreye çeviren saf sorgu.
/// </summary>
public interface IMotionSettings
{
    bool AnimationsEnabled { get; }

    /// <summary>AnimationsEnabled canlı değiştiğinde tetiklenir.</summary>
    event EventHandler? AnimationsEnabledChanged;

    /// <summary>Reduced-motion kapalıyken TimeSpan.Zero, açıkken verilen token süresini döner.</summary>
    TimeSpan Effective(TimeSpan token);
}
