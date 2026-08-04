using System.Windows;
using System.Windows.Input;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Modal odak tuzağı iddiası — İKİ diyalog (Settings, About) tarafından paylaşılır. Gövde önce
/// <see cref="SettingsDialogFocusTests"/>'in içindeydi; About ikinci bir kopyasını yazacaktı
/// (kopya YASAK, CLAUDE.md).
/// </summary>
internal static class FocusTrap
{
    /// <summary>
    /// Diyalog alt-ağacından başlayarak tekrar tekrar "Sonraki" gezinme (Tab'ın WPF içindeki gerçek
    /// mekanizması — <see cref="FrameworkElement.MoveFocus"/>) yapar: kontrol sayısından FAZLA turda ne odak
    /// arka plan kontrolüne kaçar ne de alt-ağacın dışına çıkar (Cycle sarar).
    /// </summary>
    public static void AssertCannotEscape(FrameworkElement dialogRoot, DependencyObject backgroundControl)
    {
        ArgumentNullException.ThrowIfNull(dialogRoot);
        Assert.True(dialogRoot.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)),
            "diyalog alt-ağacında odaklanabilir hiçbir kontrol bulunamadı");

        for (int i = 0; i < 25; i++) // kontrol sayısından kesinlikle fazla — Cycle sarmalıyor, kaçmıyor
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            Assert.NotNull(focused);
            Assert.NotSame(backgroundControl, focused);
            // Görsel+MANTIKSAL kip BİLEREK: odaklanan öğe bir Popup/ContentElement altındaysa görsel zincir
            // kopar, mantıksal zincir devam eder.
            Assert.True(DsResources.IsSelfOrDescendantOf(focused!, dialogRoot, includeLogical: true),
                "odak diyalog alt-ağacının DIŞINA çıktı");
            (focused as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }
    }
}
