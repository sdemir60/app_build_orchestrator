using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using BuildOrchestrator.Contracts;
using Brush = System.Windows.Media.Brush;

namespace BuildOrchestrator.App.Converters;

/// <summary>Maps a <see cref="ProjectStatus"/> to its card accent brush (Section 7 status colors).</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var color = value is ProjectStatus s ? s switch
        {
            ProjectStatus.Discovered => Color.FromRgb(0x5A, 0x5A, 0x5A),
            ProjectStatus.Queued => Color.FromRgb(0x6E, 0x7B, 0x8B),
            ProjectStatus.Building => Color.FromRgb(0x29, 0x9B, 0xF0),
            ProjectStatus.Succeeded => Color.FromRgb(0x35, 0xC7, 0x59),
            ProjectStatus.Failed => Color.FromRgb(0xE5, 0x3E, 0x3E),
            ProjectStatus.Skipped => Color.FromRgb(0x44, 0x44, 0x44),
            ProjectStatus.CycleDetected => Color.FromRgb(0xE0, 0x9B, 0x16),
            _ => Color.FromRgb(0x5A, 0x5A, 0x5A)
        } : Color.FromRgb(0x5A, 0x5A, 0x5A);

        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Bool → Visibility (true = Visible).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>Inverts a boolean.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;
}

/// <summary>True when the bound error flag colors a console line red.</summary>
public sealed class ErrorToBrushConverter : IValueConverter
{
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
    private static readonly Brush NormalBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? ErrorBrush : NormalBrush;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
