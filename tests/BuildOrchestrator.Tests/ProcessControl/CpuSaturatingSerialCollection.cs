using Xunit;

namespace BuildOrchestrator.Tests.ProcessControl;

/// <summary>
/// [T20-a / P1 review KÖK 1] CPU'yu DOYURAN testler için serileştirilmiş xUnit collection'ı
/// (<c>ConsoleUiSerialCollection</c> ve <c>BuildStateStoreSerialCollection</c> ile aynı desen).
///
/// <para><b>Neden:</b> <see cref="JobCpuRateTests.Cpu_hard_cap_holds_under_a_saturating_child"/> yaklaşık 6 saniye
/// boyunca <c>ProcessorCount + 2</c> busy-loop process ile makineyi bilerek doyurur. Paralel grupta koşarsa
/// iki yönlü zarar verir: (a) DIŞA — aynı anda koşan §3 kaskat/IOCP testlerini ve zaman duyarlı WPF
/// testlerini aç bırakır; (b) İÇE — harici yük altında capsiz ölçüm doyuma ulaşamaz ve testin kendi
/// ayırt-edicilik guard'ı düşer. <c>DisableParallelization</c> bu collection'ı başka hiçbir collection ile
/// eşzamanlı koşmayacak şekilde ayırır; production kodu DEĞİŞMEZ.</para>
///
/// <para>⚠️ Bu, makinedeki <b>harici</b> yüke (başka bir build/agent) karşı koruma DEĞİLDİR — o durum için
/// perf testi kendi içinde bounded-retry + açık <c>Skip</c> taşır (bkz. testin gövdesi).</para>
/// </summary>
[CollectionDefinition("CPU saturating (serial)", DisableParallelization = true)]
public class CpuSaturatingSerialCollection
{
}
