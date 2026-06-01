using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts;

namespace BuildOrchestrator.App.Views;

/// <summary>Maps a <see cref="ProjectStatus"/> to its accent brush (Section 7 colours).</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var status = value is ProjectStatus s ? s : ProjectStatus.Discovered;
        var color = status switch
        {
            ProjectStatus.Discovered => Color.FromRgb(0x6B, 0x72, 0x80),
            ProjectStatus.Queued => Color.FromRgb(0x94, 0xA3, 0xB8),
            ProjectStatus.Building => Color.FromRgb(0x3B, 0x82, 0xF6),
            ProjectStatus.Succeeded => Color.FromRgb(0x22, 0xC5, 0x5E),
            ProjectStatus.Failed => Color.FromRgb(0xEF, 0x44, 0x44),
            ProjectStatus.Skipped => Color.FromRgb(0x52, 0x5B, 0x6B),
            ProjectStatus.CycleDetected => Color.FromRgb(0xF5, 0x9E, 0x0B),
            _ => Color.FromRgb(0x6B, 0x72, 0x80)
        };
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>True (error) -> red, false -> light gray console text.</summary>
public sealed class ErrorToBrushConverter : IValueConverter
{
    private static readonly Brush Error = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
    private static readonly Brush Normal = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Error : Normal;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Bool -> Visibility (true = Visible). Pass "Invert" to flip.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is true;
        if (parameter as string == "Invert")
            b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>Inverts a boolean.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;
}

/// <summary>Maps a console line to its text colour: error red, success green, header blue, else gray.</summary>
public sealed class ConsoleLineBrushConverter : IValueConverter
{
    private static readonly Brush Error = Freeze(Color.FromRgb(0xFF, 0x6B, 0x6B));
    private static readonly Brush Success = Freeze(Color.FromRgb(0x4A, 0xDE, 0x80));
    private static readonly Brush Header = Freeze(Color.FromRgb(0x93, 0xC5, 0xFD));
    private static readonly Brush Normal = Freeze(Color.FromRgb(0xC8, 0xCC, 0xD2));

    private static Brush Freeze(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ConsoleLine line)
            return Normal;
        if (line.IsError)
            return Error;
        if (line.IsSuccess)
            return Success;
        if (line.IsHeader)
            return Header;
        return Normal;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
