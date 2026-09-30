using System.Windows;
using System.Windows.Automation.Peers;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// Canlı bölge duyurusunun TEK yeri (<c>aria-live</c> karşılığı): öğenin UIA peer'ini bulur, yoksa kurar ve
/// <see cref="AutomationEvents.LiveRegionChanged"/> yükseltir — ekran okuyucu bölgenin yeni metnini okur. Bölgenin
/// ne kadar ısrarlı okunacağı öğenin kendi <c>AutomationProperties.LiveSetting</c>'indedir.
///
/// <para><b>NE ZAMAN</b> duyurulacağı çağıranın kararıdır (şerit faz değişince, keşif sayacı sayı değişince,
/// restart ekranı adım değişince); bu sınıf yalnız <b>NASIL</b>'ı taşır. <c>LiveRegionTests</c> olayın başka bir
/// dosyada yükseltilmesini çitler.</para>
///
/// <para>Peer kurulamazsa (henüz realize olmamış bir öğe) sessizce atlanır; dinleyici yoksa yükseltmek güvenlidir
/// (WPF'te no-op).</para>
/// </summary>
internal static class LiveRegion
{
    /// <summary>Öğenin canlı bölgesini ekran okuyucuya duyurur.</summary>
    internal static void Announce(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var peer = UIElementAutomationPeer.FromElement(element) ?? UIElementAutomationPeer.CreatePeerForElement(element);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
