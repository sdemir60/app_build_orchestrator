using System.Windows;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T59] WPF'in gerçek zamanlı animasyon clock'u (compositor tick) yalnızca bir <see cref="Visual"/> gerçek bir
/// <see cref="PresentationSource"/>'a (HWND) bağlıyken çalışır — doğrulandı (ZDiag denemeleri): bağlı olmayan
/// (yalnız Measure/Arrange edilmiş) bir kontrolde <c>BeginAnimation</c>/<c>ApplyAnimationClock</c> HİÇBİR etki
/// üretmez (DependencyPropertyHelper.IsAnimated hep false, değer hiç değişmez — <c>ClockController.Seek</c> bile).
/// Bu yardımcı, ScrollAnimator StaFact testlerinin gerçek bir (ekran dışı, görünmez) <see cref="Window"/> içinde
/// canlı bir animasyon clock'u gözlemleyebilmesi için TEK ORTAK kurulumdur (kopya YASAK).
/// </summary>
internal static class AnimationHost
{
    /// <summary>Verilen içeriği ekran dışı, görünmez bir Window'da gösterir ve HWND/layout yerleşene kadar KISA
    /// bir süre dispatcher'ı pompalar. Dönen Window canlı tutulmalı (çağıranın metodu bitene kadar scope'ta kalır) —
    /// GC'ye bırakmak güvenlidir (test metodu boyunca köklenir), açıkça Close() gerekmez.</summary>
    public static Window ShowOffscreen(UIElement content, double width = 200, double height = 400)
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -5000,
            Top = -5000,
            Width = width,
            Height = height,
            Content = content,
        };
        window.Show();
        DispatcherPump.PumpUntil(() => false, TimeSpan.FromMilliseconds(50)); // HWND/composition target yerleşsin
        return window;
    }
}
