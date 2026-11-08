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
    private const string RaisedKey = "Brush.SurfaceRaised";

    public static void Attach(Border row) => Attach(row, Colors.Transparent, restKey: null);

    /// <summary>[branch keys] Klavyeyle işaretli satır (<c>BranchPopover</c>'ın ↓/↑ işareti): zemini dururken de hover'ınkidir
    /// (<c>surface-raised</c>) ve fare çıkınca saydama değil oraya döner — işaret fareyle silinmez. Renk <paramref name="resources"/>'tan, yani
    /// ağaçta olan bir atadan okunur: satırın kendisi henüz ağaca girmemiştir.</summary>
    public static void AttachRaised(Border row, FrameworkElement resources) =>
        Attach(row, resources.TryFindResource(RaisedKey) is SolidColorBrush b ? b.Color : Colors.Transparent, RaisedKey);

    private static void Attach(Border row, Color rest, string? restKey)
    {
        var brush = new SolidColorBrush(rest); // per-satır lokal fırça (A13.2)
        row.Background = brush;
        row.MouseEnter += (_, _) => TransitionTo(row, brush, RaisedKey);
        row.MouseLeave += (_, _) => TransitionTo(row, brush, restKey);
    }

    private static void TransitionTo(FrameworkElement host, SolidColorBrush brush, string? brushKey)
    {
        Color to = brushKey is not null && host.TryFindResource(brushKey) is SolidColorBrush b ? b.Color : Colors.Transparent;
        MotionTokens.TransitionColor(host, brush, to);
    }
}
