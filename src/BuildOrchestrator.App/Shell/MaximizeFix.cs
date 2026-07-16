using System.Windows;

namespace BuildOrchestrator.App.Shell;

public static class MaximizeFix
{
    /// dotnet/wpf#3887: WindowChrome + maximize'da içerik resize-border kadar ekran dışına taşar. [A13 ZORUNLU]
    public static Thickness PaddingFor(WindowState state, double frameWidthPx, double frameHeightPx, double paddedBorderPx, double dpiScale)
    {
        if (state != WindowState.Maximized) return new Thickness(0);
        double x = (frameWidthPx + paddedBorderPx) / dpiScale;
        double y = (frameHeightPx + paddedBorderPx) / dpiScale;
        return new Thickness(x, y, x, y);
    }
}
