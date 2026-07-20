namespace BuildOrchestrator.App.Services;

/// <summary>
/// [It-4a Foundation] OS "animasyonları göster" sinyalinin HAM kaynağı. MotionSettings bunu sarar.
/// Test için enjekte edilebilir (D8 — gerçek SystemParameters'a bağlanmadan deterministik test).
/// </summary>
public interface IMotionSignal
{
    bool AnimationsEnabled { get; }

    /// <summary>Sinyal değiştiğinde (OS ayarı canlı değişti) tetiklenir.</summary>
    event EventHandler? Changed;
}
