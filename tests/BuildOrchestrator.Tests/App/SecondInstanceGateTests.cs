using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E2/FIX1] İkinci instance mevcut pencereyi öne getirme DENEMESİNİN sonucundan türeyen KARAR — balloon
/// gösterilecek mi + hangi çıkış kodu — WPF'ten ayrıştırılmış saf bir dikiş (<see cref="SecondInstanceGate.Decide"/>)
/// üzerinden test edilir. (Önceki review Minor'ı: <c>false→balloon</c> yolu YALNIZ compile-covered'dı; gerçek bir
/// ikinci process başlatmadan ya da gerçek tray'e dokunmadan kararın İKİ dalı da burada pinlenir.)
/// </summary>
public class SecondInstanceGateTests
{
    [Fact] // öne getirilebildi → SESSİZ ve temiz kapan: balloon YOK, çıkış kodu 0
    public void Activation_success_shuts_down_silently_without_a_balloon()
    {
        var outcome = SecondInstanceGate.Decide(activated: true);

        Assert.False(outcome.ShowBalloon);
        Assert.Equal(0, outcome.ExitCode);
    }

    [Fact] // öne GETİRİLEMEDİ → SESSİZ KALMA: balloon iste + AYRIŞAN (named) çıkış koduyla kapan
    public void Activation_failure_requests_a_balloon_and_a_distinct_exit_code()
    {
        var outcome = SecondInstanceGate.Decide(activated: false);

        Assert.True(outcome.ShowBalloon);
        Assert.Equal(BuildOrchestrator.App.App.SecondInstanceActivationFailedExitCode, outcome.ExitCode);
        Assert.Equal(3, outcome.ExitCode); // named constant == 3 (machine-only ayrım korunur)
    }
}
