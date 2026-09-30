using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.Tests.App;

/// <summary>[design v1.23/v1.24 review C13] Restart ekranının (<see cref="UpdateRestartScreen"/>) sahte zamanı — ekranı
/// süren her rig'in (tek başına ekran, kabuk, akış testi) TEK zaman dikişi: kurulurken ekrana takılır (zamanlayıcı +
/// <c>NowMs</c>), <see cref="FrameAt"/> saati ilerletip bir kare atar. Gerçek zaman beklenmez (D8).</summary>
internal sealed class RestartScreenTime
{
    /// <summary>Oynatmanın başladığı an (sahte saat, ms).</summary>
    public const long StartMs = 10_000;

    public FakePollTimer Timer { get; } = new();

    /// <summary>Sahte saat — ekranın <c>NowMs</c>'i bunu okur.</summary>
    public long Now = StartMs;

    public RestartScreenTime(UpdateRestartScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        screen.Timer = Timer;
        screen.NowMs = () => Now;
    }

    /// <summary>Saati oynatmanın başından <paramref name="elapsedMs"/> sonrasına alır ve bir kare atar.</summary>
    public void FrameAt(double elapsedMs)
    {
        Now = StartMs + (long)elapsedMs;
        Timer.Tick();
    }
}
