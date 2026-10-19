using BuildOrchestrator.App;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [Exit · inceleme O6] Exit testlerinin ORTAK sahteleri — kabuğun kapanış ve kart dikişleri ile örnek süreç listesi tek yerde
/// (CLAUDE.md: ortak fixture tek yerde). Kurulduğu pencerede: uygulamayı kapatan çağrı sayaçlıdır (uygulama kapanmaz), kartın yeniden
/// sayma zamanlayıcısı elle tıklatılır (gerçek saat yok — D8), süreç sayımı gerçek job yerine <see cref="Processes"/>'i verir ve kaç kez
/// sorulduğunu sayar. Onay için pencereyi öne getiren dikişleri SAYAN testler (<c>CloseToTrayTests</c>) kendi sahtelerini ayrıca takar.
/// </summary>
internal sealed class ExitSeamFakes
{
    /// <summary>Ekran görüntüsündeki kartın listesi: dört <c>MSBuild.exe</c>, bir <c>VBCSCompiler.exe</c> — beş süreç.</summary>
    public static readonly IReadOnlyList<ProcessTally> SampleProcesses = [new("MSBuild.exe", 4), new("VBCSCompiler.exe", 1)];

    public ExitSeamFakes(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.ShutdownApplication = () => Shutdowns++;
        window.ExitCardTimer = CardTimer;
        window.TakeProcessCensus = () =>
        {
            Censuses++;
            return Processes;
        };
    }

    /// <summary>Uygulamayı kapatan çağrının sayısı.</summary>
    public int Shutdowns { get; private set; }

    /// <summary>Kartın süreç sayımının kaç kez sorulduğu.</summary>
    public int Censuses { get; private set; }

    /// <summary>Kart açıkken yeniden sayan zamanlayıcı.</summary>
    public FakePollTimer CardTimer { get; } = new();

    /// <summary>Sayımın verdiği liste — test değiştirebilir.</summary>
    public IReadOnlyList<ProcessTally> Processes { get; set; } = SampleProcesses;
}
