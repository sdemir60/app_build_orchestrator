namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T56/3b] WPF konsol UI StaFact testleri (ConsoleView/ConsoleHeader kurup AvalonEdit + gömülü font +
/// token kaynağı yükleyen [StaFact]'ler) için serileştirilmiş xUnit collection'ı. <c>DisableParallelization</c>:
/// bu collection HİÇBİR başka collection ile eşzamanlı koşmaz.
///
/// <para><b>Neden:</b> her [StaFact] kendi STA thread'inde WPF kontrol/kaynak (pack-URI font, XAML resource)
/// yükler; tam suite'in yoğun paralel yükü altında STA/kaynak çekişmesi bu testleri ara sıra (izole/rerun'da
/// geçen — timing flake) düşürüyordu (bkz. flake-serialization-report.md deseni; BuildStateStore ile aynı çözüm).
/// Serileştirme çekişmeyi kaldırır; production kodu DEĞİŞMEZ.</para>
/// </summary>
[CollectionDefinition("Console UI (serial)", DisableParallelization = true)]
public class ConsoleUiSerialCollection
{
}
