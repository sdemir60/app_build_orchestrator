using System.Windows.Threading;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [Faz 2/T9 · spec 2026-09-18 §6.4] Yeniden kurulabilir, UI thread'inde tık atan bir yoklama zamanlayıcısı. VM Dispatcher
/// TÜRÜ taşımaz (D8): üretimde <see cref="DispatcherPollTimer"/>, testte elle tıklatılan sahte bir zamanlayıcı.
/// </summary>
internal interface IPollTimer
{
    /// <summary>Çalışıyor mu (<see cref="Start"/> ile <see cref="Stop"/> arası).</summary>
    bool IsRunning { get; }

    /// <summary>Her <paramref name="interval"/>'de <paramref name="tick"/>'i çağırmaya başlar. Zaten çalışıyorsa
    /// hiçbir şey yapmaz — çağıran her geçişte güvenle çağırabilir.</summary>
    void Start(TimeSpan interval, Action tick);

    /// <summary>Tıkları durdurur; çalışmıyorsa hiçbir şey yapmaz.</summary>
    void Stop();
}

/// <summary>[Faz 2/T9] <see cref="IPollTimer"/>'ın üretim karşılığı — tık UI thread'inde gelir.</summary>
internal sealed class DispatcherPollTimer(Dispatcher dispatcher) : IPollTimer
{
    private DispatcherTimer? _timer;

    public bool IsRunning => _timer is not null;

    public void Start(TimeSpan interval, Action tick)
    {
        if (_timer is not null) return;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = interval };
        _timer.Tick += (_, _) => tick();
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }
}
