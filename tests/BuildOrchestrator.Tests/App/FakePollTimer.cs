using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>[Faz 2/T9] Elle tıklatılan yoklama zamanlayıcısı — D8: gerçek zaman beklenmez, test <see cref="Tick"/> çağırır.</summary>
internal sealed class FakePollTimer : IPollTimer
{
    private Action? _tick;

    public bool IsRunning => _tick is not null;

    /// <summary>Son <see cref="Start"/>'ın aralığı.</summary>
    public TimeSpan? Interval { get; private set; }

    public void Start(TimeSpan interval, Action tick)
    {
        if (_tick is not null) return;
        Interval = interval;
        _tick = tick;
    }

    public void Stop() => _tick = null;

    /// <summary>Çalışıyorsa bir tık atar.</summary>
    public void Tick() => _tick?.Invoke();
}
