using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [D6] design-v1'in popover/menü SATIR hover'ı (BranchRow/WtRow/BuildMenuItem AYNI: <c>transition: background
/// var(--duration-fast) var(--ease-standard)</c>, hover'da <c>surface-raised</c>, aksi halde <c>transparent</c>).
/// Üç tüketici de ORTAK bu yardımcıyı çağırır (kopya YASAK, CLAUDE.md). Renk geçişi <see cref="MotionTokens.TransitionColor"/>
/// ile bir ŞABLON-LOKAL (donmamış) fırça üzerinde akar — süre/eğri + AnimationsEnabled BAŞLATMA ANINDA taze
/// okunur (A13.2: paylaşılan token fırçası animate edilemez).
/// </summary>
internal static class HoverBackground
{
    public static void Attach(Border row)
    {
        var brush = new SolidColorBrush(Colors.Transparent); // per-satır lokal fırça (A13.2)
        row.Background = brush;
        row.MouseEnter += (_, _) => TransitionTo(row, brush, "Brush.SurfaceRaised");
        row.MouseLeave += (_, _) => TransitionTo(row, brush, null);
    }

    private static void TransitionTo(FrameworkElement host, SolidColorBrush brush, string? brushKey)
    {
        Color to = brushKey is not null && host.TryFindResource(brushKey) is SolidColorBrush b ? b.Color : Colors.Transparent;
        MotionTokens.TransitionColor(host, brush, to);
    }
}
